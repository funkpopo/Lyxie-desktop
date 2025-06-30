using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// MCP工具管理器实现
    /// </summary>
    public class McpToolManager : IMcpToolManager
    {
        private readonly IMcpService _mcpService;
        private readonly IMcpServerManager _serverManager;
        private readonly Dictionary<string, List<McpTool>> _toolsCache = new();
        private readonly object _cacheLock = new object();
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        public event EventHandler<ToolCallStatusEventArgs>? ToolCallStatusChanged;

        public McpToolManager(IMcpService mcpService, IMcpServerManager serverManager)
        {
            _mcpService = mcpService ?? throw new ArgumentNullException(nameof(mcpService));
            _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        }

        /// <summary>
        /// 获取所有可用的MCP工具
        /// </summary>
        public async Task<List<McpTool>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
        {
            // 检查缓存是否有效
            lock (_cacheLock)
            {
                if (DateTime.Now - _lastCacheUpdate < _cacheExpiry && _toolsCache.Count > 0)
                {
                    return _toolsCache.Values.SelectMany(tools => tools).ToList();
                }
            }

            var allTools = new List<McpTool>();
            var configs = await _mcpService.GetConfigsAsync();

            // 获取所有启用且可用的服务器
            var enabledServers = configs.Where(kvp => 
                kvp.Value.IsEnabled && 
                kvp.Value.IsAvailable && 
                _serverManager.IsServerRunning(kvp.Key)).ToList();

            // 并行获取每个服务器的工具
            var tasks = enabledServers.Select(async server =>
            {
                try
                {
                    var tools = await GetServerToolsAsync(server.Key, cancellationToken);
                    return new { ServerName = server.Key, Tools = tools };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"获取服务器 {server.Key} 的工具列表失败: {ex.Message}");
                    return new { ServerName = server.Key, Tools = new List<McpTool>() };
                }
            });

            var results = await Task.WhenAll(tasks);

            // 更新缓存并收集所有工具
            lock (_cacheLock)
            {
                _toolsCache.Clear();
                foreach (var result in results)
                {
                    _toolsCache[result.ServerName] = result.Tools;
                    allTools.AddRange(result.Tools);
                }
                _lastCacheUpdate = DateTime.Now;
            }

            Debug.WriteLine($"获取到 {allTools.Count} 个可用工具，来自 {enabledServers.Count} 个服务器");
            return allTools;
        }

        /// <summary>
        /// 获取指定服务器的工具列表
        /// </summary>
        private async Task<List<McpTool>> GetServerToolsAsync(string serverName, CancellationToken cancellationToken)
        {
            try
            {
                // 构建 tools/list 请求
                var requestId = Guid.NewGuid().ToString();
                var listRequest = new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/list",
                    @params = new { }
                };

                var jsonRequest = JsonConvert.SerializeObject(listRequest);
                var response = await _serverManager.SendRequestAndReadResponseAsync(serverName, jsonRequest, cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    Debug.WriteLine($"服务器 {serverName} 未响应工具列表请求");
                    return new List<McpTool>();
                }

                // 解析响应
                var mcpResponse = JsonConvert.DeserializeObject<dynamic>(response);
                if (mcpResponse?.result != null)
                {
                    var resultJson = JsonConvert.SerializeObject(mcpResponse.result);
                    var toolsResponse = JsonConvert.DeserializeObject<McpToolsListResponse>(resultJson);
                    
                    if (toolsResponse?.Tools != null)
                    {
                        // 设置工具的服务器名称
                        foreach (var tool in toolsResponse.Tools)
                        {
                            tool.ServerName = serverName;
                        }
                        
                        Debug.WriteLine($"服务器 {serverName} 提供了 {toolsResponse.Tools.Count} 个工具");
                        return toolsResponse.Tools;
                    }
                }

                Debug.WriteLine($"服务器 {serverName} 的工具列表响应格式不正确");
                return new List<McpTool>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取服务器 {serverName} 工具列表异常: {ex.Message}");
                return new List<McpTool>();
            }
        }

        /// <summary>
        /// 根据用户消息智能匹配相关工具
        /// </summary>
        public List<McpTool> MatchRelevantTools(string userMessage, List<McpTool> availableTools)
        {
            if (string.IsNullOrWhiteSpace(userMessage) || availableTools.Count == 0)
                return new List<McpTool>();

            var relevantTools = new List<(McpTool Tool, int Score)>();
            var messageLower = userMessage.ToLowerInvariant();

            foreach (var tool in availableTools)
            {
                var score = CalculateRelevanceScore(messageLower, tool);
                if (score > 0)
                {
                    relevantTools.Add((tool, score));
                }
            }

            // 按相关性得分排序，返回前5个最相关的工具
            return relevantTools
                .OrderByDescending(x => x.Score)
                .Take(5)
                .Select(x => x.Tool)
                .ToList();
        }

        /// <summary>
        /// 计算工具与用户消息的相关性得分
        /// </summary>
        private int CalculateRelevanceScore(string messageLower, McpTool tool)
        {
            var score = 0;
            var toolNameLower = tool.Name.ToLowerInvariant();
            var descriptionLower = tool.Description?.ToLowerInvariant() ?? "";

            // 工具名称完全匹配
            if (messageLower.Contains(toolNameLower))
                score += 100;

            // 工具名称部分匹配
            var toolNameWords = toolNameLower.Split('_', '-', ' ');
            foreach (var word in toolNameWords)
            {
                if (messageLower.Contains(word) && word.Length > 2)
                    score += 30;
            }

            // 描述关键词匹配
            if (!string.IsNullOrEmpty(descriptionLower))
            {
                var keywords = ExtractKeywords(descriptionLower);
                foreach (var keyword in keywords)
                {
                    if (messageLower.Contains(keyword) && keyword.Length > 3)
                        score += 20;
                }
            }

            // 常见用途模式匹配
            score += MatchCommonPatterns(messageLower, tool);

            return score;
        }

        /// <summary>
        /// 提取描述中的关键词
        /// </summary>
        private List<string> ExtractKeywords(string description)
        {
            var words = Regex.Split(description, @"\W+")
                .Where(w => w.Length > 3)
                .Select(w => w.ToLowerInvariant())
                .Where(w => !IsStopWord(w))
                .Distinct()
                .ToList();

            return words;
        }

        /// <summary>
        /// 检查是否为停用词
        /// </summary>
        private bool IsStopWord(string word)
        {
            var stopWords = new HashSet<string> { "the", "and", "for", "are", "but", "not", "you", "all", "can", "get", "use", "this", "that", "with", "from", "they", "know", "want", "been", "good", "much", "some", "time", "very", "when", "come", "here", "just", "like", "long", "make", "many", "over", "such", "take", "than", "them", "well", "will" };
            return stopWords.Contains(word);
        }

        /// <summary>
        /// 匹配常见用途模式
        /// </summary>
        private int MatchCommonPatterns(string messageLower, McpTool tool)
        {
            var score = 0;
            var toolNameLower = tool.Name.ToLowerInvariant();

            // 文件操作模式
            if ((messageLower.Contains("文件") || messageLower.Contains("file")) && 
                (toolNameLower.Contains("file") || toolNameLower.Contains("read") || toolNameLower.Contains("write")))
                score += 40;

            // 网络请求模式
            if ((messageLower.Contains("请求") || messageLower.Contains("request") || messageLower.Contains("http")) && 
                (toolNameLower.Contains("request") || toolNameLower.Contains("fetch") || toolNameLower.Contains("http")))
                score += 40;

            // 搜索模式
            if ((messageLower.Contains("搜索") || messageLower.Contains("查找") || messageLower.Contains("search")) && 
                (toolNameLower.Contains("search") || toolNameLower.Contains("find") || toolNameLower.Contains("query")))
                score += 40;

            // 数据处理模式
            if ((messageLower.Contains("数据") || messageLower.Contains("处理") || messageLower.Contains("data")) && 
                (toolNameLower.Contains("data") || toolNameLower.Contains("process") || toolNameLower.Contains("parse")))
                score += 40;

            return score;
        }

        /// <summary>
        /// 调用指定的MCP工具
        /// </summary>
        public async Task<McpToolResult> CallToolAsync(McpToolCall toolCall, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.Now;
            
            // 触发开始事件
            ToolCallStatusChanged?.Invoke(this, new ToolCallStatusEventArgs
            {
                CallId = toolCall.Id,
                ToolName = toolCall.ToolName,
                ServerName = toolCall.ServerName,
                Status = ToolCallStatus.Started,
                Message = $"开始调用工具 {toolCall.ToolName}"
            });

            try
            {
                // 检查服务器是否在运行
                if (!_serverManager.IsServerRunning(toolCall.ServerName))
                {
                    var errorResult = new McpToolResult
                    {
                        CallId = toolCall.Id,
                        IsSuccess = false,
                        ErrorMessage = $"MCP服务器 {toolCall.ServerName} 未运行",
                        Duration = DateTime.Now - startTime
                    };

                    ToolCallStatusChanged?.Invoke(this, new ToolCallStatusEventArgs
                    {
                        CallId = toolCall.Id,
                        ToolName = toolCall.ToolName,
                        ServerName = toolCall.ServerName,
                        Status = ToolCallStatus.Failed,
                        Message = errorResult.ErrorMessage
                    });

                    return errorResult;
                }

                // 构建 tools/call 请求
                var requestId = Guid.NewGuid().ToString();
                var callRequest = new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/call",
                    @params = new McpToolCallRequest
                    {
                        Name = toolCall.ToolName,
                        Arguments = toolCall.Parameters
                    }
                };

                var jsonRequest = JsonConvert.SerializeObject(callRequest);
                
                // 触发进行中事件
                ToolCallStatusChanged?.Invoke(this, new ToolCallStatusEventArgs
                {
                    CallId = toolCall.Id,
                    ToolName = toolCall.ToolName,
                    ServerName = toolCall.ServerName,
                    Status = ToolCallStatus.InProgress,
                    Message = "正在执行工具调用"
                });

                var response = await _serverManager.SendRequestAndReadResponseAsync(
                    toolCall.ServerName, jsonRequest, cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    var timeoutResult = new McpToolResult
                    {
                        CallId = toolCall.Id,
                        IsSuccess = false,
                        ErrorMessage = "工具调用超时或无响应",
                        Duration = DateTime.Now - startTime
                    };

                    ToolCallStatusChanged?.Invoke(this, new ToolCallStatusEventArgs
                    {
                        CallId = toolCall.Id,
                        ToolName = toolCall.ToolName,
                        ServerName = toolCall.ServerName,
                        Status = ToolCallStatus.Timeout,
                        Message = timeoutResult.ErrorMessage
                    });

                    return timeoutResult;
                }

                // 解析响应
                var mcpResponse = JsonConvert.DeserializeObject<dynamic>(response);
                var result = new McpToolResult
                {
                    CallId = toolCall.Id,
                    Duration = DateTime.Now - startTime
                };

                if (mcpResponse?.result != null)
                {
                    var resultJson = JsonConvert.SerializeObject(mcpResponse.result);
                    var toolResponse = JsonConvert.DeserializeObject<McpToolCallResponse>(resultJson);
                    
                    if (toolResponse?.IsError == true)
                    {
                        result.IsSuccess = false;
                        result.ErrorMessage = "工具执行返回错误";
                        result.Content = ExtractErrorContent(toolResponse);
                    }
                    else if (toolResponse != null && toolResponse.Content != null && toolResponse.Content.Count > 0)
                    {
                        result.IsSuccess = true;
                        result.Content = ExtractTextContent(toolResponse.Content);
                    }
                    else
                    {
                        result.IsSuccess = false;
                        result.ErrorMessage = "工具返回空内容";
                    }
                }
                else if (mcpResponse?.error != null)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = mcpResponse.error.message?.ToString() ?? "工具调用出错";
                }
                else
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "工具响应格式不正确";
                }

                // 触发完成事件
                var status = result.IsSuccess ? ToolCallStatus.Completed : ToolCallStatus.Failed;
                ToolCallStatusChanged?.Invoke(this, new ToolCallStatusEventArgs
                {
                    CallId = toolCall.Id,
                    ToolName = toolCall.ToolName,
                    ServerName = toolCall.ServerName,
                    Status = status,
                    Message = result.IsSuccess ? "工具调用成功" : result.ErrorMessage
                });

                return result;
            }
            catch (OperationCanceledException)
            {
                var cancelResult = new McpToolResult
                {
                    CallId = toolCall.Id,
                    IsSuccess = false,
                    ErrorMessage = "工具调用被取消",
                    Duration = DateTime.Now - startTime
                };

                ToolCallStatusChanged?.Invoke(this, new ToolCallStatusEventArgs
                {
                    CallId = toolCall.Id,
                    ToolName = toolCall.ToolName,
                    ServerName = toolCall.ServerName,
                    Status = ToolCallStatus.Failed,
                    Message = cancelResult.ErrorMessage
                });

                return cancelResult;
            }
            catch (Exception ex)
            {
                var exceptionResult = new McpToolResult
                {
                    CallId = toolCall.Id,
                    IsSuccess = false,
                    ErrorMessage = $"工具调用异常: {ex.Message}",
                    Duration = DateTime.Now - startTime
                };

                ToolCallStatusChanged?.Invoke(this, new ToolCallStatusEventArgs
                {
                    CallId = toolCall.Id,
                    ToolName = toolCall.ToolName,
                    ServerName = toolCall.ServerName,
                    Status = ToolCallStatus.Failed,
                    Message = exceptionResult.ErrorMessage
                });

                return exceptionResult;
            }
        }

        /// <summary>
        /// 提取文本内容
        /// </summary>
        private string ExtractTextContent(List<McpToolCallContent> contents)
        {
            var textParts = contents
                .Where(c => c.Type == "text" && !string.IsNullOrEmpty(c.Text))
                .Select(c => c.Text!)
                .ToList();

            return string.Join("\n", textParts);
        }

        /// <summary>
        /// 提取错误内容
        /// </summary>
        private string ExtractErrorContent(McpToolCallResponse response)
        {
            if (response.Content != null && response.Content.Count > 0)
            {
                return ExtractTextContent(response.Content);
            }
            return "未知错误";
        }

        /// <summary>
        /// 批量调用多个工具
        /// </summary>
        public async Task<List<McpToolResult>> CallToolsAsync(List<McpToolCall> toolCalls, CancellationToken cancellationToken = default)
        {
            if (toolCalls.Count == 0)
                return new List<McpToolResult>();

            // 并行执行工具调用，但限制并发数
            var semaphore = new SemaphoreSlim(Math.Min(3, toolCalls.Count));
            var tasks = toolCalls.Select(async toolCall =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await CallToolAsync(toolCall, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        /// <summary>
        /// 为用户消息生成工具调用上下文
        /// </summary>
        public async Task<McpToolContext> GenerateToolContextAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            var context = new McpToolContext();

            try
            {
                // 获取所有可用工具
                context.AvailableTools = await GetAvailableToolsAsync(cancellationToken);
                
                if (context.AvailableTools.Count == 0)
                {
                    Debug.WriteLine("没有可用的MCP工具");
                    return context;
                }

                // 匹配相关工具
                var relevantTools = MatchRelevantTools(userMessage, context.AvailableTools);
                
                if (relevantTools.Count == 0)
                {
                    Debug.WriteLine("没有找到与用户消息相关的工具");
                    return context;
                }

                // 为相关工具创建调用请求（这里简化处理，实际应用中可能需要更复杂的参数推断）
                foreach (var tool in relevantTools)
                {
                    var toolCall = new McpToolCall
                    {
                        ServerName = tool.ServerName,
                        ToolName = tool.Name,
                        Parameters = GenerateToolParameters(tool, userMessage)
                    };
                    context.PendingCalls.Add(toolCall);
                }

                // 执行工具调用
                if (context.PendingCalls.Count > 0)
                {
                    Debug.WriteLine($"准备调用 {context.PendingCalls.Count} 个相关工具");
                    context.Results = await CallToolsAsync(context.PendingCalls, cancellationToken);
                }

                return context;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"生成工具调用上下文异常: {ex.Message}");
                return context;
            }
        }

        /// <summary>
        /// 为工具生成参数（简化实现）
        /// </summary>
        private Dictionary<string, object>? GenerateToolParameters(McpTool tool, string userMessage)
        {
            // 这里是简化的参数生成逻辑
            // 实际应用中可能需要更复杂的自然语言处理来提取参数
            var parameters = new Dictionary<string, object>();

            // 如果工具需要查询参数，使用用户消息作为查询
            if (tool.InputSchema?.Properties != null && tool.InputSchema.Properties.ContainsKey("query"))
            {
                parameters["query"] = userMessage;
            }

            // 如果工具需要文本参数，使用用户消息
            if (tool.InputSchema?.Properties != null && tool.InputSchema.Properties.ContainsKey("text"))
            {
                parameters["text"] = userMessage;
            }

            return parameters.Count > 0 ? parameters : null;
        }

        /// <summary>
        /// 检查MCP服务器是否支持工具调用
        /// </summary>
        public async Task<bool> IsToolsSupportedAsync(string serverName, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_serverManager.IsServerRunning(serverName))
                    return false;

                var tools = await GetServerToolsAsync(serverName, cancellationToken);
                return tools.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            lock (_cacheLock)
            {
                _toolsCache.Clear();
            }
        }
    }
} 