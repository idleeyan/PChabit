using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Platform;
using PChabit.Infrastructure.Services;

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
            AboutSectionControl.Refresh();
            AiSectionControl.Initialize(ViewModel);
            StickyNotesControl.Initialize(ViewModel);

            // 选中项必须等 InitializeComponent 之后再设：解析期触发 SelectionChanged
            // 时右侧分区控件还不存在，会空引用崩溃
            SectionList.SelectedIndex = 0;
            ApplySection("general");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: 构造函数失败");
            throw;
        }

        Log.Information("SettingsPage: 构造函数完成");
    }

    /// <summary>左侧分区导航切换：只显示当前分组的卡片，其余折叠。</summary>
    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: ListViewItem { Tag: string tag } })
            ApplySection(tag);
    }

    private void ApplySection(string tag)
    {
        // 防御：XAML 解析期间若被提前触发，部分分区字段可能尚未赋值
        void Show(UIElement? element, bool visible)
        {
            if (element is null) return;
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        Show(GeneralSection, tag == "general");
        Show(AppearanceSection, tag == "general");
        Show(MonitorSection, tag == "monitor");
        Show(TaskbarSection, tag == "interface");
        Show(DesktopWidgetSection, tag == "interface");
        Show(StickyNotesControl, tag == "notes");
        Show(DataSection, tag == "data");
        Show(AiSectionControl, tag == "ai");
        Show(AboutSectionControl, tag == "about");
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

        TaskbarEnabledSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarEnabled", TaskbarEnabledSwitch.IsOn);

        DailyUsageGoalHoursBox.ValueChanged += (s, e) => OnSettingChanged("DailyUsageGoalHours", e.NewValue);

        TaskbarCpuSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowCpu", TaskbarCpuSwitch.IsOn);
        TaskbarMemorySwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowMemory", TaskbarMemorySwitch.IsOn);
        TaskbarGpuSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowGpu", TaskbarGpuSwitch.IsOn);
        TaskbarNetSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowNet", TaskbarNetSwitch.IsOn);
        TaskbarDiskSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowDisk", TaskbarDiskSwitch.IsOn);
        TaskbarTempSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowTemp", TaskbarTempSwitch.IsOn);
        TaskbarUsageSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowUsage", TaskbarUsageSwitch.IsOn);

        // 桌面悬浮插件（3.23.0）
        DesktopWidgetEnabledSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetEnabled", DesktopWidgetEnabledSwitch.IsOn);
        DesktopCpuSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowCpu", DesktopCpuSwitch.IsOn);
        DesktopMemorySwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowMemory", DesktopMemorySwitch.IsOn);
        DesktopGpuSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowGpu", DesktopGpuSwitch.IsOn);
        DesktopVramSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowVram", DesktopVramSwitch.IsOn);
        DesktopNetSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowNet", DesktopNetSwitch.IsOn);
        DesktopDiskSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowDisk", DesktopDiskSwitch.IsOn);
        DesktopUsageSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowUsage", DesktopUsageSwitch.IsOn);
        DesktopCurrentAppSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowCurrentApp", DesktopCurrentAppSwitch.IsOn);
        DesktopTopmostSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetTopmost", DesktopTopmostSwitch.IsOn);
        DesktopClickThroughSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetClickThrough", DesktopClickThroughSwitch.IsOn);
        DesktopClampSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetClampToScreen", DesktopClampSwitch.IsOn);


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
            case "TaskbarEnabled":
                ViewModel.TaskbarEnabled = (bool)value;
                break;
            case "DailyUsageGoalHours":
                ViewModel.DailyUsageGoalHours = (double)value;
                break;
            case "TaskbarShowCpu":
                ViewModel.TaskbarShowCpu = (bool)value;
                break;
            case "TaskbarShowMemory":
                ViewModel.TaskbarShowMemory = (bool)value;
                break;
            case "TaskbarShowGpu":
                ViewModel.TaskbarShowGpu = (bool)value;
                break;
            case "TaskbarShowNet":
                ViewModel.TaskbarShowNet = (bool)value;
                break;
            case "TaskbarShowDisk":
                ViewModel.TaskbarShowDisk = (bool)value;
                break;
            case "TaskbarShowTemp":
                ViewModel.TaskbarShowTemp = (bool)value;
                break;
            case "TaskbarShowUsage":
                ViewModel.TaskbarShowUsage = (bool)value;
                break;
            case "DesktopWidgetEnabled":
                ViewModel.DesktopWidgetEnabled = (bool)value;
                break;
            case "DesktopWidgetShowCpu":
                ViewModel.DesktopWidgetShowCpu = (bool)value;
                break;
            case "DesktopWidgetShowMemory":
                ViewModel.DesktopWidgetShowMemory = (bool)value;
                break;
            case "DesktopWidgetShowGpu":
                ViewModel.DesktopWidgetShowGpu = (bool)value;
                break;
            case "DesktopWidgetShowVram":
                ViewModel.DesktopWidgetShowVram = (bool)value;
                break;
            case "DesktopWidgetShowNet":
                ViewModel.DesktopWidgetShowNet = (bool)value;
                break;
            case "DesktopWidgetShowDisk":
                ViewModel.DesktopWidgetShowDisk = (bool)value;
                break;
            case "DesktopWidgetShowUsage":
                ViewModel.DesktopWidgetShowUsage = (bool)value;
                break;
            case "DesktopWidgetTopmost":
                ViewModel.DesktopWidgetTopmost = (bool)value;
                break;
            case "DesktopWidgetClickThrough":
                ViewModel.DesktopWidgetClickThrough = (bool)value;
                break;
            case "DesktopWidgetClampToScreen":
                ViewModel.DesktopWidgetClampToScreen = (bool)value;
                break;

        }

        // 必须落盘：只改 ViewModel 不会写 settings.json，也不会执行 ApplyStartupSetting
        ViewModel.SaveSetting(propertyName);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("SettingsPage: OnNavigatedTo 开始");
        base.OnNavigatedTo(e);

        try
        {
            AboutSectionControl.Refresh();

            _isLoading = true;
            try
            {
                await ViewModel.InitializeAsync();
                Log.Information("SettingsPage: InitializeAsync 完成");
                LoadSettingsToUI();
                AiSectionControl.Reload();
                StickyNotesControl.Reload();
            }
            finally
            {
                _isLoading = false;
            }
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
        TaskbarEnabledSwitch.IsOn = ViewModel.TaskbarEnabled;

        DailyUsageGoalHoursBox.Value = ViewModel.DailyUsageGoalHours;

        TaskbarCpuSwitch.IsOn = ViewModel.TaskbarShowCpu;
        TaskbarMemorySwitch.IsOn = ViewModel.TaskbarShowMemory;
        TaskbarGpuSwitch.IsOn = ViewModel.TaskbarShowGpu;
        TaskbarNetSwitch.IsOn = ViewModel.TaskbarShowNet;
        TaskbarDiskSwitch.IsOn = ViewModel.TaskbarShowDisk;
        TaskbarTempSwitch.IsOn = ViewModel.TaskbarShowTemp;
        TaskbarUsageSwitch.IsOn = ViewModel.TaskbarShowUsage;

        // 桌面悬浮插件（3.23.0）
        DesktopWidgetEnabledSwitch.IsOn = ViewModel.DesktopWidgetEnabled;
        DesktopCpuSwitch.IsOn = ViewModel.DesktopWidgetShowCpu;
        DesktopMemorySwitch.IsOn = ViewModel.DesktopWidgetShowMemory;
        DesktopGpuSwitch.IsOn = ViewModel.DesktopWidgetShowGpu;
        DesktopVramSwitch.IsOn = ViewModel.DesktopWidgetShowVram;
        DesktopNetSwitch.IsOn = ViewModel.DesktopWidgetShowNet;
        DesktopDiskSwitch.IsOn = ViewModel.DesktopWidgetShowDisk;
        DesktopUsageSwitch.IsOn = ViewModel.DesktopWidgetShowUsage;
        DesktopCurrentAppSwitch.IsOn = ViewModel.DesktopWidgetShowCurrentApp;
        DesktopTopmostSwitch.IsOn = ViewModel.DesktopWidgetTopmost;
        DesktopClickThroughSwitch.IsOn = ViewModel.DesktopWidgetClickThrough;
        DesktopClampSwitch.IsOn = ViewModel.DesktopWidgetClampToScreen;




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



