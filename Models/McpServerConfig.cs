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
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 实际可用性状态，通过MCP协议验证确定
    /// </summary>
    [JsonIgnore]
    public bool IsAvailable { get; set; } = false;

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
}
