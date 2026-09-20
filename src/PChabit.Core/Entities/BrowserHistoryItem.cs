using System.ComponentModel.DataAnnotations;

namespace PChabit.Core.Entities;

/// <summary>
/// 浏览器 History API 条目（与 WebSessions 停留时长统计语义不同，勿合并）。
/// </summary>
public class BrowserHistoryItem
{
    [Key]
    [MaxLength(500)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>lastVisitTime，Unix ms</summary>
    public long VisitTime { get; set; }

    public int VisitCount { get; set; }

    [MaxLength(50)]
    public string SourceBrowser { get; set; } = string.Empty;

    public long IngestedAt { get; set; }
}
