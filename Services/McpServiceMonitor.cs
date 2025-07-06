using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// MCP服务监控实现
    /// </summary>
    public class McpServiceMonitor : IMcpServiceMonitor
    {
        private readonly IMcpService _mcpService;
        private readonly IMcpToolManager _toolManager;
        private readonly object _lockObject = new object();
        
        private McpServiceStatusInfo _currentStatus;
        private bool _isMonitoring = false;
        private bool _disposed = false;

        /// <summary>
        /// 服务状态变化事件
        /// </summary>
        public event EventHandler<McpServiceStatusInfo>? StatusChanged;

        public McpServiceMonitor(IMcpService mcpService, IMcpToolManager toolManager)
        {
            _mcpService = mcpService ?? throw new ArgumentNullException(nameof(mcpService));
            _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
            
            _currentStatus = new McpServiceStatusInfo
            {
                Status = McpServiceStatus.NotInitialized,
                StatusMessage = "MCP服务监控已初始化"
            };
        }

        /// <summary>
        /// 当前服务状态
        /// </summary>
        public McpServiceStatusInfo CurrentStatus
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentStatus;
                }
            }
        }

        /// <summary>
        /// 监控是否正在运行
        /// </summary>
        public bool IsMonitoring
        {
            get
            {
                lock (_lockObject)
                {
                    return _isMonitoring;
                }
            }
        }

        /// <summary>
        /// 启动监控（仅执行一次状态检查）
        /// </summary>
        public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(McpServiceMonitor));

            lock (_lockObject)
            {
                if (_isMonitoring)
                    return;

                _isMonitoring = true;
            }

            try
            {
                Debug.WriteLine("启动MCP服务监控（一次性状态检查）...");

                // 执行一次状态检查
                await CheckStatusAsync(cancellationToken);

                Debug.WriteLine("MCP服务监控已完成初始状态检查");
            }
            catch (Exception ex)
            {
                lock (_lockObject)
                {
                    _isMonitoring = false;
                }
                Debug.WriteLine($"启动MCP服务监控失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return Task.CompletedTask;

            lock (_lockObject)
            {
                if (!_isMonitoring)
                    return Task.CompletedTask;

                _isMonitoring = false;
            }

            Debug.WriteLine("MCP服务监控已停止");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 手动触发状态检查
        /// </summary>
        public async Task<McpServiceStatusInfo> CheckStatusAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(McpServiceMonitor));

            try
            {
                UpdateStatus(McpServiceStatus.Initializing, "正在检查MCP服务状态...");

                // 获取MCP服务配置
                var configs = await _mcpService.GetConfigsAsync();
                var enabledConfigs = configs.Where(kvp => kvp.Value.IsEnabled).ToList();

                if (enabledConfigs.Count == 0)
                {
                    UpdateStatus(McpServiceStatus.Unavailable, "没有启用的MCP服务器");
                    return CurrentStatus;
                }

                // 检查服务器运行状态
                var serverStatuses = new Dictionary<string, bool>();
                var runningCount = 0;

                foreach (var config in enabledConfigs)
                {
                    var isRunning = _mcpService.ServerManager.IsServerRunning(config.Key);
                    serverStatuses[config.Key] = isRunning;
                    if (isRunning) runningCount++;
                }

                // 获取可用工具数量
                var availableTools = await _toolManager.GetAvailableToolsAsync(cancellationToken);

                // 确定整体状态
                McpServiceStatus overallStatus;
                string statusMessage;

                if (runningCount == 0)
                {
                    overallStatus = McpServiceStatus.Unavailable;
                    statusMessage = "所有MCP服务器都未运行";
                }
                else if (runningCount == enabledConfigs.Count)
                {
                    overallStatus = McpServiceStatus.Running;
                    statusMessage = $"所有MCP服务器正常运行 ({runningCount}/{enabledConfigs.Count})";
                }
                else
                {
                    overallStatus = McpServiceStatus.PartiallyAvailable;
                    statusMessage = $"部分MCP服务器运行中 ({runningCount}/{enabledConfigs.Count})";
                }

                // 更新状态信息
                var newStatus = new McpServiceStatusInfo
                {
                    Status = overallStatus,
                    StatusMessage = statusMessage,
                    TotalServers = enabledConfigs.Count,
                    RunningServers = runningCount,
                    AvailableTools = availableTools.Count,
                    LastUpdated = DateTime.Now,
                    ServerStatuses = serverStatuses
                };

                UpdateStatus(newStatus);
                return CurrentStatus;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检查MCP服务状态异常: {ex.Message}");
                UpdateStatus(McpServiceStatus.Error, $"状态检查失败: {ex.Message}", ex.Message);
                return CurrentStatus;
            }
        }

        /// <summary>
        /// 手动触发状态更新（用于外部事件驱动）
        /// </summary>
        public async Task TriggerStatusUpdateAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return;

            try
            {
                Debug.WriteLine("手动触发MCP状态更新...");
                await CheckStatusAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"手动状态更新失败: {ex.Message}");
                UpdateStatus(McpServiceStatus.Error, $"状态更新失败: {ex.Message}", ex.Message);
            }
        }

        /// <summary>
        /// 重置监控状态
        /// </summary>
        public void ResetStatus()
        {
            UpdateStatus(McpServiceStatus.NotInitialized, "监控状态已重置");
        }

        /// <summary>
        /// 更新状态信息
        /// </summary>
        private void UpdateStatus(McpServiceStatus status, string message, string? errorMessage = null)
        {
            var newStatus = new McpServiceStatusInfo
            {
                Status = status,
                StatusMessage = message,
                LastUpdated = DateTime.Now,
                ErrorMessage = errorMessage
            };

            UpdateStatus(newStatus);
        }

        /// <summary>
        /// 更新状态信息
        /// </summary>
        private void UpdateStatus(McpServiceStatusInfo newStatus)
        {
            bool statusChanged = false;

            lock (_lockObject)
            {
                // 检查状态是否真的发生了变化
                if (_currentStatus.Status != newStatus.Status || 
                    _currentStatus.StatusMessage != newStatus.StatusMessage ||
                    _currentStatus.RunningServers != newStatus.RunningServers ||
                    _currentStatus.AvailableTools != newStatus.AvailableTools)
                {
                    _currentStatus = newStatus;
                    statusChanged = true;
                }
            }

            if (statusChanged)
            {
                Debug.WriteLine($"MCP服务状态更新: {newStatus.Status} - {newStatus.StatusMessage}");
                StatusChanged?.Invoke(this, newStatus);
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
                StopMonitoringAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // 忽略释放过程中的异常
            }

            Debug.WriteLine("MCP服务监控已释放资源");
        }
    }
}
