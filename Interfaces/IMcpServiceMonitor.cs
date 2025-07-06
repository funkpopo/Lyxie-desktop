using Lyxie_desktop.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Interfaces
{
    /// <summary>
    /// MCP服务监控接口
    /// </summary>
    public interface IMcpServiceMonitor : IDisposable
    {
        /// <summary>
        /// 当前服务状态
        /// </summary>
        McpServiceStatusInfo CurrentStatus { get; }

        /// <summary>
        /// 监控是否正在运行
        /// </summary>
        bool IsMonitoring { get; }

        /// <summary>
        /// 服务状态变化事件
        /// </summary>
        event EventHandler<McpServiceStatusInfo>? StatusChanged;

        /// <summary>
        /// 启动监控
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>启动任务</returns>
        Task StartMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止监控
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>停止任务</returns>
        Task StopMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 手动触发状态检查
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>当前状态信息</returns>
        Task<McpServiceStatusInfo> CheckStatusAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 手动触发状态更新（用于外部事件驱动）
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task TriggerStatusUpdateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 重置监控状态
        /// </summary>
        void ResetStatus();
    }
}
