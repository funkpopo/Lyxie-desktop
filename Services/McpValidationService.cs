using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using Lyxie_desktop.Helpers;
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
        
        // 缓存验证结果和验证时间
        private readonly ConcurrentDictionary<string, (McpValidationResult Result, DateTime Timestamp)> _validationCache = new();
        // 验证缓存有效期（成功后10分钟内不重新验证，失败后30秒内不重新验证）
        private static readonly TimeSpan _successCacheDuration = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan _failureCacheDuration = TimeSpan.FromSeconds(30);

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
        /// <param name="forceCheck">强制重新验证，忽略缓存</param>
        /// <returns>验证结果</returns>
        public async Task<McpValidationResult> ValidateServerAsync(string name, McpServerDefinition definition, 
            CancellationToken cancellationToken = default, bool forceCheck = false)
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
            
            // 检查缓存，避免频繁验证
            if (!forceCheck && _validationCache.TryGetValue(name, out var cached))
            {
                var now = DateTime.UtcNow;
                var cacheDuration = cached.Result.IsAvailable ? _successCacheDuration : _failureCacheDuration;
                
                // 如果缓存仍然有效，直接返回缓存的结果
                if ((now - cached.Timestamp) < cacheDuration)
                {
                    System.Diagnostics.Debug.WriteLine($"使用缓存的验证结果，服务器 {name}, 状态: {cached.Result.IsAvailable}, 距上次验证: {(now - cached.Timestamp).TotalSeconds:F1}秒");
                    return cached.Result;
                }
                
                // 对于已验证成功的服务器，如果进程仍在运行，则延长缓存有效期（减少验证频率）
                if (cached.Result.IsAvailable && definition.IsStdioServer && _serverManager != null && _serverManager.IsServerRunning(name))
                {
                    System.Diagnostics.Debug.WriteLine($"服务器 {name} 进程仍在运行，延长缓存有效期");
                    _validationCache[name] = (cached.Result, now); // 更新时间戳
                    return cached.Result;
                }
            }

            try
            {
                McpValidationResult result;
                
                // 根据协议类型选择验证方法
                if (!string.IsNullOrEmpty(definition.Url))
                {
                    result = await ValidateHttpServerAsync(name, definition, cancellationToken);
                }
                else if (!string.IsNullOrEmpty(definition.Command))
                {
                    result = await ValidateStdioServerAsync(name, definition, cancellationToken);
                }
                else
                {
                    result = new McpValidationResult
                    {
                        IsAvailable = false,
                        Status = McpValidationStatus.ConfigurationError,
                        ErrorMessage = "缺少必要的配置：URL或Command"
                    };
                }
                
                // 更新缓存
                _validationCache[name] = (result, DateTime.UtcNow);
                return result;
            }
            catch (OperationCanceledException)
            {
                var result = new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Timeout,
                    ErrorMessage = "验证超时"
                };
                
                // 更新缓存
                _validationCache[name] = (result, DateTime.UtcNow);
                return result;
            }
            catch (Exception ex)
            {
                var result = new McpValidationResult
                {
                    IsAvailable = false,
                    Status = McpValidationStatus.Unavailable,
                    ErrorMessage = $"验证失败: {ex.Message}"
                };
                
                // 更新缓存
                _validationCache[name] = (result, DateTime.UtcNow);
                return result;
            }
        }

        /// <summary>
        /// 验证HTTP协议的MCP服务器
        /// </summary>
        private async Task<McpValidationResult> ValidateHttpServerAsync(string name, McpServerDefinition definition, CancellationToken cancellationToken)
        {
            try
            {
                // 1. 先进行基础网络连通性检查
                var (host, port) = NetworkHelper.ExtractHostAndPort(definition.Url!);
                if (!string.IsNullOrEmpty(host) && port > 0)
                {
                    var isReachable = await NetworkHelper.IsPortReachableAsync(host, port, definition.ConnectionTimeout * 1000);
                    if (!isReachable)
                    {
                        return new McpValidationResult
                        {
                            IsAvailable = false,
                            Status = McpValidationStatus.Unavailable,
                            ErrorMessage = $"无法连接到服务器 {host}:{port}"
                        };
                    }
                }

                // 2. 如果配置了SSE端点，先测试SSE连接
                if (!string.IsNullOrEmpty(definition.SseUrl))
                {
                    var sseTestResult = await NetworkHelper.TestSseConnectionAsync(definition.SseUrl, cancellationToken);
                    if (!sseTestResult)
                    {
                        return new McpValidationResult
                        {
                            IsAvailable = false,
                            Status = McpValidationStatus.Unavailable,
                            ErrorMessage = "SSE端点连接失败"
                        };
                    }
                }

                // 3. 进行标准MCP协议验证
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

            // 1. 检查服务器进程是否存在（包括外部启动的进程）
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
                // 2. 进行标准MCP协议验证
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

                // 3. 发送请求并等待响应（仅对内部管理的进程）
                // 对于外部进程，仅检查进程存在性即可
                if (_serverManager.IsServerRunning(name))
                {
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
                    else
                    {
                        // 如果无法通信但进程存在，可能是外部启动的进程
                        return new McpValidationResult
                        {
                            IsAvailable = true,
                            Status = McpValidationStatus.Available,
                            ErrorMessage = "进程存在（外部启动）"
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
        /// <param name="forceCheck">强制重新验证，忽略缓存</param>
        /// <returns>验证结果字典</returns>
        public async Task<Dictionary<string, McpValidationResult>> ValidateServersAsync(
            Dictionary<string, McpServerDefinition> servers, 
            CancellationToken cancellationToken = default, 
            bool forceCheck = false)
        {
            var results = new Dictionary<string, McpValidationResult>();
            var tasks = new List<Task>();

            foreach (var server in servers)
            {
                var task = Task.Run(async () =>
                {
                    var result = await ValidateServerAsync(server.Key, server.Value, cancellationToken, forceCheck);
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