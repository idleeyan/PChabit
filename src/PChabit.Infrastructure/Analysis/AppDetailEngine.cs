using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Analysis;

/// <summary>单应用详情请求。</summary>
public record AppDetailRequest(
    AnalyticsPeriod Period,
    string ProcessName,
    string AppName,
    string Category);

/// <summary>单应用详情报告（纯 POCO）。</summary>
public record AppDetailReport(
    AnalyticsPeriod Period,
    string ProcessName,
    string AppName,
    string Category,
    double TotalMinutes,
    int Sessions,
    double AvgMinutes,
    double SharePct,
    double FocusMinutes,
    int FocusCount,
    string DeltaText,
    string DeltaDirection,
    double LongestSessionMinutes,
    IReadOnlyList<string> TopWindowTitles,
    IReadOnlyList<AppHourPoint> Hourly,
    IReadOnlyList<AppDayPoint> DailyTrend,
    double? KeyPresses,
    double? KeysPerHour,
    double? CpuLoadAvg,
    double? GpuLoadAvg,
    double? GpuTempMax,
    bool HasInput,
    bool HasHardware,
    bool HasData)
{
    public string TotalText => AnalyticsEngine.FormatHours(TotalMinutes);
    public string AvgText => AnalyticsEngine.FormatHours(AvgMinutes);
    public string FocusText => AnalyticsEngine.FormatHours(FocusMinutes);
    public string LongestText => AnalyticsEngine.FormatHours(LongestSessionMinutes);
    public string ShareText => $"{SharePct:F1}%";
    public string KpiFocusText => FocusCount > 0 ? $"{FocusText} · {FocusCount} 段" : "—";
}

public record AppDayPoint(
    string DateLabel,
    double Minutes,
    int IntensityLevel)
{
    public string MinutesText => Minutes < 0.05 ? "—" : AnalyticsEngine.FormatHours(Minutes);
}

/// <summary>
/// 单应用详情计算引擎：按进程归一化聚合会话，可叠加键盘/硬件切片。
/// 口径与 AppStatsEngine 一致；Compute 可单测。
/// </summary>
public static class AppDetailEngine
{
    public static async Task<AppDetailReport> BuildAsync(
        IDbContextFactory<PChabitDbContext> dbFactory,
        AnalyticsPeriod period,
        string processName,
        string appName,
        string category,
        string? categoryForFocus = null,
        CancellationToken ct = default)
    {
        var norm = AppStatsEngine.NormalizeProcessName(processName);
        var prev = period.Previous();

        List<AppSession> cur;
        List<AppSession> prevSessions;
        long keyPresses = 0;
        List<(double Cpu, double Gpu, double? Temp)> hwSamples = new();

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            cur = await QuerySessionsAsync(db, period, processName, norm, ct);
            prevSessions = await QuerySessionsAsync(db, prev, processName, norm, ct);

            try
            {
                var startKey = period.Start.ToString("yyyy-MM-dd");
                var endKey = period.EndExclusive.ToString("yyyy-MM-dd");
                keyPresses = await db.KeyboardSessions.AsNoTracking()
                    .Where(k => k.ProcessName != null
                                && (k.ProcessName == processName || k.ProcessName.ToLower() == norm)
                                && k.Date.CompareTo(startKey) >= 0
                                && k.Date.CompareTo(endKey) < 0)
                    .SumAsync(k => (long)k.TotalKeyPresses, ct);
            }
            catch
            {
                keyPresses = 0;
            }

            try
            {
                var hwStart = period.Start.ToString("yyyy-MM-dd HH:mm");
                var hwEnd = period.EndExclusive.ToString("yyyy-MM-dd HH:mm");
                var samples = await db.HardwareSamples.AsNoTracking()
                    .Where(s => s.Timestamp.CompareTo(hwStart) >= 0 && s.Timestamp.CompareTo(hwEnd) < 0)
                    .Select(s => new HardwareSample
                    {
                        Timestamp = s.Timestamp,
                        CpuLoadAvg = s.CpuLoadAvg,
                        GpuLoadAvg = s.GpuLoadAvg,
                        GpuTempMax = s.GpuTempMax
                    })
                    .ToListAsync(ct);

                if (samples.Count > 0 && cur.Count > 0)
                {
                    // 复用对齐：只取该进程结果
                    var all = HardwareAnalyticsEngine.Build(cur, samples);
                    var mine = all.AppLoads.FirstOrDefault(a =>
                        string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(AppStatsEngine.NormalizeProcessName(a.ProcessName), norm, StringComparison.Ordinal));
                    if (mine != null)
                    {
                        // 重算该进程自己的样本均值：按会话重叠分钟归属（与 Align 一致）
                        hwSamples = CollectProcessSamples(cur, samples, norm, processName);
                    }
                }
            }
            catch
            {
                hwSamples = new();
            }
        }

