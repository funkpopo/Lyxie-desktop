using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// 对话上下文管理器 - 智能管理多轮对话中的上下文信息
    /// </summary>
    public class ConversationContextManager : IDisposable
    {
        private readonly int _maxTokens;
        private readonly int _maxMessages;
        private readonly double _compressionRatio;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        // 对话状态跟踪
        private ConversationState _currentState;
        private readonly Dictionary<string, object> _contextMetadata;

        public ConversationContextManager(int maxTokens = 8000, int maxMessages = 50, double compressionRatio = 0.7)
        {
            _maxTokens = maxTokens;
            _maxMessages = maxMessages;
            _compressionRatio = compressionRatio;
            _currentState = new ConversationState();
            _contextMetadata = new Dictionary<string, object>();
            
            Debug.WriteLine($"对话上下文管理器已初始化 - 最大Token: {_maxTokens}, 最大消息数: {_maxMessages}");
        }

        /// <summary>
        /// 智能截断对话历史
        /// </summary>
        public List<ConversationMessage> TruncateConversationHistory(List<ConversationMessage> messages)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConversationContextManager));

            lock (_lockObject)
            {
                if (messages.Count <= _maxMessages)
                {
                    var estimatedTokens = EstimateTokenCount(messages);
                    if (estimatedTokens <= _maxTokens)
                    {
                        Debug.WriteLine($"对话历史无需截断 - 消息数: {messages.Count}, 估计Token: {estimatedTokens}");
                        return new List<ConversationMessage>(messages);
                    }
                }

                Debug.WriteLine($"开始智能截断对话历史 - 原始消息数: {messages.Count}");
                
                var truncatedMessages = new List<ConversationMessage>();
                
                // 1. 保留系统消息
                var systemMessages = messages.Where(m => m.Role == "system").ToList();
                truncatedMessages.AddRange(systemMessages);
                
                // 2. 保留最近的重要消息
                var nonSystemMessages = messages.Where(m => m.Role != "system").ToList();
                var importantMessages = SelectImportantMessages(nonSystemMessages);
                
                // 3. 按时间顺序重新排列
                var finalMessages = systemMessages.Concat(importantMessages)
                    .OrderBy(m => GetMessageTimestamp(m))
                    .ToList();

                // 4. 确保不超过Token限制
                finalMessages = EnsureTokenLimit(finalMessages);
                
                Debug.WriteLine($"对话历史截断完成 - 最终消息数: {finalMessages.Count}");
                return finalMessages;
            }
        }

        /// <summary>
        /// 选择重要消息
        /// </summary>
        private List<ConversationMessage> SelectImportantMessages(List<ConversationMessage> messages)
        {
            var importantMessages = new List<ConversationMessage>();
            var messageScores = new Dictionary<ConversationMessage, double>();

            // 计算每条消息的重要性评分
            foreach (var message in messages)
            {
                var score = CalculateMessageImportance(message, messages);
                messageScores[message] = score;
            }

            // 按评分排序并选择重要消息
            var sortedMessages = messageScores.OrderByDescending(kvp => kvp.Value).ToList();
            
            // 保留最近的消息
            var recentCount = Math.Min(10, messages.Count / 2);
            var recentMessages = messages.TakeLast(recentCount).ToList();
            importantMessages.AddRange(recentMessages);

            // 添加高分消息（避免重复）
            var remainingSlots = _maxMessages - importantMessages.Count;
            foreach (var kvp in sortedMessages.Take(remainingSlots))
            {
                if (!importantMessages.Contains(kvp.Key))
                {
                    importantMessages.Add(kvp.Key);
                }
            }

            return importantMessages.OrderBy(m => GetMessageTimestamp(m)).ToList();
        }

        /// <summary>
        /// 计算消息重要性评分
        /// </summary>
        private double CalculateMessageImportance(ConversationMessage message, List<ConversationMessage> allMessages)
        {
            double score = 0.0;

            // 1. 基于消息类型
            switch (message.Role)
            {
                case "user":
                    score += 0.8; // 用户消息很重要
                    break;
                case "assistant":
                    score += 0.6; // AI回复重要
                    break;
                case "tool":
                    score += 0.4; // 工具结果中等重要
                    break;
            }

            // 2. 基于消息长度（适中长度更重要）
            var length = message.Content?.Length ?? 0;
            if (length > 50 && length < 500)
            {
                score += 0.3;
            }
            else if (length >= 500)
            {
                score += 0.1; // 太长的消息可能不太重要
            }

            // 3. 基于关键词
            var content = message.Content?.ToLowerInvariant() ?? "";
            var keywords = new[] { "错误", "失败", "成功", "完成", "重要", "问题", "解决", "结果" };
            var keywordCount = keywords.Count(keyword => content.Contains(keyword));
            score += keywordCount * 0.1;

            // 4. 基于工具调用
            if (message.ToolCalls?.Count > 0)
            {
                score += 0.5; // 包含工具调用的消息更重要
            }

            // 5. 基于时间衰减（越新越重要）
            var messageIndex = allMessages.IndexOf(message);
            if (messageIndex >= 0)
            {
                var recencyScore = (double)messageIndex / allMessages.Count * 0.2;
                score += recencyScore;
            }

            return score;
        }

        /// <summary>
        /// 确保消息列表不超过Token限制
        /// </summary>
        private List<ConversationMessage> EnsureTokenLimit(List<ConversationMessage> messages)
        {
            var result = new List<ConversationMessage>();
            var currentTokens = 0;

            // 从后往前添加消息，确保最新的消息被保留
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i];
                var messageTokens = EstimateTokenCount(new[] { message });
                
                if (currentTokens + messageTokens <= _maxTokens)
                {
                    result.Insert(0, message);
                    currentTokens += messageTokens;
                }
                else
                {
                    // 如果单条消息就超过限制，尝试压缩
                    if (result.Count == 0 && messageTokens > _maxTokens)
                    {
                        var compressedMessage = CompressMessage(message);
                        result.Insert(0, compressedMessage);
                    }
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// 压缩消息内容
        /// </summary>
        private ConversationMessage CompressMessage(ConversationMessage message)
        {
            var compressedMessage = new ConversationMessage
            {
                Role = message.Role,
                ToolCalls = message.ToolCalls,
                ToolCallId = message.ToolCallId
            };

            if (!string.IsNullOrEmpty(message.Content))
            {
                var targetLength = (int)(message.Content.Length * _compressionRatio);
                compressedMessage.Content = CompressText(message.Content, targetLength);
            }

            return compressedMessage;
        }

        /// <summary>
        /// 压缩文本内容
        /// </summary>
        private string CompressText(string text, int targetLength)
        {
            if (text.Length <= targetLength)
                return text;

            // 简单的压缩策略：保留开头和结尾，中间用省略号
            var startLength = targetLength / 3;
            var endLength = targetLength / 3;
            var start = text.Substring(0, Math.Min(startLength, text.Length));
            var end = text.Length > endLength ? text.Substring(text.Length - endLength) : "";
            
            return $"{start}...[内容已压缩]...{end}";
        }

        /// <summary>
        /// 估算Token数量
        /// </summary>
        private int EstimateTokenCount(IEnumerable<ConversationMessage> messages)
        {
            // 简单估算：平均每4个字符约等于1个Token
            var totalChars = messages.Sum(m => 
                (m.Content?.Length ?? 0) + 
                (m.ToolCalls?.Sum(tc => tc.Function?.Arguments?.Length ?? 0) ?? 0) +
                50 // 消息结构开销
            );
            
            return (int)Math.Ceiling(totalChars / 4.0);
        }

        /// <summary>
        /// 获取消息时间戳
        /// </summary>
        private DateTime GetMessageTimestamp(ConversationMessage message)
        {
            // 如果消息有时间戳属性，使用它；否则使用当前时间
            // 这里简化处理，实际应该从消息属性中获取
            return DateTime.Now;
        }

        /// <summary>
        /// 优化工具调用结果的上下文整合
        /// </summary>
        public List<ConversationMessage> OptimizeToolCallResults(List<ConversationMessage> messages)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConversationContextManager));

            var optimizedMessages = new List<ConversationMessage>();
            
            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                
                // 如果是工具调用结果，尝试整合
                if (message.Role == "tool")
                {
                    var integratedMessage = IntegrateToolResult(message, messages, i);
                    optimizedMessages.Add(integratedMessage);
                }
                else
                {
                    optimizedMessages.Add(message);
                }
            }

            return optimizedMessages;
        }

        /// <summary>
        /// 整合工具调用结果
        /// </summary>
        private ConversationMessage IntegrateToolResult(ConversationMessage toolMessage, List<ConversationMessage> allMessages, int currentIndex)
        {
            // 查找相关的工具调用请求
            var relatedCall = FindRelatedToolCall(toolMessage, allMessages, currentIndex);
            
            if (relatedCall != null)
            {
                // 创建整合后的消息
                var integratedContent = $"工具调用结果 ({relatedCall.Function?.Name}): {toolMessage.Content}";
                
                return new ConversationMessage
                {
                    Role = toolMessage.Role,
                    Content = integratedContent,
                    ToolCallId = toolMessage.ToolCallId
                };
            }

            return toolMessage;
        }

        /// <summary>
        /// 查找相关的工具调用
        /// </summary>
        private LlmToolCall? FindRelatedToolCall(ConversationMessage toolMessage, List<ConversationMessage> allMessages, int currentIndex)
        {
            // 向前查找包含工具调用的消息
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                var message = allMessages[i];
                if (message.ToolCalls != null)
                {
                    var matchingCall = message.ToolCalls.FirstOrDefault(tc => tc.Id == toolMessage.ToolCallId);
                    if (matchingCall != null)
                    {
                        return matchingCall;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 更新对话状态
        /// </summary>
        public void UpdateConversationState(string key, object value)
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                _contextMetadata[key] = value;
                _currentState.LastUpdated = DateTime.Now;
            }
        }

        /// <summary>
        /// 获取对话状态
        /// </summary>
        public T? GetConversationState<T>(string key)
        {
            if (_disposed)
                return default(T);

            lock (_lockObject)
            {
                if (_contextMetadata.TryGetValue(key, out var value) && value is T typedValue)
                {
                    return typedValue;
                }
                return default(T);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _contextMetadata.Clear();
                Debug.WriteLine("对话上下文管理器已释放");
            }
        }
    }

    /// <summary>
    /// 对话状态信息
    /// </summary>
    public class ConversationState
    {
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public int MessageCount { get; set; }
        public int ToolCallCount { get; set; }
        public string CurrentTopic { get; set; } = "";
        public Dictionary<string, int> TopicFrequency { get; set; } = new Dictionary<string, int>();
    }
}
