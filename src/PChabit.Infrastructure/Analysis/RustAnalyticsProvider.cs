using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PChabit.Core.Entities;
using Serilog;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// 分析页计算层的 Rust 数据面（P3 收口块）。
///
/// 把「查 AppSessions/DailySummaries/HardwareSamples/WebSessions/ProcessNetworkUsages
/// + 全部聚合计算」整体交给 <c>pchabit-datad analytics</c>，宿主只做 DTO 映射。
/// 口径与 <see cref="AnalyticsEngine.Compute"/> + <see cref="HardwareAnalyticsEngine.Build"/> 一致。
///
/// 任何失败（找不到 exe / 超时 / 非零退出 / JSON 异常）都回退 C# 引擎，分析页不允许空白。
/// 开关 <c>PCHABIT_RUST_ANALYTICS=0</c> 可强制走 C# 路径。
/// </summary>
public static class RustAnalyticsProvider
{
    private const string ExeName = "pchabit-datad.exe";
    private const int TimeoutMs = 20000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string? _resolvedExe;
    private static bool _exeResolved;
    private static bool _loggedSuccess;
    private static bool _loggedFailure;

    public static bool Enabled =>
        (Environment.GetEnvironmentVariable("PCHABIT_RUST_ANALYTICS") ?? "").Trim() != "0";

    public static async Task<AnalyticsPeriodReport?> TryBuildAsync(
        AnalyticsPeriod period, CancellationToken ct = default)
    {
        if (!Enabled) return null;

        var exe = ResolveExe();
        if (exe == null)
        {
            WarnOnce("未找到 pchabit-datad.exe，分析页回退 C# 引擎");
            return null;
        }

        var previous = period.Previous();
        var request = new AnalyticsRequest
        {
            CurStart = Fmt(period.Start),
            CurEnd = Fmt(period.EndExclusive),
            PrevStart = Fmt(previous.Start),
            PrevEnd = Fmt(previous.EndExclusive),
            Label = period.Label,
            PrevLabel = previous.Label
        };

        try
        {
            var dbOverride = Environment.GetEnvironmentVariable("PCHABIT_DATAD_DB");
            var args = string.IsNullOrWhiteSpace(dbOverride)
                ? "analytics"
                : $"analytics --db \"{dbOverride}\"";

            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                WarnOnce("pchabit-datad 启动失败，分析页回退 C# 引擎");
                return null;
            }

            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            await proc.StandardInput.FlushAsync();
            proc.StandardInput.Close();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            var ioTask = Task.WhenAll(stdoutTask, stderrTask);
            var finished = await Task.WhenAny(ioTask, Task.Delay(TimeoutMs, ct));

            if (finished != ioTask)
            {
                try { proc.Kill(true); } catch { /* 已在退出 */ }
                WarnOnce($"pchabit-datad(analytics) 超过 {TimeoutMs} ms 未应答，分析页回退 C# 引擎");
                return null;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (string.IsNullOrWhiteSpace(stdout))
            {
                WarnOnce($"pchabit-datad(analytics) 无输出，分析页回退 C# 引擎：{Trim(stderr)}");
                return null;
            }

            var resp = JsonSerializer.Deserialize<AnalyticsResponse>(stdout, JsonOptions);
            if (resp == null || !resp.Ok)
            {
                WarnOnce($"pchabit-datad(analytics) 应答不合法，分析页回退 C# 引擎：{Trim(stderr)}");
                return null;
            }

            var report = ToReport(period, previous, resp);
            if (!_loggedSuccess)
            {
                _loggedSuccess = true;
                Log.Information(
                    "分析页已改用 Rust 数据面（pchabit-datad）：{Kpis} 个 KPI / {Shares} 个分类 / 洞察 {Insights} 条 / 内核 {Ms:F1} ms",
                    report.Kpis.Count, report.CategoryShares.Count, report.Insights.Count, resp.Ms);
            }
            return report;
        }
        catch (Exception ex)
        {
            WarnOnce($"pchabit-datad(analytics) 调用异常，分析页回退 C# 引擎：{ex.GetType().Name} {ex.Message}");
            return null;
        }
    }

