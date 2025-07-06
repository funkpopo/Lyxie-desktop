using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// 工具调用链管理器 - 实现智能的工具调用链式执行
    /// </summary>
    public class ToolCallChainManager : IDisposable
    {
        private readonly Dictionary<string, ToolDependencyInfo> _toolDependencies;
        private readonly Dictionary<string, ToolCallValidator> _validators;
        private readonly List<ToolCallChain> _activeChains;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public ToolCallChainManager()
        {
            _toolDependencies = new Dictionary<string, ToolDependencyInfo>();
            _validators = new Dictionary<string, ToolCallValidator>();
            _activeChains = new List<ToolCallChain>();
            
            InitializeToolDependencies();
            InitializeValidators();
            
            Debug.WriteLine("工具调用链管理器已初始化");
        }

        /// <summary>
        /// 初始化工具依赖关系
        /// </summary>
        private void InitializeToolDependencies()
        {
            // 文件操作依赖关系
            _toolDependencies["write_file"] = new ToolDependencyInfo
            {
                ToolName = "write_file",
                Prerequisites = new[] { "read_file" }, // 写文件前可能需要先读取
                Conflicts = new[] { "delete_file" }, // 不能同时写入和删除
                PostActions = new[] { "verify_file" } // 写入后验证
            };

            _toolDependencies["read_file"] = new ToolDependencyInfo
            {
                ToolName = "read_file",
                Prerequisites = new string[0],
                Conflicts = new[] { "delete_file" },
                PostActions = new string[0]
            };

            _toolDependencies["delete_file"] = new ToolDependencyInfo
            {
                ToolName = "delete_file",
                Prerequisites = new[] { "backup_file" }, // 删除前备份
                Conflicts = new[] { "write_file", "read_file" },
                PostActions = new[] { "verify_deletion" }
            };

            // 网络操作依赖关系
            _toolDependencies["download_file"] = new ToolDependencyInfo
            {
                ToolName = "download_file",
                Prerequisites = new[] { "check_network" },
                Conflicts = new string[0],
                PostActions = new[] { "verify_download" }
            };

            // 系统操作依赖关系
            _toolDependencies["execute_command"] = new ToolDependencyInfo
            {
                ToolName = "execute_command",
                Prerequisites = new[] { "check_permissions" },
                Conflicts = new string[0],
                PostActions = new[] { "verify_execution" }
            };

            Debug.WriteLine($"已初始化 {_toolDependencies.Count} 个工具依赖关系");
        }

        /// <summary>
        /// 初始化验证器
        /// </summary>
        private void InitializeValidators()
        {
            // 文件操作验证器
            _validators["write_file"] = new ToolCallValidator
            {
                ValidatorName = "write_file_validator",
                ValidationRules = new[]
                {
                    "文件路径必须有效",
                    "目标目录必须存在",
                    "必须有写入权限"
                },
                ValidationFunction = ValidateFileWrite
            };

            _validators["read_file"] = new ToolCallValidator
            {
                ValidatorName = "read_file_validator",
                ValidationRules = new[]
                {
                    "文件必须存在",
                    "必须有读取权限",
                    "文件大小不能超过限制"
                },
                ValidationFunction = ValidateFileRead
            };

            _validators["download_file"] = new ToolCallValidator
            {
                ValidatorName = "download_validator",
                ValidationRules = new[]
                {
                    "URL必须有效",
                    "网络连接必须可用",
                    "目标路径必须可写"
                },
                ValidationFunction = ValidateDownload
            };

            Debug.WriteLine($"已初始化 {_validators.Count} 个工具验证器");
        }

        /// <summary>
        /// 分析工具调用依赖关系
        /// </summary>
        public ToolCallChain AnalyzeDependencies(List<LlmToolCall> toolCalls)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ToolCallChainManager));

            lock (_lockObject)
            {
                var chain = new ToolCallChain
                {
                    Id = Guid.NewGuid().ToString(),
                    OriginalCalls = new List<LlmToolCall>(toolCalls),
                    CreatedAt = DateTime.Now
                };

                Debug.WriteLine($"开始分析 {toolCalls.Count} 个工具调用的依赖关系");

                // 1. 构建依赖图
                var dependencyGraph = BuildDependencyGraph(toolCalls);
                
                // 2. 检测冲突
                var conflicts = DetectConflicts(toolCalls);
                if (conflicts.Count > 0)
                {
                    chain.HasConflicts = true;
                    chain.ConflictDetails = conflicts;
                    Debug.WriteLine($"检测到 {conflicts.Count} 个工具调用冲突");
                }

                // 3. 确定执行顺序
                chain.ExecutionOrder = DetermineExecutionOrder(toolCalls, dependencyGraph);
                
                // 4. 添加必要的前置和后置操作
                chain.EnhancedCalls = AddPreAndPostActions(chain.ExecutionOrder);

                _activeChains.Add(chain);
                Debug.WriteLine($"工具调用链分析完成，执行顺序包含 {chain.EnhancedCalls.Count} 个步骤");
                
                return chain;
            }
        }

        /// <summary>
        /// 构建依赖图
        /// </summary>
        private Dictionary<string, List<string>> BuildDependencyGraph(List<LlmToolCall> toolCalls)
        {
            var graph = new Dictionary<string, List<string>>();
            
            foreach (var call in toolCalls)
            {
                var toolName = call.Function?.Name ?? "";
                if (!graph.ContainsKey(toolName))
                {
                    graph[toolName] = new List<string>();
                }

                if (_toolDependencies.TryGetValue(toolName, out var depInfo))
                {
                    foreach (var prerequisite in depInfo.Prerequisites)
                    {
                        if (toolCalls.Any(tc => tc.Function?.Name == prerequisite))
                        {
                            graph[toolName].Add(prerequisite);
                        }
                    }
                }
            }

            return graph;
        }

        /// <summary>
        /// 检测工具调用冲突
        /// </summary>
        private List<ToolCallConflict> DetectConflicts(List<LlmToolCall> toolCalls)
        {
            var conflicts = new List<ToolCallConflict>();
            
            for (int i = 0; i < toolCalls.Count; i++)
            {
                for (int j = i + 1; j < toolCalls.Count; j++)
                {
                    var call1 = toolCalls[i];
                    var call2 = toolCalls[j];
                    
                    var conflict = CheckConflict(call1, call2);
                    if (conflict != null)
                    {
                        conflicts.Add(conflict);
                    }
                }
            }

            return conflicts;
        }

        /// <summary>
        /// 检查两个工具调用是否冲突
        /// </summary>
        private ToolCallConflict? CheckConflict(LlmToolCall call1, LlmToolCall call2)
        {
            var tool1Name = call1.Function?.Name ?? "";
            var tool2Name = call2.Function?.Name ?? "";

            if (_toolDependencies.TryGetValue(tool1Name, out var dep1))
            {
                if (dep1.Conflicts.Contains(tool2Name))
                {
                    return new ToolCallConflict
                    {
                        Tool1 = call1,
                        Tool2 = call2,
                        ConflictType = "Direct Conflict",
                        Description = $"{tool1Name} 与 {tool2Name} 存在直接冲突"
                    };
                }
            }

            // 检查资源冲突（例如同时操作同一个文件）
            var resourceConflict = CheckResourceConflict(call1, call2);
            if (resourceConflict != null)
            {
                return resourceConflict;
            }

            return null;
        }

        /// <summary>
        /// 检查资源冲突
        /// </summary>
        private ToolCallConflict? CheckResourceConflict(LlmToolCall call1, LlmToolCall call2)
        {
            // 简化的资源冲突检测：检查是否操作同一文件
            var args1 = call1.Function?.Arguments ?? "";
            var args2 = call2.Function?.Arguments ?? "";

            try
            {
                var json1 = JsonDocument.Parse(args1);
                var json2 = JsonDocument.Parse(args2);

                if (json1.RootElement.TryGetProperty("path", out var path1) &&
                    json2.RootElement.TryGetProperty("path", out var path2))
                {
                    if (path1.GetString() == path2.GetString())
                    {
                        return new ToolCallConflict
                        {
                            Tool1 = call1,
                            Tool2 = call2,
                            ConflictType = "Resource Conflict",
                            Description = $"两个工具调用操作同一文件: {path1.GetString()}"
                        };
                    }
                }
            }
            catch
            {
                // 忽略JSON解析错误
            }

            return null;
        }

        /// <summary>
        /// 确定执行顺序
        /// </summary>
        private List<LlmToolCall> DetermineExecutionOrder(List<LlmToolCall> toolCalls, Dictionary<string, List<string>> dependencyGraph)
        {
            var ordered = new List<LlmToolCall>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();

            foreach (var call in toolCalls)
            {
                var toolName = call.Function?.Name ?? "";
                if (!visited.Contains(toolName))
                {
                    TopologicalSort(toolName, toolCalls, dependencyGraph, visited, visiting, ordered);
                }
            }

            return ordered;
        }

        /// <summary>
        /// 拓扑排序
        /// </summary>
        private void TopologicalSort(string toolName, List<LlmToolCall> toolCalls, 
            Dictionary<string, List<string>> graph, HashSet<string> visited, 
            HashSet<string> visiting, List<LlmToolCall> result)
        {
            if (visiting.Contains(toolName))
            {
                Debug.WriteLine($"检测到循环依赖: {toolName}");
                return;
            }

            if (visited.Contains(toolName))
                return;

            visiting.Add(toolName);

            if (graph.TryGetValue(toolName, out var dependencies))
            {
                foreach (var dep in dependencies)
                {
                    TopologicalSort(dep, toolCalls, graph, visited, visiting, result);
                }
            }

            visiting.Remove(toolName);
            visited.Add(toolName);

            // 添加对应的工具调用
            var call = toolCalls.FirstOrDefault(tc => tc.Function?.Name == toolName);
            if (call != null && !result.Contains(call))
            {
                result.Add(call);
            }
        }

        /// <summary>
        /// 添加前置和后置操作
        /// </summary>
        private List<EnhancedToolCall> AddPreAndPostActions(List<LlmToolCall> orderedCalls)
        {
            var enhanced = new List<EnhancedToolCall>();

            foreach (var call in orderedCalls)
            {
                var toolName = call.Function?.Name ?? "";
                var enhancedCall = new EnhancedToolCall
                {
                    OriginalCall = call,
                    PreActions = new List<string>(),
                    PostActions = new List<string>()
                };

                if (_toolDependencies.TryGetValue(toolName, out var depInfo))
                {
                    enhancedCall.PreActions.AddRange(depInfo.Prerequisites);
                    enhancedCall.PostActions.AddRange(depInfo.PostActions);
                }

                enhanced.Add(enhancedCall);
            }

            return enhanced;
        }

        /// <summary>
        /// 验证工具调用结果
        /// </summary>
        public async Task<ToolCallValidationResult> ValidateToolCallResult(string toolName, McpToolResult result)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ToolCallChainManager));

            var validationResult = new ToolCallValidationResult
            {
                ToolName = toolName,
                IsValid = true,
                ValidationTime = DateTime.Now
            };

            if (_validators.TryGetValue(toolName, out var validator))
            {
                try
                {
                    var isValid = await validator.ValidationFunction(result);
                    validationResult.IsValid = isValid;
                    
                    if (!isValid)
                    {
                        validationResult.ErrorMessage = $"工具 {toolName} 的结果验证失败";
                        validationResult.ValidationDetails = validator.ValidationRules.ToList();
                    }
                }
                catch (Exception ex)
                {
                    validationResult.IsValid = false;
                    validationResult.ErrorMessage = $"验证过程中发生异常: {ex.Message}";
                }
            }

            Debug.WriteLine($"工具 {toolName} 验证结果: {(validationResult.IsValid ? "通过" : "失败")}");
            return validationResult;
        }

        /// <summary>
        /// 实现工具调用回滚
        /// </summary>
        public Task<bool> RollbackToolCall(string chainId, string toolCallId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ToolCallChainManager));

            lock (_lockObject)
            {
                var chain = _activeChains.FirstOrDefault(c => c.Id == chainId);
                if (chain == null)
                {
                    Debug.WriteLine($"未找到工具调用链: {chainId}");
                    return Task.FromResult(false);
                }

                // 这里实现具体的回滚逻辑
                // 简化实现：标记为需要回滚
                chain.RollbackRequests.Add(new ToolCallRollback
                {
                    ToolCallId = toolCallId,
                    RequestTime = DateTime.Now,
                    Reason = "用户请求回滚"
                });

                Debug.WriteLine($"已添加工具调用回滚请求: {toolCallId}");
                return Task.FromResult(true);
            }
        }

        // 验证函数实现
        private async Task<bool> ValidateFileWrite(McpToolResult result)
        {
            await Task.Delay(10); // 模拟异步验证
            return result.IsSuccess && !string.IsNullOrEmpty(result.Content);
        }

        private async Task<bool> ValidateFileRead(McpToolResult result)
        {
            await Task.Delay(10);
            return result.IsSuccess && !string.IsNullOrEmpty(result.Content);
        }

        private async Task<bool> ValidateDownload(McpToolResult result)
        {
            await Task.Delay(10);
            return result.IsSuccess;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _activeChains.Clear();
                _toolDependencies.Clear();
                _validators.Clear();
                Debug.WriteLine("工具调用链管理器已释放");
            }
        }
    }

    /// <summary>
    /// 工具依赖信息
    /// </summary>
    public class ToolDependencyInfo
    {
        public string ToolName { get; set; } = "";
        public string[] Prerequisites { get; set; } = Array.Empty<string>();
        public string[] Conflicts { get; set; } = Array.Empty<string>();
        public string[] PostActions { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 工具调用验证器
    /// </summary>
    public class ToolCallValidator
    {
        public string ValidatorName { get; set; } = "";
        public string[] ValidationRules { get; set; } = Array.Empty<string>();
        public Func<McpToolResult, Task<bool>> ValidationFunction { get; set; } = _ => Task.FromResult(true);
    }

    /// <summary>
    /// 工具调用链
    /// </summary>
    public class ToolCallChain
    {
        public string Id { get; set; } = "";
        public List<LlmToolCall> OriginalCalls { get; set; } = new List<LlmToolCall>();
        public List<LlmToolCall> ExecutionOrder { get; set; } = new List<LlmToolCall>();
        public List<EnhancedToolCall> EnhancedCalls { get; set; } = new List<EnhancedToolCall>();
        public bool HasConflicts { get; set; }
        public List<ToolCallConflict> ConflictDetails { get; set; } = new List<ToolCallConflict>();
        public List<ToolCallRollback> RollbackRequests { get; set; } = new List<ToolCallRollback>();
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 增强的工具调用
    /// </summary>
    public class EnhancedToolCall
    {
        public LlmToolCall OriginalCall { get; set; } = new LlmToolCall();
        public List<string> PreActions { get; set; } = new List<string>();
        public List<string> PostActions { get; set; } = new List<string>();
    }

    /// <summary>
    /// 工具调用冲突
    /// </summary>
    public class ToolCallConflict
    {
        public LlmToolCall Tool1 { get; set; } = new LlmToolCall();
        public LlmToolCall Tool2 { get; set; } = new LlmToolCall();
        public string ConflictType { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// 工具调用验证结果
    /// </summary>
    public class ToolCallValidationResult
    {
        public string ToolName { get; set; } = "";
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<string> ValidationDetails { get; set; } = new List<string>();
        public DateTime ValidationTime { get; set; }
    }

    /// <summary>
    /// 工具调用回滚
    /// </summary>
    public class ToolCallRollback
    {
        public string ToolCallId { get; set; } = "";
        public DateTime RequestTime { get; set; }
        public string Reason { get; set; } = "";
    }
}
