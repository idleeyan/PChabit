namespace PChabit.Core.Interfaces;

// ============================================================================
// 硬件遥测 / 进程网络 数据源接口（工程改造 E2-a 修订版，2026-11-06 用户拍板「接口下沉 Core」）
//
// 背景：HardwareSampleWriter / NetworkTrafficPersistenceService 原直接依赖
// PChabit.HardwareMonitor（平台 TFM net10.0-windows10.0.22621.0），无法迁往
// Infrastructure（基础 TFM net10.0）。本组接口把两服务的消费面抽象为纯数据契约，
// 消费方只依赖本接口，HardwareMonitor 的桥接由 App 侧适配器完成。
// 物理迁移 Infrastructure 留作后续独立批次（解耦后已无技术阻塞）。
//
// 设计约束：本文件零第三方依赖、零 WinRT 类型；载荷只含基元类型与元组，
// 保持 Core 层零依赖原则（见 AI_MAINTENANCE.md「分层边界规约」）。
// ============================================================================

/// <summary>
/// 硬件遥测数据源（对接 <c>HardwareMonitorService</c> 的 1s 采样）。
/// 消费方订阅 <see cref="ValuesUpdated"/> 后经 <see cref="Get"/> 按传感器键取值。
/// </summary>
public interface IHardwareTelemetrySource
{
    /// <summary>1 秒采样完成通知（线程池线程触发，处理必须非阻塞、自带异常防护）。</summary>
    event Action? ValuesUpdated;

    /// <summary>按传感器键取最新值（如 "CPU.Load"）；传感器缺失返回 null。</summary>
    float? Get(string key);
}

/// <summary>进程网络流量数据源（对接 <c>ProcessNetworkMonitor</c> 的轮询读取）。</summary>
public interface IProcessNetworkSource
{
    /// <summary>系统级今日累计流量（元组：TodayUp / TodayDown，另含会话累计）。</summary>
    (long SessionUp, long SessionDown, long TodayUp, long TodayDown) GetSystemTotals();

    /// <summary>全部被跟踪进程的今日累计（用于落库增量计算）。</summary>
    List<NetworkProcessTotals> GetAllTodayProcesses();

    /// <summary>当前速率 Top N 快照（用于峰值估计；返回项仅含速率字段有意义）。</summary>
    List<NetworkRateItem> GetTopRates(int topCount);

    /// <summary>把数据库历史回填为监视器「今日累计」初始值（进程维度）。</summary>
    void SeedTodayFrom(IEnumerable<(string ProcessName, long BytesUp, long BytesDown)> items);

    /// <summary>把数据库历史回填为监视器「今日累计」初始值（系统维度）。</summary>
    void SeedSystemToday(long up, long down);
}

/// <summary>进程今日流量累计（纯数据载荷，字段与 <c>ProcessNetworkItem</c> 消费面一致）。</summary>
public sealed class NetworkProcessTotals
{
    public string ProcessName { get; init; } = string.Empty;
    /// <summary>今日累计上行字节。</summary>
    public long TodayUpBytes { get; init; }
    /// <summary>今日累计下行字节。</summary>
    public long TodayDownBytes { get; init; }
    /// <summary>本采样周期上行 B/s（峰值估计用）。</summary>
    public double UpBytesPerSec { get; init; }
    /// <summary>本采样周期下行 B/s（峰值估计用）。</summary>
    public double DownBytesPerSec { get; init; }
}

/// <summary>进程当前速率条目（纯数据载荷）。</summary>
public sealed class NetworkRateItem
{
    public double UpBytesPerSec { get; init; }
    public double DownBytesPerSec { get; init; }
}
