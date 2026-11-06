using PChabit.Core.Interfaces;
using PChabit.HardwareMonitor;
using PChabit.HardwareMonitor.Hardware;

namespace PChabit.App.Services;

/// <summary>
/// 硬件遥测数据源适配器（工程改造 E2-a 修订版）：
/// 把 <see cref="HardwareMonitorService"/>（平台 TFM）桥接为 Core 的
/// <see cref="IHardwareTelemetrySource"/>，供 <c>HardwareSampleWriter</c> 等
/// 持久化服务依赖接口而非具体类型。
/// </summary>
public sealed class HardwareTelemetrySourceAdapter : IHardwareTelemetrySource
{
    private readonly HardwareMonitorService _hardware;

    public HardwareTelemetrySourceAdapter(HardwareMonitorService hardware) => _hardware = hardware;

    public event Action? ValuesUpdated
    {
        add => _hardware.ValuesUpdated += value;
        remove => _hardware.ValuesUpdated -= value;
    }

    public float? Get(string key) => _hardware.Get(key);
}

/// <summary>
/// 进程网络数据源适配器（工程改造 E2-a 修订版）：
/// 把 <see cref="ProcessNetworkMonitor"/>（平台 TFM）桥接为 Core 的
/// <see cref="IProcessNetworkSource"/>。
/// </summary>
public sealed class ProcessNetworkSourceAdapter : IProcessNetworkSource
{
    private readonly ProcessNetworkMonitor _monitor;

    public ProcessNetworkSourceAdapter(ProcessNetworkMonitor monitor) => _monitor = monitor;

    public (long SessionUp, long SessionDown, long TodayUp, long TodayDown) GetSystemTotals()
        => _monitor.GetSystemTotals();

    public List<NetworkProcessTotals> GetAllTodayProcesses()
        => _monitor.GetAllTodayProcesses()
            .Select(p => new NetworkProcessTotals
            {
                ProcessName = p.ProcessName,
                TodayUpBytes = p.TodayUpBytes,
                TodayDownBytes = p.TodayDownBytes,
                UpBytesPerSec = p.UpBytesPerSec,
                DownBytesPerSec = p.DownBytesPerSec
            })
            .ToList();

    public List<NetworkRateItem> GetTopRates(int topCount)
        => _monitor.GetSnapshot(topCount)
            .TopByRateList
            .Select(p => new NetworkRateItem
            {
                UpBytesPerSec = p.UpBytesPerSec,
                DownBytesPerSec = p.DownBytesPerSec
            })
            .ToList();

    public void SeedTodayFrom(IEnumerable<(string ProcessName, long BytesUp, long BytesDown)> items)
        => _monitor.SeedTodayFrom(items);

    public void SeedSystemToday(long up, long down)
        => _monitor.SeedSystemToday(up, down);
}
