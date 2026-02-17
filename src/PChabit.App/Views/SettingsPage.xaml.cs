using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class SettingsPage : Page
{
    private SettingsViewModel ViewModel { get; }
    private bool _isLoading = true;

    public SettingsPage()
    {
        Log.Information("SettingsPage: 构造函数开始");
        
        try
        {
            ViewModel = App.GetService<SettingsViewModel>();
            Log.Information("SettingsPage: ViewModel 已获取");
            
            InitializeComponent();
            Log.Information("SettingsPage: InitializeComponent 完成");
            
            AddEventHandlers();
            LoadVersionInfo();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: 构造函数失败");
            throw;
        }
        
        Log.Information("SettingsPage: 构造函数完成");
    }
    
    private void LoadVersionInfo()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version != null 
            ? $"版本 {version.Major}.{version.Minor}.{version.Build}" 
            : "版本未知";
    }
    
    private void AddEventHandlers()
    {
        StartWithWindowsSwitch.Toggled += (s, e) => OnSettingChanged("StartWithWindows", StartWithWindowsSwitch.IsOn);
        MinimizeToTraySwitch.Toggled += (s, e) => OnSettingChanged("MinimizeToTray", MinimizeToTraySwitch.IsOn);
        ShowNotificationsSwitch.Toggled += (s, e) => OnSettingChanged("ShowNotifications", ShowNotificationsSwitch.IsOn);
        AutoStartMonitoringSwitch.Toggled += (s, e) => OnSettingChanged("AutoStartMonitoring", AutoStartMonitoringSwitch.IsOn);
        
        MonitoringIntervalBox.ValueChanged += (s, e) => OnSettingChanged("MonitoringInterval", (int)e.NewValue);
        IdleThresholdBox.ValueChanged += (s, e) => OnSettingChanged("IdleThreshold", (int)e.NewValue);
        
        TrackKeyboardSwitch.Toggled += (s, e) => OnSettingChanged("TrackKeyboard", TrackKeyboardSwitch.IsOn);
        TrackMouseSwitch.Toggled += (s, e) => OnSettingChanged("TrackMouse", TrackMouseSwitch.IsOn);
        TrackWebBrowsingSwitch.Toggled += (s, e) => OnSettingChanged("TrackWebBrowsing", TrackWebBrowsingSwitch.IsOn);
        
        ThemeComboBox.SelectionChanged += (s, e) => 
        {
            if (!_isLoading && ThemeComboBox.SelectedItem is ComboBoxItem item)
                OnSettingChanged("SelectedThemeKey", item.Tag?.ToString() ?? "system");
        };
        
        LanguageComboBox.SelectionChanged += (s, e) => 
        {
            if (!_isLoading && LanguageComboBox.SelectedItem is ComboBoxItem item)
                OnSettingChanged("SelectedLanguageKey", item.Tag?.ToString() ?? "zh-CN");
        };
        
        ViewChangelogButton.Click += OnViewChangelogClick;
    }
    
    private async void OnViewChangelogClick(object sender, RoutedEventArgs e)
    {
        var changelogContent = """
            ## v2.14.5 (2026-02-18)
            
            ### 编译错误修复
            - MouseDetailsViewModel 编译错误修复：解决多个编译问题
              - 修复 Windows.UI.ColorHelper 和 Windows.UI.Colors 命名空间引用错误
              - 修复 TimeSpan 总和计算错误
              - 修复 ProgramCategory 类型转换错误
            - CA1416 平台兼容性警告修复：在 Infrastructure 项目中抑制 CA1416 警告
            
            ## v2.14.4 (2026-02-18)
            
            ### 新功能
            - 鼠标点击详情页面：在分析页面新增鼠标点击详情标签页
              - 统计卡片：总点击次数、左/右/中键点击、移动距离、滚动次数
              - 按程序统计：显示各应用程序的点击次数和移动距离
              - 每小时统计：展示一天中每小时的鼠标活动热力图
              - 详细记录：按时间顺序显示鼠标会话详情
              - 支持按今日、本周、上周筛选数据
            
            ## v2.14.3 (2026-02-18)
            
            ### 导航优化
            - 分析页面结构调整：将"热力图"和"智能洞察"从主菜单移到"分析"子菜单下
              - 分析页面现在包含三个子页面：周统计、热力图、智能洞察
              - 导航结构更清晰，所有分析相关功能统一归类
            
            ## v2.14.2 (2026-02-18)
            
            ### 功能优化
            - 设置页优化：删除数据管理模块，避免与备份管理页功能重复
            - 数据管理功能统一在备份管理页中进行
            
            ## v2.14.1 (2026-02-17)
            
            ### 技术改进
            - 完善语言文件夹清理，添加缺失的语言代码
              - fil-PH (菲律宾语)
              - kok-IN (孔卡尼语)
              - quz-PE (库斯科语)
            
            ## v2.14.0 (2026-02-17)
            
            ### 新功能
            - 图标系统完善：程序图标和任务栏图标统一使用 pchabit.ico
              - 窗口标题栏图标动态加载
              - 系统托盘图标动态加载
              - 图标文件自动复制到输出目录
            
            ## v2.13.9 (2026-02-17)
            
            ### 技术改进
            - 消除剩余编译警告
              - CA1416: 添加 Windows 平台支持标记到 ApplyStartupSetting 方法
              - NETSDK1206: 抑制 Windows App SDK RID 警告
            
            ## v1.0.0 (2026-02-15)
            
            ### 新功能
            - 应用程序使用时间追踪
            - 键盘活动监控和快捷键统计
            - 鼠标活动监控
            - 网页浏览历史追踪（需安装浏览器扩展）
            - 程序分类管理
            - 数据可视化分析
            - 系统托盘支持
            
            ### 浏览器扩展
            - 支持 Chrome/Edge/Firefox
            - 实时同步浏览数据
            
            ### 修复
            - 修复快捷键检测问题（支持左右修饰键）
            - 修复分类映射匹配问题
            - 修复系统托盘图标创建问题
            """;
        
        var dialog = new ContentDialog
        {
            Title = "更新日志",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = changelogContent,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 12, 0, 0)
                },
                MaxHeight = 400
            },
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot
        };
        
        await dialog.ShowAsync();
    }
    
    private void OnSettingChanged(string propertyName, object value)
    {
        if (_isLoading) return;
        
        Log.Information("SettingsPage: 设置变更 {PropertyName} = {Value}", propertyName, value);
        
        switch (propertyName)
        {
            case "StartWithWindows":
                ViewModel.StartWithWindows = (bool)value;
                break;
            case "MinimizeToTray":
                ViewModel.MinimizeToTray = (bool)value;
                break;
            case "ShowNotifications":
                ViewModel.ShowNotifications = (bool)value;
                break;
            case "AutoStartMonitoring":
                ViewModel.AutoStartMonitoring = (bool)value;
                break;
            case "MonitoringInterval":
                ViewModel.MonitoringInterval = (int)value;
                break;
            case "IdleThreshold":
                ViewModel.IdleThreshold = (int)value;
                break;
            case "TrackKeyboard":
                ViewModel.TrackKeyboard = (bool)value;
                break;
            case "TrackMouse":
                ViewModel.TrackMouse = (bool)value;
                break;
            case "TrackWebBrowsing":
                ViewModel.TrackWebBrowsing = (bool)value;
                break;
            case "SelectedThemeKey":
                ViewModel.SelectedThemeKey = (string)value;
                break;
            case "SelectedLanguageKey":
                ViewModel.SelectedLanguageKey = (string)value;
                break;
        }
        
        ViewModel.SaveSetting(propertyName);
    }
    
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("SettingsPage: OnNavigatedTo 开始");
        base.OnNavigatedTo(e);
        
        try
        {
            await ViewModel.InitializeAsync();
            Log.Information("SettingsPage: InitializeAsync 完成");
            
            LoadSettingsToUI();
            _isLoading = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: InitializeAsync 失败");
        }
    }
    
    private void LoadSettingsToUI()
    {
        Log.Information("SettingsPage: 加载设置到 UI");
        
        StartWithWindowsSwitch.IsOn = ViewModel.StartWithWindows;
        MinimizeToTraySwitch.IsOn = ViewModel.MinimizeToTray;
        ShowNotificationsSwitch.IsOn = ViewModel.ShowNotifications;
        AutoStartMonitoringSwitch.IsOn = ViewModel.AutoStartMonitoring;
        
        MonitoringIntervalBox.Value = ViewModel.MonitoringInterval;
        IdleThresholdBox.Value = ViewModel.IdleThreshold;
        
        TrackKeyboardSwitch.IsOn = ViewModel.TrackKeyboard;
        TrackMouseSwitch.IsOn = ViewModel.TrackMouse;
        TrackWebBrowsingSwitch.IsOn = ViewModel.TrackWebBrowsing;
        
        ThemeComboBox.Items.Clear();
        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "系统默认", Tag = "system" });
        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "浅色", Tag = "light" });
        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "深色", Tag = "dark" });
        ThemeComboBox.SelectedIndex = ViewModel.SelectedThemeKey switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
        
        LanguageComboBox.Items.Clear();
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-CN" });
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en-US" });
        LanguageComboBox.SelectedIndex = ViewModel.SelectedLanguageKey == "en-US" ? 1 : 0;
        
        Log.Information("SettingsPage: 设置已加载到 UI");
    }
}
