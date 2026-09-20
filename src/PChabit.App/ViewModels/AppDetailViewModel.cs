using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

/// <summary>详情卡片小时点 UI 模型。</summary>
public class AppDetailHourItem
{
    public string Label { get; init; } = "";
    public double Minutes { get; init; }
    public string MinutesText { get; init; } = "—";
    public int Intensity { get; init; }
}

/// <summary>详情卡片日趋势点 UI 模型。</summary>
public class AppDetailDayItem
{
    public string DateLabel { get; init; } = "";
    public double Minutes { get; init; }
    public string MinutesText { get; init; } = "—";
    /// <summary>柱高像素（0–48）。</summary>
    public double BarHeight { get; init; }
}

/// <summary>应用详情卡片 ViewModel（与 AppDetailDialog.xaml 绑定）。</summary>
public partial class AppDetailViewModel : ObservableObject
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly IAppIconService _iconService;

    private AnalyticsPeriod _period = AnalyticsPeriod.FromKind(AnalyticsPeriodKind.Today);
    private string _processName = "";
    private string _appName = "";
    private string _category = "未分类";
    private string _categoryColorHex = "#6B7280";
    private double _totalMinutesAllApps;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _appTitle = "";

    [ObservableProperty]
    private string _processText = "";

    [ObservableProperty]
    private string _periodText = "";

    [ObservableProperty]
    private string _categoryText = "";

    [ObservableProperty]
    private string _totalText = "—";

    [ObservableProperty]
    private string _sessionsText = "—";

    [ObservableProperty]
    private string _avgText = "—";

    [ObservableProperty]
    private string _shareText = "—";

    [ObservableProperty]
    private string _focusText = "—";

    [ObservableProperty]
    private string _deltaText = "—";

    [ObservableProperty]
    private string _longestText = "—";

    [ObservableProperty]
    private bool _hasInput;

    [ObservableProperty]
    private string _inputSummary = "";

    [ObservableProperty]
    private bool _hasHardware;

    [ObservableProperty]
    private string _hardwareSummary = "";

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private string _emptyMessage = "";

    public ObservableCollection<AppDetailHourItem> Hourly { get; } = new();
    public ObservableCollection<AppDetailDayItem> Days { get; } = new();
    public ObservableCollection<string> WindowTitles { get; } = new();

    public AppDetailViewModel(
        IDbContextFactory<PChabitDbContext> dbFactory,
        IAppIconService iconService,
        ICategoryService? categoryService = null)
    {
        _dbFactory = dbFactory;
        _iconService = iconService;
        _ = categoryService;
    }

    /// <summary>排行调用：配置参数后再 LoadReportAsync。</summary>
    public void Configure(
        AnalyticsPeriod period,
        string processName,
        string appName,
        string category,
        string categoryColorHex,
        double totalMinutesAllApps)
    {
        _period = period;
        _processName = processName ?? "";
        _appName = string.IsNullOrWhiteSpace(appName) ? processName : appName;
        _category = string.IsNullOrWhiteSpace(category) ? "未分类" : category;
        _categoryColorHex = string.IsNullOrWhiteSpace(categoryColorHex) ? "#6B7280" : categoryColorHex;
        _totalMinutesAllApps = totalMinutesAllApps;

        AppTitle = _appName;
        ProcessText = _processName;
        PeriodText = $"{period.Label}　{period.Start:yyyy-MM-dd} ~ {period.EndExclusive.AddDays(-1):yyyy-MM-dd}";
        CategoryText = _category;
        _ = _categoryColorHex;
    }

    public async Task LoadReportAsync()
    {
        if (string.IsNullOrWhiteSpace(_processName))
        {
            HasData = false;
            EmptyMessage = "未指定应用";
            return;
        }

        IsLoading = true;
        HasData = false;
        EmptyMessage = "";
        try
        {
            var report = await Task.Run(() => AppDetailEngine.BuildAsync(
                _dbFactory, _period, _processName, _appName, _category, _category));

            var share = _totalMinutesAllApps > 0 && report.TotalMinutes > 0
                ? report.TotalMinutes / _totalMinutesAllApps * 100
                : 0;
            report = AppDetailEngine.WithShare(report, share);

            TotalText = report.TotalText;
            SessionsText = $"{report.Sessions} 次";
            AvgText = report.AvgText;
            ShareText = report.ShareText;
            FocusText = report.KpiFocusText;
            DeltaText = report.DeltaText;
            LongestText = report.LongestText;

            HasInput = report.HasInput;
            InputSummary = report.HasInput
                ? $"按键 {report.KeyPresses:N0} · 密度约 {report.KeysPerHour:F0}/小时"
                : "";

            HasHardware = report.HasHardware;
            if (report.HasHardware)
            {
                var parts = new List<string>();
                if (report.CpuLoadAvg is { } c) parts.Add($"CPU均 {c:F0}%");
                if (report.GpuLoadAvg is { } g) parts.Add($"GPU均 {g:F0}%");
                if (report.GpuTempMax is { } t) parts.Add($"峰温 {t:F0}°C");
                HardwareSummary = string.Join(" · ", parts);
            }
            else
            {
                HardwareSummary = "";
            }

            var maxHour = report.Hourly.Count > 0 ? report.Hourly.Max(h => h.Minutes) : 0;
            if (maxHour <= 0) maxHour = 1;
            Hourly.Clear();
            foreach (var h in report.Hourly.Where(x => x.Minutes > 0.05))
            {
                var intensity = h.Minutes <= 0 ? 0 : (int)Math.Ceiling(h.Minutes / maxHour * 4.0);
                Hourly.Add(new AppDetailHourItem
                {
                    Label = h.Label,
                    Minutes = h.Minutes,
                    MinutesText = AnalyticsEngine.FormatHours(h.Minutes),
                    Intensity = Math.Clamp(intensity, 0, 4)
                });
            }

            var maxDay = report.DailyTrend.Count > 0 ? report.DailyTrend.Max(d => d.Minutes) : 0;
            if (maxDay <= 0) maxDay = 1;
            Days.Clear();
            foreach (var d in report.DailyTrend)
            {
                var bar = d.Minutes <= 0 ? 0 : Math.Clamp(d.Minutes / maxDay * 48.0, 2, 48);
                Days.Add(new AppDetailDayItem
                {
                    DateLabel = d.DateLabel,
                    Minutes = d.Minutes,
                    MinutesText = d.MinutesText,
                    BarHeight = bar
                });
            }

            WindowTitles.Clear();
            foreach (var t in report.TopWindowTitles) WindowTitles.Add(t);

            HasData = report.HasData;
            if (!report.HasData)
                EmptyMessage = "所选周期内该应用没有已结束会话。";

            try
            {
                var icon = await _iconService.GetAppIconAsync(_processName);
                if (icon != null) Icon = icon;
            }
            catch (Exception iconEx)
            {
                Log.Debug(iconEx, "应用详情图标加载失败");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "应用详情加载失败 {Process}", _processName);
            HasData = false;
            EmptyMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CopySummary()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {AppTitle} · {PeriodText}");
            sb.AppendLine($"进程：`{ProcessText}`　分类：{CategoryText}");
            sb.AppendLine();
            sb.AppendLine($"- 总时长：{TotalText}（{DeltaText}）");
            sb.AppendLine($"- 会话：{SessionsText}　均长：{AvgText}");
            sb.AppendLine($"- 占比：{ShareText}　专注：{FocusText}");
            sb.AppendLine($"- 最长会话：{LongestText}");
            if (HasInput) sb.AppendLine($"- 输入：{InputSummary}");
            if (HasHardware) sb.AppendLine($"- 硬件：{HardwareSummary}");
            if (WindowTitles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 窗口标题 Top");
                foreach (var t in WindowTitles)
                    sb.AppendLine($"- {t}");
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(sb.ToString());
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "复制应用详情摘要失败");
        }
    }
}
