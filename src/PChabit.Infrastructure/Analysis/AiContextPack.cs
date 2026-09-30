using System.Text.Json;
using System.Text.Json.Serialization;
using PChabit.Core.ValueObjects;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// AI 指标包 v2：数值优先、含日序列/环比/标签/目标，供深度解读与追问使用。
/// 不含窗口标题、URL、会话明细。严格模式下去掉应用显示名。
/// </summary>
public static class AiContextPack
{
    /// <summary>指标字典：id → 含义（写入 payload，供模型引用）。</summary>
    public static readonly Dictionary<string, string> MetricDictionary = new()
    {
        ["activeMinutes"] = "活跃时长（分钟）",
        ["focusMinutes"] = "专注时长（分钟，≥25min 生产力会话）",
        ["prodSharePct"] = "生产力分类占比（%）",
        ["nightMinutes"] = "夜间时长（23:00-06:00，分钟）",
        ["keys"] = "按键次数",
        ["clicks"] = "鼠标点击次数",
        ["webMinutes"] = "网页有效浏览（分钟）",
        ["webSharePct"] = "网页占比（%）",
        ["appSwitches"] = "应用切换次数",
        ["dayCountWithData"] = "有效数据天数",
        ["labelMinutes"] = "活动标签分钟字典（work-code/browse-fun/...）",
        ["daily"] = "按日序列：date/dow/activeMin/focusMin/prodPct/nightMin",
        ["hourHeat"] = "24 小时活跃分钟桶（0-23）",
    };

