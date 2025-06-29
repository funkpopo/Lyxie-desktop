using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// MCP自动验证服务实现
    /// </summary>
    public class McpAutoValidationService : IMcpAutoValidationService
    {
        private readonly IMcpServerManager _serverManager;
        private readonly McpValidationService _validationService;
        private readonly ConcurrentDictionary<string, McpServerDefinition> _servers;
        private readonly ConcurrentDictionary<string, McpValidationResult> _lastResults;
        private readonly ConcurrentDictionary<string, Timer> _validationTimers;
        private readonly object _lockObject = new object();
        
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning = false;
        private bool _disposed = false;

        /// <summary>
        /// 验证状态变化事件
        /// </summary>
        public event EventHandler<(string ServerName, McpValidationResult Result)>? ValidationCompleted;

        /// <summary>
        /// 自动验证是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        public McpAutoValidationService(IMcpServerManager serverManager)
        {
            _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
            _validationService = new McpValidationService(serverManager);
            _servers = new ConcurrentDictionary<string, McpServerDefinition>();
            _lastResults = new ConcurrentDictionary<string, McpValidationResult>();
            _validationTimers = new ConcurrentDictionary<string, Timer>();
        }

        /// <summary>
        /// 启动自动验证
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                return;

            lock (_lockObject)
            {
                if (_isRunning)
                    return;

                _cancellationTokenSource = new CancellationTokenSource();
                _isRunning = true;
            }

            try
            {
                // 启动各个服务器的定时验证
                foreach (var server in _servers)
                {
                    if (server.Value.IsEnabled && server.Value.AutoValidationEnabled)
                    {
                        await StartServerValidationTimer(server.Key, server.Value);
                    }
                }
            }
            catch (Exception)
            {
                await StopAsync(cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// 停止自动验证
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_isRunning)
                return;

            lock (_lockObject)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
                _cancellationTokenSource?.Cancel();
            }

            // 停止所有定时器
            foreach (var timer in _validationTimers.Values)
            {
                timer.Dispose();
            }
            _validationTimers.Clear();

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            await Task.CompletedTask;
        }

        /// <summary>
        /// 重新启动自动验证
        /// </summary>
        public async Task RestartAsync(CancellationToken cancellationToken = default)
        {
            await StopAsync(cancellationToken);
            await StartAsync(cancellationToken);
        }

        /// <summary>
        /// 更新验证配置
        /// </summary>
        public async Task UpdateConfigurationAsync(Dictionary<string, McpServerDefinition> servers)
        {
            if (servers == null)
                return;

            // 更新服务器配置
            _servers.Clear();
            foreach (var server in servers)
            {
                _servers.TryAdd(server.Key, server.Value);
            }

            // 如果正在运行，重新启动以应用新配置
            if (_isRunning)
            {
                await RestartAsync();
            }
        }

        /// <summary>
        /// 手动触发一次完整验证
        /// </summary>
        public async Task<Dictionary<string, McpValidationResult>> TriggerValidationAsync(CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, McpValidationResult>();
            var tasks = new List<Task>();

            foreach (var server in _servers)
            {
                if (server.Value.IsEnabled)
                {
                    var task = Task.Run(async () =>
                    {
                        var result = await ValidateServerAsync(server.Key, server.Value, cancellationToken);
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
        /// 获取最近的验证结果
        /// </summary>
        public Dictionary<string, McpValidationResult> GetLastValidationResults()
        {
            return new Dictionary<string, McpValidationResult>(_lastResults);
        }

        /// <summary>
        /// 立即验证指定的单个服务器
        /// </summary>
        /// <param name="serverName">服务器名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>验证结果</returns>
        public async Task<McpValidationResult?> ValidateServerImmediatelyAsync(string serverName, CancellationToken cancellationToken = default)
        {
            if (!_servers.TryGetValue(serverName, out var definition))
            {
                return null;
            }

            if (!definition.IsEnabled)
            {
                return null;
            }

            var result = await ValidateServerAsync(serverName, definition, cancellationToken);
            
            // 触发验证完成事件
            ValidationCompleted?.Invoke(this, (serverName, result));
            
            return result;
        }

        /// <summary>
        /// 启动单个服务器的验证定时器
        /// </summary>
        private Task StartServerValidationTimer(string name, McpServerDefinition definition)
        {
            if (_validationTimers.ContainsKey(name))
            {
                // 停止现有定时器
                if (_validationTimers.TryRemove(name, out var existingTimer))
                {
                    existingTimer.Dispose();
                }
            }

            // 计算定时器间隔
            var interval = TimeSpan.FromSeconds(Math.Max(definition.ValidationInterval, 10)); // 最小10秒间隔

            // 创建新的定时器
            var timer = new Timer(async _ => await ValidateServerCallback(name, definition), 
                                null, TimeSpan.Zero, interval);
            
            _validationTimers.TryAdd(name, timer);
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// 验证服务器回调方法
        /// </summary>
        private async Task ValidateServerCallback(string name, McpServerDefinition definition)
        {
            if (_disposed || _cancellationTokenSource?.Token.IsCancellationRequested == true)
                return;

            try
            {
                var result = await ValidateServerAsync(name, definition, _cancellationTokenSource?.Token ?? CancellationToken.None);
                
                // 触发验证完成事件
                ValidationCompleted?.Invoke(this, (name, result));
            }
            catch (Exception)
            {
                // 忽略验证过程中的异常，避免影响其他服务器的验证
            }
        }

        /// <summary>
        /// 验证单个服务器
        /// </summary>
        private async Task<McpValidationResult> ValidateServerAsync(string name, McpServerDefinition definition, CancellationToken cancellationToken)
        {
            McpValidationResult result;

            try
            {
                // 执行MCP协议验证
                result = await _validationService.ValidateServerAsync(name, definition, cancellationToken);
                
                // 更新服务器定义状态
                definition.IsAvailable = result.IsAvailable;
                definition.ValidationStatus = result.Status;
                definition.ErrorMessage = result.ErrorMessage;
                definition.LastChecked = result.ValidatedAt;

                // 特殊处理：如果是本地服务器验证失败，但进程仍在运行，可能是暂时的通信问题
                if (definition.IsStdioServer && !result.IsAvailable && _serverManager.IsServerRunning(name))
                {
                    // 本地服务器仍在运行，但MCP验证失败，可能是初始化中或暂时不可用
                    // 不需要重启，下次验证可能会成功
                }

                _lastResults.AddOrUpdate(name, result, (k, v) => result);
            }
            catch (OperationCanceledException)
            {
                result = new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Timeout,
                    ErrorMessage = "验证被取消",
                    ValidatedAt = DateTime.UtcNow
                };
                
                _lastResults.AddOrUpdate(name, result, (k, v) => result);
            }
            catch (Exception ex)
            {
                result = new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = $"验证异常: {ex.Message}",
                    ValidatedAt = DateTime.UtcNow
                };
                
                _lastResults.AddOrUpdate(name, result, (k, v) => result);
            }

            return result;
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
                var stopTask = StopAsync();
                stopTask.Wait(TimeSpan.FromSeconds(2));

                _validationService?.Dispose();
            }
            catch
            {
                // 忽略释放过程中的异常
            }
        }
    }
}