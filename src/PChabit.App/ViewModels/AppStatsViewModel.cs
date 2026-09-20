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
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;
using Windows.ApplicationModel.DataTransfer;

namespace PChabit.App.ViewModels;

public partial class AppStatsViewModel : DbSafeViewModel<AppStatsReport>
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly IAppIconService _iconService;
    private readonly IBackgroundAppSettings _backgroundAppSettings;

    [ObservableProperty]
    private AnalyticsPeriodKind _selectedRange = AnalyticsPeriodKind.Today;

    [ObservableProperty]
    private DateTime? _customStart;

    /// <summary>自定义周期结束时间（Exclusive）。</summary>
    [ObservableProperty]
    private DateTime? _customEnd;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _selectedCategoryFilter;

    // === KPI ===

    [ObservableProperty]
    private string _totalUsageTime = "0小时 0分钟";

    [ObservableProperty]
    private int _activeApps;

    [ObservableProperty]
    private string _topAppText = "无";

    [ObservableProperty]
    private string _focusText = "0 次";

    [ObservableProperty]
    private string _rangeLabel = "今天";

    [ObservableProperty]
    private string _resultHint = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private string _pieChartData = "[]";

    [ObservableProperty]
    private string _summaryMarkdown = string.Empty;

    [ObservableProperty]
    private bool _isCopying;

    /// <summary>最近一次加载的周期（供应用详情卡片）。</summary>
    public AnalyticsPeriod? LastPeriod { get; private set; }

    public double LastTotalMinutes { get; private set; }

    public ObservableCollection<AppStatItem> AppStats { get; } = new();
    public ObservableCollection<HourlyUsageItem> HourlyUsage { get; } = new();
    public ObservableCollection<string> CategoryOptions { get; } = new();

    /// <summary>周期下拉选项。</summary>
    public IReadOnlyList<PeriodOption> PeriodOptions { get; } = new[]
    {
        new PeriodOption(AnalyticsPeriodKind.Today, "今天"),
        new PeriodOption(AnalyticsPeriodKind.Yesterday, "昨天"),
        new PeriodOption(AnalyticsPeriodKind.ThisWeek, "本周"),
        new PeriodOption(AnalyticsPeriodKind.LastWeek, "上周"),
        new PeriodOption(AnalyticsPeriodKind.Last7Days, "近 7 天"),
        new PeriodOption(AnalyticsPeriodKind.Last30Days, "近 30 天"),
        new PeriodOption(AnalyticsPeriodKind.ThisMonth, "本月"),
        new PeriodOption(AnalyticsPeriodKind.Custom, "自定义")
    };

    public AppStatsViewModel(IDbContextFactory<PChabitDbContext> dbFactory, IAppIconService iconService, IBackgroundAppSettings backgroundAppSettings)
    {
        _dbFactory = dbFactory;
        _iconService = iconService;
        _backgroundAppSettings = backgroundAppSettings;
        Title = "应用统计";
    }

    // === DbSafeViewModel 抽象方法（两阶段：Phase1 后台计算 / Phase2 UI 回填） ===

    protected override async Task<AppStatsReport> LoadStatsOnBackgroundAsync()
    {
        var today = DateTime.Today;
        var period = SelectedRange switch
        {
            AnalyticsPeriodKind.Custom => AnalyticsPeriod.FromCustom(
                CustomStart ?? today,
                CustomEnd ?? today.AddDays(1)),
            _ => AnalyticsPeriod.FromKind(SelectedRange, today)
        };

        LastPeriod = period;
        return await AppStatsEngine.BuildAsync(_dbFactory, period, SelectedCategoryFilter, SearchText);
    }

    protected override async Task ApplyStatsOnUIAsync(AppStatsReport report)
    {
        LastPeriod = report.Period;
        LastTotalMinutes = report.TotalMinutes;
        var backgroundApps = _backgroundAppSettings.GetBackgroundApps();

        TotalUsageTime = AnalyticsEngine.FormatHours(report.TotalMinutes);
        ActiveApps = report.ActiveApps;
        TopAppText = report.TopAppMinutes > 0
            ? $"{report.TopAppName}（{AnalyticsEngine.FormatHours(report.TopAppMinutes)}）"
            : "无";
        FocusText = $"{report.FocusSessions} 次";
        RangeLabel = report.Period.Label;
        ResultHint = report.Rows.Count > 0 ? $"共 {report.Rows.Count} 个应用" : string.Empty;
        HasData = report.TotalMinutes > 0;

        AppStats.Clear();
        foreach (var row in report.Rows)
        {
            var item = new AppStatItem
            {
                AppName = row.AppName,
                ProcessName = row.ProcessName,
                Duration = AnalyticsEngine.FormatHours(row.DurationMinutes),
                DurationMinutes = row.DurationMinutes,
                Sessions = row.Sessions,
                AvgDuration = row.Sessions > 0 ? AnalyticsEngine.FormatHours(row.AvgDurationMinutes) : "—",
                Percentage = row.Percentage,
                Category = row.Category,
                CategoryIcon = "📁",
                CategoryColorHex = row.CategoryColorHex,
                CategoryColor = GetCategoryBrush(row.CategoryColorHex),
                DeltaText = row.DeltaText,
                DeltaDirection = row.DeltaDirection,
                FocusMinutes = row.FocusMinutes,
                IsBackgroundMode = backgroundApps.Contains(row.ProcessName)
            };

            AppStats.Add(item);
            _ = LoadIconAsync(item);
        }

        GeneratePieChartData(report);

        HourlyUsage.Clear();
        foreach (var h in report.Hourly)
        {
            HourlyUsage.Add(new HourlyUsageItem
            {
                Hour = h.Label,
                Minutes = (int)h.Minutes,
                Activity = Math.Min(100, (int)(h.Minutes / 60 * 100))
            });
        }

        // 分类筛选选项：全部分类 + 实际出现的分类
        CategoryOptions.Clear();
        CategoryOptions.Add("全部分类");
        foreach (var share in report.CategoryShares)
        {
            if (!CategoryOptions.Contains(share.Category))
                CategoryOptions.Add(share.Category);
        }

        SummaryMarkdown = BuildSummaryMarkdown(report);
    }

    private static string BuildSummaryMarkdown(AppStatsReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## 应用统计摘要（{report.Period.Label}）");
        sb.AppendLine();
        sb.AppendLine($"- 总使用时长：{AnalyticsEngine.FormatHours(report.TotalMinutes)}");
        sb.AppendLine($"- 活跃应用：{report.ActiveApps} 个");
        if (report.TopAppMinutes > 0)
            sb.AppendLine($"- Top 应用：{report.TopAppName}（{AnalyticsEngine.FormatHours(report.TopAppMinutes)}）");
        sb.AppendLine($"- 专注段（≥25 分钟且属生产力分类）：{report.FocusSessions} 次");
        if (report.Rows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### 排行 Top 5");
            foreach (var row in report.Rows.Take(5))
            {
                sb.AppendLine($"- {row.AppName}：{AnalyticsEngine.FormatHours(row.DurationMinutes)}（{row.Percentage:F0}%，{row.DeltaText}）");
            }
        }
        return sb.ToString();
    }

    [RelayCommand]
    private async Task CopySummaryAsync()
    {
        if (string.IsNullOrEmpty(SummaryMarkdown) || IsCopying) return;
        IsCopying = true;
        try
        {
            var package = new DataPackage();
            package.SetText(SummaryMarkdown);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "复制应用统计摘要失败");
        }
        finally
        {
            IsCopying = false;
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
            if (icon != null) item.Icon = icon;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载应用图标失败: {ProcessName}", item.ProcessName);
        }
    }

    private static SolidColorBrush GetCategoryBrush(string hexColor)
    {
        if (!string.IsNullOrEmpty(hexColor) && hexColor.StartsWith("#") && hexColor.Length == 7)
        {
            var r = System.Convert.ToByte(hexColor.Substring(1, 2), 16);
            var g = System.Convert.ToByte(hexColor.Substring(3, 2), 16);
            var b = System.Convert.ToByte(hexColor.Substring(5, 2), 16);
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, r, g, b));
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    partial void OnSelectedRangeChanged(AnalyticsPeriodKind value)
    {
        _ = LoadDataAsync();
    }

    partial void OnCustomStartChanged(DateTime? value)
    {
        if (SelectedRange == AnalyticsPeriodKind.Custom) _ = LoadDataAsync();
    }

    partial void OnCustomEndChanged(DateTime? value)
    {
        if (SelectedRange == AnalyticsPeriodKind.Custom) _ = LoadDataAsync();
    }

    partial void OnSelectedCategoryFilterChanged(string? value)
    {
        _ = LoadDataAsync();
    }

    private string _debouncedSearch = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        // 简单防抖：与上次搜索内容一致才触发
        var q = value?.Trim() ?? string.Empty;
        var prev = _debouncedSearch;
        _debouncedSearch = q;
        if (q != prev) _ = LoadDataAsync();
    }

    private void GeneratePieChartData(AppStatsReport report)
    {
        if (report.PieSlices.Count == 0) { PieChartData = "[]"; return; }

        var topApps = report.PieSlices.Select(s => new
        {
            name = s.Name,
            value = Math.Round(s.Minutes, 1),
            category = "",
            color = s.ColorHex
        }).ToList();

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        PieChartData = JsonSerializer.Serialize(topApps, options);
    }
}

public class PeriodOption
{
    public PeriodOption(AnalyticsPeriodKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    public AnalyticsPeriodKind Kind { get; }
    public string Label { get; }
}

public class AppStatItem : ObservableObject
{
    public string AppName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public double DurationMinutes { get; init; }
    public int Sessions { get; init; }
    public string AvgDuration { get; init; } = "—";
    public double Percentage { get; init; }
    public double FocusMinutes { get; init; }
    public string DeltaText { get; init; } = "—";
    public string DeltaDirection { get; init; } = "none";
    public string CategoryColorHex { get; init; } = "#6B7280";
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
