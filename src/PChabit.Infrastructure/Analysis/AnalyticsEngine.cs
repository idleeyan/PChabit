using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Analysis;

public enum AnalyticsPeriodKind
{
    Today,
    Yesterday,
    ThisWeek,
    LastWeek,
    Last7Days,
    Last30Days,
    ThisMonth,
    Custom
}

public record AnalyticsPeriod(DateTime Start, DateTime EndExclusive, string Label)
{
    public int DayCount => Math.Max(1, (EndExclusive.Date - Start.Date).Days);

    public AnalyticsPeriod Previous()
    {
        var days = DayCount;
        var prevEnd = Start.Date;
        var prevStart = prevEnd.AddDays(-days);
        return new AnalyticsPeriod(prevStart, prevEnd, "上一周期");
    }

    public static AnalyticsPeriod FromKind(AnalyticsPeriodKind kind, DateTime? today = null)
    {
        var t = (today ?? DateTime.Today).Date;
        return kind switch
        {
            AnalyticsPeriodKind.Today => new AnalyticsPeriod(t, t.AddDays(1), "今天"),
            AnalyticsPeriodKind.Yesterday => new AnalyticsPeriod(t.AddDays(-1), t, "昨天"),
            AnalyticsPeriodKind.ThisWeek => WeekOf(t, "本周"),
            AnalyticsPeriodKind.LastWeek => WeekOf(t.AddDays(-7), "上周"),
            AnalyticsPeriodKind.Last7Days => new AnalyticsPeriod(t.AddDays(-6), t.AddDays(1), "近 7 天"),
            AnalyticsPeriodKind.Last30Days => new AnalyticsPeriod(t.AddDays(-29), t.AddDays(1), "近 30 天"),
            AnalyticsPeriodKind.ThisMonth => new AnalyticsPeriod(
                new DateTime(t.Year, t.Month, 1),
                t.AddDays(1),
                $"{t.Month} 月"),
            AnalyticsPeriodKind.Custom => throw new ArgumentException("自定义周期请使用 FromCustom(start, endExclusive)", nameof(kind)),
            _ => WeekOf(t, "本周")
        };
    }

    /// <summary>自定义周期；endExclusive 必须晚于 start。</summary>
    public static AnalyticsPeriod FromCustom(DateTime start, DateTime endExclusive, string label = "自定义")
    {
        if (endExclusive <= start)
            throw new ArgumentException("自定义周期结束时间必须晚于开始时间", nameof(endExclusive));
        return new AnalyticsPeriod(start.Date, endExclusive.Date, label);
    }

    /// <summary>周一为一周起点。</summary>
    private static AnalyticsPeriod WeekOf(DateTime anyDay, string label)
    {
        var d = anyDay.Date;
        var diff = ((int)d.DayOfWeek + 6) % 7; // Mon=0
        var monday = d.AddDays(-diff);
        return new AnalyticsPeriod(monday, monday.AddDays(7), label);
    }
}

public record KpiCard(
    string Id,
    string Name,
    string Value,
    string DeltaText,
    string DeltaDirection, // up | down | flat | none
    string Hint);

public record DayHeatItem(
    DateTime Date,
    string DayLabel,
    string DateLabel,
    double Hours,
    int IntensityLevel, // 0-4
    bool InPeriod)
{
    public string HoursText => Hours < 0.05 ? "—" : $"{Hours:F1}h";
}

public record HourHeatItem(int Hour, string Label, double Minutes, int IntensityLevel);

public record TopChangeItem(string Name, string Kind, double CurrentMinutes, double PreviousMinutes, string DeltaText, string Direction)
{
    public string DurationText => AnalyticsEngine.FormatHours(CurrentMinutes);
}

public record CategoryShareItem(string Category, double Minutes, double Percentage)
{
    public string MinutesText => AnalyticsEngine.FormatHours(Minutes);
    public string PercentText => $"{Percentage:F0}%";
}

public record AnalyticsInsight(
    string Id,
    string Severity, // success | info | warning
    string Title,
    string Message,
    string? ActionKey);

