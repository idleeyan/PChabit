namespace PChabit.Core.Entities;

/// <summary>
/// 硬件分钟样本（1s 采集按本地时间分钟桶聚合，P0 分析底座，见分析升级计划）。
/// 时间语义与全库一致：本地时间、字符串存储；休眠/关机缺分钟无行（不补 0）。
/// 不可用传感器列为 null。
/// </summary>
public class HardwareSample : EntityBase
{
    /// <summary>本地时间分钟桶 (yyyy-MM-dd HH:mm)，业务唯一键</summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>CPU 负载 %：分钟均值 / 峰值</summary>
    public double? CpuLoadAvg { get; set; }
    public double? CpuLoadMax { get; set; }

    /// <summary>CPU 温度 ℃：分钟峰值</summary>
    public double? CpuTempMax { get; set; }

    /// <summary>GPU 负载 %：分钟均值 / 峰值</summary>
    public double? GpuLoadAvg { get; set; }
    public double? GpuLoadMax { get; set; }

    /// <summary>GPU 温度 ℃：分钟峰值</summary>
    public double? GpuTempMax { get; set; }

    /// <summary>显存占用 GB 峰值 / 显存总量 GB 快照</summary>
    public double? VramUsedMax { get; set; }
    public double? VramTotal { get; set; }

    /// <summary>内存负载 % 均值 / 峰值；内存占用 GB 峰值</summary>
    public double? MemLoadAvg { get; set; }
    public double? MemLoadMax { get; set; }
    public double? MemUsedMax { get; set; }

    /// <summary>磁盘活动率 % 均值；读写速度 字节/秒 峰值</summary>
    public double? DiskActivityAvg { get; set; }
    public double? DiskReadMax { get; set; }
    public double? DiskWriteMax { get; set; }

    /// <summary>网络上下行速度 字节/秒 均值（会话累计流量另从 HardwareMonitorOptions 取）</summary>
    public double? NetUpAvg { get; set; }
    public double? NetDownAvg { get; set; }

    /// <summary>桶内采样次数（1s 一次：满分钟约 60；关机前残余分钟可小于 60）</summary>
    public int SampleCount { get; set; }

    /// <summary>有效传感器位图（诊断该分钟哪些采集键取到了值，位定义见 HardwareSensorFlags）</summary>
    public int SensorFlags { get; set; }
}

/// <summary>HardwareSample.SensorFlags 位定义（与硬件页 Get(key) 13 键对齐）。</summary>
[Flags]
public enum HardwareSensorFlags
{
    None = 0,
    CpuLoad = 1 << 0,
    CpuTemp = 1 << 1,
    GpuLoad = 1 << 2,
    GpuTemp = 1 << 3,
    VramUsed = 1 << 4,
    VramTotal = 1 << 5,
    MemLoad = 1 << 6,
    MemUsed = 1 << 7,
    DiskActivity = 1 << 8,
    DiskRead = 1 << 9,
    DiskWrite = 1 << 10,
    NetUp = 1 << 11,
    NetDown = 1 << 12
}
