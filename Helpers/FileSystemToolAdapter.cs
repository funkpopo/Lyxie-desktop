using Lyxie_desktop.Models;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lyxie_desktop.Helpers
{
    /// <summary>
    /// 文件系统工具适配器，为MCP filesystem服务器提供预定义工具
    /// </summary>
    public static class FileSystemToolAdapter
    {
        /// <summary>
        /// 获取预定义的文件系统工具列表
        /// </summary>
        public static List<McpTool> GetFileSystemTools(string serverName)
        {
            Debug.WriteLine($"为服务器 {serverName} 创建预定义的文件系统工具");
            var tools = new List<McpTool>
            {
                // 列出目录内容工具
                new McpTool
                {
                    Name = "list_directory",
                    Description = "列出指定目录中的文件和子目录",
                    ServerName = serverName,
                    InputSchema = new McpToolInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, McpToolProperty>
                        {
                            ["path"] = new McpToolProperty
                            {
                                Type = "string",
                                Description = "要列出内容的目录路径，默认为当前目录"
                            }
                        },
                        Required = new List<string> { "path" }
                    }
                },
                
                // 读取文件内容工具
                new McpTool
                {
                    Name = "read_file",
                    Description = "读取指定文件的内容",
                    ServerName = serverName,
                    InputSchema = new McpToolInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, McpToolProperty>
                        {
                            ["path"] = new McpToolProperty
                            {
                                Type = "string",
                                Description = "要读取的文件路径"
                            }
                        },
                        Required = new List<string> { "path" }
                    }
                },
                
                // 获取当前工作目录工具
                new McpTool
                {
                    Name = "get_current_directory",
                    Description = "获取当前工作目录路径",
                    ServerName = serverName,
                    InputSchema = new McpToolInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, McpToolProperty>()
                    }
                },
                
                // 检查路径是否存在工具
                new McpTool
                {
                    Name = "path_exists",
                    Description = "检查指定路径是否存在",
                    ServerName = serverName,
                    InputSchema = new McpToolInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, McpToolProperty>
                        {
                            ["path"] = new McpToolProperty
                            {
                                Type = "string",
                                Description = "要检查的路径"
                            }
                        },
                        Required = new List<string> { "path" }
                    }
                },
                
                // 获取文件信息工具
                new McpTool
                {
                    Name = "get_file_info",
                    Description = "获取指定文件的信息（大小、修改时间等）",
                    ServerName = serverName,
                    InputSchema = new McpToolInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, McpToolProperty>
                        {
                            ["path"] = new McpToolProperty
                            {
                                Type = "string",
                                Description = "要获取信息的文件路径"
                            }
                        },
                        Required = new List<string> { "path" }
                    }
                }
            };
            
            Debug.WriteLine($"已创建 {tools.Count} 个预定义文件系统工具");
            return tools;
        }
        
        /// <summary>
        /// 检查服务器名称是否为文件系统服务器
        /// </summary>
        public static bool IsFileSystemServer(string serverName)
        {
            bool isFileSystemServer = serverName.ToLower().Contains("filesystem") || 
                   serverName.ToLower().Contains("file") ||
                   serverName.ToLower().Contains("mcp-filesystem");
            
            Debug.WriteLine($"检查服务器名称 '{serverName}' 是否为文件系统服务器: {isFileSystemServer}");
            return isFileSystemServer;
        }
    }
} 