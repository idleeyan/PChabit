using PChabit.Core.Interfaces;

namespace PChabit.Core.Entities;

/// <summary>
/// AI 深度解读快照：本地留存，供下周「计划点评」与追问上下文。
/// 不存窗口标题/URL；Summary/Plan 为结构化 JSON 字符串。
/// </summary>
public class AiInsightSnapshot : EntityBase
{
    /// <summary>周期键，如 2026-W40 或 2026-09-22（周期起始日）</summary>
    public string PeriodKey { get; set; } = string.Empty;

    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public string? Summary { get; set; }
    /// <summary>findings JSON</summary>
    public string? FindingsJson { get; set; }
    /// <summary>plan JSON：[{title,detail,targetMetricId,targetValue,status}]</summary>
    public string? PlanJson { get; set; }
    /// <summary>diagnosis JSON</summary>
    public string? DiagnosisJson { get; set; }
    /// <summary>risks JSON</summary>
    public string? RisksJson { get; set; }

    public string? Model { get; set; }
    public string PromptVersion { get; set; } = "v2";
    public DateTime CreatedAt { get; set; }
}
