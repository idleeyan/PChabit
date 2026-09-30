using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.ValueObjects;
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
    public string? TargetMetricId { get; init; }
    public double? TargetValue { get; init; }
}

/// <summary>周计划项：可采纳 / 忽略。</summary>
public sealed partial class AiPlanViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string? ActionKey { get; init; }
    public string? TargetMetricId { get; init; }
    public double? TargetValue { get; init; }
    public string Effort { get; init; } = "med";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _status = "pending"; // pending | done | skipped

    public bool IsPending => Status == "pending";
    public string StatusText => Status switch
    {
        "done" => "已采纳",
        "skipped" => "已忽略",
        _ => "待处理"
    };

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(StatusText));
    }
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
    private readonly IAiInsightHistoryService _aiHistory;
    private AnalyticsPeriodReport? _lastReport;
    private AiContextPack.AiPackModel? _lastPack;
    private AiInsightSnapshot? _lastSnapshot;
    private HabitProfile? _lastProfileForExport;
    private AnalyticsAiResponseParser.ParsedAiResult? _lastParsed;

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

    /// <summary>流式/一次性请求的取消源。</summary>
    private CancellationTokenSource? _aiCts;

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
    public ObservableCollection<AiPlanViewModel> AiPlans { get; } = new();
    public ObservableCollection<string> AiDiagnosis { get; } = new();
    public ObservableCollection<string> AiFollowUps { get; } = new();
    public ObservableCollection<HabitTrendViewModel> HabitTrends { get; } = new();

    [ObservableProperty]
    private bool _hasHabitTrends;

    [ObservableProperty]
    private bool _hasAiPlans;

    [ObservableProperty]
    private bool _hasAiDiagnosis;

    [ObservableProperty]
    private bool _hasAiFollowUps;

    [ObservableProperty]
    private string _followUpQuestion = "";

    [ObservableProperty]
    private string _followUpAnswer = "";

    [ObservableProperty]
    private bool _hasFollowUpAnswer;

    [ObservableProperty]
    private bool _isFollowUpRunning;

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
        ISettingsService settings,
        IAiInsightHistoryService aiHistory) : base()
    {
        _dbContextFactory = dbContextFactory;
        _aiService = aiService;
        _settings = settings;
        _aiHistory = aiHistory;
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

        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;

        IsAiRunning = true;
        AiResultText = "正在请求 AI 解读…（仅发送聚合指标；可取消）";
        HasAiResult = true;
        HasAiParsed = false;
        try
        {
            // 装载近 28 天画像/基线（失败不阻断解读）
            AiContextPack.AiGoals? goals = null;
            HabitProfile? profile = null;
            PersonalBaseline? baseline = null;
            List<AiDeviation>? deviations = null;
            List<AiContextPack.AiPlanItem>? lastPlan = null;
            List<HabitTrajectoryBuilder.HabitTrend>? trajectory = null;
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
                var days = await HabitDayLoader.LoadAsync(db, DateTime.Today.AddDays(-28), DateTime.Today.AddDays(1), ct);
                profile = HabitProfileBuilder.Build(days);
                baseline = PersonalBaselineBuilder.Build(days);
                trajectory = HabitTrajectoryBuilder.Build(days);
                RunOnUIThread(() =>
                {
                    HabitTrends.Clear();
                    foreach (var t in trajectory)
                    {
                        HabitTrends.Add(new HabitTrendViewModel
                        {
                            Name = t.Name,
                            Direction = t.Direction,
                            Detail = t.Detail,
                            SupportWeeks = t.SupportWeeks,
                            Strength = t.Strength
                        });
                    }
                    HasHabitTrends = HabitTrends.Count > 0;
                });
                var cur = AggregatePeriod(days, _lastReport);
                if (cur is not null)
                {
                    deviations = PersonalBaselineBuilder.ComputeDeviations(
                        baseline,
                        cur.Value.Active,
                        cur.Value.Focus,
                        cur.Value.Night,
                        cur.Value.SwitchPerHour);
                }

                // 上次计划优先来自解读历史（比内存里的 AiPlans 更完整）
                var lastPlanFromDb = await _aiHistory.LoadLastPlanAsync(ct);
                if (lastPlanFromDb.Count > 0)
                    lastPlan = AiInsightHistoryService.ToPackPlan(lastPlanFromDb);

                goals = new AiContextPack.AiGoals
                {
                    DailyActiveTargetMin = _settings.DailyUsageGoalHours > 0 ? _settings.DailyUsageGoalHours * 60 : null,
                    PlanFeedback = AiPlanFeedbackBuilder.FromPlan(
                        lastPlanFromDb.Count > 0
                            ? lastPlanFromDb
                            : AiPlans.Select(p => new AiPlanItem
                            {
                                Title = p.Title,
                                Detail = p.Detail,
                                Status = p.Status
                            }).ToList())
                };
            }
            catch (Exception loadEx)
            {
                Log.Warning(loadEx, "画像/基线加载失败，解读继续（无画像）");
            }

            if (AiPlans.Count > 0)
            {
                lastPlan ??= new List<AiContextPack.AiPlanItem>();
                foreach (var p in AiPlans.Where(p => lastPlan.All(x => x.Title != p.Title)))
                    lastPlan.Add(new AiContextPack.AiPlanItem
                    {
                        Title = p.Title,
                        TargetMetricId = p.TargetMetricId,
                        TargetValue = p.TargetValue,
                        Status = p.Status,
                        Note = p.Detail
                    });
            }

            var payload = AiContextPack.Build(
                _lastReport,
                strictPrivacy: _settings.AiStrictPrivacy,
                goals: goals,
                lastPlan: lastPlan,
                profile: profile,
                baseline: baseline,
                deviations: deviations,
                trajectory: trajectory);

            _lastPack = AiContextPack.BuildModel(_lastReport, _settings.AiStrictPrivacy, goals, lastPlan, profile, baseline, deviations, trajectory);
            _lastProfileForExport = profile;

            var raw = new System.Text.StringBuilder();
            try
            {
                await foreach (var chunk in _aiService.InterpretStreamAsync(AnalysisReportBuilder.SystemPrompt, payload, ct))
                {
                    raw.Append(chunk);
                    var partial = raw.ToString();
                    RunOnUIThread(() =>
                    {
                        AiResultText = partial.Length > 0 ? partial : "正在请求 AI 解读…";
                    });
                }
            }
            catch (Exception streamEx) when (streamEx is not OperationCanceledException)
            {
                Log.Information(streamEx, "AI 流式失败，回退一次性");
                raw.Clear();
                raw.Append(await _aiService.InterpretAsync(AnalysisReportBuilder.SystemPrompt, payload, ct));
            }

            if (ct.IsCancellationRequested)
            {
                AiResultText = "已取消本次 AI 解读。";
                HasAiParsed = false;
                return;
            }

            var rawText = raw.ToString();
            var parsed = AnalyticsAiResponseParser.Parse(rawText);
            _lastParsed = parsed;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                AiResultText = "AI 返回为空。";
                HasAiParsed = false;
            }
            else if (!parsed.Parsed)
            {
                // 未按约定返回中文 JSON：提示 + 原文可复制，不把英文堆当结论
                var looksEnglish = LooksMostlyEnglish(rawText);
                AiResultText = looksEnglish
                    ? "模型未按约定返回简体中文 JSON（疑似英文/闲聊输出）。请检查云端模型是否匹配，或改用本地/双端点。\n\n—— 原始回复（可复制）——\n" + TruncateForUi(rawText, 1200)
                    : "未能解析为结构化结果。\n\n—— 原始回复（可复制）——\n" + TruncateForUi(rawText, 1200);
                HasAiParsed = false;
            }
            else
            {
                AiResultText = parsed.Summary;
                ApplyParsedAi(parsed);
            }

            // 闭环：落库快照，供下次 lastAiPlan 与追问
            if (parsed.Parsed)
            {
                try
                {
                    var snap = AiInsightSnapshotFactory.FromParsed(
                        _lastReport.Period.Start.ToString("yyyy-MM-dd"),
                        _lastReport.Period.Label,
                        _lastReport.Period.Start,
                        _lastReport.Period.EndExclusive,
                        parsed);
                    await _aiHistory.SaveAsync(snap, ct);
                    _lastSnapshot = snap;
                }
                catch (Exception saveEx)
                {
                    Log.Warning(saveEx, "保存 AI 解读快照失败");
                }
            }
        }
        catch (OperationCanceledException)
        {
            AiResultText = "已取消本次 AI 解读。";
            HasAiParsed = false;
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

    [RelayCommand]
    private void CancelAiInsight()
    {
        try
        {
            _aiCts?.Cancel();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "取消 AI 解读");
        }
    }

    /// <summary>导出解读 Markdown（复制到剪贴板）。</summary>
    [RelayCommand]
    private async Task CopyAiInsightAsync()
    {
        try
        {
            if (_lastParsed is null || _lastReport is null)
            {
                AiResultText = "暂无可导出的 AI 解读，请先生成。";
                HasAiResult = true;
                return;
            }

            var planItems = AiPlans.Count > 0
                ? AiPlans.Select(p => new AiPlanItem
                {
                    Title = p.Title,
                    Detail = p.Detail,
                    ActionKey = p.ActionKey,
                    TargetMetricId = p.TargetMetricId,
                    TargetValue = p.TargetValue,
                    Status = p.Status
                }).ToList()
                : null;

            var md = AiInsightMarkdownExporter.Export(
                _lastReport.Period.Label,
                _lastReport.Period.Start,
                _lastReport.Period.EndExclusive,
                _lastParsed,
                planItems,
                _lastProfileForExport);

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(md);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Log.Information("AI 解读 Markdown 已复制");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "导出 AI 解读失败");
        }
    }

    /// <summary>追问：带上精简指标包 + 上次解读摘要。</summary>
    [RelayCommand]
    private async Task SendFollowUpAsync()
    {
        var q = FollowUpQuestion?.Trim();
        if (string.IsNullOrEmpty(q)) return;
        if (!_aiService.IsConfigured)
        {
            FollowUpAnswer = "AI 未启用，请到设置配置。";
            HasFollowUpAnswer = true;
            return;
        }

        IsFollowUpRunning = true;
        HasFollowUpAnswer = false;
        FollowUpAnswer = "正在追问…";
        HasFollowUpAnswer = true;
        try
        {
            var context = new System.Text.StringBuilder();
            if (_lastPack is not null)
            {
                // 精简：meta + metrics + deviations + profile 摘要
                context.AppendLine("## 指标摘要");
                context.AppendLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    _lastPack.Meta,
                    _lastPack.Metrics,
                    _lastPack.Deviations,
                    _lastPack.Profile
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));
            }
            if (!string.IsNullOrEmpty(AiSummary))
            {
                context.AppendLine("## 上次解读结论");
                context.AppendLine(AiSummary);
            }
            if (AiPlans.Count > 0)
            {
                context.AppendLine("## 当前计划");
                foreach (var p in AiPlans)
                    context.AppendLine($"- {p.Title}（{p.StatusText}）{p.Detail}");
            }
            context.AppendLine("## 用户追问");
            context.AppendLine(q);

            var answer = await _aiService.ChatFastAsync(
                AnalysisReportBuilder.FollowUpSystemPrompt,
                context.ToString());
            FollowUpAnswer = string.IsNullOrWhiteSpace(answer) ? "（无回答）" : answer;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI 追问失败");
            FollowUpAnswer = $"追问失败：{ex.Message}";
        }
        finally
        {
            IsFollowUpRunning = false;
        }
    }

    private void ApplyParsedAi(AnalyticsAiResponseParser.ParsedAiResult parsed)
    {
        AiFindings.Clear();
        AiSuggestions.Clear();
        AiRisks.Clear();
        AiPlans.Clear();
        AiDiagnosis.Clear();
        AiFollowUps.Clear();
        AiSummary = parsed.Summary;
        if (!parsed.Parsed)
        {
            HasAiParsed = false;
            HasAiPlans = false;
            HasAiDiagnosis = false;
            HasAiFollowUps = false;
            return;
        }
        foreach (var f in AiInsightDeduper.FilterFindings(
            parsed.Findings,
            Insights.Select(i => (i.Title, i.Message)).ToList()))
            AiFindings.Add(new AiItemViewModel { Title = f.Title, Detail = f.Detail });
        foreach (var s in parsed.Suggestions)
            AiSuggestions.Add(new AiItemViewModel
            {
                Title = s.Title,
                Detail = s.Action,
                ActionKey = s.ActionKey,
                TargetMetricId = s.TargetMetricId,
                TargetValue = s.TargetValue
            });
        foreach (var p in parsed.Plan ?? parsed.Suggestions.Select(s =>
            new AnalyticsAiResponseParser.AiPlanItem(s.Title, s.Action, s.ActionKey, s.TargetMetricId, s.TargetValue, "med")))
        {
            AiPlans.Add(new AiPlanViewModel
            {
                Title = p.Title,
                Detail = p.Detail,
                ActionKey = p.ActionKey,
                TargetMetricId = p.TargetMetricId,
                TargetValue = p.TargetValue,
                Effort = p.Effort
            });
        }
        foreach (var d in parsed.Diagnosis ?? Array.Empty<AnalyticsAiResponseParser.AiDiagnosis>())
        {
            var conf = d.Confidence switch { "high" => "高", "low" => "低", _ => "中" };
            AiDiagnosis.Add($"[{conf}] {d.Hypothesis}");
        }
        foreach (var f in parsed.FollowUps ?? Array.Empty<string>())
            AiFollowUps.Add(f);
        foreach (var r in parsed.Risks)
            AiRisks.Add(r);
        HasAiParsed = true;
        HasAiPlans = AiPlans.Count > 0;
        HasAiDiagnosis = AiDiagnosis.Count > 0;
        HasAiFollowUps = AiFollowUps.Count > 0;
    }

    [RelayCommand]
    private async Task MarkPlanDoneAsync(AiPlanViewModel? item)
    {
        if (item is null) return;
        item.Status = "done";
        Log.Information("计划采纳: {Title}", item.Title);
        await PersistPlanStatusAsync(item.Title, "done");
    }

    [RelayCommand]
    private async Task MarkPlanSkippedAsync(AiPlanViewModel? item)
    {
        if (item is null) return;
        item.Status = "skipped";
        Log.Information("计划忽略: {Title}", item.Title);
        await PersistPlanStatusAsync(item.Title, "skipped");
    }

    private async Task PersistPlanStatusAsync(string title, string status)
    {
        try
        {
            var snap = _lastSnapshot ?? await _aiHistory.GetLatestAsync();
            if (snap is null) return;
            await _aiHistory.UpdatePlanStatusAsync(snap.Id, title, status);
            _lastSnapshot = null; // 强制下次重读
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存计划状态失败");
        }
    }

    private static bool LooksMostlyEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var letters = 0;
        var han = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch)) continue;
            if (ch >= 0x4e00 && ch <= 0x9fff) han++;
            else letters++;
        }
        return letters > 40 && han < letters / 4;
    }

    private static string TruncateForUi(string s, int n)
    {
        s = s.Trim();
        return s.Length <= n ? s : s[..n] + "…";
    }

    /// <summary>把当期报告的活跃/专注/夜间/切换聚成一行，供偏离计算。</summary>
    private static (double Active, double Focus, double Night, double SwitchPerHour)? AggregatePeriod(
        List<HabitDay> days,
        AnalyticsPeriodReport report)
    {
        var from = report.Period.Start.Date;
        var to = report.Period.EndExclusive.Date;
        var cur = days.Where(d => d.Date >= from && d.Date < to).ToList();
        if (cur.Count == 0) return null;
        var active = cur.Sum(d => d.ActiveMinutes);
        var focus = cur.Sum(d => d.FocusMinutes ?? 0);
        var night = cur.Sum(d => d.NightMinutes);
        var switches = cur.Sum(d => d.AppSwitches ?? 0);
        var switchPerHour = active > 0 ? switches / (active / 60.0) : 0;
        return (active, focus, night, Math.Round(switchPerHour, 1));
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
