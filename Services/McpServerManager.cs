using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using Lyxie_desktop.Helpers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// MCP服务器管理器实现
    /// </summary>
    public class McpServerManager : IMcpServerManager, IDisposable
    {
        private readonly ConcurrentDictionary<string, McpServerDefinition> _serverDefinitions;
        private readonly ConcurrentDictionary<string, Process> _serverProcesses;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests;
        private readonly ConcurrentDictionary<string, StringBuilder> _responseBuffers;
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        private readonly JobObjectManager? _jobObjectManager;

        public McpServerManager()
        {
            _serverDefinitions = new ConcurrentDictionary<string, McpServerDefinition>();
            _serverProcesses = new ConcurrentDictionary<string, Process>();
            _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<string>>();
            _responseBuffers = new ConcurrentDictionary<string, StringBuilder>();
            
            if (OperatingSystem.IsWindows())
            {
                _jobObjectManager = new JobObjectManager();
            }
        }

        /// <summary>
        /// 启动指定的MCP服务器
        /// </summary>
        public async Task<bool> StartServerAsync(string name, McpServerDefinition definition, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name) || definition == null)
                return false;

            // 更新服务器定义
            _serverDefinitions.AddOrUpdate(name, definition, (key, oldValue) => definition);

            // HTTP服务器不需要启动进程
            if (definition.IsHttpServer)
            {
                definition.IsRunning = true;
                return true;
            }

            // 检查是否已经在运行
            if (IsServerRunning(name))
            {
                definition.IsRunning = true;
                return true;
            }

            try
            {
                // 在Windows上，我们使用cmd.exe来承载命令，以便可以将其添加到Job Object中
                // 这样可以确保即使是像npx这样的批处理文件也能正确执行
                string shellExecutable = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh"; // 为非Windows系统提供一个备用
                string shellArgsPrefix = OperatingSystem.IsWindows() ? "/c " : "-c ";
                
                // 构建完整命令
                string fullCommand = definition.Command!;
                
                // 添加参数
                if (definition.Args != null && definition.Args.Count > 0)
                {
                    // 对参数进行适当的引号处理
                    var processedArgs = definition.Args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg);
                    fullCommand += " " + string.Join(" ", processedArgs);
                }
                
                // 构建ProcessStartInfo
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = shellExecutable,
                    Arguments = shellArgsPrefix + fullCommand,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                var process = new Process { StartInfo = processStartInfo };
                
                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) => OnProcessExited(name, (Process)sender!);

                // 初始化响应缓冲区
                _responseBuffers.TryAdd(name, new StringBuilder());

                // 异步读取输出
                process.OutputDataReceived += (sender, args) => HandleOutput(name, args.Data);
                process.ErrorDataReceived += (sender, args) => HandleError(name, args.Data);
                
                if (process.Start())
                {
                    // 添加到进程字典
                    _serverProcesses.TryAdd(name, process);
                    
                    // 在Windows上，将进程添加到Job Object
                    _jobObjectManager?.AddProcess(process);

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    
                    definition.IsRunning = true;
                    definition.ProcessId = process.Id;
                    
                    await Task.Delay(500, cancellationToken);
                    
                    if (!process.HasExited)
                    {
                        return true;
                    }
                    else
                    {
                        OnProcessExited(name, process);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                definition.IsRunning = false;
                definition.ProcessId = null;
                definition.ErrorMessage = $"启动失败: {ex.Message}";
                return false;
            }

            return false;
        }

        /// <summary>
        /// 向指定的stdio服务器发送请求并异步读取响应
        /// </summary>
        public async Task<string?> SendRequestAndReadResponseAsync(string name, string request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(request))
                return null;

            if (!IsServerRunning(name))
                return null;

            // 清除之前可能存在的响应缓冲区
            _responseBuffers.AddOrUpdate(name, new StringBuilder(), (key, oldValue) => new StringBuilder());

            // 获取服务器进程
            if (!_serverProcesses.TryGetValue(name, out var process) || process.HasExited)
                return null;

            // 从请求中提取请求ID，用于匹配响应
            string requestId = ExtractRequestId(request);
            if (string.IsNullOrEmpty(requestId))
            {
                Debug.WriteLine($"无法从请求中提取ID: {request}");
                return null;
            }

            // 创建完成源，用于等待响应
            var completionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests.TryAdd(requestId, completionSource);

            try
            {
                // 请求添加换行符以便服务器处理
                request += "\n";
                
                // 发送请求到进程的标准输入
                await process.StandardInput.WriteAsync(request);
                await process.StandardInput.FlushAsync();

                // 等待响应，带超时
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10秒超时
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                
                // 注册取消回调
                using var registration = linkedCts.Token.Register(() => 
                {
                    if (!completionSource.Task.IsCompleted)
                    {
                        _pendingRequests.TryRemove(requestId, out _);
                        completionSource.TrySetCanceled();
                    }
                });

                // 等待响应完成
                var responseTask = completionSource.Task;
                
                try
                {
                    var response = await responseTask;
                    _pendingRequests.TryRemove(requestId, out _);
                    return response;
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"请求 {requestId} 超时或被取消");
                    _pendingRequests.TryRemove(requestId, out _);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"发送请求异常: {ex.Message}");
                _pendingRequests.TryRemove(requestId, out _);
                return null;
            }
        }

        /// <summary>
        /// 从请求JSON中提取请求ID
        /// </summary>
        private string ExtractRequestId(string request)
        {
            try
            {
                var jObject = JObject.Parse(request);
                return jObject["id"]?.ToString() ?? Guid.NewGuid().ToString();
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// 从响应中提取请求ID并处理响应
        /// </summary>
        private void ProcessJsonResponse(string response)
        {
            try
            {
                var jObject = JObject.Parse(response);
                var id = jObject["id"]?.ToString();

                if (!string.IsNullOrEmpty(id) && _pendingRequests.TryGetValue(id, out var completionSource))
                {
                    completionSource.TrySetResult(response);
                }
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Debug.WriteLine($"解析JSON响应失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止指定的MCP服务器
        /// </summary>
        public Task<bool> StopServerAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
                return Task.FromResult(false);

            if (!_serverDefinitions.TryGetValue(name, out var definition))
            {
                return Task.FromResult(false);
            }
            
            // HTTP服务器不需要停止进程
            if (definition.IsHttpServer)
            {
                definition.IsRunning = false;
                return Task.FromResult(true);
            }

            bool stopped = false;
            
            // 首先尝试使用我们保存的进程引用
            if (_serverProcesses.TryRemove(name, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        stopped = true;
                    }
                    else
                    {
                        stopped = true; // 进程已退出
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"停止进程时出错: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
            
            // 如果上述方法失败，尝试使用PID
            if (!stopped && definition.ProcessId.HasValue && definition.ProcessId > 0)
            {
                try
                {
                    var pidProcess = Process.GetProcessById((int)definition.ProcessId.Value);
                    // 不再检查进程名，直接终止
                    pidProcess.Kill();
                    stopped = true;
                }
                catch (ArgumentException)
                {
                    // 进程已不存在
                    stopped = true; 
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"通过PID {definition.ProcessId} 停止进程时出错: {ex.Message}");
                }
            }
            
            // 最后尝试通过进程名关闭
            if (!stopped && !string.IsNullOrEmpty(definition.Command))
            {
                try
                {
                    var processName = System.IO.Path.GetFileNameWithoutExtension(definition.Command);
                    var processes = Process.GetProcessesByName(processName);
                    
                    if (processes.Length > 0)
                    {
                        foreach (var p in processes)
                        {
                            try
                            {
                                p.Kill();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"强制关闭进程 {p.ProcessName} (ID: {p.Id}) 时出错: {ex.Message}");
                            }
                            finally
                            {
                                p.Dispose();
                            }
                        }
                        stopped = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"查找并关闭外部进程 {definition.Command} 时出错: {ex.Message}");
                }
            }

            // 清理相关资源
            _responseBuffers.TryRemove(name, out _);

            // 更新状态
            definition.IsRunning = false;
            definition.ProcessId = null;
            
            // 如果进程已经不在运行，也算作成功
            if (definition.Command != null && !IsExternalProcessRunning(definition.Command, definition.Args))
            {
                stopped = true;
            }

            return Task.FromResult(stopped);
        }

        /// <summary>
        /// 检查服务器是否正在运行
        /// </summary>
        public bool IsServerRunning(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            Debug.WriteLine($"检查服务器 {name} 是否运行中...");

            if (!_serverDefinitions.TryGetValue(name, out var definition))
            {
                Debug.WriteLine($"服务器 {name} 的定义未找到");
                return false;
            }

            // HTTP服务器特殊处理
            if (definition.IsHttpServer)
            {
                Debug.WriteLine($"服务器 {name} 是HTTP服务器，状态: {definition.IsRunning}");
                return definition.IsRunning;
            }
            
            // 首先检查我们保存的进程引用
            if (_serverProcesses.TryGetValue(name, out var process))
            {
                try
                {
                    bool isRunning = !process.HasExited;
                    Debug.WriteLine($"服务器 {name} 的进程引用检查结果: {isRunning}");
                    if (isRunning)
                    {
                        // 确保定义状态与实际状态同步
                        if (!definition.IsRunning)
                        {
                            Debug.WriteLine($"更新服务器 {name} 的状态为运行中");
                            definition.IsRunning = true;
                            definition.ProcessId = process.Id;
                        }
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"检查进程引用出错: {ex.Message}");
                    // 进程可能已无效，继续后续检查
                }
            }
            else
            {
                Debug.WriteLine($"服务器 {name} 无进程引用");
            }
            
            // 检查是否有记录的进程ID并且该进程仍在运行
            if (definition.ProcessId.HasValue && definition.ProcessId > 0)
            {
                try
                {
                    var pid = definition.ProcessId.Value;
                    Debug.WriteLine($"尝试通过PID {pid} 检查服务器 {name}");
                    
                    var pidProcess = Process.GetProcessById((int)pid);
                    // 进程存在，视为运行中
                    Debug.WriteLine($"服务器 {name} 的PID {pid} 存在，确认运行中");
                    
                    // 更新进程引用
                    if (!_serverProcesses.ContainsKey(name))
                    {
                        _serverProcesses[name] = pidProcess;
                        Debug.WriteLine($"已更新服务器 {name} 的进程引用");
                    }
                    
                    // 更新状态
                    if (!definition.IsRunning)
                    {
                        definition.IsRunning = true;
                        Debug.WriteLine($"更新服务器 {name} 的状态为运行中");
                    }
                    
                    return true;
                }
                catch (ArgumentException)
                {
                    Debug.WriteLine($"服务器 {name} 的PID {definition.ProcessId} 不存在");
                    // 进程不存在，清除无效的进程ID
                    definition.ProcessId = null;
                    definition.IsRunning = false;
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"通过PID检查出错: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine($"服务器 {name} 无有效PID记录");
            }

            // 特殊处理：如果是文件系统服务器，尝试通过进程名查找
            if (name.Equals("filesystem", StringComparison.OrdinalIgnoreCase) ||
                Helpers.FileSystemToolAdapter.IsFileSystemServer(name))
            {
                if (!string.IsNullOrEmpty(definition.Command))
                {
                    Debug.WriteLine($"尝试通过命令名检查文件系统服务器: {definition.Command}");
                    bool isExternalRunning = IsExternalProcessRunning(definition.Command, definition.Args);
                    
                    if (isExternalRunning)
                    {
                        Debug.WriteLine($"发现外部运行的文件系统服务进程");
                        definition.IsRunning = true;
                        return true;
                    }
                }
            }

            // 如果到这里还未确认运行，则服务未运行
            Debug.WriteLine($"服务器 {name} 确认未运行");
            return false;
        }

        /// <summary>
        /// 检查是否有外部启动的进程
        /// </summary>
        private bool IsExternalProcessRunning(string command, List<string>? args)
        {
            try
            {
                var processName = System.IO.Path.GetFileNameWithoutExtension(command);
                var processes = Process.GetProcessesByName(processName);
                
                foreach (var process in processes)
                {
                    try
                    {
                        // 基本的进程名匹配
                        if (process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                        {
                            // 如果有参数，尝试匹配命令行（Windows限制，可能无法获取）
                            // 这里只做基础检查，避免误判
                            process.Dispose();
                            return true;
                        }
                        process.Dispose();
                    }
                    catch
                    {
                        // 无法访问进程信息，跳过
                        process.Dispose();
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取正在运行的服务器列表
        /// </summary>
        public IEnumerable<string> GetRunningServers()
        {
            var runningServers = new List<string>();

            foreach (var kvp in _serverDefinitions)
            {
                if (IsServerRunning(kvp.Key))
                {
                    runningServers.Add(kvp.Key);
                }
            }

            return runningServers;
        }

        /// <summary>
        /// 启动所有已启用的MCP服务器
        /// </summary>
        public async Task<Dictionary<string, bool>> StartAllServersAsync(Dictionary<string, McpServerDefinition> servers, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, bool>();
            var tasks = new List<Task>();

            foreach (var server in servers)
            {
                if (server.Value.IsEnabled)
                {
                    var task = Task.Run(async () =>
                    {
                        var result = await StartServerAsync(server.Key, server.Value, cancellationToken);
                        lock (results)
                        {
                            results[server.Key] = result;
                        }
                    }, cancellationToken);
                    
                    tasks.Add(task);
                }
            }

            await Task.WhenAll(tasks);
            return results;
        }

        /// <summary>
        /// 停止所有正在运行的MCP服务器
        /// </summary>
        public async Task<Dictionary<string, bool>> StopAllServersAsync(CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, bool>();
            var runningServers = GetRunningServers().ToList();
            var tasks = new List<Task>();

            foreach (var serverName in runningServers)
            {
                var task = Task.Run(async () =>
                {
                    var result = await StopServerAsync(serverName, cancellationToken);
                    lock (results)
                    {
                        results[serverName] = result;
                    }
                }, cancellationToken);
                
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return results;
        }

        /// <summary>
        /// 处理进程退出事件
        /// </summary>
        private void OnProcessExited(string name, Process process)
        {
            try
            {
                if (process == null) return;

                _serverProcesses.TryRemove(name, out _);
                _responseBuffers.TryRemove(name, out _);

                if (_serverDefinitions.TryGetValue(name, out var definition))
                {
                    // 仅当退出的进程ID与记录的ID匹配时才更新状态
                    if (definition.ProcessId.HasValue && definition.ProcessId == process.Id)
                    {
                        definition.IsRunning = false;
                        definition.ProcessId = null;
                    }
                }

                process.Dispose();
            }
            catch
            {
                // 忽略清理过程中的异常
            }
        }

        /// <summary>
        /// 处理标准输出
        /// </summary>
        private void HandleOutput(string serverName, string? data)
        {
            if (data != null)
            {
                Debug.WriteLine($"[MCP-{serverName}-STDOUT] {data}");
                
                // 尝试解析为JSON响应
                if (data.TrimStart().StartsWith("{") && data.TrimEnd().EndsWith("}"))
                {
                    ProcessJsonResponse(data);
                }
                else
                {
                    // 如果不是完整的JSON，尝试缓冲并处理
                    if (_responseBuffers.TryGetValue(serverName, out var buffer))
                    {
                        buffer.AppendLine(data);
                        
                        // 检查是否已形成完整的JSON响应
                        var bufferContent = buffer.ToString();
                        if (bufferContent.TrimStart().StartsWith("{") && bufferContent.TrimEnd().EndsWith("}"))
                        {
                            ProcessJsonResponse(bufferContent);
                            buffer.Clear();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 处理标准错误
        /// </summary>
        private void HandleError(string serverName, string? data)
        {
            if (data != null)
            {
                Debug.WriteLine($"[MCP-{serverName}-STDERR] {data}");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                // 终止所有未完成的请求
                foreach (var request in _pendingRequests)
                {
                    request.Value.TrySetCanceled();
                }
                _pendingRequests.Clear();
                
                // 停止并清理所有进程
                var serverNames = _serverDefinitions.Keys.ToList();
                foreach (var name in serverNames)
                {
                    StopServerAsync(name).Wait(TimeSpan.FromSeconds(2));
                }
                
                _serverDefinitions.Clear();
                _serverProcesses.Clear();
                _responseBuffers.Clear();
                
                _jobObjectManager?.Dispose();
            }
            catch
            {
                // 忽略释放过程中的异常
            }
        }
    }
}