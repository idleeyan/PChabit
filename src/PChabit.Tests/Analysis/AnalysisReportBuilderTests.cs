using FluentAssertions;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

public class AnalysisReportBuilderTests
{
    private static AnalyticsPeriodReport SampleReport()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var prev = period.Previous();
        var start = new DateTime(2026, 9, 16, 10, 0, 0);
        var sessions = new List<AppSession>
        {
            new()
            {
                ProcessName = "devenv",
                AppName = "VS",
                Category = "开发",
                StartTime = start,
                EndTime = start.AddMinutes(180)
            }
        };
        var daily = new List<DailySummary>
        {
            new()
            {
                Date = "2026-09-16",
                TotalKeys = 5000,
                ActiveMinutes = 180,
                WebActiveDurationTicks = TimeSpan.FromMinutes(30).Ticks,
                CpuLoadAvg = 40,
                MetricsVersion = 2
            }
        };
        var samples = new List<HardwareSample>
        {
            new()
            {
                Timestamp = "2026-09-16 10:00",
                CpuLoadAvg = 50,
                GpuLoadAvg = 20,
                GpuTempMax = 70
            }
        };
        return AnalyticsEngine.Compute(period, prev, sessions, sessions, daily, daily, samples);
    }

    [Fact]
    public void BuildMarkdown_ContainsSections()
    {
        var md = AnalysisReportBuilder.BuildMarkdown(SampleReport());
        md.Should().Contain("深度周报");
        md.Should().Contain("## KPI");
        md.Should().Contain("## 数据质量");
        md.Should().Contain("## 洞察");
        md.Should().Contain("分类构成");
    }

    [Fact]
    public void BuildQuality_ReflectsSources()
    {
        var q = AnalysisReportBuilder.BuildQuality(SampleReport());
        q.HasInput.Should().BeTrue();
        q.HasWeb.Should().BeTrue();
        q.HasHardware.Should().BeTrue();
        q.HardwareSampleMinutes.Should().Be(1);
        q.Summary.Should().Contain("硬件");
    }

    [Fact]
    public void BuildAiPayload_IsValidJson_WithoutSensitiveFields()
    {
        var payload = AnalysisReportBuilder.BuildAiPayload(SampleReport());
        payload.Should().Contain("Kpis");
        payload.Should().Contain("Hardware");
        payload.Should().NotContain("WindowTitle");
        payload.Should().NotContain("http://");
        System.Text.Json.JsonDocument.Parse(payload); // 不抛即合法
    }

    [Fact]
    public void SystemPrompt_MentionsJsonAndChinese()
    {
        AnalysisReportBuilder.SystemPrompt.Should().Contain("简体中文");
        AnalysisReportBuilder.SystemPrompt.Should().Contain("JSON");
    }
}
