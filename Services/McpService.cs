using Lyxie_desktop.Helpers;
using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    public class McpService : IMcpService, IDisposable
    {
        private readonly IMcpServerManager _serverManager;
        private readonly IMcpToolManager _toolManager;

        public McpService()
        {
            _serverManager = new McpServerManager();
            _toolManager = new McpToolManager(this, _serverManager);
        }

        /// <summary>
        /// Gets the server manager instance.
        /// </summary>
        public IMcpServerManager ServerManager => _serverManager;
        
        public IMcpToolManager ToolManager => _toolManager;

        public Task<Dictionary<string, McpServerDefinition>> GetConfigsAsync()
        {
            return McpConfigHelper.LoadConfigsAsync();
        }

        /// <summary>
        /// Saves the server configurations.
        /// </summary>
        public async Task SaveConfigsAsync(Dictionary<string, McpServerDefinition> configs)
        {
            await McpConfigHelper.SaveConfigsAsync(configs);
        }

        /// <summary>
        /// Starts a specific MCP server.
        /// </summary>
        public async Task<bool> StartServerAsync(string name, CancellationToken cancellationToken = default)
        {
            var configs = await GetConfigsAsync();
            if (configs.TryGetValue(name, out var definition))
            {
                return await _serverManager.StartServerAsync(name, definition, cancellationToken);
            }
            return false;
        }

        /// <summary>
        /// Stops a specific MCP server.
        /// </summary>
        /// <param name="name">The name of the server.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>True if the server was stopped successfully, otherwise false.</returns>
        public async Task<bool> StopServerAsync(string name, CancellationToken cancellationToken = default)
        {
            return await _serverManager.StopServerAsync(name, cancellationToken);
        }

        /// <summary>
        /// Starts all enabled MCP servers.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A dictionary containing the server names and their start-up result.</returns>
        public async Task<Dictionary<string, bool>> StartAllServersAsync(CancellationToken cancellationToken = default)
        {
            var configs = await GetConfigsAsync();
            var enabledConfigs = configs.Where(c => c.Value.IsEnabled)
                                        .ToDictionary(c => c.Key, c => c.Value);
            return await _serverManager.StartAllServersAsync(enabledConfigs, cancellationToken);
        }

        /// <summary>
        /// Stops all running MCP servers.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A dictionary containing the server names and their stop result.</returns>
        public async Task<Dictionary<string, bool>> StopAllServersAsync(CancellationToken cancellationToken = default)
        {
            return await _serverManager.StopAllServersAsync(cancellationToken);
        }

        /// <summary>
        /// Gets the names of all currently running servers.
        /// </summary>
        /// <returns>A collection of server names.</returns>
        public IEnumerable<string> GetRunningServers()
        {
            return _serverManager.GetRunningServers();
        }

        /// <summary>
        /// Checks if a specific server is currently running.
        /// </summary>
        /// <param name="name">The name of the server.</param>
        /// <returns>True if the server is running, otherwise false.</returns>
        public bool IsServerRunning(string name)
        {
            return _serverManager.IsServerRunning(name);
        }

        public void Dispose()
        {
            if (_serverManager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}