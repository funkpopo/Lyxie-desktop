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
using System.Diagnostics;

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
    public static IMcpServerManager? McpServerManager => McpService.ServerManager;

    // 全局MCP服务监控实例
    public static IMcpServiceMonitor? McpServiceMonitor { get; private set; }

    // 全局工具调用日志服务实例
    public static IToolCallLogger? ToolCallLogger { get; private set; }

    // 全局工具选择优化器实例
    public static ToolSelectionOptimizer? ToolSelectionOptimizer { get; private set; }

    // 全局对话上下文管理器实例
    public static ConversationContextManager? ConversationContextManager { get; private set; }

    // 全局工具调用链管理器实例
    public static ToolCallChainManager? ToolCallChainManager { get; private set; }

    // 全局错误处理管理器实例
    public static ErrorHandlingManager? ErrorHandlingManager { get; private set; }

    // 全局并行执行管理器实例
    public static ParallelExecutionManager? ParallelExecutionManager { get; private set; }

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
            Debug.WriteLine("正在关闭MCP服务...");

            // 停止服务监控
            if (McpServiceMonitor != null)
            {
                await McpServiceMonitor.StopMonitoringAsync();
                McpServiceMonitor.Dispose();
                Debug.WriteLine("MCP服务监控已停止");
            }

            // 停止所有服务器
            await McpService.StopAllServersAsync();

            // 释放工具调用链管理器
            ToolCallChainManager?.Dispose();

            // 释放错误处理管理器
            ErrorHandlingManager?.Dispose();

            // 释放并行执行管理器
            ParallelExecutionManager?.Dispose();

            // 释放对话上下文管理器
            ConversationContextManager?.Dispose();

            // 释放工具选择优化器
            ToolSelectionOptimizer?.Dispose();

            // 释放工具调用日志服务
            ToolCallLogger?.Dispose();

            // 释放MCP服务资源
            (McpService as IDisposable)?.Dispose();

            Debug.WriteLine("MCP服务已停止并释放资源");
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
            Debug.WriteLine("聊天数据库初始化成功");
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
            // 确保MCP配置文件存在
            await ConfigureMcpServersAsync();
            
            // 启动所有配置为启用的服务器
            await StartMcpServersAsync();

            // 初始化并启动服务监控
            await InitializeServiceMonitoringAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MCP服务初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 配置MCP服务器
    /// </summary>
    private Task ConfigureMcpServersAsync()
    {
        Debug.WriteLine("正在配置MCP服务器... 用户自定义配置，跳过自动生成。");
        // 根据用户反馈，所有MCP配置均由用户自定义。
        // 此处不再自动生成或修改任何配置。
        // 用户需要自行管理 mcp_settings.json 文件。
        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动MCP服务器
    /// </summary>
    private async Task<Dictionary<string, bool>> StartMcpServersAsync()
    {
        try
        {
            var results = await McpService.StartAllServersAsync();
            foreach (var result in results)
            {
                Debug.WriteLine($"MCP服务 '{result.Key}' 启动 {(result.Value ? "成功" : "失败")}");
            }
            return results;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动MCP服务器时发生错误: {ex.Message}");
            return new Dictionary<string, bool>();
        }
    }

    /// <summary>
    /// 初始化服务监控
    /// </summary>
    private async Task InitializeServiceMonitoringAsync()
    {
        Debug.WriteLine("正在初始化MCP服务监控...");

        try
        {
            var toolManager = new McpToolManager(McpService, McpServerManager!);
            McpServiceMonitor = new McpServiceMonitor(McpService, toolManager);

            // 初始化工具调用日志服务
            ToolCallLogger = new ToolCallLogger();
            Debug.WriteLine("工具调用日志服务已初始化");

            // 初始化工具选择优化器
            ToolSelectionOptimizer = new ToolSelectionOptimizer(ToolCallLogger);
            Debug.WriteLine("工具选择优化器已初始化");

            // 初始化对话上下文管理器
            ConversationContextManager = new ConversationContextManager();
            Debug.WriteLine("对话上下文管理器已初始化");

            // 初始化工具调用链管理器
            ToolCallChainManager = new ToolCallChainManager();
            Debug.WriteLine("工具调用链管理器已初始化");

            // 初始化错误处理管理器
            ErrorHandlingManager = new ErrorHandlingManager();
            Debug.WriteLine("错误处理管理器已初始化");

            // 初始化并行执行管理器
            ParallelExecutionManager = new ParallelExecutionManager(maxConcurrency: 5, maxQueueSize: 100);
            Debug.WriteLine("并行执行管理器已初始化");

            // 启动监控
            await McpServiceMonitor.StartMonitoringAsync();
            Debug.WriteLine("MCP服务监控已启动");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"初始化服务监控失败: {ex.Message}");
            throw;
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