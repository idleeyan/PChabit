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
