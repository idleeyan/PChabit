using System.Text.Json;
using Serilog;

namespace PChabit.HardwareMonitor.Hardware;

/// <summary>
/// Rust 采样内核接入（可选通道）。
///
/// 设计取舍：公开 API 一律不变，UI 与落库侧零改动。开启后本类的 <c>SampleOnce</c> 不再自己解析
/// 裸指针连接表，而是把「系统速率」推给 Rust 守护进程、再取回它算好的快照；本类退化为缓存与转换层。
///
/// 为什么切这一块：连接表枚举 + 逐连接 ESTATS + 进程名解析是 .NET 侧最贵的一段（实测 5.2 ms/拍），
/// 同口径 Rust 只要 0.8 ms/拍；而系统级速率仍以 LHM 为权威口径，所以由宿主每秒推给内核积分。
///
/// 开关：环境变量 PCHABIT_RUST_NETSAMPLER=1（默认关闭，便于随时回退到纯 C# 实现）。
/// </summary>
public sealed partial class ProcessNetworkMonitor
{
    private const string RustEnvVar = "PCHABIT_RUST_NETSAMPLER";

    private RustNetClient? _rust;
    private bool _rustChecked;

    private long _remoteSessionUp;
    private long _remoteSessionDown;
    private long _remoteTodayUp;
    private long _remoteTodayDown;
    private bool? _remoteEstats;

    private List<ProcessNetworkItem> _remoteByRate = new();
    private List<ProcessNetworkItem> _remoteByToday = new();
    private DateTime _remoteAttributionUtc = DateTime.MinValue;

    /// <summary>内核未就绪期间暂存的系统增量（连上后补推）。</summary>
    private long _sysCarryUp;
    private long _sysCarryDown;

