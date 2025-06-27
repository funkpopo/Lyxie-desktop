using Lyxie_desktop.Helpers;
using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    public class McpService : IMcpService
{
    public Task<Dictionary<string, McpServerDefinition>> GetConfigsAsync()
    {
        return McpConfigHelper.LoadConfigsAsync();
    }

    public Task SaveConfigsAsync(Dictionary<string, McpServerDefinition> configs)
    {
        return McpConfigHelper.SaveConfigsAsync(configs);
    }
}
} 