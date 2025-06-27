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
        private static readonly string ConfigFileName = "mcp_settings.json";

        public static string GetConfigPath()
        {
            var appDataPath = AppDataHelper.GetAppDataRootPath();
            return Path.Combine(appDataPath, ConfigFileName);
        }
        
        public static async Task<string> LoadRawConfigAsync()
        {
            var configPath = GetConfigPath();
            if (!File.Exists(configPath))
            {
                return "{}"; // 返回一个空的JSON对象
            }
            return await File.ReadAllTextAsync(configPath);
        }

        public static async Task SaveRawConfigAsync(string jsonContent)
        {
            var configPath = GetConfigPath();
            await File.WriteAllTextAsync(configPath, jsonContent);
        }


        public static async Task<Dictionary<string, McpServerDefinition>> LoadConfigsAsync()
        {
            var configPath = GetConfigPath();
            if (!File.Exists(configPath))
            {
                // 如果文件不存在，返回空字典
                return new Dictionary<string, McpServerDefinition>();
            }

            var json = await File.ReadAllTextAsync(configPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                // 如果文件为空，返回空字典
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