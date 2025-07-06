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
            System.Diagnostics.Debug.WriteLine("正在关闭MCP服务...");

            // 停止服务监控
            if (McpServiceMonitor != null)
            {
                await McpServiceMonitor.StopMonitoringAsync();
                McpServiceMonitor.Dispose();
                System.Diagnostics.Debug.WriteLine("MCP服务监控已停止");
            }

            // 停止自动验证
            await McpService.StopAutoValidationAsync();

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
        const int maxRetries = 3;
        const int retryDelayMs = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"正在启动MCP服务... (尝试 {attempt}/{maxRetries})");

                // 1. 初始化工具调用日志服务
                ToolCallLogger = new ToolCallLogger();
                System.Diagnostics.Debug.WriteLine("工具调用日志服务已初始化");

                // 2. 获取并配置MCP服务器
                await ConfigureMcpServersAsync();

                // 3. 启动MCP服务器
                var startResults = await StartMcpServersAsync();

                // 4. 启动自动验证服务
                await StartAutoValidationAsync();

                // 5. 初始化服务监控
                await InitializeServiceMonitoringAsync();

                System.Diagnostics.Debug.WriteLine("MCP服务初始化完成");
                return; // 成功完成，退出重试循环
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MCP服务初始化失败 (尝试 {attempt}/{maxRetries}): {ex.Message}");

                if (attempt == maxRetries)
                {
                    // 最后一次尝试失败，记录错误但不阻止应用启动
                    Console.WriteLine($"Failed to initialize MCP services after {maxRetries} attempts: {ex.Message}");

                    // 创建一个错误状态的监控服务
                    try
                    {
                        McpServerManager = McpService.ServerManager;
                        var toolManager = new McpToolManager(McpService, McpServerManager);
                        McpServiceMonitor = new McpServiceMonitor(McpService, toolManager);
                        McpServiceMonitor.ResetStatus();
                    }
                    catch
                    {
                        // 忽略监控服务创建失败
                    }

                    return;
                }

                // 等待后重试
                await Task.Delay(retryDelayMs * attempt);
            }
        }
    }

    /// <summary>
    /// 配置MCP服务器
    /// </summary>
    private async Task ConfigureMcpServersAsync()
    {
        System.Diagnostics.Debug.WriteLine("正在配置MCP服务器...");

        var configs = await McpService.GetConfigsAsync();

        // 确保filesystem服务器配置存在
        if (!configs.ContainsKey("filesystem"))
        {
            configs["filesystem"] = new Models.McpServerDefinition
            {
                Command = "npx",
                Args = new List<string> { "-y", "@modelcontextprotocol/server-filesystem", "D:\\Projects" },
                IsEnabled = true, // 默认启用filesystem服务器
                AutoValidationEnabled = false,
                ValidationInterval = 60
            };
            await McpService.SaveConfigsAsync(configs);
            System.Diagnostics.Debug.WriteLine("已创建MCP filesystem服务默认配置（默认启用）");
        }
        else
        {
            // 如果配置已存在，保持当前启用状态，但确保至少有一次是启用的
            if (!configs["filesystem"].IsEnabled)
            {
                configs["filesystem"].IsEnabled = true;
                await McpService.SaveConfigsAsync(configs);
                System.Diagnostics.Debug.WriteLine("已启用MCP filesystem服务");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"MCP filesystem服务配置已存在，启用状态={configs["filesystem"].IsEnabled}");
            }
        }
    }

    /// <summary>
    /// 启动MCP服务器
    /// </summary>
    private async Task<Dictionary<string, bool>> StartMcpServersAsync()
    {
        System.Diagnostics.Debug.WriteLine("正在启动MCP服务器...");

        var startResults = await McpService.StartAllServersAsync();
        var successCount = startResults.Count(r => r.Value);
        var totalCount = startResults.Count;

        System.Diagnostics.Debug.WriteLine($"MCP服务器启动完成: {successCount}/{totalCount} 个服务器启动成功");

        // 记录详细的启动结果
        foreach (var result in startResults)
        {
            var status = result.Value ? "成功" : "失败";
            System.Diagnostics.Debug.WriteLine($"  - {result.Key}: {status}");
        }

        if (successCount == 0 && totalCount > 0)
        {
            throw new InvalidOperationException("所有MCP服务器启动失败");
        }

        return startResults;
    }

    /// <summary>
    /// 启动自动验证服务
    /// </summary>
    private async Task StartAutoValidationAsync()
    {
        System.Diagnostics.Debug.WriteLine("正在启动MCP自动验证服务...");

        // 从 McpService 获取服务实例
        McpServerManager = McpService.ServerManager;
        McpAutoValidationService = McpService.AutoValidationService;

        // 启动自动验证
        await McpService.StartAutoValidationAsync();
        System.Diagnostics.Debug.WriteLine("MCP自动验证已启动");

        // 异步启动自动验证服务并更新配置
        _ = Task.Run(async () =>
        {
            try
            {
                await McpAutoValidationService.StartAsync();
                var configs = await McpService.GetConfigsAsync();
                await McpAutoValidationService.UpdateConfigurationAsync(configs);
                System.Diagnostics.Debug.WriteLine("MCP自动验证服务配置已更新");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"启动自动验证服务异常: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 初始化服务监控
    /// </summary>
    private async Task InitializeServiceMonitoringAsync()
    {
        System.Diagnostics.Debug.WriteLine("正在初始化MCP服务监控...");

        try
        {
            var toolManager = new McpToolManager(McpService, McpServerManager!);
            McpServiceMonitor = new McpServiceMonitor(McpService, toolManager);

            // 初始化工具选择优化器
            if (ToolCallLogger != null)
            {
                ToolSelectionOptimizer = new ToolSelectionOptimizer(ToolCallLogger);
                System.Diagnostics.Debug.WriteLine("工具选择优化器已初始化");
            }

            // 初始化对话上下文管理器
            ConversationContextManager = new ConversationContextManager();
            System.Diagnostics.Debug.WriteLine("对话上下文管理器已初始化");

            // 初始化工具调用链管理器
            ToolCallChainManager = new ToolCallChainManager();
            System.Diagnostics.Debug.WriteLine("工具调用链管理器已初始化");

            // 初始化错误处理管理器
            ErrorHandlingManager = new ErrorHandlingManager();
            System.Diagnostics.Debug.WriteLine("错误处理管理器已初始化");

            // 初始化并行执行管理器
            ParallelExecutionManager = new ParallelExecutionManager(maxConcurrency: 5, maxQueueSize: 100);
            System.Diagnostics.Debug.WriteLine("并行执行管理器已初始化");

            // 启动监控
            await McpServiceMonitor.StartMonitoringAsync();
            System.Diagnostics.Debug.WriteLine("MCP服务监控已启动");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"初始化服务监控失败: {ex.Message}");
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