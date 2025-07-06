using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// 工具调用日志服务实现
    /// </summary>
    public class ToolCallLogger : IToolCallLogger
    {
        private readonly ConcurrentDictionary<string, ToolCallLogEntry> _logEntries;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        /// <summary>
        /// 日志记录事件
        /// </summary>
        public event EventHandler<ToolCallLogEntry>? LogEntryAdded;

        public ToolCallLogger()
        {
            _logEntries = new ConcurrentDictionary<string, ToolCallLogEntry>();
        }

        /// <summary>
        /// 记录工具调用开始
        /// </summary>
        public string LogToolCallStart(string toolCallId, string toolName, string serverName, object? parameters = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ToolCallLogger));

            var logEntry = new ToolCallLogEntry
            {
                ToolCallId = toolCallId,
                ToolName = toolName,
                ServerName = serverName,
                Parameters = parameters,
                StartTime = DateTime.Now,
                Status = ToolExecutionStatus.Executing
            };

            _logEntries.TryAdd(logEntry.Id, logEntry);

            Debug.WriteLine($"[ToolCallLogger] 开始记录工具调用: {toolName} (ID: {toolCallId})");
            LogEntryAdded?.Invoke(this, logEntry);

            return logEntry.Id;
        }

        /// <summary>
        /// 记录工具调用完成
        /// </summary>
        public void LogToolCallComplete(string logEntryId, string? result, ToolExecutionStatus status)
        {
            if (_disposed)
                return;

            if (_logEntries.TryGetValue(logEntryId, out var logEntry))
            {
                logEntry.EndTime = DateTime.Now;
                logEntry.Status = status;
                logEntry.Result = result;

                Debug.WriteLine($"[ToolCallLogger] 工具调用完成: {logEntry.ToolName} - 状态: {status}, 耗时: {logEntry.DurationMs}ms");
                LogEntryAdded?.Invoke(this, logEntry);
            }
        }

        /// <summary>
        /// 记录工具调用失败
        /// </summary>
        public void LogToolCallError(string logEntryId, string errorMessage)
        {
            if (_disposed)
                return;

            if (_logEntries.TryGetValue(logEntryId, out var logEntry))
            {
                logEntry.EndTime = DateTime.Now;
                logEntry.Status = ToolExecutionStatus.Failed;
                logEntry.ErrorMessage = errorMessage;

                Debug.WriteLine($"[ToolCallLogger] 工具调用失败: {logEntry.ToolName} - 错误: {errorMessage}");
                LogEntryAdded?.Invoke(this, logEntry);
            }
        }

        /// <summary>
        /// 获取最近的工具调用日志
        /// </summary>
        public List<ToolCallLogEntry> GetRecentLogs(int count = 50)
        {
            if (_disposed)
                return new List<ToolCallLogEntry>();

            return _logEntries.Values
                .OrderByDescending(entry => entry.StartTime)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// 获取指定工具的调用日志
        /// </summary>
        public List<ToolCallLogEntry> GetToolLogs(string toolName, int count = 20)
        {
            if (_disposed)
                return new List<ToolCallLogEntry>();

            return _logEntries.Values
                .Where(entry => entry.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.StartTime)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// 获取指定服务器的调用日志
        /// </summary>
        public List<ToolCallLogEntry> GetServerLogs(string serverName, int count = 20)
        {
            if (_disposed)
                return new List<ToolCallLogEntry>();

            return _logEntries.Values
                .Where(entry => entry.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.StartTime)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// 清理过期日志
        /// </summary>
        public Task<int> CleanupOldLogsAsync(DateTime olderThan)
        {
            if (_disposed)
                return Task.FromResult(0);

            return Task.Run(() =>
            {
                var keysToRemove = _logEntries
                    .Where(kvp => kvp.Value.StartTime < olderThan)
                    .Select(kvp => kvp.Key)
                    .ToList();

                int removedCount = 0;
                foreach (var key in keysToRemove)
                {
                    if (_logEntries.TryRemove(key, out _))
                    {
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    Debug.WriteLine($"[ToolCallLogger] 清理了 {removedCount} 条过期日志");
                }

                return removedCount;
            });
        }

        /// <summary>
        /// 获取工具调用统计信息
        /// </summary>
        public ToolCallStatistics GetStatistics()
        {
            if (_disposed)
                return new ToolCallStatistics();

            var allEntries = _logEntries.Values.ToList();
            var completedEntries = allEntries.Where(e => e.EndTime.HasValue).ToList();

            var statistics = new ToolCallStatistics
            {
                TotalCalls = allEntries.Count,
                SuccessfulCalls = completedEntries.Count(e => e.Status == ToolExecutionStatus.Completed),
                FailedCalls = completedEntries.Count(e => e.Status == ToolExecutionStatus.Failed)
            };

            // 计算平均执行时间
            if (completedEntries.Count > 0)
            {
                statistics.AverageExecutionTimeMs = completedEntries.Average(e => e.DurationMs);
            }

            // 统计最常用的工具
            var toolUsage = allEntries
                .GroupBy(e => e.ToolName)
                .ToDictionary(g => g.Key, g => g.Count());

            statistics.MostUsedTools = toolUsage
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return statistics;
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
                _logEntries.Clear();
                Debug.WriteLine("[ToolCallLogger] 工具调用日志服务已释放资源");
            }
            catch
            {
                // 忽略释放过程中的异常
            }
        }
    }
}