        var focusCat = categoryForFocus ?? category;
        return Compute(period, prev, cur, prevSessions, processName, appName, category,
            focusCat, keyPresses > 0 ? keyPresses : null, hwSamples);
    }

    private static async Task<List<AppSession>> QuerySessionsAsync(
        PChabitDbContext db, AnalyticsPeriod period, string processName, string norm, CancellationToken ct)
    {
        // SQLite 下 ToLower 在部分版本可译；再加内存二次过滤保险
        var list = await db.AppSessions.AsNoTracking()
            .Where(s => s.StartTime >= period.Start && s.StartTime < period.EndExclusive)
            .Select(s => new AppSession
            {
                Id = s.Id,
                ProcessName = s.ProcessName,
                AppName = s.AppName,
                Category = s.Category,
                WindowTitle = s.WindowTitle,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                InputEventCount = s.InputEventCount
            })
            .ToListAsync(ct);

        return list.Where(s =>
        {
            var p = s.ProcessName ?? "";
            return string.Equals(p, processName, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(AppStatsEngine.NormalizeProcessName(p), norm, StringComparison.Ordinal);
        }).ToList();
    }

    /// <summary>从分钟样本中收集与该进程会话重叠的样本（最长重叠归属，与 HardwareAnalytics 一致）。</summary>
    public static List<(double Cpu, double Gpu, double? Temp)> CollectProcessSamples(
        IReadOnlyList<AppSession> sessions,
        IReadOnlyList<HardwareSample> samples,
        string norm,
        string processName)
    {
        var parsed = HardwareAnalyticsEngine.ParseSamples(samples);
        var closed = sessions
            .Where(s => s.EndTime.HasValue && s.EndTime.Value > s.StartTime)
            .ToList();
        var result = new List<(double, double, double?)>();

        foreach (var sample in parsed)
        {
            var sampleStart = sample.Start;
            var sampleEnd = sample.Start.AddMinutes(1);
            string? best = null;
            var bestOverlap = 0.0;

            foreach (var s in closed)
            {
                var overlapStart = s.StartTime > sampleStart ? s.StartTime : sampleStart;
                var overlapEnd = s.EndTime!.Value < sampleEnd ? s.EndTime.Value : sampleEnd;
                var overlap = (overlapEnd - overlapStart).TotalMinutes;
                if (overlap <= 0) continue;
                var p = s.ProcessName ?? "";
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    best = p;
                }
            }

            if (best is null) continue;
            var isTarget = string.Equals(best, processName, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(AppStatsEngine.NormalizeProcessName(best), norm, StringComparison.Ordinal);
            if (!isTarget) continue;
            if (sample.CpuLoadAvg.HasValue || sample.GpuLoadAvg.HasValue || sample.GpuTempMax.HasValue)
                result.Add((sample.CpuLoadAvg ?? 0, sample.GpuLoadAvg ?? 0, sample.GpuTempMax));
        }

        return result;
    }

    public static AppDetailReport Compute(
        AnalyticsPeriod period,
        AnalyticsPeriod previous,
        IReadOnlyList<AppSession> sessions,
        IReadOnlyList<AppSession> prevSessions,
        string processName,
        string appName,
        string category,
        string categoryForFocus,
        double? keyPresses,
        IReadOnlyList<(double Cpu, double Gpu, double? Temp)>? hardwareSamples)
    {
        static double MinutesOf(AppSession s) =>
            s.EndTime.HasValue ? (s.EndTime.Value - s.StartTime).TotalMinutes : 0;

        var cur = sessions.ToList();
        var prev = prevSessions.ToList();
        var total = cur.Sum(MinutesOf);
        var prevTotal = prev.Sum(MinutesOf);
        var sessionCount = cur.Count;
        var avg = sessionCount > 0 ? total / sessionCount : 0;
        var focusSessions = cur.Where(s => MinutesOf(s) >= 25 && AnalyticsEngine.IsProductiveCategory(categoryForFocus)).ToList();
        var focusMin = focusSessions.Sum(MinutesOf);
        var longest = cur.Count > 0 ? cur.Max(MinutesOf) : 0;

        var dir = Math.Abs(total - prevTotal) < 5 ? "flat"
            : prevTotal <= 0 ? "up"
            : total > prevTotal ? "up" : "down";
        var deltaText = dir switch
        {
            "flat" => "—",
            "up" when prevTotal <= 0 => "新增",
            "up" => $"+{(total - prevTotal) / prevTotal * 100:F0}%",
            _ => $"{(total - prevTotal) / prevTotal * 100:F0}%"
        };

        var titles = cur
            .Where(s => !string.IsNullOrWhiteSpace(s.WindowTitle))
            .GroupBy(s => s.WindowTitle.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Title = g.Key, Min = g.Sum(MinutesOf) })
            .OrderByDescending(x => x.Min)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(x => TruncateTitle(x.Title))
            .ToList();

        var maxHour = 1.0;
        var hourMins = new double[24];
        foreach (var s in cur)
        {
            if (!s.EndTime.HasValue) continue;
            hourMins[s.StartTime.Hour] += MinutesOf(s);
        }
        maxHour = Math.Max(1, hourMins.Max());
        var hourly = new List<AppHourPoint>(24);
        for (var h = 0; h < 24; h++)
            hourly.Add(new AppHourPoint(h, $"{h:00}", hourMins[h]));

        // 日趋势：周期内每日
        var byDay = cur.Where(s => s.EndTime.HasValue)
            .GroupBy(s => s.StartTime.Date)
            .ToDictionary(g => g.Key, g => g.Sum(MinutesOf));
        var maxDay = Math.Max(1.0, byDay.Values.DefaultIfEmpty(0).DefaultIfEmpty(0).Max());
        var daily = new List<AppDayPoint>();
        for (var d = period.Start.Date; d < period.EndExclusive.Date; d = d.AddDays(1))
        {
            var mins = byDay.TryGetValue(d, out var m) ? m : 0;
            var level = mins <= 0 ? 0
                : mins < maxDay * 0.25 ? 1
                : mins < maxDay * 0.5 ? 2
                : mins < maxDay * 0.75 ? 3
                : 4;
            daily.Add(new AppDayPoint(d.ToString("M/d"), mins, level));
        }

        double? cpu = null, gpu = null, temp = null;
        var hasHw = hardwareSamples is { Count: > 0 };
        if (hasHw)
        {
            var cpuList = hardwareSamples!.Where(x => x.Cpu > 0 || x.Gpu > 0 || x.Temp.HasValue)
                .Select(x => x.Cpu).ToList();
            var gpuList = hardwareSamples!.Where(x => x.Cpu > 0 || x.Gpu > 0 || x.Temp.HasValue)
                .Select(x => x.Gpu).ToList();
            var tempList = hardwareSamples!.Where(x => x.Temp.HasValue).Select(x => x.Temp!.Value).ToList();
            if (cpuList.Count > 0) cpu = Math.Round(cpuList.Average(), 1);
            if (gpuList.Count > 0) gpu = Math.Round(gpuList.Average(), 1);
            if (tempList.Count > 0) temp = Math.Round(tempList.Max(), 1);
        }

        double? keysPerHour = null;
        if (keyPresses is > 0 && total > 0)
            keysPerHour = Math.Round(keyPresses.Value / (total / 60.0), 1);

        return new AppDetailReport(
            period,
            processName,
            string.IsNullOrWhiteSpace(appName) ? processName : appName,
            string.IsNullOrWhiteSpace(category) ? "未分类" : category,
            total,
            sessionCount,
            avg,
            0, // share 由调用方按排行占比填入可选；此处用会话内不计算全局
            focusMin,
            focusSessions.Count,
            deltaText,
            dir,
            longest,
            titles,
            hourly,
            daily,
            keyPresses,
            keysPerHour,
            cpu,
            gpu,
            temp,
            keyPresses is > 0,
            hasHw && (cpu.HasValue || gpu.HasValue || temp.HasValue),
            sessionCount > 0);
    }

    /// <summary>调用方补充全局占比。</summary>
    public static AppDetailReport WithShare(AppDetailReport report, double sharePct)
        => report with { SharePct = Math.Round(sharePct, 1) };

    private static string TruncateTitle(string title, int max = 48)
    {
        if (string.IsNullOrEmpty(title)) return title;
        return title.Length <= max ? title : title[..max] + "…";
    }
}
