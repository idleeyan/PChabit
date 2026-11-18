using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace PChabit.Infrastructure.Formatters;

/// <summary>
/// Rust XLSX 写出器：把工作簿描述交给 <c>pchabit-datad xlsx</c>，拿回字节。
/// 一次性子进程 + 临时文件；任何失败返回 null，由调用方回退 EPPlus。
/// </summary>
internal static class RustXlsxWriter
{
    private const string ExeName = "pchabit-datad.exe";
    private const int TimeoutMs = 20000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string? _resolvedExe;
    private static bool _exeResolved;
    private static bool _loggedSuccess;
    private static bool _loggedFailure;

    public static bool Enabled =>
        (Environment.GetEnvironmentVariable("PCHABIT_RUST_XLSX") ?? "").Trim() != "0";

    /// <summary>写出并返回字节；失败返回 null。</summary>
    public static async Task<byte[]?> TryWriteAsync(List<XlsxSheet> sheets, CancellationToken ct = default)
    {
        if (!Enabled) return null;

        var exe = ResolveExe();
        if (exe == null)
        {
            WarnOnce("未找到 pchabit-datad.exe，Excel 导出回退 EPPlus");
            return null;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"pchabit-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            var request = new { file = tempFile, sheets };

            var psi = new ProcessStartInfo(exe, "xlsx")
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
                WarnOnce("pchabit-datad 启动失败，Excel 导出回退 EPPlus");
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
                WarnOnce($"pchabit-datad(xlsx) 超过 {TimeoutMs} ms 未应答，回退 EPPlus");
                return null;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                WarnOnce($"pchabit-datad(xlsx) 写出失败（exit={proc.ExitCode}）：{Trim(stderr)}");
                return null;
            }

            var resp = JsonSerializer.Deserialize<XlsxResponse>(stdout, JsonOptions);
            if (resp == null || !resp.Ok || !File.Exists(tempFile))
            {
                WarnOnce($"pchabit-datad(xlsx) 应答不合法或文件缺失：{Trim(stderr)}");
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(tempFile, ct);
            if (!_loggedSuccess)
            {
                _loggedSuccess = true;
                Log.Information(
                    "Excel 导出已改用 Rust 写出器：{Sheets} 个工作表 / {Rows} 行 / {Bytes} 字节 / 内核 {Ms:F1} ms",
                    sheets.Count, resp.Rows, bytes.Length, resp.Ms);
            }
            return bytes;
        }
        catch (Exception ex)
        {
            WarnOnce($"pchabit-datad(xlsx) 调用异常，回退 EPPlus：{ex.GetType().Name} {ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* 临时文件留给系统清理 */ }
        }
    }

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

    private sealed class XlsxResponse
    {
        public bool Ok { get; set; }
        public string? Path { get; set; }
        public long Bytes { get; set; }
        public int Rows { get; set; }
        public double Ms { get; set; }
    }
}
