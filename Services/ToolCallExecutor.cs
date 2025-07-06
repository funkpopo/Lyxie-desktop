using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// 工具调用执行器 - 负责执行LLM返回的工具调用指令
    /// </summary>
    public class ToolCallExecutor
    {
        private readonly IMcpToolManager _mcpToolManager;

        public event EventHandler<ToolCallExecutionEventArgs>? ToolCallExecutionStarted;
        public event EventHandler<ToolCallExecutionEventArgs>? ToolCallExecutionCompleted;
        public event EventHandler<ToolCallExecutionEventArgs>? ToolCallExecutionFailed;

        public ToolCallExecutor(IMcpToolManager mcpToolManager)
        {
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
        }

        /// <summary>
        /// 执行LLM工具调用指令列表
        /// </summary>
        /// <param name="llmToolCalls">LLM返回的工具调用指令</param>
        /// <param name="availableTools">可用的MCP工具</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>工具调用执行结果列表</returns>
        public async Task<List<ToolCallExecution>> ExecuteToolCallsAsync(
            List<LlmToolCall> llmToolCalls,
            List<McpTool> availableTools,
            CancellationToken cancellationToken = default)
        {
            var executions = new List<ToolCallExecution>();

            if (llmToolCalls.Count == 0)
            {
                Debug.WriteLine("没有工具调用需要执行");
                return executions;
            }

            Debug.WriteLine($"开始执行 {llmToolCalls.Count} 个工具调用");

            // 1. 使用工具调用链管理器分析依赖关系
            ToolCallChain? chain = null;
            if (App.ToolCallChainManager != null)
            {
                chain = App.ToolCallChainManager.AnalyzeDependencies(llmToolCalls);

                if (chain.HasConflicts)
                {
                    Debug.WriteLine($"检测到工具调用冲突，将按原顺序执行");
                    foreach (var conflict in chain.ConflictDetails)
                    {
                        Debug.WriteLine($"冲突: {conflict.Description}");
                    }
                }
                else
                {
                    Debug.WriteLine($"工具调用链分析完成，优化后执行顺序包含 {chain.EnhancedCalls.Count} 个步骤");
                }
            }

            // 2. 根据分析结果决定执行策略
            var toolCallsToExecute = chain?.HasConflicts == false && chain.EnhancedCalls.Count > 0
                ? chain.EnhancedCalls.Select(ec => ec.OriginalCall).ToList()
                : llmToolCalls;

            // 3. 执行工具调用（如果有冲突则串行执行，否则并行执行）
            if (chain?.HasConflicts == true)
            {
                Debug.WriteLine("检测到冲突，使用串行执行模式");
                return await ExecuteToolCallsSequentially(toolCallsToExecute, availableTools, chain, cancellationToken);
            }
            else
            {
                Debug.WriteLine("使用并行执行模式");
                return await ExecuteToolCallsInParallel(toolCallsToExecute, availableTools, chain, cancellationToken);
            }
        }

        /// <summary>
        /// 执行单个工具调用
        /// </summary>
        private async Task<ToolCallExecution> ExecuteSingleToolCall(
            LlmToolCall toolCall,
            List<McpTool> availableTools,
            CancellationToken cancellationToken)
        {
            var execution = new ToolCallExecution
            {
                LlmToolCall = toolCall,
                StartTime = DateTime.Now,
                Status = ToolExecutionStatus.Pending
            };

            // 开始计时
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                execution.Status = ToolExecutionStatus.Executing;

                // 触发开始事件
                ToolCallExecutionStarted?.Invoke(this, new ToolCallExecutionEventArgs(execution));

                Debug.WriteLine($"执行工具调用: {toolCall.Function?.Name} (ID: {toolCall.Id})");

                // 解析工具调用参数
                var mcpToolCall = ConvertToMcpToolCall(toolCall, availableTools);
                if (mcpToolCall == null)
                {
                    execution.Status = ToolExecutionStatus.Failed;
                    execution.ErrorMessage = $"无法找到工具 '{toolCall.Function?.Name}' 或参数解析失败";
                    execution.EndTime = DateTime.Now;

                    ToolCallExecutionFailed?.Invoke(this, new ToolCallExecutionEventArgs(execution));
                    return execution;
                }

                // 执行MCP工具调用
                var result = await _mcpToolManager.CallToolAsync(mcpToolCall, cancellationToken);

                execution.McpResult = result;
                execution.Status = result.IsSuccess ? ToolExecutionStatus.Completed : ToolExecutionStatus.Failed;
                execution.EndTime = DateTime.Now;

                // 停止计时并更新统计
                stopwatch.Stop();
                var executionTimeMs = stopwatch.Elapsed.TotalMilliseconds;

                // 更新工具选择优化器的统计信息
                if (App.ToolSelectionOptimizer != null && toolCall.Function?.Name != null)
                {
                    App.ToolSelectionOptimizer.UpdateToolStatistics(
                        toolCall.Function.Name,
                        result.IsSuccess,
                        executionTimeMs
                    );
                }

                if (result.IsSuccess)
                {
                    Debug.WriteLine($"工具调用成功: {toolCall.Function?.Name} (耗时: {executionTimeMs:F0}ms)");
                    ToolCallExecutionCompleted?.Invoke(this, new ToolCallExecutionEventArgs(execution));
                }
                else
                {
                    execution.ErrorMessage = result.ErrorMessage;
                    Debug.WriteLine($"工具调用失败: {toolCall.Function?.Name} - {result.ErrorMessage} (耗时: {executionTimeMs:F0}ms)");
                    ToolCallExecutionFailed?.Invoke(this, new ToolCallExecutionEventArgs(execution));
                }
            }
            catch (Exception ex)
            {
                execution.Status = ToolExecutionStatus.Failed;
                execution.EndTime = DateTime.Now;

                // 停止计时并更新统计（异常情况）
                stopwatch.Stop();
                var executionTimeMs = stopwatch.Elapsed.TotalMilliseconds;

                // 使用错误处理管理器处理异常
                if (App.ErrorHandlingManager != null)
                {
                    var errorResult = await App.ErrorHandlingManager.HandleErrorAsync(
                        ex,
                        $"工具调用: {toolCall.Function?.Name}",
                        "mcp_tool",
                        cancellationToken
                    );
                    execution.ErrorMessage = errorResult.UserMessage;
                    // 记录重试建议但避免递归重试
                }
                else
                {
                    execution.ErrorMessage = ex.Message;
                }

                // 更新工具选择优化器的统计信息（失败）
                if (App.ToolSelectionOptimizer != null && toolCall.Function?.Name != null)
                {
                    App.ToolSelectionOptimizer.UpdateToolStatistics(
                        toolCall.Function.Name,
                        false,
                        executionTimeMs
                    );
                }

                Debug.WriteLine($"工具调用异常: {toolCall.Function?.Name} - {execution.ErrorMessage} (耗时: {executionTimeMs:F0}ms)");
                ToolCallExecutionFailed?.Invoke(this, new ToolCallExecutionEventArgs(execution));
            }

            return execution;
        }

        /// <summary>
        /// 并行执行工具调用
        /// </summary>
        private async Task<List<ToolCallExecution>> ExecuteToolCallsInParallel(
            List<LlmToolCall> toolCalls,
            List<McpTool> availableTools,
            ToolCallChain? chain,
            CancellationToken cancellationToken)
        {
            // 使用并行执行管理器进行高性能并行执行
            if (App.ParallelExecutionManager != null)
            {
                try
                {
                    Debug.WriteLine($"使用并行执行管理器执行 {toolCalls.Count} 个工具调用");

                    var parallelResults = await App.ParallelExecutionManager.ExecuteInParallelAsync(
                        toolCalls,
                        async (toolCall, ct) => await ExecuteSingleToolCall(toolCall, availableTools, ct),
                        cancellationToken
                    );

                    var parallelSuccessCount = parallelResults.Count(e => e.Status == ToolExecutionStatus.Completed);
                    var parallelFailedCount = parallelResults.Count(e => e.Status == ToolExecutionStatus.Failed);

                    Debug.WriteLine($"并行执行管理器完成: 成功 {parallelSuccessCount}，失败 {parallelFailedCount}");
                    return parallelResults;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"并行执行管理器异常: {ex.Message}，回退到传统并行执行");
                    // 回退到传统并行执行
                }
            }

            // 传统并行执行（备用方案）
            var executions = new List<ToolCallExecution>();

            // 创建并发执行任务
            var tasks = toolCalls.Select(async toolCall =>
            {
                return await ExecuteSingleToolCall(toolCall, availableTools, cancellationToken);
            });

            // 等待所有任务完成
            var results = await Task.WhenAll(tasks);
            executions.AddRange(results);

            var successCount = executions.Count(e => e.Status == ToolExecutionStatus.Completed);
            var failedCount = executions.Count(e => e.Status == ToolExecutionStatus.Failed);

            Debug.WriteLine($"传统并行工具调用执行完成: 成功 {successCount}，失败 {failedCount}");
            return executions;
        }

        /// <summary>
        /// 串行执行工具调用
        /// </summary>
        private async Task<List<ToolCallExecution>> ExecuteToolCallsSequentially(
            List<LlmToolCall> toolCalls,
            List<McpTool> availableTools,
            ToolCallChain? chain,
            CancellationToken cancellationToken)
        {
            var executions = new List<ToolCallExecution>();

            foreach (var toolCall in toolCalls)
            {
                var execution = await ExecuteSingleToolCall(toolCall, availableTools, cancellationToken);
                executions.Add(execution);

                // 验证工具调用结果
                if (App.ToolCallChainManager != null && execution.McpResult != null)
                {
                    var validationResult = await App.ToolCallChainManager.ValidateToolCallResult(
                        toolCall.Function?.Name ?? "", execution.McpResult);

                    if (!validationResult.IsValid)
                    {
                        Debug.WriteLine($"工具调用验证失败: {validationResult.ErrorMessage}");
                        execution.Status = ToolExecutionStatus.Failed;
                        execution.ErrorMessage = validationResult.ErrorMessage;

                        // 如果验证失败，可以选择停止后续执行或继续
                        // 这里选择继续执行，但记录错误
                    }
                }

                // 如果当前工具调用失败且是关键步骤，可以考虑回滚
                if (execution.Status == ToolExecutionStatus.Failed && chain != null)
                {
                    Debug.WriteLine($"关键工具调用失败，考虑回滚: {toolCall.Function?.Name}");
                    // 这里可以实现回滚逻辑
                }
            }

            var successCount = executions.Count(e => e.Status == ToolExecutionStatus.Completed);
            var failedCount = executions.Count(e => e.Status == ToolExecutionStatus.Failed);

            Debug.WriteLine($"串行工具调用执行完成: 成功 {successCount}，失败 {failedCount}");
            return executions;
        }



        /// <summary>
        /// 将LLM工具调用转换为MCP工具调用
        /// </summary>
        /// <param name="llmToolCall">LLM工具调用</param>
        /// <param name="availableTools">可用工具列表</param>
        /// <returns>MCP工具调用，如果转换失败返回null</returns>
        private McpToolCall? ConvertToMcpToolCall(LlmToolCall llmToolCall, List<McpTool> availableTools)
        {
            if (llmToolCall.Function == null)
            {
                Debug.WriteLine("LLM工具调用缺少function信息");
                return null;
            }

            var toolName = llmToolCall.Function.Name;
            var tool = availableTools.FirstOrDefault(t => t.Name == toolName);
            
            if (tool == null)
            {
                Debug.WriteLine($"未找到工具: {toolName}");
                return null;
            }

            Dictionary<string, object>? parameters = null;
            
            // 解析参数JSON字符串
            if (!string.IsNullOrEmpty(llmToolCall.Function.Arguments))
            {
                try
                {
                    parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(llmToolCall.Function.Arguments);
                    Debug.WriteLine($"解析工具参数成功: {toolName}，参数数量: {parameters?.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"解析工具参数失败: {toolName} - {ex.Message}");
                    Debug.WriteLine($"参数字符串: {llmToolCall.Function.Arguments}");
                    return null;
                }
            }

            return new McpToolCall
            {
                Id = llmToolCall.Id,
                ServerName = tool.ServerName,
                ToolName = tool.Name,
                Parameters = parameters
            };
        }

        /// <summary>
        /// 将工具调用执行结果转换为对话消息
        /// </summary>
        /// <param name="executions">工具调用执行结果</param>
        /// <returns>工具调用结果消息列表</returns>
        public List<ConversationMessage> ConvertExecutionsToMessages(List<ToolCallExecution> executions)
        {
            var messages = new List<ConversationMessage>();

            foreach (var execution in executions)
            {
                var message = new ConversationMessage
                {
                    Role = "tool",
                    ToolCallId = execution.LlmToolCall.Id
                };

                if (execution.Status == ToolExecutionStatus.Completed && execution.McpResult?.IsSuccess == true)
                {
                    message.Content = execution.McpResult.Content ?? "工具执行成功，但未返回内容";
                }
                else
                {
                    var errorMsg = execution.ErrorMessage ?? execution.McpResult?.ErrorMessage ?? "未知错误";
                    message.Content = $"工具执行失败: {errorMsg}";
                }

                messages.Add(message);
            }

            return messages;
        }

        /// <summary>
        /// 格式化工具调用执行结果摘要
        /// </summary>
        /// <param name="executions">工具调用执行结果</param>
        /// <returns>格式化的摘要文本</returns>
        public string FormatExecutionSummary(List<ToolCallExecution> executions)
        {
            if (executions.Count == 0)
                return string.Empty;

            var summary = new List<string>();
            var successCount = executions.Count(e => e.Status == ToolExecutionStatus.Completed);
            var failedCount = executions.Count(e => e.Status == ToolExecutionStatus.Failed);

            summary.Add($"🔧 执行了 {executions.Count} 个工具调用");
            
            if (successCount > 0)
                summary.Add($"✅ 成功: {successCount}");
            
            if (failedCount > 0)
                summary.Add($"❌ 失败: {failedCount}");

            // 添加成功工具的详细信息
            var successfulExecutions = executions.Where(e => e.Status == ToolExecutionStatus.Completed).ToList();
            if (successfulExecutions.Count > 0)
            {
                summary.Add("\n执行成功的工具:");
                foreach (var execution in successfulExecutions)
                {
                    var toolName = execution.LlmToolCall.Function?.Name ?? "未知工具";
                    summary.Add($"  • {toolName}");
                }
            }

            // 添加失败工具的错误信息
            var failedExecutions = executions.Where(e => e.Status == ToolExecutionStatus.Failed).ToList();
            if (failedExecutions.Count > 0)
            {
                summary.Add("\n执行失败的工具:");
                foreach (var execution in failedExecutions)
                {
                    var toolName = execution.LlmToolCall.Function?.Name ?? "未知工具";
                    var error = execution.ErrorMessage ?? "未知错误";
                    summary.Add($"  • {toolName}: {error}");
                }
            }

            return string.Join(" ", summary);
        }
    }

    /// <summary>
    /// 工具调用执行事件参数
    /// </summary>
    public class ToolCallExecutionEventArgs : EventArgs
    {
        public ToolCallExecution Execution { get; }

        public ToolCallExecutionEventArgs(ToolCallExecution execution)
        {
            Execution = execution;
        }
    }
} 