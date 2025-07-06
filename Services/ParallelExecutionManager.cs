using Lyxie_desktop.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// 并行执行管理器 - 实现高性能的工具调用并行执行
    /// </summary>
    public class ParallelExecutionManager : IDisposable
    {
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly ConcurrentDictionary<string, ResourcePool> _resourcePools;
        private readonly TaskScheduler _taskScheduler;
        private readonly CancellationTokenSource _shutdownTokenSource;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        // 配置参数
        private int _maxConcurrency;
        private int _maxQueueSize;
        private TimeSpan _defaultTimeout;

        // 统计信息
        private long _totalExecutions;
        private long _successfulExecutions;
        private long _failedExecutions;
        private readonly ConcurrentDictionary<string, ExecutionMetrics> _executionMetrics;

        public ParallelExecutionManager(int maxConcurrency = 5, int maxQueueSize = 100)
        {
            _maxConcurrency = maxConcurrency;
            _maxQueueSize = maxQueueSize;
            _defaultTimeout = TimeSpan.FromMinutes(5);

            _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            _resourcePools = new ConcurrentDictionary<string, ResourcePool>();
            _taskScheduler = TaskScheduler.Default;
            _shutdownTokenSource = new CancellationTokenSource();
            _executionMetrics = new ConcurrentDictionary<string, ExecutionMetrics>();

            InitializeResourcePools();
            Debug.WriteLine($"并行执行管理器已初始化 - 最大并发数: {maxConcurrency}, 最大队列大小: {maxQueueSize}");
        }

        /// <summary>
        /// 初始化资源池
        /// </summary>
        private void InitializeResourcePools()
        {
            // 网络资源池
            _resourcePools["network"] = new ResourcePool
            {
                PoolName = "network",
                MaxConcurrency = Math.Max(1, _maxConcurrency / 2),
                CurrentUsage = 0,
                ResourceType = ResourceType.Network
            };

            // 文件系统资源池
            _resourcePools["filesystem"] = new ResourcePool
            {
                PoolName = "filesystem",
                MaxConcurrency = Math.Max(1, _maxConcurrency / 3),
                CurrentUsage = 0,
                ResourceType = ResourceType.FileSystem
            };

            // CPU密集型资源池
            _resourcePools["cpu"] = new ResourcePool
            {
                PoolName = "cpu",
                MaxConcurrency = Environment.ProcessorCount,
                CurrentUsage = 0,
                ResourceType = ResourceType.CPU
            };

            Debug.WriteLine($"已初始化 {_resourcePools.Count} 个资源池");
        }

        /// <summary>
        /// 并行执行工具调用
        /// </summary>
        public async Task<List<ToolCallExecution>> ExecuteInParallelAsync<T>(
            List<T> items,
            Func<T, CancellationToken, Task<ToolCallExecution>> executor,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ParallelExecutionManager));

            if (items == null || items.Count == 0)
                return new List<ToolCallExecution>();

            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _shutdownTokenSource.Token).Token;

            var executions = new ConcurrentBag<ToolCallExecution>();
            var semaphore = new SemaphoreSlim(Math.Min(_maxConcurrency, items.Count));

            Debug.WriteLine($"开始并行执行 {items.Count} 个工具调用");

            try
            {
                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync(combinedToken);
                    try
                    {
                        var execution = await ExecuteWithResourceManagement(
                            () => executor(item, combinedToken),
                            GetResourceType(item),
                            combinedToken
                        );
                        executions.Add(execution);
                        return execution;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var results = await Task.WhenAll(tasks);
                
                // 更新统计信息
                UpdateExecutionStatistics(results);
                
                Debug.WriteLine($"并行执行完成 - 成功: {results.Count(r => r.Status == ToolExecutionStatus.Completed)}, " +
                              $"失败: {results.Count(r => r.Status == ToolExecutionStatus.Failed)}");

                return results.ToList();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("并行执行被取消");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"并行执行异常: {ex.Message}");
                throw;
            }
            finally
            {
                semaphore.Dispose();
            }
        }

        /// <summary>
        /// 使用资源管理执行任务
        /// </summary>
        private async Task<ToolCallExecution> ExecuteWithResourceManagement(
            Func<Task<ToolCallExecution>> taskFactory,
            ResourceType resourceType,
            CancellationToken cancellationToken)
        {
            var poolName = GetPoolName(resourceType);
            var pool = _resourcePools.GetValueOrDefault(poolName);

            if (pool == null)
            {
                // 如果没有对应的资源池，直接执行
                return await taskFactory();
            }

            // 等待资源可用
            await WaitForResource(pool, cancellationToken);

            try
            {
                // 增加资源使用计数
                var currentUsage = pool.CurrentUsage;
                pool.CurrentUsage = currentUsage + 1;
                
                var startTime = DateTime.Now;
                var execution = await taskFactory();
                var duration = DateTime.Now - startTime;

                // 更新资源池统计
                UpdateResourcePoolMetrics(pool, duration, execution.Status == ToolExecutionStatus.Completed);

                return execution;
            }
            finally
            {
                // 释放资源
                var currentUsage = pool.CurrentUsage;
                pool.CurrentUsage = Math.Max(0, currentUsage - 1);
            }
        }

        /// <summary>
        /// 等待资源可用
        /// </summary>
        private async Task WaitForResource(ResourcePool pool, CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromSeconds(30); // 资源等待超时
            var startTime = DateTime.Now;

            while (pool.CurrentUsage >= pool.MaxConcurrency)
            {
                if (DateTime.Now - startTime > timeout)
                {
                    throw new TimeoutException($"等待资源池 {pool.PoolName} 超时");
                }

                await Task.Delay(100, cancellationToken);
            }
        }

        /// <summary>
        /// 获取资源类型
        /// </summary>
        private ResourceType GetResourceType<T>(T item)
        {
            // 根据项目类型或内容判断资源类型
            if (item is LlmToolCall toolCall)
            {
                var toolName = toolCall.Function?.Name?.ToLowerInvariant() ?? "";
                
                if (toolName.Contains("file") || toolName.Contains("read") || toolName.Contains("write"))
                    return ResourceType.FileSystem;
                
                if (toolName.Contains("http") || toolName.Contains("api") || toolName.Contains("web"))
                    return ResourceType.Network;
                
                return ResourceType.CPU;
            }

            return ResourceType.CPU;
        }

        /// <summary>
        /// 获取资源池名称
        /// </summary>
        private string GetPoolName(ResourceType resourceType)
        {
            return resourceType switch
            {
                ResourceType.Network => "network",
                ResourceType.FileSystem => "filesystem",
                ResourceType.CPU => "cpu",
                _ => "cpu"
            };
        }

        /// <summary>
        /// 更新执行统计信息
        /// </summary>
        private void UpdateExecutionStatistics(ToolCallExecution[] executions)
        {
            Interlocked.Add(ref _totalExecutions, executions.Length);
            
            var successful = executions.Count(e => e.Status == ToolExecutionStatus.Completed);
            var failed = executions.Count(e => e.Status == ToolExecutionStatus.Failed);
            
            Interlocked.Add(ref _successfulExecutions, successful);
            Interlocked.Add(ref _failedExecutions, failed);

            // 更新详细指标
            foreach (var execution in executions)
            {
                var toolName = execution.LlmToolCall.Function?.Name ?? "unknown";
                _executionMetrics.AddOrUpdate(toolName, 
                    new ExecutionMetrics { ToolName = toolName, TotalExecutions = 1, SuccessfulExecutions = successful > 0 ? 1 : 0 },
                    (key, existing) => 
                    {
                        existing.TotalExecutions++;
                        if (execution.Status == ToolExecutionStatus.Completed)
                            existing.SuccessfulExecutions++;
                        return existing;
                    });
            }
        }

        /// <summary>
        /// 更新资源池指标
        /// </summary>
        private void UpdateResourcePoolMetrics(ResourcePool pool, TimeSpan duration, bool success)
        {
            pool.TotalExecutions++;
            pool.TotalExecutionTime += duration;
            
            if (success)
                pool.SuccessfulExecutions++;

            pool.AverageExecutionTime = TimeSpan.FromMilliseconds(
                pool.TotalExecutionTime.TotalMilliseconds / pool.TotalExecutions);
        }

        /// <summary>
        /// 获取执行统计信息
        /// </summary>
        public ExecutionStatistics GetExecutionStatistics()
        {
            if (_disposed)
                return new ExecutionStatistics();

            return new ExecutionStatistics
            {
                TotalExecutions = _totalExecutions,
                SuccessfulExecutions = _successfulExecutions,
                FailedExecutions = _failedExecutions,
                SuccessRate = _totalExecutions > 0 ? (double)_successfulExecutions / _totalExecutions : 0,
                ResourcePools = _resourcePools.Values.ToList(),
                ToolMetrics = _executionMetrics.Values.ToList()
            };
        }

        /// <summary>
        /// 动态调整并发数
        /// </summary>
        public void AdjustConcurrency(int newMaxConcurrency)
        {
            if (_disposed || newMaxConcurrency <= 0)
                return;

            lock (_lockObject)
            {
                var oldConcurrency = _maxConcurrency;
                _maxConcurrency = newMaxConcurrency;

                // 调整信号量
                var difference = newMaxConcurrency - oldConcurrency;
                if (difference > 0)
                {
                    _concurrencyLimiter.Release(difference);
                }

                Debug.WriteLine($"并发数已调整: {oldConcurrency} -> {newMaxConcurrency}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _shutdownTokenSource.Cancel();
                _concurrencyLimiter.Dispose();
                _shutdownTokenSource.Dispose();
                _resourcePools.Clear();
                _executionMetrics.Clear();
                Debug.WriteLine("并行执行管理器已释放");
            }
        }
    }
}
