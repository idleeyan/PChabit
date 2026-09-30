namespace PChabit.Core.Entities;

public class DailySummary : EntityBase
{
    /// <summary>日期 (yyyy-MM-dd)，业务主键</summary>
    public string Date { get; set; } = string.Empty;
    
    /// <summary>总按键次数</summary>
    public long TotalKeys { get; set; }
    
    /// <summary>总鼠标点击次数</summary>
    public long TotalMouseClicks { get; set; }
    
    /// <summary>活跃分钟数</summary>
    public double ActiveMinutes { get; set; }
    
    /// <summary>Top 应用 JSON: [{"name":"微信","minutes":120}]</summary>
    public string TopApps { get; set; } = "[]";
    
    /// <summary>每小时按键分布 JSON: [120, 300, ...] (24 个元素)</summary>
    public string HourlyKeyDistribution { get; set; } = "[]";

    /// <summary>当日网页访问次数（真实会话条数）</summary>
    public int WebPages { get; set; }

    /// <summary>当日网页墙钟总时长（Ticks）</summary>
    public long WebDurationTicks { get; set; }

    /// <summary>当日网页有效浏览总时长（Ticks）</summary>
    public long WebActiveDurationTicks { get; set; }

    // ===== 分析升级 P0 扩展列（均可空；null = 尚未按 v2 口径产出）=====

    /// <summary>当日应用切换次数（P1 v2 引擎填充）</summary>
    public int? AppSwitches { get; set; }

    /// <summary>v2 口径专注总分钟 / 专注段数 / 平均专注质量 0-100（P1 填充）</summary>
    public double? FocusMinutesV2 { get; set; }
    public int? FocusCountV2 { get; set; }
    public double? FocusQualityAvg { get; set; }

    /// <summary>当日网页有效浏览分钟数（P0 即可由 WebActiveDurationTicks 换算填充）</summary>
    public double? WebMinutes { get; set; }

    // ----- 硬件日汇总（P0：由 HardwareSamples 分钟样本聚合）-----
    /// <summary>CPU 日均负载 % / 日 P95 负载 %</summary>
    public double? CpuLoadAvg { get; set; }
    public double? CpuLoadP95 { get; set; }
    /// <summary>GPU 日均负载 % / 日最高温度 ℃</summary>
    public double? GpuLoadAvg { get; set; }
    public double? GpuTempMax { get; set; }
    /// <summary>内存日均负载 %</summary>
    public double? MemLoadAvg { get; set; }
    /// <summary>当日网络上下行总字节（预留；分钟样本仅存速率均值，P1 接入会话累计口径后填充）</summary>
    public long? NetBytesUp { get; set; }
    public long? NetBytesDown { get; set; }

    /// <summary>指标口径版本：null/1 = v1 行为数据；2 = 硬件/专注扩展；3 = 活动标签/夜间/起止</summary>
    public int? MetricsVersion { get; set; }

    /// <summary>活动标签分钟 JSON：{"work-code":120,...}（ActivityLabels 字面量键）</summary>
    public string? LabelMinutesJson { get; set; }

    /// <summary>夜间分钟（23:00–06:00 与会话重叠）</summary>
    public double? NightMinutes { get; set; }

    /// <summary>当日首次活跃时刻 "HH:mm"；无数据为空</summary>
    public string? FirstActiveTime { get; set; }

    /// <summary>当日最后活跃时刻 "HH:mm"；无数据为空</summary>
    public string? LastActiveTime { get; set; }

    /// <summary>最后更新时间</summary>
    public DateTime LastUpdated { get; set; }
}
