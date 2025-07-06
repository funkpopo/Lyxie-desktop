using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Lyxie_desktop.Views; // 添加对LlmApiConfig所在命名空间的引用
using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;

namespace Lyxie_desktop.Services
{
    public class LlmApiService : ILlmApiService
    {
        private readonly HttpClient _httpClient;

        public LlmApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60); // 增加超时时间以支持流式响应
        }

        /// <summary>
        /// 测试LLM API是否可用，发送简单的"ping"消息
        /// </summary>
        /// <param name="config">LLM API配置</param>
        /// <returns>测试结果和消息</returns>
        public async Task<(bool Success, string Message)> TestApiAsync(LlmApiConfig config)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.ApiUrl))
                    return (false, "API URL不能为空");

                if (string.IsNullOrWhiteSpace(config.ApiKey))
                    return (false, "API Key不能为空");

                // 克隆HttpClient以便能设置不同的请求头
                using var httpClient = new HttpClient();
                httpClient.Timeout = _httpClient.Timeout;
                
                // 设置请求头
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

                // 构建简单的请求消息
                var requestData = new
                {
                    model = config.ModelName,
                    messages = new[]
                    {
                        new { role = "user", content = "ping" }
                    },
                    temperature = config.Temperature,
                    max_tokens = Math.Min(20, config.MaxTokens) // 测试时限制token数量
                };

                // 转换为JSON
                var jsonContent = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 发送请求
                var response = await httpClient.PostAsync(config.ApiUrl, content);

                // 检查响应状态
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return (true, "API连接成功");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return (false, $"API返回错误状态码 {(int)response.StatusCode}: {errorContent}");
                }
            }
            catch (HttpRequestException ex)
            {
                // 使用错误处理管理器处理网络错误
                if (App.ErrorHandlingManager != null)
                {
                    try
                    {
                        var errorResult = await App.ErrorHandlingManager.HandleErrorAsync(
                            ex,
                            "LLM API连接测试",
                            "llm_api"
                        );
                        return (false, errorResult.UserMessage);
                    }
                    catch
                    {
                        // 如果错误处理器失败，使用默认消息
                    }
                }
                return (false, $"网络请求错误: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, "请求超时");
            }
            catch (Exception ex)
            {
                // 使用错误处理管理器处理未知错误
                if (App.ErrorHandlingManager != null)
                {
                    try
                    {
                        var errorResult = await App.ErrorHandlingManager.HandleErrorAsync(
                            ex,
                            "LLM API连接测试",
                            "llm_api"
                        );
                        return (false, errorResult.UserMessage);
                    }
                    catch
                    {
                        // 如果错误处理器失败，使用默认消息
                    }
                }
                return (false, $"未知错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送流式消息请求
        /// </summary>
        public async Task<bool> SendStreamingMessageAsync(
            LlmApiConfig config,
            string message,
            StreamingDataCallback onDataReceived,
            StreamingErrorCallback? onError = null,
            CancellationToken cancellationToken = default)
        {
            return await SendStreamingMessageAsync(config, message, null, onDataReceived, onError, cancellationToken);
        }

        /// <summary>
        /// 发送流式消息请求（支持工具调用上下文）
        /// </summary>
        public async Task<bool> SendStreamingMessageAsync(
            LlmApiConfig config,
            string message,
            string? toolContext,
            StreamingDataCallback onDataReceived,
            StreamingErrorCallback? onError = null,
            CancellationToken cancellationToken = default)
        {
            return await SendStreamingMessageAsync(config, message, null, toolContext, onDataReceived, onError, cancellationToken);
        }

        /// <summary>
        /// 发送流式消息请求（支持工具定义和工具调用结果）
        /// </summary>
        public async Task<bool> SendStreamingMessageAsync(
            LlmApiConfig config,
            string message,
            List<McpTool>? availableTools,
            string? toolResults,
            StreamingDataCallback onDataReceived,
            StreamingErrorCallback? onError = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    onError?.Invoke("API配置不完整");
                    return false;
                }

                // 构建流式请求数据
                var messages = new List<object>();
                
                // 如果有工具调用结果，添加系统消息
                if (!string.IsNullOrEmpty(toolResults))
                {
                    System.Diagnostics.Debug.WriteLine($"添加工具调用结果到LLM请求，长度: {toolResults.Length}");
                    messages.Add(new { role = "system", content = $"以下是相关的工具调用结果，请基于这些信息回答用户的问题：\n\n{toolResults}" });
                }
                
                messages.Add(new { role = "user", content = message });

                // 准备请求对象
                object requestData;

                // 如果有可用工具，添加工具定义
                if (availableTools != null && availableTools.Count > 0)
                {
                    // 转换MCP工具到OpenAI工具格式
                    var openAITools = ConvertToOpenAIToolFormat(availableTools);
                    
                    System.Diagnostics.Debug.WriteLine($"添加 {openAITools.Count} 个工具定义到LLM请求");
                    
                    // 打印工具名称，方便调试
                    foreach (var tool in openAITools)
                    {
                        try
                        {
                            var functionObj = (dynamic)((dynamic)tool).function;
                            var name = (string)functionObj.name;
                            System.Diagnostics.Debug.WriteLine($"  - 工具: {name}");
                        }
                        catch 
                        {
                            // 忽略错误
                        }
                    }
                    
                    requestData = new
                    {
                        model = config.ModelName,
                        messages = messages.ToArray(),
                        temperature = config.Temperature,
                        max_tokens = config.MaxTokens,
                        stream = true, // 启用流式响应
                        tools = openAITools.ToArray()
                    };
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("LLM请求中不包含工具定义");
                    
                    requestData = new
                    {
                        model = config.ModelName,
                        messages = messages.ToArray(),
                        temperature = config.Temperature,
                        max_tokens = config.MaxTokens,
                        stream = true // 启用流式响应
                    };
                }

                var jsonContent = JsonConvert.SerializeObject(requestData);
                System.Diagnostics.Debug.WriteLine($"LLM请求JSON: {jsonContent.Substring(0, Math.Min(500, jsonContent.Length))}...");
                
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 创建请求消息
                var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl)
                {
                    Content = content
                };
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
                request.Headers.Add("Accept", "text/event-stream");

                System.Diagnostics.Debug.WriteLine($"发送LLM请求到 {config.ApiUrl}");
                
                // 发送请求并获取响应流
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"LLM API错误 {(int)response.StatusCode}: {errorContent}");
                    onError?.Invoke($"API错误 {(int)response.StatusCode}: {errorContent}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("LLM API连接成功，开始读取流式响应");
                
                // 读取流式数据
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var fullContent = new StringBuilder();
                string? line;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 跳过空行
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // 处理SSE格式数据 (data: {...})
                    if (line.StartsWith("data: "))
                    {
                        var jsonData = line.Substring(6).Trim();
                        
                        // 检查是否为结束标记
                        if (jsonData == "[DONE]")
                        {
                            System.Diagnostics.Debug.WriteLine("收到LLM流式响应结束标记");
                            onDataReceived.Invoke("", true);
                            break;
                        }

                        try
                        {
                            // 解析JSON数据
                            var jsonObject = JObject.Parse(jsonData);
                            var choices = jsonObject["choices"] as JArray;
                            
                            if (choices != null && choices.Count > 0)
                            {
                                var firstChoice = choices[0];
                                var delta = firstChoice["delta"] as JObject;
                                
                                if (delta != null)
                                {
                                    // 处理文本内容
                                    if (delta.ContainsKey("content"))
                                    {
                                        var contentChunk = delta["content"]?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(contentChunk))
                                        {
                                            fullContent.Append(contentChunk);
                                            onDataReceived.Invoke(contentChunk, false);
                                        }
                                    }

                                    // 处理工具调用
                                    var toolCalls = delta["tool_calls"] as JArray;
                                    if (toolCalls != null && toolCalls.Count > 0)
                                    {
                                        foreach (var toolCall in toolCalls)
                                        {
                                            if (toolCall == null) continue;
                                            // 工具调用不在流式响应中直接处理
                                            System.Diagnostics.Debug.WriteLine($"检测到工具调用: {toolCall}");
                                        }
                                    }
                                }
                                
                                // 检查是否完成
                                var finishReason = firstChoice["finish_reason"];
                                if (finishReason != null && !string.IsNullOrEmpty(finishReason.ToString()))
                                {
                                    System.Diagnostics.Debug.WriteLine($"流式响应完成，原因: {finishReason}");
                                    onDataReceived.Invoke("", true);
                                    break;
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"解析流式数据错误: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"错误的JSON数据: {jsonData}");
                            // 继续处理下一行，不中断整个流程
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"LLM流式响应完成，总内容长度: {fullContent.Length}");
                return true;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("LLM请求被用户取消");
                // 用户取消，正常情况
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LLM流式请求错误: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"错误详情: {ex}");
                onError?.Invoke($"流式请求错误: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 将MCP工具定义转换为OpenAI工具格式
        /// </summary>
        private List<object> ConvertToOpenAIToolFormat(List<McpTool> mcpTools)
        {
            var openAITools = new List<object>();
            System.Diagnostics.Debug.WriteLine($"开始转换 {mcpTools.Count} 个MCP工具到OpenAI格式");
            
            foreach (var tool in mcpTools)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"转换工具: {tool.Name} (服务器: {tool.ServerName})");
                    
                    // 如果有InputSchema，使用其值；否则创建一个基本的参数对象
                    object parameters;
                    
                    if (tool.InputSchema != null)
                    {
                        // 深度处理InputSchema，确保格式正确
                        var properties = new Dictionary<string, object>();
                        
                        if (tool.InputSchema.Properties != null)
                        {
                            foreach (var prop in tool.InputSchema.Properties)
                            {
                                if (prop.Value == null) continue;

                                var propObj = new Dictionary<string, object>
                                {
                                    ["type"] = prop.Value.Type
                                };
                                
                                if (!string.IsNullOrEmpty(prop.Value.Description))
                                {
                                    propObj["description"] = prop.Value.Description;
                                }
                                
                                if (prop.Value.Enum != null && prop.Value.Enum.Count > 0)
                                {
                                    propObj["enum"] = prop.Value.Enum;
                                }
                                
                                properties[prop.Key] = propObj;
                            }
                        }
                        
                        parameters = new
                        {
                            type = "object",
                            properties = properties,
                            required = tool.InputSchema.Required ?? new List<string>()
                        };
                        
                        System.Diagnostics.Debug.WriteLine($"  - 带有 {properties.Count} 个参数的模式");
                    }
                    else
                    {
                        // 创建一个最小的空参数对象
                        parameters = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>(),
                            required = new List<string>()
                        };
                        
                        System.Diagnostics.Debug.WriteLine("  - 使用空参数模式");
                    }
                    
                    // 构建完整的OpenAI工具对象
                    var openAITool = new 
                    {
                        type = "function",
                        function = new 
                        {
                            name = tool.Name,
                            description = !string.IsNullOrEmpty(tool.Description) 
                                ? tool.Description 
                                : $"工具 {tool.Name} 来自服务器 {tool.ServerName}",
                            parameters = parameters
                        }
                    };
                    
                    openAITools.Add(openAITool);
                    System.Diagnostics.Debug.WriteLine($"  - 成功转换工具 {tool.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"转换工具 {tool.Name} 定义失败: {ex.Message}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"转换完成，共 {openAITools.Count} 个工具");
            return openAITools;
        }

        /// <summary>
        /// 发送对话消息请求（支持完整的函数调用模式）
        /// </summary>
        public async Task<bool> SendConversationAsync(
            LlmApiConfig config,
            List<ConversationMessage> messages,
            List<McpTool>? availableTools,
            LlmResponseCallback onLlmResponse,
            StreamingErrorCallback? onError = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    onError?.Invoke("API配置不完整");
                    return false;
                }

                // 构建请求对象
                var requestMessages = messages.Select(m => new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_call_id = m.ToolCallId,
                    tool_calls = m.ToolCalls?.Select(tc => new
                    {
                        id = tc.Id,
                        type = tc.Type,
                        function = tc.Function == null ? null : new
                        {
                            name = tc.Function!.Name,
                            arguments = tc.Function!.Arguments
                        }
                    }).ToArray()
                }).ToArray();

                object requestData;

                // 如果有可用工具，添加工具定义
                if (availableTools != null && availableTools.Count > 0)
                {
                    var openAITools = ConvertToOpenAIToolFormat(availableTools);

                    // 使用工具选择优化器确定最佳tool_choice策略
                    string toolChoice = "auto";
                    if (App.ToolSelectionOptimizer != null && messages.Count > 0)
                    {
                        var userMessage = messages.FirstOrDefault(m => m.Role == "user")?.Content ?? "";
                        toolChoice = App.ToolSelectionOptimizer.GetOptimalToolChoice(availableTools, userMessage);
                    }

                    requestData = new
                    {
                        model = config.ModelName,
                        messages = requestMessages,
                        temperature = config.Temperature,
                        max_tokens = config.MaxTokens,
                        stream = true,
                        tools = openAITools.ToArray(),
                        tool_choice = toolChoice
                    };
                }
                else
                {
                    requestData = new
                    {
                        model = config.ModelName,
                        messages = requestMessages,
                        temperature = config.Temperature,
                        max_tokens = config.MaxTokens,
                        stream = true
                    };
                }

                var jsonContent = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl)
                {
                    Content = content
                };
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
                request.Headers.Add("Accept", "text/event-stream");

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    onError?.Invoke($"API错误 {(int)response.StatusCode}: {errorContent}");
                    return false;
                }

                // 读取流式数据并解析LLM响应
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var currentResponse = new LlmResponse();
                var currentToolCalls = new Dictionary<string, LlmToolCall>();
                string? line;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                        continue;

                    var jsonData = line.Substring(6).Trim();
                    
                    if (jsonData == "[DONE]")
                    {
                        onLlmResponse.Invoke(currentResponse, true);
                        break;
                    }

                    try
                    {
                        var jsonObject = JObject.Parse(jsonData);
                        var choices = jsonObject["choices"] as JArray;
                        
                        if (choices != null && choices.Count > 0)
                        {
                            var firstChoice = choices[0];
                            var delta = firstChoice["delta"] as JObject;
                            
                            if (delta != null)
                            {
                                // 处理文本内容
                                if (delta.ContainsKey("content"))
                                {
                                    var contentChunk = delta["content"]?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(contentChunk))
                                    {
                                        currentResponse.Content += contentChunk;
                                        onLlmResponse.Invoke(currentResponse, false);
                                    }
                                }
                                
                                // 处理工具调用
                                var toolCalls = delta["tool_calls"] as JArray;
                                if (toolCalls != null)
                                {
                                    foreach (var toolCallToken in toolCalls)
                                    {
                                        var index = toolCallToken["index"]?.Value<int>() ?? 0;
                                        var id = toolCallToken["id"]?.ToString();
                                        var type = toolCallToken["type"]?.ToString();
                                        var function = toolCallToken["function"];

                                        if (!string.IsNullOrEmpty(id) && !currentToolCalls.ContainsKey(id))
                                        {
                                            currentToolCalls[id] = new LlmToolCall
                                            {
                                                Id = id,
                                                Type = type ?? "function",
                                                Function = new LlmToolFunction()
                                            };
                                        }

                                        if (currentToolCalls.TryGetValue(id ?? "", out var existingCall) && function != null)
                                        {
                                            if (function["name"] != null)
                                            {
                                                existingCall.Function!.Name = function["name"]!.ToString();
                                            }
                                            
                                            if (function["arguments"] != null)
                                            {
                                                existingCall.Function!.Arguments += function["arguments"]!.ToString();
                                            }
                                        }
                                    }
                                    
                                    // 更新当前响应的工具调用
                                    currentResponse.ToolCalls = currentToolCalls.Values.ToList();
                                    onLlmResponse.Invoke(currentResponse, false);
                                }
                            }
                            
                            // 检查是否完成
                            var finishReason = firstChoice["finish_reason"];
                            if (finishReason != null && !string.IsNullOrEmpty(finishReason.ToString()))
                            {
                                onLlmResponse.Invoke(currentResponse, true);
                                break;
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"解析流式数据错误: {ex.Message}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"对话请求错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送非流式对话消息请求（获取完整响应）
        /// </summary>
        public async Task<LlmResponse?> SendConversationAndGetResponseAsync(
            LlmApiConfig config,
            List<ConversationMessage> messages,
            List<McpTool>? availableTools,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    return null;
                }

                // 构建请求对象
                var requestMessages = messages.Select(m => new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_call_id = m.ToolCallId,
                    tool_calls = m.ToolCalls?.Select(tc => new
                    {
                        id = tc.Id,
                        type = tc.Type,
                        function = tc.Function == null ? null : new
                        {
                            name = tc.Function!.Name,
                            arguments = tc.Function!.Arguments
                        }
                    }).ToArray()
                }).ToArray();

                object requestData;

                // 如果有可用工具，添加工具定义
                if (availableTools != null && availableTools.Count > 0)
                {
                    var openAITools = ConvertToOpenAIToolFormat(availableTools);

                    // 使用工具选择优化器确定最佳tool_choice策略
                    string toolChoice = "auto";
                    if (App.ToolSelectionOptimizer != null && messages.Count > 0)
                    {
                        var userMessage = messages.FirstOrDefault(m => m.Role == "user")?.Content ?? "";
                        toolChoice = App.ToolSelectionOptimizer.GetOptimalToolChoice(availableTools, userMessage);
                    }

                    requestData = new
                    {
                        model = config.ModelName,
                        messages = requestMessages,
                        temperature = config.Temperature,
                        max_tokens = config.MaxTokens,
                        tools = openAITools.ToArray(),
                        tool_choice = toolChoice
                    };
                }
                else
                {
                    requestData = new
                    {
                        model = config.ModelName,
                        messages = requestMessages,
                        temperature = config.Temperature,
                        max_tokens = config.MaxTokens
                    };
                }

                var jsonContent = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl)
                {
                    Content = content
                };
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"LLM API错误 {(int)response.StatusCode}: {errorContent}");
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonObject = JObject.Parse(responseContent);
                
                var choices = jsonObject["choices"] as JArray;
                if (choices == null || choices.Count == 0)
                    return null;

                var firstChoice = choices[0];
                var message = firstChoice["message"];
                
                if (message == null)
                    return null;

                var llmResponse = new LlmResponse();
                
                // 解析文本内容
                if (message["content"] != null)
                {
                    llmResponse.Content = message["content"]!.ToString();
                }
                
                // 解析工具调用
                var toolCalls = message["tool_calls"] as JArray;
                if (toolCalls != null)
                {
                    foreach (var toolCallToken in toolCalls)
                    {
                        var toolCall = new LlmToolCall
                        {
                            Id = toolCallToken["id"]?.ToString() ?? "",
                            Type = toolCallToken["type"]?.ToString() ?? "function",
                            Function = new LlmToolFunction
                            {
                                Name = toolCallToken["function"]?["name"]?.ToString() ?? "",
                                Arguments = toolCallToken["function"]?["arguments"]?.ToString() ?? ""
                            }
                        };
                        
                        llmResponse.ToolCalls.Add(toolCall);
                    }
                }

                return llmResponse;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"非流式对话请求错误: {ex.Message}");
                return null;
            }
        }

        // 释放资源
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
} 