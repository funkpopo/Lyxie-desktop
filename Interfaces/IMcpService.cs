using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lyxie_desktop.Interfaces
{
    public interface IMcpService
    {
        Task<List<McpServerConfig>> GetConfigsAsync();
        Task AddConfigAsync(McpServerConfig config);
        Task UpdateConfigAsync(McpServerConfig config);
        Task DeleteConfigAsync(Guid id);
    }
} 