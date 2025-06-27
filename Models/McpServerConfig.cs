using System;

namespace Lyxie_desktop.Models
{
    public class McpServerConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }
} 