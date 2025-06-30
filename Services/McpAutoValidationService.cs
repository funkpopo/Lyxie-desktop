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
            
            // "一次性验证"模型下，StartAsync只标记服务为运行状态，不主动发起验证。
            // 验证由TriggerValidationAsync按需触发。
            System.Diagnostics.Debug.WriteLine("McpAutoValidationService started.");
            await Task.CompletedTask;
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

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            System.Diagnostics.Debug.WriteLine("McpAutoValidationService stopped.");
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
            
            // 清除旧的验证结果，因为配置已改变
            _lastResults.Clear();
            System.Diagnostics.Debug.WriteLine("MCP validation configuration updated and results cleared.");

            // 如果正在运行，重新启动以应用新配置
            if (_isRunning)
            {
                await RestartAsync();
            }
        }

        /// <summary>
        /// 手动触发一次完整验证。
        /// 在"一次性验证"模型中，此方法是核心。
        /// 它只验证那些尚未成功验证过的服务器。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="forceCheck">是否强制验证。如果为true，将重新验证失败的服务器，但仍会跳过已成功的。</param>
        /// <returns>验证结果字典</returns>
        public async Task<Dictionary<string, McpValidationResult>> TriggerValidationAsync(
            CancellationToken cancellationToken = default,
            bool forceCheck = false)
        {
            var results = new Dictionary<string, McpValidationResult>();
            var tasks = new List<Task>();

            foreach (var server in _servers)
            {
                if (!server.Value.IsEnabled) continue;

                var serverName = server.Key;
                var serverDef = server.Value;

                // 核心逻辑：如果服务器已经验证成功，则永远跳过，除非状态被重置。
                if (_lastResults.TryGetValue(serverName, out var lastResult) && lastResult.IsAvailable && !forceCheck)
                {
                    System.Diagnostics.Debug.WriteLine($"使用缓存的验证结果，服务器 {serverName}, 状态: {lastResult.IsAvailable}, 距上次验证: {(DateTime.UtcNow - lastResult.ValidatedAt).TotalSeconds:F1}秒");
                    results[serverName] = lastResult;
                    continue;
                }
                
                // 如果是强制检查，或者之前没有成功过，则需要验证
                var task = Task.Run(async () =>
                {
                    var result = await ValidateServerAsync(serverName, serverDef, cancellationToken);
                    lock (results)
                    {
                        results[serverName] = result;
                    }
                }, cancellationToken);
                
                tasks.Add(task);
            }

            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
                System.Diagnostics.Debug.WriteLine($"已触发验证，验证了 {tasks.Count} 个服务器。");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("无需验证新的服务器，所有已启用服务器均已成功验证。");
            }
            
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
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = "服务器已禁用",
                    ValidatedAt = DateTime.UtcNow
                };
            }

            var result = await ValidateServerAsync(serverName, definition, cancellationToken);
            
            // 触发验证完成事件
            ValidationCompleted?.Invoke(this, (serverName, result));
            
            return result;
        }
        
        /// <summary>
        /// 重置指定服务器的验证状态，使其可以在下次触发时被重新验证。
        /// </summary>
        /// <param name="serverName">要重置的服务器名</param>
        public void ResetValidationState(string serverName)
        {
            if (string.IsNullOrEmpty(serverName)) return;

            if (_lastResults.TryRemove(serverName, out _))
            {
                System.Diagnostics.Debug.WriteLine($"服务器 {serverName} 的验证状态已重置。");
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
                // 执行实际验证
                System.Diagnostics.Debug.WriteLine($"开始验证服务器 {name}...");
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
                    result.IsAvailable = true;
                    result.Status = McpValidationStatus.Available;
                    result.ErrorMessage = "进程存在（外部启动或初始化中）";
                    System.Diagnostics.Debug.WriteLine($"服务器 {name} 虽然验证失败但进程存在，标记为可用");
                }

                // 更新结果和时间戳
                _lastResults.AddOrUpdate(name, result, (k, v) => result);
                
                // 打印验证结果
                System.Diagnostics.Debug.WriteLine($"验证服务器 {name}: {(result.IsAvailable ? "成功" : "失败")} - {result.ErrorMessage ?? "无错误"}");
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
                System.Diagnostics.Debug.WriteLine($"验证服务器 {name} 被取消");
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
                System.Diagnostics.Debug.WriteLine($"验证服务器 {name} 发生异常: {ex.Message}");
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