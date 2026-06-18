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

    private static async Task InitializeDefaultCategoriesAsync(PChabitDbContext dbContext)
    {
        if (await dbContext.ProgramCategories.AnyAsync())
        {
            return;
        }

        var now = DateTime.Now;
        var defaultCategories = new List<ProgramCategory>
        {
            new() { Name = "开发", Description = "开发工具和IDE", Color = "#4A90E4", Icon = "💻", SortOrder = 1, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "浏览", Description = "浏览器", Color = "#50C878", Icon = "🌐", SortOrder = 2, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "沟通", Description = "即时通讯和邮件", Color = "#FF6B6B", Icon = "💬", SortOrder = 3, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "娱乐", Description = "游戏和娱乐", Color = "#9B59B6", Icon = "🎮", SortOrder = 4, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "办公", Description = "办公软件", Color = "#F39C12", Icon = "📊", SortOrder = 5, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "设计", Description = "设计工具", Color = "#E74C3C", Icon = "🎨", SortOrder = 6, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "其他", Description = "未分类程序", Color = "#95A5A6", Icon = "📁", SortOrder = 99, IsSystem = true, IsActive = true, CreatedAt = now }
        };

        var defaultMappings = new List<ProgramCategoryMapping>
        {
            new() { ProcessName = "code.exe", CategoryId = 1 },
            new() { ProcessName = "devenv.exe", CategoryId = 1 },
            new() { ProcessName = "idea64.exe", CategoryId = 1 },
            new() { ProcessName = "pycharm64.exe", CategoryId = 1 },
            new() { ProcessName = "chrome.exe", CategoryId = 2 },
            new() { ProcessName = "msedge.exe", CategoryId = 2 },
            new() { ProcessName = "firefox.exe", CategoryId = 2 },
            new() { ProcessName = "slack.exe", CategoryId = 3 },
            new() { ProcessName = "discord.exe", CategoryId = 3 },
            new() { ProcessName = "teams.exe", CategoryId = 3 },
            new() { ProcessName = "outlook.exe", CategoryId = 3 },
            new() { ProcessName = "spotify.exe", CategoryId = 4 },
            new() { ProcessName = "steam.exe", CategoryId = 4 },
            new() { ProcessName = "wmplayer.exe", CategoryId = 4 },
            new() { ProcessName = "WINWORD.EXE", CategoryId = 5 },
            new() { ProcessName = "EXCEL.EXE", CategoryId = 5 },
            new() { ProcessName = "POWERPNT.EXE", CategoryId = 5 },
            new() { ProcessName = "Photoshop.exe", CategoryId = 6 },
            new() { ProcessName = "Figma.exe", CategoryId = 6 }
        };

        dbContext.ProgramCategories.AddRange(defaultCategories);
        await dbContext.SaveChangesAsync();

        foreach (var mapping in defaultMappings)
        {
            var category = defaultCategories.FirstOrDefault(c => c.Id == mapping.CategoryId);
            if (category != null)
            {
                mapping.ProcessAlias = category.Name;
            }
        }

        dbContext.ProgramCategoryMappings.AddRange(defaultMappings);
        await dbContext.SaveChangesAsync();

        Log.Information("已初始化默认类别和映射");
    }
    
    public async Task LoadCategoriesAsync()
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            await InitializeDefaultCategoriesAsync(dbContext);

            var categories = await dbContext.ProgramCategories
                .Include(c => c.ProgramMappings)
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            var mappings = await dbContext.ProgramCategoryMappings.ToListAsync();

            await RunOnUIThreadAsync(() =>
            {
                Categories.Clear();
                foreach (var category in categories)
                {
                    var programCount = mappings.Count(m => m.CategoryId == category.Id);
                    Categories.Add(new CategoryDisplayItem
                    {
                        Id = category.Id,
                        Name = category.Name,
                        Description = category.Description ?? "",
                        Icon = category.Icon,
                        Color = category.Color,
                        IsSystem = category.IsSystem,
                        ProgramCount = programCount,
                        SortOrder = category.SortOrder
                    });
                }

                UpdateSelectedCategoryCount();
                StatusMessage = $"已加载 {Categories.Count} 个类别";
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载类别失败");
            StatusMessage = "加载类别失败";
        }
    }
    
    [RelayCommand]
    private async Task AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            if (await dbContext.ProgramCategories.AnyAsync(c => c.Name == NewCategoryName && c.IsActive))
            {
                StatusMessage = "类别名称已存在";
                return;
            }

            var category = new ProgramCategory
            {
                Name = NewCategoryName,
                Description = "",
                Color = "#4A90E4",
                Icon = "📁",
                SortOrder = Categories.Count + 1,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            dbContext.ProgramCategories.Add(category);
            await dbContext.SaveChangesAsync();
            await LoadCategoriesAsync();

            NewCategoryName = "";
            StatusMessage = "类别创建成功";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建类别失败");
            StatusMessage = "创建类别失败";
        }
    }
    
    public async Task SaveCategoryFromDialogAsync(ProgramCategory category, bool isEditMode)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            if (isEditMode)
            {
                var existing = await dbContext.ProgramCategories.FindAsync(category.Id);
                if (existing != null)
                {
                    existing.Name = category.Name;
                    existing.Description = category.Description;
                    existing.Color = category.Color;
                    existing.Icon = category.Icon;
                    existing.SortOrder = category.SortOrder;
                    existing.UpdatedAt = DateTime.Now;
                    await dbContext.SaveChangesAsync();
                }
                StatusMessage = $"分类 \"{category.Name}\" 已更新";
            }
            else
            {
                category.IsActive = true;
                category.CreatedAt = DateTime.Now;
                dbContext.ProgramCategories.Add(category);
                await dbContext.SaveChangesAsync();
                StatusMessage = $"分类 \"{category.Name}\" 已创建";
            }

            await LoadCategoriesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存分类失败");
            StatusMessage = "保存分类失败";
        }
    }
    
    [RelayCommand]
    private async Task DeleteCategory(CategoryDisplayItem? item)
    {
        if (item == null) return;

        if (item.IsSystem)
        {
            StatusMessage = "系统分类不能删除";
            return;
        }

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var category = await dbContext.ProgramCategories.FindAsync(item.Id);
            if (category != null)
            {
                category.IsActive = false;
                await dbContext.SaveChangesAsync();
            }

            await LoadCategoriesAsync();
            StatusMessage = $"分类 \"{item.Name}\" 已删除";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除分类失败");
            StatusMessage = "删除分类失败";
        }
    }
    
    [RelayCommand]
    private void ToggleCategorySelection(CategoryDisplayItem? item)
    {
        if (item == null) return;
        
        item.IsSelected = !item.IsSelected;
        UpdateSelectedCategoryCount();
    }
    
    [RelayCommand]
    private void SelectAllCategories()
    {
        foreach (var category in Categories.Where(c => !c.IsSystem))
        {
            category.IsSelected = true;
        }
        UpdateSelectedCategoryCount();
    }
    
    [RelayCommand]
    private void DeselectAllCategories()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = false;
        }
        UpdateSelectedCategoryCount();
    }
    
    [RelayCommand]
    private async Task DeleteSelectedCategories()
    {
        var selectedIds = Categories.Where(c => c.IsSelected && !c.IsSystem).Select(c => c.Id).ToList();

        if (selectedIds.Count == 0)
        {
            StatusMessage = "请选择要删除的分类";
            return;
        }

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var categories = await dbContext.ProgramCategories
                .Where(c => selectedIds.Contains(c.Id) && !c.IsSystem)
                .ToListAsync();

            foreach (var category in categories)
            {
                category.IsActive = false;
            }

            await dbContext.SaveChangesAsync();
            await LoadCategoriesAsync();
            StatusMessage = $"已删除 {categories.Count} 个分类";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "批量删除分类失败");
            StatusMessage = "批量删除失败";
        }
    }
    
    [RelayCommand]
    private async Task MoveCategoryUp(CategoryDisplayItem? item)
    {
        if (item == null) return;

        var index = Categories.IndexOf(item);
        if (index <= 0) return;

        var previousItem = Categories[index - 1];
        var tempOrder = item.SortOrder;
        item.SortOrder = previousItem.SortOrder;
        previousItem.SortOrder = tempOrder;

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var cat1 = await dbContext.ProgramCategories.FindAsync(item.Id);
            var cat2 = await dbContext.ProgramCategories.FindAsync(previousItem.Id);
            if (cat1 != null) cat1.SortOrder = item.SortOrder;
            if (cat2 != null) cat2.SortOrder = previousItem.SortOrder;
            await dbContext.SaveChangesAsync();

            Categories.Move(index, index - 1);
            StatusMessage = "排序已更新";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新排序失败");
            StatusMessage = "更新排序失败";
        }
    }

    [RelayCommand]
    private async Task MoveCategoryDown(CategoryDisplayItem? item)
    {
        if (item == null) return;

        var index = Categories.IndexOf(item);
        if (index < 0 || index >= Categories.Count - 1) return;

        var nextItem = Categories[index + 1];
        var tempOrder = item.SortOrder;
        item.SortOrder = nextItem.SortOrder;
        nextItem.SortOrder = tempOrder;

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var cat1 = await dbContext.ProgramCategories.FindAsync(item.Id);
            var cat2 = await dbContext.ProgramCategories.FindAsync(nextItem.Id);
            if (cat1 != null) cat1.SortOrder = item.SortOrder;
            if (cat2 != null) cat2.SortOrder = nextItem.SortOrder;
            await dbContext.SaveChangesAsync();

            Categories.Move(index, index + 1);
            StatusMessage = "排序已更新";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新排序失败");
            StatusMessage = "更新排序失败";
        }
    }

    [RelayCommand]
    private async Task ExportCategories()
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var categories = await dbContext.ProgramCategories.Where(c => c.IsActive).ToListAsync();
            var mappings = await dbContext.ProgramCategoryMappings.ToListAsync();

            var export = new
            {
                Categories = categories,
                Mappings = mappings,
                ExportedAt = DateTime.Now
            };

            var json = System.Text.Json.JsonSerializer.Serialize(export);
            var exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PChabit", "categories_export.json");
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            await File.WriteAllTextAsync(exportPath, json);

            StatusMessage = $"分类已导出到: {exportPath}";
            Log.Information("分类已导出到: {Path}", exportPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出分类失败");
            StatusMessage = "导出分类失败";
        }
    }

    [RelayCommand]
    private async Task ImportCategories()
    {
        try
        {
            var importPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PChabit", "categories_export.json");
            if (!File.Exists(importPath))
            {
                StatusMessage = "未找到导入文件";
                return;
            }

            var json = await File.ReadAllTextAsync(importPath);

            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            int count = 0;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Categories", out var categoriesElement))
                {
                    foreach (var category in categoriesElement.EnumerateArray())
                    {
                        var name = category.GetProperty("Name").GetString() ?? "";
                        var existing = await dbContext.ProgramCategories
                            .FirstOrDefaultAsync(c => c.Name == name);

                        if (existing == null)
                        {
                            dbContext.ProgramCategories.Add(new ProgramCategory
                            {
                                Name = name,
                                Description = category.TryGetProperty("Description", out var desc) ? desc.GetString() ?? "" : "",
                                Color = category.TryGetProperty("Color", out var color) ? color.GetString() ?? "#4A90E4" : "#4A90E4",
                                Icon = category.TryGetProperty("Icon", out var icon) ? icon.GetString() ?? "📁" : "📁",
                                SortOrder = category.TryGetProperty("SortOrder", out var sort) ? sort.GetInt32() : 99,
                                IsSystem = false,
                                IsActive = true,
                                CreatedAt = DateTime.Now
                            });
                            count++;
                        }
                    }
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "导入类别失败");
            }

            await LoadCategoriesAsync();
            StatusMessage = $"已导入 {count} 个分类";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导入分类失败");
            StatusMessage = "导入分类失败";
        }
    }
    
    private void UpdateSelectedCategoryCount()
    {
        SelectedCategoryCount = Categories.Count(c => c.IsSelected);
    }
    
    [ObservableProperty]
    private string _newCategoryName = "";
    
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
        }
        
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("SaveSetting: 设置 {PropertyName} 已保存", propertyName);
    }
    
    private void LoadCategoryMappings()
    {
        CategoryMappings.Clear();
        CategoryMappings.Add(new CategoryMapping { ProcessName = "code.exe", Category = "开发", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "devenv.exe", Category = "开发", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "chrome.exe", Category = "浏览", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "msedge.exe", Category = "浏览", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "slack.exe", Category = "沟通", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "spotify.exe", Category = "娱乐", IsEditable = true });
    }
    
    private void UpdateConnectedBrowsers()
    {
        ConnectedBrowsers = _monitorManager.GetConnectedBrowserCount();
    }
    
    partial void OnStartWithWindowsChanged(bool value)
    {
        Log.Information("OnStartWithWindowsChanged: {Value}", value);
        _settingsService.StartWithWindows = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: StartWithWindows = {Value}", value);
    }
    
    partial void OnMinimizeToTrayChanged(bool value)
    {
        Log.Information("OnMinimizeToTrayChanged: {Value}", value);
        _settingsService.MinimizeToTray = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: MinimizeToTray = {Value}", value);
    }
    
    partial void OnShowNotificationsChanged(bool value)
    {
        Log.Information("OnShowNotificationsChanged: {Value}", value);
        _settingsService.ShowNotifications = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: ShowNotifications = {Value}", value);
    }
    
    partial void OnAutoStartMonitoringChanged(bool value)
    {
        Log.Information("OnAutoStartMonitoringChanged: {Value}", value);
        _settingsService.AutoStartMonitoring = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: AutoStartMonitoring = {Value}", value);
    }
    
    partial void OnMonitoringIntervalChanged(int value)
    {
        Log.Information("OnMonitoringIntervalChanged: {Value}", value);
        _settingsService.MonitoringInterval = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: MonitoringInterval = {Value}", value);
    }
    
    partial void OnIdleThresholdChanged(int value)
    {
        Log.Information("OnIdleThresholdChanged: {Value}", value);
        _settingsService.IdleThreshold = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: IdleThreshold = {Value}", value);
    }
    
    partial void OnTrackKeyboardChanged(bool value)
    {
        Log.Information("OnTrackKeyboardChanged: {Value}", value);
        _settingsService.TrackKeyboard = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: TrackKeyboard = {Value}", value);
        // ApplyMonitorSettings 内部调用 Win32 钩子安装（SetWindowsHookEx），
        // 必须在 UI 线程执行（钩子需要消息循环），否则在 Task.Run 线程会卡死（陷阱 #5.5）
        RunOnUIThread(ApplyMonitorSettings);
    }

    partial void OnTrackMouseChanged(bool value)
    {
        Log.Information("OnTrackMouseChanged: {Value}", value);
        _settingsService.TrackMouse = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: TrackMouse = {Value}", value);
        RunOnUIThread(ApplyMonitorSettings);
    }

    partial void OnTrackWebBrowsingChanged(bool value)
    {
        Log.Information("OnTrackWebBrowsingChanged: {Value}", value);
        _settingsService.TrackWebBrowsing = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: TrackWebBrowsing = {Value}", value);
        RunOnUIThread(ApplyMonitorSettings);
    }
    
    partial void OnAnonymizeDataChanged(bool value)
    {
        Log.Information("OnAnonymizeDataChanged: {Value}", value);
        _settingsService.AnonymizeData = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: AnonymizeData = {Value}", value);
    }
    
    partial void OnWebSocketPortChanged(string value)
    {
        Log.Information("OnWebSocketPortChanged: {Value}", value);
        _settingsService.WebSocketPort = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
        Log.Information("设置已保存: WebSocketPort = {Value}", value);
    }
    
    partial void OnSelectedThemeKeyChanged(string value)
    {
        Log.Information("OnSelectedThemeKeyChanged: {Value}", value);
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settingsService.CurrentTheme = value;
            _ = Task.Run(() => _settingsService.SaveAsync());
            Log.Information("设置已保存: CurrentTheme = {Value}", value);
            // ApplyTheme 访问 Window.Content（COM 对象），必须在 UI 线程调用（陷阱 #5.5 / #10）
            RunOnUIThread(() => ApplyTheme(value));
        }
    }
    
    partial void OnSelectedLanguageKeyChanged(string value)
    {
        Log.Information("OnSelectedLanguageKeyChanged: {Value}", value);
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settingsService.CurrentLanguage = value;
            _ = Task.Run(() => _settingsService.SaveAsync());
            Log.Information("设置已保存: CurrentLanguage = {Value}", value);
        }
    }
    
    partial void OnDataRetentionDaysChanged(int value)
    {
        if (value < 30) value = 30;
        if (value > 365) value = 365;
        _dataRetentionDays = value;
        _settingsService.DataRetentionDays = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
    }
    
    partial void OnAutoCleanupEnabledChanged(bool value)
    {
        _settingsService.AutoCleanupEnabled = value;
        _ = Task.Run(() => _settingsService.SaveAsync());
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

    /// <summary>
    /// 根据主题更新软色背景画刷（深色模式下变暗以保持视觉舒适度）
    /// </summary>
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
    private void AddCategoryMapping()
    {
        CategoryMappings.Add(new CategoryMapping { ProcessName = "new.exe", Category = "其他", IsEditable = true });
    }
    
    [RelayCommand]
    private void RemoveCategoryMapping(CategoryMapping? mapping)
    {
        if (mapping != null)
        {
            CategoryMappings.Remove(mapping);
        }
    }
    
    [RelayCommand]
    private void RefreshConnectionStatus()
    {
        UpdateConnectedBrowsers();
    }
}

public class ThemeOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public class LanguageOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public class CategoryMapping
{
    public string ProcessName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsEditable { get; init; }
}

public partial class CategoryDisplayItem : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "📁";
    public string Color { get; set; } = "#4A90E4";
    public bool IsSystem { get; set; }
    public int ProgramCount { get; set; }
    public int SortOrder { get; set; }
    
    [ObservableProperty]
    private bool _isSelected;
}
