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
        
        RetentionDaysBox.ValueChanged += (s, e) => OnSettingChanged("RetentionDays", (int)e.NewValue);
        
        ClearDataButton.Click += (s, e) => ViewModel.ClearDataCommand.Execute(null);
        ViewChangelogButton.Click += OnViewChangelogClick;
    }
    
    private async void OnViewChangelogClick(object sender, RoutedEventArgs e)
    {
        var changelogContent = """
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
            case "RetentionDays":
                ViewModel.RetentionDays = (int)value;
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
        
        RetentionDaysBox.Value = ViewModel.RetentionDays;
        DataPathText.Text = ViewModel.DataPath;
        DatabaseSizeText.Text = ViewModel.DatabaseSize;
        
        Log.Information("SettingsPage: 设置已加载到 UI");
    }
}
