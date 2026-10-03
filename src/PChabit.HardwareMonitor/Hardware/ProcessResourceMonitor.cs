using System.Diagnostics;
using System.Runtime.InteropServices;
using static PChabit.HardwareMonitor.Hardware.ProcessNetworkNative;

namespace PChabit.HardwareMonitor.Hardware;

/// <summary>进程资源占用条目（CPU / 内存 / 磁盘 IO / GPU）。</summary>
public sealed class ProcessResourceItem
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    /// <summary>CPU 占用 %（相对整机，可 &gt;100 于多核进程时由调用方钳制显示）。</summary>
    public double CpuPercent { get; init; }
    /// <summary>工作集内存 MB。</summary>
    public double MemoryMb { get; init; }
    /// <summary>磁盘读+写 B/s。</summary>
    public double DiskBytesPerSec { get; init; }
    /// <summary>GPU 引擎占用 %（多引擎取最大，近似任务管理器 GPU 列）。</summary>
    public double GpuPercent { get; init; }
    /// <summary>GPU 专用显存 MB（Dedicated Usage）。</summary>
    public double GpuDedicatedMb { get; init; }
}

/// <summary>各资源维度的 Top 进程快照。</summary>
public sealed class ProcessResourceSnapshot
{
    public ProcessResourceItem? TopCpu { get; init; }
    public ProcessResourceItem? TopMemory { get; init; }
    public ProcessResourceItem? TopDisk { get; init; }
    public ProcessResourceItem? TopGpu { get; init; }
    public ProcessResourceItem? TopGpuMemory { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

/// <summary>
/// 进程级 CPU / 内存 / 磁盘 / GPU 占用监视器（任务管理器式「最大占用进程」）。
/// CPU=TotalProcessorTime 差分；内存=WorkingSet64；磁盘=IO Read+Write Transfer 差分；
/// GPU=PDH「GPU Engine / Utilization Percentage」与「Dedicated Usage」（无计数器时 GPU 为空）。
/// </summary>
public sealed class ProcessResourceMonitor : IDisposable
{
    // 3.24.0 性能优化：2s→5s。GPU PDH 计数器是本服务最大开销，5s 对「最大占用进程」卡片足够。
    private const int SampleIntervalMs = 5000;
    private const int MaxTrackedProcesses = 384;
    /// <summary>GPU 计数器重建后每类引擎每进程最多保留的实例数（去重保护）。</summary>
    private const int MaxGpuCounters = 160;

    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "Secure System", "_Total"
    };

    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private bool _running;

    private readonly Dictionary<int, ProcSample> _last = new();
    private DateTime _lastSampleUtc = DateTime.UtcNow;

    // GPU 计数器缓存（实例名 → 计数器），定期重建
    private readonly Dictionary<string, PerformanceCounter> _gpuUtilCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PerformanceCounter> _gpuMemCounters = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _gpuCacheBuiltUtc = DateTime.MinValue;
    private bool _gpuUnavailable;
    // 3.24.0：类别存在性缓存（Exists 单次约 400ms，进程生命周期内只查一次）
    private bool _gpuEngineCategoryAvailable;
    private bool? _gpuMemCategoryAvailable;

    private ProcessResourceSnapshot _snapshot = new();

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
            _timer = new System.Threading.Timer(_ => TickSafe(), null, 400, SampleIntervalMs);
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
            DisposeGpuCounters();
        }
    }

    public void Dispose() => Stop();

    public ProcessResourceSnapshot GetSnapshot()
    {
        lock (_sync) return _snapshot;
    }

    private void TickSafe()
    {
        try
        {
            SampleOnce();
            Updated?.Invoke();
        }
        catch
        {
            // 采样失败不打断主循环
        }
    }

    private void SampleOnce()
    {
        var nowUtc = DateTime.UtcNow;
        double dt;
        lock (_sync)
        {
            dt = (nowUtc - _lastSampleUtc).TotalSeconds;
            if (dt < 0.2) return;
            _lastSampleUtc = nowUtc;
        }

        var samples = CollectProcessSamples(dt);
        var gpu = CollectGpu(samples);
        ApplyGpu(samples, gpu);

        ProcessResourceItem? topCpu = null, topMem = null, topDisk = null, topGpu = null, topGpuMem = null;
        foreach (var s in samples.Values)
        {
            if (IgnoredNames.Contains(s.ProcessName)) continue;
            var item = ToItem(s);
            if (topCpu == null || item.CpuPercent > topCpu.CpuPercent) topCpu = item;
            if (topMem == null || item.MemoryMb > topMem.MemoryMb) topMem = item;
            if (topDisk == null || item.DiskBytesPerSec > topDisk.DiskBytesPerSec) topDisk = item;
            if (item.GpuPercent > 0 && (topGpu == null || item.GpuPercent > topGpu.GpuPercent)) topGpu = item;
            if (item.GpuDedicatedMb > 0 && (topGpuMem == null || item.GpuDedicatedMb > topGpuMem.GpuDedicatedMb)) topGpuMem = item;
        }

        var snap = new ProcessResourceSnapshot
        {
            TopCpu = topCpu is { CpuPercent: > 0.3 } ? topCpu : null,
            TopMemory = topMem is { MemoryMb: > 8 } ? topMem : null,
            TopDisk = topDisk is { DiskBytesPerSec: > 1024 } ? topDisk : null,
            TopGpu = topGpu is { GpuPercent: > 0.5 } ? topGpu : null,
            TopGpuMemory = topGpuMem is { GpuDedicatedMb: > 16 } ? topGpuMem : null,
            Timestamp = DateTime.Now
        };

        lock (_sync) _snapshot = snap;
    }

    private Dictionary<int, ProcSample> CollectProcessSamples(double dt)
    {
        var map = new Dictionary<int, ProcSample>(MaxTrackedProcesses);
        Process[] procs;
        try
        {
            procs = Process.GetProcesses();
        }
        catch
        {
            return map;
        }

        int count = 0;
        foreach (var p in procs)
        {
            if (count++ > MaxTrackedProcesses + 64) break;
            try
            {
                int pid = p.Id;
                string name = p.ProcessName;
                if (string.IsNullOrEmpty(name) || IgnoredNames.Contains(name)) continue;

                double cpuTimeMs = 0;
                try { cpuTimeMs = p.TotalProcessorTime.TotalMilliseconds; } catch { /* 退出中 */ }

                double memMb = 0;
                try { memMb = p.WorkingSet64 / (1024.0 * 1024.0); } catch { }

                double diskBps = SampleDiskRate(pid, dt);

                map[pid] = new ProcSample
                {
                    ProcessId = pid,
                    ProcessName = name,
                    CpuTimeMs = cpuTimeMs,
                    MemoryMb = memMb,
                    DiskBytesPerSec = diskBps
                };
            }
            catch
            {
                // 单进程失败跳过
            }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        // CPU%：与上一拍 TotalProcessorTime 差分
        foreach (var kv in map)
        {
            if (_last.TryGetValue(kv.Key, out var prev) && dt > 0)
            {
                var deltaMs = kv.Value.CpuTimeMs - prev.CpuTimeMs;
                if (deltaMs >= 0)
                {
                    int cores = Math.Max(1, Environment.ProcessorCount);
                    kv.Value.CpuPercent = Math.Clamp(deltaMs / (dt * 1000.0) / cores * 100.0, 0, 100 * cores);
                }
            }
        }

        _last.Clear();
        foreach (var kv in map) _last[kv.Key] = kv.Value;
        return map;
    }

    private readonly Dictionary<int, ulong> _lastDiskIo = new();

    private double SampleDiskRate(int pid, double dt)
    {
        if (dt <= 0) return 0;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return 0;
        try
        {
            if (!GetProcessIoCounters(handle, out var c)) return 0;
            ulong current = c.ReadTransferCount + c.WriteTransferCount;
            if (_lastDiskIo.TryGetValue(pid, out var last) && current >= last)
            {
                var delta = current - last;
                // 超大跳变视为进程重启/句柄异常，本拍不计
                if (delta < 500_000_000UL)
                    return delta / dt;
            }
            _lastDiskIo[pid] = current;
            return 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static ProcessResourceItem ToItem(ProcSample s) => new()
    {
        ProcessId = s.ProcessId,
        ProcessName = s.ProcessName,
        CpuPercent = s.CpuPercent,
        MemoryMb = s.MemoryMb,
        DiskBytesPerSec = s.DiskBytesPerSec,
        GpuPercent = s.GpuPercent,
        GpuDedicatedMb = s.GpuDedicatedMb
    };

    // ---------- GPU：GPU Engine 性能计数器 ----------

    private readonly struct GpuSample
    {
        public readonly Dictionary<int, double> UtilByPid;
        public readonly Dictionary<int, double> DedicatedMbByPid;
        public GpuSample(Dictionary<int, double> util, Dictionary<int, double> mem)
        {
            UtilByPid = util;
            DedicatedMbByPid = mem;
        }
    }

    private GpuSample CollectGpu(Dictionary<int, ProcSample> _)
    {
        var util = new Dictionary<int, double>();
        var mem = new Dictionary<int, double>();
        if (_gpuUnavailable) return new GpuSample(util, mem);

        try
        {
            EnsureGpuCache();
            foreach (var (inst, pc) in _gpuUtilCounters)
            {
                if (!TryParsePid(inst, out int pid)) continue;
                float v;
                try { v = pc.NextValue(); } catch { continue; }
                if (v <= 0) continue;
                util[pid] = Math.Max(util.GetValueOrDefault(pid), v);
            }

            foreach (var (inst, pc) in _gpuMemCounters)
            {
                if (!TryParsePid(inst, out int pid)) continue;
                float v;
                try { v = pc.NextValue(); } catch { continue; }
                if (v <= 16 * 1024 * 1024f) continue; // <16MB 忽略
                mem[pid] = Math.Max(mem.GetValueOrDefault(pid), v / (1024.0 * 1024.0));
            }
        }
        catch
        {
            _gpuUnavailable = true;
            DisposeGpuCounters();
        }

        return new GpuSample(util, mem);
    }

    private void ApplyGpu(Dictionary<int, ProcSample> samples, GpuSample gpu)
    {
        foreach (var kv in samples)
        {
            kv.Value.GpuPercent = gpu.UtilByPid.GetValueOrDefault(kv.Key);
            kv.Value.GpuDedicatedMb = gpu.DedicatedMbByPid.GetValueOrDefault(kv.Key);
        }

        // GPU 有、进程表没有（刚退出）时补一条
        foreach (var (pid, util) in gpu.UtilByPid)
        {
            if (samples.ContainsKey(pid)) continue;
            samples[pid] = new ProcSample
            {
                ProcessId = pid,
                ProcessName = ResolveName(pid),
                GpuPercent = util,
                GpuDedicatedMb = gpu.DedicatedMbByPid.GetValueOrDefault(pid)
            };
        }
    }

    private void EnsureGpuCache()
    {
        if ((DateTime.UtcNow - _gpuCacheBuiltUtc).TotalSeconds < 30 && _gpuUtilCounters.Count > 0)
            return;

        DisposeGpuCounters();
        _gpuCacheBuiltUtc = DateTime.UtcNow;

        try
        {
            // 3.24.0：类别存在性只检测一次（Exists 单次约 400ms，旧代码在循环内反复调用）
            if (!_gpuEngineCategoryAvailable)
            {
                _gpuEngineCategoryAvailable = PerformanceCounterCategory.Exists("GPU Engine");
                if (!_gpuEngineCategoryAvailable) { _gpuUnavailable = true; return; }
            }
            bool memCategoryAvailable = _gpuMemCategoryAvailable ??= PerformanceCounterCategory.Exists("GPU Process Memory");

            // 3.24.0：只保留 3D / VideoDecode 引擎（任务管理器 GPU 列口径），且每进程每引擎仅 1 个实例。
            // 旧代码对全部引擎（Copy/Cuda/Security/Compute…）建最多 257 个计数器，一轮 NextValue ≈370ms。
            var seenUtil = new HashSet<string>(StringComparer.Ordinal);
            var seenMem = new HashSet<int>();

            var cat = new PerformanceCounterCategory("GPU Engine");
            foreach (var inst in cat.GetInstanceNames())
            {
                if (_gpuUtilCounters.Count >= MaxGpuCounters) break;
                if (!TryParsePid(inst, out int pid)) continue;
                if (!IsWatchedEngine(inst)) continue;
                // 去重键：pid + 引擎类型（同进程多个 eng_N 只取一个，值在 CollectGpu 中按 pid 取 Max）
                string engineKind = inst.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase) ? "vd" : "3d";
                if (!seenUtil.Add(pid + ":" + engineKind)) continue;

                try
                {
                    _gpuUtilCounters[inst] = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                }
                catch { /* 个别实例无权限/已退出 */ }
            }

            // GPU Process Memory 实例名形如 pid_1234_luid_..._phys_0（不带 engtype），天然每进程一个
            if (memCategoryAvailable)
            {
                var memCat = new PerformanceCounterCategory("GPU Process Memory");
                foreach (var inst in memCat.GetInstanceNames())
                {
                    if (_gpuMemCounters.Count >= MaxGpuCounters) break;
                    if (!TryParsePid(inst, out int pid)) continue;
                    if (!seenMem.Add(pid)) continue;
                    try
                    {
                        _gpuMemCounters[inst] = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", inst, readOnly: true);
                    }
                    catch { }
                }
            }

            // 预热一拍（NextValue 需两次才有速率/有效值）
            foreach (var pc in _gpuUtilCounters.Values)
            {
                try { pc.NextValue(); } catch { }
            }
            foreach (var pc in _gpuMemCounters.Values)
            {
                try { pc.NextValue(); } catch { }
            }
        }
        catch
        {
            _gpuUnavailable = true;
            DisposeGpuCounters();
        }
    }

    /// <summary>仅关注 3D 与视频解码引擎（覆盖桌面/游戏/浏览器视频的 GPU 占用口径）。</summary>
    private static bool IsWatchedEngine(string instanceName) =>
        instanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)
        || instanceName.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase);

    private void DisposeGpuCounters()
    {
        foreach (var pc in _gpuUtilCounters.Values)
        {
            try { pc.Dispose(); } catch { }
        }
        foreach (var pc in _gpuMemCounters.Values)
        {
            try { pc.Dispose(); } catch { }
        }
        _gpuUtilCounters.Clear();
        _gpuMemCounters.Clear();
    }

    /// <summary>实例名形如 pid_1234_luid_0x00000000_0x0000d400_phys_0_eng_0_engtype_3D。</summary>
    private static bool TryParsePid(string instanceName, out int pid)
    {
        pid = 0;
        if (string.IsNullOrEmpty(instanceName)) return false;
        var span = instanceName.AsSpan();
        if (!span.StartsWith("pid_".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
        var rest = span[4..];
        int end = 0;
        while (end < rest.Length && char.IsAsciiDigit(rest[end])) end++;
        return int.TryParse(rest[..end], out pid) && pid > 0;
    }

    private static string ResolveName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return string.IsNullOrEmpty(p.ProcessName) ? $"PID {pid}" : p.ProcessName;
        }
        catch
        {
            return $"PID {pid}";
        }
    }

    private sealed class ProcSample
    {
        public int ProcessId;
        public string ProcessName = string.Empty;
        public double CpuTimeMs;
        public double CpuPercent;
        public double MemoryMb;
        public double DiskBytesPerSec;
        public double GpuPercent;
        public double GpuDedicatedMb;
    }
}
