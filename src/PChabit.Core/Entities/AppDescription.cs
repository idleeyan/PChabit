using System.ComponentModel.DataAnnotations;

namespace PChabit.Core.Entities;

/// <summary>
/// 应用详细说明（进程 → 说明文本）。
/// 内置预置库种子写入（IsUserEdited=false），用户在界面修改后置 IsUserEdited=true，种子不再覆盖。
/// ProcessName 一律存规范化形式（小写、无 .exe），查询时用 <c>AppStatsEngine.NormalizeProcessName</c> 归一。
/// </summary>
public class AppDescription
{
    [Key]
    public int Id { get; set; }

    /// <summary>规范化进程名：小写、无 .exe、去空白（如 "chrome" / "potplayermini64"）。</summary>
    [Required]
    [MaxLength(100)]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>应用详细说明正文（TEXT，可长文）。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>是否用户手动编辑过。true 时预置种子不再覆盖该行。</summary>
    public bool IsUserEdited { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }
}
