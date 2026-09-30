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

    /// <summary>发给大模型的聚合上下文（不含窗口标题/URL/完整会话）。</summary>
    public static string BuildAiPayload(AnalyticsPeriodReport report)
    {
        var quality = BuildQuality(report);
        var payload = new AiPayload
        {
            Period = $"{report.Period.Start:yyyy-MM-dd}~{report.Period.EndExclusive.AddDays(-1):yyyy-MM-dd}",
            Label = report.Period.Label,
            Quality = quality.Summary,
            HasEnoughData = report.HasEnoughData,
            Kpis = report.Kpis.Select(k => new AiKpi(k.Name, k.Value, k.DeltaText)).ToList(),
            Extra = report.Extra == null ? null : new AiExtra
            {
                Keys = report.Extra.TotalKeys,
                Clicks = report.Extra.TotalClicks,
                KeysPerActiveHour = Math.Round(report.Extra.KeysPerActiveHour, 1),
                WebMinutes = Math.Round(report.Extra.WebMinutes, 1),
                WebSharePct = Math.Round(report.Extra.WebSharePct, 1)
            },
            Categories = report.CategoryShares
                .Select(c => new AiShare(c.Category, Math.Round(c.Minutes, 1), Math.Round(c.Percentage, 1)))
                .ToList(),
            TopApps = report.TopChanges
                .Select(t => new AiShare(t.Name, Math.Round(t.CurrentMinutes, 1), 0))
                .ToList(),
            Hardware = report.Hardware is { HasData: true } hw ? new AiHardware
            {
                CpuAvg = hw.CpuLoadAvg,
                CpuP95 = hw.CpuLoadP95,
                GpuAvg = hw.GpuLoadAvg,
                GpuTempMax = hw.GpuTempMax,
                SampleDays = hw.SampleDays,
                AppLoads = hw.AppLoads.Take(8)
                    .Select(a => new AiShare(a.DisplayName, Math.Round(a.OverlapMinutes, 1), a.GpuLoadAvg ?? 0))
                    .ToList()
            } : null,
            RuleInsights = report.Insights
                .Select(i => new AiInsightItem(i.Severity, i.Title, i.Message))
                .ToList()
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    public const string SystemPrompt =
        "【语言硬性要求】只用简体中文输出，禁止英文正文。只输出指定 JSON，不要 markdown 围栏。" +
        "你是电脑使用习惯分析顾问。只根据用户提供的聚合 JSON 分析，禁止编造未出现的数据、URL 或窗口标题。" +
        "禁止只复述构成/占比；必须给出归因与可执行计划。数据不足时在 summary 中明确说明缺失来源。" +
        "输出 JSON（不要多余文字）：{" +
        "\"summary\":\"120字内结论\"," +
        "\"findings\":[{\"title\":\"\",\"detail\":\"\",\"evidence\":[{\"metricId\":\"\",\"value\":0,\"prev\":0}]}]," +
        "\"diagnosis\":[{\"hypothesis\":\"\",\"confidence\":\"high|medium|low\",\"evidence\":[{\"metricId\":\"\",\"value\":0}]}]," +
        "\"plan\":[{\"title\":\"\",\"detail\":\"\",\"actionKey\":null,\"targetMetricId\":null,\"targetValue\":null,\"effort\":\"low|med|high\"}]," +
        "\"risks\":[\"\"],\"followUps\":[\"追问建议\"]}。" +
        "evidence.metricId 必须来自 payload 的 metrics/daily 键；actionKey 仅可使用：compose、rhythm、hardware、HistoryReport，或 null。" +
        "plan 尽量带 targetMetricId + targetValue。语气克制、具体可执行。" +
        "confidence/effort 用英文枚举，其余字符串字段必须是简体中文。";

    /// <summary>追问用：允许自然语言，不要求 JSON。</summary>
    public const string FollowUpSystemPrompt =
        "【语言硬性要求】只用简体中文回答，禁止大段英文。" +
        "你是电脑使用习惯分析顾问。基于给定指标摘要与上一次解读回答用户追问。" +
        "只依据给出的数据，禁止编造。回答具体、可执行，不要鸡汤。";

    /// <summary>用户消息前缀：部分模型只看 user，再次锁定语言。</summary>
    public const string UserLanguagePreamble =
        "请严格用简体中文输出。若输出 JSON，字符串字段一律中文，禁止英文正文。\n\n";

    private sealed class AiPayload
    {
        public string Period { get; set; } = "";
        public string Label { get; set; } = "";
        public string Quality { get; set; } = "";
        public bool HasEnoughData { get; set; }
        public List<AiKpi> Kpis { get; set; } = new();
        public AiExtra? Extra { get; set; }
        public List<AiShare> Categories { get; set; } = new();
        public List<AiShare> TopApps { get; set; } = new();
        public AiHardware? Hardware { get; set; }
        public List<AiInsightItem> RuleInsights { get; set; } = new();
    }

    private sealed record AiKpi(string Name, string Value, string Delta);

    private sealed class AiExtra
    {
        public double Keys { get; set; }
        public double Clicks { get; set; }
        public double KeysPerActiveHour { get; set; }
        public double WebMinutes { get; set; }
        public double WebSharePct { get; set; }
    }

    private sealed record AiShare(string Name, double Minutes, double Metric);

    private sealed class AiHardware
    {
        public double? CpuAvg { get; set; }
        public double? CpuP95 { get; set; }
        public double? GpuAvg { get; set; }
        public double? GpuTempMax { get; set; }
        public int SampleDays { get; set; }
        public List<AiShare> AppLoads { get; set; } = new();
    }

    private sealed record AiInsightItem(string Severity, string Title, string Message);
}
