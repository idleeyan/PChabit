using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

/// <summary>
/// 分析页：周期选择 + KPI 环比 + 日历/小时热力 + Top 变化 + 硬件 + 应用流向。
/// 计算逻辑在 AnalyticsEngine，本类只负责绑定与交互。
/// </summary>
public partial class AnalyticsViewModel : ViewModelBase
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    private AnalyticsPeriodReport? _lastReport;

    [ObservableProperty]
    private string _selectedPeriodKey = "近 7 天";

    public string[] PeriodOptions { get; } =
        { "近 7 天", "前 7 天", "近 30 天", "近 3 天", "今天" };

    [ObservableProperty]
    private string _periodLabel = "近 7 天";

    [ObservableProperty]
    private string _periodRangeText = "";

    [ObservableProperty]
    private string _dataQualityText = "";

    [ObservableProperty]
    private bool _showLowDataBanner;

    [ObservableProperty]
    private bool _hasTopChanges;

    [ObservableProperty]
    private bool _hasExtraMetrics;

    [ObservableProperty]
    private string _extraSummaryText = "";

    [ObservableProperty]
    private bool _hasHardwareProfile;

    [ObservableProperty]
    private string _hardwareSummaryText = "";

    [ObservableProperty]
    private string _dataQualityDetail = "";

    [ObservableProperty]
    private string _webSummaryText = "";

    [ObservableProperty]
    private bool _hasWebData;

    /// <summary>硬件页 WebView2 图表数据 JSON。</summary>
    [ObservableProperty]
    private string _hardwareChartJson = "{}";

    /// <summary>构成页双饼图 WebView2 数据 JSON（app/web 两组，value 单位分钟）。</summary>
    /// <summary>页面展示的 KPI（仅保留常用 4 项：活跃时长/专注段/生产力占比/网页时长）。</summary>
    public ObservableCollection<KpiCard> ShownKpis { get; } = new();

    public ObservableCollection<KpiCard> Kpis { get; } = new();
    public ObservableCollection<DayHeatItem> CalendarDays { get; } = new();
    public ObservableCollection<HourHeatItem> HourHeat { get; } = new();
    public ObservableCollection<HourHeatItem> HourWeb { get; } = new();
    public ObservableCollection<HourHeatItem> HourNet { get; } = new();
    public ObservableCollection<TopChangeItem> TopChanges { get; } = new();
    public ObservableCollection<AppHardwareLoad> HardwareAppLoads { get; } = new();
    public ObservableCollection<HardwareHourPoint> HardwareHours { get; } = new();
    public AnalyticsExtraMetrics? ExtraMetrics { get; private set; }
    public HardwareAnalyticsReport? HardwareReport { get; private set; }

    public AnalyticsViewModel(
        IDbContextFactory<PChabitDbContext> dbContextFactory) : base()
    {
        _dbContextFactory = dbContextFactory;
        Title = "分析";
    }

    partial void OnSelectedPeriodKeyChanged(string value)
    {
        _ = LoadDataAsync();
    }

    /// <summary>
    /// 页内导航请求（参数为 ActionKey：overview / rhythm / flow 等）。
    /// 由 AnalyticsPage 订阅并定位到对应区块锚点；跨页调用方可用 AnalyticsNavArgs 走 NavigationService。
    /// </summary>
    public event Action<string>? PivotNavigationRequested;

    /// <summary>
    /// 网页浏览历史已转移到「网页访问」页，这里请求整页导航并落到历史页签。
    /// </summary>
    [RelayCommand]
    private void OpenHistoryReport()
    {
        Log.Information("请求导航到网页访问-网页浏览历史页签");
        PivotNavigationRequested?.Invoke("history");
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var kind = SelectedPeriodKey switch
            {
                "前 7 天" => AnalyticsPeriodKind.LastWeek,
                "近 30 天" => AnalyticsPeriodKind.Last30Days,
                "近 3 天" => AnalyticsPeriodKind.Custom,
                "今天" => AnalyticsPeriodKind.Today,
                _ => AnalyticsPeriodKind.Last7Days
            };

            AnalyticsPeriod period;
            if (kind == AnalyticsPeriodKind.Custom && SelectedPeriodKey == "近 3 天")
                period = AnalyticsPeriod.FromCustom(DateTime.Today.AddDays(-2), DateTime.Today.AddDays(1), "近 3 天");
            else
                period = AnalyticsPeriod.FromKind(kind);
            var report = await AnalyticsEngine.BuildAsync(_dbContextFactory, period);
            _lastReport = report;

            await RunOnUIThreadAsync(() =>
            {
                PeriodLabel = report.Period.Label;
                PeriodRangeText = $"{report.Period.Start:yyyy-MM-dd} ~ {report.Period.EndExclusive.AddDays(-1):yyyy-MM-dd}" +
                                  (report.Previous != null
                                      ? $"　对比 {report.Previous.Start:MM-dd} ~ {report.Previous.EndExclusive.AddDays(-1):MM-dd}"
                                      : "");
                DataQualityText = report.HasEnoughData
                    ? $"有数据 {report.DayCountWithData} 天"
                    : $"数据较少（{report.DayCountWithData} 天）";
                DataQualityDetail = AnalysisReportBuilder.BuildQuality(report).Summary;
                ShowLowDataBanner = !report.HasEnoughData;

                Kpis.Clear();
                foreach (var k in report.Kpis) Kpis.Add(k);

                // 展示层裁剪：仅保留常用 4 项（活跃时长/专注段/生产力占比/网页时长）
                var keepKpiIds = new[] { "active", "focus", "prod", "web" };
                ShownKpis.Clear();
                foreach (var k in report.Kpis.Where(x => keepKpiIds.Contains(x.Id)))
                    ShownKpis.Add(k);

                CalendarDays.Clear();
                foreach (var d in report.CalendarDays) CalendarDays.Add(d);

                HourHeat.Clear();
                foreach (var h in report.HourHeat) HourHeat.Add(h);

                HourWeb.Clear();
                HourNet.Clear();
                var webMax = report.HourWebMinutes.Count > 0 ? report.HourWebMinutes.Max() : 0;
                var netMax = report.HourNetDownMb.Count > 0 ? report.HourNetDownMb.Max() : 0;
                for (var hour = 0; hour < 24; hour++)
                {
                    var wm = hour < report.HourWebMinutes.Count ? report.HourWebMinutes[hour] : 0;
                    var nm = hour < report.HourNetDownMb.Count ? report.HourNetDownMb[hour] : 0;
                    HourWeb.Add(new HourHeatItem(hour, $"{hour:00}", wm,
                        webMax > 0 ? Math.Clamp((int)Math.Ceiling(wm / webMax * 4.0), 0, 4) : 0));
                    HourNet.Add(new HourHeatItem(hour, $"{hour:00}", nm,
                        netMax > 0 ? Math.Clamp((int)Math.Ceiling(nm / netMax * 4.0), 0, 4) : 0));
                }

                TopChanges.Clear();
                foreach (var t in report.TopChanges) TopChanges.Add(t);
                HasTopChanges = TopChanges.Count > 0;

                ExtraMetrics = report.Extra;
                ExtraSummaryText = BuildExtraSummary(report.Extra);
                HasExtraMetrics = !string.IsNullOrEmpty(ExtraSummaryText);

                HardwareReport = report.Hardware;
                HardwareAppLoads.Clear();
                HardwareHours.Clear();
                if (report.Hardware is { HasData: true } hw)
                {
                    HardwareSummaryText = hw.SummaryText;
                    foreach (var a in hw.AppLoads) HardwareAppLoads.Add(a);
                    foreach (var h in hw.Hours.Where(x => x.SampleCount > 0)) HardwareHours.Add(h);
                    HasHardwareProfile = HardwareAppLoads.Count > 0 || HardwareHours.Count > 0;
                }
                else
                {
                    HardwareSummaryText = "";
                    HasHardwareProfile = false;
                }

                if (report.Extra is { HasWebData: true } exw)
                {
                    WebSummaryText =
                        $"网页 {Infrastructure.Analysis.AnalyticsEngine.FormatHours(exw.WebMinutes)} · {exw.WebPages:N0} 页 · 占比 {exw.WebSharePct:F0}%　可打开「网页浏览历史」查看域名与访问趋势";
                    HasWebData = true;
                }
                else
                {
                    WebSummaryText = "";
                    HasWebData = false;
                }

                HardwareChartJson = BuildHardwareChartJson(report);
                return Task.CompletedTask;
            });

            Log.Information("AnalyticsViewModel: 加载完成 period={Period} kpis={K} heat={H}",
                report.Period.Label, report.Kpis.Count, report.CalendarDays.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载分析数据失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string BuildHardwareChartJson(AnalyticsPeriodReport report)
    {
        if (report.Hardware is not { HasData: true } hw) return "{}";
        var hours = hw.Hours
            .Where(h => h.SampleCount > 0)
            .Select(h => new
            {
                label = h.Label,
                cpu = h.CpuLoadAvg,
                gpu = h.GpuLoadAvg,
                temp = h.GpuTempAvg,
                n = h.SampleCount
            })
            .ToList();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            hours,
            summary = hw.SummaryText
        });
    }

    private static string BuildExtraSummary(AnalyticsExtraMetrics? extra)
    {
        if (extra is null) return "";
        var parts = new List<string>();
        if (extra.HasInputData)
            parts.Add($"按键 {extra.TotalKeys:N0} · 点击 {extra.TotalClicks:N0} · 密度 {extra.KeysPerActiveHour:F0}/时");
        if (extra.HasWebData)
            parts.Add($"网页 {Infrastructure.Analysis.AnalyticsEngine.FormatHours(extra.WebMinutes)} · 占比 {extra.WebSharePct:F0}%");
        if (extra.HasHardwareData)
        {
            var hw = new List<string>();
            if (extra.CpuLoadAvg is { } c) hw.Add($"CPU均 {c:F0}%");
            if (extra.GpuLoadAvg is { } g) hw.Add($"GPU均 {g:F0}%");
            if (extra.GpuTempMax is { } t) hw.Add($"GPU峰温 {t:F0}°C");
            if (hw.Count > 0) parts.Add(string.Join(" · ", hw));
        }
        return string.Join("　|　", parts);
    }
}

/// <summary>
/// 分析页导航参数（NavigationService.NavigateTo("Analytics", new AnalyticsNavArgs(key))）。
/// Pivot 取值：overview / rhythm / compose / flow。
/// 3.10+ 分析页已具备节奏、构成页签，ActionKey 导航可直接命中。
/// </summary>
public sealed record AnalyticsNavArgs(string Pivot);