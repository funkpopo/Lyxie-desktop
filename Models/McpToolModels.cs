using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Lyxie_desktop.Models
{
    /// <summary>
    /// MCP工具信息
    /// </summary>
    public class McpTool
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("inputSchema")]
        public McpToolInputSchema? InputSchema { get; set; }

        /// <summary>
        /// 所属的MCP服务器名称
        /// </summary>
        [JsonIgnore]
        public string ServerName { get; set; } = string.Empty;
    }

    /// <summary>
    /// MCP工具输入模式
    /// </summary>
    public class McpToolInputSchema
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "object";

        [JsonProperty("properties")]
        public Dictionary<string, McpToolProperty>? Properties { get; set; }

        [JsonProperty("required")]
        public List<string>? Required { get; set; }
    }

    /// <summary>
    /// MCP工具属性定义
    /// </summary>
    public class McpToolProperty
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("enum")]
        public List<string>? Enum { get; set; }
    }

    /// <summary>
    /// MCP工具调用请求
    /// </summary>
    public class McpToolCall
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerName { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// MCP工具调用结果
    /// </summary>
    public class McpToolResult
    {
        public string CallId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string? Content { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.Now;
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 是否包含文本内容
        /// </summary>
        public bool HasTextContent => !string.IsNullOrEmpty(Content);
    }

    /// <summary>
    /// MCP工具调用上下文
    /// </summary>
    public class McpToolContext
    {
        public List<McpTool> AvailableTools { get; set; } = new();
        public List<McpToolCall> PendingCalls { get; set; } = new();
        public List<McpToolResult> Results { get; set; } = new();

        /// <summary>
        /// 获取格式化的工具调用结果文本
        /// </summary>
        public string GetFormattedResults()
        {
            if (Results.Count == 0)
                return string.Empty;

            var formatted = new List<string>();
            foreach (var result in Results)
            {
                if (result.IsSuccess && result.HasTextContent)
                {
                    formatted.Add($"工具调用结果：\n{result.Content}");
                }
                else if (!result.IsSuccess)
                {
                    formatted.Add($"工具调用失败：{result.ErrorMessage}");
                }
            }

            return string.Join("\n\n", formatted);
        }
    }

    /// <summary>
    /// MCP tools/list 响应模型
    /// </summary>
    public class McpToolsListResponse
    {
        [JsonProperty("tools")]
        public List<McpTool>? Tools { get; set; }
    }

    /// <summary>
    /// MCP tools/call 请求模型
    /// </summary>
    public class McpToolCallRequest
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public Dictionary<string, object>? Arguments { get; set; }
    }

    /// <summary>
    /// MCP tools/call 响应模型
    /// </summary>
    public class McpToolCallResponse
    {
        [JsonProperty("content")]
        public List<McpToolCallContent>? Content { get; set; }

        [JsonProperty("isError")]
        public bool? IsError { get; set; }
    }

    /// <summary>
    /// MCP工具调用响应内容
    /// </summary>
    public class McpToolCallContent
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("text")]
        public string? Text { get; set; }
    }

    /// <summary>
    /// LLM工具调用指令
    /// </summary>
    public class LlmToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = "function";

        [JsonProperty("function")]
        public LlmToolFunction? Function { get; set; }
    }

    /// <summary>
    /// LLM工具调用函数信息
    /// </summary>
    public class LlmToolFunction
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public string Arguments { get; set; } = string.Empty;
    }

    /// <summary>
    /// LLM完整响应
    /// </summary>
    public class LlmResponse
    {
        /// <summary>
        /// 文本内容
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 工具调用列表
        /// </summary>
        public List<LlmToolCall> ToolCalls { get; set; } = new();

        /// <summary>
        /// 是否包含工具调用
        /// </summary>
        public bool HasToolCalls => ToolCalls.Count > 0;

        /// <summary>
        /// 是否为最终响应（不包含工具调用）
        /// </summary>
        public bool IsFinalResponse => !HasToolCalls && !string.IsNullOrEmpty(Content);
    }

    /// <summary>
    /// 工具调用执行上下文
    /// </summary>
    public class ToolCallExecutionContext
    {
        /// <summary>
        /// 用户原始消息
        /// </summary>
        public string UserMessage { get; set; } = string.Empty;

        /// <summary>
        /// 对话历史
        /// </summary>
        public List<ConversationMessage> ConversationHistory { get; set; } = new();

        /// <summary>
        /// 可用工具列表
        /// </summary>
        public List<McpTool> AvailableTools { get; set; } = new();

        /// <summary>
        /// 工具调用执行结果
        /// </summary>
        public List<ToolCallExecution> ToolExecutions { get; set; } = new();

        /// <summary>
        /// 最大递归深度
        /// </summary>
        public int MaxRecursionDepth { get; set; } = 5;

        /// <summary>
        /// 当前递归深度
        /// </summary>
        public int CurrentDepth { get; set; } = 0;

        /// <summary>
        /// 是否已达到最大递归深度
        /// </summary>
        public bool HasReachedMaxDepth => CurrentDepth >= MaxRecursionDepth;
    }

    /// <summary>
    /// 对话消息
    /// </summary>
    public class ConversationMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; } = string.Empty; // user, assistant, tool

        [JsonProperty("content")]
        public string Content { get; set; } = string.Empty;

        [JsonProperty("tool_call_id")]
        public string? ToolCallId { get; set; }

        [JsonProperty("tool_calls")]
        public List<LlmToolCall>? ToolCalls { get; set; }
    }

    /// <summary>
    /// MCP服务状态枚举
    /// </summary>
    public enum McpServiceStatus
    {
        /// <summary>
        /// 未初始化
        /// </summary>
        NotInitialized,

        /// <summary>
        /// 初始化中
        /// </summary>
        Initializing,

        /// <summary>
        /// 运行中
        /// </summary>
        Running,

        /// <summary>
        /// 部分可用（部分服务器启动失败）
        /// </summary>
        PartiallyAvailable,

        /// <summary>
        /// 不可用
        /// </summary>
        Unavailable,

        /// <summary>
        /// 错误状态
        /// </summary>
        Error
    }

    /// <summary>
    /// MCP服务状态信息
    /// </summary>
    public class McpServiceStatusInfo
    {
        /// <summary>
        /// 服务状态
        /// </summary>
        public McpServiceStatus Status { get; set; } = McpServiceStatus.NotInitialized;

        /// <summary>
        /// 状态描述
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// 总服务器数量
        /// </summary>
        public int TotalServers { get; set; }

        /// <summary>
        /// 运行中的服务器数量
        /// </summary>
        public int RunningServers { get; set; }

        /// <summary>
        /// 可用工具数量
        /// </summary>
        public int AvailableTools { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        /// <summary>
        /// 错误信息（如果有）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 服务器详细状态
        /// </summary>
        public Dictionary<string, bool> ServerStatuses { get; set; } = new();
    }

    /// <summary>
    /// 工具调用日志条目
    /// </summary>
    public class ToolCallLogEntry
    {
        /// <summary>
        /// 日志ID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 工具调用ID
        /// </summary>
        public string ToolCallId { get; set; } = string.Empty;

        /// <summary>
        /// 工具名称
        /// </summary>
        public string ToolName { get; set; } = string.Empty;

        /// <summary>
        /// 服务器名称
        /// </summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// 调用参数
        /// </summary>
        public object? Parameters { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 执行状态
        /// </summary>
        public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.Pending;

        /// <summary>
        /// 执行结果
        /// </summary>
        public string? Result { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        public long DurationMs => EndTime.HasValue ?
            (long)(EndTime.Value - StartTime).TotalMilliseconds : 0;
    }

    /// <summary>
    /// 工具调用执行记录
    /// </summary>
    public class ToolCallExecution
    {
        /// <summary>
        /// LLM工具调用指令
        /// </summary>
        public LlmToolCall LlmToolCall { get; set; } = new();

        /// <summary>
        /// MCP工具调用结果
        /// </summary>
        public McpToolResult? McpResult { get; set; }

        /// <summary>
        /// 执行开始时间
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 执行结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 执行状态
        /// </summary>
        public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.Pending;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 工具执行状态
    /// </summary>
    public enum ToolExecutionStatus
    {
        Pending,
        Executing,
        Completed,
        Failed
    }

    /// <summary>
    /// 错误严重级别
    /// </summary>
    public enum ErrorSeverity
    {
        Low,      // 低级错误，可自动恢复
        Medium,   // 中级错误，需要用户注意
        High,     // 高级错误，需要用户干预
        Critical  // 致命错误，需要立即处理
    }

    /// <summary>
    /// 错误记录
    /// </summary>
    public class ErrorRecord
    {
        public Exception Exception { get; set; } = new Exception();
        public string OperationContext { get; set; } = "";
        public string OperationType { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Low;
    }

    /// <summary>
    /// 错误处理策略
    /// </summary>
    public class ErrorHandlingStrategy
    {
        public ErrorSeverity Severity { get; set; }
        public bool ShouldRetry { get; set; }
        public int MaxRetryAttempts { get; set; }
        public int RetryDelayMs { get; set; }
        public bool ShouldNotifyUser { get; set; }
        public bool ShouldLogError { get; set; }
        public string[] RecoveryActions { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 重试策略
    /// </summary>
    public class RetryPolicy
    {
        public string PolicyName { get; set; } = "";
        public int MaxAttempts { get; set; }
        public int BaseDelayMs { get; set; }
        public double BackoffMultiplier { get; set; } = 1.0;
        public int MaxDelayMs { get; set; }
        public string[] RetryableExceptions { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 重试信息
    /// </summary>
    public class RetryInfo
    {
        public bool ShouldRetry { get; set; }
        public int DelayMs { get; set; }
        public int AttemptNumber { get; set; }
    }

    /// <summary>
    /// 错误处理结果
    /// </summary>
    public class ErrorHandlingResult
    {
        public ErrorRecord ErrorRecord { get; set; } = new();
        public ErrorHandlingStrategy Strategy { get; set; } = new();
        public bool ShouldRetry { get; set; }
        public int RetryDelayMs { get; set; }
        public int RetryAttempt { get; set; }
        public List<string> RecoveryActions { get; set; } = new();
        public string UserMessage { get; set; } = "";
    }

    /// <summary>
    /// 错误统计信息
    /// </summary>
    public class ErrorStatistics
    {
        public int TotalErrors { get; set; }
        public Dictionary<ErrorSeverity, int> ErrorsBySeverity { get; set; } = new();
        public Dictionary<string, int> ErrorsByType { get; set; } = new();
        public Dictionary<string, int> ErrorsByOperation { get; set; } = new();
        public TimeSpan TimeWindow { get; set; }
    }

    /// <summary>
    /// 资源类型
    /// </summary>
    public enum ResourceType
    {
        Network,    // 网络资源
        FileSystem, // 文件系统资源
        CPU,        // CPU资源
        Memory      // 内存资源
    }

    /// <summary>
    /// 资源池
    /// </summary>
    public class ResourcePool
    {
        public string PoolName { get; set; } = "";
        public ResourceType ResourceType { get; set; }
        public int MaxConcurrency { get; set; }
        public int CurrentUsage { get; set; }
        public long TotalExecutions { get; set; }
        public long SuccessfulExecutions { get; set; }
        public TimeSpan TotalExecutionTime { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }

        public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions : 0;
        public double UtilizationRate => MaxConcurrency > 0 ? (double)CurrentUsage / MaxConcurrency : 0;
    }

    /// <summary>
    /// 执行指标
    /// </summary>
    public class ExecutionMetrics
    {
        public string ToolName { get; set; } = "";
        public long TotalExecutions { get; set; }
        public long SuccessfulExecutions { get; set; }
        public TimeSpan TotalExecutionTime { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }
        public DateTime LastExecutionTime { get; set; }

        public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions : 0;
    }

    /// <summary>
    /// 执行统计信息
    /// </summary>
    public class ExecutionStatistics
    {
        public long TotalExecutions { get; set; }
        public long SuccessfulExecutions { get; set; }
        public long FailedExecutions { get; set; }
        public double SuccessRate { get; set; }
        public List<ResourcePool> ResourcePools { get; set; } = new();
        public List<ExecutionMetrics> ToolMetrics { get; set; } = new();
    }
}