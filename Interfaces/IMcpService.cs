using Lyxie_desktop.Models;
using Lyxie_desktop.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Interfaces
{
    public interface IMcpService : IDisposable
{
    Task<Dictionary<string, McpServerDefinition>> GetConfigsAsync();
    Task SaveConfigsAsync(Dictionary<string, McpServerDefinition> configs);
    
    /// <summary>
    /// 验证单个MCP服务器的可用性
    /// </summary>
    /// <param name="name">服务器名称</param>
    /// <param name="definition">服务器定义</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证结果</returns>
    Task<McpValidationResult> ValidateServerAsync(string name, McpServerDefinition definition, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 验证所有MCP服务器的可用性
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证结果字典</returns>
    Task<Dictionary<string, McpValidationResult>> ValidateAllServersAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取服务器验证状态摘要
    /// </summary>
    /// <returns>状态摘要</returns>
    Task<McpValidationSummary> GetValidationSummaryAsync();
}
} 