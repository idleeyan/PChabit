using FluentAssertions;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Formatters;
using Xunit;

namespace PChabit.Tests.Analysis;

/// <summary>分析页 Excel 导出（Rust rust_xlsxwriter 生成，EPPlus 仅作第三方校验）。</summary>
public class AnalysisExcelExportTests
{
    [Fact]
    public void ExcelExport_ProducesNonEmptyBytes()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var prev = period.Previous();
        var start = new DateTime(2026, 9, 16, 10, 0, 0);
        var sessions = new List<PChabit.Core.Entities.AppSession>
        {
            new()
            {
                ProcessName = "devenv",
                Category = "开发",
                StartTime = start,
                EndTime = start.AddMinutes(90)
            }
        };
        var report = AnalyticsEngine.Compute(period, prev, sessions, sessions, null, null, null);
        var bytes = AnalysisExcelExporter.Export(report);
        bytes.Length.Should().BeGreaterThan(100);
        // ZIP/PK header
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');

        // 六张表齐全（EPPlus 在这里只当第三方校验器）
        using var package = new OfficeOpenXml.ExcelPackage(new MemoryStream(bytes));
        var names = package.Workbook.Worksheets.Select(w => w.Name).ToList();
        names.Should().BeEquivalentTo(new[] { "概览", "KPI", "分类构成", "应用变化", "硬件", "洞察" });
        // 概览的键值块：A 列标签、B 列取值
        package.Workbook.Worksheets["概览"].Cells["A3"].Text.Should().Be("周期");
        // KPI 表头在第 1 行
        package.Workbook.Worksheets["KPI"].Cells["A1"].Text.Should().Be("名称");
    }
}