/// <summary>日汇总聚合后的多源指标（键鼠 / 网页 / 硬件）。无数据字段为 null。</summary>
public record AnalyticsExtraMetrics(
    double TotalKeys,
    double TotalClicks,
    double KeysPerActiveHour,
    double WebMinutes,
    double WebPages,
    double WebSharePct,
    double? CpuLoadAvg,
    double? CpuLoadP95,
    double? GpuLoadAvg,
    double? GpuTempMax,
    double? MemLoadAvg,
    bool HasInputData,
    bool HasWebData,
    bool HasHardwareData);

public record AnalyticsPeriodReport(
    AnalyticsPeriod Period,
    AnalyticsPeriod? Previous,
    IReadOnlyList<KpiCard> Kpis,
    IReadOnlyList<DayHeatItem> CalendarDays,
    IReadOnlyList<HourHeatItem> HourHeat,
    IReadOnlyList<TopChangeItem> TopChanges,
    IReadOnlyList<CategoryShareItem> CategoryShares,
    IReadOnlyList<AnalyticsInsight> Insights,
    AnalyticsExtraMetrics? Extra,
    HardwareAnalyticsReport? Hardware,
    int DayCountWithData,
    bool HasEnoughData);

/// <summary>
/// 分析指标计算：AppSessions + DailySummaries（键鼠/网页/硬件日汇总）→ KPI / 热力 / 构成 / 洞察。
/// Compute 为纯函数可单测；BuildAsync 只负责查询。
/// </summary>
public static class AnalyticsEngine
{
    private static readonly string[] ProductiveCategories =
        { "开发", "开发工具", "办公", "办公软件", "AI 助手", "生产力" };

    private static readonly string[] EntertainmentCategories =
        { "视频", "游戏", "社交", "娱乐" };

