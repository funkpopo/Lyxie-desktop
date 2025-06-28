using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Interfaces
{
    /// <summary>
    /// MCP自动验证服务接口
    /// </summary>
    public interface IMcpAutoValidationService : IDisposable
    {
        /// <summary>
        /// 自动验证是否正在运行
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 验证状态变化事件
        /// </summary>
        event EventHandler<(string ServerName, Models.McpValidationResult Result)>? ValidationCompleted;

        /// <summary>
        /// 启动自动验证
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>启动任务</returns>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止自动验证
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>停止任务</returns>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 重新启动自动验证
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>重启任务</returns>
        Task RestartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新验证配置
        /// </summary>
        /// <param name="servers">服务器配置字典</param>
        /// <returns>更新任务</returns>
        Task UpdateConfigurationAsync(Dictionary<string, Models.McpServerDefinition> servers);

        /// <summary>
        /// 手动触发一次完整验证
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>验证结果字典</returns>
        Task<Dictionary<string, Models.McpValidationResult>> TriggerValidationAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取最近的验证结果
        /// </summary>
        /// <returns>验证结果字典</returns>
        Dictionary<string, Models.McpValidationResult> GetLastValidationResults();

        /// <summary>
        /// 立即验证指定的单个服务器
        /// </summary>
        /// <param name="serverName">服务器名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>验证结果</returns>
        Task<Models.McpValidationResult?> ValidateServerImmediatelyAsync(string serverName, CancellationToken cancellationToken = default);
    }
}