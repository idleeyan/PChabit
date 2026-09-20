using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Formatters;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

/// <summary>AI 解读中的条目（标题+说明，可带 ActionKey）。</summary>
public sealed class AiItemViewModel
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string? ActionKey { get; init; }
}

/// <summary>
/// 分析页：周期选择 + KPI 环比 + 日历/小时热力 + Top 变化 + 可行动洞察。
/// 计算逻辑在 AnalyticsEngine，本类只负责绑定与交互。
/// </summary>
public partial class AnalyticsViewModel : ViewModelBase
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    private readonly IAnalyticsAiService _aiService;
    private readonly ISettingsService _settings;
    private AnalyticsPeriodReport? _lastReport;

    [ObservableProperty]
    private string _selectedPeriodKey = "本周";

    public string[] PeriodOptions { get; } =
        { "本周", "上周", "近 7 天", "近 30 天", "本月" };

    [ObservableProperty]
    private string _periodLabel = "本周";

    [ObservableProperty]
    private string _periodRangeText = "";

    [ObservableProperty]
    private string _dataQualityText = "";

    [ObservableProperty]
    private bool _showLowDataBanner;

    [ObservableProperty]
    private bool _hasTopChanges;

    [ObservableProperty]
    private bool _hasCategories;

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
    private string _aiResultText = "";

    [ObservableProperty]
    private bool _hasAiResult;

    [ObservableProperty]
    private bool _isAiRunning;

    [ObservableProperty]
    private string _aiSummary = "";

    [ObservableProperty]
    private bool _hasAiParsed;

    [ObservableProperty]
    private string _webSummaryText = "";

    [ObservableProperty]
    private bool _hasWebData;

    /// <summary>硬件页 WebView2 图表数据 JSON。</summary>
    [ObservableProperty]
    private string _hardwareChartJson = "{}";

    public ObservableCollection<AiItemViewModel> AiFindings { get; } = new();
    public ObservableCollection<AiItemViewModel> AiSuggestions { get; } = new();
    public ObservableCollection<string> AiRisks { get; } = new();

    public bool AiConfigured => _aiService.IsConfigured;

    public ObservableCollection<KpiCard> Kpis { get; } = new();
    public ObservableCollection<DayHeatItem> CalendarDays { get; } = new();
    public ObservableCollection<HourHeatItem> HourHeat { get; } = new();
    public ObservableCollection<TopChangeItem> TopChanges { get; } = new();
    public ObservableCollection<CategoryShareItem> CategoryShares { get; } = new();
    public ObservableCollection<AnalyticsInsight> Insights { get; } = new();
    public ObservableCollection<AppHardwareLoad> HardwareAppLoads { get; } = new();
    public ObservableCollection<HardwareHourPoint> HardwareHours { get; } = new();
    public AnalyticsExtraMetrics? ExtraMetrics { get; private set; }
    public HardwareAnalyticsReport? HardwareReport { get; private set; }

    // 兼容旧 XAML 引用（若仍有绑定）
    public ObservableCollection<WeeklyDataItem> WeeklyData { get; } = new();
    public ObservableCollection<TrendItem> Trends { get; } = new();
    public ObservableCollection<PatternItem> Patterns { get; } = new();
    public ObservableCollection<InsightItem> InsightsLegacy { get; } = new();
    public string TotalActiveTime { get; private set; } = "—";
    public string AverageDailyTime { get; private set; } = "—";
    public double AverageProductivity { get; private set; }
    public int TotalKeyPresses { get; private set; }
    public int TotalMouseClicks { get; private set; }
    public int TotalWebPages { get; private set; }

    public AnalyticsViewModel(
        IDbContextFactory<PChabitDbContext> dbContextFactory,
        IAnalyticsAiService aiService,
        ISettingsService settings) : base()
    {
        _dbContextFactory = dbContextFactory;
        _aiService = aiService;
        _settings = settings;
        Title = "分析";
    }

    partial void OnSelectedPeriodKeyChanged(string value)
    {
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadDataAsync();

    /// <summary>
    /// 页内导航请求（参数为 ActionKey：compose/rhythm 等）。
    /// 由 AnalyticsPage 订阅并切换 Pivot/锚点；跨页调用方则用 AnalyticsNavArgs 走 NavigationService。
    /// </summary>
    public event Action<string>? PivotNavigationRequested;

    [RelayCommand]
    private void NavigateInsight(AnalyticsInsight? insight)
    {
        if (insight?.ActionKey is null) return;
        Log.Information("洞察行动: {Action} — {Title}", insight.ActionKey, insight.Title);
        PivotNavigationRequested?.Invoke(insight.ActionKey);
    }

    [RelayCommand]
    private async Task CopySummaryAsync()
    {
        try
        {
            var text = _lastReport is null
                ? "暂无分析数据"
                : AnalysisReportBuilder.BuildMarkdown(_lastReport);

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Log.Information("分析深度报告已复制到剪贴板");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "复制分析深度报告失败");
        }
    }

    [RelayCommand]
    private async Task RunAiInsightAsync()
    {
        if (_lastReport is null) return;
        if (!_aiService.IsConfigured)
        {
            AiResultText = "AI 深度解读未启用。请到「设置 → 分析 AI 深度解读」打开开关并填写 Base URL / API Key / 模型（本地模型可空 Key，超时建议 ≥300 秒）。";
            HasAiResult = true;
            HasAiParsed = false;
            return;
        }

        IsAiRunning = true;
        AiResultText = "正在请求 AI 解读…（仅发送聚合指标，可在设置调整超时）";
        HasAiResult = true;
        HasAiParsed = false;
        try
        {
            var payload = AnalysisReportBuilder.BuildAiPayload(_lastReport);
            var raw = await _aiService.InterpretAsync(AnalysisReportBuilder.SystemPrompt, payload);
            var parsed = AnalyticsAiResponseParser.Parse(raw);
            AiResultText = string.IsNullOrWhiteSpace(parsed.Raw) ? "AI 返回为空" : parsed.Raw;
            ApplyParsedAi(parsed);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI 深度解读失败");
            AiResultText = $"AI 解读失败：{ex.Message}";
            HasAiParsed = false;
        }
        finally
        {
            IsAiRunning = false;
        }
    }

    private void ApplyParsedAi(AnalyticsAiResponseParser.ParsedAiResult parsed)
    {
        AiFindings.Clear();
        AiSuggestions.Clear();
        AiRisks.Clear();
        AiSummary = parsed.Summary;
        if (!parsed.Parsed)
        {
            HasAiParsed = false;
            return;
        }
        foreach (var f in parsed.Findings)
            AiFindings.Add(new AiItemViewModel { Title = f.Title, Detail = f.Detail });
        foreach (var s in parsed.Suggestions)
            AiSuggestions.Add(new AiItemViewModel { Title = s.Title, Detail = s.Action, ActionKey = s.ActionKey });
        foreach (var r in parsed.Risks)
            AiRisks.Add(r);
        HasAiParsed = true;
    }

    [RelayCommand]
    private void NavigateAiAction(AiItemViewModel? item)
    {
        var key = item?.ActionKey;
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            if (string.Equals(key, "HistoryReport", StringComparison.OrdinalIgnoreCase))
            {
                var ok = App.GetService<NavigationService>().NavigateTo("HistoryReport");
                Log.Information("AI 行动导航 HistoryReport: {Ok}", ok);
                return;
            }
            PivotNavigationRequested?.Invoke(key);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI 行动导航失败: {Key}", key);
        }
    }

    [RelayCommand]
    private void OpenHistoryReport()
    {
        try
        {
            var ok = App.GetService<NavigationService>().NavigateTo("HistoryReport");
            Log.Information("打开历史分析: {Ok}", ok);
            if (!ok)
            {
                AiResultText = "无法打开历史分析：导航服务未初始化";
                HasAiResult = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开历史分析失败");
            AiResultText = $"打开历史分析失败：{ex.Message}";
            HasAiResult = true;
        }
    }

    public byte[]? BuildExcelBytes()
        => _lastReport is null ? null : AnalysisExcelExporter.Export(_lastReport);

    public AnalyticsPeriodReport? LastReport => _lastReport;

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

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var kind = SelectedPeriodKey switch
            {
                "上周" => AnalyticsPeriodKind.LastWeek,
                "近 7 天" => AnalyticsPeriodKind.Last7Days,
                "近 30 天" => AnalyticsPeriodKind.Last30Days,
                "本月" => AnalyticsPeriodKind.ThisMonth,
                _ => AnalyticsPeriodKind.ThisWeek
            };

            var period = AnalyticsPeriod.FromKind(kind);
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

                CalendarDays.Clear();
                foreach (var d in report.CalendarDays) CalendarDays.Add(d);

                HourHeat.Clear();
                foreach (var h in report.HourHeat) HourHeat.Add(h);

                TopChanges.Clear();
                foreach (var t in report.TopChanges) TopChanges.Add(t);
                HasTopChanges = TopChanges.Count > 0;

                CategoryShares.Clear();
                foreach (var c in report.CategoryShares) CategoryShares.Add(c);
                HasCategories = CategoryShares.Count > 0;

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

                Insights.Clear();
                foreach (var i in report.Insights) Insights.Add(i);

                // 旧字段兼容
                var active = report.Kpis.FirstOrDefault(k => k.Id == "active");
                TotalActiveTime = active?.Value ?? "—";
                AverageDailyTime = $"{report.Period.DayCount} 天周期";
                AverageProductivity = report.Kpis.FirstOrDefault(k => k.Id == "prod") is { } p
                    && double.TryParse(p.Value.TrimEnd('%'), out var pv) ? pv : 0;
                if (report.Extra is { HasInputData: true } ex)
                {
                    TotalKeyPresses = (int)Math.Min(ex.TotalKeys, int.MaxValue);
                    TotalMouseClicks = (int)Math.Min(ex.TotalClicks, int.MaxValue);
                }
                if (report.Extra is { HasWebData: true } exw)
                {
                    TotalWebPages = (int)Math.Min(exw.WebPages, int.MaxValue);
                    WebSummaryText =
                        $"网页 {Infrastructure.Analysis.AnalyticsEngine.FormatHours(exw.WebMinutes)} · {exw.WebPages:N0} 页 · 占比 {exw.WebSharePct:F0}%　可打开「历史分析」查看域名与访问趋势";
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

            Log.Information("AnalyticsViewModel: 加载完成 period={Period} kpis={K} heat={H} insights={I}",
                report.Period.Label, report.Kpis.Count, report.CalendarDays.Count, report.Insights.Count);
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

    // 旧类型保留，避免其它 XAML/代码引用断裂
    public class WeeklyDataItem
    {
        public string Day { get; set; } = "";
        public int Hours { get; set; }
        public int Productivity { get; set; }
    }

    public class TrendItem
    {
        public string Name { get; set; } = "";
        public string Change { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class PatternItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "";
    }

    public class InsightItem
    {
        public string Type { get; set; } = "info";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
    }
}

/// <summary>
/// 分析页导航参数（NavigationService.NavigateTo("Analytics", new AnalyticsNavArgs(key))）。
/// Pivot 取值：overview / rhythm / compose / flow。
/// 3.10+ 分析页已具备节奏、构成页签，ActionKey 导航可直接命中。
/// </summary>
public sealed record AnalyticsNavArgs(string Pivot);