    public static bool IsProductiveCategory(string? category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        return ProductiveCategories.Any(p => category.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsEntertainmentCategory(string? category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        return EntertainmentCategories.Any(p => category.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<AnalyticsPeriodReport> BuildAsync(
        IDbContextFactory<PChabitDbContext> dbFactory,
        AnalyticsPeriod period,
        CancellationToken ct = default)
    {
        var prev = period.Previous();

        List<AppSession> sessions;
        List<AppSession> prevSessions;
        List<DailySummary> daily;
        List<DailySummary> prevDaily;
        List<HardwareSample> hardwareSamples = new();
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            sessions = await QuerySessionsAsync(db, period, ct);
            prevSessions = await QuerySessionsAsync(db, prev, ct);

            // DailySummaries.Date = "yyyy-MM-dd" 字典序与日历一致
            var startKey = period.Start.ToString("yyyy-MM-dd");
            var endKey = period.EndExclusive.ToString("yyyy-MM-dd");
            daily = await db.DailySummaries.AsNoTracking()
                .Where(d => d.Date.CompareTo(startKey) >= 0 && d.Date.CompareTo(endKey) < 0)
                .ToListAsync(ct);

            var pStartKey = prev.Start.ToString("yyyy-MM-dd");
            var pEndKey = prev.EndExclusive.ToString("yyyy-MM-dd");
            prevDaily = await db.DailySummaries.AsNoTracking()
                .Where(d => d.Date.CompareTo(pStartKey) >= 0 && d.Date.CompareTo(pEndKey) < 0)
                .ToListAsync(ct);

            try
            {
                var hwStart = period.Start.ToString("yyyy-MM-dd HH:mm");
                var hwEnd = period.EndExclusive.ToString("yyyy-MM-dd HH:mm");
                hardwareSamples = await db.HardwareSamples.AsNoTracking()
                    .Where(s => s.Timestamp.CompareTo(hwStart) >= 0 && s.Timestamp.CompareTo(hwEnd) < 0)
                    .Select(s => new HardwareSample
                    {
                        Timestamp = s.Timestamp,
                        CpuLoadAvg = s.CpuLoadAvg,
                        GpuLoadAvg = s.GpuLoadAvg,
                        GpuTempMax = s.GpuTempMax,
                        MemLoadAvg = s.MemLoadAvg
                    })
                    .ToListAsync(ct);
            }
            catch
            {
                // 表尚未创建/迁移时硬件融合降级为空
                hardwareSamples = new List<HardwareSample>();
            }
        }

        return Compute(period, prev, sessions, prevSessions, daily, prevDaily, hardwareSamples);
    }

    private static async Task<List<AppSession>> QuerySessionsAsync(
        PChabitDbContext db, AnalyticsPeriod period, CancellationToken ct)
        => await db.AppSessions.AsNoTracking()
            .Where(s => s.StartTime >= period.Start && s.StartTime < period.EndExclusive && s.EndTime != null)
            .Select(s => new AppSession
            {
                Id = s.Id,
                ProcessName = s.ProcessName,
                Category = s.Category,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                InputEventCount = s.InputEventCount
            })
            .ToListAsync(ct);

    public static AnalyticsPeriodReport Compute(
        AnalyticsPeriod period,
        AnalyticsPeriod prev,
        IReadOnlyList<AppSession> sessions,
        IReadOnlyList<AppSession> prevSessions)
        => Compute(period, prev, sessions, prevSessions, null, null, null);

    public static AnalyticsPeriodReport Compute(
        AnalyticsPeriod period,
        AnalyticsPeriod prev,
        IReadOnlyList<AppSession> sessions,
        IReadOnlyList<AppSession> prevSessions,
        IReadOnlyList<DailySummary>? daily,
        IReadOnlyList<DailySummary>? prevDaily)
        => Compute(period, prev, sessions, prevSessions, daily, prevDaily, null);

    public static AnalyticsPeriodReport Compute(
        AnalyticsPeriod period,
        AnalyticsPeriod prev,
        IReadOnlyList<AppSession> sessions,
        IReadOnlyList<AppSession> prevSessions,
        IReadOnlyList<DailySummary>? daily,
        IReadOnlyList<DailySummary>? prevDaily,
        IReadOnlyList<HardwareSample>? hardwareSamples)
    {
        static double Minutes(AppSession s) =>
            s.EndTime.HasValue ? (s.EndTime.Value - s.StartTime).TotalMinutes : 0;

        var curMin = sessions.Sum(Minutes);
        var prevMin = prevSessions.Sum(Minutes);
        var curProd = sessions.Where(s => IsProductiveCategory(s.Category)).Sum(Minutes);
        var prevProd = prevSessions.Where(s => IsProductiveCategory(s.Category)).Sum(Minutes);
        var curFocus = sessions.Count(s => Minutes(s) >= 25 && IsProductiveCategory(s.Category));
        var prevFocus = prevSessions.Count(s => Minutes(s) >= 25 && IsProductiveCategory(s.Category));
        var curNight = sessions.Where(s => s.StartTime.Hour is >= 23 or < 6).Sum(Minutes);
        var prevNight = prevSessions.Where(s => s.StartTime.Hour is >= 23 or < 6).Sum(Minutes);
        var curProdPct = curMin > 0 ? curProd / curMin * 100 : 0;
        var prevProdPct = prevMin > 0 ? prevProd / prevMin * 100 : 0;

        var daysWithData = sessions.Select(s => s.StartTime.Date).Distinct().Count();

        var curExtra = AggregateDaily(daily);
        var prevExtra = AggregateDaily(prevDaily);
        var activeForInput = curMin > 0 ? curMin : curExtra.WebMinutes;

        var kpis = new List<KpiCard>
        {
            MakeKpi("active", "活跃时长", FormatHours(curMin), curMin, prevMin, higherIsBetter: true,
                "应用会话总时长"),
            MakeKpi("focus", "专注段", $"{curFocus} 次", curFocus, prevFocus, higherIsBetter: true,
                "≥25 分钟且属生产力分类"),
            MakeKpi("prod", "生产力占比", $"{curProdPct:F0}%", curProdPct, prevProdPct, higherIsBetter: true,
                "生产力时长 / 总时长"),
            MakeKpi("night", "夜间使用", FormatHours(curNight), curNight, prevNight, higherIsBetter: false,
                "23:00–06:00")
        };

        // 多源 KPI（有日汇总才追加，避免空数据干扰）
        if (curExtra.HasInputData)
        {
            kpis.Add(MakeKpi("keys", "键盘按键",
                $"{curExtra.TotalKeys:N0} 次", curExtra.TotalKeys, prevExtra.TotalKeys,
                higherIsBetter: true, "DailySummaries 合计"));
            kpis.Add(MakeKpi("input", "输入密度",
                $"{curExtra.KeysPerActiveHour:F0}/时", curExtra.KeysPerActiveHour, prevExtra.KeysPerActiveHour,
                higherIsBetter: true, "按键 ÷ 活跃小时（可辅助判断挂机）"));
        }

        if (curExtra.HasWebData)
        {
            kpis.Add(MakeKpi("web", "网页时长",
                FormatHours(curExtra.WebMinutes), curExtra.WebMinutes, prevExtra.WebMinutes,
                higherIsBetter: false, "有效浏览分钟（日汇总）"));
            kpis.Add(MakeKpi("web-share", "网页占比",
                $"{curExtra.WebSharePct:F0}%", curExtra.WebSharePct, prevExtra.WebSharePct,
                higherIsBetter: false, "网页分钟 ÷（应用活跃+网页）"));
        }

        if (curExtra.HasHardwareData)
        {
            if (curExtra.CpuLoadAvg is { } cpuAvg)
                kpis.Add(MakeKpi("cpu", "CPU 日均",
                    $"{cpuAvg:F0}%", cpuAvg, prevExtra.CpuLoadAvg ?? 0,
                    higherIsBetter: false, "HardwareSamples 日均（有样本天）"));
            if (curExtra.GpuLoadAvg is { } gpuAvg)
                kpis.Add(MakeKpi("gpu", "GPU 日均",
                    $"{gpuAvg:F0}%", gpuAvg, prevExtra.GpuLoadAvg ?? 0,
                    higherIsBetter: false, "HardwareSamples 日均"));
            if (curExtra.GpuTempMax is { } gpuTemp)
                kpis.Add(MakeKpi("gpu-temp", "GPU 峰值温度",
                    $"{gpuTemp:F0}°C", gpuTemp, prevExtra.GpuTempMax ?? 0,
                    higherIsBetter: false, "周期内日峰值最高值"));
        }

        // 日历：近 14 天（含期外，便于对比）
        var heatEnd = period.EndExclusive.Date.AddDays(-1);
        var heatStart = heatEnd.AddDays(-13);
        var byDay = sessions
            .GroupBy(s => s.StartTime.Date)
            .ToDictionary(g => g.Key, g => g.Sum(Minutes));

        var maxDayHours = byDay.Values.DefaultIfEmpty(0).Max() / 60.0;
        if (maxDayHours <= 0) maxDayHours = 1;

        var calendar = new List<DayHeatItem>();
        for (var d = heatStart; d <= heatEnd; d = d.AddDays(1))
        {
            var mins = byDay.TryGetValue(d, out var m) ? m : 0;
            var hours = mins / 60.0;
            var level = mins <= 0 ? 0
                : hours < 1 ? 1
                : hours < 3 ? 2
                : hours < 6 ? 3
                : 4;
            calendar.Add(new DayHeatItem(
                d,
                DayName(d.DayOfWeek),
                d.ToString("M/d"),
                hours,
                level,
                d >= period.Start.Date && d < period.EndExclusive.Date));
        }

        // 24h
        var byHour = sessions
            .GroupBy(s => s.StartTime.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(Minutes));
        var maxHour = byHour.Values.DefaultIfEmpty(0).Max();
        if (maxHour <= 0) maxHour = 1;
        var hourHeat = new List<HourHeatItem>();
        for (var h = 0; h < 24; h++)
        {
            var mins = byHour.TryGetValue(h, out var hm) ? hm : 0;
            var level = mins <= 0 ? 0 : (int)Math.Ceiling(mins / maxHour * 4.0);
            hourHeat.Add(new HourHeatItem(h, $"{h:00}", mins, Math.Clamp(level, 0, 4)));
        }

        // Top 变化应用
        var curApp = sessions.GroupBy(s => s.ProcessName)
            .ToDictionary(g => g.Key, g => g.Sum(Minutes));
        var prevApp = prevSessions.GroupBy(s => s.ProcessName)
            .ToDictionary(g => g.Key, g => g.Sum(Minutes));
        var allApps = curApp.Keys.Union(prevApp.Keys, StringComparer.OrdinalIgnoreCase);
        var topChanges = allApps
            .Select(name =>
            {
                var c = curApp.GetValueOrDefault(name, 0);
                var p = prevApp.GetValueOrDefault(name, 0);
                var delta = c - p;
                var dir = Math.Abs(delta) < 5 ? "flat" : delta > 0 ? "up" : "down";
                var pct = p > 0 ? delta / p * 100 : (c > 0 ? 100 : 0);
                return new TopChangeItem(
                    name, "app", c, p,
                    $"{(pct >= 0 ? "+" : "")}{pct:F0}%", dir);
            })
            .Where(x => Math.Abs(x.CurrentMinutes - x.PreviousMinutes) >= 15)
            .OrderByDescending(x => Math.Abs(x.CurrentMinutes - x.PreviousMinutes))
            .Take(5)
            .ToList();

        // 分类构成
        var totalForShare = sessions.Sum(Minutes);
        var categoryShares = sessions
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Category) ? "未分类" : s.Category!)
            .Select(g =>
            {
                var mins = g.Sum(Minutes);
                var pct = totalForShare > 0 ? mins / totalForShare * 100 : 0;
                return new CategoryShareItem(g.Key, mins, pct);
            })
            .Where(x => x.Minutes >= 5)
            .OrderByDescending(x => x.Minutes)
            .Take(8)
            .ToList();

        var extra = MergeExtra(curExtra, activeForInput);
        if (!extra.HasInputData && !extra.HasWebData && !extra.HasHardwareData)
            extra = null;
        var insights = BuildInsights(
            curMin, prevMin, curProdPct, prevProdPct, curFocus, prevFocus,
            curNight, sessions, daysWithData, period, extra, categoryShares);

        var hardware = hardwareSamples is { Count: > 0 }
            ? HardwareAnalyticsEngine.Build(sessions, hardwareSamples)
            : null;

        if (hardware is { HasData: true })
        {
            insights.AddRange(BuildHardwareInsights(hardware));
            insights = insights
                .OrderBy(i => i.Severity == "warning" ? 0 : i.Severity == "success" ? 2 : 1)
                .ToList();
        }

        var hasEnough = daysWithData >= 2 && curMin >= 30;

        return new AnalyticsPeriodReport(
            period, prev, kpis, calendar, hourHeat, topChanges, categoryShares, insights,
            extra, hardware, daysWithData, hasEnough);
    }

    private static List<AnalyticsInsight> BuildHardwareInsights(HardwareAnalyticsReport hardware)
    {
        var list = new List<AnalyticsInsight>();
        var topGpu = hardware.AppLoads.FirstOrDefault(a => a.GpuLoadAvg is >= 45);
        if (topGpu is not null)
            list.Add(new AnalyticsInsight("gpu-app", "info", "高 GPU 应用",
                $"{topGpu.DisplayName} 前台重叠时段 GPU 均约 {topGpu.GpuLoadAvg:F0}%（{topGpu.MinutesText}，样本 {topGpu.SampleCount}）。", null));

        var topCpu = hardware.AppLoads
            .Where(a => a.CpuLoadAvg.HasValue)
            .OrderByDescending(a => a.CpuLoadAvg)
            .FirstOrDefault();
        if (topCpu is { CpuLoadAvg: >= 70 })
            list.Add(new AnalyticsInsight("cpu-app", "info", "高 CPU 应用",
                $"{topCpu.DisplayName} 重叠时段 CPU 均约 {topCpu.CpuLoadAvg:F0}%。", null));

        if (hardware.GpuTempMax is >= 85)
            list.Add(new AnalyticsInsight("gpu-hot-sample", "warning", "样本内 GPU 高温",
                $"周期分钟样本 GPU 峰值 {hardware.GpuTempMax:F0}°C，建议关注散热与连续高负载时长。", null));

        return list;
    }

    /// <summary>从 DailySummaries 列表聚合多源指标（无表时返回全空）。</summary>
    public static AnalyticsExtraMetrics AggregateDaily(IReadOnlyList<DailySummary>? daily)
    {
        if (daily == null || daily.Count == 0)
        {
            return new AnalyticsExtraMetrics(
                0, 0, 0, 0, 0, 0,
                null, null, null, null, null,
                false, false, false);
        }

        double Sum(Func<DailySummary, double> f) => daily.Sum(f);

        var keys = Sum(d => d.TotalKeys);
        var clicks = Sum(d => d.TotalMouseClicks);
        var webMin = Sum(d => d.WebMinutes
            ?? (d.WebActiveDurationTicks > 0 ? TimeSpan.FromTicks(d.WebActiveDurationTicks).TotalMinutes : 0));
        var webPages = Sum(d => d.WebPages);

        var cpuAvgs = daily.Where(d => d.CpuLoadAvg.HasValue).Select(d => d.CpuLoadAvg!.Value).ToList();
        var cpuP95s = daily.Where(d => d.CpuLoadP95.HasValue).Select(d => d.CpuLoadP95!.Value).ToList();
        var gpuAvgs = daily.Where(d => d.GpuLoadAvg.HasValue).Select(d => d.GpuLoadAvg!.Value).ToList();
        var gpuTemps = daily.Where(d => d.GpuTempMax.HasValue).Select(d => d.GpuTempMax!.Value).ToList();
        var memAvgs = daily.Where(d => d.MemLoadAvg.HasValue).Select(d => d.MemLoadAvg!.Value).ToList();

        var hasInput = keys > 0 || clicks > 0;
        var hasWeb = webMin > 0 || webPages > 0;
        var hasHw = cpuAvgs.Count > 0 || gpuAvgs.Count > 0 || gpuTemps.Count > 0;

        var activeMinutes = Sum(d => d.ActiveMinutes);
        // 今日未入库时 ActiveMinutes 可能为 0，键盘密度用按键总量兜底展示在 MergeExtra
        var keysPerHour = activeMinutes > 0 ? keys / (activeMinutes / 60.0) : 0;
        var webShare = activeMinutes + webMin > 0 ? webMin / (activeMinutes + webMin) * 100 : 0;

        return new AnalyticsExtraMetrics(
            keys, clicks, keysPerHour, webMin, webPages, webShare,
            cpuAvgs.Count > 0 ? cpuAvgs.Average() : null,
            cpuP95s.Count > 0 ? cpuP95s.Max() : null,
            gpuAvgs.Count > 0 ? gpuAvgs.Average() : null,
            gpuTemps.Count > 0 ? gpuTemps.Max() : null,
            memAvgs.Count > 0 ? memAvgs.Average() : null,
            hasInput, hasWeb, hasHw);
    }

    /// <summary>应用会话活跃时长更准确时，用其重算输入密度/网页占比。</summary>
    private static AnalyticsExtraMetrics MergeExtra(AnalyticsExtraMetrics extra, double sessionActiveMinutes)
    {
        if (!extra.HasInputData && !extra.HasWebData) return extra;

        var active = sessionActiveMinutes > 0 ? sessionActiveMinutes : 0;
        var keysPerHour = active > 0 ? extra.TotalKeys / (active / 60.0) : extra.KeysPerActiveHour;
        var webShare = active + extra.WebMinutes > 0
            ? extra.WebMinutes / (active + extra.WebMinutes) * 100
            : extra.WebSharePct;

        return extra with
        {
            KeysPerActiveHour = keysPerHour,
            WebSharePct = webShare
        };
    }

    private static List<AnalyticsInsight> BuildInsights(
        double curMin, double prevMin,
        double curProdPct, double prevProdPct,
        int curFocus, int prevFocus,
        double curNight,
        IReadOnlyList<AppSession> sessions,
        int daysWithData,
        AnalyticsPeriod period,
        AnalyticsExtraMetrics? extra,
        IReadOnlyList<CategoryShareItem> categoryShares)
    {
        var list = new List<AnalyticsInsight>();

        if (daysWithData < 2 || curMin < 30)
        {
            list.Add(new AnalyticsInsight(
                "low-data", "info", "数据不足",
                $"本周期仅有 {daysWithData} 天、{FormatHours(curMin)} 活跃数据，洞察可能不准确。请确认监控已开启。",
                null));
            return list;
        }

        // 生产力环比
        if (prevProdPct > 0)
        {
            var drop = prevProdPct - curProdPct;
            if (drop >= 15)
                list.Add(new AnalyticsInsight("prod-drop", "warning", "生产力下降",
                    $"生产力占比从 {prevProdPct:F0}% 降至 {curProdPct:F0}%（-{drop:F0} 个百分点）。可到「构成」查看是哪些应用拖累。", "compose"));
            else if (curProdPct - prevProdPct >= 15)
                list.Add(new AnalyticsInsight("prod-up", "success", "生产力提升",
                    $"生产力占比提升至 {curProdPct:F0}%（+{curProdPct - prevProdPct:F0} 个百分点），继续保持。", null));
        }

        // 活跃环比
        if (prevMin > 60)
        {
            var change = (curMin - prevMin) / prevMin * 100;
            if (change <= -30)
                list.Add(new AnalyticsInsight("active-drop", "warning", "活跃时间明显减少",
                    $"总活跃 {FormatHours(curMin)}，较上一周期下降 {Math.Abs(change):F0}%。是否漏采或休息较多？", null));
            else if (change >= 50)
                list.Add(new AnalyticsInsight("active-up", "info", "活跃时间大幅增加",
                    $"总活跃 {FormatHours(curMin)}，较上一周期增加 {change:F0}%。", null));
        }

        // 夜间
        if (curNight >= 90)
            list.Add(new AnalyticsInsight("night", "warning", "夜间使用偏多",
                $"23:00–06:00 使用 {FormatHours(curNight)}，建议关注作息。", "rhythm"));

        // 专注
        if (prevFocus > 0 && curFocus < prevFocus * 0.5)
            list.Add(new AnalyticsInsight("focus-drop", "warning", "专注段减少",
                $"≥25 分钟专注段 {curFocus} 次（上期 {prevFocus} 次）。可尝试固定深度工作时段。", "rhythm"));
        else if (curFocus >= 10)
            list.Add(new AnalyticsInsight("focus-good", "success", "专注状态良好",
                $"本周期完成 {curFocus} 次专注段。", null));

        // 超长连续
        var longRun = sessions.Count(s =>
            s.EndTime.HasValue && (s.EndTime.Value - s.StartTime).TotalHours >= 3);
        if (longRun > 0)
            list.Add(new AnalyticsInsight("long-run", "warning", "注意休息",
                $"有 {longRun} 次连续使用超过 3 小时。", null));

        // 高效小时
        var topHour = sessions
            .Where(s => s.EndTime.HasValue)
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Min = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
            .OrderByDescending(x => x.Min)
            .FirstOrDefault();
        if (topHour != null && topHour.Min >= 60)
            list.Add(new AnalyticsInsight("peak-hour", "info", "高效时段",
                $"{topHour.Hour}:00–{topHour.Hour + 1}:00 累计 {FormatHours(topHour.Min)}，适合安排深度工作。", "rhythm"));

        // Top 应用
        var topApp = sessions
            .GroupBy(s => s.ProcessName)
            .Select(g => new { Name = g.Key, Min = g.Sum(s => s.EndTime.HasValue ? (s.EndTime.Value - s.StartTime).TotalMinutes : 0) })
            .OrderByDescending(x => x.Min)
            .FirstOrDefault();
        if (topApp != null && topApp.Min >= 60)
            list.Add(new AnalyticsInsight("top-app", "info", "最常用应用",
                $"{topApp.Name} 占用 {FormatHours(topApp.Min)}。", "compose"));

        // 构成
        var topCat = categoryShares.FirstOrDefault();
        if (topCat is { Percentage: >= 45 })
            list.Add(new AnalyticsInsight("cat-concentrate", "info", "时间高度集中",
                $"「{topCat.Category}」约占 {topCat.PercentText}（{topCat.MinutesText}）。若非目标分类，可到构成查看明细。", "compose"));

        // 多源：网页
        if (extra is { HasWebData: true } && extra.WebSharePct >= 40 && extra.WebMinutes >= 60)
            list.Add(new AnalyticsInsight("web-heavy", "info", "网页占比较高",
                $"有效网页浏览 {FormatHours(extra.WebMinutes)}，约占行为时间 {extra.WebSharePct:F0}%。可在网页访问页查看域名分布。", null));

        // 多源：输入密度偏低（挂机嫌疑）
        if (extra is { HasInputData: true } && curMin >= 120 && extra.KeysPerActiveHour > 0 && extra.KeysPerActiveHour < 80)
            list.Add(new AnalyticsInsight("low-input", "warning", "输入密度偏低",
                $"活跃 {FormatHours(curMin)}，但按键密度仅 {extra.KeysPerActiveHour:F0}/小时，会话可能含挂机/只读。", null));
        else if (extra is { HasInputData: true } && extra.KeysPerActiveHour >= 400)
            list.Add(new AnalyticsInsight("high-input", "info", "输入强度较高",
                $"按键密度约 {extra.KeysPerActiveHour:F0}/小时，节奏偏紧，注意穿插休息。", null));

        // 多源：硬件
        if (extra is { HasHardwareData: true })
        {
            if (extra.GpuTempMax is >= 85)
                list.Add(new AnalyticsInsight("gpu-hot", "warning", "GPU 温度偏高",
                    $"周期内 GPU 日峰值最高 {extra.GpuTempMax:F0}°C，长期高温可能影响稳定性与噪音。", null));
            if (extra.CpuLoadP95 is >= 90 || extra.CpuLoadAvg is >= 75)
                list.Add(new AnalyticsInsight("cpu-high", "info", "CPU 负载较高",
                    $"CPU 日均 {extra.CpuLoadAvg:F0}%、P95 {extra.CpuLoadP95:F0}%，可在硬件监控页对照运行中的应用。", null));
            if (extra.GpuLoadAvg is >= 70)
                list.Add(new AnalyticsInsight("gpu-high", "info", "GPU 持续高负载",
                    $"GPU 日均负载约 {extra.GpuLoadAvg:F0}%，创作/游戏/AI 推理会拉高此项。", null));
        }

        return list
            .OrderBy(i => i.Severity == "warning" ? 0 : i.Severity == "success" ? 2 : 1)
            .ToList();
    }

    private static KpiCard MakeKpi(
        string id, string name, string display,
        double cur, double prev, bool higherIsBetter, string hint)
    {
        if (prev <= 0 && cur <= 0)
            return new KpiCard(id, name, display, "—", "none", hint);

        if (prev <= 0)
            return new KpiCard(id, name, display, "新增", "up", hint);

        var pct = (cur - prev) / prev * 100;
        var dir = Math.Abs(pct) < 1 ? "flat" : pct > 0 ? "up" : "down";
        var arrow = dir == "flat" ? "→" : pct > 0 ? "▲" : "▼";
        var text = $"{arrow} {Math.Abs(pct):F0}%";
        return new KpiCard(id, name, display, text, dir, hint);
    }

    public static string FormatHours(double minutes)
    {
        if (minutes < 1) return "0分钟";
        var h = (int)(minutes / 60);
        var m = (int)(minutes % 60);
        if (h <= 0) return $"{m}分钟";
        if (m == 0) return $"{h}小时";
        return $"{h}小时{m}分";
    }

    private static string DayName(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        _ => "日"
    };
}
