using System.IO;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
            ? $"{version.Major}.{version.Minor}.{version.Build}" 
            : "未知";
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
        // 先禁用按钮防止重复点击
        ViewChangelogButton.IsEnabled = false;
        
        try
        {
            // 显示加载中的对话框
            var loadingDialog = new ContentDialog
            {
                Title = "更新日志",
                Content = new StackPanel
                {
                    Spacing = 12,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                    Children =
                    {
                        new ProgressRing
                        {
                            IsActive = true,
                            Width = 32,
                            Height = 32
                        },
                        new TextBlock
                        {
                            Text = "正在加载更新日志...",
                            FontSize = 13,
                            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
                        }
                    }
                },
                XamlRoot = XamlRoot
            };
            
            // 不阻塞地显示加载对话框
            _ = loadingDialog.ShowAsync();
            
            // 在后台线程异步加载 CHANGELOG 内容，避免 UI 线程卡死
            var content = await Task.Run(async () =>
            {
                try
                {
                    var changelogPath = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
                    if (File.Exists(changelogPath))
                    {
                        return await File.ReadAllTextAsync(changelogPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "读取 CHANGELOG.md 失败");
                }
                
                return "更新日志文件未找到。";
            });
            
            // 关闭加载对话框
            loadingDialog.Hide();
            
            // 让 UI 线程在渲染对话框前喘息
            await Task.Yield();
            
            // 构建内容对话框 - 使用 StackPanel 分段渲染，避免单个超大 TextBlock 卡死
            var contentPanel = new StackPanel { Spacing = 12 };
            var sections = content.Split("## ");
            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section)) continue;
                
                var sectionText = "## " + section.TrimEnd();
                var textBlock = new TextBlock
                {
                    Text = sectionText,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    LineHeight = 20,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")
                };
                contentPanel.Children.Add(textBlock);
            }
            
            var dialog = new ContentDialog
            {
                Title = "更新日志",
                Content = new ScrollViewer
                {
                    Content = contentPanel,
                    MaxHeight = 500
                },
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };
            
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "显示更新日志失败");
        }
        finally
        {
            ViewChangelogButton.IsEnabled = true;
        }
    }
    
    private void OnSettingChanged(string propertyName, object value)
    {
        if (_isLoading) return;
        
        Log.Information("SettingsPage: 设置变更 {PropertyName} = {Value}", propertyName, value);
        
        // 设置 ViewModel 属性后，OnXxxChanged 部分方法会自动调用 SaveAsync 持久化，
        // 此处不再重复调用 SaveSetting（避免双重保存）
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
