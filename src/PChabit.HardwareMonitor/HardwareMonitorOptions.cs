namespace PChabit.HardwareMonitor;

/// <summary>
/// 硬件监控配置（替代 LiteMonitor 的 Settings，仅保留 PChabit 需要的成员）。
/// 上游：LiteMonitor.src.Core.Settings（MIT License），见 NOTICE.md。
/// </summary>
public sealed class HardwareMonitorOptions
{
    /// <summary>采集刷新间隔（毫秒）。</summary>
    public int RefreshMs { get; set; } = 1000;

    /// <summary>使用 Windows 性能计数器（CPU/内存/磁盘）作为主要数据源。</summary>
    public bool UseWinPerCounters { get; set; } = true;

    /// <summary>网页服务开关（PChabit v1 不启用，保留字段保持与上游逻辑兼容）。</summary>
    public bool WebServerEnabled { get; set; } = false;

    /// <summary>忽略 SMB（内网文件共享）流量。</summary>
    public bool IgnoreSmbTraffic { get; set; } = true;

    /// <summary>首选监控项（用户手动指定时为非空；自动模式为空字符串）。</summary>
    public string? PreferredNetwork { get; set; }
    public string? PreferredDisk { get; set; }
    public string? PreferredGpu { get; set; }
    public string? PreferredCpuFan { get; set; }
    public string? PreferredCpuPump { get; set; }
    public string? PreferredCaseFan { get; set; }
    public string? PreferredMoboTemp { get; set; }

    /// <summary>自动选择的设备（由采集层写入）。</summary>
    public string? LastAutoDisk { get; set; }
    public string? LastAutoNetwork { get; set; }

    /// <summary>本次会话累计流量（字节）。</summary>
    public long SessionUploadBytes { get; set; }
    public long SessionDownloadBytes { get; set; }

    /// <summary>静态检测结果（LHM/性能计数器初始化后写入）。</summary>
    public static float DetectedRamTotalGB { get; set; }
    public static float DetectedGpuVramTotalGB { get; set; }

    /// <summary>
    /// 监控项开关。PChabit v1 核心监控项全开；
    /// 风扇/水泵/机箱风扇等控制器项默认关闭（避免开启 LHM 的 USB 控制器扫描）。
    /// </summary>
    public bool IsAnyEnabled(string key)
    {
        switch (key)
        {
            case "CPU.Fan":
            case "CPU.Pump":
            case "CASE.Fan":
                return false;
            default:
                return true;
        }
    }

    // 风扇/水泵等项目的最大值记录（上游 Settings.UpdateMaxRecord 的简化实现）
    private readonly Dictionary<string, float> _maxRecords = new();

    /// <summary>记录某监控项的历史最大值。</summary>
    public void UpdateMaxRecord(string key, float value)
    {
        lock (_maxRecords)
        {
            if (!_maxRecords.TryGetValue(key, out float current) || value > current)
            {
                _maxRecords[key] = value;
            }
        }
    }

    /// <summary>获取某监控项的历史最大值（无记录时返回 null）。</summary>
    public float? GetMaxRecord(string key)
    {
        lock (_maxRecords)
        {
            return _maxRecords.TryGetValue(key, out float v) ? v : null;
        }
    }
}
