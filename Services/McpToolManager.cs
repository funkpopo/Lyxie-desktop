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

        // 工具调用去重缓存
        private readonly Dictionary<string, DateTime> _recentToolCalls = new();
        private readonly object _deduplicationLock = new object();
        private readonly TimeSpan _deduplicationWindow = TimeSpan.FromSeconds(5); // 5秒内的相同调用视为重复

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
                    Debug.WriteLine($"使用缓存中的工具，数量：{_toolsCache.Values.SelectMany(tools => tools).Count()}");
                    return _toolsCache.Values.SelectMany(tools => tools).ToList();
                }
            }

            var allTools = new List<McpTool>();
            var configs = await _mcpService.GetConfigsAsync();
            
            Debug.WriteLine($"开始获取可用工具，配置数量：{configs.Count}");

            // 获取所有启用且可用的服务器
            var enabledServers = configs.Where(kvp => 
                kvp.Value.IsEnabled).ToList();
            
            Debug.WriteLine($"找到 {enabledServers.Count} 个启用的服务器");

            foreach (var server in enabledServers)
            {
                var serverName = server.Key;
                var tools = await GetServerToolsAsync(serverName, cancellationToken);
                if (tools != null && tools.Count > 0)
                {
                    allTools.AddRange(tools);
                }
            }

            // 更新缓存
            lock (_cacheLock)
            {
                _toolsCache.Clear();
                foreach (var server in enabledServers)
                {
                    var serverName = server.Key;
                    var tools = allTools.Where(t => t.ServerName == serverName).ToList();
                    _toolsCache[serverName] = tools;
                }
                _lastCacheUpdate = DateTime.Now;
            }

            Debug.WriteLine($"获取到总共 {allTools.Count} 个可用工具，来自 {_toolsCache.Count} 个服务器");
            return allTools;
        }

        /// <summary>
        /// 获取指定服务器的工具列表
        /// </summary>
        private async Task<List<McpTool>> GetServerToolsAsync(string serverName, CancellationToken cancellationToken)
        {
            try
            {            
                // 检查服务器是否在运行
                if (!_serverManager.IsServerRunning(serverName))
                {
                    Debug.WriteLine($"服务器 {serverName} 未运行，无法获取工具列表");
                    return new List<McpTool>();
                }
            
                // 构建 tools/list 请求
                var requestId = Guid.NewGuid().ToString();
                var listRequest = new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/list",
                    @params = new { }
                };

                Debug.WriteLine($"向服务器 {serverName} 发送工具列表请求...");
                var jsonRequest = JsonConvert.SerializeObject(listRequest);
                var response = await _serverManager.SendRequestAndReadResponseAsync(serverName, jsonRequest, cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    Debug.WriteLine($"服务器 {serverName} 未响应工具列表请求");
                    return new List<McpTool>();
                }

                Debug.WriteLine($"收到服务器 {serverName} 的响应: {response.Substring(0, Math.Min(100, response.Length))}...");

                // 解析响应
                var mcpResponse = JsonConvert.DeserializeObject<dynamic>(response);
                if (mcpResponse?.result == null)
                {
                    Debug.WriteLine($"服务器 {serverName} 的响应中不包含result对象");
                    return new List<McpTool>();
                }
                
                try
                {
                    var resultJson = JsonConvert.SerializeObject(mcpResponse.result);

                    // 1. 尝试直接解析标准格式 { "tools": [...] }
                    var toolsResponse = JsonConvert.DeserializeObject<McpToolsListResponse>(resultJson);
                    if (toolsResponse?.Tools != null && toolsResponse.Tools.Count > 0)
                    {
                        var validTools = new List<McpTool>();
                        for (int i = 0; i < toolsResponse.Tools.Count; i++)
                        {
                            var tool = toolsResponse.Tools[i];
                            if (tool != null)
                            {
                                tool.ServerName = serverName;
                                validTools.Add(tool);
                            }
                        }
                        Debug.WriteLine($"服务器 {serverName} 提供了 {validTools.Count} 个工具（标准格式）");
                        return validTools;
                    }

                    // 2. 尝试解析为根对象就是工具列表的格式 [...]
                    var toolsList = JsonConvert.DeserializeObject<List<McpTool>>(resultJson);
                    if (toolsList != null && toolsList.Count > 0)
                    {
                        var validTools = new List<McpTool>();
                        for (int i = 0; i < toolsList.Count; i++)
                        {
                            var tool = toolsList[i];
                            if (tool != null)
                            {
                                tool.ServerName = serverName;
                                validTools.Add(tool);
                            }
                        }
                        Debug.WriteLine($"服务器 {serverName} 提供了 {validTools.Count} 个工具（列表格式）");
                        return validTools;
                    }

                    // 3. 尝试解析嵌套格式，如 { "some_key": { "tools": [...] } } 或 { "capabilities": { "tools": {...} } }
                    var resultObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultJson);
                    if (resultObj != null)
                    {
                        // a. 直接在根寻找 "tools"
                        if (resultObj.TryGetValue("tools", out object? toolsValue) && toolsValue != null)
                        {
                            var tools = JsonConvert.DeserializeObject<List<McpTool>>(JsonConvert.SerializeObject(toolsValue));
                            if (tools != null && tools.Count > 0)
                            {
                                var validTools = new List<McpTool>();
                                for (int i = 0; i < tools.Count; i++)
                                {
                                    var tool = tools[i];
                                    if (tool != null)
                                    {
                                        tool.ServerName = serverName;
                                        validTools.Add(tool);
                                    }
                                }
                                Debug.WriteLine($"服务器 {serverName} 提供了 {validTools.Count} 个工具（嵌套列表格式）");
                                return validTools;
                            }
                        }

                        // b. 在 "capabilities" 中寻找 "tools"
                        if (resultObj.TryGetValue("capabilities", out object? capsValue) && capsValue != null)
                        {
                            var capsJson = JsonConvert.SerializeObject(capsValue);
                            var caps = JsonConvert.DeserializeObject<Dictionary<string, object>>(capsJson);

                            if (caps != null && caps.TryGetValue("tools", out object? toolsObj) && toolsObj != null)
                            {
                                var toolsJson = JsonConvert.SerializeObject(toolsObj);
                                var toolsDict = JsonConvert.DeserializeObject<Dictionary<string, McpTool>>(toolsJson);

                                if (toolsDict != null && toolsDict.Count > 0)
                                {
                                    var extractedTools = new List<McpTool>();
                                    foreach (var kvp in toolsDict)
                                    {
                                        if (kvp.Value != null)
                                        {
                                            var tool = kvp.Value;
                                            tool.Name = kvp.Key;
                                            tool.ServerName = serverName;
                                            extractedTools.Add(tool);
                                        }
                                    }
                                    Debug.WriteLine($"服务器 {serverName} 提供了 {extractedTools.Count} 个工具（映射格式）");
                                    return extractedTools;
                                }
                            }
                        }
                    }

                    Debug.WriteLine($"服务器 {serverName} 的工具列表响应格式无法识别或为空");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"解析服务器 {serverName} 的工具列表失败: {ex.Message}");
                }

                return new List<McpTool>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取服务器 {serverName} 工具列表的整体过程发生异常: {ex.Message}");
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

            Debug.WriteLine($"开始匹配相关工具，用户消息: '{userMessage}'");
            Debug.WriteLine($"可用工具数量: {availableTools.Count}");

            foreach (var tool in availableTools)
            {
                var score = CalculateRelevanceScore(messageLower, tool);
                Debug.WriteLine($"工具 '{tool.Name}' 相关性得分: {score}");

                // 降低阈值，让更多工具有机会被选中
                if (score > 0)
                {
                    relevantTools.Add((tool, score));
                }
            }

            // 按相关性得分排序，返回前8个最相关的工具（增加数量）
            var selectedTools = relevantTools
                .OrderByDescending(x => x.Score)
                .Take(8)
                .Select(x => x.Tool)
                .ToList();

            Debug.WriteLine($"匹配到 {selectedTools.Count} 个相关工具:");
            foreach (var tool in selectedTools)
            {
                Debug.WriteLine($"  - {tool.Name}: {tool.Description}");
            }

            return selectedTools;
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

            // 通用动作词匹配（增加基础分数）
            var actionWords = new[] { "获取", "查询", "搜索", "查找", "创建", "生成", "执行", "运行", "处理", "分析", "读取", "写入", "下载", "上传" };
            foreach (var action in actionWords)
            {
                if (messageLower.Contains(action))
                {
                    score += 15; // 任何动作词都增加基础分数
                }
            }

            // 问号表示查询意图
            if (messageLower.Contains("?") || messageLower.Contains("？"))
            {
                score += 10;
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

            // 检查是否为重复调用
            var deduplicationKey = GenerateDeduplicationKey(toolCall);
            lock (_deduplicationLock)
            {
                if (_recentToolCalls.TryGetValue(deduplicationKey, out var lastCallTime))
                {
                    if (DateTime.Now - lastCallTime < _deduplicationWindow)
                    {
                        Debug.WriteLine($"检测到重复工具调用，跳过: {toolCall.ToolName}");
                        return new McpToolResult
                        {
                            CallId = toolCall.Id,
                            IsSuccess = false,
                            ErrorMessage = "重复的工具调用已被跳过",
                            Duration = TimeSpan.Zero
                        };
                    }
                }

                // 记录此次调用
                _recentToolCalls[deduplicationKey] = DateTime.Now;

                // 清理过期的记录
                var expiredKeys = _recentToolCalls
                    .Where(kvp => DateTime.Now - kvp.Value > _deduplicationWindow)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _recentToolCalls.Remove(key);
                }
            }

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
                Debug.WriteLine($"=== 开始工具调用前的服务器状态验证 ===");
                Debug.WriteLine($"工具: {toolCall.ToolName}, 服务器: {toolCall.ServerName}");

                // 多次检查服务器状态，确保可靠性
                bool isServerRunning = false;
                int maxRetries = 3;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    Debug.WriteLine($"第 {attempt} 次检查服务器 {toolCall.ServerName} 状态...");
                    isServerRunning = _serverManager.IsServerRunning(toolCall.ServerName);

                    if (isServerRunning)
                    {
                        Debug.WriteLine($"第 {attempt} 次检查: 服务器 {toolCall.ServerName} 确认运行中");
                        break;
                    }
                    else
                    {
                        Debug.WriteLine($"第 {attempt} 次检查: 服务器 {toolCall.ServerName} 未运行");

                        if (attempt < maxRetries)
                        {
                            Debug.WriteLine($"等待100ms后重试...");
                            await Task.Delay(100);
                        }
                    }
                }

                if (!isServerRunning)
                {
                    Debug.WriteLine($"=== 经过 {maxRetries} 次检查，服务器 {toolCall.ServerName} 确认未运行，工具调用失败 ===");

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

                Debug.WriteLine($"=== 服务器 {toolCall.ServerName} 状态验证通过，继续工具调用 ===");

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

                    if (toolResponse == null)
                    {
                        result.IsSuccess = false;
                        result.ErrorMessage = "工具返回无法解析的内容";
                    }
                    else if (toolResponse.IsError == true)
                    {
                        result.IsSuccess = false;
                        result.ErrorMessage = "工具执行返回错误";
                        result.Content = ExtractErrorContent(toolResponse);
                    }
                    else if (toolResponse.Content != null && toolResponse.Content.Count > 0)
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
        /// 生成工具调用去重键
        /// </summary>
        private string GenerateDeduplicationKey(McpToolCall toolCall)
        {
            // 基于工具名称、服务器名称和参数生成唯一键
            var parametersJson = toolCall.Parameters != null
                ? JsonConvert.SerializeObject(toolCall.Parameters, Formatting.None)
                : "";

            var keyData = $"{toolCall.ServerName}:{toolCall.ToolName}:{parametersJson}";

            // 使用简单的哈希来缩短键长度
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyData));
                return Convert.ToBase64String(hashBytes).Substring(0, 16); // 取前16个字符
            }
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
            Debug.WriteLine($"开始为消息生成工具调用上下文: {userMessage.Substring(0, Math.Min(50, userMessage.Length))}...");

            try
            {
                // 获取所有可用工具
                Debug.WriteLine("获取可用工具列表...");
                context.AvailableTools = await GetAvailableToolsAsync(cancellationToken);
                
                if (context.AvailableTools.Count == 0)
                {
                    Debug.WriteLine("没有可用的MCP工具");
                    return context;
                }

                Debug.WriteLine($"获取到 {context.AvailableTools.Count} 个可用工具，准备匹配相关工具...");
                
                // 匹配相关工具
                var relevantTools = MatchRelevantTools(userMessage, context.AvailableTools);
                
                if (relevantTools.Count == 0)
                {
                    Debug.WriteLine("没有找到与用户消息相关的工具");
                    return context;
                }

                Debug.WriteLine($"匹配到 {relevantTools.Count} 个相关工具: {string.Join(", ", relevantTools.Select(t => t.Name))}");
                
                // 为相关工具创建调用请求
                foreach (var tool in relevantTools)
                {
                    var parameters = GenerateToolParameters(tool, userMessage);
                    Debug.WriteLine($"为工具 {tool.Name} 生成参数: {(parameters != null ? JsonConvert.SerializeObject(parameters) : "null")}");
                    
                    var toolCall = new McpToolCall
                    {
                        ServerName = tool.ServerName,
                        ToolName = tool.Name,
                        Parameters = parameters
                    };
                    context.PendingCalls.Add(toolCall);
                }

                // 执行工具调用
                if (context.PendingCalls.Count > 0)
                {
                    Debug.WriteLine($"准备调用 {context.PendingCalls.Count} 个相关工具");
                    context.Results = await CallToolsAsync(context.PendingCalls, cancellationToken);
                    
                    Debug.WriteLine($"工具调用完成，获得 {context.Results.Count} 个结果");
                    foreach (var result in context.Results)
                    {
                        if (result.IsSuccess)
                        {
                            Debug.WriteLine($"工具 {context.PendingCalls.FirstOrDefault(c => c.Id == result.CallId)?.ToolName} 调用成功，内容长度: {(result.Content?.Length ?? 0)}");
                        }
                        else
                        {
                            Debug.WriteLine($"工具 {context.PendingCalls.FirstOrDefault(c => c.Id == result.CallId)?.ToolName} 调用失败: {result.ErrorMessage}");
                        }
                    }
                }

                return context;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"生成工具调用上下文异常: {ex.Message}");
                Debug.WriteLine($"异常详情: {ex}");
                return context;
            }
        }

        /// <summary>
        /// 为工具生成参数
        /// </summary>
        private Dictionary<string, object>? GenerateToolParameters(McpTool tool, string userMessage)
        {
            var parameters = new Dictionary<string, object>();
            var messageLower = userMessage.ToLowerInvariant();

            // 如果是文件系统工具，进行专门处理
            if (tool.ServerName.ToLower().Contains("filesystem") || 
                tool.ServerName.ToLower().Contains("file"))
            {
                // 根据工具名称和用户消息生成参数
                switch (tool.Name.ToLower())
                {
                    case "list_directory":
                        // 尝试从用户消息中提取路径
                        string? dirPath = ExtractPathFromMessage(messageLower);
                        if (!string.IsNullOrEmpty(dirPath))
                        {
                            parameters["path"] = dirPath;
                        }
                        else
                        {
                            // 默认使用当前路径
                            parameters["path"] = ".";
                        }
                        return parameters;

                    case "read_file":
                        string? filePath = ExtractPathFromMessage(messageLower);
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            parameters["path"] = filePath;
                            return parameters;
                        }
                        break;

                    case "get_current_directory":
                        // 不需要参数
                        return parameters;

                    case "path_exists":
                        string? pathToCheck = ExtractPathFromMessage(messageLower);
                        if (!string.IsNullOrEmpty(pathToCheck))
                        {
                            parameters["path"] = pathToCheck;
                            return parameters;
                        }
                        break;

                    case "get_file_info":
                        string? fileInfoPath = ExtractPathFromMessage(messageLower);
                        if (!string.IsNullOrEmpty(fileInfoPath))
                        {
                            parameters["path"] = fileInfoPath;
                            return parameters;
                        }
                        break;
                }
            }

            // 通用参数处理逻辑
            if (tool.InputSchema?.Properties != null)
            {
                // 如果工具需要查询参数，使用用户消息作为查询
                if (tool.InputSchema.Properties.ContainsKey("query"))
                {
                    parameters["query"] = userMessage;
                }

                // 如果工具需要文本参数，使用用户消息
                if (tool.InputSchema.Properties.ContainsKey("text"))
                {
                    parameters["text"] = userMessage;
                }

                // 如果工具需要路径参数但未设置，尝试提取
                if (tool.InputSchema.Properties.ContainsKey("path") && !parameters.ContainsKey("path"))
                {
                    string? path = ExtractPathFromMessage(messageLower);
                    if (!string.IsNullOrEmpty(path))
                    {
                        parameters["path"] = path;
                    }
                    else
                    {
                        // 默认使用当前路径
                        parameters["path"] = ".";
                    }
                }
            }

            return parameters.Count > 0 ? parameters : null;
        }

        /// <summary>
        /// 从用户消息中提取路径
        /// </summary>
        private string? ExtractPathFromMessage(string message)
        {
            // 常见的路径引导词
            string[] pathIndicators = {
                "查看", "路径", "目录", "文件夹", "文件", 
                "path", "directory", "folder", "file", 
                "查询", "读取", "check", "read", "list"
            };
            
            // 拆分消息为单词
            var words = message.Split(new char[] { ' ', ':', '，', ',', '。', '？', '?', '\n', '\r', '\t' }, 
                                        StringSplitOptions.RemoveEmptyEntries);
            
            // 查找路径指示词后面的可能路径
            for (int i = 0; i < words.Length - 1; i++)
            {
                foreach (var indicator in pathIndicators)
                {
                    if (words[i].Contains(indicator))
                    {
                        // 检查下一个单词是否可能是路径
                        string potentialPath = words[i + 1];
                        
                        // 如果是引号包裹的路径
                        if (potentialPath.StartsWith("\"") && potentialPath.EndsWith("\""))
                        {
                            return potentialPath.Trim('"');
                        }
                        
                        // 如果是单引号包裹的路径
                        if (potentialPath.StartsWith("'") && potentialPath.EndsWith("'"))
                        {
                            return potentialPath.Trim('\'');
                        }
                        
                        // 检查是否是明显的路径格式
                        if (IsLikelyPath(potentialPath))
                        {
                            return potentialPath;
                        }
                    }
                }
            }
            
            // 找不到明确的路径，检查是否有默认路径
            foreach (var word in words)
            {
                if (IsLikelyPath(word))
                {
                    return word;
                }
            }
            
            // 如果消息中提到"当前"，返回当前目录
            if (message.Contains("当前") || message.Contains("current"))
            {
                return ".";
            }
            
            return null;
        }
        
        /// <summary>
        /// 判断字符串是否可能是路径
        /// </summary>
        private bool IsLikelyPath(string text)
        {
            // 检查是否包含路径分隔符
            if (text.Contains("/") || text.Contains("\\"))
                return true;
                
            // 检查是否包含盘符格式 (如 C:)
            if (text.Length >= 2 && char.IsLetter(text[0]) && text[1] == ':')
                return true;
                
            // 检查是否是相对路径指示符
            if (text == "." || text == ".." || text.StartsWith("./") || text.StartsWith("../"))
                return true;
                
            return false;
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
