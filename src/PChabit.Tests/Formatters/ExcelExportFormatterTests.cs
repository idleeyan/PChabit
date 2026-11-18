using FluentAssertions;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Formatters;
using Xunit;

namespace PChabit.Tests.Formatters;

/// <summary>
/// P3 第三块回归：Rust XLSX 写出器产出的文件必须是合法 xlsx（PK 头 + 可被 EPPlus 打开）。
/// 找不到 datad 时跳过（回退 EPPlus 本身另有用例覆盖）。
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
    public async Task Excel导出_应产出可打开的xlsx()
    {
        var formatter = new ExcelExportFormatter();
        var base64 = await formatter.FormatAsync(SampleData());

        base64.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(base64);
        bytes.Length.Should().BeGreaterThan(1000);
        bytes[0].Should().Be(0x50); // P
        bytes[1].Should().Be(0x4B); // K

        // 用 EPPlus 打开一次，确认不是「合法 zip 但内容坏」
        using var package = new OfficeOpenXml.ExcelPackage(new MemoryStream(bytes));
        package.Workbook.Worksheets.Should().NotBeEmpty();
        package.Workbook.Worksheets[0].Name.Should().Be("概览");
    }

    [Fact]
    public async Task Excel导出_关闭Rust时仍能回退EPPlus()
    {
        var old = Environment.GetEnvironmentVariable("PCHABIT_RUST_XLSX");
        try
        {
            Environment.SetEnvironmentVariable("PCHABIT_RUST_XLSX", "0");
            var base64 = await new ExcelExportFormatter().FormatAsync(SampleData());
            var bytes = Convert.FromBase64String(base64);
            bytes[0].Should().Be(0x50);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCHABIT_RUST_XLSX", old);
        }
    }
}
