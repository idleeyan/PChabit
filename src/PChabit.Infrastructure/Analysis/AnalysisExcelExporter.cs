using System.Text.Json;
using System.Text.Json.Serialization;
using PChabit.Infrastructure.Analysis;
using Serilog;

namespace PChabit.Infrastructure.Formatters;

/// <summary>
/// 分析周期报告 → 多工作表 Excel（概览 / KPI / 分类构成 / 应用变化 / 硬件 / 洞察）。
/// P3 第三块的收尾：写出同样交给 <c>pchabit-datad xlsx</c>，EPPlus 从产品路径彻底退出。
/// </summary>
public static class AnalysisExcelExporter
{
    public static byte[] Export(AnalyticsPeriodReport report)
    {
        var sheets = BuildSheets(report);
        var bytes = RustXlsxWriter.TryWriteAsync(sheets).GetAwaiter().GetResult();
        if (bytes == null)
            throw new InvalidOperationException("分析报告导出失败：pchabit-datad xlsx 不可用。");
        return bytes;
    }

    public static async Task ExportToFileAsync(AnalyticsPeriodReport report, string path)
    {
        var bytes = Export(report);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static List<XlsxSheet> BuildSheets(AnalyticsPeriodReport report) => new()
    {
        BuildOverview(report),
        BuildKpis(report),
        BuildCategories(report),
        BuildTopChanges(report),
        BuildHardware(report),
        BuildInsights(report)
    };

    private static XlsxSheet BuildOverview(AnalyticsPeriodReport report)
    {
        var kv = new List<object?[]>
        {
            new object?[] { "周期", report.Period.Label },
            new object?[] { "时间范围", $"{report.Period.Start:yyyy-MM-dd} ~ {report.Period.EndExclusive.AddDays(-1):yyyy-MM-dd}" }
        };
        if (report.Previous != null)
        {
            kv.Add(new object?[] { "对比期", $"{report.Previous.Start:yyyy-MM-dd} ~ {report.Previous.EndExclusive.AddDays(-1):yyyy-MM-dd}" });
        }

        kv.Add(new object?[] { null, null });
        kv.Add(new object?[] { "数据质量", AnalysisReportBuilder.BuildQuality(report).Summary });
        kv.Add(new object?[] { "导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });

        if (report.Extra != null)
        {
            kv.Add(new object?[] { null, null });
            kv.Add(new object?[] { "键盘按键", report.Extra.TotalKeys });
            kv.Add(new object?[] { "网页分钟", Math.Round(report.Extra.WebMinutes, 1) });
            kv.Add(new object?[] { "输入密度/时", Math.Round(report.Extra.KeysPerActiveHour, 1) });
        }

        return new XlsxSheet
        {
            Name = "概览",
            Widths = new double[] { 18, 64 },
            Sections = { new XlsxSection { Title = "PChabit 分析报告", Kv = kv } }
        };
    }

    private static XlsxSheet BuildKpis(AnalyticsPeriodReport report)
    {
        var rows = report.Kpis.Select(k => new object?[] { k.Name, k.Value, k.DeltaText, k.Hint }).ToList();
        return Sheet("KPI", new[] { "名称", "数值", "环比", "说明" }, new double[] { 16, 14, 12, 40 }, rows);
    }

    private static XlsxSheet BuildCategories(AnalyticsPeriodReport report)
    {
        var rows = report.CategoryShares.Select(c => new object?[]
        {
            c.Category, c.MinutesText, Math.Round(c.Minutes, 1), Math.Round(c.Percentage, 1)
        }).ToList();
        return Sheet("分类构成", new[] { "分类", "时长", "分钟", "占比%" }, new double[] { 20, 14, 12, 12 }, rows);
    }

    private static XlsxSheet BuildTopChanges(AnalyticsPeriodReport report)
    {
        var rows = report.TopChanges.Select(t => new object?[]
        {
            t.Name, Math.Round(t.CurrentMinutes, 1), Math.Round(t.PreviousMinutes, 1), t.DeltaText
        }).ToList();
        return Sheet("应用变化", new[] { "应用", "本期分钟", "上期分钟", "变化" }, new double[] { 28, 14, 14, 12 }, rows);
    }

    private static XlsxSheet BuildHardware(AnalyticsPeriodReport report)
    {
        // 硬件表结构特殊：汇总行 -> 应用段（表头在第 3 行）-> 空两行 -> 小时段（另一组表头）
        if (report.Hardware is not { HasData: true } hw)
        {
            return Sheet("硬件", new[] { "说明" }, new double[] { 40 },
                new List<object?[]> { new object?[] { "本周期无硬件分钟样本" } });
        }

        var appRows = hw.AppLoads.Select(a => new object?[]
        {
            a.DisplayName, a.CpuLoadAvg, a.GpuLoadAvg, a.GpuTempMax, a.OverlapMinutes, a.SampleCount
        }).ToList();

        var hourRows = hw.Hours.Where(x => x.SampleCount > 0).Select(h => new object?[]
        {
            h.Label, h.CpuLoadAvg, h.GpuLoadAvg, h.SampleCount
        }).ToList();

        return new XlsxSheet
        {
            Name = "硬件",
            Widths = new double[] { 24, 14, 14, 14, 14, 14 },
            Sections =
            {
                new XlsxSection
                {
                    Kv = new List<object?[]> { new object?[] { "汇总", hw.SummaryText } },
                    Headers = new List<string> { "应用", "CPU均%", "GPU均%", "峰温°C", "重叠分钟", "样本数" },
                    Rows = appRows
                },
                new XlsxSection
                {
                    Headers = new List<string> { "小时", "CPU%", "GPU%", "样本" },
                    Rows = hourRows
                }
            }
        };
    }

    private static XlsxSheet BuildInsights(AnalyticsPeriodReport report)
    {
        var rows = report.Insights.Select(i => new object?[] { i.Severity, i.Title, i.Message, i.ActionKey }).ToList();
        return Sheet("洞察", new[] { "级别", "标题", "说明", "ActionKey" }, new double[] { 10, 20, 72, 14 }, rows);
    }

    private static XlsxSheet Sheet(string name, string[] headers, double[] widths, List<object?[]> rows) => new()
    {
        Name = name,
        Widths = widths,
        Sections = { XlsxSection.Table(headers, rows) }
    };
}

/// <summary>AI 深度解读结构化解析 v2：契约 v1/v2 兼容 + 截断修复 + evidence。</summary>
public static class AnalyticsAiResponseParser
{
    public sealed record AiFinding(string Title, string Detail, IReadOnlyList<AiEvidence>? Evidence = null);
    public sealed record AiSuggestion(string Title, string Action, string? ActionKey, string? TargetMetricId = null, double? TargetValue = null);
    public sealed record AiEvidence(string MetricId, double? Value = null, double? Prev = null, string? Date = null);
    public sealed record AiDiagnosis(string Hypothesis, string Confidence, IReadOnlyList<AiEvidence> Evidence);
    public sealed record AiPlanItem(string Title, string Detail, string? ActionKey, string? TargetMetricId, double? TargetValue, string Effort);

    public sealed record ParsedAiResult(
        string Summary,
        IReadOnlyList<AiFinding> Findings,
        IReadOnlyList<AiSuggestion> Suggestions,
        IReadOnlyList<string> Risks,
        string Raw,
        bool Parsed,
        IReadOnlyList<AiDiagnosis>? Diagnosis = null,
        IReadOnlyList<AiPlanItem>? Plan = null,
        IReadOnlyList<string>? FollowUps = null);

    public static ParsedAiResult Parse(string? raw)
    {
        raw = raw?.Trim() ?? "";
        if (raw.Length == 0)
            return new ParsedAiResult("", Array.Empty<AiFinding>(), Array.Empty<AiSuggestion>(), Array.Empty<string>(), "", false);

        var json = ExtractJsonObject(raw);
        if (json is null)
            return new ParsedAiResult(raw, Array.Empty<AiFinding>(), Array.Empty<AiSuggestion>(), Array.Empty<string>(), raw, false);

        var parsed = TryParseJson(json) ?? TryParseJson(RepairJson(json));
        if (parsed is null)
            return new ParsedAiResult(raw, Array.Empty<AiFinding>(), Array.Empty<AiSuggestion>(), Array.Empty<string>(), raw, false);

        var (summary, findings, suggestions, risks, diagnosis, plan, followUps) = parsed.Value;
        if (summary.Length == 0 && findings.Count == 0 && suggestions.Count == 0)
            return new ParsedAiResult(raw, findings, suggestions, risks, raw, false, diagnosis, plan, followUps);

        return new ParsedAiResult(summary, findings, suggestions, risks, raw, true, diagnosis, plan, followUps);
    }

    private static (string, List<AiFinding>, List<AiSuggestion>, List<string>, List<AiDiagnosis>, List<AiPlanItem>, List<string>)? TryParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";

            var findings = new List<AiFinding>();
            if (root.TryGetProperty("findings", out var fs) && fs.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fs.EnumerateArray())
                {
                    var title = f.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var detail = f.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
                    var ev = ParseEvidence(f);
                    if (title.Length > 0 || detail.Length > 0)
                        findings.Add(new AiFinding(title, detail, ev));
                }
            }

            var suggestions = new List<AiSuggestion>();
            // 契约 v2 用 plan，v1 用 suggestions
            var planItems = new List<AiPlanItem>();
            if (root.TryGetProperty("plan", out var ps) && ps.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in ps.EnumerateArray())
                {
                    var title = p.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var detail = p.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
                    var key = p.TryGetProperty("actionKey", out var k) ? k.GetString() : null;
                    var mid = p.TryGetProperty("targetMetricId", out var m) ? m.GetString() : null;
                    double? tv = p.TryGetProperty("targetValue", out var tvel) && tvel.ValueKind == JsonValueKind.Number
                        ? tvel.GetDouble() : null;
                    var effort = p.TryGetProperty("effort", out var e) ? e.GetString() ?? "med" : "med";
                    if (title.Length > 0 || detail.Length > 0)
                    {
                        planItems.Add(new AiPlanItem(title, detail, key, mid, tv, effort));
                        suggestions.Add(new AiSuggestion(title, detail, key, mid, tv));
                    }
                }
            }
            if (suggestions.Count == 0 && root.TryGetProperty("suggestions", out var ss) && ss.ValueKind == JsonValueKind.Array)
            {
                foreach (var sg in ss.EnumerateArray())
                {
                    var title = sg.TryGetProperty("title", out var t2) ? t2.GetString() ?? "" : "";
                    var action = sg.TryGetProperty("action", out var a2) ? a2.GetString() ?? "" : "";
                    var key = sg.TryGetProperty("actionKey", out var k2) ? k2.GetString() : null;
                    var mid = sg.TryGetProperty("targetMetricId", out var m2) ? m2.GetString() : null;
                    double? tv = sg.TryGetProperty("targetValue", out var tv2) && tv2.ValueKind == JsonValueKind.Number
                        ? tv2.GetDouble() : null;
                    if (title.Length > 0 || action.Length > 0)
                    {
                        suggestions.Add(new AiSuggestion(title, action, key, mid, tv));
                        planItems.Add(new AiPlanItem(title, action, key, mid, tv, "med"));
                    }
                }
            }

            var risks = new List<string>();
            if (root.TryGetProperty("risks", out var rs) && rs.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rs.EnumerateArray())
                {
                    var text = r.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) risks.Add(text!);
                }
            }

            var diagnosis = new List<AiDiagnosis>();
            if (root.TryGetProperty("diagnosis", out var ds) && ds.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in ds.EnumerateArray())
                {
                    var hyp = d.TryGetProperty("hypothesis", out var h) ? h.GetString() ?? "" : "";
                    var conf = d.TryGetProperty("confidence", out var c) ? c.GetString() ?? "medium" : "medium";
                    if (hyp.Length > 0)
                        diagnosis.Add(new AiDiagnosis(hyp, conf, ParseEvidence(d) ?? new List<AiEvidence>()));
                }
            }

            var followUps = new List<string>();
            if (root.TryGetProperty("followUps", out var fu) && fu.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fu.EnumerateArray())
                {
                    var text = f.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) followUps.Add(text!);
                }
            }

            return (summary, findings, suggestions, risks, diagnosis, planItems, followUps);
        }
        catch (Exception ex)
        {
            // 预期：AI 快照 JSON 为旧版格式或损坏，Excel 导出跳过该区块
            Log.Debug(ex, "AI 快照解析失败，Excel 导出跳过解读区块");
            return null;
        }
    }

    private static List<AiEvidence>? ParseEvidence(JsonElement parent)
    {
        if (!parent.TryGetProperty("evidence", out var ev) || ev.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<AiEvidence>();
        foreach (var e in ev.EnumerateArray())
        {
            var mid = e.TryGetProperty("metricId", out var m) ? m.GetString() ?? "" : "";
            if (mid.Length == 0) continue;
            double? val = e.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            double? prev = e.TryGetProperty("prev", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
            var date = e.TryGetProperty("date", out var dt) ? dt.GetString() : null;
            list.Add(new AiEvidence(mid, val, prev, date));
        }
        return list.Count > 0 ? list : null;
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text.Substring(start, end - start + 1);
    }

    /// <summary>截断 JSON 修复：按括号配平补齐。</summary>
    internal static string RepairJson(string json)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escape = false;
        foreach (var ch in json)
        {
            if (escape) { escape = false; continue; }
            if (ch == '\\') { escape = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (ch is '{' or '[') stack.Push(ch);
            else if (ch is '}' or ']')
            {
                if (stack.Count > 0) stack.Pop();
            }
        }
        var sb = new System.Text.StringBuilder(json.TrimEnd());
        // 去掉可能的尾部残缺 token
        while (sb.Length > 0 && (sb[^1] == ',' || sb[^1] == ' '))
            sb.Length--;
        while (stack.Count > 0)
        {
            var open = stack.Pop();
            sb.Append(open == '{' ? '}' : ']');
        }
        return sb.ToString();
    }
}

