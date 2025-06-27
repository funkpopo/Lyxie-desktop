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
        private List<McpServerConfig> _configs;

        public McpService()
        {
            // Initialize configs async is not ideal in a constructor.
            // A factory or an async initialization method would be better,
            // but for simplicity, we'll load it synchronously for the first access.
            _configs = new List<McpServerConfig>();
            // Let's rely on the first call to load the configs
        }

        private async Task EnsureConfigsLoadedAsync()
        {
            if (_configs == null || !_configs.Any())
            {
                _configs = await McpConfigHelper.LoadConfigsAsync();
            }
        }

        public async Task<List<McpServerConfig>> GetConfigsAsync()
        {
            await EnsureConfigsLoadedAsync();
            return _configs;
        }

        public async Task AddConfigAsync(McpServerConfig config)
        {
            await EnsureConfigsLoadedAsync();
            _configs.Add(config);
            await McpConfigHelper.SaveConfigsAsync(_configs);
        }

        public async Task UpdateConfigAsync(McpServerConfig config)
        {
            await EnsureConfigsLoadedAsync();
            var existingConfig = _configs.FirstOrDefault(c => c.Id == config.Id);
            if (existingConfig != null)
            {
                existingConfig.Name = config.Name;
                existingConfig.Command = config.Command;
                existingConfig.Arguments = config.Arguments;
                existingConfig.IsEnabled = config.IsEnabled;
                await McpConfigHelper.SaveConfigsAsync(_configs);
            }
        }

        public async Task DeleteConfigAsync(Guid id)
        {
            await EnsureConfigsLoadedAsync();
            var configToRemove = _configs.FirstOrDefault(c => c.Id == id);
            if (configToRemove != null)
            {
                _configs.Remove(configToRemove);
                await McpConfigHelper.SaveConfigsAsync(_configs);
            }
        }
    }
} 