using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Tai.Core.Entities;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Services;

namespace Tai.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly MonitorManager _monitorManager;
    private readonly ICategoryService _categoryService;
    
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
    private string _dataPath = string.Empty;
    
    [ObservableProperty]
    private string _databaseSize = "0 MB";
    
    [ObservableProperty]
    private int _retentionDays = 90;
    
    [ObservableProperty]
    private string _webSocketPort = "8765";
    
    [ObservableProperty]
    private string _selectedThemeKey = "system";
    
    [ObservableProperty]
    private string _selectedLanguageKey = "zh-CN";
    
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
    
    public SettingsViewModel(ISettingsService settingsService, MonitorManager monitorManager, ICategoryService categoryService) : base()
    {
        _settingsService = settingsService;
        _monitorManager = monitorManager;
        _categoryService = categoryService;
        Title = "设置";
        DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tai", "Data", "tai.db");
        
        LoadSettings();
        
        Log.Information("SettingsViewModel: 构造函数完成");
    }
    
    public async Task InitializeAsync()
    {
        Log.Information("SettingsViewModel: InitializeAsync 开始");
        
        try
        {
            await Task.Run(() => _categoryService.InitializeDefaultCategoriesSync());
            Log.Information("SettingsViewModel: 默认分类初始化完成");
            
            var categories = await Task.Run(() => _categoryService.GetAllCategoriesSync());
            Log.Information("SettingsViewModel: 获取到 {Count} 个分类", categories.Count);
            
            var mappings = await Task.Run(() => _categoryService.GetAllMappingsSync());
            Log.Information("SettingsViewModel: 获取到 {Count} 个映射", mappings.Count);
            
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
            UpdateDatabaseSize();
            UpdateConnectedBrowsers();
            StatusMessage = $"已加载 {Categories.Count} 个类别";
            Log.Information("SettingsViewModel: InitializeAsync 完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsViewModel: InitializeAsync 失败");
            StatusMessage = "加载类别失败";
        }
    }
    
    public void LoadCategories()
    {
        try
        {
            _categoryService.InitializeDefaultCategoriesSync();
            var categories = _categoryService.GetAllCategoriesSync();
            var mappings = _categoryService.GetAllMappingsSync();
            
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
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载类别失败");
            StatusMessage = "加载类别失败";
        }
    }
    
    [RelayCommand]
    private void AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;
        
        try
        {
            if (_categoryService.CategoryExists(NewCategoryName))
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
                SortOrder = Categories.Count + 1
            };
            
            _categoryService.CreateCategory(category);
            LoadCategories();
            
            NewCategoryName = "";
            StatusMessage = "类别创建成功";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建类别失败");
            StatusMessage = "创建类别失败";
        }
    }
    
    public void SaveCategoryFromDialog(ProgramCategory category, bool isEditMode)
    {
        try
        {
            if (isEditMode)
            {
                _categoryService.UpdateCategory(category);
                StatusMessage = $"分类 \"{category.Name}\" 已更新";
            }
            else
            {
                _categoryService.CreateCategory(category);
                StatusMessage = $"分类 \"{category.Name}\" 已创建";
            }
            
            LoadCategories();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存分类失败");
            StatusMessage = "保存分类失败";
        }
    }
    
    [RelayCommand]
    private void DeleteCategory(CategoryDisplayItem? item)
    {
        if (item == null) return;
        
        if (item.IsSystem)
        {
            StatusMessage = "系统分类不能删除";
            return;
        }
        
        try
        {
            _categoryService.DeleteCategory(item.Id);
            LoadCategories();
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
            var deletedCount = await _categoryService.DeleteCategoriesAsync(selectedIds);
            LoadCategories();
            StatusMessage = $"已删除 {deletedCount} 个分类";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "批量删除分类失败");
            StatusMessage = "批量删除失败";
        }
    }
    
    [RelayCommand]
    private void MoveCategoryUp(CategoryDisplayItem? item)
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
            _categoryService.UpdateCategorySortOrderAsync(item.Id, item.SortOrder);
            _categoryService.UpdateCategorySortOrderAsync(previousItem.Id, previousItem.SortOrder);
            
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
    private void MoveCategoryDown(CategoryDisplayItem? item)
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
            _categoryService.UpdateCategorySortOrderAsync(item.Id, item.SortOrder);
            _categoryService.UpdateCategorySortOrderAsync(nextItem.Id, nextItem.SortOrder);
            
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
            var json = await _categoryService.ExportCategoriesAsync();
            var exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tai", "categories_export.json");
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
            var importPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tai", "categories_export.json");
            if (!File.Exists(importPath))
            {
                StatusMessage = "未找到导入文件";
                return;
            }
            
            var json = await File.ReadAllTextAsync(importPath);
            var count = await _categoryService.ImportCategoriesAsync(json);
            
            LoadCategories();
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
    
    private void LoadSettings()
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
        RetentionDays = _settingsService.RetentionDays;
        WebSocketPort = _settingsService.WebSocketPort;
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
            case "RetentionDays":
                _settingsService.RetentionDays = RetentionDays;
                break;
            case "SelectedThemeKey":
                _settingsService.CurrentTheme = SelectedThemeKey;
                break;
            case "SelectedLanguageKey":
                _settingsService.CurrentLanguage = SelectedLanguageKey;
                break;
        }
        
        _settingsService.Save();
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
    
    private void UpdateDatabaseSize()
    {
        try
        {
            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tai", "Data", "tai.db");
            if (File.Exists(dbPath))
            {
                var size = new FileInfo(dbPath).Length;
                if (size < 1024)
                    DatabaseSize = $"{size} B";
                else if (size < 1024 * 1024)
                    DatabaseSize = $"{size / 1024.0:F1} KB";
                else
                    DatabaseSize = $"{size / 1024.0 / 1024.0:F2} MB";
            }
            else
            {
                DatabaseSize = "未找到数据库";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "获取数据库大小失败");
            DatabaseSize = "获取失败";
        }
    }
    
    private void UpdateConnectedBrowsers()
    {
        ConnectedBrowsers = _monitorManager.GetConnectedBrowserCount();
    }
    
    partial void OnStartWithWindowsChanged(bool value)
    {
        Log.Information("OnStartWithWindowsChanged: {Value}", value);
        _settingsService.StartWithWindows = value;
        _settingsService.Save();
        Log.Information("设置已保存: StartWithWindows = {Value}", value);
    }
    
    partial void OnMinimizeToTrayChanged(bool value)
    {
        Log.Information("OnMinimizeToTrayChanged: {Value}", value);
        _settingsService.MinimizeToTray = value;
        _settingsService.Save();
        Log.Information("设置已保存: MinimizeToTray = {Value}", value);
    }
    
    partial void OnShowNotificationsChanged(bool value)
    {
        Log.Information("OnShowNotificationsChanged: {Value}", value);
        _settingsService.ShowNotifications = value;
        _settingsService.Save();
        Log.Information("设置已保存: ShowNotifications = {Value}", value);
    }
    
    partial void OnAutoStartMonitoringChanged(bool value)
    {
        Log.Information("OnAutoStartMonitoringChanged: {Value}", value);
        _settingsService.AutoStartMonitoring = value;
        _settingsService.Save();
        Log.Information("设置已保存: AutoStartMonitoring = {Value}", value);
    }
    
    partial void OnMonitoringIntervalChanged(int value)
    {
        Log.Information("OnMonitoringIntervalChanged: {Value}", value);
        _settingsService.MonitoringInterval = value;
        _settingsService.Save();
        Log.Information("设置已保存: MonitoringInterval = {Value}", value);
    }
    
    partial void OnIdleThresholdChanged(int value)
    {
        Log.Information("OnIdleThresholdChanged: {Value}", value);
        _settingsService.IdleThreshold = value;
        _settingsService.Save();
        Log.Information("设置已保存: IdleThreshold = {Value}", value);
    }
    
    partial void OnTrackKeyboardChanged(bool value)
    {
        Log.Information("OnTrackKeyboardChanged: {Value}", value);
        _settingsService.TrackKeyboard = value;
        _settingsService.Save();
        Log.Information("设置已保存: TrackKeyboard = {Value}", value);
        ApplyMonitorSettings();
    }
    
    partial void OnTrackMouseChanged(bool value)
    {
        Log.Information("OnTrackMouseChanged: {Value}", value);
        _settingsService.TrackMouse = value;
        _settingsService.Save();
        Log.Information("设置已保存: TrackMouse = {Value}", value);
        ApplyMonitorSettings();
    }
    
    partial void OnTrackWebBrowsingChanged(bool value)
    {
        Log.Information("OnTrackWebBrowsingChanged: {Value}", value);
        _settingsService.TrackWebBrowsing = value;
        _settingsService.Save();
        Log.Information("设置已保存: TrackWebBrowsing = {Value}", value);
        ApplyMonitorSettings();
    }
    
    partial void OnAnonymizeDataChanged(bool value)
    {
        Log.Information("OnAnonymizeDataChanged: {Value}", value);
        _settingsService.AnonymizeData = value;
        _settingsService.Save();
        Log.Information("设置已保存: AnonymizeData = {Value}", value);
    }
    
    partial void OnRetentionDaysChanged(int value)
    {
        Log.Information("OnRetentionDaysChanged: {Value}", value);
        _settingsService.RetentionDays = value;
        _settingsService.Save();
        Log.Information("设置已保存: RetentionDays = {Value}", value);
    }
    
    partial void OnWebSocketPortChanged(string value)
    {
        Log.Information("OnWebSocketPortChanged: {Value}", value);
        _settingsService.WebSocketPort = value;
        _settingsService.Save();
        Log.Information("设置已保存: WebSocketPort = {Value}", value);
    }
    
    partial void OnSelectedThemeKeyChanged(string value)
    {
        Log.Information("OnSelectedThemeKeyChanged: {Value}", value);
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settingsService.CurrentTheme = value;
            _settingsService.Save();
            Log.Information("设置已保存: CurrentTheme = {Value}", value);
            ApplyTheme(value);
        }
    }
    
    partial void OnSelectedLanguageKeyChanged(string value)
    {
        Log.Information("OnSelectedLanguageKeyChanged: {Value}", value);
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settingsService.CurrentLanguage = value;
            _settingsService.Save();
            Log.Information("设置已保存: CurrentLanguage = {Value}", value);
        }
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
            Log.Information("主题已切换: {Theme}", themeKey);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "切换主题失败");
        }
    }
    
    [RelayCommand]
    private void SaveSettings()
    {
        _settingsService.Save();
        Log.Information("设置已保存");
    }
    
    [RelayCommand]
    private void ResetToDefaults()
    {
        _settingsService.ResetToDefaults();
        LoadSettings();
        Log.Information("设置已重置为默认值");
    }
    
    [RelayCommand]
    private void ClearData()
    {
        try
        {
            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tai", "data.db");
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
                Log.Information("数据库已清除");
                DatabaseSize = "0 MB";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "清除数据失败");
        }
    }
    
    [RelayCommand]
    private void ExportSettings()
    {
        try
        {
            var exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tai", "settings_export.json");
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            _settingsService.Save();
            File.Copy(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tai", "settings.json"),
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
            var importPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tai", "settings_export.json");
            if (File.Exists(importPath))
            {
                var destPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tai", "settings.json");
                File.Copy(importPath, destPath, true);
                _settingsService.Load();
                LoadSettings();
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
