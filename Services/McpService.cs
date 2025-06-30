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
    public class McpService : IMcpService
    {
        private readonly McpValidationService _validationService;
        private readonly IMcpServerManager _serverManager;
        private readonly IMcpAutoValidationService _autoValidationService;

        public McpService()
        {
            _serverManager = new McpServerManager();
            _validationService = new McpValidationService(_serverManager);
            _autoValidationService = new McpAutoValidationService(_serverManager);
        }

        /// <summary>
        /// 自动验证是否正在运行
        /// </summary>
        public bool IsAutoValidationRunning => _autoValidationService.IsRunning;

        /// <summary>
        /// 获取自动验证服务实例
        /// </summary>
        public IMcpAutoValidationService AutoValidationService => _autoValidationService;

        /// <summary>
        /// 获取服务器管理器实例
        /// </summary>
        public IMcpServerManager ServerManager => _serverManager;

        public Task<Dictionary<string, McpServerDefinition>> GetConfigsAsync()
        {
            return McpConfigHelper.LoadConfigsAsync();
        }

        public async Task SaveConfigsAsync(Dictionary<string, McpServerDefinition> configs)
        {
            await McpConfigHelper.SaveConfigsAsync(configs);
            
            // 更新自动验证配置
            await _autoValidationService.UpdateConfigurationAsync(configs);
        }

        /// <summary>
        /// 验证单个MCP服务器的可用性
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <param name="definition">服务器定义</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>验证结果</returns>
        public async Task<McpValidationResult> ValidateServerAsync(string name, McpServerDefinition definition, CancellationToken cancellationToken = default)
        {
            var result = await _validationService.ValidateServerAsync(name, definition, cancellationToken);
            
            // 更新服务器定义的状态
            definition.IsAvailable = result.IsAvailable;
            definition.ValidationStatus = result.Status;
            definition.ErrorMessage = result.ErrorMessage;
            definition.LastChecked = result.ValidatedAt;

            return result;
        }

        /// <summary>
        /// 验证所有MCP服务器的可用性
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>验证结果字典</returns>
        public async Task<Dictionary<string, McpValidationResult>> ValidateAllServersAsync(CancellationToken cancellationToken = default)
        {
            var configs = await GetConfigsAsync();
            var results = await _validationService.ValidateServersAsync(configs, cancellationToken);

            // 更新配置状态
            foreach (var result in results)
            {
                if (configs.TryGetValue(result.Key, out var definition))
                {
                    definition.IsAvailable = result.Value.IsAvailable;
                    definition.ValidationStatus = result.Value.Status;
                    definition.ErrorMessage = result.Value.ErrorMessage;
                    definition.LastChecked = result.Value.ValidatedAt;
                }
            }

            // 保存更新后的配置
            await SaveConfigsAsync(configs);

            return results;
        }

        /// <summary>
        /// 获取服务器验证状态摘要
        /// </summary>
        /// <returns>状态摘要</returns>
        public async Task<McpValidationSummary> GetValidationSummaryAsync()
        {
            var configs = await GetConfigsAsync();
            var summary = new McpValidationSummary();

            foreach (var config in configs.Values)
            {
                summary.TotalCount++;
                
                if (config.IsEnabled)
                {
                    summary.EnabledCount++;
                    
                    switch (config.ValidationStatus)
                    {
                        case McpValidationStatus.Available:
                            summary.AvailableCount++;
                            break;
                        case McpValidationStatus.Unavailable:
                        case McpValidationStatus.Timeout:
                        case McpValidationStatus.ConfigurationError:
                            summary.UnavailableCount++;
                            break;
                        case McpValidationStatus.Unknown:
                        case McpValidationStatus.Validating:
                            summary.UnknownCount++;
                            break;
                    }
                }
                else
                {
                    summary.DisabledCount++;
                }
            }

            return summary;
        }

        /// <summary>
        /// 启动指定的MCP服务器
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
        /// 启动指定的MCP服务器并立即验证
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>启动并验证的结果</returns>
        public async Task<(bool Started, McpValidationResult? ValidationResult)> StartServerWithValidationAsync(string name, CancellationToken cancellationToken = default)
        {
            var configs = await GetConfigsAsync();
            if (!configs.TryGetValue(name, out var definition))
            {
                return (false, null);
            }

            // 启动服务器
            var started = await _serverManager.StartServerAsync(name, definition, cancellationToken);
            if (!started)
            {
                return (false, null);
            }

            // 更新配置状态
            definition.IsEnabled = true;
            await SaveConfigsAsync(configs);

            // 立即验证
            var validationResult = await ValidateServerAsync(name, definition, cancellationToken);
            
            return (true, validationResult);
        }

        /// <summary>
        /// 停止指定的MCP服务器
        /// </summary>
        public async Task<bool> StopServerAsync(string name, CancellationToken cancellationToken = default)
        {
            return await _serverManager.StopServerAsync(name, cancellationToken);
        }

        /// <summary>
        /// 启动所有已启用的MCP服务器
        /// </summary>
        public async Task<Dictionary<string, bool>> StartAllServersAsync(CancellationToken cancellationToken = default)
        {
            var configs = await GetConfigsAsync();
            return await _serverManager.StartAllServersAsync(configs, cancellationToken);
        }

        /// <summary>
        /// 停止所有正在运行的MCP服务器
        /// </summary>
        public async Task<Dictionary<string, bool>> StopAllServersAsync(CancellationToken cancellationToken = default)
        {
            return await _serverManager.StopAllServersAsync(cancellationToken);
        }

        /// <summary>
        /// 启动自动验证
        /// </summary>
        public async Task StartAutoValidationAsync(CancellationToken cancellationToken = default)
        {
            var configs = await GetConfigsAsync();
            await _autoValidationService.UpdateConfigurationAsync(configs);
            await _autoValidationService.StartAsync(cancellationToken);
        }

        /// <summary>
        /// 停止自动验证
        /// </summary>
        public async Task StopAutoValidationAsync(CancellationToken cancellationToken = default)
        {
            await _autoValidationService.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 获取正在运行的服务器列表
        /// </summary>
        public IEnumerable<string> GetRunningServers()
        {
            return _serverManager.GetRunningServers();
        }

        /// <summary>
        /// 检查指定服务器是否正在运行
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <returns>是否正在运行</returns>
        public bool IsServerRunning(string name)
        {
            return _serverManager.IsServerRunning(name);
        }

        public void Dispose()
        {
            _validationService?.Dispose();
            _serverManager?.Dispose();
            _autoValidationService?.Dispose();
        }
    }
}