using CommunityToolkit.Mvvm.ComponentModel;
using PChabit.HardwareMonitor;
using PChabit.HardwareMonitor.Hardware;
using Serilog;

namespace PChabit.App.ViewModels;

/// <summary>
/// 硬件监控页 ViewModel：订阅 HardwareMonitorService.ValuesUpdated（线程池线程触发），
/// 通过 ViewModelBase.OnPropertyChanged 自动派发到 UI 线程。
/// 颜色以十六进制字符串暴露，由 StringToBrushConverter 在 XAML 侧转为画刷（避免跨线程创建画刷）。
/// </summary>
public partial class HardwareMonitorViewModel : ViewModelBase
{
    private readonly HardwareMonitorService _monitorService;
    private readonly ProcessNetworkMonitor _processNetwork;
    private readonly ProcessResourceMonitor _processResource;
    private bool _subscribed;

    // ---- CPU ----
    [ObservableProperty]
    private double _cpuUsage;
    [ObservableProperty]
    private string _cpuUsageText = "--";
    [ObservableProperty]
    private string _cpuUsageColor = MutedHex;
    [ObservableProperty]
    private string _cpuTempText = "--";
    [ObservableProperty]
    private double _cpuTemp;
    [ObservableProperty]
    private string _cpuTempColor = MutedHex;
    [ObservableProperty]
    private string _cpuTopProcessName = "--";
    [ObservableProperty]
    private string _cpuTopProcessRate = "--";

    // ---- GPU ----
    [ObservableProperty]
    private double _gpuUsage;
    [ObservableProperty]
    private string _gpuUsageText = "--";
    [ObservableProperty]
    private string _gpuUsageColor = MutedHex;
    [ObservableProperty]
    private string _gpuTempText = "--";
    [ObservableProperty]
    private double _gpuTemp;
    [ObservableProperty]
    private string _gpuTempColor = MutedHex;
    [ObservableProperty]
    private string _gpuVramText = "--";
    [ObservableProperty]
    private double _gpuVramPercent;
    [ObservableProperty]
    private string _gpuVramColor = MutedHex;
    [ObservableProperty]
    private string _gpuTopProcessName = "--";
    [ObservableProperty]
    private string _gpuTopProcessRate = "--";

    // ---- 内存 ----
    [ObservableProperty]
    private double _memLoad;
    [ObservableProperty]
    private string _memLoadText = "--";
    [ObservableProperty]
    private string _memUsageText = "--";
    [ObservableProperty]
    private string _memColor = MutedHex;
    [ObservableProperty]
    private string _memTopProcessName = "--";
    [ObservableProperty]
    private string _memTopProcessRate = "--";

    // ---- 磁盘 ----
    [ObservableProperty]
    private string _diskReadText = "--";
    [ObservableProperty]
    private string _diskWriteText = "--";
    [ObservableProperty]
    private double _diskReadMbps;
    [ObservableProperty]
    private double _diskWriteMbps;
    [ObservableProperty]
    private string _diskReadColor = MutedHex;
    [ObservableProperty]
    private string _diskWriteColor = MutedHex;
    [ObservableProperty]
    private double _diskActivity;
    [ObservableProperty]
    private string _diskActivityText = "--";
    [ObservableProperty]
    private string _diskActivityColor = MutedHex;
    [ObservableProperty]
    private string _diskTopProcessName = "--";
    [ObservableProperty]
    private string _diskTopProcessRate = "--";

    // ---- 网络 ----
    [ObservableProperty]
    private string _netUpText = "--";
    [ObservableProperty]
    private string _netDownText = "--";
    [ObservableProperty]
    private double _netUpMbps;
    [ObservableProperty]
    private double _netDownMbps;
    [ObservableProperty]
    private string _netSessionText = "--";
    [ObservableProperty]
    private string _netIpText = "--";
    [ObservableProperty]
    private string _netTopProcessName = "--";
    [ObservableProperty]
    private string _netTopProcessRate = "--";
    [ObservableProperty]
    private string _netProcessTopText = "暂无活跃网络进程";
    [ObservableProperty]
    private string _netTrafficStatsText = "--";

    [ObservableProperty]
    private string _updatedTimeText = "--";

    // 阈值配色（与 App.xaml 调色板一致）；无读数时用中性灰，避免副指标假绿
    private const string SuccessHex = "#10B981";
    private const string WarningHex = "#F59E0B";
    private const string DangerHex = "#EF4444";
    private const string MutedHex = "#64748B";
    private const string NetDownHex = "#0EA5E9";
    private const string NetUpHex = "#512BD4";

    /// <summary>温度进度条满刻度（°C）。</summary>
    public double TempBarMax => 100;

