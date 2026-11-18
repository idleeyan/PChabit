using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace PChabit.Core.Runtime;

/// <summary>
/// 与 Rust 采样内核（pchabit-netd）之间的命名管道客户端。
/// 协议为行式文本：一行命令 → 一行 JSON 应答；见 rust\pchabit-netd\src\pipe.rs。
///
/// 为什么放在 Core：内核从「只做网络采样」扩到 GPU 与前台窗口后，HardwareMonitor 与
/// Infrastructure 两个程序集都要用它，而管道服务端同一时刻只服务一个客户端
/// （serve 循环逐个 accept），全进程必须共用同一条连接。
/// </summary>
public sealed class DaemonPipe : IDisposable
{
    public const string PipeName = "pchabit-netd-v1";
    private const int ReadTimeoutMs = 2000;

    /// <summary>
    /// 进程内共享的单一连接。生命周期跟随进程；内核自带空闲退出，无需宿主显式 STOP。
    /// </summary>
    public static DaemonPipe Shared { get; } = new DaemonPipe();

    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Process? _owned;
    private bool _disposed;

    /// <summary>守护进程是否已连接（最近一次交互成功）。</summary>
    public bool IsConnected
    {
        get { lock (_gate) return _pipe is { IsConnected: true }; }
    }

    /// <summary>发送一条命令并取回应答；连接不可用时尝试重连/拉起，失败返回 null（不抛出）。</summary>
    public string? Send(string command)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (!EnsureConnected()) return null;

                    _writer!.WriteLine(command);
                    _writer.Flush();
                    var reply = ReadLineWithTimeout();
                    if (reply == null) throw new IOException("管道对端已关闭");
                    return reply;
                }
                catch
                {
                    // 超时/管道断开/对端退出：重建连接后重试一次，仍失败由调用方回退
                    DropConnection();
                }
            }
            return null;
        }
    }

    private string? ReadLineWithTimeout()
    {
        var reader = _reader!;
        var task = Task.Run(() => reader.ReadLine());
        if (!task.Wait(ReadTimeoutMs)) throw new TimeoutException();
        return task.Result;
    }

    private bool EnsureConnected()
    {
        if (_pipe is { IsConnected: true }) return true;

        DropConnection();

        var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        try
        {
            client.Connect(300);
        }
        catch (TimeoutException)
        {
            client.Dispose();
            // 首次失败时拉起内核进程（已有实例时内核会自行静默退出）
            if (!TryStartDaemon()) return false;
            client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(3000);
        }

        _pipe = client;
        _writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
        _reader = new StreamReader(client, new UTF8Encoding(false));
        return true;
    }

    private void DropConnection()
    {
        try { _reader?.Dispose(); } catch { /* 连接已坏，忽略 */ }
        try { _writer?.Dispose(); } catch { /* 连接已坏，忽略 */ }
        try { _pipe?.Dispose(); } catch { /* 连接已坏，忽略 */ }
        _reader = null;
        _writer = null;
        _pipe = null;
    }

    private bool TryStartDaemon()
    {
        if (_owned is { HasExited: false }) return true;

        var exe = ResolveDaemonPath();
        if (exe == null) return false;

        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty
            };
            psi.ArgumentList.Add("daemon");
            psi.ArgumentList.Add("--exclude-name");
            psi.ArgumentList.Add(Process.GetCurrentProcess().ProcessName);
            _owned = Process.Start(psi);
            return _owned != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解析守护进程路径：显式环境变量 → 发布目录 → 开发期 cargo 产物。
    /// </summary>
    private static string? ResolveDaemonPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("PCHABIT_NETD_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;

        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
                 {
                     Path.Combine(baseDir, "pchabit-netd.exe"),
                     Path.Combine(baseDir, "rust", "pchabit-netd.exe"),
                     Path.Combine(baseDir, "netd", "pchabit-netd.exe")
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        var dir = new DirectoryInfo(baseDir);
        for (var depth = 0; depth < 6 && dir != null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "rust", "pchabit-netd", "target", "release", "pchabit-netd.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_writer != null)
                {
                    _writer.Write("STOP\n");
                    _writer.Flush();
                    ReadLineWithTimeout();
                }
            }
            catch
            {
                // 停止指令失败不影响退出流程
            }
            DropConnection();
        }

        // 只回收自己拉起的进程，避免误杀外部实例
        try
        {
            if (_owned is { HasExited: false })
            {
                if (!_owned.WaitForExit(1000)) _owned.Kill(entireProcessTree: true);
            }
            _owned?.Dispose();
        }
        catch
        {
            // 回收失败不影响退出
        }
    }
}
