using System.Globalization;
using PChabit.Core.Entities;

namespace PChabit.Infrastructure.Analysis;

/// <summary>单应用在会话重叠硬件样本上的负载画像。</summary>
public record AppHardwareLoad(
    string ProcessName,
    string DisplayName,
    double OverlapMinutes,
    double? CpuLoadAvg,
    double? CpuLoadP95,
    double? GpuLoadAvg,
    double? GpuTempMax,
    int SampleCount)
{
    public string CpuText => CpuLoadAvg is { } c ? $"{c:F0}%" : "—";
    public string GpuText => GpuLoadAvg is { } g ? $"{g:F0}%" : "—";
    public string TempText => GpuTempMax is { } t ? $"{t:F0}°C" : "—";
    public string MinutesText => AnalyticsEngine.FormatHours(OverlapMinutes);
}

/// <summary>硬件小时桶（用于节奏/硬件趋势）。</summary>
public record HardwareHourPoint(
    int Hour,
    string Label,
    double CpuLoadAvg,
    double GpuLoadAvg,
    double? GpuTempAvg,
    int SampleCount)
{
    public string CpuText => SampleCount <= 0 ? "—" : $"{CpuLoadAvg:F0}";
    public string GpuText => SampleCount <= 0 ? "—" : $"{GpuLoadAvg:F0}";
}

/// <summary>周期硬件分析汇总。</summary>
public record HardwareAnalyticsReport(
    IReadOnlyList<AppHardwareLoad> AppLoads,
    IReadOnlyList<HardwareHourPoint> Hours,
    double? CpuLoadAvg,
    double? CpuLoadP95,
    double? GpuLoadAvg,
    double? GpuTempMax,
    double? MemLoadAvg,
    int SampleMinutes,
    int SampleDays,
    bool HasData)
{
    public string SummaryText => HasData
        ? $"样本 {SampleDays} 天 · {SampleMinutes} 分钟　CPU 均 {CpuLoadAvg:F0}% / P95 {CpuLoadP95:F0}%　GPU 均 {GpuLoadAvg:F0}%　峰温 {GpuTempMax:F0}°C"
        : "";
}

/// <summary>
/// P2：应用会话 × 硬件分钟样本对齐。纯计算可单测。
/// 同一分钟多个前台候选时，归属重叠时长最长的进程（平手取先出现者）。
/// </summary>
public static class HardwareAnalyticsEngine
{
    public static HardwareAnalyticsReport Build(
        IReadOnlyList<AppSession> sessions,
        IReadOnlyList<HardwareSample> samples)
    {
        var parsed = ParseSamples(samples);
        if (parsed.Count == 0)
        {
            return new HardwareAnalyticsReport(
                Array.Empty<AppHardwareLoad>(), Array.Empty<HardwareHourPoint>(),
                null, null, null, null, null, 0, 0, false);
        }

        var appLoads = AlignAppLoads(sessions, parsed);
        var hours = BuildHourBuckets(parsed);
        var days = parsed.Select(s => s.Start.Date).Distinct().Count();

        var cpuVals = parsed.Where(s => s.CpuLoadAvg.HasValue).Select(s => s.CpuLoadAvg!.Value).ToList();
        var gpuVals = parsed.Where(s => s.GpuLoadAvg.HasValue).Select(s => s.GpuLoadAvg!.Value).ToList();
        var temps = parsed.Where(s => s.GpuTempMax.HasValue).Select(s => s.GpuTempMax!.Value).ToList();
        var memVals = parsed.Where(s => s.MemLoadAvg.HasValue).Select(s => s.MemLoadAvg!.Value).ToList();

        return new HardwareAnalyticsReport(
            appLoads,
            hours,
            cpuVals.Count > 0 ? Math.Round(cpuVals.Average(), 1) : null,
            cpuVals.Count > 0 ? Percentile95(cpuVals) : null,
            gpuVals.Count > 0 ? Math.Round(gpuVals.Average(), 1) : null,
            temps.Count > 0 ? Math.Round(temps.Max(), 1) : null,
            memVals.Count > 0 ? Math.Round(memVals.Average(), 1) : null,
            parsed.Count,
            days,
            true);
    }

    /// <summary>应用 × 分钟样本对齐。</summary>
    public static IReadOnlyList<AppHardwareLoad> AlignAppLoads(
        IReadOnlyList<AppSession> sessions,
        IReadOnlyList<ParsedSample> samples)
    {
        if (sessions.Count == 0 || samples.Count == 0)
            return Array.Empty<AppHardwareLoad>();

        var closed = sessions
            .Where(s => s.EndTime.HasValue && s.EndTime.Value > s.StartTime)
            .Select(s => (Session: s, Minutes: (s.EndTime!.Value - s.StartTime).TotalMinutes))
            .ToList();
        if (closed.Count == 0)
            return Array.Empty<AppHardwareLoad>();

        var displayNames = closed
            .GroupBy(s => s.Session.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Session.AppName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? g.Key,
                StringComparer.OrdinalIgnoreCase);

        var buckets = new Dictionary<string, List<(double Cpu, double Gpu, double? Temp)>>(StringComparer.OrdinalIgnoreCase);
        var minutes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in samples)
        {
            var sampleStart = sample.Start;
            var sampleEnd = sample.Start.AddMinutes(1);

            string? bestProcess = null;
            var bestOverlap = 0.0;

            foreach (var (session, _) in closed)
            {
                var sStart = session.StartTime;
                var sEnd = session.EndTime!.Value;
                var overlapStart = sStart > sampleStart ? sStart : sampleStart;
                var overlapEnd = sEnd < sampleEnd ? sEnd : sampleEnd;
                var overlap = (overlapEnd - overlapStart).TotalMinutes;
                if (overlap <= 0) continue;

                var process = session.ProcessName ?? "";
                if (process.Length == 0) continue;

                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    bestProcess = process;
                }
            }