    private static AnalyticsPeriodReport ToReport(AnalyticsPeriod period, AnalyticsPeriod previous, AnalyticsResponse r)
    {
        var kpis = (r.Kpis ?? new()).Select(k => new KpiCard(
            k.Id ?? "", k.Name ?? "", k.Value ?? "", k.DeltaText ?? "—", k.DeltaDirection ?? "none", k.Hint ?? "")).ToList();

        var calendar = (r.CalendarDays ?? new()).Select(d =>
        {
            // 日期标签按当前区域设置生成，避免 Rust 侧写死分隔符导致与 C# 引擎不一致
            var date = ParseDate(d.Date);
            return new DayHeatItem(date, DayName(date.DayOfWeek), date.ToString("M/d"),
                d.Hours, d.IntensityLevel, d.InPeriod);
        }).ToList();

        var hourHeat = (r.HourHeat ?? new()).Select(h => new HourHeatItem(h.Hour, h.Label ?? $"{h.Hour:00}", h.Minutes, h.IntensityLevel)).ToList();

        var topChanges = (r.TopChanges ?? new()).Select(t => new TopChangeItem(
            t.Name ?? "", t.Kind ?? "app", t.CurrentMinutes, t.PreviousMinutes, t.DeltaText ?? "", t.Direction ?? "flat")).ToList();

        var shares = (r.CategoryShares ?? new()).Select(c => new CategoryShareItem(c.Category ?? "", c.Minutes, c.Percentage)).ToList();
        var webShares = (r.WebCategoryShares ?? new()).Select(c => new CategoryShareItem(c.Category ?? "", c.Minutes, c.Percentage)).ToList();

        var insights = (r.Insights ?? new()).Select(i => new AnalyticsInsight(
            i.Id ?? "", i.Severity ?? "info", i.Title ?? "", i.Message ?? "", i.ActionKey)).ToList();

        AnalyticsExtraMetrics? extra = null;
        if (r.Extra is { } e)
        {
            extra = new AnalyticsExtraMetrics(
                e.TotalKeys, e.TotalClicks, e.KeysPerActiveHour, e.WebMinutes, e.WebPages, e.WebSharePct,
                e.CpuLoadAvg, e.CpuLoadP95, e.GpuLoadAvg, e.GpuTempMax, e.MemLoadAvg,
                e.HasInputData, e.HasWebData, e.HasHardwareData,
                e.NetBytesUp, e.NetBytesDown, e.HasNetData, e.SearchCount);
        }

        HardwareAnalyticsReport? hardware = null;
        if (r.Hardware is { HasData: true } hw)
        {
            var loads = (hw.AppLoads ?? new()).Select(a => new AppHardwareLoad(
                a.ProcessName ?? "", a.DisplayName ?? "", a.OverlapMinutes,
                a.CpuLoadAvg, a.CpuLoadP95, a.GpuLoadAvg, a.GpuTempMax, a.SampleCount)).ToList();

            var hours = (hw.Hours ?? new()).Select(h => new HardwareHourPoint(
                h.Hour, h.Label ?? $"{h.Hour:00}", h.CpuLoadAvg, h.GpuLoadAvg, h.GpuTempAvg, h.SampleCount)).ToList();

            hardware = new HardwareAnalyticsReport(
                loads, hours, hw.CpuLoadAvg, hw.CpuLoadP95, hw.GpuLoadAvg, hw.GpuTempMax, hw.MemLoadAvg,
                hw.SampleMinutes, hw.SampleDays, true);
        }

        return new AnalyticsPeriodReport(
            period, previous, kpis, calendar, hourHeat, topChanges, shares, insights,
            extra, hardware, r.DayCountWithData, r.HasEnoughData,
            r.HourWebMinutes ?? Enumerable.Repeat(0.0, 24).ToArray(),
            r.HourNetDownMb ?? Enumerable.Repeat(0.0, 24).ToArray(),
            webShares);
    }

    private static DateTime ParseDate(string? iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.MinValue;

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

    private static string Fmt(DateTime dt) =>
        dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static string? ResolveExe()
    {
        if (_exeResolved) return _resolvedExe;
        _exeResolved = true;

        var env = Environment.GetEnvironmentVariable("PCHABIT_DATAD_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return _resolvedExe = env;

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, ExeName),
            Path.Combine(AppContext.BaseDirectory, "rust", ExeName),
            Path.Combine(AppContext.BaseDirectory, "datad", ExeName)
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "rust", "pchabit-datad", "target", "release", ExeName));

        foreach (var c in candidates)
            if (File.Exists(c)) return _resolvedExe = c;

