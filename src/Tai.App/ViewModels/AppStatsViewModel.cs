using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using Tai.App.Services;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;
using Tai.Infrastructure.Services;

namespace Tai.App.ViewModels;

public partial class AppStatsViewModel : ViewModelBase
{
    private readonly TaiDbContext _dbContext;
    private readonly IAppIconService _iconService;
    private readonly IBackgroundAppSettings _backgroundAppSettings;
    private readonly ICategoryService _categoryService;
    
    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;
    
    [ObservableProperty]
    private string _totalUsageTime = "0小时 0分钟";
    
    [ObservableProperty]
    private int _totalApps;
    
    [ObservableProperty]
    private string _mostUsedApp = "无";
    
    public ObservableCollection<AppStatItem> AppStats { get; } = new();
    public ObservableCollection<HourlyUsageItem> HourlyUsage { get; } = new();
    
    public AppStatsViewModel(TaiDbContext dbContext, IAppIconService iconService, IBackgroundAppSettings backgroundAppSettings, ICategoryService categoryService)
    {
        _dbContext = dbContext;
        _iconService = iconService;
        _backgroundAppSettings = backgroundAppSettings;
        _categoryService = categoryService;
        Title = "应用统计";
    }
    
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        
        try
        {
            try { _dbContext.ChangeTracker.Clear(); } catch { }
            
            List<Core.Entities.ProgramCategory>? categories = null;
            try
            {
                categories = await _categoryService.GetAllCategoriesAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载分类失败");
            }
            
            var categoryDictionary = new Dictionary<string, Core.Entities.ProgramCategory>(StringComparer.OrdinalIgnoreCase);
            if (categories != null)
            {
                foreach (var cat in categories)
                {
                    if (cat.ProgramMappings != null)
                    {
                        foreach (var mapping in cat.ProgramMappings)
                        {
                            if (!string.IsNullOrEmpty(mapping.ProcessName))
                            {
                                var processName = mapping.ProcessName;
                                categoryDictionary[processName] = cat;
                                
                                if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    var nameWithoutExe = processName.Substring(0, processName.Length - 4);
                                    categoryDictionary[nameWithoutExe] = cat;
                                }
                                else
                                {
                                    categoryDictionary[$"{processName}.exe"] = cat;
                                }
                            }
                        }
                    }
                }
            }
            
            Log.Information("AppStatsViewModel: 加载了 {Count} 个分类映射", categoryDictionary.Count);
            
            var selectedDate = SelectedDate.Date;
            var nextDay = selectedDate.AddDays(1);
            
            List<Core.Entities.AppSession> sessions;
            
            try
            {
                sessions = await _dbContext.AppSessions
                    .Where(s => s.StartTime >= selectedDate && s.StartTime < nextDay)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 AppSessions 失败");
                sessions = new List<Core.Entities.AppSession>();
            }
            
            var totalMinutes = sessions
                .Where(s => s.EndTime.HasValue)
                .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
            
            TotalUsageTime = $"{(int)(totalMinutes / 60)}小时 {(int)(totalMinutes % 60)}分钟";
            TotalApps = sessions.Select(s => s.ProcessName).Distinct().Count();
            
