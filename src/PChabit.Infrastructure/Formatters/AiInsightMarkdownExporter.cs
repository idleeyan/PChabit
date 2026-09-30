using System.Text;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Services;

namespace PChabit.Infrastructure.Formatters;

/// <summary>把 AI 解读导出为 Markdown（可粘贴到笔记或给其它模型）。</summary>
public static class AiInsightMarkdownExporter
{
    public static string Export(
        string periodLabel,
        DateTime periodStart,
        DateTime periodEnd,
        AnalyticsAiResponseParser.ParsedAiResult parsed,
        IReadOnlyList<AiPlanItem>? plan = null,
        HabitProfile? profile = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# PChabit AI 深度解读 · {periodLabel}");
        sb.AppendLine($"{periodStart:yyyy-MM-dd} ~ {periodEnd.AddDays(-1):yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## 结论");
        sb.AppendLine(string.IsNullOrWhiteSpace(parsed.Summary) ? "（无）" : parsed.Summary);
        sb.AppendLine();

        if (parsed.Findings.Count > 0)
        {
            sb.AppendLine("## 发现");
            foreach (var f in parsed.Findings)
            {
                sb.AppendLine($"- **{f.Title}**：{f.Detail}");
                if (f.Evidence is { Count: > 0 } ev)
                {
                    sb.AppendLine("  - 依据：" + string.Join("；", ev.Select(e =>
                        e.Prev is { } p ? $"{e.MetricId} {e.Value}（前 {p}）" : $"{e.MetricId} {e.Value}")));
                }
            }
            sb.AppendLine();
        }

        if (parsed.Diagnosis is { Count: > 0 } diag)
        {
            sb.AppendLine("## 可能原因");
            foreach (var d in diag)
                sb.AppendLine($"- [{d.Confidence}] {d.Hypothesis}");
            sb.AppendLine();
        }

        var planItems = plan
            ?? (parsed.Plan ?? Array.Empty<AnalyticsAiResponseParser.AiPlanItem>())
                .Select(p => new AiPlanItem { Title = p.Title, Detail = p.Detail, ActionKey = p.ActionKey, TargetMetricId = p.TargetMetricId, TargetValue = p.TargetValue, Status = "pending" })
                .ToList();
        if (planItems.Count > 0)
        {
            sb.AppendLine("## 周计划");
            foreach (var p in planItems)
            {
                var target = p.TargetMetricId is { } mid
                    ? $"（目标 {mid}={p.TargetValue}）"
                    : "";
                sb.AppendLine($"- [{p.Status}] **{p.Title}**{target}：{p.Detail}");
            }
            sb.AppendLine();
        }

        if (parsed.Risks.Count > 0)
        {
            sb.AppendLine("## 风险");
            foreach (var r in parsed.Risks)
                sb.AppendLine($"- {r}");
            sb.AppendLine();
        }

        if (profile is not null && profile.Mature)
        {
            sb.AppendLine("## 习惯画像");
            sb.AppendLine($"- 节奏：{profile.Chronotype}；典型 {profile.TypicalStartHour:0.#}–{profile.TypicalEndHour:0.#}");
            sb.AppendLine($"- 中位活跃 {profile.MedianActiveMinutes:0} 分 / 专注 {profile.MedianFocusMinutes:0} 分 / 夜间 {profile.MedianNightMinutes:0} 分");
            sb.AppendLine();
        }

        if (parsed.FollowUps is { Count: > 0 } fu)
        {
            sb.AppendLine("## 可追问");
            foreach (var f in fu)
                sb.AppendLine($"- {f}");
            sb.AppendLine();
        }

        sb.AppendLine("*由 PChabit AI 深度解读生成，仅供自我复盘参考。*");
        return sb.ToString();
    }
}
