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

            // 并行执行所有工具调用
            var tasks = llmToolCalls.Select(async toolCall =>
            {
                var execution = new ToolCallExecution
                {
                    LlmToolCall = toolCall,
                    StartTime = DateTime.Now,
                    Status = ToolExecutionStatus.Pending
                };

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

                    if (result.IsSuccess)
                    {
                        Debug.WriteLine($"工具调用成功: {toolCall.Function?.Name}");
                        ToolCallExecutionCompleted?.Invoke(this, new ToolCallExecutionEventArgs(execution));
                    }
                    else
                    {
                        execution.ErrorMessage = result.ErrorMessage;
                        Debug.WriteLine($"工具调用失败: {toolCall.Function?.Name} - {result.ErrorMessage}");
                        ToolCallExecutionFailed?.Invoke(this, new ToolCallExecutionEventArgs(execution));
                    }
                }
                catch (Exception ex)
                {
                    execution.Status = ToolExecutionStatus.Failed;
                    execution.ErrorMessage = ex.Message;
                    execution.EndTime = DateTime.Now;
                    
                    Debug.WriteLine($"工具调用异常: {toolCall.Function?.Name} - {ex.Message}");
                    ToolCallExecutionFailed?.Invoke(this, new ToolCallExecutionEventArgs(execution));
                }

                return execution;
            });

            var results = await Task.WhenAll(tasks);
            executions.AddRange(results);

            var successCount = executions.Count(e => e.Status == ToolExecutionStatus.Completed);
            var failedCount = executions.Count(e => e.Status == ToolExecutionStatus.Failed);
            
            Debug.WriteLine($"工具调用执行完成: 成功 {successCount}，失败 {failedCount}");

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