using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lyxie_desktop.Views;
using Lyxie_desktop.Models;

namespace Lyxie_desktop.Interfaces;

/// <summary>
/// 流式数据接收回调委托
/// </summary>
/// <param name="content">接收到的内容片段</param>
/// <param name="isComplete">是否为完整消息结束</param>
public delegate void StreamingDataCallback(string content, bool isComplete);

/// <summary>
/// 流式错误回调委托
/// </summary>
/// <param name="error">错误信息</param>
public delegate void StreamingErrorCallback(string error);

/// <summary>
/// LLM响应回调委托（支持工具调用）
/// </summary>
/// <param name="response">LLM响应对象</param>
/// <param name="isComplete">是否为完整响应结束</param>
public delegate void LlmResponseCallback(LlmResponse response, bool isComplete);

public interface ILlmApiService : IDisposable
{
    /// <summary>
    /// 测试LLM API连接
    /// </summary>
    Task<(bool Success, string Message)> TestApiAsync(LlmApiConfig config);
    
    /// <summary>
    /// 发送流式消息请求
    /// </summary>
    /// <param name="config">LLM API配置</param>
    /// <param name="message">用户消息</param>
    /// <param name="onDataReceived">数据接收回调</param>
    /// <param name="onError">错误回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功启动流式请求</returns>
    Task<bool> SendStreamingMessageAsync(
        LlmApiConfig config,
        string message,
        StreamingDataCallback onDataReceived,
        StreamingErrorCallback? onError = null,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// 发送流式消息请求（支持工具调用上下文）
    /// </summary>
    Task<bool> SendStreamingMessageAsync(
        LlmApiConfig config,
        string message,
        string? toolContext,
        StreamingDataCallback onDataReceived,
        StreamingErrorCallback? onError = null,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// 发送流式消息请求（支持工具定义和工具调用结果）
    /// </summary>
    /// <param name="config">LLM API配置</param>
    /// <param name="message">用户消息</param>
    /// <param name="availableTools">可用的工具定义列表</param>
    /// <param name="toolResults">工具调用结果文本</param>
    /// <param name="onDataReceived">数据接收回调</param>
    /// <param name="onError">错误回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功启动流式请求</returns>
    Task<bool> SendStreamingMessageAsync(
        LlmApiConfig config,
        string message,
        List<McpTool>? availableTools,
        string? toolResults,
        StreamingDataCallback onDataReceived,
        StreamingErrorCallback? onError = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送对话消息请求（支持完整的函数调用模式）
    /// </summary>
    /// <param name="config">LLM API配置</param>
    /// <param name="messages">对话消息历史</param>
    /// <param name="availableTools">可用的工具定义列表</param>
    /// <param name="onLlmResponse">LLM响应回调</param>
    /// <param name="onError">错误回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功启动请求</returns>
    Task<bool> SendConversationAsync(
        LlmApiConfig config,
        List<ConversationMessage> messages,
        List<McpTool>? availableTools,
        LlmResponseCallback onLlmResponse,
        StreamingErrorCallback? onError = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送非流式对话消息请求（获取完整响应）
    /// </summary>
    /// <param name="config">LLM API配置</param>
    /// <param name="messages">对话消息历史</param>
    /// <param name="availableTools">可用的工具定义列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>LLM完整响应</returns>
    Task<LlmResponse?> SendConversationAndGetResponseAsync(
        LlmApiConfig config,
        List<ConversationMessage> messages,
        List<McpTool>? availableTools,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否支持function call（函数调用/工具调用）能力，默认true。
    /// </summary>
    bool SupportsFunctionCall { get; }
} 