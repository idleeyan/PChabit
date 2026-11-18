using PChabit.Core.Interfaces;
using Serilog;
using System.Text.Json.Serialization;

namespace PChabit.Infrastructure.Formatters;

/// <summary>
/// Excel 导出（P3 第三块）：数据组织在 C#，XLSX 生成交给 <c>pchabit-datad xlsx</c>（纯 Rust）。
/// EPPlus 已从产品路径移除（其 NonCommercial 许可不再被产品依赖），仅测试项目保留它做产物校验。
/// <c>PCHABIT_RUST_XLSX=0</c> 可关闭 Rust 路径（关闭后导出会明确报错，而不是静默产出空文件）。
/// </summary>
public class ExcelExportFormatter : IExportFormatter
{
    public string Format => "excel";
    public string FileExtension => ".xlsx";
    public string MimeType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<string> FormatAsync(ExportData data, ExportOptions? options = null)
    {
        var sheets = BuildSheets(data, options);
        var bytes = await RustXlsxWriter.TryWriteAsync(sheets);
        if (bytes == null)
        {
            Log.Error("Excel 导出失败：pchabit-datad xlsx 不可用（检查产物是否存在，或 PCHABIT_RUST_XLSX 是否被关闭）");
            throw new InvalidOperationException("Excel 导出需要 pchabit-datad xlsx（Rust 数据面），当前不可用。");
        }
        return Convert.ToBase64String(bytes);
    }

    // ==================== 数据组织（唯一一份） ====================

    private static List<XlsxSheet> BuildSheets(ExportData data, ExportOptions? options)
    {
        var sheets = new List<XlsxSheet> { BuildSummary(data) };

        if (data.AppSessions.Any()) sheets.Add(BuildAppSessions(data, options));
        if (data.KeyboardSessions.Any()) sheets.Add(BuildKeyboard(data));
        if (data.MouseSessions.Any()) sheets.Add(BuildMouse(data));
        if (data.WebSessions.Any()) sheets.Add(BuildWeb(data, options));
        if (data.DailyPatterns.Any()) sheets.Add(BuildDaily(data));

        if (data.Statistics?.TopApplications?.Any() == true) sheets.Add(BuildTopApps(data));
        if (data.Statistics?.TopWebsites?.Any() == true) sheets.Add(BuildTopSites(data));

        return sheets;
    }

    private static XlsxSheet BuildSummary(ExportData data)
    {
        var kv = new List<object?[]>
        {
            new object?[] { "导出时间", data.ExportTime.ToString("yyyy-MM-dd HH:mm:ss") },
            new object?[] { "时间范围", $"{data.StartTime:yyyy-MM-dd HH:mm:ss} - {data.EndTime:yyyy-MM-dd HH:mm:ss}" },
            new object?[] { "总时长(分钟)", data.Duration.TotalMinutes }
        };

        if (data.Statistics != null)
        {
            var stats = data.Statistics;
            kv.Add(new object?[] { "统计摘要", null });
            kv.Add(new object?[] { "应用会话总数", stats.TotalAppSessions });
            kv.Add(new object?[] { "键盘会话总数", stats.TotalKeyboardSessions });
            kv.Add(new object?[] { "鼠标会话总数", stats.TotalMouseSessions });
            kv.Add(new object?[] { "网页会话总数", stats.TotalWebSessions });
            kv.Add(new object?[] { "总活跃时间(分钟)", stats.TotalActiveTime.TotalMinutes });
            kv.Add(new object?[] { "总空闲时间(分钟)", stats.TotalIdleTime.TotalMinutes });
            kv.Add(new object?[] { "总按键次数", stats.TotalKeyPresses });
            kv.Add(new object?[] { "总鼠标点击次数", stats.TotalMouseClicks });
            kv.Add(new object?[] { "总网页访问数", stats.TotalWebPages });
            kv.Add(new object?[] { "平均生产力评分", stats.AverageProductivityScore });
            kv.Add(new object?[] { "专注块数量", stats.TotalFocusBlocks });
            kv.Add(new object?[] { "深度工作时间(分钟)", stats.TotalDeepWorkTime.TotalMinutes });
        }

        return new XlsxSheet
        {
            Name = "概览",
            Widths = new double[] { 20, 50 },
            Sections = { new XlsxSection { Title = "PChabit 数据导出报告", Kv = kv } }
        };
    }

    private static XlsxSheet BuildAppSessions(ExportData data, ExportOptions? options)
    {
        var sessions = options?.MaxItems > 0 ? data.AppSessions.Take(options.MaxItems) : data.AppSessions;
        var rows = sessions.Select(s => new object?[]
        {
            s.Id, s.ProcessName, s.WindowTitle ?? "", s.Category ?? "Unknown",
            s.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
            s.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
            s.Duration.TotalMinutes, s.ActiveDuration.TotalMinutes
        }).ToList();

        return Sheet("应用会话", new[] { "ID", "进程名", "窗口标题", "分类", "开始时间", "结束时间", "持续时间(分钟)", "活跃时间(分钟)" }, rows);
    }

