using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Interfaces
{
    /// <summary>
    /// MCP服务器管理器接口
    /// </summary>
    public interface IMcpServerManager : IDisposable
    {
        /// <summary>
        /// 启动指定的MCP服务器
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <param name="definition">服务器定义</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>启动是否成功</returns>
        Task<bool> StartServerAsync(string name, Models.McpServerDefinition definition, CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止指定的MCP服务器
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>停止是否成功</returns>
        Task<bool> StopServerAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查服务器是否正在运行
        /// </summary>
        /// <param name="name">服务器名称</param>
        /// <returns>是否正在运行</returns>
        bool IsServerRunning(string name);

        /// <summary>
        /// 获取正在运行的服务器列表
        /// </summary>
        /// <returns>正在运行的服务器名称列表</returns>
        IEnumerable<string> GetRunningServers();

        /// <summary>
        /// 启动所有已启用的MCP服务器
        /// </summary>
        /// <param name="servers">服务器配置字典</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>启动结果字典</returns>
        Task<Dictionary<string, bool>> StartAllServersAsync(Dictionary<string, Models.McpServerDefinition> servers, CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止所有正在运行的MCP服务器
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>停止结果字典</returns>
        Task<Dictionary<string, bool>> StopAllServersAsync(CancellationToken cancellationToken = default);
    }
}