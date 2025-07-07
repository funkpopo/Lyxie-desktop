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

            Debug.WriteLine($"开始启动MCP服务器: {name}");

            // 更新服务器定义
            _serverDefinitions.AddOrUpdate(name, definition, (key, oldValue) => definition);

            // HTTP服务器不需要启动进程
            if (definition.IsHttpServer)
            {
                Debug.WriteLine($"服务器 {name} 是HTTP服务器，直接标记为运行中");
                definition.IsRunning = true;
                return true;
            }

            // 检查是否已经在运行
            if (IsServerRunning(name))
            {
                Debug.WriteLine($"服务器 {name} 已经在运行中");
                definition.IsRunning = true;
                return true;
            }

            try
            {
                Debug.WriteLine($"准备启动服务器 {name}，命令: {definition.Command}");

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

                Debug.WriteLine($"完整命令: {shellExecutable} {shellArgsPrefix}{fullCommand}");

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

                Debug.WriteLine($"尝试启动进程...");
                if (process.Start())
                {
                    Debug.WriteLine($"进程启动成功，PID: {process.Id}");

                    // 立即更新状态信息
                    definition.IsRunning = true;
                    definition.ProcessId = process.Id;
                    definition.ErrorMessage = null; // 清除之前的错误信息

                    // 添加到进程字典
                    bool addedToDict = _serverProcesses.TryAdd(name, process);
                    Debug.WriteLine($"进程添加到字典: {addedToDict}");

                    // 在Windows上，将进程添加到Job Object
                    try
                    {
                        _jobObjectManager?.AddProcess(process);
                        Debug.WriteLine($"进程已添加到Job Object");
                    }
                    catch (Exception jobEx)
                    {
                        Debug.WriteLine($"添加到Job Object失败: {jobEx.Message}");
                    }

                    // 开始异步读取输出
                    try
                    {
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        Debug.WriteLine($"开始异步读取进程输出");
                    }
                    catch (Exception ioEx)
                    {
                        Debug.WriteLine($"开始读取输出失败: {ioEx.Message}");
                    }

                    Debug.WriteLine($"等待500ms确认进程稳定...");
                    await Task.Delay(500, cancellationToken);

                    // 再次检查进程状态
                    try
                    {
                        if (!process.HasExited)
                        {
                            Debug.WriteLine($"=== 服务器 {name} 启动成功并稳定运行，PID: {process.Id} ===");

                            // 最终确认状态同步
                            if (!definition.IsRunning)
                            {
                                definition.IsRunning = true;
                                Debug.WriteLine($"最终状态同步: 确保服务器 {name} 标记为运行中");
                            }

                            return true;
                        }
                        else
                        {
                            Debug.WriteLine($"服务器 {name} 进程在启动后退出，ExitCode: {process.ExitCode}");
                            OnProcessExited(name, process);
                            return false;
                        }
                    }
                    catch (Exception checkEx)
                    {
                        Debug.WriteLine($"检查进程状态时出错: {checkEx.Message}");
                        // 假设进程仍在运行
                        Debug.WriteLine($"假设服务器 {name} 仍在运行");
                        return true;
                    }
                }
                else
                {
                    Debug.WriteLine($"进程启动失败 - Process.Start() 返回false");
                    definition.ErrorMessage = "进程启动失败";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动服务器 {name} 时发生异常: {ex.Message}");
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
            {
                Debug.WriteLine($"服务器名称为空，返回false");
                return false;
            }

            Debug.WriteLine($"=== 开始检查服务器 {name} 是否运行中 ===");

            if (!_serverDefinitions.TryGetValue(name, out var definition))
            {
                Debug.WriteLine($"服务器 {name} 的定义未找到");
                return false;
            }

            Debug.WriteLine($"服务器 {name} 定义状态: IsRunning={definition.IsRunning}, ProcessId={definition.ProcessId}");

            // HTTP服务器特殊处理
            if (definition.IsHttpServer)
            {
                Debug.WriteLine($"服务器 {name} 是HTTP服务器，状态: {definition.IsRunning}");
                return definition.IsRunning;
            }

            // 方法1：检查我们保存的进程引用
            if (_serverProcesses.TryGetValue(name, out var process))
            {
                try
                {
                    bool isRunning = !process.HasExited;
                    Debug.WriteLine($"方法1 - 进程引用检查: 服务器 {name} 进程状态={isRunning}, PID={process.Id}");

                    if (isRunning)
                    {
                        // 确保定义状态与实际状态同步
                        if (!definition.IsRunning)
                        {
                            Debug.WriteLine($"同步状态: 更新服务器 {name} 的定义状态为运行中");
                            definition.IsRunning = true;
                            definition.ProcessId = process.Id;
                        }
                        Debug.WriteLine($"=== 服务器 {name} 通过进程引用确认运行中 ===");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine($"进程引用显示已退出，清理进程引用");
                        _serverProcesses.TryRemove(name, out _);
                        definition.IsRunning = false;
                        definition.ProcessId = null;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"检查进程引用出错: {ex.Message}，清理无效引用");
                    _serverProcesses.TryRemove(name, out _);
                    // 继续后续检查
                }
            }
            else
            {
                Debug.WriteLine($"方法1 - 服务器 {name} 无进程引用记录");
            }

            // 方法2：通过记录的进程ID检查
            if (definition.ProcessId.HasValue && definition.ProcessId > 0)
            {
                try
                {
                    var pid = definition.ProcessId.Value;
                    Debug.WriteLine($"方法2 - 尝试通过PID {pid} 检查服务器 {name}");

                    var pidProcess = Process.GetProcessById(pid);
                    Debug.WriteLine($"方法2 - 服务器 {name} 的PID {pid} 存在，进程名: {pidProcess.ProcessName}");

                    // 更新进程引用
                    if (!_serverProcesses.ContainsKey(name))
                    {
                        _serverProcesses[name] = pidProcess;
                        Debug.WriteLine($"恢复进程引用: 已更新服务器 {name} 的进程引用");
                    }

                    // 更新状态
                    if (!definition.IsRunning)
                    {
                        definition.IsRunning = true;
                        Debug.WriteLine($"同步状态: 更新服务器 {name} 的定义状态为运行中");
                    }

                    Debug.WriteLine($"=== 服务器 {name} 通过PID确认运行中 ===");
                    return true;
                }
                catch (ArgumentException)
                {
                    Debug.WriteLine($"方法2 - 服务器 {name} 的PID {definition.ProcessId} 不存在，清理状态");
                    // 进程不存在，清除无效的进程ID
                    definition.ProcessId = null;
                    definition.IsRunning = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"方法2 - 通过PID检查出错: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine($"方法2 - 服务器 {name} 无有效PID记录");
            }

            // 如果到这里还未确认运行，则服务未运行
            Debug.WriteLine($"=== 服务器 {name} 所有检查方法均未确认运行，标记为未运行 ===");
            definition.IsRunning = false;
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
                if (process == null)
                {
                    Debug.WriteLine($"OnProcessExited: 进程对象为null，服务器: {name}");
                    return;
                }

                Debug.WriteLine($"=== 进程退出事件: 服务器 {name}, PID: {process.Id}, ExitCode: {process.ExitCode} ===");

                // 清理进程引用和缓冲区
                bool removedFromDict = _serverProcesses.TryRemove(name, out _);
                bool removedBuffer = _responseBuffers.TryRemove(name, out _);

                Debug.WriteLine($"清理结果: 进程字典={removedFromDict}, 缓冲区={removedBuffer}");

                if (_serverDefinitions.TryGetValue(name, out var definition))
                {
                    Debug.WriteLine($"当前定义状态: IsRunning={definition.IsRunning}, ProcessId={definition.ProcessId}");

                    // 仅当退出的进程ID与记录的ID匹配时才更新状态
                    if (definition.ProcessId.HasValue && definition.ProcessId == process.Id)
                    {
                        Debug.WriteLine($"进程ID匹配，更新服务器 {name} 状态为未运行");
                        definition.IsRunning = false;
                        definition.ProcessId = null;
                        
                        // 如果 stderr 中没有更具体的错误信息，则使用通用的退出代码消息
                        if (string.IsNullOrEmpty(definition.ErrorMessage))
                        {
                            definition.ErrorMessage = $"进程退出，退出码: {process.ExitCode}";
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"进程ID不匹配，不更新状态。定义PID: {definition.ProcessId}, 退出PID: {process.Id}");
                    }
                }
                else
                {
                    Debug.WriteLine($"未找到服务器 {name} 的定义");
                }

                try
                {
                    process.Dispose();
                    Debug.WriteLine($"进程对象已释放");
                }
                catch (Exception disposeEx)
                {
                    Debug.WriteLine($"释放进程对象时出错: {disposeEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"处理进程退出事件时出错: {ex.Message}");
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
                
                if (_serverDefinitions.TryGetValue(serverName, out var definition))
                {
                    // 捕获第一个非空的错误信息作为主要错误原因
                    if (string.IsNullOrEmpty(definition.ErrorMessage))
                    {
                        definition.ErrorMessage = data;
                    }
                }
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