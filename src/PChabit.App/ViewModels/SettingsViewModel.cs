using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.Core.Interfaces;


namespace PChabit.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    [ObservableProperty]
    private bool _startWithWindows = true;
    
    [ObservableProperty]
    private bool _minimizeToTray = true;
    
    [ObservableProperty]
    private bool _showNotifications = true;
    
    [ObservableProperty]
    private bool _autoStartMonitoring = true;
    
    [ObservableProperty]
    private int _monitoringInterval = 1;
    
    [ObservableProperty]
    private int _idleThreshold = 5;
    
    [ObservableProperty]
    private bool _trackKeyboard = true;
    
    [ObservableProperty]
    private bool _trackMouse = true;
    
    [ObservableProperty]
    private bool _trackWebBrowsing = true;
    
    [ObservableProperty]
    private bool _anonymizeData = false;
    
    [ObservableProperty]
    private string _webSocketPort = "8765";
    
    [ObservableProperty]
    private string _selectedThemeKey = "system";
    
    [ObservableProperty]
    private string _selectedLanguageKey = "zh-CN";
    [ObservableProperty]
    private bool _taskbarEnabled = true;

    [ObservableProperty]
    private bool _taskbarShowCpu = true;

    [ObservableProperty]
    private bool _taskbarShowMemory = true;

    [ObservableProperty]
    private bool _taskbarShowGpu = true;

    [ObservableProperty]
    private bool _taskbarShowNet = true;

    [ObservableProperty]
    private bool _taskbarShowDisk = true;

    [ObservableProperty]
    private bool _taskbarShowTemp = true;

    [ObservableProperty]
    private bool _taskbarShowUsage = true;

    [ObservableProperty]
    private double _dailyUsageGoalHours = 6;

    // ===== 桌面硬件悬浮插件（3.23.0）=====
    [ObservableProperty]
    private bool _desktopWidgetEnabled = false;

    [ObservableProperty]
    private bool _desktopWidgetShowCpu = true;

    [ObservableProperty]
    private bool _desktopWidgetShowMemory = true;

    [ObservableProperty]
    private bool _desktopWidgetShowGpu = true;

    [ObservableProperty]
    private bool _desktopWidgetShowVram = true;

    [ObservableProperty]
    private bool _desktopWidgetShowNet = true;

    [ObservableProperty]
    private bool _desktopWidgetShowDisk = true;

    [ObservableProperty]
    private bool _desktopWidgetShowUsage = true;

    [ObservableProperty]
    private bool _desktopWidgetShowCurrentApp = true;

    [ObservableProperty]
    private bool _desktopWidgetTopmost = true;

    [ObservableProperty]
    private bool _desktopWidgetClickThrough = false;

    [ObservableProperty]
    private bool _desktopWidgetClampToScreen = true;

    [ObservableProperty]
    private bool _aiInsightsEnabled;

    [ObservableProperty]
    private string _aiBaseUrl = "https://api.openai.com/v1";

    [ObservableProperty]
    private string _aiApiKey = "";

    [ObservableProperty]
    private string _aiModel = "glm-4-flash";

    [ObservableProperty]
    private string _aiModelFast = "";

    [ObservableProperty]
    private string _aiLocalBaseUrl = "http://127.0.0.1:1234/v1";

    [ObservableProperty]
    private string _aiLocalModel = "local-model";

    [ObservableProperty]
    private string _aiCloudBaseUrl = "";

    [ObservableProperty]
    private string _aiCloudApiKey = "";

    [ObservableProperty]
    private string _aiCloudModel = "";

    [ObservableProperty]
    private string _aiEndpointMode = "cloud";

    [ObservableProperty]
    private string _aiProvider = "zhipu";

    [ObservableProperty]
    private double _aiTimeoutSeconds = 300;

    [ObservableProperty]
    private bool _aiStrictPrivacy;

    [ObservableProperty]
    private bool _aiAutoWeeklyInsight;

    // ===== 便签（3.24.0）=====
    [ObservableProperty]
    private bool _stickyNotesEnabled = true;

    [ObservableProperty]
    private string _stickyNotesHotkeyNew = "Ctrl+Alt+N";

    [ObservableProperty]
    private string _stickyNotesHotkeyBoard = "Ctrl+Alt+B";

    [ObservableProperty]
    private bool _stickyNotesSyncEnabled;

    [ObservableProperty]
    private int _stickyNotesRetentionDays = 30;

    [ObservableProperty]
    private bool _stickyNotesQuickDismissOnFocusLost = true;

    [ObservableProperty]
    private string _stickyNotesLastSyncText = "从未同步";

    [ObservableProperty]
    private int _dataRetentionDays = 90;
    
    [ObservableProperty]
    private bool _autoCleanupEnabled = true;

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new()
    {
        new ThemeOption { Key = "system", Label = "系统默认" },
        new ThemeOption { Key = "light", Label = "浅色" },
        new ThemeOption { Key = "dark", Label = "深色" }
    };
    
    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new()
    {
        new LanguageOption { Key = "zh-CN", Label = "简体中文" },
        new LanguageOption { Key = "en-US", Label = "English" }
    };

    public SettingsViewModel(ISettingsService settingsService) : base()
    {
        _settingsService = settingsService;
        Title = "设置";

        // 分类 UI 已于 3.30.0 迁至独立「分类管理」页；原分类加载/映射代码于 3.32.0 删除。
        // 分类种子改由 DatabaseInitializer 在启动期补齐，不再依赖用户是否打开过本页。

        Log.Information("SettingsViewModel: 构造函数完成");
    }

    public async Task InitializeAsync()
    {
        Log.Information("SettingsViewModel: InitializeAsync 开始");

        try
        {
            // Phase 1: 线程池 — 文件 I/O（设置项从 JSON 读入）
            await Task.Run(() => _settingsService.Load());

            // Phase 2: UI 线程 — 写入设置项 UI 状态
            await RunOnUIThreadAsync(() =>
            {
                LoadUiFromSettings();
                return Task.CompletedTask;
            });

            Log.Information("SettingsViewModel: InitializeAsync 完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsViewModel: InitializeAsync 失败");
        }
    }

    private void LoadUiFromSettings()
    {
        StartWithWindows = _settingsService.StartWithWindows;
        MinimizeToTray = _settingsService.MinimizeToTray;
        ShowNotifications = _settingsService.ShowNotifications;
        AutoStartMonitoring = _settingsService.AutoStartMonitoring;
        MonitoringInterval = _settingsService.MonitoringInterval;
        IdleThreshold = _settingsService.IdleThreshold;
        TrackKeyboard = _settingsService.TrackKeyboard;
        TrackMouse = _settingsService.TrackMouse;
        TrackWebBrowsing = _settingsService.TrackWebBrowsing;
        AnonymizeData = _settingsService.AnonymizeData;
        WebSocketPort = _settingsService.WebSocketPort;
        DataRetentionDays = _settingsService.DataRetentionDays;
        AutoCleanupEnabled = _settingsService.AutoCleanupEnabled;
        SelectedThemeKey = _settingsService.CurrentTheme;
        SelectedLanguageKey = _settingsService.CurrentLanguage;
        TaskbarEnabled = _settingsService.TaskbarEnabled;
        TaskbarShowCpu = _settingsService.TaskbarShowCpu;
        TaskbarShowMemory = _settingsService.TaskbarShowMemory;
        TaskbarShowGpu = _settingsService.TaskbarShowGpu;
        TaskbarShowNet = _settingsService.TaskbarShowNet;
        TaskbarShowDisk = _settingsService.TaskbarShowDisk;
        TaskbarShowTemp = _settingsService.TaskbarShowTemp;
        TaskbarShowUsage = _settingsService.TaskbarShowUsage;
        DailyUsageGoalHours = _settingsService.DailyUsageGoalHours;
        DesktopWidgetEnabled = _settingsService.DesktopWidgetEnabled;
        DesktopWidgetShowCpu = _settingsService.DesktopWidgetShowCpu;
        DesktopWidgetShowMemory = _settingsService.DesktopWidgetShowMemory;
        DesktopWidgetShowGpu = _settingsService.DesktopWidgetShowGpu;
        DesktopWidgetShowVram = _settingsService.DesktopWidgetShowVram;
        DesktopWidgetShowNet = _settingsService.DesktopWidgetShowNet;
        DesktopWidgetShowDisk = _settingsService.DesktopWidgetShowDisk;
        DesktopWidgetShowUsage = _settingsService.DesktopWidgetShowUsage;
        DesktopWidgetShowCurrentApp = _settingsService.DesktopWidgetShowCurrentApp;
        DesktopWidgetTopmost = _settingsService.DesktopWidgetTopmost;
        DesktopWidgetClickThrough = _settingsService.DesktopWidgetClickThrough;
        DesktopWidgetClampToScreen = _settingsService.DesktopWidgetClampToScreen;
        AiInsightsEnabled = _settingsService.AiInsightsEnabled;
        AiStrictPrivacy = _settingsService.AiStrictPrivacy;
        AiAutoWeeklyInsight = _settingsService.AiAutoWeeklyInsight;
        AiBaseUrl = _settingsService.AiBaseUrl;
        AiApiKey = _settingsService.AiApiKey;
        AiModel = _settingsService.AiModel;
        AiModelFast = _settingsService.AiModelFast;
        AiLocalBaseUrl = _settingsService.AiLocalBaseUrl;
        AiLocalModel = _settingsService.AiLocalModel;
        AiCloudBaseUrl = _settingsService.AiCloudBaseUrl;
        AiCloudApiKey = _settingsService.AiCloudApiKey;
        AiCloudModel = _settingsService.AiCloudModel;
        AiEndpointMode = _settingsService.AiEndpointMode;
        AiProvider = string.IsNullOrEmpty(_settingsService.AiProvider) ? "zhipu" : _settingsService.AiProvider;
        AiTimeoutSeconds = _settingsService.AiTimeoutSeconds;

        // 便签（3.24.0）
        StickyNotesEnabled = _settingsService.StickyNotesEnabled;
        StickyNotesHotkeyNew = _settingsService.StickyNotesHotkeyNew;
        StickyNotesHotkeyBoard = _settingsService.StickyNotesHotkeyBoard;
        StickyNotesSyncEnabled = _settingsService.StickyNotesSyncEnabled;
        StickyNotesRetentionDays = _settingsService.StickyNotesRetentionDays;
        StickyNotesQuickDismissOnFocusLost = _settingsService.StickyNotesQuickDismissOnFocusLost;
        StickyNotesLastSyncText = _settingsService.StickyNotesLastSync is { } last
            ? last.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "从未同步";
    }

    public void SaveSetting(string propertyName)
    {
        Log.Information("SaveSetting: 保存设置 {PropertyName}", propertyName);
        
        switch (propertyName)
        {
            case "StartWithWindows":
                _settingsService.StartWithWindows = StartWithWindows;
                break;
            case "MinimizeToTray":
                _settingsService.MinimizeToTray = MinimizeToTray;
                break;
            case "ShowNotifications":
                _settingsService.ShowNotifications = ShowNotifications;
                break;
            case "AutoStartMonitoring":
                _settingsService.AutoStartMonitoring = AutoStartMonitoring;
                break;
            case "MonitoringInterval":
                _settingsService.MonitoringInterval = MonitoringInterval;
                break;
            case "IdleThreshold":
                _settingsService.IdleThreshold = IdleThreshold;
                break;
            case "TrackKeyboard":
                _settingsService.TrackKeyboard = TrackKeyboard;
                break;
            case "TrackMouse":
                _settingsService.TrackMouse = TrackMouse;
                break;
            case "TrackWebBrowsing":
                _settingsService.TrackWebBrowsing = TrackWebBrowsing;
                break;
            case "SelectedThemeKey":
                _settingsService.CurrentTheme = SelectedThemeKey;
                break;
            case "SelectedLanguageKey":
                _settingsService.CurrentLanguage = SelectedLanguageKey;
                break;
            case "DataRetentionDays":
                _settingsService.DataRetentionDays = DataRetentionDays;
                break;
            case "AutoCleanupEnabled":
                _settingsService.AutoCleanupEnabled = AutoCleanupEnabled;
                break;
            case "TaskbarEnabled":
                _settingsService.TaskbarEnabled = TaskbarEnabled;
                break;
            case "TaskbarShowCpu":
                _settingsService.TaskbarShowCpu = TaskbarShowCpu;
                break;
            case "TaskbarShowMemory":
                _settingsService.TaskbarShowMemory = TaskbarShowMemory;
                break;
            case "TaskbarShowGpu":
                _settingsService.TaskbarShowGpu = TaskbarShowGpu;
                break;
            case "TaskbarShowNet":
                _settingsService.TaskbarShowNet = TaskbarShowNet;
                break;
            case "TaskbarShowDisk":
                _settingsService.TaskbarShowDisk = TaskbarShowDisk;
                break;
            case "TaskbarShowTemp":
                _settingsService.TaskbarShowTemp = TaskbarShowTemp;
                break;
            case "TaskbarShowUsage":
                _settingsService.TaskbarShowUsage = TaskbarShowUsage;
                break;
            case "DailyUsageGoalHours":
                _settingsService.DailyUsageGoalHours = DailyUsageGoalHours;
                break;
            case "DesktopWidgetEnabled":
                _settingsService.DesktopWidgetEnabled = DesktopWidgetEnabled;
                break;
            case "DesktopWidgetShowCpu":
                _settingsService.DesktopWidgetShowCpu = DesktopWidgetShowCpu;
                break;
            case "DesktopWidgetShowMemory":
                _settingsService.DesktopWidgetShowMemory = DesktopWidgetShowMemory;
                break;
            case "DesktopWidgetShowGpu":
                _settingsService.DesktopWidgetShowGpu = DesktopWidgetShowGpu;
                break;
            case "DesktopWidgetShowVram":
                _settingsService.DesktopWidgetShowVram = DesktopWidgetShowVram;
                break;
            case "DesktopWidgetShowNet":
                _settingsService.DesktopWidgetShowNet = DesktopWidgetShowNet;
                break;
            case "DesktopWidgetShowDisk":
                _settingsService.DesktopWidgetShowDisk = DesktopWidgetShowDisk;
                break;
            case "DesktopWidgetShowUsage":
                _settingsService.DesktopWidgetShowUsage = DesktopWidgetShowUsage;
                break;
            case "DesktopWidgetShowCurrentApp":
                _settingsService.DesktopWidgetShowCurrentApp = DesktopWidgetShowCurrentApp;
                break;
            case "DesktopWidgetTopmost":
                _settingsService.DesktopWidgetTopmost = DesktopWidgetTopmost;
                break;
            case "DesktopWidgetClickThrough":
                _settingsService.DesktopWidgetClickThrough = DesktopWidgetClickThrough;
                break;
            case "DesktopWidgetClampToScreen":
                _settingsService.DesktopWidgetClampToScreen = DesktopWidgetClampToScreen;
                break;
            case "AiInsightsEnabled":
                _settingsService.AiInsightsEnabled = AiInsightsEnabled;
                break;
            case "AiStrictPrivacy":
                _settingsService.AiStrictPrivacy = AiStrictPrivacy;
                break;
            case "AiAutoWeeklyInsight":
                _settingsService.AiAutoWeeklyInsight = AiAutoWeeklyInsight;
                break;
            case "AiBaseUrl":
                _settingsService.AiBaseUrl = AiBaseUrl;
                break;
            case "AiApiKey":
                _settingsService.AiApiKey = AiApiKey;
                break;
            case "AiModel":
                _settingsService.AiModel = AiModel;
                break;
            case "AiModelFast":
                _settingsService.AiModelFast = AiModelFast;
                break;
            case "AiLocalBaseUrl":
                _settingsService.AiLocalBaseUrl = AiLocalBaseUrl;
                break;
            case "AiLocalModel":
                _settingsService.AiLocalModel = AiLocalModel;
                break;
            case "AiCloudBaseUrl":
                _settingsService.AiCloudBaseUrl = AiCloudBaseUrl;
                break;
            case "AiCloudApiKey":
                _settingsService.AiCloudApiKey = AiCloudApiKey;
                break;
            case "AiCloudModel":
                _settingsService.AiCloudModel = AiCloudModel;
                break;
            case "AiEndpointMode":
                _settingsService.AiEndpointMode = AiEndpointMode;
                break;
            case "AiProvider":
                _settingsService.AiProvider = AiProvider;
                // 预设仅用于「一键填充」，这里不再根据 URL 内容自动覆盖用户配置
                break;
            case "AiTimeoutSeconds":
                _settingsService.AiTimeoutSeconds = (int)Math.Clamp(AiTimeoutSeconds, 30, 900);
                break;

            // 便签（3.24.0）
            case "StickyNotesEnabled":
                _settingsService.StickyNotesEnabled = StickyNotesEnabled;
                break;
            case "StickyNotesHotkeyNew":
                _settingsService.StickyNotesHotkeyNew = StickyNotesHotkeyNew?.Trim() ?? "";
                break;
            case "StickyNotesHotkeyBoard":
                _settingsService.StickyNotesHotkeyBoard = StickyNotesHotkeyBoard?.Trim() ?? "";
                break;
            case "StickyNotesSyncEnabled":
                _settingsService.StickyNotesSyncEnabled = StickyNotesSyncEnabled;
                break;
            case "StickyNotesRetentionDays":
                _settingsService.StickyNotesRetentionDays = Math.Clamp(StickyNotesRetentionDays, 1, 3650);
                break;
            case "StickyNotesQuickDismissOnFocusLost":
                _settingsService.StickyNotesQuickDismissOnFocusLost = StickyNotesQuickDismissOnFocusLost;
                break;

        }
        
        // 同步落盘：异步 Task.Run 会在退出/并发时丢设置
        _settingsService.Save();
        Log.Information("SaveSetting: 设置 {PropertyName} 已保存", propertyName);
    }

    private void ApplyTheme(string themeKey)
    {
        try
        {
            if (App.Current is App app)
            {
                var windowField = typeof(App).GetField("_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var window = windowField?.GetValue(app) as Microsoft.UI.Xaml.Window;
                if (window?.Content is Microsoft.UI.Xaml.FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = themeKey switch
                    {
                        "light" => Microsoft.UI.Xaml.ElementTheme.Light,
                        "dark" => Microsoft.UI.Xaml.ElementTheme.Dark,
                        _ => Microsoft.UI.Xaml.ElementTheme.Default
                    };
                }
            }

            // 更新软色背景画刷以适应深色/浅色主题
            UpdateSoftColorsForTheme(themeKey);

            Log.Information("主题已切换: {Theme}", themeKey);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "切换主题失败");
        }
    }

    private static void UpdateSoftColorsForTheme(string themeKey)
    {
        var isDark = themeKey == "dark";
        try
        {
            var resources = App.Current.Resources;

            // PrimarySoft: 浅色 #DBEAFE → 深色 #1E3A5F
            if (resources["PrimarySoftBrush"] is SolidColorBrush primarySoftBrush)
                primarySoftBrush.Color = isDark ? Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x3A, 0x5F) : Windows.UI.Color.FromArgb(0xFF, 0xDB, 0xEA, 0xFE);

            // SuccessSoft: 浅色 #D1FAE5 → 深色 #064E3B
            if (resources["SuccessSoftBrush"] is SolidColorBrush successSoftBrush)
                successSoftBrush.Color = isDark ? Windows.UI.Color.FromArgb(0xFF, 0x06, 0x4E, 0x3B) : Windows.UI.Color.FromArgb(0xFF, 0xD1, 0xFA, 0xE5);

            // WarningSoft: 浅色 #FEF3C7 → 深色 #713F12
            if (resources["WarningSoftBrush"] is SolidColorBrush warningSoftBrush)
                warningSoftBrush.Color = isDark ? Windows.UI.Color.FromArgb(0xFF, 0x71, 0x3F, 0x12) : Windows.UI.Color.FromArgb(0xFF, 0xFE, 0xF3, 0xC7);

            // InsightSoft: 浅色 #EDE9FE → 深色 #2E1065
            if (resources["InsightSoftBrush"] is SolidColorBrush insightSoftBrush)
                insightSoftBrush.Color = isDark ? Windows.UI.Color.FromArgb(0xFF, 0x2E, 0x10, 0x65) : Windows.UI.Color.FromArgb(0xFF, 0xED, 0xE9, 0xFE);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新软色画刷失败");
        }
    }

}

