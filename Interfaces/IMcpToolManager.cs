using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Interfaces
{
    /// <summary>
    /// MCP工具管理器接口
    /// </summary>
    public interface IMcpToolManager : IDisposable
    {
        /// <summary>
        /// 获取所有可用的MCP工具
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>可用工具列表</returns>
        Task<List<McpTool>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据用户消息智能匹配相关工具
        /// </summary>
        /// <param name="userMessage">用户消息</param>
        /// <param name="availableTools">可用工具列表</param>
        /// <returns>匹配的工具列表</returns>
        List<McpTool> MatchRelevantTools(string userMessage, List<McpTool> availableTools);

        /// <summary>
        /// 调用指定的MCP工具
        /// </summary>
        /// <param name="toolCall">工具调用请求</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>工具调用结果</returns>
        Task<McpToolResult> CallToolAsync(McpToolCall toolCall, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量调用多个工具
        /// </summary>
        /// <param name="toolCalls">工具调用请求列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>工具调用结果列表</returns>
        Task<List<McpToolResult>> CallToolsAsync(List<McpToolCall> toolCalls, CancellationToken cancellationToken = default);

        /// <summary>
        /// 为用户消息生成工具调用上下文
        /// </summary>
        /// <param name="userMessage">用户消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>工具调用上下文</returns>
        Task<McpToolContext> GenerateToolContextAsync(string userMessage, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查MCP服务器是否支持工具调用
        /// </summary>
        /// <param name="serverName">服务器名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否支持工具调用</returns>
        Task<bool> IsToolsSupportedAsync(string serverName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 工具调用状态事件
        /// </summary>
        event EventHandler<ToolCallStatusEventArgs>? ToolCallStatusChanged;
    }

    /// <summary>
    /// 工具调用状态事件参数
    /// </summary>
    public class ToolCallStatusEventArgs : EventArgs
    {
        public string CallId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public ToolCallStatus Status { get; set; }
        public string? Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 工具调用状态枚举
    /// </summary>
    public enum ToolCallStatus
    {
        Started,
        InProgress,
        Completed,
        Failed,
        Timeout
    }
} 