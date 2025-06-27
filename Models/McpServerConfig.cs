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

        // Helper to get a clean JSON representation for editing
        public string ToJson()
        {
            var tempDict = new Dictionary<string, object>();
            if (Command != null) tempDict.Add("command", Command);
            if (Args != null) tempDict.Add("args", Args);
            if (Url != null) tempDict.Add("url", Url);
            if (Protocol != null) tempDict.Add("protocol", Protocol);
            tempDict.Add("isEnabled", IsEnabled);
            return JsonConvert.SerializeObject(tempDict, Formatting.Indented);
        }
    }
}
