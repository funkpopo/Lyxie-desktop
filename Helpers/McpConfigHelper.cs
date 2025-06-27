using Lyxie_desktop.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Lyxie_desktop.Helpers
{
    public static class McpConfigHelper
    {
        private static readonly string ConfigFileName = "mcp_servers.json";

        public static string GetConfigPath()
        {
            var appDataPath = AppDataHelper.GetAppDataRootPath();
            return Path.Combine(appDataPath, ConfigFileName);
        }

        public static async Task<Dictionary<string, McpServerDefinition>> LoadConfigsAsync()
        {
            var configPath = GetConfigPath();
            if (!File.Exists(configPath) || new FileInfo(configPath).Length == 0)
            {
                // Create a default file if it doesn't exist or is empty
                var defaultConfig = new McpConfigRoot
                {
                    McpServers = new Dictionary<string, McpServerDefinition>
                    {
                        {
                            "codelf", new McpServerDefinition
                            {
                                IsEnabled = true,
                                Protocol = "sse",
                                Url = "http://127.0.0.1:30031/mcp"
                            }
                        },
                        {
                            "context7", new McpServerDefinition
                            {
                                IsEnabled = true,
                                Protocol = "sse",
                                Url = "http://127.0.0.1:30032/mcp"
                            }
                        },
                        {
                            "serena", new McpServerDefinition
                            {
                                IsEnabled = true,
                                Protocol = "sse",
                                Url = "http://127.0.0.1:30033/mcp"
                            }
                        }
                    }
                };
                await SaveConfigsAsync(defaultConfig.McpServers);
                return defaultConfig.McpServers;
            }

            var json = await File.ReadAllTextAsync(configPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, McpServerDefinition>();
            }

            var configRoot = JsonConvert.DeserializeObject<McpConfigRoot>(json);
            return configRoot?.McpServers ?? new Dictionary<string, McpServerDefinition>();
        }

        public static async Task SaveConfigsAsync(Dictionary<string, McpServerDefinition> configs)
        {
            var configPath = GetConfigPath();
            var configRoot = new McpConfigRoot { McpServers = configs };
            var json = JsonConvert.SerializeObject(configRoot, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            });
            await File.WriteAllTextAsync(configPath, json);
        }

        
    }
} 