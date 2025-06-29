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
    
    /// <summary>
    /// 启动指定的MCP服务器
    /// </summary>
    /// <param name="name">服务器名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>启动是否成功</returns>
    Task<bool> StartServerAsync(string name, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 停止指定的MCP服务器
    /// </summary>
    /// <param name="name">服务器名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>停止是否成功</returns>
    Task<bool> StopServerAsync(string name, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 启动所有已启用的MCP服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>启动结果字典</returns>
    Task<Dictionary<string, bool>> StartAllServersAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 停止所有正在运行的MCP服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>停止结果字典</returns>
    Task<Dictionary<string, bool>> StopAllServersAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 启动自动验证
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>启动任务</returns>
    Task StartAutoValidationAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 停止自动验证
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>停止任务</returns>
    Task StopAutoValidationAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 自动验证是否正在运行
    /// </summary>
    bool IsAutoValidationRunning { get; }
    
    /// <summary>
    /// 获取正在运行的服务器列表
    /// </summary>
    /// <returns>正在运行的服务器名称列表</returns>
    IEnumerable<string> GetRunningServers();

    IMcpAutoValidationService AutoValidationService { get; }
}
} 