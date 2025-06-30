using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lyxie_desktop.Services;
using Lyxie_desktop.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lyxie_desktop.Helpers;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lyxie_desktop.Interfaces;

namespace Lyxie_desktop;

public partial class App : Application
{
    // 全局主题服务实例
    public static ThemeService ThemeService { get; private set; } = new ThemeService();

    // 全局语言服务实例
    public static LanguageService LanguageService { get; private set; } = new LanguageService();
    
    // 全局MCP服务实例
    public static IMcpService McpService { get; private set; } = new McpService();
    
    // 全局MCP服务器管理实例
    public static IMcpServerManager? McpServerManager { get; private set; }
    
    // 全局MCP自动验证服务实例
    public static IMcpAutoValidationService? McpAutoValidationService { get; private set; }
    
    // 全局TTS服务实例
    public static TtsApiService TtsApiService { get; private set; } = new TtsApiService();
    
    // 全局应用程序设置
    public static AppSettings Settings { get; private set; } = new AppSettings();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 在创建主窗口前加载并应用设置
        LoadAndApplySettings();
        
        // 初始化聊天数据库
        InitializeChatDatabase();
        
        // 自动启动MCP服务和验证
        InitializeMcpServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 设置应用关闭模式，使得只有在明确调用 Shutdown() 时应用才会退出。
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            // 创建托盘图标
            CreateTrayIcon(desktop);
            
            desktop.ShutdownRequested += OnShutdown;
            
            // 应用启动时直接打开主窗口
            ShowMainWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var openMenuItem = new NativeMenuItem("打开");
        openMenuItem.Click += (sender, args) => ShowMainWindow(desktop);

        var exitMenuItem = new NativeMenuItem("退出");
        exitMenuItem.Click += (sender, args) => desktop.Shutdown();

        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Lyxie-desktop/Lyxie.ico"))),
            ToolTipText = "Lyxie",
            Menu = new NativeMenu
            {
                Items =
                {
                    openMenuItem,
                    new NativeMenuItemSeparator(),
                    exitMenuItem
                }
            }
        };

        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow == null)
        {
            desktop.MainWindow = new MainWindow();
            // 当主窗口关闭时，我们只是隐藏它而不是关闭应用
            desktop.MainWindow.Closing += (s, e) =>
            {
                if (e is WindowClosingEventArgs closingEventArgs)
                {
                    closingEventArgs.Cancel = true;
                }
                desktop.MainWindow.Hide();
            };
        }
        
        desktop.MainWindow.Show();
        desktop.MainWindow.WindowState = WindowState.Normal;
        desktop.MainWindow.Activate();
    }

    private async void OnShutdown(object? sender, ShutdownRequestedEventArgs e)
    {
        // 停止MCP服务
        try
        {
            await McpService.StopAutoValidationAsync();
            await McpService.StopAllServersAsync();
            
            // 释放服务资源
            (McpService as IDisposable)?.Dispose();
            
            System.Diagnostics.Debug.WriteLine("MCP服务已停止并释放资源");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to stop MCP services: {ex.Message}");
        }
        
        // 清理TTS缓存目录
        try
        {
            var tempPath = AppDataHelper.GetTempPath();
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to clear TTS cache: {ex.Message}");
        }
    }

    // 加载并应用设置
    private void LoadAndApplySettings()
    {
        var settingsFile = AppDataHelper.GetSettingsFilePath();
        if (File.Exists(settingsFile))
        {
            try
            {
                var json = File.ReadAllText(settingsFile);
                var settings = JsonSerializer.Deserialize<Views.AppSettings>(json);

                if (settings != null)
                {
                    // 保存到静态Settings属性
                    Settings = settings;
                    
                    // 应用主题设置
                    var themeMode = ThemeService.GetThemeModeFromIndex(settings.ThemeIndex);
                    ThemeService.InitializeTheme(themeMode);

                    // 应用语言设置
                    var language = LanguageService.GetLanguageFromIndex(settings.LanguageIndex);
                    LanguageService.SetLanguage(language);
                }
            }
            catch (Exception ex)
            {
                // 如果加载失败，使用默认设置
                Console.WriteLine($"Failed to load settings: {ex.Message}");
                ThemeService.InitializeTheme(ThemeMode.System);
                LanguageService.SetLanguage(Language.SimplifiedChinese);
            }
        }
        else
        {
            // 如果设置文件不存在，使用默认设置
            ThemeService.InitializeTheme(ThemeMode.System);
            LanguageService.SetLanguage(Language.SimplifiedChinese);
        }
    }
    
    // 初始化聊天数据库
    private async void InitializeChatDatabase()
    {
        try
        {
            await ChatDataHelper.InitializeDatabaseAsync();
            System.Diagnostics.Debug.WriteLine("聊天数据库初始化成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize chat database: {ex.Message}");
        }
    }
    
    // 初始化MCP服务
    private async void InitializeMcpServices()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("正在启动MCP服务...");
            
            // 首先获取所有配置
            var configs = await McpService.GetConfigsAsync();
            
            // 确保filesystem服务器配置存在
            if (!configs.ContainsKey("filesystem"))
            {
                // 创建默认配置
                configs["filesystem"] = new Models.McpServerDefinition
                {
                    Command = "npx",
                    Args = new List<string> { "-y", "@modelcontextprotocol/server-filesystem", "D:\\Projects" },
                    IsEnabled = Settings.EnableDev2, // 根据设置决定是否启用
                    AutoValidationEnabled = false,   // 默认不自动验证
                    ValidationInterval = 60          // 验证间隔60秒
                };
                await McpService.SaveConfigsAsync(configs);
                System.Diagnostics.Debug.WriteLine("已创建MCP filesystem服务默认配置");
            }
            else
            {
                // 更新现有配置状态
                configs["filesystem"].IsEnabled = Settings.EnableDev2;
                await McpService.SaveConfigsAsync(configs);
                System.Diagnostics.Debug.WriteLine($"已更新MCP filesystem服务配置: 启用状态={Settings.EnableDev2}");
            }
            
            // 只启动已启用的MCP服务器
            var startResults = await McpService.StartAllServersAsync();
            var successCount = startResults.Count(r => r.Value);
            System.Diagnostics.Debug.WriteLine($"MCP服务器启动完成: {successCount}/{startResults.Count} 个服务器启动成功");
            
            // 启动自动验证
            await McpService.StartAutoValidationAsync();
            System.Diagnostics.Debug.WriteLine("MCP自动验证已启动");
            
            // 跳过额外验证，避免资源浪费
            System.Diagnostics.Debug.WriteLine("跳过额外的初始化验证，由UI控件根据需要触发验证");

            // 从 McpService 获取其他服务实例
            McpServerManager = McpService.ServerManager;
            McpAutoValidationService = McpService.AutoValidationService;

            // 启动自动验证服务并更新配置
            _ = Task.Run(async () =>
            {
                await McpAutoValidationService.StartAsync();
                var configs = await McpService.GetConfigsAsync();
                await McpAutoValidationService.UpdateConfigurationAsync(configs);
            });
            
            // 初始化TTS服务
            // TtsApiService = new TtsApiService(); // 已在属性声明时初始化
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize MCP services: {ex.Message}");
        }
    }
    
    // 保存设置
    public static void SaveSettings()
    {
        var settingsFile = AppDataHelper.GetSettingsFilePath();
        try
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}