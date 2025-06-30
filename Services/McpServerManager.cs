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

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// MCP服务器管理器实现
    /// </summary>
    public class McpServerManager : IMcpServerManager
    {
        private readonly ConcurrentDictionary<string, McpServerDefinition> _serverDefinitions;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public McpServerManager()
        {
            _serverDefinitions = new ConcurrentDictionary<string, McpServerDefinition>();
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
                // 使用PowerShell或CMD来启动MCP服务
                bool useCmd = true; // 设置为true使用CMD, false使用PowerShell
                string shellExecutable = useCmd ? "cmd.exe" : "powershell.exe";
                string shellArgs = useCmd ? "/c " : "-NoProfile -ExecutionPolicy Bypass -Command ";
                
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
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Arguments = shellArgs + fullCommand
                };

                var process = new Process { StartInfo = processStartInfo };
                
                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) => OnProcessExited(name, (Process)sender!);

                // 异步读取输出
                process.OutputDataReceived += (sender, args) => HandleOutput(name, args.Data);
                process.ErrorDataReceived += (sender, args) => HandleError(name, args.Data);
                
                if (process.Start())
                {
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
        /// 向指定的stdio服务器发送请求并异步读取响应（此功能在此次重构中简化或禁用）
        /// </summary>
        public Task<string?> SendRequestAndReadResponseAsync(string name, string request, CancellationToken cancellationToken = default)
        {
            // TODO: 如果需要，需要重新实现与直接管理的进程交互的逻辑
            // 目前，这个功能将返回null
            return Task.FromResult<string?>(null);
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
            
            // 1. 尝试通过记录的PID停止进程
            if (definition.ProcessId.HasValue && definition.ProcessId > 0)
            {
                try
                {
                    var process = Process.GetProcessById((int)definition.ProcessId.Value);
                    // 不再检查进程名，直接终止
                    process.Kill();
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
            
            // 2. 如果通过PID失败或没有PID，尝试通过进程名强制关闭
            if (!stopped && !string.IsNullOrEmpty(definition.Command))
            {
                try
                {
                    var processName = System.IO.Path.GetFileNameWithoutExtension(definition.Command);
                    var processes = Process.GetProcessesByName(processName);
                    
                    if (processes.Length > 0)
                    {
                        foreach (var process in processes)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"强制关闭进程 {process.ProcessName} (ID: {process.Id}) 时出错: {ex.Message}");
                            }
                            finally
                            {
                                process.Dispose();
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

            if (!_serverDefinitions.TryGetValue(name, out var definition))
            {
                return false;
            }

            if (definition.IsHttpServer)
            {
                return definition.IsRunning;
            }
            
            // 检查是否有记录的进程ID并且该进程仍在运行
            if (definition.ProcessId.HasValue && definition.ProcessId > 0)
            {
                try
                {
                    var process = Process.GetProcessById((int)definition.ProcessId.Value);
                    // 注意：现在启动的是shell进程(cmd.exe或powershell.exe)，不再检查进程名匹配
                    return true;
                }
                catch (ArgumentException)
                {
                    // 进程不存在
                    return false;
                }
            }

            // 如果没有有效的PID，则该服务未被此应用实例运行
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
                var serverNames = _serverDefinitions.Keys.ToList();
                foreach (var name in serverNames)
                {
                    StopServerAsync(name).Wait(TimeSpan.FromSeconds(2));
                }
                _serverDefinitions.Clear();
            }
            catch
            {
                // 忽略释放过程中的异常
            }
        }
    }
}