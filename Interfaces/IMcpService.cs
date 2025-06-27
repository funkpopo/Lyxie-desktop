using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lyxie_desktop.Interfaces
{
    public interface IMcpService
    {
        Task<Dictionary<string, McpServerDefinition>> GetConfigsAsync();
        Task SaveConfigsAsync(Dictionary<string, McpServerDefinition> configs);
    }
} 