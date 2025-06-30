using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia;
using Lyxie_desktop.Controls;
using Lyxie_desktop.Helpers;
using Lyxie_desktop.Services;
using Lyxie_desktop.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using Material.Icons;
using Material.Icons.Avalonia;
using Lyxie_desktop.Models;

namespace Lyxie_desktop.Views;

public partial class MainView : UserControl
{
    // 事件：请求显示设置界面
    public event EventHandler? SettingsRequested;

    // 工具面板状态
    private bool _isToolPanelVisible = false;
    private bool _isAnimating = false;

    // 词云控件和光晕动画
    private WordCloudControl? _wordCloudControl;
    private DispatcherTimer? _glowAnimationTimer;
    private bool _isGlowAnimationRunning = false;

    // 边框渐变旋转动画
    private bool _isBorderAnimationRunning = false;
    
    // 光晕动画状态
    private bool _isGlowAnimating = false;

    // 对话界面状态
    private bool _isChatVisible = false;
    private CancellationTokenSource? _cancellationTokenSource;
    
    // TTS功能
    private TtsApiService? _ttsApiService;
    private StringBuilder? _currentAiResponseBuilder;

    // 聊天历史和侧边栏
    private ChatSidebarControl? _chatSidebar;
    private ChatHistory _chatHistory = new();
    private ChatSession? _currentSession;
    
    // MCP工具管理器
    private McpToolManager? _mcpToolManager;

    public MainView()
    {
        InitializeComponent();
        InitializeWordCloud();
        InitializeGlowAnimation();
        InitializeBorderAnimation();

        // 为圆形按钮添加事件
        var button = this.FindControl<Button>("MainCircleButton");
        if (button != null)
        {
            button.Click += OnMainButtonClick;
            button.PointerEntered += OnMainButtonPointerEntered;
            button.PointerExited += OnMainButtonPointerExited;
            button.PointerPressed += OnMainButtonPointerPressed;
            button.PointerReleased += OnMainButtonPointerReleased;
        }

        // 为设置按钮添加点击事件
        var settingsButton = this.FindControl<Button>("SettingsButton");
        if (settingsButton != null)
        {
            settingsButton.Click += OnSettingsButtonClick;
        }

        // 为扳手按钮添加点击事件
        var toolButton = this.FindControl<Button>("ToolButton");
        if (toolButton != null)
        {
            toolButton.Click += OnToolButtonClick;
        }

        // 初始化工具面板开关状态
        InitializeToolToggles();

        // 为开关添加事件
        var ttsToggle = this.FindControl<ToggleSwitch>("TTSToggle");
        if (ttsToggle != null)
        {
            ttsToggle.IsCheckedChanged += OnTTSToggled;
        }

        var dev1Toggle = this.FindControl<ToggleSwitch>("Dev1Toggle");
        if (dev1Toggle != null)
        {
            dev1Toggle.IsCheckedChanged += OnDev1Toggled;
        }

        var dev2Toggle = this.FindControl<ToggleSwitch>("Dev2Toggle");
        if (dev2Toggle != null)
        {
            dev2Toggle.IsCheckedChanged += OnDev2Toggled;
        }

        // 订阅语言变更事件
        App.LanguageService.LanguageChanged += OnLanguageChanged;

        // 初始化界面文本
        UpdateInterfaceTexts();

        // 初始化对话界面
        InitializeChatInterface();
        
        // 初始化LLM API配置显示
        RefreshLlmApiConfig();
        
        // 初始化TTS服务
        InitializeTtsService();
        
        // 初始化聊天历史和侧边栏
        InitializeChatHistory();
        
        // 初始化MCP工具管理器
        InitializeMcpToolManager();
    }
    
    // 初始化工具面板开关状态
    private async void InitializeToolToggles()
    {
        var ttsToggle = this.FindControl<ToggleSwitch>("TTSToggle");
        var dev1Toggle = this.FindControl<ToggleSwitch>("Dev1Toggle");
        var dev2Toggle = this.FindControl<ToggleSwitch>("Dev2Toggle");
        
        if (ttsToggle != null)
            ttsToggle.IsChecked = App.Settings.EnableTTS;
            
        if (dev1Toggle != null)
            dev1Toggle.IsChecked = App.Settings.EnableDev1;
            
        if (dev2Toggle != null)
        {
            // 初始化MCP开关状态，不触发事件
            dev2Toggle.IsChecked = App.Settings.EnableDev2;
            
            // 检查MCP服务状态并更新UI
            try
            {
                // 获取MCP filesystem服务状态
                bool isRunning = App.McpService.GetRunningServers().Contains("filesystem");
                var configs = await App.McpService.GetConfigsAsync();
                
                if (configs.TryGetValue("filesystem", out var config))
                {
                    // 设置UI显示状态
                    if (config.IsEnabled && isRunning)
                    {
                        if (config.IsAvailable)
                        {
                            dev2Toggle.Content = "已启用 - 可用";
                        }
                        else
                        {
                            dev2Toggle.Content = "已启用 - 未验证";
                        }
                    }
                    else if (config.IsEnabled && !isRunning)
                    {
                        dev2Toggle.Content = "已启用 - 未运行";
                    }
                    else
                    {
                        dev2Toggle.Content = "已禁用";
                        // 确保自动验证也被关闭
                        config.AutoValidationEnabled = false;
                        await App.McpService.SaveConfigsAsync(configs);
                    }
                }
                else
                {
                    dev2Toggle.Content = "未配置";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化MCP状态时出错: {ex.Message}");
                dev2Toggle.Content = "状态错误";
            }
        }
    }
    
    private async void OnMainButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isAnimating) return;

        // 通知词云按钮被按下
        _wordCloudControl?.SetButtonState(false, true);

        // 执行对话界面转换动画
        await ShowChatInterface();

