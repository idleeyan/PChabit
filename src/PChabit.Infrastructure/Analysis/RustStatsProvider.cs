using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// 应用统计的 Rust 数据面（P3 第一块）。
///
/// 把「按周期查 AppSessions + 聚合」整体交给 <c>pchabit-datad.exe</c>：它持有 rusqlite，
/// 只读打开宿主同一个库，算完回一行 JSON。C# 只做 DTO 组装，不再执行 EF 查询与 LINQ 聚合。
///
/// 设计约束（刻意的）：
/// - 一次性子进程（stdin 请求 → stdout 应答 → 退出），不做常驻守护：一次统计页刷新一个进程，
///   成本约 20 ms，比维护管道生命周期、重连、健康检查便宜得多。真变成热路径再升级为常驻。
/// - 任何失败（找不到 exe / 超时 / 非零退出 / JSON 异常）都回退 <see cref="AppStatsEngine"/>，
///   统计页不允许出现空白。
/// - 开关 <c>PCHABIT_RUST_STATS=0</c> 可强制走 C# 路径。
/// </summary>
public static class RustStatsProvider
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

    /// <summary>是否启用 Rust 数据面（默认启用，设 PCHABIT_RUST_STATS=0 关闭）。</summary>
    public static bool Enabled =>
        (Environment.GetEnvironmentVariable("PCHABIT_RUST_STATS") ?? "").Trim() != "0";

    /// <summary>尝试用 Rust 计算报告；任何异常都返回 null，由调用方回退。</summary>
    public static async Task<AppStatsReport?> TryBuildAsync(
        AnalyticsPeriod period,
        string? categoryFilter,
        string? searchText,
        CancellationToken ct = default)
    {
        if (!Enabled) return null;

        var exe = ResolveExe();
        if (exe == null)
        {
            WarnOnce("未找到 pchabit-datad.exe，应用统计回退 C# 引擎（可用 PCHABIT_DATAD_PATH 指定路径）");
            return null;
        }

        var previous = period.Previous();
        var request = new StatsRequest
        {
            CurStart = Fmt(period.Start),
            CurEnd = Fmt(period.EndExclusive),
            PrevStart = Fmt(previous.Start),
            PrevEnd = Fmt(previous.EndExclusive),
            CatFilter = string.IsNullOrWhiteSpace(categoryFilter) ? null : categoryFilter,
            Search = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim()
        };

        try
        {
            var psi = new ProcessStartInfo(exe, "stats")
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
                WarnOnce("pchabit-datad 启动失败，应用统计回退 C# 引擎");
                return null;
            }

            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            await proc.StandardInput.FlushAsync();
            proc.StandardInput.Close();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            // 注意：Task.WhenAll 每次调用都会返回新的 Task 实例，必须先存下来再比较，
            // 否则 `WhenAny(...) != Task.WhenAll(...)` 恒为真，会被误判成超时。
            var ioTask = Task.WhenAll(stdoutTask, stderrTask);
            var finished = await Task.WhenAny(ioTask, Task.Delay(TimeoutMs, ct));

            if (finished != ioTask)
            {
                try { proc.Kill(true); } catch { /* 已在退出 */ }
                WarnOnce($"pchabit-datad 超过 {TimeoutMs} ms 未应答，应用统计回退 C# 引擎");
                return null;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (string.IsNullOrWhiteSpace(stdout))
            {
                WarnOnce($"pchabit-datad 无输出，应用统计回退 C# 引擎：{Trim(stderr)}");
                return null;
            }

            var resp = JsonSerializer.Deserialize<StatsResponse>(stdout, JsonOptions);
            if (resp == null || !resp.Ok)
            {
                WarnOnce($"pchabit-datad 应答不合法，应用统计回退 C# 引擎：{Trim(stderr)}");
                return null;
            }

            var report = ToReport(period, previous, resp);
            if (!_loggedSuccess)
            {
                _loggedSuccess = true;
                Log.Information(
                    "应用统计已改用 Rust 数据面（pchabit-datad）：{Rows} 行 / {Minutes:F1} 分钟 / 内核 {Ms:F1} ms",
                    report.Rows.Count, report.TotalMinutes, resp.Ms);
            }
            return report;
        }
        catch (Exception ex)
        {
            WarnOnce($"pchabit-datad 调用异常，应用统计回退 C# 引擎：{ex.GetType().Name} {ex.Message}");
            return null;
        }
    }

    private static AppStatsReport ToReport(AnalyticsPeriod period, AnalyticsPeriod previous, StatsResponse r)
    {
        var rows = (r.Rows ?? new List<RowDto>()).Select(x => new AppRankRow(
            x.ProcessName ?? "",
            x.AppName ?? "",
            x.Category ?? AppStatsEngine.UncategorizedName,
            x.CategoryColorHex ?? "#6B7280",
            x.DurationMinutes,
            x.Sessions,
            x.AvgDurationMinutes,
            x.Percentage,
            x.FocusMinutes,
            x.DeltaText ?? "—",
            x.DeltaDirection ?? "none")).ToList();

        var pie = (r.PieSlices ?? new List<PieDto>())
            .Select(x => new AppPieSlice(x.Name ?? "", x.Minutes, x.ColorHex ?? "#6B7280"))
            .ToList();

        var shares = (r.CategoryShares ?? new List<ShareDto>())
            .Select(x => new AppCategoryShare(x.Category ?? AppStatsEngine.UncategorizedName, x.Minutes, x.Percentage, x.ColorHex ?? "#6B7280"))
            .ToList();

        var hourly = (r.Hourly ?? new List<HourDto>())
            .Select(x => new AppHourPoint(x.Hour, x.Label ?? $"{x.Hour:00}:00", x.Minutes))
            .ToList();

        return new AppStatsReport(
            period, previous,
            r.TotalMinutes, r.ActiveApps,
            r.TopAppName ?? "无", r.TopAppMinutes,
            r.FocusSessions,
            rows, pie, shares, hourly);
    }

    /// <summary>与 EF 落库同格式（SQLite TEXT，7 位小数），保证与 StartTime 的字典序可比。</summary>
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

        // 开发机：从输出目录向上找 rust\pchabit-datad\target\release
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "rust", "pchabit-datad", "target", "release", ExeName));
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return _resolvedExe = c;
        }
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

    private sealed class StatsRequest
    {
        [JsonPropertyName("curStart")] public string CurStart { get; set; } = "";
        [JsonPropertyName("curEnd")] public string CurEnd { get; set; } = "";
        [JsonPropertyName("prevStart")] public string PrevStart { get; set; } = "";
        [JsonPropertyName("prevEnd")] public string PrevEnd { get; set; } = "";
        [JsonPropertyName("catFilter")] public string? CatFilter { get; set; }
        [JsonPropertyName("search")] public string? Search { get; set; }
    }

    private sealed class StatsResponse
    {
        public bool Ok { get; set; }
        public double TotalMinutes { get; set; }
        public int ActiveApps { get; set; }
        public string? TopAppName { get; set; }
        public double TopAppMinutes { get; set; }
        public int FocusSessions { get; set; }
        public double Ms { get; set; }
        public List<RowDto>? Rows { get; set; }
        public List<PieDto>? PieSlices { get; set; }
        public List<ShareDto>? CategoryShares { get; set; }
        public List<HourDto>? Hourly { get; set; }
    }

    private sealed class RowDto
    {
        public string? ProcessName { get; set; }
        public string? AppName { get; set; }
        public string? Category { get; set; }
        public string? CategoryColorHex { get; set; }
        public double DurationMinutes { get; set; }
        public int Sessions { get; set; }
        public double AvgDurationMinutes { get; set; }
        public double Percentage { get; set; }
        public double FocusMinutes { get; set; }
        public string? DeltaText { get; set; }
        public string? DeltaDirection { get; set; }
    }

    private sealed class PieDto
    {
        public string? Name { get; set; }
        public double Minutes { get; set; }
        public string? ColorHex { get; set; }
    }

    private sealed class ShareDto
    {
        public string? Category { get; set; }
        public double Minutes { get; set; }
        public double Percentage { get; set; }
        public string? ColorHex { get; set; }
    }

    private sealed class HourDto
    {
        public int Hour { get; set; }
        public string? Label { get; set; }
        public double Minutes { get; set; }
    }
}
