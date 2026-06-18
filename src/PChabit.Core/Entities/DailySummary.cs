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
    
    /// <summary>最后更新时间</summary>
    public DateTime LastUpdated { get; set; }
}
