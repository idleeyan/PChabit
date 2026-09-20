namespace PChabit.Core.Entities;

/// <summary>
/// 应用统计日预聚合表（Date + ProcessName 复合主键）。
/// 用于「应用统计」页近 30 天等大范围查询，避免每次从 AppSessions 全量聚合。
/// 分类展示不固化到本表，读取时按当前 ProgramCategoryMappings 重算。
/// </summary>
public class AppDailyStats
{
    /// <summary>日期 (yyyy-MM-dd)，复合主键第 1 列</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>进程名（规范化：小写去 .exe 后缀），复合主键第 2 列</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>当日该应用已结束会话总时长（分钟）</summary>
    public double Minutes { get; set; }

    /// <summary>当日该应用会话数（含未结束会话）</summary>
    public int Sessions { get; set; }

    /// <summary>当日专注分钟数（≥25 分钟且属生产力分类的会话时长）</summary>
    public double FocusMinutes { get; set; }

    /// <summary>当日该应用每小时活跃分钟 JSON：[m0, m1, ..., m23]（24 个元素）</summary>
    public string HourlyJson { get; set; } = "[]";

    /// <summary>最后更新时间</summary>
    public DateTime LastUpdated { get; set; }
}
