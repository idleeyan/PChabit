// 类 HardwareMonitor 与子命名空间 PChabit.HardwareMonitor.Hardware 同名，需别名消歧
using Hardware = PChabit.HardwareMonitor.Hardware;

namespace PChabit.HardwareMonitor;

/// <summary>
/// 硬件监控服务：PChabit 生命周期包装（Start/Stop + 周期采集 Timer）。
/// 采集逻辑来自 LiteMonitor 移植核心（见 NOTICE.md）。
/// 注意：ValuesUpdated 事件触发于线程池 Timer 线程，
/// UI 侧订阅时必须自行派发到 UI 线程（DispatcherQueue.TryEnqueue）。
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly HardwareMonitorOptions _options = new();
    private readonly object _sync = new();
    private Hardware.HardwareMonitor? _monitor;
    private System.Threading.Timer? _timer;

    /// <summary>采集数值更新事件（线程池线程触发）。</summary>
    public event Action? ValuesUpdated;

    /// <summary>采集配置（可运行时调整）。</summary>
    public HardwareMonitorOptions Options => _options;

    /// <summary>是否正在运行。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>启动硬件监控（非阻塞：硬件初始化在后台线程完成）。</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning) return;

            _monitor = new Hardware.HardwareMonitor(_options);
            _monitor.OnValuesUpdated += OnValuesUpdated;

            _timer = new System.Threading.Timer(
                Tick,
                null,
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);
            _timer.Change(500, Math.Max(200, _options.RefreshMs));

            IsRunning = true;
        }
    }

    /// <summary>停止硬件监控并释放资源。</summary>
    public void Stop()
    {
        lock (_sync)
        {
            if (!IsRunning) return;
            IsRunning = false;

            _timer?.Dispose();
            _timer = null;

            if (_monitor != null)
            {
                _monitor.OnValuesUpdated -= OnValuesUpdated;
                _monitor.Dispose();
                _monitor = null;
            }
        }
    }

    /// <summary>读取监控值（如 "CPU.Load"、"GPU.Temp"、"MEM.Load"、"NET.Down"、"DISK.Read"）。</summary>
    public float? Get(string key) => _monitor?.Get(key);

    /// <summary>获取当前内网 IP（带缓存）。</summary>
    public string GetNetworkIP() => _monitor?.GetNetworkIP() ?? "";

    private void OnValuesUpdated() => ValuesUpdated?.Invoke();

    private void Tick(object? state)
    {
        try
        {
            _monitor?.UpdateAll();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HardwareMonitorService] Tick Error: {ex.Message}");
        }
    }

    public void Dispose() => Stop();
}