        // 恢复词云状态
        _wordCloudControl?.SetButtonState(false, false);
    }

    private void OnMainButtonPointerEntered(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        // 通知词云按钮被悬停
        _wordCloudControl?.SetButtonState(true, false);
        
        // 启动光晕效果
        AnimateButtonGlow(isHover: true, isPressed: false);
    }

    private void OnMainButtonPointerExited(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        // 通知词云按钮悬停结束
        _wordCloudControl?.SetButtonState(false, false);
        
        // 关闭光晕效果
        AnimateButtonGlow(isHover: false, isPressed: false);
    }

    private void OnMainButtonPointerPressed(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        // 按压时的光晕效果
        AnimateButtonGlow(isHover: true, isPressed: true);
    }

    private void OnMainButtonPointerReleased(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        // 释放时恢复悬停光晕
        AnimateButtonGlow(isHover: true, isPressed: false);
    }

    private async void AnimateButtonGlow(bool isHover, bool isPressed)
    {
        // 防止重复动画
        if (_isGlowAnimating) return;
        
        var glowBackground = this.FindControl<Border>("ButtonGlowBackground");
        if (glowBackground?.Effect is BlurEffect blurEffect)
        {
            _isGlowAnimating = true;
            
            try
            {
                double targetOpacity = 0;
                double targetBlurRadius = 0;
                
                if (isHover)
                {
                    targetOpacity = 0.3; // 进一步降低透明度，更加微妙
                    targetBlurRadius = isPressed ? 6 : 15; // 减小模糊半径，更精细的效果
                }

                // 快速响应的动画过渡
                const int steps = 10;
                const int duration = 200; // 减少动画时间
                const int stepDelay = duration / steps;

                double startOpacity = glowBackground.Opacity;
                double startBlurRadius = blurEffect.Radius;

                for (int i = 0; i <= steps; i++)
                {
                    // 检查动画是否应该被中断
                    if (!_isGlowAnimating) break;
                    
                    double progress = (double)i / steps;
                    
                    // 使用更平滑的缓动函数
                    double easedProgress = Math.Sin(progress * Math.PI * 0.5);
                    
                    glowBackground.Opacity = startOpacity + (targetOpacity - startOpacity) * easedProgress;
                    blurEffect.Radius = startBlurRadius + (targetBlurRadius - startBlurRadius) * easedProgress;

                    if (i < steps)
                    {
                        await Task.Delay(stepDelay);
                    }
                }
            }
            catch (Exception)
            {
                // 动画失败时静默处理
            }
            finally
            {
                _isGlowAnimating = false;
            }
        }
    }

    private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        // 触发设置界面请求事件
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnToolButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isAnimating) return;

        var toolPanel = this.FindControl<Border>("ToolPanel");
        if (toolPanel == null) return;

        _isAnimating = true;

        if (_isToolPanelVisible)
        {
            await HideToolPanel();
        }
        else
        {
            await ShowToolPanel();
        }

        _isAnimating = false;
    }

    private void OnLanguageChanged(object? sender, Services.Language language)
    {
        UpdateInterfaceTexts();
    }

    private void UpdateInterfaceTexts()
    {
        var languageService = App.LanguageService;

        var clickToStartTextBlock = this.FindControl<TextBlock>("ClickToStartTextBlock");
        if (clickToStartTextBlock != null)
        {
            clickToStartTextBlock.Text = languageService.GetText("ClickToStart");
        }

        // 应用名称保持不变，不需要翻译
        // var appNameTextBlock = this.FindControl<TextBlock>("AppNameTextBlock");
        // if (appNameTextBlock != null)
        // {
        //     appNameTextBlock.Text = "Lyxie";
        // }
    }

    private async Task ShowToolPanel()
    {
        var toolPanel = this.FindControl<Border>("ToolPanel");
        if (toolPanel == null) return;

        _isToolPanelVisible = true;

        // 确保面板是可见的
        toolPanel.IsVisible = true;
        toolPanel.IsHitTestVisible = true; // 确保可以接收点击事件
        
        // 淡入动画 - 优化响应速度
        const int steps = 10;
        const int totalDuration = 150;  // 增加持续时间，使动画更平滑
        const int stepDelay = totalDuration / steps;

        for (int i = 0; i <= steps; i++)
        {
            double progress = (double)i / steps;
            double easedProgress = 1.0 - Math.Pow(1.0 - progress, 3.0);

            toolPanel.Opacity = easedProgress;

            if (i < steps)
            {
                await Task.Delay(stepDelay);
            }
        }
        
        // 确保最终不透明度为1
        toolPanel.Opacity = 1.0;
        
        // 确保所有开关可点击
        var ttsToggle = this.FindControl<ToggleSwitch>("TTSToggle");
        var dev1Toggle = this.FindControl<ToggleSwitch>("Dev1Toggle");
        var dev2Toggle = this.FindControl<ToggleSwitch>("Dev2Toggle");
        
        if (ttsToggle != null) ttsToggle.IsHitTestVisible = true;
        if (dev1Toggle != null) dev1Toggle.IsHitTestVisible = true;
        if (dev2Toggle != null) dev2Toggle.IsHitTestVisible = true;
    }

    private async Task HideToolPanel()
    {
        var toolPanel = this.FindControl<Border>("ToolPanel");
        if (toolPanel == null) return;

        _isToolPanelVisible = false;

        // 淡出动画 - 优化响应速度
        const int steps = 10;
        const int totalDuration = 150;  // 增加持续时间，使动画更平滑
        const int stepDelay = totalDuration / steps;

        for (int i = 0; i <= steps; i++)
        {
            double progress = (double)i / steps;
            double easedProgress = 1.0 - Math.Pow(1.0 - progress, 3.0);

            toolPanel.Opacity = 1.0 - easedProgress;

            if (i < steps)
            {
                await Task.Delay(stepDelay);
            }
        }
        
        // 确保最终不透明度为0
        toolPanel.Opacity = 0.0;

        // 隐藏面板
        toolPanel.IsVisible = false;
    }

    private void OnTTSToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            App.Settings.EnableTTS = toggle.IsChecked ?? false;
            App.SaveSettings();
            
            System.Diagnostics.Debug.WriteLine($"TTS开关状态: {toggle.IsChecked}");
            
            // 如果关闭TTS，停止当前播放
            if (!toggle.IsChecked.GetValueOrDefault() && _ttsApiService != null)
            {
                _ttsApiService.Stop();
            }
        }
    }

    private void OnDev1Toggled(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            App.Settings.EnableDev1 = toggle.IsChecked ?? false;
            App.SaveSettings();
            
            // TODO: 实现开发功能1切换
            System.Diagnostics.Debug.WriteLine($"开发功能1开关状态: {toggle.IsChecked}");
        }
    }

    private async void OnDev2Toggled(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            bool isEnabled = toggle.IsChecked ?? false;
            App.Settings.EnableDev2 = isEnabled;
            App.SaveSettings();
            
            var dev2Toggle = toggle; // 在异步方法中使用本地变量

            // MCP文件系统工具切换
            try
            {
                if (isEnabled)
                {
                    System.Diagnostics.Debug.WriteLine("请求启用MCP文件系统服务...");
                    
                    // 获取服务器配置
                    var configs = await App.McpService.GetConfigsAsync();
                    if (configs.TryGetValue("filesystem", out var config))
                    {
                        // 1. 更新配置为启用
                        config.IsEnabled = true;
                        await App.McpService.SaveConfigsAsync(configs);
                        
                        // 2. 启动底层服务器
                        bool started = await App.McpService.StartServerAsync("filesystem");
                        if (started)
                        {
                            System.Diagnostics.Debug.WriteLine("MCP文件系统服务已启动，现触发一次性验证...");
                            dev2Toggle.Content = "正在验证...";
                            
                            // 3. 触发"一次性"验证
                            var validationResults = await App.McpAutoValidationService.TriggerValidationAsync(forceCheck: false);
                            
                            // 4. 根据验证结果更新UI
                            if (validationResults.TryGetValue("filesystem", out var result))
                            {
                                dev2Toggle.Content = result.IsAvailable ? "已启用 - 可用" : "已启用 - 验证失败";
                                System.Diagnostics.Debug.WriteLine($"服务器 filesystem 验证完成. 状态: {(result.IsAvailable ? "成功" : "失败")}");
                            }
                            else
                            {
                                dev2Toggle.Content = "已启用 - 未知状态";
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("MCP文件系统服务启动失败");
                            dev2Toggle.Content = "启动失败";
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("未找到MCP文件系统服务配置");
                        dev2Toggle.Content = "配置缺失";
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("请求禁用MCP文件系统服务...");
                    
                    // 1. 获取服务器配置并更新为禁用
                    var configs = await App.McpService.GetConfigsAsync();
                    if (configs.TryGetValue("filesystem", out var config))
                    {
                        config.IsEnabled = false;
                        await App.McpService.SaveConfigsAsync(configs);
                    }
                    
                    // 2. 停止底层服务器
                    bool stopped = await App.McpService.StopServerAsync("filesystem");
                    System.Diagnostics.Debug.WriteLine($"MCP文件系统服务停止{(stopped ? "成功" : "失败")}");

                    // 3. 重置验证状态，以便下次可以重新验证
                    App.McpAutoValidationService.ResetValidationState("filesystem");
                    
                    // 4. 更新UI
                    dev2Toggle.Content = "已禁用";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MCP文件系统服务操作异常: {ex.Message}");
                dev2Toggle.Content = "操作异常";
            }
        }
    }

    #region 词云和光晕动画

    /// <summary>
    /// 初始化词云控件
    /// </summary>
    private void InitializeWordCloud()
    {
        var container = this.FindControl<Border>("WordCloudContainer");
        if (container != null)
        {
            _wordCloudControl = new WordCloudControl();
            container.Child = _wordCloudControl;
            
            // 监听容器大小变化以重新定位词云
            container.SizeChanged += OnWordCloudContainerSizeChanged;
        }
    }

    /// <summary>
    /// 词云容器大小变化处理
    /// </summary>
    private void OnWordCloudContainerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 当容器大小变化时，确保词云正确重新定位
        _wordCloudControl?.InvalidateVisual();
    }

    /// <summary>
    /// 初始化光晕动画
    /// </summary>
    private void InitializeGlowAnimation()
    {
        _glowAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(17) // 60fps for ultra-smooth animation
        };
        _glowAnimationTimer.Tick += OnGlowAnimationTick;

        // 启动光晕动画
        StartGlowAnimation();
    }

    /// <summary>
    /// 启动光晕动画
    /// </summary>
    private void StartGlowAnimation()
    {
        if (!_isGlowAnimationRunning)
        {
            _isGlowAnimationRunning = true;
            _glowAnimationTimer?.Start();
        }
    }

    /// <summary>
    /// 初始化边框渐变旋转动画
    /// </summary>
    private void InitializeBorderAnimation()
    {
        // 延迟启动边框动画，确保控件已完全加载
        Dispatcher.UIThread.Post(() =>
        {
            var borderContainer = this.FindControl<Border>("BorderContainer");
            if (borderContainer?.BorderBrush is LinearGradientBrush)
            {
                StartBorderRotationAnimation(borderContainer);
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 启动边框渐变旋转动画
    /// </summary>
    private void StartBorderRotationAnimation(Border borderContainer)
    {
        if (_isBorderAnimationRunning) return;

        _isBorderAnimationRunning = true;

        try
        {
            // 使用AnimationHelper创建无限循环的渐变旋转动画
            _ = AnimationHelper.CreateInfiniteGradientRotationAnimation(borderContainer, TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"边框动画错误: {ex.Message}");
            _isBorderAnimationRunning = false;
        }
    }

    /// <summary>
    /// 停止光晕动画
    /// </summary>
    private void StopGlowAnimation()
    {
        _isGlowAnimationRunning = false;
        _glowAnimationTimer?.Stop();
    }

    /// <summary>
    /// 光晕动画帧更新 - 增强版呼吸和脉冲效果
    /// </summary>
    private void OnGlowAnimationTick(object? sender, EventArgs e)
    {
        if (!_isGlowAnimationRunning) return;

        var time = DateTime.Now.TimeOfDay.TotalSeconds;

        // 获取光晕层
        var outerGlow = this.FindControl<Border>("OuterGlow");
        var middleGlow = this.FindControl<Border>("MiddleGlow");
        var innerGlow = this.FindControl<Border>("InnerGlow");

        if (outerGlow != null && middleGlow != null && innerGlow != null)
        {
            // 主呼吸周期（慢速）
            var breathingCycle = Math.Sin(time * 0.6) * 0.5 + 0.5; // 0-1范围
            
            // 心跳脉冲效果（快速）
            var pulseCycle = Math.Max(0, Math.Sin(time * 2.5)) * 0.3;
            
            // 微妙的随机波动
            var randomFlicker = Math.Sin(time * 3.7) * 0.02;
            
            // 组合效果
            var baseIntensity = 0.8 + 0.4 * breathingCycle + pulseCycle + randomFlicker;
            
            // 为每层应用不同的强度和相位
            var outerOpacity = Math.Max(0.05, Math.Min(0.25, 0.12 * baseIntensity));
            var middleOpacity = Math.Max(0.08, Math.Min(0.35, 0.18 * baseIntensity * Math.Sin(time * 0.8 + Math.PI / 4)));
            var innerOpacity = Math.Max(0.12, Math.Min(0.45, 0.25 * baseIntensity * Math.Sin(time * 1.1 + Math.PI / 2)));

            outerGlow.Opacity = outerOpacity;
            middleGlow.Opacity = middleOpacity;
            innerGlow.Opacity = innerOpacity;
        }
    }

    /// <summary>
    /// 执行按钮点击动画
    /// </summary>
    private async Task PerformClickAnimation()
    {
        _isAnimating = true;

        var button = this.FindControl<Button>("MainCircleButton");
        var outerGlow = this.FindControl<Border>("OuterGlow");
        var middleGlow = this.FindControl<Border>("MiddleGlow");
        var innerGlow = this.FindControl<Border>("InnerGlow");

        if (button != null && outerGlow != null && middleGlow != null && innerGlow != null)
        {
            // 保存原始透明度
            var originalOuterOpacity = outerGlow.Opacity;
            var originalMiddleOpacity = middleGlow.Opacity;
            var originalInnerOpacity = innerGlow.Opacity;

            try
            {
                // 第一阶段：快速缩小 + 光晕增强 (更平滑的缓动)
                var scaleTask = AnimationHelper.CreateScaleAnimation(button, 1.0, 0.92, TimeSpan.FromMilliseconds(120), new CubicEaseIn());
                var glowTask1 = Task.WhenAll(
                    AnimationHelper.CreateOpacityAnimation(outerGlow, originalOuterOpacity, 0.7, TimeSpan.FromMilliseconds(120)),
                    AnimationHelper.CreateOpacityAnimation(middleGlow, originalMiddleOpacity, 0.8, TimeSpan.FromMilliseconds(120)),
                    AnimationHelper.CreateOpacityAnimation(innerGlow, originalInnerOpacity, 0.9, TimeSpan.FromMilliseconds(120))
                );

                await Task.WhenAll(scaleTask, glowTask1);

                // 第二阶段：弹性放大 + 轻微旋转 (更自然的弹性效果)
                var bounceTask = AnimationHelper.CreateCompositeTransformAnimation(button, 0.92, 1.06, 0, 3, TimeSpan.FromMilliseconds(350), new SineEaseInOut());
                await bounceTask;

                // 第三阶段：平滑回到原始状态
                var restoreTask = AnimationHelper.CreateCompositeTransformAnimation(button, 1.06, 1.0, 3, 0, TimeSpan.FromMilliseconds(300), new CubicEaseOut());
                var glowTask2 = Task.WhenAll(
                    AnimationHelper.CreateOpacityAnimation(outerGlow, 0.7, originalOuterOpacity, TimeSpan.FromMilliseconds(300)),
                    AnimationHelper.CreateOpacityAnimation(middleGlow, 0.8, originalMiddleOpacity, TimeSpan.FromMilliseconds(300)),
                    AnimationHelper.CreateOpacityAnimation(innerGlow, 0.9, originalInnerOpacity, TimeSpan.FromMilliseconds(300))
                );

                await Task.WhenAll(restoreTask, glowTask2);
            }
            catch (Exception)
            {
                // 动画可能被中断，恢复原始状态
                button.RenderTransform = null;
                outerGlow.Opacity = originalOuterOpacity;
                middleGlow.Opacity = originalMiddleOpacity;
                innerGlow.Opacity = originalInnerOpacity;
            }
        }

        _isAnimating = false;
    }

    #endregion

    #region WelcomeView按钮管理

    /// <summary>
    /// 隐藏WelcomeView的按钮
    /// </summary>
    private void HideWelcomeViewButtons()
    {
        var settingsButton = this.FindControl<Button>("SettingsButton");
        var toolButton = this.FindControl<Button>("ToolButton");
        
        if (settingsButton != null)
        {
            settingsButton.IsVisible = false;
        }
        
        if (toolButton != null)
        {
            toolButton.IsVisible = false;
        }
    }

    /// <summary>
    /// 显示WelcomeView的按钮
    /// </summary>
    private void ShowWelcomeViewButtons()
    {
        var settingsButton = this.FindControl<Button>("SettingsButton");
        var toolButton = this.FindControl<Button>("ToolButton");
        
        if (settingsButton != null)
        {
            settingsButton.IsVisible = true;
        }
        
        if (toolButton != null)
        {
            toolButton.IsVisible = true;
        }
    }

    /// <summary>
    /// 带动画效果隐藏WelcomeView的按钮
    /// </summary>
    private async Task HideWelcomeViewButtonsAnimated()
    {
        var settingsButton = this.FindControl<Button>("SettingsButton");
        var toolButton = this.FindControl<Button>("ToolButton");
        
        var buttons = new[] { settingsButton, toolButton }.Where(b => b != null).ToArray();
        if (buttons.Length == 0) return;

        // 快速淡出动画
        const int steps = 8;
        const int duration = 150;
        const int stepDelay = duration / steps;

        for (int i = 0; i <= steps; i++)
        {
            double opacity = 1.0 - (double)i / steps;
            
            foreach (var button in buttons)
            {
                if (button != null)
                    button.Opacity = opacity;
            }

            if (i < steps)
                await Task.Delay(stepDelay);
        }

        // 最终隐藏按钮
        foreach (var button in buttons)
        {
            if (button != null)
            {
                button.IsVisible = false;
                button.Opacity = 1.0; // 重置透明度以备后用
            }
        }
    }

    /// <summary>
    /// 带动画效果显示WelcomeView的按钮
    /// </summary>
    private async Task ShowWelcomeViewButtonsAnimated()
    {
        var settingsButton = this.FindControl<Button>("SettingsButton");
        var toolButton = this.FindControl<Button>("ToolButton");
        
        var buttons = new[] { settingsButton, toolButton }.Where(b => b != null).ToArray();
        if (buttons.Length == 0) return;

        // 先显示按钮但设为透明
        foreach (var button in buttons)
        {
            if (button != null)
            {
                button.IsVisible = true;
                button.Opacity = 0.0;
            }
        }

        // 快速淡入动画
        const int steps = 8;
        const int duration = 150;
        const int stepDelay = duration / steps;

        for (int i = 0; i <= steps; i++)
        {
            double opacity = (double)i / steps;
            
            foreach (var button in buttons)
            {
                if (button != null)
                    button.Opacity = opacity;
            }

            if (i < steps)
                await Task.Delay(stepDelay);
        }

        // 确保最终完全不透明
        foreach (var button in buttons)
        {
            if (button != null)
                button.Opacity = 1.0;
        }
    }

    #endregion

    #region 聊天历史和侧边栏

    /// <summary>
    /// 初始化聊天历史和侧边栏
    /// </summary>
    private async void InitializeChatHistory()
    {
        try
        {
            // 初始化数据库
            await ChatDataHelper.InitializeDatabaseAsync();
            
            // 创建并初始化侧边栏控件
            _chatSidebar = new ChatSidebarControl();
            
            // 绑定侧边栏事件
            _chatSidebar.SessionSelected += OnSessionSelected;
            _chatSidebar.NewChatRequested += OnNewChatRequested;
            _chatSidebar.SessionDeleted += OnSessionDeleted;
            _chatSidebar.SidebarToggled += OnSidebarToggled;
            
            // 将侧边栏添加到容器
            var sidebarContainer = this.FindControl<Border>("ChatSidebarContainer");
            if (sidebarContainer != null)
            {
                sidebarContainer.Child = _chatSidebar;
            }
            
            // 加载现有会话
            await LoadChatSessions();
            
            System.Diagnostics.Debug.WriteLine("聊天历史和侧边栏初始化完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"初始化聊天历史失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载聊天会话
    /// </summary>
    private async Task LoadChatSessions()
    {
        try
        {
            if (_chatSidebar != null)
            {
                await _chatSidebar.LoadSessionsAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载聊天会话失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 会话选择事件处理
    /// </summary>
    private async void OnSessionSelected(object? sender, ChatSession session)
    {
        try
        {
            if (_currentSession == session) return;

            _currentSession = session;
            await LoadSessionMessages(session);
            
            System.Diagnostics.Debug.WriteLine($"切换到会话: {session.Title}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"切换会话失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 新建聊天事件处理
    /// </summary>
    private async void OnNewChatRequested(object? sender, EventArgs e)
    {
        try
        {
            var newSession = await ChatDataHelper.CreateNewSessionAsync();
            _currentSession = newSession;
            
            if (_chatSidebar != null)
            {
                _chatSidebar.AddSession(newSession);
                _chatSidebar.SelectedSession = newSession;
            }
            
            // 清空当前消息列表
            ClearMessageList();
            
            System.Diagnostics.Debug.WriteLine($"创建新会话: {newSession.Title}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"创建新会话失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 删除会话事件处理
    /// </summary>
    private async void OnSessionDeleted(object? sender, ChatSession session)
    {
        try
        {
            await ChatDataHelper.DeleteSessionAsync(session.Id);
            
            if (_chatSidebar != null)
            {
                _chatSidebar.RemoveSession(session);
            }
            
            // 如果删除的是当前会话，清空消息列表并创建新会话
            if (_currentSession?.Id == session.Id)
            {
                ClearMessageList();
                OnNewChatRequested(this, EventArgs.Empty);
            }
            
            System.Diagnostics.Debug.WriteLine($"删除会话: {session.Title}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"删除会话失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 侧边栏展开/收起事件处理
    /// </summary>
    private async void OnSidebarToggled(object? sender, bool isExpanded)
    {
        try
        {
            var chatContainer = this.FindControl<Grid>("ChatContainer");
            if (chatContainer?.ColumnDefinitions.Count > 0)
            {
                var sidebarColumn = chatContainer.ColumnDefinitions[0];
                
                // 添加平滑的宽度过渡动画
                const int animationDuration = 250;
                const int steps = 15;
                const int stepDelay = animationDuration / steps;
                
                double startWidth = sidebarColumn.Width.Value;
                double targetWidth = isExpanded ? 300 : 70;
                
                // 执行平滑动画
                for (int i = 0; i <= steps; i++)
                {
                    double progress = (double)i / steps;
                    // 使用EaseOutCubic缓动函数
                    double easedProgress = 1 - Math.Pow(1 - progress, 3);
                    
                    double currentWidth = startWidth + (targetWidth - startWidth) * easedProgress;
                    sidebarColumn.Width = new GridLength(currentWidth);
                    
                    if (i < steps)
                        await Task.Delay(stepDelay);
                }
                
                // 确保最终宽度精确
                sidebarColumn.Width = new GridLength(targetWidth);
                
                // 更新聊天区域的布局
                UpdateChatAreaLayout(isExpanded);
            }
            
            System.Diagnostics.Debug.WriteLine($"侧边栏状态变更: {(isExpanded ? "展开" : "收起")}，宽度: {(isExpanded ? 300 : 70)}px");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理侧边栏状态变更失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 更新聊天区域布局以适应侧边栏变化
    /// </summary>
    private void UpdateChatAreaLayout(bool sidebarExpanded)
    {
        try
        {
            var mainChatArea = this.FindControl<Grid>("MainChatArea");
            if (mainChatArea != null)
            {
                // 可以在这里添加聊天区域的特殊布局调整
                // 例如调整消息气泡的最大宽度等
                
                // 触发聊天区域重新布局
                mainChatArea.InvalidateArrange();
                mainChatArea.InvalidateMeasure();
            }
            
            // 确保消息列表正确滚动
            var messageScrollViewer = this.FindControl<ScrollViewer>("MessageScrollViewer");
            if (messageScrollViewer != null)
            {
                // 延迟一帧确保布局完成后再滚动
                Dispatcher.UIThread.Post(() =>
                {
                    messageScrollViewer.ScrollToEnd();
                }, DispatcherPriority.Loaded);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"更新聊天区域布局失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载会话消息
    /// </summary>
    private async Task LoadSessionMessages(ChatSession session)
    {
        try
        {
            var messages = await ChatDataHelper.GetSessionMessagesAsync(session.Id);
            
            // 清空当前消息列表
            ClearMessageList();
            
            // 重新显示历史消息
            var messageList = this.FindControl<StackPanel>("MessageList");
            if (messageList != null)
            {
                foreach (var message in messages)
                {
                    var messageBubble = new MessageBubble();
                    bool isUser = message.MessageType == MessageType.User;
                    
                    // 设置消息内容和发送者
                    string senderName = isUser ? "您" : "Lyxie";
                    messageBubble.SetMessage(message.Content, isUser, senderName);
                    
                    // 设置消息气泡的对齐方式
                    messageBubble.HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                    
                    messageList.Children.Add(messageBubble);
                }
                
                // 滚动到底部
                var scrollViewer = this.FindControl<ScrollViewer>("MessageScrollViewer");
                scrollViewer?.ScrollToEnd();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载会话消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 清空消息列表
    /// </summary>
    private void ClearMessageList()
    {
        var messageList = this.FindControl<StackPanel>("MessageList");
        messageList?.Children.Clear();
    }

    /// <summary>
    /// 保存消息到当前会话
    /// </summary>
    private async Task SaveMessageToCurrentSession(string content, MessageType messageType)
    {
        if (_currentSession == null)
        {
            OnNewChatRequested(this, EventArgs.Empty);
        }
        
        if (_currentSession != null)
        {
            var message = new ChatMessage
            {
                SessionId = _currentSession.Id,
                Sender = messageType == MessageType.User ? "User" : "Assistant",
                Content = content,
                MessageType = messageType,
                Timestamp = DateTime.Now
            };
            
            await ChatDataHelper.SaveMessageAsync(message);
            
            // 如果是用户消息且当前会话标题还是默认的"新对话"，则使用用户问题内容作为标题
            if (messageType == MessageType.User && _currentSession.Title == "新对话")
            {
                // 截取用户问题的前20个字符作为对话标题
                string newTitle = content.Length > 20 ? content.Substring(0, 20) + "..." : content;
                // 移除换行符，保持标题简洁
                newTitle = newTitle.Replace("\n", " ").Replace("\r", " ");
                
                _currentSession.Title = newTitle;
                await ChatDataHelper.UpdateSessionAsync(_currentSession);
                
                System.Diagnostics.Debug.WriteLine($"自动设置对话标题: {newTitle}");
            }
            
            // 更新会话的最后更新时间
            _currentSession.LastUpdatedAt = DateTime.Now;
            _currentSession.LastMessage = content.Length > 50 ? content.Substring(0, 50) + "..." : content;
            
            if (_chatSidebar != null)
            {
                _chatSidebar.UpdateSessionOrder(_currentSession);
                _chatSidebar.RefreshSession(_currentSession);
            }
        }
    }

    #endregion

    private void InitializeChatInterface()
    {
        // 获取对话界面相关控件
        var chatBackButton = this.FindControl<Button>("ChatBackButton");
        var chatSettingsButton = this.FindControl<Button>("ChatSettingsButton");
        var sendButton = this.FindControl<Button>("SendButton");
        var voiceInputButton = this.FindControl<Button>("VoiceInputButton");
        var messageInput = this.FindControl<TextBox>("MessageInput");
        
        // 绑定事件处理
        if (chatBackButton != null)
        {
            chatBackButton.Click += OnChatBackButtonClick;
        }
        
        // 绑定设置按钮点击事件
        if (chatSettingsButton != null)
        {
            chatSettingsButton.Click += OnChatSettingsButtonClick;
        }
        
        if (sendButton != null)
        {
            sendButton.Click += OnSendButtonClick;
        }
        
        if (voiceInputButton != null)
        {
            voiceInputButton.Click += OnVoiceInputButtonClick;
        }
        
        if (messageInput != null)
        {
            messageInput.KeyDown += OnMessageInputKeyDown;
        }
        
        // 初始化消息列表
        var messageList = this.FindControl<StackPanel>("MessageList");
        // StackPanel不需要ItemsSource设置

        // 初始化标题信息
        UpdateChatTitle();
    }

    /// <summary>
    /// 更新对话界面标题，显示当前LLM API信息
    /// </summary>
    public void UpdateChatTitle()
    {
        var titleText = this.FindControl<TextBlock>("ChatTitleText");
        var apiInfoText = this.FindControl<TextBlock>("ChatApiInfoText");

        if (titleText != null)
        {
            titleText.Text = "Lyxie";
        }

        if (apiInfoText != null)
        {
            // 检查是否有LLM API配置
            if (App.Settings.LlmApiConfigs == null || App.Settings.LlmApiConfigs.Count == 0)
            {
                apiInfoText.Text = App.LanguageService.CurrentLanguage == Services.Language.SimplifiedChinese 
                    ? "未配置 API" 
                    : "No API Configured";
                return;
            }

            var activeConfigIndex = App.Settings.ActiveLlmConfigIndex;
            if (activeConfigIndex < 0 || activeConfigIndex >= App.Settings.LlmApiConfigs.Count)
            {
                activeConfigIndex = 0; // 默认使用第一个配置
                App.Settings.ActiveLlmConfigIndex = 0;
                App.SaveSettings();
            }

            var config = App.Settings.LlmApiConfigs[activeConfigIndex];
            
            // 格式化API信息显示
            string modelInfo = config.ModelName ?? "未知模型";
            string configName = !string.IsNullOrEmpty(config.Name) ? config.Name : "默认配置";
            
            // 提取API提供商信息
            string provider = "未知";
            if (!string.IsNullOrEmpty(config.ApiUrl))
            {
                if (config.ApiUrl.Contains("openai.com"))
                    provider = "OpenAI";
                else if (config.ApiUrl.Contains("anthropic.com"))
                    provider = "Anthropic";
                else if (config.ApiUrl.Contains("google.com") || config.ApiUrl.Contains("googleapis.com"))
                    provider = "Google";
                else if (config.ApiUrl.Contains("azure.com"))
                    provider = "Azure";
                else if (config.ApiUrl.Contains("cohere.ai"))
                    provider = "Cohere";
                else if (config.ApiUrl.Contains("localhost") || config.ApiUrl.Contains("127.0.0.1"))
                    provider = "本地";
                else
                {
                    // 尝试从URL中提取域名
                    try
                    {
                        var uri = new Uri(config.ApiUrl);
                        provider = uri.Host.Replace("api.", "").Replace("www.", "");
                    }
                    catch
                    {
                        provider = "自定义";
                    }
                }
            }

            // 显示格式：配置名称 - 模型名 (提供商)
            apiInfoText.Text = $"{configName} - {modelInfo} ({provider})";
            
            System.Diagnostics.Debug.WriteLine($"更新API信息显示: {apiInfoText.Text}");
        }
    }

    #region 对话界面动画和交互

    /// <summary>
    /// 刷新LLM API配置信息
    /// </summary>
    public void RefreshLlmApiConfig()
    {
        System.Diagnostics.Debug.WriteLine("刷新LLM API配置信息");
        
        // 确保配置已初始化
        if (App.Settings.LlmApiConfigs == null)
        {
            App.Settings.LlmApiConfigs = new List<Views.LlmApiConfig>();
        }
        
        // 验证活跃配置索引
        if (App.Settings.LlmApiConfigs.Count > 0)
        {
            if (App.Settings.ActiveLlmConfigIndex < 0 || App.Settings.ActiveLlmConfigIndex >= App.Settings.LlmApiConfigs.Count)
            {
                App.Settings.ActiveLlmConfigIndex = 0;
                App.SaveSettings();
            }
        }
        
        // 触发标题更新
        UpdateChatTitle();
        
        System.Diagnostics.Debug.WriteLine($"当前LLM配置数量: {App.Settings.LlmApiConfigs.Count}, 激活索引: {App.Settings.ActiveLlmConfigIndex}");
    }

    /// <summary>
    /// 显示对话界面的动画
    /// </summary>
    private async Task ShowChatInterface()
    {
        if (_isAnimating || _isChatVisible) return;
        _isAnimating = true;

        var chatContainer = this.FindControl<Grid>("ChatContainer");
        var mainButton = this.FindControl<Button>("MainCircleButton");
        var borderContainer = this.FindControl<Border>("BorderContainer");
        var chatInputArea = this.FindControl<Border>("ChatInputArea");
        var chatHistoryArea = this.FindControl<Border>("ChatHistoryArea");
        var glowElements = new[] { "OuterGlow", "MiddleGlow", "InnerGlow" }
            .Select(name => this.FindControl<Border>(name))
            .Where(b => b != null);

        if (chatContainer == null || mainButton == null || borderContainer == null || chatInputArea == null || chatHistoryArea == null)
        {
            _isAnimating = false;
            return;
        }

        // 立即隐藏WelcomeView的按钮（带动画）
        await HideWelcomeViewButtonsAnimated();
        
        // 确保工具面板隐藏
        if (_isToolPanelVisible)
        {
            await HideToolPanel();
        }

        // 显示对话容器并设置初始状态
        chatContainer.IsVisible = true;
        chatContainer.Opacity = 0; // 初始透明状态，避免突然出现
        
        // 确保侧边栏处于正确状态
        var chatSidebarContainer = this.FindControl<Border>("ChatSidebarContainer");
        if (chatSidebarContainer != null)
        {
            chatSidebarContainer.Opacity = 0; // 初始透明，稍后渐显
        }

        // 第一步：圆形按钮收缩并向下移动（300ms）
        var buttonAnimation = Task.Run(async () =>
        {
            const int steps = 20;
            const int duration = 300;
            const int stepDelay = duration / steps;

            double startSize = 600;
            double endSize = 80;
            double startY = 0;
            double endY = this.Bounds.Height - 100; // 移动到底部

            for (int i = 0; i <= steps; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    double progress = (double)i / steps;
                    double easedProgress = 1 - Math.Pow(1 - progress, 3); // EaseOutCubic

                    // 缩小按钮
                    double currentSize = startSize + (endSize - startSize) * easedProgress;
                    mainButton.Width = currentSize;
                    mainButton.Height = currentSize;
                    mainButton.CornerRadius = new CornerRadius(currentSize / 2);

                    borderContainer.Width = currentSize + 12;
                    borderContainer.Height = currentSize + 12;
                    borderContainer.CornerRadius = new CornerRadius((currentSize + 12) / 2);

                    // 移动按钮
                    var transform = mainButton.RenderTransform as TranslateTransform;
                    if (transform == null)
                    {
                        transform = new TranslateTransform();
                        mainButton.RenderTransform = transform;
                    }
                    transform.Y = startY + (endY - startY) * easedProgress;

                    borderContainer.RenderTransform = transform;

                    // 淡出文字
                    var textElements = mainButton.GetVisualDescendants()
                        .OfType<TextBlock>()
                        .ToList();
                    foreach (var text in textElements)
                    {
                        text.Opacity = 1 - easedProgress;
                    }
                });

                if (i < steps) await Task.Delay(stepDelay);
            }
        });

        // 同时淡出光晕和词云
        var fadeOutTask = Task.Run(async () =>
        {
            const int steps = 20;
            const int duration = 300;
            const int stepDelay = duration / steps;

            for (int i = 0; i <= steps; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    double opacity = 1 - (double)i / steps;

                    foreach (var glow in glowElements)
                    {
                        if (glow != null)
                            glow.Opacity = opacity * 0.25; // 原始透明度的比例
                    }

                    if (_wordCloudControl != null)
                    {
                        _wordCloudControl.Opacity = opacity;
                    }
                });

                if (i < steps) await Task.Delay(stepDelay);
            }
        });

        await Task.WhenAll(buttonAnimation, fadeOutTask);

        // 隐藏主按钮和边框
        mainButton.IsVisible = false;
        borderContainer.IsVisible = false;

        // 第二步：渐显对话容器、侧边栏并显示输入区域
        await Task.Run(async () =>
        {
            const int steps = 10;
            const int duration = 200;
            const int stepDelay = duration / steps;

            for (int i = 0; i <= steps; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    double opacity = (double)i / steps;
                    chatContainer.Opacity = opacity;
                    
                    // 同时渐显侧边栏
                    if (chatSidebarContainer != null)
                    {
                        chatSidebarContainer.Opacity = opacity;
                    }
                });

                if (i < steps) await Task.Delay(stepDelay);
            }
        });
        
        chatInputArea.Height = 80;

        // 第三步：展开对话历史区域（400ms）
        const int expandSteps = 30;
        const int expandDuration = 400;
        const int expandStepDelay = expandDuration / expandSteps;

        chatHistoryArea.Opacity = 0;
        for (int i = 0; i <= expandSteps; i++)
        {
            double progress = (double)i / expandSteps;
            double easedProgress = 1 - Math.Pow(1 - progress, 3); // EaseOutCubic

            chatHistoryArea.Opacity = easedProgress;

            if (i < expandSteps) await Task.Delay(expandStepDelay);
        }

        _isChatVisible = true;
        _isAnimating = false;

        // 聚焦到输入框
        var messageInput = this.FindControl<TextBox>("MessageInput");
        messageInput?.Focus();
    }

    /// <summary>
    /// 隐藏对话界面的动画
    /// </summary>
    private async Task HideChatInterface()
    {
        if (_isAnimating || !_isChatVisible) return;
        _isAnimating = true;

        var chatContainer = this.FindControl<Grid>("ChatContainer");
        var mainButton = this.FindControl<Button>("MainCircleButton");
        var borderContainer = this.FindControl<Border>("BorderContainer");
        var chatInputArea = this.FindControl<Border>("ChatInputArea");
        var chatHistoryArea = this.FindControl<Border>("ChatHistoryArea");
        var glowElements = new[] { "OuterGlow", "MiddleGlow", "InnerGlow" }
            .Select(name => this.FindControl<Border>(name))
            .Where(b => b != null);

        if (chatContainer == null || mainButton == null || borderContainer == null) 
        {
            _isAnimating = false;
            return;
        }

        // 第一步：渐隐对话容器和侧边栏
        const int fadeSteps = 15;
        const int fadeDuration = 200;
        const int fadeStepDelay = fadeDuration / fadeSteps;
        
        var chatSidebarContainer = this.FindControl<Border>("ChatSidebarContainer");

        for (int i = fadeSteps; i >= 0; i--)
        {
            double opacity = (double)i / fadeSteps;
            chatContainer.Opacity = opacity;
            
            // 同时渐隐侧边栏
            if (chatSidebarContainer != null)
            {
                chatSidebarContainer.Opacity = opacity;
            }

            if (i > 0) await Task.Delay(fadeStepDelay);
        }

        // 第二步：收缩对话历史区域
        if (chatHistoryArea != null)
            chatHistoryArea.Opacity = 0;

        // 显示主按钮和边框
        mainButton.IsVisible = true;
        borderContainer.IsVisible = true;

        // 反向动画：按钮放大并移回中心
        var expandTask = Task.Run(async () =>
        {
            const int steps = 20;
            const int duration = 300;
            const int stepDelay = duration / steps;

            for (int i = steps; i >= 0; i--)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    double progress = (double)i / steps;
                    double easedProgress = 1 - Math.Pow(1 - progress, 3);

                    // 放大按钮
                    double currentSize = 600 - (600 - 80) * easedProgress;
                    mainButton.Width = currentSize;
                    mainButton.Height = currentSize;
                    mainButton.CornerRadius = new CornerRadius(currentSize / 2);

                    borderContainer.Width = currentSize + 12;
                    borderContainer.Height = currentSize + 12;
                    borderContainer.CornerRadius = new CornerRadius((currentSize + 12) / 2);

                    // 移动按钮
                    var transform = mainButton.RenderTransform as TranslateTransform;
                    if (transform != null)
                    {
                        transform.Y = (this.Bounds.Height - 100) * easedProgress;
                    }

                    // 淡入文字
                    var textElements = mainButton.GetVisualDescendants()
                        .OfType<TextBlock>()
                        .ToList();
                    foreach (var text in textElements)
                    {
                        text.Opacity = 1 - easedProgress;
                    }
                });

                if (i > 0) await Task.Delay(stepDelay);
            }
        });

        // 同时淡入光晕和词云
        var fadeInTask = Task.Run(async () =>
        {
            const int steps = 20;
            const int duration = 300;
            const int stepDelay = duration / steps;

            for (int i = 0; i <= steps; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    double opacity = (double)i / steps;

                    foreach (var glow in glowElements)
                    {
                        if (glow != null)
                            glow.Opacity = opacity * 0.25; // 原始透明度的比例
                    }

                    if (_wordCloudControl != null)
                    {
                        _wordCloudControl.Opacity = opacity;
                    }
                });

                if (i < steps) await Task.Delay(stepDelay);
            }
        });

        await Task.WhenAll(expandTask, fadeInTask);

        // 隐藏对话容器
        if (chatContainer != null)
        {
            chatContainer.IsVisible = false;
        }
        if (chatInputArea != null)
            chatInputArea.Height = 0;

        _isChatVisible = false;
        _isAnimating = false;

        // 重新显示WelcomeView的按钮（带动画）
        await ShowWelcomeViewButtonsAnimated();
    }

    private async void OnChatBackButtonClick(object? sender, RoutedEventArgs e)
    {
        // 停止TTS播放
        _ttsApiService?.Stop();
        
        // 隐藏对话界面，返回到主界面
        await HideChatInterface();
    }

    private void OnChatSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        // 触发设置界面请求事件
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSendButtonClick(object? sender, RoutedEventArgs e)
    {
        SendMessage();
    }

    private void OnVoiceInputButtonClick(object? sender, RoutedEventArgs e)
    {
        // TODO: 实现语音输入功能
        System.Diagnostics.Debug.WriteLine("语音输入按钮被点击");
    }

    private void OnMessageInputKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (_cancellationTokenSource != null) return; // 如果正在发送，则不处理回车

        if (e.Key == Avalonia.Input.Key.Enter &&
            !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
        {
            e.Handled = true;
            SendMessage();
        }
    }

    private void OnStopButtonClick(object? sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        System.Diagnostics.Debug.WriteLine("用户请求停止流式传输");
    }

    private void UpdateSendButtonState(bool isSending)
    {
        var sendButton = this.FindControl<Button>("SendButton");
        var sendButtonIcon = this.FindControl<MaterialIcon>("SendButtonIcon");

        if (sendButton == null || sendButtonIcon == null) return;

        if (isSending)
        {
            sendButtonIcon.Kind = MaterialIconKind.Stop;
            sendButtonIcon.Foreground = Brushes.Red;
            sendButton.Click -= OnSendButtonClick;
            sendButton.Click += OnStopButtonClick;
        }
        else
        {
            sendButtonIcon.Kind = MaterialIconKind.Send;
            // 安全地获取资源，如果失败则使用默认颜色
            try
            {
                var buttonTextBrush = Application.Current?.FindResource("ButtonTextBrush");
                sendButtonIcon.Foreground = (buttonTextBrush as IBrush) ?? Brushes.White;
            }
            catch
            {
                sendButtonIcon.Foreground = Brushes.White;
            }
            sendButton.Click -= OnStopButtonClick;
            sendButton.Click += OnSendButtonClick;
        }
    }

    private async void SendMessage()
    {
        var messageInput = this.FindControl<TextBox>("MessageInput");
        var messageList = this.FindControl<StackPanel>("MessageList");
        var messageScrollViewer = this.FindControl<ScrollViewer>("MessageScrollViewer");
        
        if (messageInput == null || messageList == null) return;
        
        string message = messageInput.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(message)) return;

        // 初始化TTS内容构造器
        _currentAiResponseBuilder = new StringBuilder();
        
        // 设置为发送中状态
        _cancellationTokenSource = new CancellationTokenSource();
        UpdateSendButtonState(isSending: true);

        System.Diagnostics.Debug.WriteLine($"发送消息: {message}");

        // 保存用户消息到数据库
        await SaveMessageToCurrentSession(message, MessageType.User);

        // 创建用户消息气泡
        var userBubble = new MessageBubble();
        userBubble.SetMessage(message, true, "您");
        userBubble.HorizontalAlignment = HorizontalAlignment.Right;
        
        messageList.Children.Add(userBubble);
        
        // 清空输入框
        messageInput.Text = "";
        
        // 强制刷新UI，滚动到底部
        Dispatcher.UIThread.Post(() =>
        {
            messageScrollViewer?.ScrollToEnd();
        }, DispatcherPriority.Background);
        
        // 创建AI回复的流式消息气泡
        MessageBubble? aiBubble = null;
        
        try
        {
            // 获取当前激活的LLM API配置（实时读取最新配置）
            if (App.Settings.LlmApiConfigs == null || App.Settings.LlmApiConfigs.Count == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var errorBubble = new MessageBubble();
                    errorBubble.SetMessage("错误：未配置LLM API。请先在设置中添加API配置。", false, "系统");
                    errorBubble.HorizontalAlignment = HorizontalAlignment.Left;
                    messageList.Children.Add(errorBubble);
                });
                return;
            }

            var activeConfigIndex = App.Settings.ActiveLlmConfigIndex;
            if (activeConfigIndex < 0 || activeConfigIndex >= App.Settings.LlmApiConfigs.Count)
            {
                activeConfigIndex = 0; // 默认使用第一个配置
                App.Settings.ActiveLlmConfigIndex = 0;
                App.SaveSettings();
            }

            var config = App.Settings.LlmApiConfigs[activeConfigIndex];
            
            System.Diagnostics.Debug.WriteLine($"使用LLM配置: {config.Name} ({config.ModelName}) - {config.ApiUrl}");

            // 获取MCP工具调用上下文
            string? toolContext = null;
            if (_mcpToolManager != null)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("开始获取MCP工具调用上下文...");
                    var mcpContext = await _mcpToolManager.GenerateToolContextAsync(message, _cancellationTokenSource.Token);
                    
                    if (mcpContext.Results.Count > 0)
                    {
                        toolContext = mcpContext.GetFormattedResults();
                        System.Diagnostics.Debug.WriteLine($"MCP工具调用完成，获得 {mcpContext.Results.Count} 个结果");
                        
                        // 在UI中显示工具调用状态
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var toolInfoBubble = new MessageBubble();
                            toolInfoBubble.SetMessage($"🔧 已调用 {mcpContext.Results.Count} 个相关工具获取信息", false, "工具");
                            toolInfoBubble.HorizontalAlignment = HorizontalAlignment.Left;
                            messageList.Children.Add(toolInfoBubble);
                            messageScrollViewer?.ScrollToEnd();
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("未找到相关的MCP工具或工具调用失败");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MCP工具调用异常: {ex.Message}");
                    // 工具调用失败不影响正常的LLM对话
                }
            }

            // 在UI线程中创建AI消息气泡并初始化为流式模式
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                aiBubble = new MessageBubble();
                aiBubble.InitializeStreamingMessage(false, "Lyxie");
                aiBubble.HorizontalAlignment = HorizontalAlignment.Left;
                messageList.Children.Add(aiBubble);
                messageScrollViewer?.ScrollToEnd();
            });

            // 使用LLM API服务发送流式请求
            var apiService = new LlmApiService();
            
            var success = await apiService.SendStreamingMessageAsync(
                config,
                message,
                toolContext, // 传递工具调用上下文
                onDataReceived: (content, isComplete) =>
                {
                    if (aiBubble != null)
                    {
                        // 累积内容用于TTS
                        if (!string.IsNullOrEmpty(content))
                        {
                            _currentAiResponseBuilder?.Append(content);
                        }

                        if (isComplete)
                        {
                            // 流式接收完成，启用Markdown渲染
                            aiBubble.CompleteStreamingAndEnableMarkdown();
                            Dispatcher.UIThread.Post(() =>
                            {
                                messageScrollViewer?.ScrollToEnd();
                            });
                            
                            // 使用累积的完整回复播放TTS
                            var fullAiResponse = _currentAiResponseBuilder?.ToString();
                            if (!string.IsNullOrWhiteSpace(fullAiResponse))
                            {
                                // 保存AI回复到数据库
                                _ = SaveMessageToCurrentSession(fullAiResponse, MessageType.Assistant);
                                
                                HandleAiResponseForTts(fullAiResponse, aiBubble);
                            }
                        }
                        else if (!string.IsNullOrEmpty(content))
                        {
                            // 追加内容到UI
                            aiBubble.AppendContent(content);
                            Dispatcher.UIThread.Post(() =>
                            {
                                messageScrollViewer?.ScrollToEnd();
                            });
                        }
                    }
                },
                onError: (error) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (aiBubble != null && messageList.Children.Contains(aiBubble))
                        {
                            messageList.Children.Remove(aiBubble);
                        }
                        
                        var errorBubble = new MessageBubble();
                        errorBubble.SetMessage($"流式请求错误：{error}", false, "错误");
                        errorBubble.HorizontalAlignment = HorizontalAlignment.Left;
                        messageList.Children.Add(errorBubble);
                        messageScrollViewer?.ScrollToEnd();
                    });
                },
                _cancellationTokenSource.Token
            );

            if (!success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (aiBubble != null && messageList.Children.Contains(aiBubble))
                    {
                        // 尝试保留已有内容并完成Markdown渲染
                        try
                        {
                            aiBubble.CompleteStreamingAndEnableMarkdown();
                            System.Diagnostics.Debug.WriteLine("流式请求失败，但保留了已有内容并完成Markdown渲染");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"完成Markdown渲染时出错: {ex.Message}");
                            // 如果Markdown渲染也失败，则移除aiBubble
                            messageList.Children.Remove(aiBubble);
                        }
                    }
                    
                    // 如果aiBubble中没有内容或处理失败，显示错误提示
                    if (aiBubble == null || !messageList.Children.Contains(aiBubble))
                    {
                        var errorBubble = new MessageBubble();
                        errorBubble.SetMessage("无法启动流式请求，请检查API配置。", false, "错误");
                        errorBubble.HorizontalAlignment = HorizontalAlignment.Left;
                        messageList.Children.Add(errorBubble);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 请求被用户取消
                System.Diagnostics.Debug.WriteLine("请求被用户取消。");
                if (aiBubble != null && messageList.Children.Contains(aiBubble))
                {
                    // 尝试保留已有内容并完成Markdown渲染
                    try
                    {
                        aiBubble.CompleteStreamingAndEnableMarkdown();
                        System.Diagnostics.Debug.WriteLine("请求被取消，但保留了已有内容并完成Markdown渲染");
                        
                        // 添加取消提示（如果有内容则不替换，只添加提示）
                        var cancelledBubble = new MessageBubble();
                        cancelledBubble.SetMessage("（流式传输已被用户中止）", false, "系统");
                        cancelledBubble.HorizontalAlignment = HorizontalAlignment.Left;
                        messageList.Children.Add(cancelledBubble);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"完成Markdown渲染时出错: {ex.Message}");
                        // 如果Markdown渲染失败，则移除aiBubble并显示取消消息
                        messageList.Children.Remove(aiBubble);
                        var cancelledBubble = new MessageBubble();
                        cancelledBubble.SetMessage("消息请求已取消。", false, "系统");
                        cancelledBubble.HorizontalAlignment = HorizontalAlignment.Left;
                        messageList.Children.Add(cancelledBubble);
                    }
                }
                else
                {
                    var cancelledBubble = new MessageBubble();
                    cancelledBubble.SetMessage("消息请求已取消。", false, "系统");
                    cancelledBubble.HorizontalAlignment = HorizontalAlignment.Left;
                    messageList.Children.Add(cancelledBubble);
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 尝试保留已有内容
                if (aiBubble != null && messageList.Children.Contains(aiBubble))
                {
                    try
                    {
                        aiBubble.CompleteStreamingAndEnableMarkdown();
                        System.Diagnostics.Debug.WriteLine("发生异常，但保留了已有内容并完成Markdown渲染");
                        
                        // 添加错误提示
                        var errorBubble = new MessageBubble();
                        errorBubble.SetMessage($"发生错误：{ex.Message}", false, "错误");
                        errorBubble.HorizontalAlignment = HorizontalAlignment.Left;
                        messageList.Children.Add(errorBubble);
                    }
                    catch (Exception renderEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"完成Markdown渲染时出错: {renderEx.Message}");
                        // 如果Markdown渲染失败，则移除aiBubble
                        messageList.Children.Remove(aiBubble);
                        
                        // 显示异常信息
                        var errorBubble = new MessageBubble();
                        errorBubble.SetMessage($"发生错误：{ex.Message}", false, "错误");
                        errorBubble.HorizontalAlignment = HorizontalAlignment.Left;
                        messageList.Children.Add(errorBubble);
                    }
                }
                else
                {
                    // 显示异常信息
                    var errorBubble = new MessageBubble();
                    errorBubble.SetMessage($"发生错误：{ex.Message}", false, "错误");
                    errorBubble.HorizontalAlignment = HorizontalAlignment.Left;
                    messageList.Children.Add(errorBubble);
                }
            });
            
            System.Diagnostics.Debug.WriteLine($"发送消息异常: {ex}");
        }
        finally
        {
            // 恢复按钮状态
            UpdateSendButtonState(isSending: false);
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _currentAiResponseBuilder = null;
        }
        
        // 最后再滚动到底部
        Dispatcher.UIThread.Post(() =>
        {
            messageScrollViewer?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
    
    private void OnReplayTtsRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is MessageBubble bubble && !string.IsNullOrEmpty(bubble.AudioFilePath))
        {
            // 获取当前激活的TTS配置
            if (App.Settings.TtsApiConfigs == null || App.Settings.TtsApiConfigs.Count == 0) return;
            var activeTtsConfigIndex = App.Settings.ActiveTtsConfigIndex;
            if (activeTtsConfigIndex < 0 || activeTtsConfigIndex >= App.Settings.TtsApiConfigs.Count)
            {
                activeTtsConfigIndex = 0;
            }
            var ttsConfig = App.Settings.TtsApiConfigs[activeTtsConfigIndex];

            _ttsApiService?.PlayFromFileAsync(bubble.AudioFilePath, ttsConfig);
        }
    }
    
    /// <summary>
    /// 初始化MCP工具管理器
    /// </summary>
    private void InitializeMcpToolManager()
    {
        try
        {
            var mcpService = new McpService();
            var serverManager = new McpServerManager();
            _mcpToolManager = new McpToolManager(mcpService, serverManager);
            
            // 订阅工具调用状态事件
            _mcpToolManager.ToolCallStatusChanged += OnToolCallStatusChanged;
            
            Debug.WriteLine("MCP工具管理器初始化成功");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MCP工具管理器初始化失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 工具调用状态变化处理
    /// </summary>
    private void OnToolCallStatusChanged(object? sender, ToolCallStatusEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Debug.WriteLine($"工具调用状态: {e.Status} - {e.ToolName} ({e.ServerName}): {e.Message}");
            
            // 可以在这里添加UI状态更新，比如显示工具调用进度
        });
    }

    #region TTS功能
    
    /// <summary>
    /// 初始化TTS服务
    /// </summary>
    private void InitializeTtsService()
    {
        _ttsApiService = new TtsApiService();
        
        // 订阅TTS事件
        _ttsApiService.StateChanged += OnTtsStateChanged;
        _ttsApiService.ErrorOccurred += OnTtsErrorOccurred;
    }
    
    /// <summary>
    /// TTS状态变化回调
    /// </summary>
    private void OnTtsStateChanged(TtsPlaybackState state, string message = "")
    {
        Dispatcher.UIThread.Post(() =>
        {
            System.Diagnostics.Debug.WriteLine($"TTS状态变化: {state} - {message}");
            
            // 可以在这里更新UI状态，比如显示播放状态指示器
        });
    }
    
    /// <summary>
    /// TTS错误回调
    /// </summary>
    private void OnTtsErrorOccurred(string error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            System.Diagnostics.Debug.WriteLine($"TTS错误: {error}");
            
            // 可以选择在UI上显示错误提示
            // 这里简化处理，只记录日志
        });
    }
    
    /// <summary>
    /// 播放TTS语音
    /// </summary>
    /// <param name="text">要播放的文本</param>
    private async Task<string?> PlayTtsAsync(string text)
    {
        // 检查TTS是否启用
        if (!App.Settings.EnableTTS || _ttsApiService == null)
        {
            return null;
        }
        
        // 检查是否有TTS配置
        if (App.Settings.TtsApiConfigs == null || App.Settings.TtsApiConfigs.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("TTS播放失败: 未配置TTS API");
            return null;
        }
        
        // 获取当前激活的TTS配置
        var activeTtsConfigIndex = App.Settings.ActiveTtsConfigIndex;
        if (activeTtsConfigIndex < 0 || activeTtsConfigIndex >= App.Settings.TtsApiConfigs.Count)
        {
            activeTtsConfigIndex = 0; // 使用第一个配置
        }
        
        var ttsConfig = App.Settings.TtsApiConfigs[activeTtsConfigIndex];
        
        try
        {
            // 停止当前播放
            _ttsApiService.Stop();
            
            // 播放新的文本并获取缓存路径
            System.Diagnostics.Debug.WriteLine($"开始TTS播放: {text.Substring(0, Math.Min(50, text.Length))}...");
            return await _ttsApiService.SpeakAsync(ttsConfig, text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TTS播放异常: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// 处理AI回复完成后的TTS播放
    /// </summary>
    /// <param name="aiResponse">AI回复文本</param>
    private async void HandleAiResponseForTts(string aiResponse, MessageBubble aiBubble)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
            return;
            
        // 清理文本，移除Markdown格式
        var cleanText = CleanTextForTts(aiResponse);
        
        if (!string.IsNullOrWhiteSpace(cleanText))
        {
            var audioPath = await PlayTtsAsync(cleanText);
            if (audioPath != null)
            {
                // 将音频路径与气泡关联并显示重播按钮
                aiBubble.AudioFilePath = audioPath;
                aiBubble.ShowReplayButton(true);
                aiBubble.ReplayRequested += OnReplayTtsRequested;
            }
        }
    }
    
    /// <summary>
    /// 清理文本用于TTS播放
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <returns>清理后的文本</returns>
    private string CleanTextForTts(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // 检查并移除 <think>...</think> 标签及其内容，只保留之后的部分
        const string thinkTag = "</think>";
        int thinkTagIndex = text.IndexOf(thinkTag, StringComparison.OrdinalIgnoreCase);
        if (thinkTagIndex != -1)
        {
            text = text.Substring(thinkTagIndex + thinkTag.Length);
        }

        // 移除Markdown格式标记
        var cleanText = text;
        
        // 移除代码块
        cleanText = Regex.Replace(cleanText, @"```[\s\S]*?```", "", RegexOptions.Multiline);
        // 移除内联代码
        cleanText = cleanText.Replace("`", "");
        // 移除链接格式 [text](url)
        cleanText = Regex.Replace(cleanText, @"\[([^\]]+)\]\([^\)]+\)", "$1");
        // 移除粗体和斜体标记
        cleanText = cleanText.Replace("**", "").Replace("*", "");
        // 移除标题标记
        cleanText = Regex.Replace(cleanText, @"^#+\s*", "", RegexOptions.Multiline);
        // 移除列表标记
        cleanText = Regex.Replace(cleanText, @"^\s*[-*+]\s*", "", RegexOptions.Multiline);
        // 移除数字列表标记
        cleanText = Regex.Replace(cleanText, @"^\s*\d+\.\s*", "", RegexOptions.Multiline);
        // 移除多余的空白字符
        cleanText = Regex.Replace(cleanText, @"\s+", " ");
        cleanText = cleanText.Trim();
            
        return cleanText;
    }
    
    #endregion
    
    /// <summary>
    /// 释放资源
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        try
        {
            _mcpToolManager?.Dispose();
            _ttsApiService?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"释放资源时发生异常: {ex.Message}");
        }
        
        base.OnDetachedFromVisualTree(e);
    }

    #endregion
}
