using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Serilog;
using static PChabit.HardwareMonitor.Hardware.ProcessNetworkNative;

namespace PChabit.HardwareMonitor.Hardware;

/// <summary>进程网络流量快照条目。</summary>
public sealed class ProcessNetworkItem
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    /// <summary>本采样周期下行 B/s。</summary>
    public double DownBytesPerSec { get; init; }
    /// <summary>本采样周期上行 B/s。</summary>
    public double UpBytesPerSec { get; init; }
    /// <summary>会话累计下行字节。</summary>
    public long SessionDownBytes { get; init; }
    /// <summary>会话累计上行字节。</summary>
    public long SessionUpBytes { get; init; }
    /// <summary>今日累计下行字节（本程序采样期间）。</summary>
    public long TodayDownBytes { get; init; }
    /// <summary>今日累计上行字节（本程序采样期间）。</summary>
    public long TodayUpBytes { get; init; }
    /// <summary>当前活动连接数（TCP/UDP）。</summary>
    public int ConnectionCount { get; init; }
    public double TotalBytesPerSec => DownBytesPerSec + UpBytesPerSec;
}

/// <summary>进程网络流量统计快照。</summary>
public sealed class ProcessNetworkSnapshot
{
    public ProcessNetworkItem? TopByRate { get; init; }
    public IReadOnlyList<ProcessNetworkItem> TopByRateList { get; init; } = Array.Empty<ProcessNetworkItem>();
    public IReadOnlyList<ProcessNetworkItem> TopBySession { get; init; } = Array.Empty<ProcessNetworkItem>();
    public IReadOnlyList<ProcessNetworkItem> TopByToday { get; init; } = Array.Empty<ProcessNetworkItem>();
    /// <summary>系统级会话累计（与 HardwareMonitorOptions 同源口径时由调用方填充；此处为监视器自算）。</summary>
    public long SystemSessionUpBytes { get; init; }
    public long SystemSessionDownBytes { get; init; }
    public long SystemTodayUpBytes { get; init; }
    public long SystemTodayDownBytes { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

/// <summary>
/// 进程级网络流量监视器。
/// 周期采样 Windows IP Helper 连接表 + ESTATS/进程 IO，得到「当前最大占用进程」
/// 与会话/今日流量统计（类似任务管理器网络列 + 设置里的数据使用量，应用内自采）。
/// </summary>
public sealed partial class ProcessNetworkMonitor : IDisposable
{
    private const int SampleIntervalMs = 1000;
    private const int TopListCount = 8;
    private const int MaxTrackedProcesses = 256;

    /// <summary>3.85.1：自指监控必须排除，否则采集器自身的落库/监听 IO 会被算成流量。</summary>
    private static readonly int SelfPid = ResolveSelfPid();
    private static readonly string SelfProcessName = ResolveSelfProcessName();

    private readonly object _sync = new();
    /// <summary>F2（3.83.0）：采样 Tick 重入防护标志（Interlocked，陷阱 27）。</summary>
    private int _tickBusy;
    private System.Threading.Timer? _timer;
    private bool _running;

    private readonly Dictionary<int, ProcState> _states = new();
    private readonly Dictionary<string, ProcDayBucket> _todayByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _nameCache = new();
    private readonly Dictionary<string, (long Up, long Down)> _adapterLast = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>每条 TCP 连接的 ESTATS 累计基线，只记增量，避免连接生命周期字节一次性入账。</summary>
    private readonly Dictionary<string, (ulong Up, ulong Down)> _connEstatsLast = new(StringComparer.Ordinal);
    /// <summary>已尝试过启用连接级统计的连接键（每连接只付一次 syscall 成本）。</summary>
    private readonly HashSet<string> _estatsEnableAttempted = new(StringComparer.Ordinal);
    /// <summary>连接级统计可用性：null=尚未判定，true=已启用，false=权限不足（永久放弃）。</summary>
    private bool? _estatsGranted;
    private bool _estatsDeniedLogged;
    private bool _adapterPrimed;

    // ---- 归因健康度诊断：回答「ESTATS 到底有没有在出数」 ----
    private int _tickRodOk;            // 本拍成功读回 ROD 的连接数
    private int _tickRodNonZero;       // 其中累计字节非零的连接数
    private int _tickConnsWithBytes;   // 本拍产生了增量的连接数
    private long _tickDeltaBytes;      // 本拍归因到的总字节
    private int _zeroTickStreak;       // 连续无增量拍数
    private bool _dataFlowLogged;
    private DateTime _lastAttributionUtc = DateTime.MinValue;

    /// <summary>当前进程是否以管理员权限运行（Windows 只允许管理员启用连接级统计）。</summary>
    public static bool IsElevated { get; } = ResolveElevated();

    /// <summary>
    /// 连接级统计是否可用（= 已提权且启用成功）。
    /// false 时进程榜必然为空：这不是采样失败，而是 Windows 未授权归因。
    /// </summary>
    public bool EstatsEnabled
    {
        get
        {
            lock (_sync) return _rust != null ? _remoteEstats == true : _estatsGranted == true;
        }
    }

    /// <summary>
    /// 最近 60 秒是否归因到过进程级字节。
    /// 用于区分「确实没有进程在传数据」与「归因链路没出数」，避免浮窗给错提示。
    /// </summary>
    public bool HasRecentAttribution
    {
        get
        {
            lock (_sync)
            {
                var stamp = _rust != null ? _remoteAttributionUtc : _lastAttributionUtc;
                return (DateTime.UtcNow - stamp).TotalSeconds < 60;
            }
        }
    }

    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private string _todayKey = DateTime.Now.ToString("yyyy-MM-dd");
    private long _systemTodayUp;
    private long _systemTodayDown;
    private long _systemSessionUp;
    private long _systemSessionDown;

    /// <summary>可选：由硬件监控注入系统网卡速率（B/s）。网卡 IP 统计优先，此 getter 仅作回退。</summary>
    public Func<float?>? NetUpGetter { get; set; }
    public Func<float?>? NetDownGetter { get; set; }

    public event Action? Updated;

    public bool IsRunning
    {
        get { lock (_sync) return _running; }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_running) return;
            _running = true;
            _lastSampleUtc = DateTime.UtcNow;
            _timer = new System.Threading.Timer(_ => TickSafe(), null, 200, SampleIntervalMs);
        }

        // 可选通道：Rust 采样内核（PCHABIT_RUST_NETSAMPLER=1），连不上则静默回退
        EnsureRustSampler();
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_running)
            {
                _running = false;
                _timer?.Dispose();
                _timer = null;
            }
        }

        // 即使从未 Start（例如基准工具直接调 SampleOnce），也要回收内核连接与进程
        StopRustSampler();
    }

    public void Dispose() => Stop();

    private void TickSafe()
    {
        // F2（3.83.0）：采样慢一轮时下一拍跳过，不堆积（陷阱 27）
        if (Interlocked.Exchange(ref _tickBusy, 1) == 1) return;
        try
        {
            SampleOnce();
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            // 预期：单拍采样失败不打断监控主循环（Debug 防高频刷屏）
            Log.Debug(ex, "进程网络采样失败，本拍跳过");
        }
        finally
        {
            Interlocked.Exchange(ref _tickBusy, 0);
        }
    }

    /// <summary>执行一次采样（供定时器或测试调用）。</summary>
    public void SampleOnce()
    {
        // Rust 通道：采样与聚合在守护进程内完成，本类只推系统速率并接收快照
        if (RustActive)
        {
            SampleRemote();
            return;
        }

        var nowUtc = DateTime.UtcNow;
        double elapsedSec;
        lock (_sync)
        {
            elapsedSec = Math.Clamp((nowUtc - _lastSampleUtc).TotalSeconds, 0.05, 10.0);
            _lastSampleUtc = nowUtc;
            RollDayIfNeeded();
            _tickRodOk = 0;
            _tickRodNonZero = 0;
            _tickConnsWithBytes = 0;
            _tickDeltaBytes = 0;
        }

        var connStats = CollectConnectionStats();

        // 系统口径与硬件卡片一致：优先 LHM NET.Up/Down（页面上显示的那组）；
        // 无 getter 时用网卡 IP Statistics 增量。
        var (adapterUp, adapterDown) = SampleAdapterDeltas();
        long sysUpDelta = adapterUp;
        long sysDownDelta = adapterDown;

        var getterUp = NetUpGetter?.Invoke();
        var getterDown = NetDownGetter?.Invoke();
        if (getterUp.HasValue && !float.IsNaN(getterUp.Value) && !float.IsInfinity(getterUp.Value) && getterUp.Value >= 0)
        {
            sysUpDelta = (long)Math.Max(0, getterUp.Value * elapsedSec);
        }

        if (getterDown.HasValue && !float.IsNaN(getterDown.Value) && !float.IsInfinity(getterDown.Value) && getterDown.Value >= 0)
        {
            sysDownDelta = (long)Math.Max(0, getterDown.Value * elapsedSec);
        }

        // 页面上展示的系统速率 B/s
        var displayUpBps = elapsedSec > 0 ? sysUpDelta / elapsedSec : 0.0;
        var displayDownBps = elapsedSec > 0 ? sysDownDelta / elapsedSec : 0.0;

        lock (_sync)
        {
            _systemSessionUp += sysUpDelta;
            _systemSessionDown += sysDownDelta;
            _systemTodayUp += sysUpDelta;
            _systemTodayDown += sysDownDelta;

            foreach (var (pid, conn) in connStats)
            {
                // 3.85.1：自指监控会把本程序自身的采集/落库 IO 计入统计，直接排除
                if (pid == SelfPid) continue;

                if (!_states.TryGetValue(pid, out var st))
                {
                    if (_states.Count >= MaxTrackedProcesses && !conn.HasBytes) continue;
                    st = new ProcState { ProcessId = pid };
                    _states[pid] = st;
                }

                st.ConnectionCount = conn.ConnectionCount;
                st.ProcessName = ResolveName(pid);
            }

            // 1) 只对有 ESTATS 真实字节增量的进程记账。
            // 3.85.1：此前「无 ESTATS 时按 OtherTransferCount / 连接数加权分摊整机流量」
            // 会把系统无法归因的流量虚构到进程名下。Windows 默认未启用连接级 ESTATS
            // （本程序也从未调用 SetPerTcpConnectionEStats），因此几乎每拍都走兜底；
            // 而本进程持续写 SQLite/日志且常驻本地监听端口，权重最大，导致整机约 90%
            // 的流量被误记成 PChabit 自身（实测单日 20.8 GB）。现改为只记可归因增量，
            // 系统残余流量仅计入系统会话/今日累计，不再虚构进程归属。
            foreach (var (pid, st) in _states.ToArray())
            {
                var hasConn = connStats.ContainsKey(pid);
                if (!hasConn && st.ConnectionCount <= 0)
                {
                    st.RateDown = 0;
                    st.RateUp = 0;
                    continue;
                }

                long estatsDown = 0;
                long estatsUp = 0;
                if (connStats.TryGetValue(pid, out var c))
                {
                    estatsDown = Math.Max(0, c.EstatsDownDelta);
                    estatsUp = Math.Max(0, c.EstatsUpDelta);
                }

                if (estatsDown > 0 || estatsUp > 0)
                {
                    st.RateDown = estatsDown / elapsedSec;
                    st.RateUp = estatsUp / elapsedSec;
                    st.SessionDownBytes += estatsDown;
                    st.SessionUpBytes += estatsUp;
                    AddToday(st.ProcessName, pid, estatsUp, estatsDown);
                }
                else
                {
                    st.RateDown = 0;
                    st.RateUp = 0;
                }
            }

            // 2) 对齐：进程速率之和不得超过页面上的系统速率（允许 5% 误差）
            NormalizeRatesToSystem(displayUpBps, displayDownBps);
            ReportEstatsHealth();

            // 回收
            var expire = nowUtc.AddSeconds(-30);
            foreach (var pid in _states.Keys.ToArray())
            {
                var st = _states[pid];
                if (st.ConnectionCount == 0 && st.RateDown <= 0 && st.RateUp <= 0 && st.LastActiveUtc < expire)
                {
                    _states.Remove(pid);
                }
                else if (st.RateDown > 0 || st.RateUp > 0 || st.ConnectionCount > 0)
                {
                    st.LastActiveUtc = nowUtc;
                }
            }
        }
    }

    /// <summary>进程侧速率向系统显示口径收缩，避免「进程合计 &gt;&gt; 系统上下行」。</summary>
    private void NormalizeRatesToSystem(double displayUpBps, double displayDownBps)
    {
        double sumUp = 0;
        double sumDown = 0;
        foreach (var st in _states.Values)
        {
            if (st.RateUp > 0) sumUp += st.RateUp;
            if (st.RateDown > 0) sumDown += st.RateDown;
        }

        var upScale = 1.0;
        var downScale = 1.0;
        // 只有系统读数本身有效（>0）时才收缩：displayUp/Down 为 0 时（LHM 传感器缺值，
        // 3.85.1 起速率缺值一律归零）不能把进程速率乘成 0，否则实时条目会整片消失。
        if (displayUpBps > 0 && sumUp > displayUpBps * 1.05)
        {
            upScale = displayUpBps / sumUp;
        }

        if (displayDownBps > 0 && sumDown > displayDownBps * 1.05)
        {
            downScale = displayDownBps / sumDown;
        }

        if (Math.Abs(upScale - 1.0) < 0.001 && Math.Abs(downScale - 1.0) < 0.001) return;

        foreach (var st in _states.Values)
        {
            if (upScale != 1.0) st.RateUp *= upScale;
            if (downScale != 1.0) st.RateDown *= downScale;
        }
    }

    /// <summary>
    /// 归因健康度自证：出数时记一条 Information，长期无增量时给出可诊断的 Warning。
    /// 有了它，日志本身就能回答「ESTATS 是没读到字节，还是流量根本不走 TCP」。
    /// </summary>
    private void ReportEstatsHealth()
    {
        if (_tickDeltaBytes > 0) _lastAttributionUtc = DateTime.UtcNow;

        if (_estatsGranted != true)
        {
            _zeroTickStreak = 0;
            return;
        }

        if (_tickDeltaBytes > 0)
        {
            _zeroTickStreak = 0;
            if (_dataFlowLogged) return;
            _dataFlowLogged = true;

            // 附上此刻的 Top3，日志即可核对「浮窗会显示哪些进程、量级是否合理」
            var top = _states.Values
                .Where(s => s.RateDown + s.RateUp > 0)
                .OrderByDescending(s => s.RateDown + s.RateUp)
                .Take(3)
                .Select(s => $"{s.ProcessName} ↓{s.RateDown / 1024:F0}K/s ↑{s.RateUp / 1024:F0}K/s");

            Log.Information("进程级流量归因开始出数：本拍 {Bytes} 字节 / {Conns} 条连接（已尝试启用 {Attempted} 条），Top: {Top}",
                _tickDeltaBytes, _tickConnsWithBytes, _estatsEnableAttempted.Count, string.Join(" | ", top));
            return;
        }

        _zeroTickStreak++;
        if (_zeroTickStreak != 5 && _zeroTickStreak != 30) return;
        Log.Warning("ESTATS 已启用但连续 {Ticks} 拍无字节增量：ROD 读到 {RodOk} 条（累计非零 {RodNonZero} 条），已尝试启用 {Attempted} 条",
            _zeroTickStreak, _tickRodOk, _tickRodNonZero, _estatsEnableAttempted.Count);
    }

    private void AddToday(string processName, int pid, long upDelta, long downDelta)
    {
        if (upDelta <= 0 && downDelta <= 0) return;
        if (IsIgnoredProcess(processName)) return;
        var key = string.IsNullOrEmpty(processName) ? "pid:" + pid : processName;
        if (!_todayByName.TryGetValue(key, out var bucket))
        {
            bucket = new ProcDayBucket { ProcessName = key };
            _todayByName[key] = bucket;
        }

        bucket.DownBytes += downDelta;
        bucket.UpBytes += upDelta;
        bucket.LastSeenUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// 采样物理/活跃网卡 BytesSent/BytesReceived 增量（系统真实上/下行）。
    /// 首次只建基线；跳过回环与常见虚拟网卡。
    /// </summary>
    private (long Up, long Down) SampleAdapterDeltas()
    {
        long up = 0;
        long down = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (IsVirtualNic(nic.Name) || IsVirtualNic(nic.Description)) continue;

                    var id = nic.Id;
                    if (string.IsNullOrEmpty(id)) id = nic.Name + "|" + nic.Description;
                    seen.Add(id);

                    var stats = nic.GetIPStatistics();
                    long sent = stats.BytesSent;
                    long recv = stats.BytesReceived;
                    if (!_adapterLast.TryGetValue(id, out var last))
                    {
                        _adapterLast[id] = (sent, recv);
                        continue;
                    }

                    if (sent >= last.Up) up += sent - last.Up;
                    if (recv >= last.Down) down += recv - last.Down;
                    _adapterLast[id] = (sent, recv);
                }
                catch (Exception ex)
                {
                    // 预期：单网卡读取失败（拔出/禁用），跳过
                    Log.Debug(ex, "网卡统计读取失败，跳过");
                }
            }
        }
        catch (Exception ex)
        {
            // 预期：网卡枚举失败，保持 0 由 getter 回退 LHM 数据
            Log.Debug(ex, "网卡枚举失败，本拍增量记 0");
        }

        // 清理消失的网卡
        if (_adapterLast.Count > 0 && seen.Count > 0)
        {
            foreach (var key in _adapterLast.Keys.ToArray())
            {
                if (!seen.Contains(key)) _adapterLast.Remove(key);
            }
        }

        // 第一轮只打点，增量为 0
        if (!_adapterPrimed)
        {
            _adapterPrimed = true;
            return (0, 0);
        }

        return (up, down);
    }

    private static bool IsVirtualNic(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string[] keys =
        {
            "virtual", "vmware", "hyper-v", "hyper v", "vbox", "loopback",
            "tunnel", "tap", "tun", "bluetooth", "zerotier", "tailscale", "wan miniport"
        };
        foreach (var k in keys)
        {
            if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }

    /// <summary>取当前快照（UI 线程安全）。按进程名合并多 PID，避免同名进程被拆成多行后看起来「各占满系统」。</summary>
    public ProcessNetworkSnapshot GetSnapshot(int topCount = 5)
    {
        lock (_sync)
        {
            if (_rust != null) return BuildRemoteSnapshot(topCount);

            var byName = new Dictionary<string, (double Up, double Down, long SUp, long SDown, int Conns, int Pid)>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in _states.Values)
            {
                if (IsIgnoredProcess(s.ProcessName)) continue;
                var key = string.IsNullOrWhiteSpace(s.ProcessName) ? "pid:" + s.ProcessId : s.ProcessName;
                byName.TryGetValue(key, out var acc);
                byName[key] = (
                    acc.Up + Math.Max(0, s.RateUp),
                    acc.Down + Math.Max(0, s.RateDown),
                    acc.SUp + s.SessionUpBytes,
                    acc.SDown + s.SessionDownBytes,
                    acc.Conns + s.ConnectionCount,
                    s.ProcessId);
            }

            var items = byName.Select(kv => new ProcessNetworkItem
            {
                ProcessId = kv.Value.Pid,
                ProcessName = kv.Key,
                DownBytesPerSec = kv.Value.Down,
                UpBytesPerSec = kv.Value.Up,
                SessionDownBytes = kv.Value.SDown,
                SessionUpBytes = kv.Value.SUp,
                ConnectionCount = kv.Value.Conns
            }).ToList();

            // 3.85.1：只展示有真实可归因流量的进程，避免「零速率但有连接」的进程霸榜
            var byRate = items
                .Where(i => i.TotalBytesPerSec > 0.01)
                .OrderByDescending(i => i.TotalBytesPerSec)
                .ThenByDescending(i => i.ConnectionCount)
                .Take(topCount)
                .ToList();

            var bySession = items
                .Where(i => i.SessionDownBytes + i.SessionUpBytes > 0)
                .OrderByDescending(i => i.SessionDownBytes + i.SessionUpBytes)
                .Take(topCount)
                .ToList();

            var byToday = _todayByName.Values
                .Where(v => !IsIgnoredProcess(v.ProcessName) && v.DownBytes + v.UpBytes > 0)
                .Select(v => new ProcessNetworkItem
                {
                    ProcessId = 0,
                    ProcessName = v.ProcessName,
                    DownBytesPerSec = 0,
                    UpBytesPerSec = 0,
                    SessionDownBytes = v.DownBytes,
                    SessionUpBytes = v.UpBytes,
                    TodayDownBytes = v.DownBytes,
                    TodayUpBytes = v.UpBytes
                })
                .OrderByDescending(i => i.TodayDownBytes + i.TodayUpBytes)
                .Take(topCount)
                .ToList();

            return new ProcessNetworkSnapshot
            {
                TopByRate = byRate.FirstOrDefault(),
                TopByRateList = byRate,
                TopBySession = bySession,
                TopByToday = byToday,
                SystemSessionUpBytes = _systemSessionUp,
                SystemSessionDownBytes = _systemSessionDown,
                SystemTodayUpBytes = _systemTodayUp,
                SystemTodayDownBytes = _systemTodayDown,
                Timestamp = DateTime.Now
            };
        }
    }

    /// <summary>今日全部进程累计（供落库/统计页，不截断）。</summary>
    public List<ProcessNetworkItem> GetAllTodayProcesses()
    {
        lock (_sync)
        {
            if (_rust != null)
            {
                return _remoteByToday
                    .Where(v => v.TodayDownBytes > 0 || v.TodayUpBytes > 0)
                    .OrderByDescending(v => v.TodayDownBytes + v.TodayUpBytes)
                    .ToList();
            }

            return _todayByName.Values
                .Where(v => !IsIgnoredProcess(v.ProcessName) && (v.DownBytes > 0 || v.UpBytes > 0))
                .Select(v => new ProcessNetworkItem
                {
                    ProcessId = 0,
                    ProcessName = v.ProcessName,
                    SessionDownBytes = v.DownBytes,
                    SessionUpBytes = v.UpBytes,
                    TodayDownBytes = v.DownBytes,
                    TodayUpBytes = v.UpBytes
                })
                .OrderByDescending(i => i.TodayDownBytes + i.TodayUpBytes)
                .ToList();
        }
    }

    /// <summary>系统级会话/今日累计字节。</summary>
    public (long SessionUp, long SessionDown, long TodayUp, long TodayDown) GetSystemTotals()
    {
        lock (_sync)
        {
            if (_rust != null)
            {
                return (_remoteSessionUp, _remoteSessionDown, _remoteTodayUp, _remoteTodayDown);
            }
            return (_systemSessionUp, _systemSessionDown, _systemTodayUp, _systemTodayDown);
        }
    }

    /// <summary>启动时用库内「今日」进程累计做种子，避免重启清零。</summary>
    public void SeedTodayFrom(IEnumerable<(string ProcessName, long BytesUp, long BytesDown)> items)
    {
        if (TryEnsureRustForSeed())
        {
            var client = _rust;
            foreach (var (name, up, down) in items)
            {
                if (string.IsNullOrWhiteSpace(name) || (up <= 0 && down <= 0)) continue;
                if (IsIgnoredProcess(name)) continue;
                // 名称含空格时作为第四个参数整体传给内核（协议按 splitn(4) 切分）
                client!.Send($"SEEDP {up} {down} {name}");
            }
            return;
        }

        lock (_sync)
        {
            foreach (var (name, up, down) in items)
            {
                if (string.IsNullOrWhiteSpace(name) || (up <= 0 && down <= 0)) continue;
                if (IsIgnoredProcess(name)) continue;
                if (!_todayByName.TryGetValue(name, out var bucket))
                {
                    bucket = new ProcDayBucket { ProcessName = name };
                    _todayByName[name] = bucket;
                }

                bucket.DownBytes = Math.Max(bucket.DownBytes, down);
                bucket.UpBytes = Math.Max(bucket.UpBytes, up);
            }
        }
    }

    /// <summary>启动时用库内今日系统累计做种子。</summary>
    public void SeedSystemToday(long up, long down)
    {
        if (TryEnsureRustForSeed())
        {
            _rust!.Send($"SEEDSYS {up} {down}");
            return;
        }

        lock (_sync)
        {
            _systemTodayUp = Math.Max(_systemTodayUp, up);
            _systemTodayDown = Math.Max(_systemTodayDown, down);
        }
    }

    private void RollDayIfNeeded()
    {
        var key = DateTime.Now.ToString("yyyy-MM-dd");
        if (string.Equals(key, _todayKey, StringComparison.Ordinal)) return;

        _todayKey = key;
        _todayByName.Clear();
        _systemTodayUp = 0;
        _systemTodayDown = 0;
    }

    private static ProcessNetworkItem ToItem(ProcState st) => new()
    {
        ProcessId = st.ProcessId,
        ProcessName = st.ProcessName,
        DownBytesPerSec = st.RateDown,
        UpBytesPerSec = st.RateUp,
        SessionDownBytes = st.SessionDownBytes,
        SessionUpBytes = st.SessionUpBytes,
        ConnectionCount = st.ConnectionCount
    };

    private string ResolveName(int pid)
    {
        if (_nameCache.TryGetValue(pid, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        string name = $"pid:{pid}";
        try
        {
            using var p = Process.GetProcessById(pid);
            name = string.IsNullOrEmpty(p.ProcessName) ? name : p.ProcessName;
        }
        catch (Exception ex)
        {
            // 预期：进程已退出（高频竞态），保留 pid 占位名并缓存
            Log.Debug(ex, "进程名解析失败（已退出），PID {Pid}", pid);
        }

        if (_nameCache.Count > 512) _nameCache.Clear();
        _nameCache[pid] = name;
        return name;
    }

    private static int ResolveSelfPid() => Environment.ProcessId;

    /// <summary>是否以管理员权限运行（决定能否启用连接级统计）。</summary>
    private static bool ResolveElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            // 预期：身份读取失败（极少见），按普通权限处理，进程榜为空但不影响其它功能
            Log.Debug(ex, "管理员权限判定失败，按普通权限处理");
            return false;
        }
    }

    private static string ResolveSelfProcessName()
    {
        // Environment.ProcessName 并非 BCL API；用可执行文件路径取基名，
        // 与 ResolveName() 里 Process.GetProcessById().ProcessName 的口径一致。
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        return "PChabit";
    }

    private static bool IsIgnoredProcess(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return name.Equals(SelfProcessName, StringComparison.OrdinalIgnoreCase)
               || name.Equals("Idle", StringComparison.OrdinalIgnoreCase)
               || name.Equals("System", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Registry", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Secure System", StringComparison.OrdinalIgnoreCase);
    }

    // ===================== 连接表 + ESTATS =====================

    private sealed class ConnAgg
    {
        public int ConnectionCount;
        public long EstatsDownDelta;
        public long EstatsUpDelta;
        public bool HasBytes;
        public List<string> ConnKeys { get; } = new();
    }

    private Dictionary<int, ConnAgg> CollectConnectionStats()
    {
        var result = new Dictionary<int, ConnAgg>();

        CollectTcpV4(result);
        CollectTcpV6(result);
        CollectUdpV4(result);
        CollectUdpV6(result);

        // 清理消失连接的 ESTATS 基线，防止泄漏
        if (_connEstatsLast.Count > 512)
        {
            var live = new HashSet<string>(StringComparer.Ordinal);
            foreach (var agg in result.Values)
            {
                foreach (var k in agg.ConnKeys) live.Add(k);
            }

            foreach (var key in _connEstatsLast.Keys.ToArray())
            {
                if (!live.Contains(key)) _connEstatsLast.Remove(key);
            }
        }

        return result;
    }

    /// <summary>
    /// 为单条 IPv4 TCP 连接启用 TCP_ESTATS_DATA 采集。
    /// 每连接只尝试一次（成功/权限不足都记入 <see cref="_estatsEnableAttempted"/>）；
    /// 权限不足时把 <see cref="_estatsGranted"/> 永久置 false，后续采样不再产生 syscall。
    /// </summary>
    private void TryEnableEstats(ref MibTcpRow row, string connKey)
    {
        MarkEnableAttempt(connKey);
        if (!_estatsGranted.HasValue && !IsElevated)
        {
            MarkEstatsUnavailable();
            return;
        }

        if (_estatsGranted == false) return;

        var rw = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(rw, 1); // TCP_ESTATS_DATA_RW_v0.EnableCollection = TRUE
            var ret = SetPerTcpConnectionEStats(ref row, TcpEstatsType.TcpConnectionEstatsData, rw, 0, 1, 0);
            ApplyEnableResult(ret, connKey);
        }
        finally
        {
            Marshal.FreeHGlobal(rw);
        }
    }

    /// <summary>为单条 IPv6 TCP 连接启用 TCP_ESTATS_DATA 采集（规则同 IPv4）。</summary>
    private void TryEnableEstats6(ref MibTcp6Row row, string connKey)
    {
        MarkEnableAttempt(connKey);
        if (!_estatsGranted.HasValue && !IsElevated)
        {
            MarkEstatsUnavailable();
            return;
        }

        if (_estatsGranted == false) return;

        var rw = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(rw, 1);
            var ret = SetPerTcp6ConnectionEStats(ref row, TcpEstatsType.TcpConnectionEstatsData, rw, 0, 1, 0);
            ApplyEnableResult(ret, connKey);
        }
        finally
        {
            Marshal.FreeHGlobal(rw);
        }
    }

    private void MarkEnableAttempt(string connKey)
    {
        if (_estatsEnableAttempted.Count > 1024) _estatsEnableAttempted.Clear();
        _estatsEnableAttempted.Add(connKey);
    }

    private void MarkEstatsUnavailable()
    {
        _estatsGranted = false;
        if (_estatsDeniedLogged) return;
        _estatsDeniedLogged = true;
        Log.Information("进程级流量归因未启用：当前非管理员权限，Windows 拒绝 SetPerTcpConnectionEStats（连接级统计）");
    }

    private void ApplyEnableResult(uint ret, string connKey)
    {
        switch (ret)
        {
            case NO_ERROR:
                if (_estatsGranted != true)
                {
                    _estatsGranted = true;
                    Log.Information("连接级 TCP 统计已启用，进程级流量归因生效");
                }

                break;

            case ERROR_ACCESS_DENIED:
                if (_estatsGranted != false)
                {
                    _estatsGranted = false;
                    if (!_estatsDeniedLogged)
                    {
                        _estatsDeniedLogged = true;
                        Log.Warning("进程级流量归因被拒绝（ERROR_ACCESS_DENIED）：需以管理员身份运行");
                    }
                }

                break;

            default:
                // ERROR_NOT_FOUND 等：连接在取样与启用之间已关闭，不缓存结论，下拍重试
                _estatsEnableAttempted.Remove(connKey);
                break;
        }
    }

    private void CollectTcpV4(Dictionary<int, ConnAgg> result)
    {
        const int rowSize = 24; // MIB_TCPROW_OWNER_PID
        int bufLen = 0;
        var ret = GetExtendedTcpTable(IntPtr.Zero, ref bufLen, false, AF_INET, TcpTableClass.TcpTableOwnerPidAll, 0);
        if (bufLen <= 0) return;

        var buffer = Marshal.AllocHGlobal(bufLen);
        try
        {
            ret = GetExtendedTcpTable(buffer, ref bufLen, false, AF_INET, TcpTableClass.TcpTableOwnerPidAll, 0);
            if (ret != NO_ERROR) return;

            var count = Marshal.ReadInt32(buffer);
            var rowsPtr = IntPtr.Add(buffer, 4);

            for (var i = 0; i < count; i++)
            {
                var rowPtr = IntPtr.Add(rowsPtr, i * rowSize);
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                if (row.OwningPid == 0 || row.OwningPid == uint.MaxValue) continue;

                var pid = (int)row.OwningPid;
                var agg = GetAgg(result, pid);
                agg.ConnectionCount++;

                if (row.State != 5) continue; // ESTABLISHED

                var connKey = string.Concat(
                    row.LocalAddr.ToString(), ":", row.LocalPort.ToString(), ">",
                    row.RemoteAddr.ToString(), ":", row.RemotePort.ToString());
                agg.ConnKeys.Add(connKey);

                var mibRow = new MibTcpRow
                {
                    State = row.State,
                    LocalAddr = row.LocalAddr,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemotePort = row.RemotePort
                };

                // Windows 默认关闭连接级统计：未启用时 GetPerTcpConnectionEStats 返回
                // NO_ERROR 但字节计数恒为 0（实测 40/40 条连接全 0）。必须先用
                // SetPerTcpConnectionEStats 打开 TCP_ESTATS_DATA，该调用仅管理员可用。
                if (_estatsGranted != false && !_estatsEnableAttempted.Contains(connKey))
                {
                    TryEnableEstats(ref mibRow, connKey);
                }

                var err = GetPerTcpConnectionEStats(
                    ref mibRow,
                    TcpEstatsType.TcpConnectionEstatsData,
                    IntPtr.Zero, 0, 0,   // Rw：不改读写配置
                    IntPtr.Zero, 0, 0,   // Ros：不要只读静态项
                    out var rod,
                    0, (uint)Marshal.SizeOf<TcpEstatsDataRodV0>());

                if (err != NO_ERROR) continue;
                AccumulateConnEstats(agg, connKey, rod);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 把一条连接的累计字节折算成本拍增量。
    /// 只记相对上次的增量，避免连接建立前累计字节被一次性计入；
    /// 同时累计诊断计数，用于回答「ESTATS 到底有没有在出数」。
    /// </summary>
    private void AccumulateConnEstats(ConnAgg agg, string connKey, in TcpEstatsDataRodV0 rod)
    {
        var cumUp = Math.Max(rod.DataBytesOut, rod.ThruBytesAcked);
        var cumDown = Math.Max(rod.DataBytesIn, rod.ThruBytesReceived);
        _tickRodOk++;
        if (cumUp > 0 || cumDown > 0) _tickRodNonZero++;

        if (_connEstatsLast.TryGetValue(connKey, out var last))
        {
            if (cumUp >= last.Up)
            {
                var dUp = (long)(cumUp - last.Up);
                if (dUp > 0 && dUp < 500_000_000L)
                {
                    agg.EstatsUpDelta += dUp;
                    agg.HasBytes = true;
                    _tickDeltaBytes += dUp;
                    _tickConnsWithBytes++;
                }
            }

            if (cumDown >= last.Down)
            {
                var dDown = (long)(cumDown - last.Down);
                if (dDown > 0 && dDown < 500_000_000L)
                {
                    agg.EstatsDownDelta += dDown;
                    agg.HasBytes = true;
                    _tickDeltaBytes += dDown;
                    _tickConnsWithBytes++;
                }
            }
        }

        _connEstatsLast[connKey] = (cumUp, cumDown);
    }

    private void CollectTcpV6(Dictionary<int, ConnAgg> result)
    {
        const int rowSize = 56; // MIB_TCP6ROW_OWNER_PID
        int bufLen = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufLen, false, AF_INET6, TcpTableClass.TcpTableOwnerPidAll, 0);
        if (bufLen <= 0) return;

        var buffer = Marshal.AllocHGlobal(bufLen);
        try
        {
            var ret = GetExtendedTcpTable(buffer, ref bufLen, false, AF_INET6, TcpTableClass.TcpTableOwnerPidAll, 0);
            if (ret != NO_ERROR) return;

            var count = Marshal.ReadInt32(buffer);
            var rowsPtr = IntPtr.Add(buffer, 4);
            for (var i = 0; i < count; i++)
            {
                var rowPtr = IntPtr.Add(rowsPtr, i * rowSize);
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPtr);
                if (row.OwningPid == 0 || row.OwningPid == uint.MaxValue) continue;

                var pid = (int)row.OwningPid;
                var agg = GetAgg(result, pid);
                agg.ConnectionCount++;

                if (row.State != 5) continue; // ESTABLISHED

                // IPv6 地址是 16 字节二进制，用十六进制串做连接键，避免多行碰撞
                var connKey = string.Concat(
                    "6|", Convert.ToHexString(row.LocalAddr), ":", row.LocalPort.ToString(), ">",
                    Convert.ToHexString(row.RemoteAddr), ":", row.RemotePort.ToString());
                agg.ConnKeys.Add(connKey);

                var mibRow = new MibTcp6Row
                {
                    State = row.State,
                    LocalAddr = row.LocalAddr,
                    LocalScopeId = row.LocalScopeId,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemoteScopeId = row.RemoteScopeId,
                    RemotePort = row.RemotePort
                };

                if (_estatsGranted != false && !_estatsEnableAttempted.Contains(connKey))
                {
                    TryEnableEstats6(ref mibRow, connKey);
                }

                var err = GetPerTcp6ConnectionEStats(
                    ref mibRow,
                    TcpEstatsType.TcpConnectionEstatsData,
                    IntPtr.Zero, 0, 0,
                    IntPtr.Zero, 0, 0,
                    out var rod,
                    0, (uint)Marshal.SizeOf<TcpEstatsDataRodV0>());
                if (err != NO_ERROR) continue;
                AccumulateConnEstats(agg, connKey, rod);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void CollectUdpV4(Dictionary<int, ConnAgg> result)
    {
        const int rowSize = 12; // MIB_UDPROW_OWNER_PID
        int bufLen = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufLen, false, AF_INET, UdpTableClass.UdpTableOwnerPid, 0);
        if (bufLen <= 0) return;

        var buffer = Marshal.AllocHGlobal(bufLen);
        try
        {
            var ret = GetExtendedUdpTable(buffer, ref bufLen, false, AF_INET, UdpTableClass.UdpTableOwnerPid, 0);
            if (ret != NO_ERROR) return;

            var count = Marshal.ReadInt32(buffer);
            var rowsPtr = IntPtr.Add(buffer, 4);
            for (var i = 0; i < count; i++)
            {
                var rowPtr = IntPtr.Add(rowsPtr, i * rowSize);
                var pid = Marshal.ReadInt32(rowPtr, 8);
                if (pid <= 0) continue;
                GetAgg(result, pid).ConnectionCount++;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void CollectUdpV6(Dictionary<int, ConnAgg> result)
    {
        const int rowSize = 28; // MIB_UDP6ROW_OWNER_PID
        const int pidOffset = 24;
        int bufLen = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufLen, false, AF_INET6, UdpTableClass.UdpTableOwnerPid, 0);
        if (bufLen <= 0) return;

        var buffer = Marshal.AllocHGlobal(bufLen);
        try
        {
            var ret = GetExtendedUdpTable(buffer, ref bufLen, false, AF_INET6, UdpTableClass.UdpTableOwnerPid, 0);
            if (ret != NO_ERROR) return;

            var count = Marshal.ReadInt32(buffer);
            var rowsPtr = IntPtr.Add(buffer, 4);
            for (var i = 0; i < count; i++)
            {
                var rowPtr = IntPtr.Add(rowsPtr, i * rowSize);
                var pid = Marshal.ReadInt32(rowPtr, pidOffset);
                if (pid <= 0) continue;
                GetAgg(result, pid).ConnectionCount++;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ConnAgg GetAgg(Dictionary<int, ConnAgg> result, int pid)
    {
        if (!result.TryGetValue(pid, out var agg))
        {
            agg = new ConnAgg();
            result[pid] = agg;
        }

        return agg;
    }

    private sealed class ProcState
    {
        public int ProcessId;
        public string ProcessName = string.Empty;
        public int ConnectionCount;
        public double RateDown;
        public double RateUp;
        public long SessionDownBytes;
        public long SessionUpBytes;
        public DateTime LastActiveUtc = DateTime.UtcNow;
    }

    private sealed class ProcDayBucket
    {
        public string ProcessName = string.Empty;
        public long DownBytes;
        public long UpBytes;
        public DateTime LastSeenUtc;
    }
}
