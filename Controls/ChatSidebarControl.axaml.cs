using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using Lyxie_desktop.Models;
using Lyxie_desktop.Helpers;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Lyxie_desktop.Controls
{
    public partial class ChatSidebarControl : UserControl
    {
        // 事件定义
        public event EventHandler<ChatSession>? SessionSelected;
        public event EventHandler? NewChatRequested;
        public event EventHandler<ChatSession>? SessionDeleted;
        public event EventHandler<ChatSession>? SessionRenamed;
        public event EventHandler<bool>? SidebarToggled;

        // 数据属性
        public ObservableCollection<ChatSession> Sessions { get; private set; } = new();
        private ChatSession? _selectedSession;
        private ChatSession? _contextMenuSession;
        private bool _isExpanded = true;
        private bool _isAnimating = false;

        public ChatSession? SelectedSession
        {
            get => _selectedSession;
            set
            {
                _selectedSession = value;
                UpdateSessionSelection();
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value && !_isAnimating)
                {
                    _isExpanded = value;
                    _ = AnimateStateChange();
                    SidebarToggled?.Invoke(this, _isExpanded);
                }
            }
        }

        public ChatSidebarControl()
        {
            InitializeComponent();
            InitializeEvents();
            InitializeData();
            
            // 初始化侧边栏状态
            UpdateSidebarState();
        }

        private void InitializeEvents()
        {
            // 展开状态按钮
            var newChatButton = this.FindControl<Button>("NewChatButton");
            var searchTextBox = this.FindControl<TextBox>("SearchTextBox");
            var clearSearchButton = this.FindControl<Button>("ClearSearchButton");
            var toggleButton = this.FindControl<Button>("ToggleSidebarButton");
            
            // 收起状态按钮
            var toggleButtonCollapsed = this.FindControl<Button>("ToggleSidebarButtonCollapsed");
            var newChatButtonCollapsed = this.FindControl<Button>("NewChatButtonCollapsed");

            // 绑定事件
            if (newChatButton != null)
                newChatButton.Click += OnNewChatButtonClick;
            
            if (newChatButtonCollapsed != null)
                newChatButtonCollapsed.Click += OnNewChatButtonClick;
            
            if (searchTextBox != null)
                searchTextBox.TextChanged += OnSearchTextChanged;
            
            if (clearSearchButton != null)
                clearSearchButton.Click += OnClearSearchButtonClick;
            
            if (toggleButton != null)
                toggleButton.Click += OnToggleSidebarButtonClick;
            
            if (toggleButtonCollapsed != null)
                toggleButtonCollapsed.Click += OnToggleSidebarButtonClick;

            // 会话列表
            var sessionList = this.FindControl<ItemsControl>("SessionList");
            if (sessionList != null)
            {
                sessionList.ItemsSource = Sessions;
            }
            
            // 绑定会话项点击事件
            this.PointerPressed += OnControlPointerPressed;
            this.PointerReleased += OnControlPointerReleased;
        }

        private async void InitializeData()
        {
            await LoadSessionsAsync();
        }

        /// <summary>
        /// 平滑的状态切换动画
        /// </summary>
        private async Task AnimateStateChange()
        {
            if (_isAnimating) return;
            
            _isAnimating = true;
            
            try
            {
                var expandedLayout = this.FindControl<Grid>("ExpandedLayout");
                var collapsedLayout = this.FindControl<Grid>("CollapsedLayout");
                
                if (expandedLayout == null || collapsedLayout == null)
                {
                    // 如果找不到控件，使用简单切换
                    UpdateSidebarState();
                    return;
                }

                const int animationDuration = 200;
                const int steps = 10;
                const int stepDelay = animationDuration / steps;

                if (_isExpanded)
                {
                    // 收起 -> 展开
                    collapsedLayout.IsVisible = false;
                    expandedLayout.IsVisible = true;
                    expandedLayout.Opacity = 0;
                    
                    // 淡入动画
                    for (int i = 0; i <= steps; i++)
                    {
                        double progress = (double)i / steps;
                        double easedProgress = 1 - Math.Pow(1 - progress, 2); // EaseOutQuad
                        
                        expandedLayout.Opacity = easedProgress;
                        
                        if (i < steps)
                            await Task.Delay(stepDelay);
                    }
                }
                else
                {
                    // 展开 -> 收起
                    // 淡出动画
                    for (int i = steps; i >= 0; i--)
                    {
                        double progress = (double)i / steps;
                        expandedLayout.Opacity = progress;
                        
                        if (i > 0)
                            await Task.Delay(stepDelay);
                    }
                    
                    expandedLayout.IsVisible = false;
                    collapsedLayout.IsVisible = true;
                    collapsedLayout.Opacity = 0;
                    
                    // 淡入收起状态
                    for (int i = 0; i <= steps; i++)
                    {
                        double progress = (double)i / steps;
                        double easedProgress = 1 - Math.Pow(1 - progress, 2); // EaseOutQuad
                        
                        collapsedLayout.Opacity = easedProgress;
                        
                        if (i < steps)
                            await Task.Delay(stepDelay);
                    }
                }
                
                // 更新图标和提示
                UpdateToggleButtonState();
            }
            finally
            {
                _isAnimating = false;
            }
        }

        /// <summary>
        /// 更新侧边栏状态（无动画版本，用于初始化）
        /// </summary>
        private void UpdateSidebarState()
        {
            var expandedLayout = this.FindControl<Grid>("ExpandedLayout");
            var collapsedLayout = this.FindControl<Grid>("CollapsedLayout");

            if (expandedLayout != null && collapsedLayout != null)
            {
                expandedLayout.IsVisible = _isExpanded;
                expandedLayout.Opacity = _isExpanded ? 1.0 : 0.0;
                
                collapsedLayout.IsVisible = !_isExpanded;
                collapsedLayout.Opacity = !_isExpanded ? 1.0 : 0.0;
            }

            UpdateToggleButtonState();
        }

        /// <summary>
        /// 更新切换按钮状态
        /// </summary>
        private void UpdateToggleButtonState()
        {
            // 更新展开状态的图标
            var toggleIcon = this.FindControl<MaterialIcon>("ToggleIcon");
            if (toggleIcon != null)
            {
                toggleIcon.Kind = _isExpanded ? MaterialIconKind.ChevronLeft : MaterialIconKind.ChevronRight;
            }

            // 更新工具提示
            var toggleButton = this.FindControl<Button>("ToggleSidebarButton");
            if (toggleButton != null)
            {
                ToolTip.SetTip(toggleButton, _isExpanded ? "收起侧边栏" : "展开侧边栏");
            }
            
            var toggleButtonCollapsed = this.FindControl<Button>("ToggleSidebarButtonCollapsed");
            if (toggleButtonCollapsed != null)
            {
                ToolTip.SetTip(toggleButtonCollapsed, "展开侧边栏");
            }
        }

        /// <summary>
        /// 加载所有会话
        /// </summary>
        public async Task LoadSessionsAsync()
        {
            try
            {
                var sessions = await ChatDataHelper.GetAllSessionsAsync();
                Sessions.Clear();
                foreach (var session in sessions)
                {
                    Sessions.Add(session);
                }
                UpdateSessionCount();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载会话失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加新会话到列表
        /// </summary>
        public void AddSession(ChatSession session)
        {
            Sessions.Insert(0, session);
            UpdateSessionCount();
        }

        /// <summary>
        /// 从列表中移除会话
        /// </summary>
        public void RemoveSession(ChatSession session)
        {
            Sessions.Remove(session);
            UpdateSessionCount();
        }

        /// <summary>
        /// 更新会话在列表中的位置（用于最近更新排序）
        /// </summary>
        public void UpdateSessionOrder(ChatSession session)
        {
            var index = Sessions.IndexOf(session);
            if (index > 0)
            {
                Sessions.Move(index, 0);
            }
        }

        private void UpdateSessionCount()
        {
            // 会话计数显示已移除，此方法保留用于其他可能的统计更新
        }

        private void UpdateSessionSelection()
        {
            // 更新UI中的选中状态
            var sessionList = this.FindControl<ItemsControl>("SessionList");
            if (sessionList?.ItemsSource != null)
            {
                // 触发UI更新以显示选中状态
                // 注意：这里可能需要根据具体的UI绑定方式进行调整
            }
        }

        private void OnNewChatButtonClick(object? sender, RoutedEventArgs e)
        {
            NewChatRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                var clearButton = this.FindControl<Button>("ClearSearchButton");
                if (clearButton != null)
                {
                    clearButton.IsVisible = !string.IsNullOrEmpty(textBox.Text);
                }

                await PerformSearchAsync(textBox.Text);
            }
        }

        private void OnClearSearchButtonClick(object? sender, RoutedEventArgs e)
        {
            var searchTextBox = this.FindControl<TextBox>("SearchTextBox");
            if (searchTextBox != null)
            {
                searchTextBox.Text = "";
            }
        }

        private void OnToggleSidebarButtonClick(object? sender, RoutedEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }

        private void OnRenameSessionMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (_contextMenuSession != null)
            {
                // TODO: 显示重命名对话框
                // 暂时触发事件
                SessionRenamed?.Invoke(this, _contextMenuSession);
            }
        }

        private void OnDeleteSessionMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (_contextMenuSession != null)
            {
                SessionDeleted?.Invoke(this, _contextMenuSession);
            }
        }

        private void OnExportSessionMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (_contextMenuSession != null)
            {
                // TODO: 实现导出功能
            }
        }

        /// <summary>
        /// 执行搜索
        /// </summary>
        private async Task PerformSearchAsync(string? keyword)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    await LoadSessionsAsync();
                }
                else
                {
                    var searchResults = await ChatDataHelper.SearchSessionsAsync(keyword);
                    Sessions.Clear();
                    foreach (var session in searchResults)
                    {
                        Sessions.Add(session);
                    }
                    UpdateSessionCount();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"搜索会话失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理会话项点击
        /// </summary>
        public void OnSessionItemClicked(ChatSession session)
        {
            SelectedSession = session;
            SessionSelected?.Invoke(this, session);
        }

        /// <summary>
        /// 处理会话项右键菜单
        /// </summary>
        public void OnSessionItemRightClicked(ChatSession session, PointerEventArgs e)
        {
            _contextMenuSession = session;
            
            var contextMenu = this.FindControl<ContextMenu>("SessionContextMenu");
            if (contextMenu != null)
            {
                // 显示右键菜单
                contextMenu.Open(this);
            }
        }

        /// <summary>
        /// 刷新指定会话的显示
        /// </summary>
        public void RefreshSession(ChatSession session)
        {
            var index = Sessions.IndexOf(session);
            if (index >= 0)
            {
                // 触发UI更新
                Sessions[index] = session;
            }
        }

        /// <summary>
        /// 处理控件内的指针按下事件
        /// </summary>
        private void OnControlPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var target = e.Source as Control;
                
                // 向上遍历视觉树，查找会话项
                while (target != null)
                {
                    if (target is Border border && border.Name == "SessionItem" && border.Tag is ChatSession session)
                    {
                        var properties = e.GetCurrentPoint(this).Properties;
                        
                        if (properties.IsLeftButtonPressed)
                        {
                            // 左键点击
                            OnSessionItemClicked(session);
                        }
                        else if (properties.IsRightButtonPressed)
                        {
                            // 右键点击
                            _contextMenuSession = session;
                            
                            // 显示右键菜单
                            var contextMenu = this.FindResource("SessionContextMenu") as ContextMenu;
                            if (contextMenu != null)
                            {
                                contextMenu.Open(border);
                                e.Handled = true;
                            }
                        }
                        break;
                    }
                    target = target.Parent as Control;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"处理会话项点击失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理指针释放事件
        /// </summary>
        private void OnControlPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                var target = e.Source as Control;
                
                // 向上遍历视觉树，查找会话项
                while (target != null)
                {
                    if (target is Border border && border.Name == "SessionItem" && border.Tag is ChatSession session)
                    {
                        _contextMenuSession = session;
                        
                        // 显示右键菜单
                        var contextMenu = this.FindResource("SessionContextMenu") as ContextMenu;
                        if (contextMenu != null)
                        {
                            contextMenu.Open(border);
                            e.Handled = true;
                        }
                        break;
                    }
                    target = target.Parent as Control;
                }
            }
        }

        /// <summary>
        /// 重命名菜单项点击
        /// </summary>
        private void OnRenameMenuItemClick(object? sender, RoutedEventArgs e)
        {
            OnRenameSessionMenuItemClick(sender, e);
        }

        /// <summary>
        /// 删除菜单项点击
        /// </summary>
        private void OnDeleteMenuItemClick(object? sender, RoutedEventArgs e)
        {
            OnDeleteSessionMenuItemClick(sender, e);
        }

        /// <summary>
        /// 导出菜单项点击
        /// </summary>
        private void OnExportMenuItemClick(object? sender, RoutedEventArgs e)
        {
            OnExportSessionMenuItemClick(sender, e);
        }
    }
} 