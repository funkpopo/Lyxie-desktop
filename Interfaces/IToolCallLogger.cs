using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lyxie_desktop.Interfaces
{
    /// <summary>
    /// 工具调用日志接口
    /// </summary>
    public interface IToolCallLogger : IDisposable
    {
        /// <summary>
        /// 记录工具调用开始
        /// </summary>
        /// <param name="toolCallId">工具调用ID</param>
        /// <param name="toolName">工具名称</param>
        /// <param name="serverName">服务器名称</param>
        /// <param name="parameters">调用参数</param>
        /// <returns>日志条目ID</returns>
        string LogToolCallStart(string toolCallId, string toolName, string serverName, object? parameters = null);

        /// <summary>
        /// 记录工具调用完成
        /// </summary>
        /// <param name="logEntryId">日志条目ID</param>
        /// <param name="result">执行结果</param>
        /// <param name="status">执行状态</param>
        void LogToolCallComplete(string logEntryId, string? result, ToolExecutionStatus status);

        /// <summary>
        /// 记录工具调用失败
        /// </summary>
        /// <param name="logEntryId">日志条目ID</param>
        /// <param name="errorMessage">错误信息</param>
        void LogToolCallError(string logEntryId, string errorMessage);

        /// <summary>
        /// 获取最近的工具调用日志
        /// </summary>
        /// <param name="count">获取数量</param>
        /// <returns>日志条目列表</returns>
        List<ToolCallLogEntry> GetRecentLogs(int count = 50);

        /// <summary>
        /// 获取指定工具的调用日志
        /// </summary>
        /// <param name="toolName">工具名称</param>
        /// <param name="count">获取数量</param>
        /// <returns>日志条目列表</returns>
        List<ToolCallLogEntry> GetToolLogs(string toolName, int count = 20);

        /// <summary>
        /// 获取指定服务器的调用日志
        /// </summary>
        /// <param name="serverName">服务器名称</param>
        /// <param name="count">获取数量</param>
        /// <returns>日志条目列表</returns>
        List<ToolCallLogEntry> GetServerLogs(string serverName, int count = 20);

        /// <summary>
        /// 清理过期日志
        /// </summary>
        /// <param name="olderThan">清理早于此时间的日志</param>
        /// <returns>清理的日志数量</returns>
        Task<int> CleanupOldLogsAsync(DateTime olderThan);

        /// <summary>
        /// 获取工具调用统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        ToolCallStatistics GetStatistics();

        /// <summary>
        /// 日志记录事件
        /// </summary>
        event EventHandler<ToolCallLogEntry>? LogEntryAdded;
    }

    /// <summary>
    /// 工具调用统计信息
    /// </summary>
    public class ToolCallStatistics
    {
        /// <summary>
        /// 总调用次数
        /// </summary>
        public int TotalCalls { get; set; }

        /// <summary>
        /// 成功调用次数
        /// </summary>
        public int SuccessfulCalls { get; set; }

        /// <summary>
        /// 失败调用次数
        /// </summary>
        public int FailedCalls { get; set; }

        /// <summary>
        /// 平均执行时间（毫秒）
        /// </summary>
        public double AverageExecutionTimeMs { get; set; }

        /// <summary>
        /// 最常用的工具
        /// </summary>
        public Dictionary<string, int> MostUsedTools { get; set; } = new();

        /// <summary>
        /// 成功率
        /// </summary>
        public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls * 100 : 0;
    }
}