    public static string Build(
        AnalyticsPeriodReport report,
        bool strictPrivacy = false,
        AiGoals? goals = null,
        IReadOnlyList<AiPlanItem>? lastPlan = null,
        HabitProfile? profile = null,
        PersonalBaseline? baseline = null,
        IReadOnlyList<AiDeviation>? deviations = null,
        IReadOnlyList<HabitTrajectoryBuilder.HabitTrend>? trajectory = null)
    {
        var payload = BuildModel(report, strictPrivacy, goals, lastPlan, profile, baseline, deviations, trajectory);
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    public static AiPackModel BuildModel(
        AnalyticsPeriodReport report,
        bool strictPrivacy = false,
        AiGoals? goals = null,
        IReadOnlyList<AiPlanItem>? lastPlan = null,
        HabitProfile? profile = null,
        PersonalBaseline? baseline = null,
        IReadOnlyList<AiDeviation>? deviations = null,
        IReadOnlyList<HabitTrajectoryBuilder.HabitTrend>? trajectory = null)
    {
        var quality = AnalysisReportBuilder.BuildQuality(report);
        var metrics = new List<AiMetric>();

        void Add(string id, string name, double? value, double? prev, string unit)
        {
            double? delta = null, deltaPct = null;
            if (value is { } v && prev is { } p)
            {
                delta = Math.Round(v - p, 2);
                deltaPct = p != 0 ? Math.Round((v - p) / Math.Abs(p) * 100, 1) : null;
            }
            metrics.Add(new AiMetric
            {
                Id = id,
                Name = name,
                Value = value is { } vv ? Math.Round(vv, 2) : null,
                Prev = prev is { } pp ? Math.Round(pp, 2) : null,
                Delta = delta,
                DeltaPct = deltaPct,
                Unit = unit
            });
        }

        double? ParseNum(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var t = s.Trim().Replace(",", "").Replace("％", "%").Replace("%", "").Replace("h", "").Replace("H", "");
            return double.TryParse(t, out var n) ? n : null;
        }

        foreach (var k in report.Kpis)
        {
            // KpiCard.Value/DeltaText 为展示串；此处尽力解析数值
            var unit = k.Name.Contains("时长") || k.Value.Contains('h') || k.Value.Contains('H') ? "min"
                : k.Name.Contains("%") || k.Value.Contains('%') || k.Name.Contains("占比") ? "pct"
                : k.Name.Contains("次") || k.Name.Contains("键") ? "count" : "raw";
            Add(k.Id, k.Name, ParseNum(k.Value), null, unit);
        }

        // 从 Extra 与 HourHeat 补关键数值
        if (report.Extra is { } ex)
        {
            ReplaceOrAdd(metrics, "keys", "按键次数", ex.TotalKeys, null, "count");
            ReplaceOrAdd(metrics, "clicks", "鼠标点击次数", ex.TotalClicks, null, "count");
            ReplaceOrAdd(metrics, "webMinutes", "网页有效浏览", ex.WebMinutes, null, "min");
            ReplaceOrAdd(metrics, "webSharePct", "网页占比", ex.WebSharePct, null, "pct");
        }

        // 夜间：小时桶 23 + 0-5
        if (report.HourHeat.Count >= 24)
        {
            double night = 0;
            for (var hour = 23; hour < 24; hour++) night += report.HourHeat[hour].Minutes;
            for (var hour = 0; hour < 6; hour++) night += report.HourHeat[hour].Minutes;
            ReplaceOrAdd(metrics, "nightMinutes", "夜间活跃分钟(23-6)", Math.Round(night, 1), null, "min");
        }

        ReplaceOrAdd(metrics, "dayCountWithData", "有效数据天数", report.DayCountWithData, null, "count");

        // 日序列（仅期内）
        var daily = report.CalendarDays
            .Where(d => d.InPeriod)
            .Select(d => new AiDaily
            {
                Date = d.Date.ToString("yyyy-MM-dd"),
                Dow = (int)d.Date.DayOfWeek,
                ActiveMin = Math.Round(d.Hours * 60, 1),
                Hours = Math.Round(d.Hours, 2)
            })
            .ToList();

        var hourHeat = report.HourHeat
            .OrderBy(hh => hh.Hour)
            .Select(hh => Math.Round(hh.Minutes, 1))
            .ToList();

        var categories = report.CategoryShares
            .Select(c => new AiShare
            {
                Name = strictPrivacy ? "category" : c.Category,
                Minutes = Math.Round(c.Minutes, 1),
                Pct = Math.Round(c.Percentage, 1)
            })
            .ToList();

        var topApps = report.TopChanges
            .Select(t => new AiTopApp
            {
                Name = strictPrivacy ? "app" : t.Name,
                Kind = t.Kind,
                Minutes = Math.Round(t.CurrentMinutes, 1),
                PrevMinutes = Math.Round(t.PreviousMinutes, 1),
                Delta = Math.Round(t.CurrentMinutes - t.PreviousMinutes, 1),
                DeltaText = t.DeltaText,
                Direction = t.Direction
            })
            .ToList();

        AiHardwarePack? hw = null;
        if (report.Hardware is { HasData: true } h)
        {
            hw = new AiHardwarePack
            {
                CpuAvg = h.CpuLoadAvg,
                CpuP95 = h.CpuLoadP95,
                GpuAvg = h.GpuLoadAvg,
                GpuTempMax = h.GpuTempMax,
                MemAvg = h.MemLoadAvg,
                SampleDays = h.SampleDays,
                SampleMinutes = h.SampleMinutes,
                AppLoads = h.AppLoads.Take(8).Select(a => new AiShare
                {
                    Name = strictPrivacy ? "app" : a.DisplayName,
                    Minutes = Math.Round(a.OverlapMinutes, 1),
                    Metric = a.GpuLoadAvg ?? 0
                }).ToList()
            };
        }

        var labelMinutes = new Dictionary<string, double>();
        foreach (var l in ActivityLabels.All) labelMinutes[l] = 0;
        // 若 Extra/日汇总未直接给出标签，保持全 0 并标 missing —— 由调用方后续注入
        // 这里从 report 无法直接读 DailySummary；调用方可通过 InjectLabels 填充

        return new AiPackModel
        {
            Meta = new AiMeta
            {
                Period = $"{report.Period.Start:yyyy-MM-dd}~{report.Period.EndExclusive.AddDays(-1):yyyy-MM-dd}",
                Label = report.Period.Label,
                Quality = quality.Summary,
                HasEnoughData = report.HasEnoughData,
                DayCountWithData = report.DayCountWithData,
                Sources = BuildSources(report),
                StrictPrivacy = strictPrivacy
            },
            Metrics = metrics,
            Daily = daily,
            HourHeat = hourHeat,
            Categories = categories,
            TopApps = topApps,
            Hardware = hw,
            LabelMinutes = labelMinutes,
            Goals = goals,
            LastAiPlan = lastPlan?.ToList(),
            Profile = profile is null ? null : new AiProfilePack
            {
                Chronotype = profile.Chronotype,
                TypicalStartHour = profile.TypicalStartHour,
                TypicalEndHour = profile.TypicalEndHour,
                PeakDeepHour = profile.PeakDeepHour,
                MedianActiveMinutes = profile.MedianActiveMinutes,
                MedianFocusMinutes = profile.MedianFocusMinutes,
                MedianNightMinutes = profile.MedianNightMinutes,
                MedianSwitchPerHour = profile.MedianSwitchPerHour,
                ProductiveSharePct = profile.ProductiveSharePct,
                SampleDays = profile.SampleDays,
                Mature = profile.Mature
            },
            Baseline = baseline is null ? null : PersonalBaselineBuilder.ToAiSummary(baseline),
            Deviations = deviations?.Select(d => new AiDeviationPack
            {
                MetricId = d.MetricId,
                Name = d.Name,
                Value = d.Value,
                BaselineP50 = d.BaselineP50,
                DeviationPct = d.DeviationPct,
                Elevated = d.Elevated,
                Low = d.Low
            }).ToList(),
            HabitTrajectory = trajectory?.Select(t => new AiTrendPack
            {
                Name = t.Name,
                Direction = t.Direction,
                Strength = t.Strength,
                SupportWeeks = t.SupportWeeks,
                Detail = t.Detail
            }).ToList(),
            RuleInsights = report.Insights
                .Take(8)
                .Select(i => new AiRuleInsight { Severity = i.Severity, Title = i.Title, Message = i.Message })
                .ToList(),
            MetricDictionary = MetricDictionary
        };
    }

    /// <summary>把 DailySummary 的标签分钟注入 pack（有数据时覆盖全 0）。</summary>
    public static void InjectLabels(AiPackModel pack, IReadOnlyDictionary<string, double>? labelMinutes)
    {
        if (labelMinutes is null || labelMinutes.Count == 0) return;
        foreach (var kv in labelMinutes)
            pack.LabelMinutes[kv.Key] = Math.Round(kv.Value, 1);
    }

    private static void ReplaceOrAdd(List<AiMetric> metrics, string id, string name, double? value, double? prev, string unit)
    {
        var existing = metrics.Find(m => m.Id == id);
        if (existing is not null)
        {
            existing.Value = value is { } v ? Math.Round(v, 2) : existing.Value;
            return;
        }
        metrics.Add(new AiMetric { Id = id, Name = name, Value = value is { } vv ? Math.Round(vv, 2) : null, Unit = unit });
    }

    private static List<string> BuildSources(AnalyticsPeriodReport report)
    {
        var s = new List<string> { "app" };
        if (report.Extra is { HasInputData: true }) s.Add("input");
        if (report.Extra is { HasWebData: true }) s.Add("web");
        if (report.Hardware is { HasData: true }) s.Add("hardware");
        return s;
    }

    // ===== 模型 =====

    public sealed class AiPackModel
    {
        public AiMeta Meta { get; set; } = new();
        public List<AiMetric> Metrics { get; set; } = new();
        public List<AiDaily> Daily { get; set; } = new();
        public List<double> HourHeat { get; set; } = new();
        public Dictionary<string, double> LabelMinutes { get; set; } = new();
        public List<AiShare> Categories { get; set; } = new();
        public List<AiTopApp> TopApps { get; set; } = new();
        public AiHardwarePack? Hardware { get; set; }
        public AiGoals? Goals { get; set; }
        public List<AiPlanItem>? LastAiPlan { get; set; }
        public AiProfilePack? Profile { get; set; }
        public Dictionary<string, object>? Baseline { get; set; }
        public List<AiDeviationPack>? Deviations { get; set; }
        public List<AiTrendPack>? HabitTrajectory { get; set; }
        public List<AiRuleInsight> RuleInsights { get; set; } = new();
        public Dictionary<string, string> MetricDictionary { get; set; } = new();
    }

    public sealed class AiTrendPack
    {
        public string Name { get; set; } = "";
        public string Direction { get; set; } = "stable";
        public double Strength { get; set; }
        public int SupportWeeks { get; set; }
        public string Detail { get; set; } = "";
    }

    public sealed class AiProfilePack
    {
        public string Chronotype { get; set; } = "unknown";
        public double? TypicalStartHour { get; set; }
        public double? TypicalEndHour { get; set; }
        public double? PeakDeepHour { get; set; }
        public double MedianActiveMinutes { get; set; }
        public double MedianFocusMinutes { get; set; }
        public double MedianNightMinutes { get; set; }
        public double MedianSwitchPerHour { get; set; }
        public double? ProductiveSharePct { get; set; }
        public int SampleDays { get; set; }
        public bool Mature { get; set; }
    }

    public sealed class AiDeviationPack
    {
        public string MetricId { get; set; } = "";
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public double BaselineP50 { get; set; }
        public double? DeviationPct { get; set; }
        public bool Elevated { get; set; }
        public bool Low { get; set; }
    }

    public sealed class AiMeta
    {
        public string Period { get; set; } = "";
        public string Label { get; set; } = "";
        public string Quality { get; set; } = "";
        public bool HasEnoughData { get; set; }
        public int DayCountWithData { get; set; }
        public List<string> Sources { get; set; } = new();
        public bool StrictPrivacy { get; set; }
    }

    public sealed class AiMetric
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double? Value { get; set; }
        public double? Prev { get; set; }
        public double? Delta { get; set; }
        public double? DeltaPct { get; set; }
        public string Unit { get; set; } = "";
    }

