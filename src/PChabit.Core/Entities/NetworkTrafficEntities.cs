namespace PChabit.Core.Entities;

/// <summary>
/// 系统级网络分钟样本（IP Helper/网卡速率采样累计）。
/// Timestamp：本地 yyyy-MM-dd HH:mm；业务唯一。
/// </summary>
public class NetworkTrafficSample
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>本地时间分钟桶 (yyyy-MM-dd HH:mm)</summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>该分钟上行总字节</summary>
    public long BytesUp { get; set; }

    /// <summary>该分钟下行总字节</summary>
    public long BytesDown { get; set; }

    /// <summary>该分钟峰值上行 B/s</summary>
    public double PeakUpBps { get; set; }

    /// <summary>该分钟峰值下行 B/s</summary>
    public double PeakDownBps { get; set; }

    /// <summary>采样次数（约 4 次/分钟，若 15s 刷盘）</summary>
    public int SampleCount { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

/// <summary>
/// 进程网络流量按「日 + 小时」聚合（独立统计页数据源）。
/// 主键：Date + Hour + ProcessName。
/// </summary>
public class ProcessNetworkUsage
{
    /// <summary>yyyy-MM-dd（本地）</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>0-23</summary>
    public int Hour { get; set; }

    /// <summary>进程名（不区分大小写）</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>该小时上行字节</summary>
    public long BytesUp { get; set; }

    /// <summary>该小时下行字节</summary>
    public long BytesDown { get; set; }

    /// <summary>该小时峰值总速率 B/s</summary>
    public long PeakBytesPerSec { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.Now;
}
