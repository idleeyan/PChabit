using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;


namespace PChabit.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly MonitorManager _monitorManager;
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
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

    [ObservableProperty]
    private bool _aiInsightsEnabled;

    [ObservableProperty]
    private string _aiBaseUrl = "https://api.openai.com/v1";

    [ObservableProperty]
    private string _aiApiKey = "";

    [ObservableProperty]
    private string _aiModel = "glm-4-flash";

    [ObservableProperty]
    private string _aiProvider = "zhipu";

    [ObservableProperty]
    private double _aiTimeoutSeconds = 300;

    
    [ObservableProperty]
    private int _dataRetentionDays = 90;
    
    [ObservableProperty]
    private bool _autoCleanupEnabled = true;
    
    [ObservableProperty]
    private int _connectedBrowsers = 0;
    
    [ObservableProperty]
    private string _statusMessage = "";
    
    [ObservableProperty]
    private string _categorySearchText = "";
    
    [ObservableProperty]
    private bool _isCategorySelectionMode;
    
    [ObservableProperty]
    private int _selectedCategoryCount;

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
    
    public ObservableCollection<CategoryMapping> CategoryMappings { get; } = new();
    
    public ObservableCollection<CategoryDisplayItem> Categories { get; } = new();

    [ObservableProperty]
    private string _newCategoryName = "";

    public SettingsViewModel(ISettingsService settingsService, MonitorManager monitorManager, IDbContextFactory<PChabitDbContext> dbFactory) : base()
    {
        _settingsService = settingsService;
        _monitorManager = monitorManager;
        _dbFactory = dbFactory;
        Title = "设置";
        
        // LoadSettings 已改为异步，在 InitializeAsync 中调用以避免构造函数中同步文件 I/O 阻塞 UI
        LoadCategoryMappings();
        
        Log.Information("SettingsViewModel: 构造函数完成");
    }

    public async Task InitializeAsync()
    {
        Log.Information("SettingsViewModel: InitializeAsync 开始");

        try
        {
            // Phase 1: 线程池 — 文件 I/O + DB 查询
            var (catItems, totalCount) = await Task.Run(async () =>
            {
                _settingsService.Load();

                await using var dbContext = await _dbFactory.CreateDbContextAsync();

                await InitializeDefaultCategoriesAsync(dbContext);

                var categories = await dbContext.ProgramCategories
                    .Include(c => c.ProgramMappings)
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .ThenBy(c => c.Name)
                    .ToListAsync();

                var mappings = await dbContext.ProgramCategoryMappings.ToListAsync();

                var items = categories.Select(category =>
                {
                    var programCount = mappings.Count(m => m.CategoryId == category.Id);
                    return new CategoryDisplayItem
                    {
                        Id = category.Id,
                        Name = category.Name,
                        Description = category.Description ?? "",
                        Icon = category.Icon,
                        Color = category.Color,
                        IsSystem = category.IsSystem,
                        ProgramCount = programCount,
                        SortOrder = category.SortOrder
                    };
                }).ToList();

                Log.Information("SettingsViewModel: 获取到 {Count} 个分类", items.Count);
                Log.Information("SettingsViewModel: 获取到 {Count} 个映射", mappings.Count);

                return (items, items.Count);
            });

            // Phase 2: UI 线程 — 设置 UI 状态 + ObservableCollection 更新
            await RunOnUIThreadAsync(() =>
            {
                LoadUiFromSettings();

                Categories.Clear();
                foreach (var item in catItems)
                {
                    Categories.Add(item);
                }
                return Task.CompletedTask;
            });

            UpdateSelectedCategoryCount();
            UpdateConnectedBrowsers();
            StatusMessage = $"已加载 {totalCount} 个类别";
            Log.Information("SettingsViewModel: InitializeAsync 完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsViewModel: InitializeAsync 失败");
            StatusMessage = "加载类别失败";
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
        AiInsightsEnabled = _settingsService.AiInsightsEnabled;
        AiBaseUrl = _settingsService.AiBaseUrl;
        AiApiKey = _settingsService.AiApiKey;
        AiModel = _settingsService.AiModel;
        AiProvider = string.IsNullOrEmpty(_settingsService.AiProvider) ? "zhipu" : _settingsService.AiProvider;
        AiTimeoutSeconds = _settingsService.AiTimeoutSeconds;
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
            case "AiInsightsEnabled":
                _settingsService.AiInsightsEnabled = AiInsightsEnabled;
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
            case "AiProvider":
                _settingsService.AiProvider = AiProvider;
                var preset = PChabit.Infrastructure.Services.AiProviderPresets.All
                    .FirstOrDefault(p => string.Equals(p.Kind.ToString(), AiProvider, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.Label, AiProvider, StringComparison.Ordinal));
                // 若通过枚举名匹配
                if (Enum.TryParse<PChabit.Infrastructure.Services.AiProviderKind>(AiProvider, true, out var kind))
                    preset = PChabit.Infrastructure.Services.AiProviderPresets.Get(kind);
                if (preset != null)
                {
                    if (string.IsNullOrWhiteSpace(AiBaseUrl) || AiBaseUrl.Contains("bigmodel") || AiBaseUrl.Contains("deepseek") || AiBaseUrl.Contains("1234/v1"))
                    {
                        AiBaseUrl = preset.DefaultBaseUrl;
                        _settingsService.AiBaseUrl = AiBaseUrl;
                    }
                    if (string.IsNullOrWhiteSpace(AiModel) || AiModel is "gpt-4o-mini" or "glm-4-flash" or "deepseek-chat" or "local-model")
                    {
                        AiModel = preset.DefaultModel;
                        _settingsService.AiModel = AiModel;
                    }
                }
                break;
            case "AiTimeoutSeconds":
                _settingsService.AiTimeoutSeconds = (int)Math.Clamp(AiTimeoutSeconds, 30, 900);
                break;

        }
        
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("SaveSetting: 设置 {PropertyName} 已保存", propertyName);
    }

    private void UpdateConnectedBrowsers()
    {
        ConnectedBrowsers = _monitorManager.GetConnectedBrowserCount();
    }

    private void ApplyMonitorSettings()
    {
        if (!TrackKeyboard)
        {
            _monitorManager.StopKeyboardMonitor();
        }
        else
        {
            _monitorManager.StartKeyboardMonitor();
        }
        
        if (!TrackMouse)
        {
            _monitorManager.StopMouseMonitor();
        }
        else
        {
            _monitorManager.StartMouseMonitor();
        }
        
        _monitorManager.WebMonitoringEnabled = TrackWebBrowsing;
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

    [RelayCommand]
    private void SaveSettings()
    {
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存");
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        _settingsService.ResetToDefaults();
        LoadUiFromSettings();
        Log.Information("设置已重置为默认值");
    }

    [RelayCommand]
    private void ExportSettings()
    {
        try
        {
            var exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PChabit", "settings_export.json");
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            _ = Task.Run(() => _settingsService.SaveAsync());
            File.Copy(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PChabit", "settings.json"),
                exportPath,
                true);
            Log.Information("设置已导出到: {Path}", exportPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出设置失败");
        }
    }

    [RelayCommand]
    private void ImportSettings()
    {
        try
        {
            var importPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PChabit", "settings_export.json");
            if (File.Exists(importPath))
            {
                var destPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PChabit", "settings.json");
                File.Copy(importPath, destPath, true);
                _settingsService.Load();
                LoadUiFromSettings();
                Log.Information("设置已导入");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导入设置失败");
        }
    }

    [RelayCommand]
    private void RefreshConnectionStatus()
    {
        UpdateConnectedBrowsers();
    }

}