    public sealed class AiDaily
    {
        public string Date { get; set; } = "";
        public int Dow { get; set; }
        public double Hours { get; set; }
        public double ActiveMin { get; set; }
    }

    public sealed class AiShare
    {
        public string Name { get; set; } = "";
        public double Minutes { get; set; }
        public double Pct { get; set; }
        public double Metric { get; set; }
    }

    public sealed class AiTopApp
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public double Minutes { get; set; }
        public double PrevMinutes { get; set; }
        public double Delta { get; set; }
        public string DeltaText { get; set; } = "";
        public string Direction { get; set; } = "";
    }

    public sealed class AiHardwarePack
    {
        public double? CpuAvg { get; set; }
        public double? CpuP95 { get; set; }
        public double? GpuAvg { get; set; }
        public double? GpuTempMax { get; set; }
        public double? MemAvg { get; set; }
        public int SampleDays { get; set; }
        public int SampleMinutes { get; set; }
        public List<AiShare> AppLoads { get; set; } = new();
    }

    public sealed class AiRuleInsight
    {
        public string Severity { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public sealed class AiGoals
    {
        public double? DailyActiveTargetMin { get; set; }
        public double? WeeklyFocusTargetMin { get; set; }
        public string? UserNote { get; set; }
        /// <summary>历史计划反馈：done/skipped 统计，供模型降低无效建议。</summary>
        public AiPlanFeedback? PlanFeedback { get; set; }
    }

    public sealed class AiPlanFeedback
    {
        public int Total { get; set; }
        public int Done { get; set; }
        public int Skipped { get; set; }
        public List<string> OftenSkipped { get; set; } = new();
        public List<string> OftenDone { get; set; } = new();
    }

    public sealed class AiPlanItem
    {
        public string Title { get; set; } = "";
        public string? TargetMetricId { get; set; }
        public double? TargetValue { get; set; }
        public string Status { get; set; } = "pending"; // pending | done | skipped
        public string? Note { get; set; }
    }
}
