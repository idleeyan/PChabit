using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
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
public sealed class ProcessNetworkMonitor : IDisposable
{
    private const int SampleIntervalMs = 1000;
    private const int TopListCount = 8;
    private const int MaxTrackedProcesses = 256;

    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private bool _running;

    private readonly Dictionary<int, ProcState> _states = new();
    private readonly Dictionary<string, ProcDayBucket> _todayByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _nameCache = new();
    private readonly Dictionary<string, (long Up, long Down)> _adapterLast = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>每条 TCP 连接的 ESTATS 累计基线，只记增量，避免连接生命周期字节一次性入账。</summary>
    private readonly Dictionary<string, (ulong Up, ulong Down)> _connEstatsLast = new(StringComparer.Ordinal);
    private bool _adapterPrimed;

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
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_running) return;
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose() => Stop();

    private void TickSafe()
    {
        try
        {
            SampleOnce();
            Updated?.Invoke();
        }
        catch
        {
            // 采样失败不打断监控主循环
        }
    }

    /// <summary>执行一次采样（供定时器或测试调用）。</summary>
    public void SampleOnce()
    {
        var nowUtc = DateTime.UtcNow;
        double elapsedSec;
        lock (_sync)
        {
            elapsedSec = Math.Clamp((nowUtc - _lastSampleUtc).TotalSeconds, 0.05, 10.0);
            _lastSampleUtc = nowUtc;
            RollDayIfNeeded();
        }

        var connStats = CollectConnectionStats();
        var ioDeltas = CollectIoDeltas();

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
                if (!_states.TryGetValue(pid, out var st))
                {
                    if (_states.Count >= MaxTrackedProcesses && !conn.HasBytes) continue;
                    st = new ProcState { ProcessId = pid };
                    _states[pid] = st;
                }

                st.ConnectionCount = conn.ConnectionCount;
                st.ProcessName = ResolveName(pid);
            }

            // 1) 有 ESTATS 字节的进程：直接记账
            long estatsUpSum = 0;
            long estatsDownSum = 0;
            var fallbackPids = new List<int>();

            foreach (var (pid, st) in _states.ToArray())
            {
                var hasConn = connStats.ContainsKey(pid);
                if (!hasConn && st.ConnectionCount <= 0 && !ioDeltas.ContainsKey(pid))
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
                    estatsDownSum += estatsDown;
                    estatsUpSum += estatsUp;
                }
                else
                {
                    fallbackPids.Add(pid);
                }
            }

            // 2) 无 ESTATS：按权重分摊「页面显示口径」的系统上下行
            long residUp = Math.Max(0, sysUpDelta - estatsUpSum);
            long residDown = Math.Max(0, sysDownDelta - estatsDownSum);
            if (fallbackPids.Count > 0 && (residUp > 0 || residDown > 0 || (estatsUpSum == 0 && estatsDownSum == 0)))
            {
                if (estatsUpSum == 0 && estatsDownSum == 0)
                {
                    residUp = Math.Max(0, sysUpDelta);
                    residDown = Math.Max(0, sysDownDelta);
                }

                var weights = new Dictionary<int, double>();
                double totalWeight = 0;
                foreach (var pid in fallbackPids)
                {
                    if (!_states.TryGetValue(pid, out var st)) continue;
                    double w = 0;
                    if (ioDeltas.TryGetValue(pid, out var io) && io > 0) w = io;
                    else w = Math.Max(1, st.ConnectionCount);
                    if (w <= 0) continue;
                    weights[pid] = w;
                    totalWeight += w;
                }

                if (totalWeight > 0)
                {
                    foreach (var (pid, w) in weights)
                    {
                        if (!_states.TryGetValue(pid, out var st)) continue;
                        var share = w / totalWeight;
                        var downDelta = (long)Math.Round(residDown * share);
                        var upDelta = (long)Math.Round(residUp * share);
                        st.RateDown = downDelta / elapsedSec;
                        st.RateUp = upDelta / elapsedSec;
                        st.SessionDownBytes += downDelta;
                        st.SessionUpBytes += upDelta;
                        AddToday(st.ProcessName, pid, upDelta, downDelta);
                    }
                }
            }
            else
            {
                foreach (var pid in fallbackPids)
                {
                    if (!_states.TryGetValue(pid, out var st)) continue;
                    st.RateDown = 0;
                    st.RateUp = 0;
                }
            }

            // 3) 对齐：进程速率之和不得超过页面上的系统速率（允许 5% 误差）
            NormalizeRatesToSystem(displayUpBps, displayDownBps);

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
        if (sumUp > displayUpBps * 1.05 && sumUp > 0)
        {
            upScale = displayUpBps / sumUp;
        }

        if (sumDown > displayDownBps * 1.05 && sumDown > 0)
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

    private void AddToday(string processName, int pid, long upDelta, long downDelta)
    {
        if (upDelta <= 0 && downDelta <= 0) return;
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
                catch
                {
                    // 单网卡失败忽略
                }
            }
        }
        catch
        {
            // 枚举失败时保持 0，由 getter 回退
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

            var byRate = items
                .Where(i => i.TotalBytesPerSec > 0.01 || i.ConnectionCount > 0)
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
            return (_systemSessionUp, _systemSessionDown, _systemTodayUp, _systemTodayDown);
        }
    }

    /// <summary>启动时用库内「今日」进程累计做种子，避免重启清零。</summary>
    public void SeedTodayFrom(IEnumerable<(string ProcessName, long BytesUp, long BytesDown)> items)
    {
        lock (_sync)
        {
            foreach (var (name, up, down) in items)
            {
                if (string.IsNullOrWhiteSpace(name) || (up <= 0 && down <= 0)) continue;
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
        catch
        {
            // 进程已退出
        }

        if (_nameCache.Count > 512) _nameCache.Clear();
        _nameCache[pid] = name;
        return name;
    }

    private static bool IsIgnoredProcess(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return name.Equals("Idle", StringComparison.OrdinalIgnoreCase)
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

                var err = GetPerTcpConnectionEStats(
                    ref mibRow,
                    TcpEstatsType.TcpConnectionEstatsData,
                    IntPtr.Zero, 0, 0,
                    out var rod,
                    0, (uint)Marshal.SizeOf<TcpEstatsDataRodV0>(),
                    IntPtr.Zero, 0, 0);

                if (err != NO_ERROR) continue;

                var cumUp = Math.Max(rod.DataBytesOut, rod.ThruBytesAcked);
                var cumDown = Math.Max(rod.DataBytesIn, rod.ThruBytesReceived);

                if (_connEstatsLast.TryGetValue(connKey, out var last))
                {
                    // 只记相对上次的增量，避免连接建立前累计字节一次性计入
                    if (cumUp >= last.Up)
                    {
                        var dUp = (long)(cumUp - last.Up);
                        if (dUp > 0 && dUp < 500_000_000L)
                        {
                            agg.EstatsUpDelta += dUp;
                            agg.HasBytes = true;
                        }
                    }

                    if (cumDown >= last.Down)
                    {
                        var dDown = (long)(cumDown - last.Down);
                        if (dDown > 0 && dDown < 500_000_000L)
                        {
                            agg.EstatsDownDelta += dDown;
                            agg.HasBytes = true;
                        }
                    }
                }

                _connEstatsLast[connKey] = (cumUp, cumDown);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void CollectTcpV6(Dictionary<int, ConnAgg> result)
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
                var pid = Marshal.ReadInt32(rowPtr, 52); // OwningPid at offset 52
                if (pid <= 0) continue;
                GetAgg(result, pid).ConnectionCount++;
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

    // ===================== 进程 IO 增量（补充口径） =====================

    private readonly Dictionary<int, ulong> _lastOtherIo = new();

    private Dictionary<int, long> CollectIoDeltas()
    {
        var deltas = new Dictionary<int, long>();
        List<int> pids;

        lock (_sync)
        {
            pids = _states.Values
                .Where(s => s.ConnectionCount > 0)
                .Select(s => s.ProcessId)
                .Distinct()
                .Take(64)
                .ToList();

            // 也纳入刚发现的连接进程
            foreach (var pid in _states.Keys)
            {
                if (_states[pid].ConnectionCount > 0 && !pids.Contains(pid))
                {
                    pids.Add(pid);
                }
            }
        }

        // 首次采样时 CollectConnectionStats 已写入 ConnectionCount；此处对活跃 PID 采 IO
        foreach (var pid in pids.Distinct().Take(64))
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) continue;
            try
            {
                if (!GetProcessIoCounters(handle, out var counters)) continue;
                var current = counters.OtherTransferCount;
                if (_lastOtherIo.TryGetValue(pid, out var last) && current >= last)
                {
                    var delta = (long)(current - last);
                    if (delta > 0 && delta < 200_000_000L)
                    {
                        deltas[pid] = delta;
                    }
                }

                _lastOtherIo[pid] = current;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        return deltas;
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
        public long LastEstatsDown;
        public long LastEstatsUp;
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
