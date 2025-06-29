using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Helpers
{
    /// <summary>
    /// 网络连通性检查工具类
    /// </summary>
    public static class NetworkHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// 检查TCP端口是否可达
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>是否可达</returns>
        public static async Task<bool> IsPortReachableAsync(string host, int port, int timeout = 5000)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(timeout);
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                
                if (completedTask == connectTask && client.Connected)
                {
                    return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从URL提取主机和端口
        /// </summary>
        /// <param name="url">URL字符串</param>
        /// <returns>主机和端口信息</returns>
        public static (string Host, int Port) ExtractHostAndPort(string url)
        {
            try
            {
                var uri = new Uri(url);
                var host = uri.Host;
                var port = uri.Port;
                
                // 如果端口未指定，使用默认端口
                if (port == -1)
                {
                    port = uri.Scheme.ToLower() switch
                    {
                        "http" => 80,
                        "https" => 443,
                        _ => 80
                    };
                }
                
                return (host, port);
            }
            catch
            {
                return (string.Empty, 0);
            }
        }

        /// <summary>
        /// 检查HTTP端点是否可达
        /// </summary>
        /// <param name="url">HTTP端点URL</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否可达</returns>
        public static async Task<bool> IsHttpEndpointReachableAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// 简单的SSE连接测试
        /// </summary>
        /// <param name="sseUrl">SSE端点URL</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否可连接</returns>
        public static async Task<bool> TestSseConnectionAsync(string sseUrl, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, sseUrl);
                request.Headers.Accept.ParseAdd("text/event-stream");
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                
                // 检查响应头是否包含SSE相关标识
                return response.IsSuccessStatusCode && 
                       (response.Content.Headers.ContentType?.MediaType?.Contains("text/event-stream") == true ||
                        response.Headers.Contains("Content-Type"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ping测试主机连通性
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>是否可达</returns>
        public static async Task<bool> PingHostAsync(string host, int timeout = 5000)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, timeout);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
    }
} 