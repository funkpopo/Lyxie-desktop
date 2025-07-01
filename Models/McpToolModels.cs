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
} 