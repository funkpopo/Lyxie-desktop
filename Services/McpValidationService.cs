using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using Newtonsoft.Json;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// MCP验证服务，负责验证MCP服务器的可用性
    /// </summary>
    public class McpValidationService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly IMcpServerManager? _serverManager;
        private const int DefaultTimeoutSeconds = 30;

        public McpValidationService(IMcpServerManager? serverManager = null)
        {
            _serverManager = serverManager;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds)
            };
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
            if (definition == null)
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.ConfigurationError,
                    ErrorMessage = "服务器定义为空"
                };
            }

            if (!definition.IsEnabled)
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = "服务器已禁用"
                };
            }

            try
            {
                // 根据协议类型选择验证方法
                if (!string.IsNullOrEmpty(definition.Url))
                {
                    return await ValidateHttpServerAsync(name, definition, cancellationToken);
                }
                else if (!string.IsNullOrEmpty(definition.Command))
                {
                    return await ValidateStdioServerAsync(name, definition, cancellationToken);
                }
                else
                {
                    return new McpValidationResult
                    {
                        IsAvailable = false,
                        Status = McpValidationStatus.ConfigurationError,
                        ErrorMessage = "缺少必要的配置：URL或Command"
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Timeout,
                    ErrorMessage = "验证超时"
                };
            }
            catch (Exception ex)
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = $"验证失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 验证HTTP协议的MCP服务器
        /// </summary>
        private async Task<McpValidationResult> ValidateHttpServerAsync(string name, McpServerDefinition definition, CancellationToken cancellationToken)
        {
            try
            {
                // 构建MCP初始化请求
                var initRequest = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new
                        {
                            roots = new { listChanged = true },
                            sampling = new { }
                        },
                        clientInfo = new
                        {
                            name = "Lyxie",
                            version = "1.0.0"
                        }
                    }
                };

                var jsonContent = JsonConvert.SerializeObject(initRequest);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(definition.Url, httpContent, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    // 尝试解析响应以验证是否为有效的MCP响应
                    var mcpResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    
                    if (mcpResponse?.result != null)
                    {
                        return new McpValidationResult
                        {
                            IsAvailable = true,
                            Status = McpValidationStatus.Available,
                            ErrorMessage = null
                        };
                    }
                    else
                    {
                        return new McpValidationResult
                        {
                            IsAvailable = false,
                            Status = McpValidationStatus.Unavailable,
                            ErrorMessage = "服务器响应格式不正确"
                        };
                    }
                }
                else
                {
                    return new McpValidationResult
                    {
                        IsAvailable = false,
                        Status = McpValidationStatus.Unavailable,
                        ErrorMessage = $"HTTP错误: {response.StatusCode}"
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = $"网络错误: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 验证stdio协议的MCP服务器
        /// </summary>
        private async Task<McpValidationResult> ValidateStdioServerAsync(string name, McpServerDefinition definition, CancellationToken cancellationToken)
        {
            if (_serverManager is null)
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.ConfigurationError,
                    ErrorMessage = "McpServerManager未初始化，无法验证stdio服务器"
                };
            }

            // 1. 检查服务器是否正在运行
            if (!_serverManager.IsServerRunning(name))
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = "服务器进程未运行"
                };
            }
            
            try
            {
                // 2. 构建MCP初始化请求
                var requestId = Guid.NewGuid().ToString();
                var initRequest = new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new
                        {
                            roots = new { listChanged = true },
                            sampling = new { }
                        },
                        clientInfo = new
                        {
                            name = "Lyxie",
                            version = "1.0.0"
                        }
                    }
                };
                var jsonRequest = JsonConvert.SerializeObject(initRequest);

                // 3. 发送请求并等待响应
                var responseLine = await _serverManager.SendRequestAndReadResponseAsync(name, jsonRequest, cancellationToken);

                if (!string.IsNullOrEmpty(responseLine))
                {
                    var mcpResponse = JsonConvert.DeserializeObject<dynamic>(responseLine);
                    if (mcpResponse?.result != null && mcpResponse?.id == requestId)
                    {
                        return new McpValidationResult
                        {
                            IsAvailable = true,
                            Status = McpValidationStatus.Available,
                            ErrorMessage = null
                        };
                    }
                }

                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = "进程无响应或响应格式不正确"
                };
            }
            catch (OperationCanceledException)
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Timeout,
                    ErrorMessage = "验证请求超时"
                };
            }
            catch (Exception ex)
            {
                return new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = $"验证异常: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 批量验证多个MCP服务器
        /// </summary>
        /// <param name="servers">服务器字典</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>验证结果字典</returns>
        public async Task<Dictionary<string, McpValidationResult>> ValidateServersAsync(Dictionary<string, McpServerDefinition> servers, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, McpValidationResult>();
            var tasks = new List<Task>();

            foreach (var server in servers)
            {
                var task = Task.Run(async () =>
                {
                    var result = await ValidateServerAsync(server.Key, server.Value, cancellationToken);
                    lock (results)
                    {
                        results[server.Key] = result;
                    }
                }, cancellationToken);
                
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return results;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }


}