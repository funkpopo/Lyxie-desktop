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
} 