    private static XlsxSheet BuildKeyboard(ExportData data)
    {
        var rows = data.KeyboardSessions.Select(s => new object?[]
        {
            s.Id, s.Date.ToString("yyyy-MM-dd"), s.Hour, s.TotalKeyPresses,
            s.Shortcuts != null ? string.Join("; ", s.Shortcuts.Select(x => x.Shortcut)) : ""
        }).ToList();

        return Sheet("键盘会话", new[] { "ID", "日期", "小时", "总按键次数", "快捷键" }, rows);
    }

    private static XlsxSheet BuildMouse(ExportData data)
    {
        var rows = data.MouseSessions.Select(s => new object?[]
        {
            s.Id, s.Date.ToString("yyyy-MM-dd"), s.Hour,
            s.LeftClickCount, s.RightClickCount, s.MiddleClickCount, s.ScrollCount, s.TotalDistance
        }).ToList();

        return Sheet("鼠标会话", new[] { "ID", "日期", "小时", "左键点击", "右键点击", "中键点击", "滚动次数", "移动距离" }, rows);
    }

    private static XlsxSheet BuildWeb(ExportData data, ExportOptions? options)
    {
        var sessions = options?.MaxItems > 0 ? data.WebSessions.Take(options.MaxItems) : data.WebSessions;
        var rows = sessions.Select(s => new object?[]
        {
            s.Id,
            options?.AnonymizeUrls == true ? AnonymizeUrl(s.Url) : s.Url,
            s.Title ?? "", s.Domain ?? "", s.Browser ?? "",
            s.StartTime.ToString("yyyy-MM-dd HH:mm:ss"), s.Duration.TotalMinutes
        }).ToList();

        return Sheet("网页会话", new[] { "ID", "URL", "标题", "域名", "浏览器", "开始时间", "持续时间(分钟)" }, rows);
    }

    private static XlsxSheet BuildDaily(ExportData data)
    {
        var rows = data.DailyPatterns.Select(p => new object?[]
        {
            p.Id, p.Date.ToString("yyyy-MM-dd"),
            p.TotalActiveTime.TotalMinutes, p.TotalIdleTime.TotalMinutes,
            p.ProductivityScore, p.InterruptionCount, p.DeepWorkTime.TotalMinutes
        }).ToList();

        return Sheet("每日模式", new[] { "ID", "日期", "活跃时间(分钟)", "空闲时间(分钟)", "生产力评分", "中断次数", "深度工作时间(分钟)" }, rows);
    }

    private static XlsxSheet BuildTopApps(ExportData data)
    {
        var rows = data.Statistics!.TopApplications
            .OrderByDescending(x => x.Value)
            .Take(20)
            .Select(x => new object?[] { x.Key, x.Value.TotalMinutes })
            .ToList();

        return Sheet("热门应用", new[] { "应用名称", "使用时间(分钟)" }, rows);
    }

    private static XlsxSheet BuildTopSites(ExportData data)
    {
        var rows = data.Statistics!.TopWebsites
            .OrderByDescending(x => x.Value)
            .Take(20)
            .Select(x => new object?[] { x.Key, x.Value.TotalMinutes })
            .ToList();

        return Sheet("热门网站", new[] { "网站域名", "访问时间(分钟)" }, rows);
    }

    internal static XlsxSheet Sheet(string name, string[] headers, List<object?[]> rows) => new()
    {
        Name = name,
        Widths = AutoWidths(headers, rows),
        Sections = { XlsxSection.Table(headers, rows) }
    };

    /// <summary>按内容长度估算列宽（替代 EPPlus 的 AutoFitColumns，两边表现接近）。</summary>
    internal static double[] AutoWidths(string[] headers, List<object?[]> rows)
    {
        var widths = new double[headers.Length];
        for (var i = 0; i < headers.Length; i++)
        {
            var max = headers[i].Length;
            foreach (var row in rows.Take(200))
            {
                if (i < row.Length && row[i] != null)
                    max = Math.Max(max, row[i]!.ToString()!.Length);
            }
            widths[i] = Math.Clamp(max + 2, 10, 50);
        }
        return widths;
    }

    private static string AnonymizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}/***";
        }
        catch (Exception ex)
        {
            // 预期：库内残留非法 URL（历史数据），保守全匿名不泄露
            Log.Debug(ex, "导出脱敏：URL 解析失败，已全匿名");
            return "***";
        }
    }
}

/// <summary>工作簿描述（与 Rust <c>xlsx</c> 命令的协议一一对应）。</summary>
internal sealed class XlsxSheet
{
    [JsonPropertyName("name")] public string Name { get; set; } = "Sheet";
    [JsonPropertyName("widths")] public double[]? Widths { get; set; }
    [JsonPropertyName("sections")] public List<XlsxSection> Sections { get; set; } = new();
}

/// <summary>工作表内的一个区块：可选的标题 / 键值 / 表头 + 数据行。</summary>
internal sealed class XlsxSection
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("kv")] public List<object?[]>? Kv { get; set; }
    [JsonPropertyName("headers")] public List<string>? Headers { get; set; }
    [JsonPropertyName("rows")] public List<object?[]>? Rows { get; set; }

    public static XlsxSection Table(string[] headers, List<object?[]> rows) =>
        new() { Headers = headers.ToList(), Rows = rows };
}