            if (bestProcess is null) continue;

            if (!buckets.TryGetValue(bestProcess, out var list))
            {
                list = new List<(double, double, double?)>();
                buckets[bestProcess] = list;
            }

            if (sample.CpuLoadAvg.HasValue || sample.GpuLoadAvg.HasValue || sample.GpuTempMax.HasValue)
                list.Add((sample.CpuLoadAvg ?? 0, sample.GpuLoadAvg ?? 0, sample.GpuTempMax));

            minutes[bestProcess] = minutes.GetValueOrDefault(bestProcess) + bestOverlap;
        }

        var result = new List<AppHardwareLoad>();
        foreach (var (process, mins) in minutes)
        {
            buckets.TryGetValue(process, out var samplesForApp);
            samplesForApp ??= new List<(double Cpu, double Gpu, double? Temp)>();

            var cpuList = samplesForApp.Where(x => x.Cpu > 0 || samplesForApp.Count > 0 && x.Cpu >= 0)
                .Select(x => x.Cpu)
                .ToList();
            // 仅统计真正有传感器读数的样本（Cpu=0 且 Gpu=0 且 Temp=null 时跳过）
            var valid = samplesForApp
                .Where(x => x.Temp.HasValue || x.Cpu > 0 || x.Gpu > 0)
                .ToList();
            var cpu = valid.Count > 0 ? valid.Select(x => x.Cpu).Where(c => c >= 0).ToList() : new List<double>();
            var gpu = valid.Count > 0 ? valid.Select(x => x.Gpu).Where(g => g >= 0).ToList() : new List<double>();
            var temp = valid.Where(x => x.Temp.HasValue).Select(x => x.Temp!.Value).ToList();

            result.Add(new AppHardwareLoad(
                process,
                displayNames.TryGetValue(process, out var dn) ? dn : process,
                Math.Round(mins, 1),
                cpu.Count > 0 ? Math.Round(cpu.Average(), 1) : null,
                cpu.Count > 0 ? Percentile95(cpu) : null,
                gpu.Count > 0 ? Math.Round(gpu.Average(), 1) : null,
                temp.Count > 0 ? Math.Round(temp.Max(), 1) : null,
                valid.Count));
        }

        return result
            .OrderByDescending(x => x.GpuLoadAvg ?? 0)
            .ThenByDescending(x => x.CpuLoadAvg ?? 0)
            .ThenByDescending(x => x.OverlapMinutes)
            .Take(12)
            .ToList();
    }

    public static IReadOnlyList<HardwareHourPoint> BuildHourBuckets(IReadOnlyList<ParsedSample> samples)
    {
        var list = new List<HardwareHourPoint>(24);
        for (var h = 0; h < 24; h++)
        {
            var hourSamples = samples.Where(s => s.Start.Hour == h).ToList();
            if (hourSamples.Count == 0)
            {
                list.Add(new HardwareHourPoint(h, $"{h:00}", 0, 0, null, 0));
                continue;
            }

            var cpu = hourSamples.Where(s => s.CpuLoadAvg.HasValue).Select(s => s.CpuLoadAvg!.Value).ToList();
            var gpu = hourSamples.Where(s => s.GpuLoadAvg.HasValue).Select(s => s.GpuLoadAvg!.Value).ToList();
            var temp = hourSamples.Where(s => s.GpuTempMax.HasValue).Select(s => s.GpuTempMax!.Value).ToList();

            list.Add(new HardwareHourPoint(
                h, $"{h:00}",
                cpu.Count > 0 ? Math.Round(cpu.Average(), 1) : 0,
                gpu.Count > 0 ? Math.Round(gpu.Average(), 1) : 0,
                temp.Count > 0 ? Math.Round(temp.Average(), 1) : null,
                hourSamples.Count));
        }
        return list;
    }

    public sealed record ParsedSample(
        DateTime Start,
        double? CpuLoadAvg,
        double? GpuLoadAvg,
        double? GpuTempMax,
        double? MemLoadAvg);

    /// <summary>Timestamp "yyyy-MM-dd HH:mm" → ParsedSample；解析失败的行丢弃。</summary>
    public static List<ParsedSample> ParseSamples(IReadOnlyList<HardwareSample> samples)
    {
        var list = new List<ParsedSample>(samples.Count);
        foreach (var s in samples)
        {
            if (string.IsNullOrWhiteSpace(s.Timestamp)) continue;
            if (!DateTime.TryParseExact(s.Timestamp, "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                continue;
            list.Add(new ParsedSample(dt, s.CpuLoadAvg, s.GpuLoadAvg, s.GpuTempMax, s.MemLoadAvg));
        }
        return list;
    }

    private static double Percentile95(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var ordered = values.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling(0.95 * ordered.Count) - 1;
        if (index < 0) index = 0;
        return Math.Round(ordered[index], 1);
    }
}