    /// <summary>磁盘读写进度条参考满刻度（MB/s）；超过则条打满，颜色仍按阈值。</summary>
    public double DiskSpeedBarMax => Math.Max(20, Math.Max(DiskReadMbps, DiskWriteMbps) * 1.25);

    /// <summary>网络上下行进度条参考满刻度（MB/s）。</summary>
    public double NetSpeedBarMax => Math.Max(1, Math.Max(NetDownMbps, NetUpMbps) * 1.25);

    public HardwareMonitorViewModel(HardwareMonitorService monitorService, ProcessNetworkMonitor processNetwork, ProcessResourceMonitor processResource)
    {
        _monitorService = monitorService;
        _processNetwork = processNetwork;
        _processResource = processResource;
    }

    /// <summary>页面进入时订阅（在 UI 线程调用）。</summary>
    public void Subscribe()
    {
        if (_subscribed) return;
        _monitorService.ValuesUpdated += OnValuesUpdated;
        _subscribed = true;
        // 3.24.0：进程资源（含 PDH GPU 计数器）仅页面可见时采样，离开即停
        _processResource.Start();
        RefreshValues(); // 立即刷新一次
    }

    /// <summary>页面离开时取消订阅（在 UI 线程调用）。</summary>
    public void Unsubscribe()
    {
        if (!_subscribed) return;
        _monitorService.ValuesUpdated -= OnValuesUpdated;
        _subscribed = false;
        _processResource.Stop();
    }

    // 回调运行于线程池 Timer 线程；[ObservableProperty] 通知由基类自动派发到 UI 线程
    private void OnValuesUpdated() => RefreshValues();

