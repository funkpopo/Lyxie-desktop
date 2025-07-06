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
    /// 错误处理和重试管理器 - 实现分级错误处理和智能重试机制
    /// </summary>
    public class ErrorHandlingManager : IDisposable
    {
        private readonly Dictionary<ErrorSeverity, ErrorHandlingStrategy> _strategies;
        private readonly Dictionary<string, RetryPolicy> _retryPolicies;
        private readonly List<ErrorRecord> _errorHistory;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public ErrorHandlingManager()
        {
            _strategies = new Dictionary<ErrorSeverity, ErrorHandlingStrategy>();
            _retryPolicies = new Dictionary<string, RetryPolicy>();
            _errorHistory = new List<ErrorRecord>();
            
            InitializeErrorStrategies();
            InitializeRetryPolicies();
            
            Debug.WriteLine("错误处理管理器已初始化");
        }

        /// <summary>
        /// 初始化错误处理策略
        /// </summary>
        private void InitializeErrorStrategies()
        {
            // 低级错误策略
            _strategies[ErrorSeverity.Low] = new ErrorHandlingStrategy
            {
                Severity = ErrorSeverity.Low,
                ShouldRetry = true,
                MaxRetryAttempts = 3,
                RetryDelayMs = 1000,
                ShouldNotifyUser = false,
                ShouldLogError = true,
                RecoveryActions = new[] { "重新尝试", "使用备用方法" }
            };

            // 中级错误策略
            _strategies[ErrorSeverity.Medium] = new ErrorHandlingStrategy
            {
                Severity = ErrorSeverity.Medium,
                ShouldRetry = true,
                MaxRetryAttempts = 2,
                RetryDelayMs = 2000,
                ShouldNotifyUser = true,
                ShouldLogError = true,
                RecoveryActions = new[] { "检查网络连接", "验证配置", "重新尝试" }
            };

            // 高级错误策略
            _strategies[ErrorSeverity.High] = new ErrorHandlingStrategy
            {
                Severity = ErrorSeverity.High,
                ShouldRetry = false,
                MaxRetryAttempts = 0,
                RetryDelayMs = 0,
                ShouldNotifyUser = true,
                ShouldLogError = true,
                RecoveryActions = new[] { "检查系统配置", "联系技术支持", "重启应用程序" }
            };

            // 致命错误策略
            _strategies[ErrorSeverity.Critical] = new ErrorHandlingStrategy
            {
                Severity = ErrorSeverity.Critical,
                ShouldRetry = false,
                MaxRetryAttempts = 0,
                RetryDelayMs = 0,
                ShouldNotifyUser = true,
                ShouldLogError = true,
                RecoveryActions = new[] { "立即停止操作", "保存数据", "重启应用程序" }
            };

            Debug.WriteLine($"已初始化 {_strategies.Count} 个错误处理策略");
        }

        /// <summary>
        /// 初始化重试策略
        /// </summary>
        private void InitializeRetryPolicies()
        {
            // 网络相关错误重试策略
            _retryPolicies["network"] = new RetryPolicy
            {
                PolicyName = "network",
                MaxAttempts = 3,
                BaseDelayMs = 1000,
                BackoffMultiplier = 2.0,
                MaxDelayMs = 10000,
                RetryableExceptions = new[] { "TimeoutException", "HttpRequestException", "SocketException" }
            };

            // 文件操作重试策略
            _retryPolicies["file"] = new RetryPolicy
            {
                PolicyName = "file",
                MaxAttempts = 2,
                BaseDelayMs = 500,
                BackoffMultiplier = 1.5,
                MaxDelayMs = 2000,
                RetryableExceptions = new[] { "IOException", "UnauthorizedAccessException", "DirectoryNotFoundException" }
            };

            // MCP工具调用重试策略
            _retryPolicies["mcp_tool"] = new RetryPolicy
            {
                PolicyName = "mcp_tool",
                MaxAttempts = 2,
                BaseDelayMs = 1500,
                BackoffMultiplier = 2.0,
                MaxDelayMs = 6000,
                RetryableExceptions = new[] { "McpServerException", "ToolExecutionException", "TimeoutException" }
            };

            // LLM API调用重试策略
            _retryPolicies["llm_api"] = new RetryPolicy
            {
                PolicyName = "llm_api",
                MaxAttempts = 3,
                BaseDelayMs = 2000,
                BackoffMultiplier = 1.8,
                MaxDelayMs = 15000,
                RetryableExceptions = new[] { "HttpRequestException", "TaskCanceledException", "ApiRateLimitException" }
            };

            Debug.WriteLine($"已初始化 {_retryPolicies.Count} 个重试策略");
        }

        /// <summary>
        /// 处理错误并决定是否重试
        /// </summary>
        public Task<ErrorHandlingResult> HandleErrorAsync(
            Exception exception, 
            string operationContext, 
            string? operationType = null,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ErrorHandlingManager));

            var errorRecord = new ErrorRecord
            {
                Exception = exception,
                OperationContext = operationContext,
                OperationType = operationType ?? "unknown",
                Timestamp = DateTime.Now,
                Severity = DetermineErrorSeverity(exception)
            };

            lock (_lockObject)
            {
                _errorHistory.Add(errorRecord);
                
                // 保持错误历史记录在合理范围内
                if (_errorHistory.Count > 1000)
                {
                    _errorHistory.RemoveRange(0, 100);
                }
            }

            Debug.WriteLine($"处理错误: {exception.GetType().Name} - {exception.Message} (严重级别: {errorRecord.Severity})");

            var strategy = _strategies[errorRecord.Severity];
            var result = new ErrorHandlingResult
            {
                ErrorRecord = errorRecord,
                Strategy = strategy,
                ShouldRetry = strategy.ShouldRetry,
                RetryDelayMs = strategy.RetryDelayMs,
                RecoveryActions = strategy.RecoveryActions.ToList(),
                UserMessage = GenerateUserMessage(errorRecord, strategy)
            };

            // 如果需要重试，应用重试策略
            if (strategy.ShouldRetry && !string.IsNullOrEmpty(operationType))
            {
                var retryInfo = CalculateRetryInfo(operationType, errorRecord);
                result.ShouldRetry = retryInfo.ShouldRetry;
                result.RetryDelayMs = retryInfo.DelayMs;
                result.RetryAttempt = retryInfo.AttemptNumber;
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// 确定错误严重级别
        /// </summary>
        private ErrorSeverity DetermineErrorSeverity(Exception exception)
        {
            var exceptionType = exception.GetType().Name;
            var message = exception.Message?.ToLowerInvariant() ?? "";

            // 致命错误
            if (exceptionType.Contains("OutOfMemory") || 
                exceptionType.Contains("StackOverflow") ||
                exceptionType.Contains("AccessViolation"))
            {
                return ErrorSeverity.Critical;
            }

            // 高级错误
            if (exceptionType.Contains("Security") ||
                exceptionType.Contains("Unauthorized") ||
                message.Contains("access denied") ||
                message.Contains("permission"))
            {
                return ErrorSeverity.High;
            }

            // 中级错误
            if (exceptionType.Contains("Http") ||
                exceptionType.Contains("Network") ||
                exceptionType.Contains("Timeout") ||
                exceptionType.Contains("Connection"))
            {
                return ErrorSeverity.Medium;
            }

            // 默认为低级错误
            return ErrorSeverity.Low;
        }

        /// <summary>
        /// 计算重试信息
        /// </summary>
        private RetryInfo CalculateRetryInfo(string operationType, ErrorRecord errorRecord)
        {
            var retryInfo = new RetryInfo { ShouldRetry = false };

            if (!_retryPolicies.TryGetValue(operationType, out var policy))
            {
                return retryInfo;
            }

            // 检查异常类型是否可重试
            var exceptionType = errorRecord.Exception.GetType().Name;
            if (!policy.RetryableExceptions.Contains(exceptionType))
            {
                return retryInfo;
            }

            // 计算当前操作的重试次数
            var recentErrors = GetRecentErrors(errorRecord.OperationContext, TimeSpan.FromMinutes(5));
            var attemptNumber = recentErrors.Count;

            if (attemptNumber >= policy.MaxAttempts)
            {
                return retryInfo;
            }

            // 计算延迟时间（指数退避）
            var delayMs = (int)(policy.BaseDelayMs * Math.Pow(policy.BackoffMultiplier, attemptNumber));
            delayMs = Math.Min(delayMs, policy.MaxDelayMs);

            retryInfo.ShouldRetry = true;
            retryInfo.DelayMs = delayMs;
            retryInfo.AttemptNumber = attemptNumber + 1;

            return retryInfo;
        }

        /// <summary>
        /// 获取最近的错误记录
        /// </summary>
        private List<ErrorRecord> GetRecentErrors(string operationContext, TimeSpan timeWindow)
        {
            var cutoffTime = DateTime.Now - timeWindow;
            
            lock (_lockObject)
            {
                return _errorHistory
                    .Where(e => e.OperationContext == operationContext && e.Timestamp >= cutoffTime)
                    .ToList();
            }
        }

        /// <summary>
        /// 生成用户友好的错误消息
        /// </summary>
        private string GenerateUserMessage(ErrorRecord errorRecord, ErrorHandlingStrategy strategy)
        {
            var baseMessage = errorRecord.Severity switch
            {
                ErrorSeverity.Low => "遇到了一个小问题，正在尝试解决...",
                ErrorSeverity.Medium => "操作遇到问题，请稍等片刻...",
                ErrorSeverity.High => "发生了严重错误，请检查系统配置",
                ErrorSeverity.Critical => "系统遇到致命错误，需要立即处理",
                _ => "发生了未知错误"
            };

            if (strategy.RecoveryActions.Length > 0)
            {
                var actions = string.Join("、", strategy.RecoveryActions);
                baseMessage += $"\n建议操作：{actions}";
            }

            return baseMessage;
        }

        /// <summary>
        /// 生成错误恢复建议
        /// </summary>
        public List<string> GenerateRecoveryRecommendations(string operationContext)
        {
            if (_disposed)
                return new List<string>();

            var recentErrors = GetRecentErrors(operationContext, TimeSpan.FromHours(1));
            var recommendations = new List<string>();

            if (recentErrors.Count == 0)
            {
                return recommendations;
            }

            // 分析错误模式
            var errorTypes = recentErrors.GroupBy(e => e.Exception.GetType().Name)
                .OrderByDescending(g => g.Count())
                .ToList();

            var mostCommonError = errorTypes.FirstOrDefault();
            if (mostCommonError != null)
            {
                recommendations.Add($"最常见错误：{mostCommonError.Key}，发生 {mostCommonError.Count()} 次");
                
                // 根据错误类型提供具体建议
                var errorType = mostCommonError.Key;
                if (errorType.Contains("Http") || errorType.Contains("Network"))
                {
                    recommendations.Add("建议检查网络连接和API配置");
                }
                else if (errorType.Contains("File") || errorType.Contains("IO"))
                {
                    recommendations.Add("建议检查文件权限和磁盘空间");
                }
                else if (errorType.Contains("Timeout"))
                {
                    recommendations.Add("建议增加超时时间或优化操作性能");
                }
            }

            // 时间模式分析
            var errorsByHour = recentErrors.GroupBy(e => e.Timestamp.Hour).ToList();
            if (errorsByHour.Count > 1)
            {
                var peakHour = errorsByHour.OrderByDescending(g => g.Count()).First();
                recommendations.Add($"错误高峰时段：{peakHour.Key}:00，建议在此时段避免重要操作");
            }

            return recommendations;
        }

        /// <summary>
        /// 获取错误统计信息
        /// </summary>
        public ErrorStatistics GetErrorStatistics(TimeSpan? timeWindow = null)
        {
            if (_disposed)
                return new ErrorStatistics();

            var window = timeWindow ?? TimeSpan.FromHours(24);
            var cutoffTime = DateTime.Now - window;

            lock (_lockObject)
            {
                var relevantErrors = _errorHistory.Where(e => e.Timestamp >= cutoffTime).ToList();
                
                return new ErrorStatistics
                {
                    TotalErrors = relevantErrors.Count,
                    ErrorsBySeverity = relevantErrors.GroupBy(e => e.Severity)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    ErrorsByType = relevantErrors.GroupBy(e => e.Exception.GetType().Name)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    ErrorsByOperation = relevantErrors.GroupBy(e => e.OperationType)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    TimeWindow = window
                };
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _errorHistory.Clear();
                _strategies.Clear();
                _retryPolicies.Clear();
                Debug.WriteLine("错误处理管理器已释放");
            }
        }
    }
}