        return _resolvedExe = null;
    }

    private static void WarnOnce(string message)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        Log.Warning(message);
    }

    private static string Trim(string? s) =>
        string.IsNullOrEmpty(s) ? "(空)" : (s.Length > 200 ? s[..200] + "…" : s);

    // ==================== 协议 DTO ====================

    private sealed class AnalyticsRequest
    {
        [JsonPropertyName("curStart")] public string CurStart { get; set; } = "";
        [JsonPropertyName("curEnd")] public string CurEnd { get; set; } = "";
        [JsonPropertyName("prevStart")] public string PrevStart { get; set; } = "";
        [JsonPropertyName("prevEnd")] public string PrevEnd { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("prevLabel")] public string PrevLabel { get; set; } = "";
    }

    private sealed class AnalyticsResponse
    {
        public bool Ok { get; set; }
        public double Ms { get; set; }
        public int DayCountWithData { get; set; }
        public bool HasEnoughData { get; set; }
        public List<KpiDto>? Kpis { get; set; }
        public List<DayDto>? CalendarDays { get; set; }
        public List<HourHeatDto>? HourHeat { get; set; }
        public List<ChangeDto>? TopChanges { get; set; }
        public List<ShareDto>? CategoryShares { get; set; }
        public List<ShareDto>? WebCategoryShares { get; set; }
        public List<InsightDto>? Insights { get; set; }
        public ExtraDto? Extra { get; set; }
        public HardwareDto? Hardware { get; set; }
        public double[]? HourWebMinutes { get; set; }
        public double[]? HourNetDownMb { get; set; }
    }

    private sealed class KpiDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Value { get; set; }
        public string? DeltaText { get; set; }
        public string? DeltaDirection { get; set; }
        public string? Hint { get; set; }
    }

    private sealed class DayDto
    {
        public string? Date { get; set; }
        public string? DayLabel { get; set; }
        public string? DateLabel { get; set; }
        public double Hours { get; set; }
        public int IntensityLevel { get; set; }
        public bool InPeriod { get; set; }
    }

    private sealed class HourHeatDto
    {
        public int Hour { get; set; }
        public string? Label { get; set; }
        public double Minutes { get; set; }
        public int IntensityLevel { get; set; }
    }

    private sealed class ChangeDto
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public double CurrentMinutes { get; set; }
        public double PreviousMinutes { get; set; }
        public string? DeltaText { get; set; }
        public string? Direction { get; set; }
    }

    private sealed class ShareDto
    {
        public string? Category { get; set; }
        public double Minutes { get; set; }
        public double Percentage { get; set; }
    }

    private sealed class InsightDto
    {
        public string? Id { get; set; }
        public string? Severity { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? ActionKey { get; set; }
    }

    private sealed class ExtraDto
    {
        public double TotalKeys { get; set; }
        public double TotalClicks { get; set; }
        public double KeysPerActiveHour { get; set; }
        public double WebMinutes { get; set; }
        public double WebPages { get; set; }
        public double WebSharePct { get; set; }
        public double? CpuLoadAvg { get; set; }
        public double? CpuLoadP95 { get; set; }
        public double? GpuLoadAvg { get; set; }
        public double? GpuTempMax { get; set; }
        public double? MemLoadAvg { get; set; }
        public bool HasInputData { get; set; }
        public bool HasWebData { get; set; }
        public bool HasHardwareData { get; set; }
        public long? NetBytesUp { get; set; }
        public long? NetBytesDown { get; set; }
        public bool HasNetData { get; set; }
        public long SearchCount { get; set; }
    }

    private sealed class HardwareDto
    {
        public bool HasData { get; set; }
        public List<AppLoadDto>? AppLoads { get; set; }
        public List<HwHourDto>? Hours { get; set; }
        public double? CpuLoadAvg { get; set; }
        public double? CpuLoadP95 { get; set; }
        public double? GpuLoadAvg { get; set; }
        public double? GpuTempMax { get; set; }
        public double? MemLoadAvg { get; set; }
        public int SampleMinutes { get; set; }
        public int SampleDays { get; set; }
    }

    private sealed class AppLoadDto
    {
        public string? ProcessName { get; set; }
        public string? DisplayName { get; set; }
        public double OverlapMinutes { get; set; }
        public double? CpuLoadAvg { get; set; }
        public double? CpuLoadP95 { get; set; }
        public double? GpuLoadAvg { get; set; }
        public double? GpuTempMax { get; set; }
        public int SampleCount { get; set; }
    }

    private sealed class HwHourDto
    {
        public int Hour { get; set; }
        public string? Label { get; set; }
        public double CpuLoadAvg { get; set; }
        public double GpuLoadAvg { get; set; }
        public double? GpuTempAvg { get; set; }
        public int SampleCount { get; set; }
    }
}
