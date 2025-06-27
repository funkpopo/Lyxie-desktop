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

        public static async Task<List<McpServerConfig>> LoadConfigsAsync()
        {
            var configPath = GetConfigPath();
            if (!File.Exists(configPath))
            {
                return new List<McpServerConfig>();
            }

            var json = await File.ReadAllTextAsync(configPath);
            return JsonConvert.DeserializeObject<List<McpServerConfig>>(json) ?? new List<McpServerConfig>();
        }

        public static async Task SaveConfigsAsync(List<McpServerConfig> configs)
        {
            var configPath = GetConfigPath();
            var json = JsonConvert.SerializeObject(configs, Formatting.Indented);
            await File.WriteAllTextAsync(configPath, json);
        }
    }
} 