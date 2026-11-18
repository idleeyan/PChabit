using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PChabit.Infrastructure.Analysis;

/// <summary>分析数据质量：多源覆盖与有效天数。</summary>
public record AnalyticsQuality(
    int AppDays,
    int PeriodDays,
    bool HasInput,
    bool HasWeb,
    bool HasHardware,
    int HardwareSampleMinutes,
    int HardwareSampleDays)
{
    public string Summary
    {
        get
        {
            var sources = new List<string> { "应用" };
            if (HasInput) sources.Add("键鼠");
            if (HasWeb) sources.Add("网页");
            if (HasHardware) sources.Add($"硬件({HardwareSampleDays}天/{HardwareSampleMinutes}分)");
            var cover = PeriodDays > 0 ? $"{AppDays}/{PeriodDays} 天" : $"{AppDays} 天";
            return $"有效 {cover}　数据源：{string.Join(" · ", sources)}";
        }
    }
}

/// <summary>深度周报 / AI 上下文构建（本地规则数据 → Markdown 或结构化 JSON）。</summary>
public static class AnalysisReportBuilder
{
    public static AnalyticsQuality BuildQuality(AnalyticsPeriodReport report)
    {
        var extra = report.Extra;
        return new AnalyticsQuality(
            report.DayCountWithData,
            report.Period.DayCount,
            extra is { HasInputData: true },
            extra is { HasWebData: true },
            report.Hardware is { HasData: true },
            report.Hardware?.SampleMinutes ?? 0,
            report.Hardware?.SampleDays ?? 0);
    }

    /// <summary>完整深度周报 Markdown（可复制给 AI 或存档）。</summary>
    public static string BuildMarkdown(AnalyticsPeriodReport report)
    {
        var sb = new StringBuilder();
        var p = report.Period;
        var quality = BuildQuality(report);

        sb.AppendLine($"# PChabit 深度周报 · {p.Label}");
        sb.AppendLine($"{p.Start:yyyy-MM-dd} ~ {p.EndExclusive.AddDays(-1):yyyy-MM-dd}");
        if (report.Previous != null)
            sb.AppendLine($"对比期：{report.Previous.Start:yyyy-MM-dd} ~ {report.Previous.EndExclusive.AddDays(-1):yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## 数据质量");
        sb.AppendLine(quality.Summary);
        if (!report.HasEnoughData)
            sb.AppendLine("> 有效数据偏少，结论仅供参考。");
        sb.AppendLine();

        sb.AppendLine("## KPI");
        foreach (var k in report.Kpis)
            sb.AppendLine($"- **{k.Name}**：{k.Value}（{k.DeltaText}）— {k.Hint}");

        if (report.Extra != null)
        {
            sb.AppendLine();
            sb.AppendLine("## 多源摘要");
            if (report.Extra.HasInputData)
                sb.AppendLine($"- 键盘 {report.Extra.TotalKeys:N0} 次 · 点击 {report.Extra.TotalClicks:N0} · 密度 {report.Extra.KeysPerActiveHour:F0}/时");
            if (report.Extra.HasWebData)
                sb.AppendLine($"- 网页 {AnalyticsEngine.FormatHours(report.Extra.WebMinutes)} · 占比 {report.Extra.WebSharePct:F0}% · {report.Extra.WebPages:N0} 页");
            if (report.Extra.HasNetData)
                sb.AppendLine($"- 流量 ↓ {FormatGb(report.Extra.NetBytesDown)} · ↑ {FormatGb(report.Extra.NetBytesUp)}");
            if (report.Extra.SearchCount > 0)
                sb.AppendLine($"- 搜索 {report.Extra.SearchCount:N0} 次（仅计数，不存关键词）");
        }

        if (report.WebCategoryShares.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 网页分类构成");
            foreach (var c in report.WebCategoryShares)
                sb.AppendLine($"- **{c.Category}**：{c.MinutesText}（{c.PercentText}）");
        }

        if (report.CategoryShares.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 分类构成");
            foreach (var c in report.CategoryShares)
                sb.AppendLine($"- **{c.Category}**：{c.MinutesText}（{c.PercentText}）");
        }

        if (report.TopChanges.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 应用变化 Top");
            foreach (var t in report.TopChanges)
                sb.AppendLine($"- **{t.Name}**：{t.DurationText}（{t.DeltaText}）");
        }

        if (report.Hardware is { HasData: true } hw)
        {
            sb.AppendLine();
            sb.AppendLine("## 硬件画像");
            sb.AppendLine(hw.SummaryText);
            foreach (var a in hw.AppLoads.Take(8))
                sb.AppendLine($"- **{a.DisplayName}**：CPU {a.CpuText} · GPU {a.GpuText} · 峰温 {a.TempText} · 重叠 {a.MinutesText}");
        }

        sb.AppendLine();
        sb.AppendLine("## 洞察");
        if (report.Insights.Count == 0)
            sb.AppendLine("- （无）");
        foreach (var i in report.Insights)
            sb.AppendLine($"- [{i.Severity}] **{i.Title}**：{i.Message}");

        sb.AppendLine();
        sb.AppendLine("## 建议方向（规则引擎，非医嘱）");
        sb.AppendLine("1. 对照「构成」压缩非目标分类的大块时间");
        sb.AppendLine("2. 在高效时段安排深度工作，夜间尽量收工");
        sb.AppendLine("3. 若存在高 GPU/高温应用，注意连续负载时长与散热");
        sb.AppendLine();
        sb.AppendLine($"*由 PChabit AnalyticsEngine 本地生成 · {DateTime.Now:yyyy-MM-dd HH:mm}*");
        return sb.ToString();
    }

    private static string FormatGb(long? bytes)
    {
        if (bytes is not { } b || b <= 0) return "-";
        return (b / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
    }
}
