using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// 应用详情的 Rust 数据面（P3 第二块）：会话 + 键鼠切片 + 硬件归属一次算完。
///
/// 与 <see cref="RustStatsProvider"/> 同一套约束：一次性子进程、失败即回退、开关可关。
/// 口径对齐点：进程匹配（原名忽略大小写 / 归一化相等）、专注阈值 25 分钟、
/// 窗口标题 Top5（忽略大小写分组 + 48 字截断）、小时标签两位数字（不带 ":00"）、
/// 日趋势强度四档、硬件样本按「最长重叠会话」归属后再取均值/峰值。
/// </summary>
public static class RustDetailProvider
{
    private const string ExeName = "pchabit-datad.exe";
    private const int TimeoutMs = 15000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string? _resolvedExe;
    private static bool _exeResolved;
    private static bool _loggedSuccess;
    private static bool _loggedFailure;

    /// <summary>默认启用，<c>PCHABIT_RUST_DETAIL=0</c> 关闭。</summary>
    public static bool Enabled =>
        (Environment.GetEnvironmentVariable("PCHABIT_RUST_DETAIL") ?? "").Trim() != "0";

    public static async Task<AppDetailReport?> TryBuildAsync(
        AnalyticsPeriod period,
        string processName,
        string appName,
        string category,
        string? categoryForFocus,
        CancellationToken ct = default)
    {
        if (!Enabled) return null;

        var exe = ResolveExe();
        if (exe == null)
        {
            WarnOnce("未找到 pchabit-datad.exe，应用详情回退 C# 引擎");
            return null;
        }

        var previous = period.Previous();
        var request = new DetailRequest
        {
            CurStart = FmtSeconds(period.Start),
            CurEnd = FmtSeconds(period.EndExclusive),
            PrevStart = FmtSeconds(previous.Start),
            PrevEnd = FmtSeconds(previous.EndExclusive),
            DayStart = FmtDay(period.Start.Date),
            DayEnd = FmtDay(period.EndExclusive.Date),
            HwStart = FmtMinute(period.Start),
            HwEnd = FmtMinute(period.EndExclusive),
            ProcessName = processName,
            AppName = appName,
            Category = category,
            CatForFocus = string.IsNullOrWhiteSpace(categoryForFocus) ? null : categoryForFocus
        };

        try
        {
            var psi = new ProcessStartInfo(exe, "detail")
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
                WarnOnce("pchabit-datad 启动失败，应用详情回退 C# 引擎");
                return null;
            }

            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            await proc.StandardInput.FlushAsync();
            proc.StandardInput.Close();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            // Task.WhenAll 每次调用都是新实例，必须先存变量再比较（3.90.0 的坑）
            var ioTask = Task.WhenAll(stdoutTask, stderrTask);
            var finished = await Task.WhenAny(ioTask, Task.Delay(TimeoutMs, ct));
            if (finished != ioTask)
            {
                try { proc.Kill(true); } catch { /* 已在退出 */ }
                WarnOnce($"pchabit-datad(detail) 超过 {TimeoutMs} ms 未应答，回退 C# 引擎");
                return null;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (string.IsNullOrWhiteSpace(stdout))
            {
                WarnOnce($"pchabit-datad(detail) 无输出，回退 C# 引擎：{Trim(stderr)}");
                return null;
            }

            var resp = JsonSerializer.Deserialize<DetailResponse>(stdout, JsonOptions);
            if (resp == null || !resp.Ok)
            {
                WarnOnce($"pchabit-datad(detail) 应答不合法，回退 C# 引擎：{Trim(stderr)}");
                return null;
            }

            var report = ToReport(period, processName, resp);
            if (!_loggedSuccess)
            {
                _loggedSuccess = true;
                Log.Information(
                    "应用详情已改用 Rust 数据面（{App}）：{Sessions} 段 / {Minutes:F1} 分钟 / 内核 {Ms:F1} ms",
                    report.ProcessName, report.Sessions, report.TotalMinutes, resp.Ms);
            }
            return report;
        }
        catch (Exception ex)
        {
            WarnOnce($"pchabit-datad(detail) 调用异常，回退 C# 引擎：{ex.GetType().Name} {ex.Message}");
            return null;
        }
    }

