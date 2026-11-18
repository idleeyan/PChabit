using FluentAssertions;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Formatters;
using Xunit;

namespace PChabit.Tests.Formatters;

/// <summary>
/// P3 第三块回归：产品路径只有 Rust 写出器（EPPlus 已从产品依赖中移除），
/// 所以这里既要保证产物是第三方库能打开的合法 xlsx，也要保证「关掉 Rust」会明确失败而不是静默产出空文件。
/// </summary>
public class ExcelExportFormatterTests
{
    private static ExportData SampleData() => new()
    {
        ExportTime = new DateTime(2026, 11, 18, 20, 30, 0),
        StartTime = new DateTime(2026, 11, 18, 0, 0, 0),
        EndTime = new DateTime(2026, 11, 19, 0, 0, 0),
        AppSessions = new List<AppSession>
        {
            new()
            {
                Id = Guid.NewGuid(), ProcessName = "Doubao.exe", WindowTitle = "豆包 - 聊天",
                Category = "AI 助手",
                StartTime = new DateTime(2026, 11, 18, 19, 0, 0),
                EndTime = new DateTime(2026, 11, 18, 19, 30, 0)
            }
        },
        Statistics = new ExportStatistics
        {
            TotalAppSessions = 1,
            TotalActiveTime = TimeSpan.FromMinutes(30),
            TopApplications = new Dictionary<string, TimeSpan> { ["Doubao.exe"] = TimeSpan.FromMinutes(30) }
        }
    };

    [Fact]
    public async Task Excel导出_应产出可被EPPlus打开的xlsx()
    {
        var base64 = await new ExcelExportFormatter().FormatAsync(SampleData());

        base64.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(base64);
        bytes.Length.Should().BeGreaterThan(1000);
        bytes[0].Should().Be(0x50); // P
        bytes[1].Should().Be(0x4B); // K

        using var package = new OfficeOpenXml.ExcelPackage(new MemoryStream(bytes));
        package.Workbook.Worksheets.Should().NotBeEmpty();
        package.Workbook.Worksheets[0].Name.Should().Be("概览");
        // 概览表标题落在合并区首格
        package.Workbook.Worksheets[0].Cells["A1"].Text.Should().Contain("PChabit");
        package.Workbook.Worksheets.Any(w => w.Name == "应用会话").Should().BeTrue();
    }

    [Fact]
    public async Task Excel导出_关闭Rust时必须明确报错而不是静默空文件()
    {
        var old = Environment.GetEnvironmentVariable("PCHABIT_RUST_XLSX");
        try
        {
            Environment.SetEnvironmentVariable("PCHABIT_RUST_XLSX", "0");
            var act = async () => await new ExcelExportFormatter().FormatAsync(SampleData());
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Rust 数据面*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCHABIT_RUST_XLSX", old);
        }
    }
}
