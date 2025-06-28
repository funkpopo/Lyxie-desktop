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

    public McpService()
    {
        _validationService = new McpValidationService();
    }

    public Task<Dictionary<string, McpServerDefinition>> GetConfigsAsync()
    {
        return McpConfigHelper.LoadConfigsAsync();
    }

    public Task SaveConfigsAsync(Dictionary<string, McpServerDefinition> configs)
    {
        return McpConfigHelper.SaveConfigsAsync(configs);
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

    public void Dispose()
    {
        _validationService?.Dispose();
    }
}

    /// <summary>
    /// MCP验证状态摘要
    /// </summary>
    public class McpValidationSummary
    {
        /// <summary>
        /// 总服务器数量
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 已启用服务器数量
        /// </summary>
        public int EnabledCount { get; set; }

        /// <summary>
        /// 已禁用服务器数量
        /// </summary>
        public int DisabledCount { get; set; }

        /// <summary>
        /// 可用服务器数量
        /// </summary>
        public int AvailableCount { get; set; }

        /// <summary>
        /// 不可用服务器数量
        /// </summary>
        public int UnavailableCount { get; set; }

        /// <summary>
        /// 未知状态服务器数量
        /// </summary>
        public int UnknownCount { get; set; }
    }
} 