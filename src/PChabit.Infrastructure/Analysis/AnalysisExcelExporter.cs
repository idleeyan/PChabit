using System.Text.Json;
using System.Text.Json.Serialization;
using OfficeOpenXml;
using PChabit.Infrastructure.Analysis;

namespace PChabit.Infrastructure.Formatters;

/// <summary>分析周期报告 → 多工作表 Excel（KPI / 构成 / 应用变化 / 硬件 / 洞察）。</summary>
public static class AnalysisExcelExporter
{
    static AnalysisExcelExporter()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public static byte[] Export(AnalyticsPeriodReport report)
    {
        using var package = new ExcelPackage();
        AddOverview(package, report);
        AddKpis(package, report);
        AddCategories(package, report);
        AddTopChanges(package, report);
        AddHardware(package, report);
        AddInsights(package, report);
        return package.GetAsByteArray();
    }

    public static async Task ExportToFileAsync(AnalyticsPeriodReport report, string path)
    {
        var bytes = Export(report);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static void AddOverview(ExcelPackage package, AnalyticsPeriodReport report)
    {
        var sheet = package.Workbook.Worksheets.Add("概览");
        sheet.Cells["A1"].Value = "PChabit 分析报告";
        sheet.Cells["A1"].Style.Font.Size = 16;
        sheet.Cells["A1"].Style.Font.Bold = true;
        sheet.Cells["A3"].Value = "周期";
        sheet.Cells["B3"].Value = report.Period.Label;
        sheet.Cells["A4"].Value = "时间范围";
        sheet.Cells["B4"].Value =
            $"{report.Period.Start:yyyy-MM-dd} ~ {report.Period.EndExclusive.AddDays(-1):yyyy-MM-dd}";
        if (report.Previous != null)
        {
            sheet.Cells["A5"].Value = "对比期";
            sheet.Cells["B5"].Value =
                $"{report.Previous.Start:yyyy-MM-dd} ~ {report.Previous.EndExclusive.AddDays(-1):yyyy-MM-dd}";
        }
        var quality = AnalysisReportBuilder.BuildQuality(report);
        sheet.Cells["A7"].Value = "数据质量";
        sheet.Cells["B7"].Value = quality.Summary;
        sheet.Cells["A8"].Value = "导出时间";
        sheet.Cells["B8"].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (report.Extra != null)
        {
            sheet.Cells["A10"].Value = "键盘按键";
            sheet.Cells["B10"].Value = report.Extra.TotalKeys;
            sheet.Cells["A11"].Value = "网页分钟";
            sheet.Cells["B11"].Value = Math.Round(report.Extra.WebMinutes, 1);
            sheet.Cells["A12"].Value = "输入密度/时";
            sheet.Cells["B12"].Value = Math.Round(report.Extra.KeysPerActiveHour, 1);
        }
        sheet.Column(1).Width = 18;
        sheet.Column(2).Width = 64;
    }

    private static void AddKpis(ExcelPackage package, AnalyticsPeriodReport report)
    {
        var sheet = package.Workbook.Worksheets.Add("KPI");
        sheet.Cells["A1"].Value = "名称";
        sheet.Cells["B1"].Value = "数值";
        sheet.Cells["C1"].Value = "环比";
        sheet.Cells["D1"].Value = "说明";
        StyleHeader(sheet, 1);
        var row = 2;
        foreach (var k in report.Kpis)
        {
            sheet.Cells[row, 1].Value = k.Name;
            sheet.Cells[row, 2].Value = k.Value;
            sheet.Cells[row, 3].Value = k.DeltaText;
            sheet.Cells[row, 4].Value = k.Hint;
            row++;
        }
        sheet.Column(1).Width = 16;
        sheet.Column(2).Width = 14;
        sheet.Column(3).Width = 12;
        sheet.Column(4).Width = 40;
    }

    private static void AddCategories(ExcelPackage package, AnalyticsPeriodReport report)
    {
        var sheet = package.Workbook.Worksheets.Add("分类构成");
        sheet.Cells["A1"].Value = "分类";
        sheet.Cells["B1"].Value = "时长";
        sheet.Cells["C1"].Value = "分钟";
        sheet.Cells["D1"].Value = "占比%";
        StyleHeader(sheet, 1);
        var row = 2;
        foreach (var c in report.CategoryShares)
        {
            sheet.Cells[row, 1].Value = c.Category;
            sheet.Cells[row, 2].Value = c.MinutesText;
            sheet.Cells[row, 3].Value = Math.Round(c.Minutes, 1);
            sheet.Cells[row, 4].Value = Math.Round(c.Percentage, 1);
            row++;
        }
        sheet.Column(1).Width = 20;
        sheet.Column(2).Width = 14;
        sheet.Column(3).Width = 12;
        sheet.Column(4).Width = 12;
    }

    private static void AddTopChanges(ExcelPackage package, AnalyticsPeriodReport report)
    {
        var sheet = package.Workbook.Worksheets.Add("应用变化");
        sheet.Cells["A1"].Value = "应用";
        sheet.Cells["B1"].Value = "本期分钟";
        sheet.Cells["C1"].Value = "上期分钟";
        sheet.Cells["D1"].Value = "变化";
        StyleHeader(sheet, 1);
        var row = 2;
        foreach (var t in report.TopChanges)
        {
            sheet.Cells[row, 1].Value = t.Name;
            sheet.Cells[row, 2].Value = Math.Round(t.CurrentMinutes, 1);
            sheet.Cells[row, 3].Value = Math.Round(t.PreviousMinutes, 1);
            sheet.Cells[row, 4].Value = t.DeltaText;
            row++;
        }
        sheet.Column(1).Width = 28;
        sheet.Column(2).Width = 14;
        sheet.Column(3).Width = 14;
        sheet.Column(4).Width = 12;
    }

    private static void AddHardware(ExcelPackage package, AnalyticsPeriodReport report)
    {
        var sheet = package.Workbook.Worksheets.Add("硬件");
        if (report.Hardware is not { HasData: true } hw)
        {
            sheet.Cells["A1"].Value = "本周期无硬件分钟样本";
            return;
        }
        sheet.Cells["A1"].Value = "汇总";
        sheet.Cells["B1"].Value = hw.SummaryText;
        sheet.Cells["A3"].Value = "应用";
        sheet.Cells["B3"].Value = "CPU均%";
        sheet.Cells["C3"].Value = "GPU均%";
        sheet.Cells["D3"].Value = "峰温°C";
        sheet.Cells["E3"].Value = "重叠分钟";
        sheet.Cells["F3"].Value = "样本数";
        StyleHeader(sheet, 3);
        var row = 4;
        foreach (var a in hw.AppLoads)
        {
            sheet.Cells[row, 1].Value = a.DisplayName;
            sheet.Cells[row, 2].Value = a.CpuLoadAvg;
            sheet.Cells[row, 3].Value = a.GpuLoadAvg;
            sheet.Cells[row, 4].Value = a.GpuTempMax;
            sheet.Cells[row, 5].Value = a.OverlapMinutes;
            sheet.Cells[row, 6].Value = a.SampleCount;
            row++;
        }
        row += 2;
        sheet.Cells[row, 1].Value = "小时";
        sheet.Cells[row, 2].Value = "CPU%";
        sheet.Cells[row, 3].Value = "GPU%";
        sheet.Cells[row, 4].Value = "样本";
        StyleHeader(sheet, row);
        row++;
        foreach (var h in hw.Hours.Where(x => x.SampleCount > 0))
        {
            sheet.Cells[row, 1].Value = h.Label;
            sheet.Cells[row, 2].Value = h.CpuLoadAvg;
            sheet.Cells[row, 3].Value = h.GpuLoadAvg;
            sheet.Cells[row, 4].Value = h.SampleCount;
            row++;
        }
        for (var c = 1; c <= 6; c++) sheet.Column(c).Width = 14;
        sheet.Column(1).Width = 24;
    }

    private static void AddInsights(ExcelPackage package, AnalyticsPeriodReport report)
    {
        var sheet = package.Workbook.Worksheets.Add("洞察");
        sheet.Cells["A1"].Value = "级别";
        sheet.Cells["B1"].Value = "标题";
        sheet.Cells["C1"].Value = "说明";
        sheet.Cells["D1"].Value = "ActionKey";
        StyleHeader(sheet, 1);
        var row = 2;
        foreach (var i in report.Insights)
        {
            sheet.Cells[row, 1].Value = i.Severity;
            sheet.Cells[row, 2].Value = i.Title;
            sheet.Cells[row, 3].Value = i.Message;
            sheet.Cells[row, 4].Value = i.ActionKey;
            row++;
        }
        sheet.Column(1).Width = 10;
        sheet.Column(2).Width = 20;
        sheet.Column(3).Width = 72;
        sheet.Column(4).Width = 14;
    }

    private static void StyleHeader(ExcelWorksheet sheet, int row)
    {
        using var range = sheet.Cells[row, 1, row, 4];
        range.Style.Font.Bold = true;
    }
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
        catch
        {
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

