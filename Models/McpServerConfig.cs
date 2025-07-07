using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Lyxie_desktop.Models
{
    public class McpConfigRoot
    {
        [JsonProperty("mcpServers")]
        public Dictionary<string, McpServerDefinition> McpServers { get; set; } = new Dictionary<string, McpServerDefinition>();
    }

    public class McpServerDefinition
{
    [JsonProperty("command", NullValueHandling = NullValueHandling.Ignore)]
    public string? Command { get; set; }

    [JsonProperty("args", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Args { get; set; }

    [JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
    public string? Url { get; set; }

    [JsonProperty("protocol", NullValueHandling = NullValueHandling.Ignore)]
    public string? Protocol { get; set; }

    [JsonProperty("isEnabled")]
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// 是否启用自动验证
    /// </summary>
    [JsonProperty("autoValidationEnabled")]
    public bool AutoValidationEnabled { get; set; } = true;

    /// <summary>
    /// 自动验证间隔（秒），默认60秒
    /// </summary>
    [JsonProperty("validationInterval")]
    public int ValidationInterval { get; set; } = 60;

    /// <summary>
    /// SSE端点URL（用于验证SSE连接）
    /// </summary>
    [JsonProperty("sseUrl", NullValueHandling = NullValueHandling.Ignore)]
    public string? SseUrl { get; set; }

    /// <summary>
    /// 连接超时时间（秒），默认10秒
    /// </summary>
    [JsonProperty("connectionTimeout")]
    public int ConnectionTimeout { get; set; } = 10;

    /// <summary>
    /// 实际可用性状态，通过MCP协议验证确定
    /// </summary>
    [JsonIgnore]
    public bool IsAvailable { get; set; } = false;

    /// <summary>
    /// 是否正在运行（仅对本地stdio服务器有效）
    /// </summary>
    [JsonIgnore]
    public bool IsRunning { get; set; } = false;

    /// <summary>
    /// 进程ID（仅对本地stdio服务器有效）
    /// </summary>
    [JsonIgnore]
    public int? ProcessId { get; set; }

    /// <summary>
    /// 上次验证时间
    /// </summary>
    [JsonIgnore]
    public DateTime? LastChecked { get; set; }

    /// <summary>
    /// 验证错误信息
    /// </summary>
    [JsonIgnore]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 验证状态
    /// </summary>
    [JsonIgnore]
    public McpValidationStatus ValidationStatus { get; set; } = McpValidationStatus.Unknown;

    /// <summary>
    /// 是否为本地stdio服务器
    /// </summary>
    [JsonIgnore]
    public bool IsStdioServer => !string.IsNullOrEmpty(Command);

    /// <summary>
    /// 是否为HTTP服务器
    /// </summary>
    [JsonIgnore]
    public bool IsHttpServer => !string.IsNullOrEmpty(Url);
}

    /// <summary>
    /// MCP验证状态枚举
    /// </summary>
    public enum McpValidationStatus
    {
        /// <summary>
        /// 未知状态，尚未验证
        /// </summary>
        Unknown,
        
        /// <summary>
        /// 验证中
        /// </summary>
        Validating,
        
        /// <summary>
        /// 验证成功，服务可用
        /// </summary>
        Available,
        
        /// <summary>
        /// 验证失败，服务不可用
        /// </summary>
        Unavailable,
        
        /// <summary>
        /// 验证超时
        /// </summary>
        Timeout,
        
        /// <summary>
        /// 配置错误
        /// </summary>
        ConfigurationError
    }

    /// <summary>
    /// MCP验证结果
    /// </summary>
    public class McpValidationResult
    {
        /// <summary>
        /// 是否可用
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// 验证状态
        /// </summary>
        public McpValidationStatus Status { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 验证时间
        /// </summary>
        public DateTime ValidatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// MCP验证状态摘要
    /// </summary>
    public class McpValidationSummary
    {
        /// <summary>
        /// 总服务器数量
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 已启用服务器数量
        /// </summary>
        public int EnabledCount { get; set; }

        /// <summary>
        /// 已禁用服务器数量
        /// </summary>
        public int DisabledCount { get; set; }

        /// <summary>
        /// 可用服务器数量
        /// </summary>
        public int AvailableCount { get; set; }

        /// <summary>
        /// 不可用服务器数量
        /// </summary>
        public int UnavailableCount { get; set; }

        /// <summary>
        /// 未知状态服务器数量
        /// </summary>
        public int UnknownCount { get; set; }
    }
}
