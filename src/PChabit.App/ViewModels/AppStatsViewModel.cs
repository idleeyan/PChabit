using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public partial class AppStatsViewModel : DbSafeViewModel<AppStatsViewModel.AppStatsData>
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly IAppIconService _iconService;
    private readonly IBackgroundAppSettings _backgroundAppSettings;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private string _totalUsageTime = "0小时 0分钟";

    [ObservableProperty]
    private int _totalApps;

    [ObservableProperty]
    private string _mostUsedApp = "无";

    [ObservableProperty]
    private string _pieChartData = "[]";

    public ObservableCollection<AppStatItem> AppStats { get; } = new();
    public ObservableCollection<HourlyUsageItem> HourlyUsage { get; } = new();

    public AppStatsViewModel(IDbContextFactory<PChabitDbContext> dbFactory, IAppIconService iconService, IBackgroundAppSettings backgroundAppSettings)
    {
        _dbFactory = dbFactory;
        _iconService = iconService;
        _backgroundAppSettings = backgroundAppSettings;
        Title = "应用统计";
    }

    // === Phase 1 中间数据 ===

    public sealed class AppStatsData
    {
        public string TotalUsageTime = "0小时 0分钟";
        public int TotalApps;
        public string MostUsedApp = "无";
        public List<AppGroupInfo> AppGroups = new();
        public List<HourlyUsageItem> HourlyUsage = new();
        public double TotalMinutes;
    }

    public sealed class AppGroupInfo
    {
        public string ProcessName { get; set; } = "";
        public string AppName { get; set; } = "";
        public double Duration { get; set; }
        public int Sessions { get; set; }
        public string Category { get; set; } = "";
        public string? CategoryColor { get; set; }
        public string? CategoryIcon { get; set; }
    }

    // === DbSafeViewModel 抽象方法 ===

    protected override async Task<AppStatsData> LoadStatsOnBackgroundAsync()
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();

        List<Core.Entities.ProgramCategory>? categories = null;
        try
        {
            categories = await dbContext.ProgramCategories
                .Include(c => c.ProgramMappings)
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();
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
                                categoryDictionary[processName.Substring(0, processName.Length - 4)] = cat;
                            else
                                categoryDictionary[$"{processName}.exe"] = cat;
                        }
                    }
                }
            }
        }

        var selectedDate = SelectedDate.Date;
        var nextDay = selectedDate.AddDays(1);

        List<Core.Entities.AppSession> sessions;
        try
        {
            sessions = await dbContext.AppSessions
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
                }
                else
                {
                    categoryName = g.First().Category ?? "其他";
                }

                return new AppGroupInfo
                {
                    ProcessName = g.Key ?? "",
                    AppName = g.First().AppName ?? g.Key ?? "",
                    Duration = g.Where(s => s.EndTime.HasValue).Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes),
                    Sessions = g.Count(),
                    Category = categoryName,
                    CategoryColor = categoryColor,
                    CategoryIcon = categoryIcon
                };
            })
            .OrderByDescending(x => x.Duration)
            .ToList();

        var hourlyItems = new List<HourlyUsageItem>();
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
            hourlyItems.Add(new HourlyUsageItem { Hour = $"{i:D2}:00", Minutes = (int)hourMinutes, Activity = Math.Min(100, (int)(hourMinutes / 60 * 100)) });
        }

        return new AppStatsData
        {
            TotalUsageTime = $"{(int)(totalMinutes / 60)}小时 {(int)(totalMinutes % 60)}分钟",
            TotalApps = sessions.Select(s => s.ProcessName).Distinct().Count(),
            MostUsedApp = appGroups.FirstOrDefault()?.AppName ?? "无",
            AppGroups = appGroups,
            HourlyUsage = hourlyItems,
            TotalMinutes = totalMinutes
        };
    }

    protected override async Task ApplyStatsOnUIAsync(AppStatsData s)
    {
        TotalUsageTime = s.TotalUsageTime;
        TotalApps = s.TotalApps;
        MostUsedApp = s.MostUsedApp;

        var backgroundApps = _backgroundAppSettings.GetBackgroundApps();

        AppStats.Clear();
        foreach (var app in s.AppGroups)
        {
            var hours = (int)(app.Duration / 60);
            var minutes = (int)(app.Duration % 60);
            var seconds = (int)((app.Duration - Math.Floor(app.Duration)) * 60);

            var item = new AppStatItem
            {
                AppName = app.AppName,
                ProcessName = app.ProcessName ?? "",
                Duration = hours > 0 ? $"{hours}h {minutes}m {seconds}s" : $"{minutes}m {seconds}s",
                DurationMinutes = app.Duration,
                Sessions = app.Sessions,
                Category = app.Category,
                CategoryIcon = app.CategoryIcon ?? "📁",
                CategoryColor = GetCategoryBrush(app.Category, app.CategoryColor),
                Percentage = s.TotalMinutes > 0 ? (int)(app.Duration / s.TotalMinutes * 100) : 0,
                IsBackgroundMode = backgroundApps.Contains(app.ProcessName ?? "")
            };

            AppStats.Add(item);
            _ = LoadIconAsync(item);
        }

        GeneratePieChartData(s.AppGroups, s.TotalMinutes);

        HourlyUsage.Clear();
        foreach (var item in s.HourlyUsage) HourlyUsage.Add(item);
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
            if (icon != null) item.Icon = icon;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载应用图标失败: {ProcessName}", item.ProcessName);
        }
    }

    private static SolidColorBrush GetCategoryBrush(string? category, string? customColor = null)
    {
        string hexColor;
        if (!string.IsNullOrEmpty(customColor)) hexColor = customColor;
        else hexColor = category switch { "开发" => "#512BD4", "浏览" => "#0078D4", "沟通" => "#107C10", "娱乐" => "#FF8C00", "办公" => "#00B7C3", _ => "#6B7280" };

        if (hexColor.StartsWith("#") && hexColor.Length == 7)
        {
            var r = System.Convert.ToByte(hexColor.Substring(1, 2), 16);
            var g = System.Convert.ToByte(hexColor.Substring(3, 2), 16);
            var b = System.Convert.ToByte(hexColor.Substring(5, 2), 16);
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, r, g, b));
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadDataAsync();
    }

    private void GeneratePieChartData(List<AppGroupInfo> appGroups, double totalMinutes)
    {
        if (totalMinutes <= 0 || appGroups.Count == 0) { PieChartData = "[]"; return; }

        var topApps = appGroups.Take(8).Select((app, _) => new {
            name = app.AppName,
            value = Math.Round(app.Duration, 1),
            category = app.Category,
            color = GetCategoryHexColor(app.Category, app.CategoryColor)
        }).ToList();

        if (appGroups.Count > 8)
        {
            double otherMinutes = appGroups.Skip(8).Sum(app => app.Duration);
            if (otherMinutes > 0)
                topApps.Add(new { name = "其他", value = Math.Round(otherMinutes, 1), category = "其他", color = "#8B5CF6" });
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        PieChartData = JsonSerializer.Serialize(topApps, options);
    }

    private static string GetCategoryHexColor(string? category, string? customColor = null)
    {
        if (!string.IsNullOrEmpty(customColor)) return customColor;
        return category switch { "开发" => "#512BD4", "浏览" => "#0078D4", "沟通" => "#107C10", "娱乐" => "#FF8C00", "办公" => "#00B7C3", _ => "#8B5CF6" };
    }
}

public class AppStatItem : ObservableObject
{
    public string AppName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public double DurationMinutes { get; init; }
    public int Sessions { get; init; }
    public int Percentage { get; init; }
    private string _category = string.Empty;
    public string Category { get => _category; set => SetProperty(ref _category, value); }
    private string _categoryIcon = "📁";
    public string CategoryIcon { get => _categoryIcon; set => SetProperty(ref _categoryIcon, value); }
    private SolidColorBrush _categoryColor = new();
    public SolidColorBrush CategoryColor { get => _categoryColor; set => SetProperty(ref _categoryColor, value); }
    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => SetProperty(ref _icon, value); }
    private bool _isBackgroundMode;
    public bool IsBackgroundMode { get => _isBackgroundMode; set => SetProperty(ref _isBackgroundMode, value); }
}

public class HourlyUsageItem { public string Hour { get; init; } = string.Empty; public int Minutes { get; init; } public int Activity { get; init; } }