            AppStats.Clear();
            var appGroups = sessions
                .GroupBy(s => s.ProcessName)
                .Select(g =>
                {
                    var processName = g.Key ?? "";
                    string categoryName;
                    string? categoryColor = null;
                    string? categoryIcon = null;
                    
                    if (categoryDictionary.TryGetValue(processName, out var category))
                    {
                        categoryName = category.Name;
                        categoryColor = category.Color;
                        categoryIcon = category.Icon;
                        Log.Information("AppStatsViewModel: 进程 {ProcessName} 映射到分类 {Category}", processName, categoryName);
                    }
                    else
                    {
                        categoryName = g.First().Category ?? "其他";
                        Log.Information("AppStatsViewModel: 进程 {ProcessName} 未找到映射，使用默认分类 {Category}", processName, categoryName);
                    }
                    
                    return new
                    {
                        ProcessName = g.Key,
                        AppName = g.First().AppName ?? g.Key ?? "",
                        Duration = g.Where(s => s.EndTime.HasValue)
                                    .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes),
                        Sessions = g.Count(),
                        Category = categoryName,
                        CategoryColor = categoryColor,
                        CategoryIcon = categoryIcon
                    };
                })
                .OrderByDescending(x => x.Duration)
                .ToList();
            
            MostUsedApp = appGroups.FirstOrDefault()?.AppName ?? "无";
            
            var backgroundApps = _backgroundAppSettings.GetBackgroundApps();
            
            foreach (var app in appGroups)
            {
                var item = new AppStatItem
                {
                    AppName = app.AppName,
                    ProcessName = app.ProcessName ?? "",
                    Duration = $"{(int)(app.Duration / 60)}h {(int)(app.Duration % 60)}m",
                    DurationMinutes = app.Duration,
                    Sessions = app.Sessions,
                    Category = app.Category,
                    CategoryIcon = app.CategoryIcon ?? "📁",
                    CategoryColor = GetCategoryBrush(app.Category, app.CategoryColor),
                    Percentage = totalMinutes > 0 ? (int)(app.Duration / totalMinutes * 100) : 0,
                    IsBackgroundMode = backgroundApps.Contains(app.ProcessName ?? "")
                };
                
                AppStats.Add(item);
                
                _ = LoadIconAsync(item);
            }
            
            HourlyUsage.Clear();
            for (int i = 0; i < 24; i++)
            {
                var hourStart = selectedDate.AddHours(i);
                var hourEnd = hourStart.AddHours(1);
                
                var hourMinutes = sessions
                    .Where(s => s.StartTime < hourEnd && (s.EndTime == null || s.EndTime > hourStart))
                    .Sum(s =>
                    {
                        var start = s.StartTime < hourStart ? hourStart : s.StartTime;
                        var end = s.EndTime == null || s.EndTime > hourEnd ? hourEnd : s.EndTime.Value;
                        return (end - start).TotalMinutes;
                    });
                
                HourlyUsage.Add(new HourlyUsageItem
                {
                    Hour = $"{i:D2}:00",
                    Minutes = (int)hourMinutes,
                    Activity = Math.Min(100, (int)(hourMinutes / 60 * 100))
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载应用统计数据失败");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private void ToggleBackgroundMode(AppStatItem? item)
    {
        if (item == null) return;
        
        item.IsBackgroundMode = !item.IsBackgroundMode;
        _backgroundAppSettings.SetBackgroundApp(item.ProcessName, item.IsBackgroundMode);
    }
    
    private async Task LoadIconAsync(AppStatItem item)
    {
        try
        {
            var icon = await _iconService.GetAppIconAsync(item.ProcessName, 24);
            if (icon != null)
            {
                item.Icon = icon;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载应用图标失败: {ProcessName}", item.ProcessName);
        }
    }
    
    private static SolidColorBrush GetCategoryBrush(string? category, string? customColor = null)
    {
        string hexColor;
        
        if (!string.IsNullOrEmpty(customColor))
        {
            hexColor = customColor;
        }
        else
        {
            hexColor = category switch
            {
                "开发" => "#512BD4",
                "浏览" => "#0078D4",
                "沟通" => "#107C10",
                "娱乐" => "#FF8C00",
                "办公" => "#00B7C3",
                _ => "#6B7280"
            };
        }
        
        var color = Microsoft.UI.Colors.Transparent;
        if (hexColor.StartsWith("#") && hexColor.Length == 7)
        {
            var r = System.Convert.ToByte(hexColor.Substring(1, 2), 16);
            var g = System.Convert.ToByte(hexColor.Substring(3, 2), 16);
            var b = System.Convert.ToByte(hexColor.Substring(5, 2), 16);
            color = Microsoft.UI.ColorHelper.FromArgb(255, r, g, b);
        }
        return new SolidColorBrush(color);
    }
    
    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadDataAsync();
    }
}

public class AppStatItem : ObservableObject
{
    public string AppName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public double DurationMinutes { get; init; }
    public int Sessions { get; init; }
    public string Category { get; init; } = string.Empty;
    public string CategoryIcon { get; init; } = "📁";
    public SolidColorBrush CategoryColor { get; init; } = new SolidColorBrush();
    public int Percentage { get; init; }
    
    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }
    
    private bool _isBackgroundMode;
    public bool IsBackgroundMode
    {
        get => _isBackgroundMode;
        set => SetProperty(ref _isBackgroundMode, value);
    }
}

public class HourlyUsageItem
{
    public string Hour { get; init; } = string.Empty;
    public int Minutes { get; init; }
    public int Activity { get; init; }
}
