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
        private readonly ConcurrentDictionary<string, ManagedProcess> _runningProcesses;
        private readonly ConcurrentDictionary<string, McpServerDefinition> _serverDefinitions;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public McpServerManager()
        {
            _runningProcesses = new ConcurrentDictionary<string, ManagedProcess>();
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
                return true;
            }

            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = definition.Command!,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // 添加参数
                if (definition.Args != null)
                {
                    foreach (var arg in definition.Args)
                    {
                        processStartInfo.ArgumentList.Add(arg);
                    }
                }

                var process = new Process { StartInfo = processStartInfo };
                
                // 设置进程退出事件处理
                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) => OnProcessExited(name, (Process)sender!);
                
                var managedProcess = new ManagedProcess(name, process, this);

                if (managedProcess.Process.Start())
                {
                    // 开始异步读取输出
                    managedProcess.Process.BeginOutputReadLine();
                    managedProcess.Process.BeginErrorReadLine();

                    _runningProcesses.TryAdd(name, managedProcess);
                    definition.IsRunning = true;
                    definition.ProcessId = managedProcess.Process.Id;
                    
                    // 等待一小段时间确保进程稳定启动
                    await Task.Delay(500, cancellationToken);
                    
                    // 检查进程是否仍在运行
                    if (!managedProcess.Process.HasExited)
                    {
                        return true;
                    }
                    else
                    {
                        // 进程意外退出
                        OnProcessExited(name, managedProcess.Process);
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
            if (!_runningProcesses.TryGetValue(name, out var managedProcess))
            {
                return null; // 服务器未运行
            }

            return await managedProcess.SendRequestAsync(request, cancellationToken);
        }

        /// <summary>
        /// 停止指定的MCP服务器
        /// </summary>
        public async Task<bool> StopServerAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // 获取服务器定义
            if (_serverDefinitions.TryGetValue(name, out var definition))
            {
                // HTTP服务器不需要停止进程
                if (definition.IsHttpServer)
                {
                    definition.IsRunning = false;
                    return true;
                }
            }

            if (_runningProcesses.TryRemove(name, out var managedProcess))
            {
                try
                {
                    if (!managedProcess.Process.HasExited)
                    {
                        managedProcess.Unsubscribe();
                        
                        // 尝试优雅关闭
                        managedProcess.Process.CloseMainWindow();
                        
                        // 等待2秒让进程自然退出
                        if (!managedProcess.Process.WaitForExit(2000))
                        {
                            // 强制终止
                            managedProcess.Process.Kill();
                            await managedProcess.Process.WaitForExitAsync(cancellationToken);
                        }
                    }

                    managedProcess.Process.Dispose();

                    if (definition != null)
                    {
                        definition.IsRunning = false;
                        definition.ProcessId = null;
                    }

                    return true;
                }
                catch (Exception)
                {
                    // 进程可能已经退出
                    if (definition != null)
                    {
                        definition.IsRunning = false;
                        definition.ProcessId = null;
                    }
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查服务器是否正在运行
        /// </summary>
        public bool IsServerRunning(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // 获取服务器定义
            if (_serverDefinitions.TryGetValue(name, out var definition))
            {
                // HTTP服务器根据配置状态判断
                if (definition.IsHttpServer)
                {
                    return definition.IsRunning;
                }
            }

            // stdio服务器检查进程状态
            if (_runningProcesses.TryGetValue(name, out var managedProcess))
            {
                try
                {
                    return !managedProcess.Process.HasExited;
                }
                catch
                {
                    // 进程对象可能已无效
                    OnProcessExited(name, managedProcess.Process);
                    return false;
                }
            }

            return false;
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

                if (_runningProcesses.TryRemove(name, out var managedProcess))
                {
                    managedProcess.Unsubscribe();
                }
                
                if (_serverDefinitions.TryGetValue(name, out var definition))
                {
                    definition.IsRunning = false;
                    definition.ProcessId = null;
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
                // 停止所有进程
                var stopTask = StopAllServersAsync();
                stopTask.Wait(TimeSpan.FromSeconds(5)); // 最多等待5秒

                // 清理资源
                foreach (var managedProcess in _runningProcesses.Values)
                {
                    try
                    {
                        if (!managedProcess.Process.HasExited)
                        {
                            managedProcess.Process.Kill();
                        }
                        managedProcess.Process.Dispose();
                    }
                    catch
                    {
                        // 忽略清理异常
                    }
                }

                _runningProcesses.Clear();
                _serverDefinitions.Clear();
            }
            catch
            {
                // 忽略释放过程中的异常
            }
        }

        /// <summary>
        /// 内部类，用于管理进程及其事件处理器
        /// </summary>
        private class ManagedProcess
        {
            public Process Process { get; }
            private readonly string _serverName;
            private readonly McpServerManager _owner;
            private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests = new();

            public ManagedProcess(string serverName, Process process, McpServerManager owner)
            {
                _serverName = serverName;
                Process = process;
                _owner = owner;
                
                Subscribe();
            }

            private void Subscribe()
            {
                Process.OutputDataReceived += HandleOutput;
                Process.ErrorDataReceived += HandleError;
            }

            public void Unsubscribe()
            {
                try
                {
                    Process.CancelOutputRead();
                    Process.CancelErrorRead();
                    Process.OutputDataReceived -= HandleOutput;
                    Process.ErrorDataReceived -= HandleError;
                }
                catch (InvalidOperationException)
                {
                    // 进程可能已经退出，忽略异常
                }
            }
            
            private void HandleOutput(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;

                try
                {
                    using var jsonDoc = JsonDocument.Parse(e.Data);
                    if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
                    {
                        var id = idElement.ToString();
                        if (_pendingRequests.TryRemove(id, out var tcs))
                        {
                            tcs.TrySetResult(e.Data);
                        }
                    }
                }
                catch (JsonException)
                {
                    // 不是有效的JSON或不包含ID，可能是通知或日志，直接路由到调试输出
                    _owner.HandleOutput(_serverName, e.Data);
                }
            }
            
            private void HandleError(object sender, DataReceivedEventArgs e) => _owner.HandleError(_serverName, e.Data);

            public async Task<string?> SendRequestAsync(string request, CancellationToken cancellationToken)
            {
                string? requestId = null;
                try
                {
                    using var jsonDoc = JsonDocument.Parse(request);
                    if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
                    {
                        requestId = idElement.ToString();
                    }
                }
                catch (JsonException)
                {
                    // 请求不是有效的JSON，无法处理
                    return null;
                }

                if (string.IsNullOrEmpty(requestId))
                {
                    // 不支持没有ID的请求
                    return null;
                }

                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_pendingRequests.TryAdd(requestId, tcs))
                {
                    // 重复的请求ID
                    return null;
                }

                try
                {
                    await Process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken);
                    await Process.StandardInput.FlushAsync(cancellationToken);

                    // 等待响应或超时
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));

                    if (completedTask == tcs.Task)
                    {
                        return await tcs.Task; // 返回响应
                    }
                    else
                    {
                        // 超时或取消
                        tcs.TrySetCanceled();
                        return null;
                    }
                }
                finally
                {
                    _pendingRequests.TryRemove(requestId, out _);
                }
            }
        }
    }
}