    /// <summary>
    /// 是否启用 Rust 采样内核：默认启用，显式设 "0"/"false"/"off" 时回退到内置 C# 采样。
    /// 找不到或连不上内核时同样静默回退，不会影响功能。
    /// </summary>
    private static bool RustSamplerEnabled
    {
        get
        {
            var v = Environment.GetEnvironmentVariable(RustEnvVar);
            if (string.IsNullOrWhiteSpace(v)) return true;
            return !(v.Trim() == "0"
                     || v.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
                     || v.Trim().Equals("off", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>启动期按需接入 Rust 内核；连不上则静默回退到 C# 采样。</summary>
    private void EnsureRustSampler()
    {
        lock (_sync)
        {
            if (_rustChecked) return;
            _rustChecked = true;
        }

        if (!RustSamplerEnabled) return;

        // 与 GPU 探针共用同一条管道连接（服务端同一时刻只服务一个客户端）
        var client = RustNetClient.Shared;
        var health = client.Send("PING");
        if (health == null)
        {
            Log.Warning("Rust 采样内核不可用，回退到内置 C# 采样");
            return;
        }

        lock (_sync)
        {
            _rust = client;
        }
        Log.Information("已启用 Rust 采样内核（{Var}=1）", RustEnvVar);
    }

    private void StopRustSampler()
    {
        RustNetClient? client;
        lock (_sync)
        {
            client = _rust;
            _rust = null;
            _rustChecked = true;
        }
        // 连接为进程级共享（GPU 探针也在用），不在此处 Dispose；
        // 内核自带 90 秒空闲退出，宿主退出后自动回收。
        _ = client;
    }

    private bool RustActive
    {
        get { lock (_sync) return _rust != null; }
    }

    /// <summary>启动期回填种子时也需要内核在线，必要时提前接入（幂等）。</summary>
    private bool TryEnsureRustForSeed()
    {
        if (!RustSamplerEnabled) return false;
        EnsureRustSampler();
        lock (_sync) return _rust != null;
    }

    /// <summary>Rust 通道下的一拍：推系统速率 → 取快照 → 刷新缓存。</summary>
    private void SampleRemote()
    {
        RustNetClient? client;
        lock (_sync) client = _rust;
        if (client == null) return;

        var nowUtc = DateTime.UtcNow;
        double elapsedSec;
        lock (_sync)
        {
            elapsedSec = Math.Clamp((nowUtc - _lastSampleUtc).TotalSeconds, 0.05, 10.0);
            _lastSampleUtc = nowUtc;
        }

        // 系统增量：与内置实现同序——LHM getter 优先，缺失时退回网卡 IP Statistics 增量
        var upBps = NormalizeGetter(NetUpGetter?.Invoke());
        var downBps = NormalizeGetter(NetDownGetter?.Invoke());

        long sysUpDelta;
        long sysDownDelta;
        if (upBps < 0 || downBps < 0)
        {
            var (adapterUp, adapterDown) = SampleAdapterDeltas();
            sysUpDelta = upBps >= 0 ? (long)Math.Max(0, upBps * elapsedSec) : adapterUp;
            sysDownDelta = downBps >= 0 ? (long)Math.Max(0, downBps * elapsedSec) : adapterDown;
        }
        else
        {
            // 两个 getter 都有效时无需枚举网卡：这一段本身就要 1~4 ms。
            // 顺手清掉基线，将来回退时重建，避免把「跳过期间攒下的字节」一次性入账。
            lock (_sync) _adapterLast.Clear();
            sysUpDelta = (long)Math.Max(0, upBps * elapsedSec);
            sysDownDelta = (long)Math.Max(0, downBps * elapsedSec);
        }

        // 推「本拍增量」而不是速率：内核按自己的 1 秒节拍积分会和宿主错拍丢尖峰（实测低 15%）
        long carryUp;
        long carryDown;
        lock (_sync)
        {
            _sysCarryUp += sysUpDelta;
            _sysCarryDown += sysDownDelta;
            carryUp = _sysCarryUp;
            carryDown = _sysCarryDown;
            _sysCarryUp = 0;
            _sysCarryDown = 0;
        }

        if (client.Send($"SYS {carryUp} {carryDown}") == null)
        {
            // 内核尚未就绪（启动期 1~2 拍）：先攒着，连上后一次性补推，避免系统累计开头就少一截
            lock (_sync)
            {
                _sysCarryUp += carryUp;
                _sysCarryDown += carryDown;
            }
        }

        var reply = client.Send("SNAP 20");
        if (reply == null) return;

        try
        {
            ApplyRemoteSnapshot(reply);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "解析 Rust 采样内核快照失败，本拍保留上一份缓存");
        }
    }

    /// <summary>getter 值非法时返回 -1，交由调用方回退。</summary>
    private static double NormalizeGetter(float? value)
    {
        if (!value.HasValue) return -1;
        var v = value.Value;
        if (float.IsNaN(v) || float.IsInfinity(v) || v < 0) return -1;
        return v;
    }

    private void ApplyRemoteSnapshot(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var byRate = new List<ProcessNetworkItem>();
        if (root.TryGetProperty("rate", out var rateArr) && rateArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in rateArr.EnumerateArray())
            {
                byRate.Add(new ProcessNetworkItem
                {
                    ProcessId = e.TryGetProperty("p", out var p) ? p.GetInt32() : 0,
                    ProcessName = e.TryGetProperty("n", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    UpBytesPerSec = e.TryGetProperty("u", out var u) ? u.GetDouble() : 0,
                    DownBytesPerSec = e.TryGetProperty("d", out var d) ? d.GetDouble() : 0,
                    SessionUpBytes = e.TryGetProperty("su", out var su) ? su.GetInt64() : 0,
                    SessionDownBytes = e.TryGetProperty("sd", out var sd) ? sd.GetInt64() : 0,
                    ConnectionCount = e.TryGetProperty("c", out var c) ? c.GetInt32() : 0
                });
            }
        }

        var byToday = new List<ProcessNetworkItem>();
        if (root.TryGetProperty("today", out var todayArr) && todayArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in todayArr.EnumerateArray())
            {
                var name = e.TryGetProperty("n", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var up = e.TryGetProperty("u", out var u) ? u.GetInt64() : 0;
                var down = e.TryGetProperty("d", out var d) ? d.GetInt64() : 0;
                byToday.Add(new ProcessNetworkItem
                {
                    ProcessId = 0,
                    ProcessName = name,
                    SessionUpBytes = up,
                    SessionDownBytes = down,
                    TodayUpBytes = up,
                    TodayDownBytes = down
                });
            }
        }

        long sysUp = 0, sysDown = 0, dayUp = 0, dayDown = 0;
        if (root.TryGetProperty("sys", out var sys) && sys.ValueKind == JsonValueKind.Object)
        {
            if (sys.TryGetProperty("su", out var su)) sysUp = su.GetInt64();
            if (sys.TryGetProperty("sd", out var sd)) sysDown = sd.GetInt64();
            if (sys.TryGetProperty("tu", out var tu)) dayUp = tu.GetInt64();
            if (sys.TryGetProperty("td", out var td)) dayDown = td.GetInt64();
        }

        var estats = root.TryGetProperty("estats", out var es) && es.ValueKind == JsonValueKind.True;
        var hasAttribution = byRate.Exists(i => i.TotalBytesPerSec > 0.01);

        lock (_sync)
        {
            _remoteByRate = byRate;
            _remoteByToday = byToday;
            _remoteSessionUp = sysUp;
            _remoteSessionDown = sysDown;
            _remoteTodayUp = dayUp;
            _remoteTodayDown = dayDown;
            _remoteEstats = estats;
            if (hasAttribution) _remoteAttributionUtc = DateTime.UtcNow;
        }
    }

    /// <summary>远程通道下按缓存组装快照（不触碰原生调用，UI 线程零阻塞）。</summary>
    private ProcessNetworkSnapshot BuildRemoteSnapshot(int topCount)
    {
        var byRate = _remoteByRate
            .Where(i => i.TotalBytesPerSec > 0.01)
            .OrderByDescending(i => i.TotalBytesPerSec)
            .ThenByDescending(i => i.ConnectionCount)
            .Take(topCount)
            .ToList();

        var bySession = _remoteByRate
            .Where(i => i.SessionDownBytes + i.SessionUpBytes > 0)
            .OrderByDescending(i => i.SessionDownBytes + i.SessionUpBytes)
            .Take(topCount)
            .ToList();

        var byToday = _remoteByToday
            .OrderByDescending(i => i.TodayDownBytes + i.TodayUpBytes)
            .Take(topCount)
            .ToList();

        return new ProcessNetworkSnapshot
        {
            TopByRate = byRate.FirstOrDefault(),
            TopByRateList = byRate,
            TopBySession = bySession,
            TopByToday = byToday,
            SystemSessionUpBytes = _remoteSessionUp,
            SystemSessionDownBytes = _remoteSessionDown,
            SystemTodayUpBytes = _remoteTodayUp,
            SystemTodayDownBytes = _remoteTodayDown,
            Timestamp = DateTime.Now
        };
    }
}