    private static AppDetailReport ToReport(AnalyticsPeriod period, string processName, DetailResponse r) =>
        new(
            period,
            r.ProcessName ?? processName,
            r.AppName ?? processName,
            r.Category ?? AppStatsEngine.UncategorizedName,
            r.TotalMinutes,
            r.Sessions,
            r.AvgMinutes,
            0, // SharePct 由调用方按排行占比补
            r.FocusMinutes,
            r.FocusCount,
            r.DeltaText ?? "—",
            r.DeltaDirection ?? "none",
            r.LongestSessionMinutes,
            r.TopWindowTitles ?? new List<string>(),
            (r.Hourly ?? new List<HourDto>())
                .Select(h => new AppHourPoint(h.Hour, h.Label ?? $"{h.Hour:00}", h.Minutes)).ToList(),
            (r.DailyTrend ?? new List<DayDto>())
                .Select(d => new AppDayPoint(d.DateLabel ?? "", d.Minutes, d.IntensityLevel)).ToList(),
            r.KeyPresses, r.KeysPerHour,
            r.MouseClicks, r.MouseMoveDistance, r.MouseScrolls,
            r.CpuLoadAvg, r.GpuLoadAvg, r.GpuTempMax,
            r.HasInput, r.HasKeyMouse, r.HasHardware, r.HasData);

    private static string FmtSeconds(DateTime dt) =>
        dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static string FmtDay(DateTime dt) =>
        dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FmtMinute(DateTime dt) =>
        dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

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

    private sealed class DetailRequest
    {
        [JsonPropertyName("curStart")] public string CurStart { get; set; } = "";
        [JsonPropertyName("curEnd")] public string CurEnd { get; set; } = "";
        [JsonPropertyName("prevStart")] public string PrevStart { get; set; } = "";
        [JsonPropertyName("prevEnd")] public string PrevEnd { get; set; } = "";
        [JsonPropertyName("dayStart")] public string DayStart { get; set; } = "";
        [JsonPropertyName("dayEnd")] public string DayEnd { get; set; } = "";
        [JsonPropertyName("hwStart")] public string HwStart { get; set; } = "";
        [JsonPropertyName("hwEnd")] public string HwEnd { get; set; } = "";
        [JsonPropertyName("processName")] public string ProcessName { get; set; } = "";
        [JsonPropertyName("appName")] public string AppName { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("catForFocus")] public string? CatForFocus { get; set; }
    }

    private sealed class DetailResponse
    {
        public bool Ok { get; set; }
        public string? ProcessName { get; set; }
        public string? AppName { get; set; }
        public string? Category { get; set; }
        public double TotalMinutes { get; set; }
        public int Sessions { get; set; }
        public double AvgMinutes { get; set; }
        public double FocusMinutes { get; set; }
        public int FocusCount { get; set; }
        public string? DeltaText { get; set; }
        public string? DeltaDirection { get; set; }
        public double LongestSessionMinutes { get; set; }
        public List<string>? TopWindowTitles { get; set; }
        public List<HourDto>? Hourly { get; set; }
        public List<DayDto>? DailyTrend { get; set; }
        public double? KeyPresses { get; set; }
        public double? KeysPerHour { get; set; }
        public double? MouseClicks { get; set; }
        public double? MouseMoveDistance { get; set; }
        public double? MouseScrolls { get; set; }
        public double? CpuLoadAvg { get; set; }
        public double? GpuLoadAvg { get; set; }
        public double? GpuTempMax { get; set; }
        public bool HasInput { get; set; }
        public bool HasKeyMouse { get; set; }
        public bool HasHardware { get; set; }
        public bool HasData { get; set; }
        public double Ms { get; set; }
    }

    private sealed class HourDto
    {
        public int Hour { get; set; }
        public string? Label { get; set; }
        public double Minutes { get; set; }
    }

    private sealed class DayDto
    {
        public string? DateLabel { get; set; }
        public double Minutes { get; set; }
        public int IntensityLevel { get; set; }
    }
}
