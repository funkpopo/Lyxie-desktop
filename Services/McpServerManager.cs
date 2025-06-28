using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly ConcurrentDictionary<string, Process> _runningProcesses;
        private readonly ConcurrentDictionary<string, McpServerDefinition> _serverDefinitions;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public McpServerManager()
        {
            _runningProcesses = new ConcurrentDictionary<string, Process>();
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
                process.Exited += (sender, e) => OnProcessExited(name, process);

                if (process.Start())
                {
                    _runningProcesses.TryAdd(name, process);
                    definition.IsRunning = true;
                    definition.ProcessId = process.Id;
                    
                    // 等待一小段时间确保进程稳定启动
                    await Task.Delay(500, cancellationToken);
                    
                    // 检查进程是否仍在运行
                    if (!process.HasExited)
                    {
                        return true;
                    }
                    else
                    {
                        // 进程意外退出
                        _runningProcesses.TryRemove(name, out _);
                        definition.IsRunning = false;
                        definition.ProcessId = null;
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

            if (_runningProcesses.TryRemove(name, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        // 尝试优雅关闭
                        process.CloseMainWindow();
                        
                        // 等待2秒让进程自然退出
                        if (!process.WaitForExit(2000))
                        {
                            // 强制终止
                            process.Kill();
                            await process.WaitForExitAsync(cancellationToken);
                        }
                    }

                    process.Dispose();

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
            if (_runningProcesses.TryGetValue(name, out var process))
            {
                try
                {
                    return !process.HasExited;
                }
                catch
                {
                    // 进程对象可能已无效
                    _runningProcesses.TryRemove(name, out _);
                    if (definition != null)
                    {
                        definition.IsRunning = false;
                        definition.ProcessId = null;
                    }
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
                _runningProcesses.TryRemove(name, out _);
                
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
                foreach (var process in _runningProcesses.Values)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                        process.Dispose();
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
    }
}