    private void RefreshValues()
    {
        try
        {
            // ---- CPU ----
            var cpu = _monitorService.Get("CPU.Load");
            CpuUsage = cpu ?? 0;
            CpuUsageText = FormatPercent(cpu);
            CpuUsageColor = ColorFor(cpu, 70, 90);

            var cpuTemp = _monitorService.Get("CPU.Temp");
            CpuTempText = FormatTemp(cpuTemp);
            CpuTemp = ValidTemp(cpuTemp) ? cpuTemp!.Value : 0;
            CpuTempColor = ColorFor(cpuTemp, 70, 85);

            // ---- GPU ----
            var gpu = _monitorService.Get("GPU.Load");
            GpuUsage = gpu ?? 0;
            GpuUsageText = FormatPercent(gpu);
            GpuUsageColor = ColorFor(gpu, 70, 90);

            var gpuTemp = _monitorService.Get("GPU.Temp");
            GpuTempText = FormatTemp(gpuTemp);
            GpuTemp = ValidTemp(gpuTemp) ? gpuTemp!.Value : 0;
            GpuTempColor = ColorFor(gpuTemp, 75, 90);

            var vramUsed = _monitorService.Get("GPU.VRAM.Used");
            var vramTotal = _monitorService.Get("GPU.VRAM.Total");
            GpuVramText = FormatCapacityPair(vramUsed, vramTotal);
            GpuVramPercent = PercentOf(vramUsed, vramTotal);
            GpuVramColor = ColorFor(HasCapacityPair(vramUsed, vramTotal) ? (float?)GpuVramPercent : null, 80, 95);

            // ---- 内存 ----
            var mem = _monitorService.Get("MEM.Load");
            MemLoad = mem ?? 0;
            MemLoadText = FormatPercent(mem);
            MemColor = ColorFor(mem, 70, 90);

            float totalMb = HardwareMonitorOptions.DetectedRamTotalGB > 0
                ? HardwareMonitorOptions.DetectedRamTotalGB * 1024f
                : 0f;
            MemUsageText = FormatCapacityPair(_monitorService.Get("MEM.Used"), totalMb > 0 ? totalMb : null, inMb: true);

            // ---- 磁盘 ----
            var diskRead = _monitorService.Get("DISK.Read");
            var diskWrite = _monitorService.Get("DISK.Write");
            DiskReadMbps = ToMbps(diskRead);
            DiskWriteMbps = ToMbps(diskWrite);
            DiskReadText = FormatSpeed(diskRead);
            DiskWriteText = FormatSpeed(diskWrite);
            DiskReadColor = ColorForSpeed(diskRead, 50, 150);
            DiskWriteColor = ColorForSpeed(diskWrite, 50, 150);
            var diskAct = _monitorService.Get("DISK.Activity");
            DiskActivity = diskAct ?? 0;
            DiskActivityText = FormatPercent(diskAct);
            DiskActivityColor = ColorFor(diskAct, 70, 90);

            // ---- 网络 ----
            var netUp = _monitorService.Get("NET.Up");
            var netDown = _monitorService.Get("NET.Down");
            NetUpMbps = ToMbps(netUp);
            NetDownMbps = ToMbps(netDown);
            NetUpText = FormatSpeed(netUp);
            NetDownText = FormatSpeed(netDown);

            long up = _monitorService.Options.SessionUploadBytes;
            long down = _monitorService.Options.SessionDownloadBytes;
            NetSessionText = $"本会话 上传 {FormatTotalBytes(up)} · 下载 {FormatTotalBytes(down)}";
            NetIpText = string.IsNullOrWhiteSpace(_monitorService.GetNetworkIP()) ? "--" : _monitorService.GetNetworkIP();

            RefreshProcessNetwork();
            RefreshProcessResources();

            UpdatedTimeText = DateTime.Now.ToString("HH:mm:ss");

            // 计算属性：磁盘/网络进度条满刻度随峰值变化
            OnPropertyChanged(nameof(DiskSpeedBarMax));
            OnPropertyChanged(nameof(NetSpeedBarMax));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "硬件监控数值刷新失败");
        }
    }

    /// <summary>各卡片「最大占用进程」：CPU / 内存 / 磁盘 / GPU。</summary>
    private void RefreshProcessResources()
    {
        var snap = _processResource.GetSnapshot();

        if (snap.TopCpu is { } cpu)
        {
            CpuTopProcessName = DisplayProcessName(cpu.ProcessName, cpu.ProcessId);
            CpuTopProcessRate = $"{cpu.CpuPercent:F1}%";
        }
        else
        {
            CpuTopProcessName = "--";
            CpuTopProcessRate = "--";
        }

        if (snap.TopMemory is { } mem)
        {
            MemTopProcessName = DisplayProcessName(mem.ProcessName, mem.ProcessId);
            MemTopProcessRate = FormatMbValue(mem.MemoryMb);
        }
        else
        {
            MemTopProcessName = "--";
            MemTopProcessRate = "--";
        }

        if (snap.TopDisk is { } disk)
        {
            DiskTopProcessName = DisplayProcessName(disk.ProcessName, disk.ProcessId);
            DiskTopProcessRate = FormatSpeedValue(disk.DiskBytesPerSec);
        }
        else
        {
            DiskTopProcessName = "--";
            DiskTopProcessRate = "--";
        }

        // GPU：优先 GPU 引擎占用；无引擎数据时回退专用显存 Top
        if (snap.TopGpu is { } gpu)
        {
            GpuTopProcessName = DisplayProcessName(gpu.ProcessName, gpu.ProcessId);
            GpuTopProcessRate = $"{gpu.GpuPercent:F1}%" + (gpu.GpuDedicatedMb > 16 ? $" · {FormatMbValue(gpu.GpuDedicatedMb)}" : "");
        }
        else if (snap.TopGpuMemory is { } gpuMem)
        {
            GpuTopProcessName = DisplayProcessName(gpuMem.ProcessName, gpuMem.ProcessId);
            GpuTopProcessRate = FormatMbValue(gpuMem.GpuDedicatedMb);
        }
        else
        {
            GpuTopProcessName = "--";
            GpuTopProcessRate = "--";
        }
    }

    private static string DisplayProcessName(string name, int pid)
        => string.IsNullOrWhiteSpace(name) ? $"PID {pid}" : name;

    private static string FormatMbValue(double mb)
    {
        if (mb <= 0) return "--";
        return mb >= 1024 ? $"{mb / 1024:F2} GB" : $"{mb:F0} MB";
    }

    /// <summary>进程网络：最大占用进程 + Top 列表 + 流量统计。</summary>
    private void RefreshProcessNetwork()
    {
        var snap = _processNetwork.GetSnapshot(5);
        var top = snap.TopByRate;
        if (top == null)
        {
            NetTopProcessName = "--";
            NetTopProcessRate = "--";
            NetProcessTopText = "暂无活跃网络进程";
        }
        else
        {
            NetTopProcessName = string.IsNullOrWhiteSpace(top.ProcessName) ? $"PID {top.ProcessId}" : top.ProcessName;
            NetTopProcessRate = FormatProcessRate(top);
            var lines = snap.TopByRateList
                .Select((p, i) => $"{i + 1}. {p.ProcessName}  {FormatProcessRate(p)}")
                .ToList();
            NetProcessTopText = lines.Count > 0 ? string.Join("\n", lines) : "暂无活跃网络进程";
        }

        // 流量统计：系统会话/今日 + 进程今日 Top（本程序采样口径，类似系统「数据使用量」）
        var stats = new List<string>
        {
            $"系统会话 ↑{FormatTotalBytes(snap.SystemSessionUpBytes)} ↓{FormatTotalBytes(snap.SystemSessionDownBytes)}",
            $"系统今日 ↑{FormatTotalBytes(snap.SystemTodayUpBytes)} ↓{FormatTotalBytes(snap.SystemTodayDownBytes)}"
        };

        if (snap.TopByToday.Count > 0)
        {
            stats.Add("今日进程累计:");
            foreach (var p in snap.TopByToday.Take(3))
            {
                var total = p.TodayDownBytes + p.TodayUpBytes;
                stats.Add($"  {p.ProcessName}  {FormatTotalBytes(total)}");
            }
        }

        NetTrafficStatsText = string.Join("\n", stats);
    }

    private static string FormatProcessRate(ProcessNetworkItem item)
    {
        return $"↓{FormatSpeedValue(item.DownBytesPerSec)} ↑{FormatSpeedValue(item.UpBytesPerSec)}";
    }

    private static string FormatSpeedValue(double bytesPerSec)
    {
        if (bytesPerSec < 0) return "--";
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F1} KB/s";
        return $"{bytesPerSec / 1024 / 1024:F2} MB/s";
    }

    // ================= 格式化与配色 =================

    private static string FormatPercent(float? v)
        => v.HasValue && !float.IsNaN(v.Value) ? $"{v.Value:F0}%" : "--";

    private static string FormatTemp(float? v)
        => ValidTemp(v) ? $"{v!.Value:F0}°C" : "--";

    private static bool ValidTemp(float? v)
        => v.HasValue && !float.IsNaN(v.Value) && v.Value > 0;

    /// <summary>传感器速度为 B/s，换算为 MB/s 供进度条使用；无效为 0。</summary>
    private static double ToMbps(float? bytesPerSec)
    {
        if (!bytesPerSec.HasValue || float.IsNaN(bytesPerSec.Value) || bytesPerSec.Value < 0) return 0;
        return bytesPerSec.Value / (1024.0 * 1024.0);
    }

    private static bool HasCapacityPair(float? usedRaw, float? totalRaw)
        => NormalizeToMb(usedRaw).HasValue || NormalizeToMb(totalRaw).HasValue;

    /// <summary>磁盘读写颜色：单位 MB/s，参考阈值 50 / 150。</summary>
    private static string ColorForSpeed(float? bytesPerSec, double warnMBps, double critMBps)
        => ColorFor(bytesPerSec.HasValue && !float.IsNaN(bytesPerSec.Value) && bytesPerSec.Value >= 0
            ? (float)ToMbps(bytesPerSec)
            : null, warnMBps, critMBps);

    private static string FormatSpeed(float? bytesPerSec)
    {
        if (!bytesPerSec.HasValue || float.IsNaN(bytesPerSec.Value) || bytesPerSec.Value < 0) return "--";
        double v = bytesPerSec.Value;
        if (v < 1024) return $"{v:F0} B/s";
        if (v < 1024 * 1024) return $"{v / 1024:F1} KB/s";
        if (v < 1024 * 1024 * 1024) return $"{v / 1024 / 1024:F2} MB/s";
        return $"{v / 1024 / 1024 / 1024:F2} GB/s";
    }

    private static string FormatTotalBytes(long bytes)
    {
        if (bytes < 0) return "--";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }

    /// <summary>
    /// 容量对显示。LHM 内存传感器单位为 MB；显存传感器单位为字节，
    /// 先按上游 HardwareValueProvider 的规则归一化到 MB 再格式化。
    /// </summary>
    private static string FormatCapacityPair(float? usedRaw, float? totalRaw, bool inMb = false)
    {
        float? usedMb = inMb ? usedRaw : NormalizeToMb(usedRaw);
        float? totalMb = inMb ? totalRaw : NormalizeToMb(totalRaw);
        if (!usedMb.HasValue && !totalMb.HasValue) return "--";
        return $"{FormatMb(usedMb)} / {FormatMb(totalMb)}";
    }

    private static string FormatMb(float? mb)
    {
        if (!mb.HasValue || float.IsNaN(mb.Value) || mb.Value <= 0) return "--";
        if (mb.Value >= 1024) return $"{mb.Value / 1024f:F1} GB";
        return $"{mb.Value:F0} MB";
    }

    /// <summary>与上游一致：值大于 10MB 视为字节，除以 1048576 归一化为 MB。</summary>
    private static float? NormalizeToMb(float? raw)
    {
        if (!raw.HasValue || float.IsNaN(raw.Value) || raw.Value <= 0) return null;
        return raw.Value > 10485760f ? raw.Value / 1048576f : raw.Value;
    }

    private static double PercentOf(float? used, float? total)
    {
        if (used.HasValue && total.HasValue && total.Value > 0 && used.Value >= 0)
        {
            return Math.Clamp(used.Value / total.Value * 100.0, 0, 100);
        }
        return 0;
    }

    private static string ColorFor(float? value, double warn, double crit)
    {
        if (!value.HasValue || float.IsNaN(value.Value)) return MutedHex;
        if (value.Value >= crit) return DangerHex;
        if (value.Value >= warn) return WarningHex;
        return SuccessHex;
    }
}
