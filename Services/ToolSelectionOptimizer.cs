using Lyxie_desktop.Interfaces;
using Lyxie_desktop.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Lyxie_desktop.Services
{
    /// <summary>
    /// 工具选择优化器 - 提升LLM工具选择决策的智能性
    /// </summary>
    public class ToolSelectionOptimizer : IDisposable
    {
        private readonly IToolCallLogger _toolCallLogger;
        private readonly Dictionary<string, ToolUsageStatistics> _toolStats;
        private readonly Dictionary<string, List<string>> _toolCategories;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public ToolSelectionOptimizer(IToolCallLogger toolCallLogger)
        {
            _toolCallLogger = toolCallLogger ?? throw new ArgumentNullException(nameof(toolCallLogger));
            _toolStats = new Dictionary<string, ToolUsageStatistics>();
            _toolCategories = new Dictionary<string, List<string>>();
            
            InitializeToolCategories();
            Debug.WriteLine("工具选择优化器已初始化");
        }

        /// <summary>
        /// 初始化工具分类
        /// </summary>
        private void InitializeToolCategories()
        {
            _toolCategories["文件操作"] = new List<string> { "read_file", "write_file", "list_directory", "create_directory" };
            _toolCategories["系统信息"] = new List<string> { "get_system_info", "get_process_list", "get_environment" };
            _toolCategories["网络请求"] = new List<string> { "http_get", "http_post", "download_file" };
            _toolCategories["数据处理"] = new List<string> { "parse_json", "parse_xml", "format_data" };
            _toolCategories["搜索查询"] = new List<string> { "search_files", "grep_content", "find_pattern" };
        }

        /// <summary>
        /// 生成优化的工具选择提示
        /// </summary>
        public string GenerateToolSelectionPrompt(List<McpTool> availableTools, string userQuery)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ToolSelectionOptimizer));

            var prompt = new List<string>
            {
                "# 工具选择指南",
                "",
                "你有以下工具可用，请根据用户需求智能选择最合适的工具："
            };

            // 按类别组织工具
            var categorizedTools = CategorizeTools(availableTools);
            
            foreach (var category in categorizedTools)
            {
                prompt.Add($"\n## {category.Key}");
                foreach (var tool in category.Value)
                {
                    var stats = GetToolStatistics(tool.Name);
                    var reliability = CalculateReliabilityScore(stats);
                    var relevance = CalculateRelevanceScore(tool, userQuery);
                    
                    prompt.Add($"- **{tool.Name}**: {tool.Description}");
                    prompt.Add($"  - 可靠性: {reliability:P0} | 相关性: {relevance:P0}");
                    
                    if (stats.TotalCalls > 0)
                    {
                        prompt.Add($"  - 使用统计: {stats.SuccessfulCalls}/{stats.TotalCalls} 成功");
                    }
                }
            }

            prompt.AddRange(new[]
            {
                "",
                "## 工具调用指导原则",
                "1. **一次性完成**: 请在一次工具调用中获取所有必要信息，然后直接提供完整回答",
                "2. **优先选择**: 选择高可靠性和高相关性的工具",
                "3. **避免重复**: 不要调用功能重复的工具",
                "4. **逻辑顺序**: 按逻辑顺序安排工具调用",
                "5. **完整回复**: 工具调用完成后，请基于结果直接回答用户问题，无需再次调用工具",
                "",
                "**重要**: 请在获得工具调用结果后，立即为用户提供完整、详细的回答。不要要求进一步的工具调用。",
                ""
            });

            return string.Join("\n", prompt);
        }

        /// <summary>
        /// 按类别组织工具
        /// </summary>
        private Dictionary<string, List<McpTool>> CategorizeTools(List<McpTool> tools)
        {
            var categorized = new Dictionary<string, List<McpTool>>();
            var uncategorized = new List<McpTool>();

            foreach (var tool in tools)
            {
                bool categorized_flag = false;
                foreach (var category in _toolCategories)
                {
                    if (category.Value.Any(pattern => tool.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!categorized.ContainsKey(category.Key))
                            categorized[category.Key] = new List<McpTool>();
                        
                        categorized[category.Key].Add(tool);
                        categorized_flag = true;
                        break;
                    }
                }
                
                if (!categorized_flag)
                {
                    uncategorized.Add(tool);
                }
            }

            // 添加未分类的工具
            if (uncategorized.Count > 0)
            {
                categorized["其他工具"] = uncategorized;
            }

            return categorized;
        }

        /// <summary>
        /// 获取工具使用统计
        /// </summary>
        private ToolUsageStatistics GetToolStatistics(string toolName)
        {
            lock (_lockObject)
            {
                if (_toolStats.TryGetValue(toolName, out var stats))
                {
                    return stats;
                }

                // 从日志中计算统计信息
                var recentLogs = _toolCallLogger.GetRecentLogs(100);
                var toolLogs = recentLogs.Where(log => log.ToolName == toolName).ToList();

                var newStats = new ToolUsageStatistics
                {
                    ToolName = toolName,
                    TotalCalls = toolLogs.Count,
                    SuccessfulCalls = toolLogs.Count(log => log.Status == ToolExecutionStatus.Completed),
                    FailedCalls = toolLogs.Count(log => log.Status == ToolExecutionStatus.Failed),
                    AverageExecutionTime = toolLogs.Where(log => log.DurationMs > 0).DefaultIfEmpty().Average(log => log?.DurationMs ?? 0),
                    LastUsed = toolLogs.OrderByDescending(log => log.StartTime).FirstOrDefault()?.StartTime
                };

                _toolStats[toolName] = newStats;
                return newStats;
            }
        }

        /// <summary>
        /// 计算工具可靠性评分
        /// </summary>
        private double CalculateReliabilityScore(ToolUsageStatistics stats)
        {
            if (stats.TotalCalls == 0)
                return 0.5; // 未使用过的工具给予中等评分

            var successRate = (double)stats.SuccessfulCalls / stats.TotalCalls;
            
            // 考虑使用频次的权重
            var usageWeight = Math.Min(1.0, stats.TotalCalls / 10.0);
            
            // 考虑执行时间的影响（执行时间越短越好）
            var timeWeight = stats.AverageExecutionTime > 0 ? 
                Math.Max(0.1, 1.0 - (stats.AverageExecutionTime / 10000.0)) : 1.0;

            return successRate * 0.7 + usageWeight * 0.2 + timeWeight * 0.1;
        }

        /// <summary>
        /// 计算工具与查询的相关性评分
        /// </summary>
        private double CalculateRelevanceScore(McpTool tool, string userQuery)
        {
            if (string.IsNullOrEmpty(userQuery))
                return 0.3; // 提高默认分数

            var query = userQuery.ToLowerInvariant();
            var toolName = tool.Name.ToLowerInvariant();
            var toolDesc = tool.Description?.ToLowerInvariant() ?? "";

            double score = 0.0;

            // 工具名称匹配（提高权重）
            if (query.Contains(toolName) || toolName.Contains(query))
                score += 0.5;

            // 部分名称匹配
            var toolNameParts = toolName.Split('_', '-', ' ');
            foreach (var part in toolNameParts)
            {
                if (query.Contains(part) && part.Length > 2)
                    score += 0.2;
            }

            // 描述匹配（提高权重）
            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var descWords = toolDesc.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var matchingWords = queryWords.Intersect(descWords).Count();
            if (queryWords.Length > 0)
            {
                score += (double)matchingWords / queryWords.Length * 0.4;
            }

            // 关键词匹配（提高权重）
            var keywords = ExtractKeywords(query);
            foreach (var keyword in keywords)
            {
                if (toolName.Contains(keyword) || toolDesc.Contains(keyword))
                {
                    score += 0.15;
                }
            }

            // 通用动作词匹配
            var actionWords = new[] { "获取", "查询", "搜索", "查找", "创建", "生成", "执行", "运行", "处理", "分析" };
            foreach (var action in actionWords)
            {
                if (query.Contains(action))
                {
                    score += 0.1; // 任何动作词都增加基础分数
                    break;
                }
            }

            // 如果查询包含问号，增加工具调用倾向
            if (query.Contains("?") || query.Contains("？"))
            {
                score += 0.1;
            }

            return Math.Min(1.0, score);
        }

        /// <summary>
        /// 提取查询中的关键词
        /// </summary>
        private List<string> ExtractKeywords(string query)
        {
            var keywords = new List<string>();
            
            // 文件操作关键词
            if (query.Contains("文件") || query.Contains("file"))
                keywords.Add("file");
            
            if (query.Contains("读取") || query.Contains("read"))
                keywords.Add("read");
                
            if (query.Contains("写入") || query.Contains("write"))
                keywords.Add("write");
                
            if (query.Contains("目录") || query.Contains("文件夹") || query.Contains("directory"))
                keywords.Add("directory");

            // 系统操作关键词
            if (query.Contains("系统") || query.Contains("system"))
                keywords.Add("system");
                
            if (query.Contains("进程") || query.Contains("process"))
                keywords.Add("process");

            // 网络操作关键词
            if (query.Contains("下载") || query.Contains("download"))
                keywords.Add("download");

            if (query.Contains("请求") || query.Contains("http") || query.Contains("api") || query.Contains("网络"))
                keywords.Add("http");

            // 数据操作关键词
            if (query.Contains("数据") || query.Contains("data"))
                keywords.Add("data");

            if (query.Contains("搜索") || query.Contains("search") || query.Contains("查找"))
                keywords.Add("search");

            if (query.Contains("分析") || query.Contains("analyze") || query.Contains("analysis"))
                keywords.Add("analyze");

            // 工具操作关键词
            if (query.Contains("工具") || query.Contains("tool"))
                keywords.Add("tool");

            if (query.Contains("命令") || query.Contains("command") || query.Contains("执行"))
                keywords.Add("command");

            // 信息获取关键词
            if (query.Contains("信息") || query.Contains("info") || query.Contains("详情"))
                keywords.Add("info");

            return keywords;
        }

        /// <summary>
        /// 动态调整tool_choice策略
        /// </summary>
        public string GetOptimalToolChoice(List<McpTool> availableTools, string userQuery)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ToolSelectionOptimizer));

            // 分析查询复杂度
            var queryComplexity = AnalyzeQueryComplexity(userQuery);
            
            // 计算工具相关性（降低阈值以增加工具调用机会）
            var relevantTools = availableTools
                .Where(tool => CalculateRelevanceScore(tool, userQuery) > 0.1)
                .ToList();

            Debug.WriteLine($"工具选择优化器: 在{availableTools.Count}个可用工具中找到{relevantTools.Count}个相关工具");
            foreach (var tool in relevantTools)
            {
                var score = CalculateRelevanceScore(tool, userQuery);
                Debug.WriteLine($"  - 相关工具: {tool.Name} (相关性: {score:F2})");
            }

            string toolChoice;
            if (relevantTools.Count == 0)
            {
                // 即使没有明显相关的工具，也给LLM选择的机会
                toolChoice = queryComplexity > 0.2 ? "auto" : "none";
                Debug.WriteLine($"工具选择决策: 无相关工具，复杂度{queryComplexity:F2} -> {toolChoice}");
            }
            else if (relevantTools.Count == 1 && queryComplexity > 0.3)
            {
                // 有明确相关工具且查询复杂时，建议使用
                toolChoice = "auto"; // 改为auto让LLM决定
                Debug.WriteLine($"工具选择决策: 单个相关工具，复杂度{queryComplexity:F2} -> {toolChoice}");
            }
            else
            {
                // 多个相关工具或复杂查询，让LLM自动选择
                toolChoice = "auto";
                Debug.WriteLine($"工具选择决策: {relevantTools.Count}个相关工具，复杂度{queryComplexity:F2} -> {toolChoice}");
            }

            return toolChoice;
        }

        /// <summary>
        /// 分析查询复杂度
        /// </summary>
        private double AnalyzeQueryComplexity(string query)
        {
            if (string.IsNullOrEmpty(query))
                return 0.0;

            double complexity = 0.0;

            // 基于长度（降低阈值）
            complexity += Math.Min(0.2, query.Length / 100.0);

            // 基于问号数量（多个问题）
            complexity += Math.Min(0.3, query.Count(c => c == '?' || c == '？') * 0.15);

            // 基于连接词（复杂逻辑）
            var conjunctions = new[] { "和", "或", "但是", "然后", "接着", "同时", "以及", "还有", "另外", "and", "or", "but", "then", "also", "additionally" };
            complexity += Math.Min(0.3, conjunctions.Count(conj => query.Contains(conj, StringComparison.OrdinalIgnoreCase)) * 0.1);

            // 基于动词数量（多个动作，扩展词汇）
            var actionWords = new[] { "读取", "写入", "创建", "删除", "搜索", "查找", "下载", "上传", "分析", "处理", "获取", "生成", "执行", "运行", "计算", "转换", "解析", "验证" };
            complexity += Math.Min(0.3, actionWords.Count(action => query.Contains(action, StringComparison.OrdinalIgnoreCase)) * 0.08);

            // 基于技术词汇（表明需要工具支持）
            var techWords = new[] { "文件", "数据", "API", "系统", "网络", "服务", "配置", "日志", "进程", "命令", "脚本", "代码" };
            complexity += Math.Min(0.2, techWords.Count(tech => query.Contains(tech, StringComparison.OrdinalIgnoreCase)) * 0.05);

            Debug.WriteLine($"查询复杂度分析: '{query}' -> 复杂度: {complexity:F2}");
            return Math.Min(1.0, complexity);
        }

        /// <summary>
        /// 更新工具使用统计
        /// </summary>
        public void UpdateToolStatistics(string toolName, bool success, double executionTimeMs)
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                if (!_toolStats.TryGetValue(toolName, out var stats))
                {
                    stats = new ToolUsageStatistics { ToolName = toolName };
                    _toolStats[toolName] = stats;
                }

                stats.TotalCalls++;
                if (success)
                    stats.SuccessfulCalls++;
                else
                    stats.FailedCalls++;

                // 更新平均执行时间
                if (executionTimeMs > 0)
                {
                    var totalTime = stats.AverageExecutionTime * (stats.TotalCalls - 1) + executionTimeMs;
                    stats.AverageExecutionTime = totalTime / stats.TotalCalls;
                }

                stats.LastUsed = DateTime.Now;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Debug.WriteLine("工具选择优化器已释放");
            }
        }
    }

    /// <summary>
    /// 工具使用统计信息
    /// </summary>
    public class ToolUsageStatistics
    {
        public string ToolName { get; set; } = "";
        public int TotalCalls { get; set; }
        public int SuccessfulCalls { get; set; }
        public int FailedCalls { get; set; }
        public double AverageExecutionTime { get; set; }
        public DateTime? LastUsed { get; set; }
        
        public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 0.0;
    }
}
