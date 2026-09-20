using FluentAssertions;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Formatters;
using Xunit;

namespace PChabit.Tests.Analysis;

public class AnalysisAiParserAndExcelTests
{
    [Fact]
    public void Parse_StructuredJson_ReturnsCards()
    {
        var raw = """
            {"summary":"本周专注尚可","findings":[{"title":"网页偏多","detail":"占比约四成"}],
             "suggestions":[{"title":"压缩浏览","action":"设定网页时段","actionKey":"compose"}],
             "risks":["夜间使用偏多"]}
            """;
        var parsed = AnalyticsAiResponseParser.Parse(raw);
        parsed.Parsed.Should().BeTrue();
        parsed.Summary.Should().Contain("专注");
        parsed.Findings.Should().HaveCount(1);
        parsed.Suggestions.Should().ContainSingle(s => s.ActionKey == "compose");
        parsed.Risks.Should().Contain("夜间使用偏多");
    }

    [Fact]
    public void Parse_PlainText_FallsBackToRaw()
    {
        var parsed = AnalyticsAiResponseParser.Parse("今天状态不错，继续保持。");
        parsed.Parsed.Should().BeFalse();
        parsed.Summary.Should().Contain("状态");
    }

    [Fact]
    public void Parse_MarkdownFencedJson_StillWorks()
    {
        var raw = "```json\n{\"summary\":\"ok\",\"findings\":[],\"suggestions\":[],\"risks\":[]}\n```";
        var parsed = AnalyticsAiResponseParser.Parse(raw);
        parsed.Parsed.Should().BeTrue();
        parsed.Summary.Should().Be("ok");
    }

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
    }
}
