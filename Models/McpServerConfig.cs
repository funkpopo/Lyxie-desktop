using Newtonsoft.Json;
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
    }
}
