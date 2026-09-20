using FluentAssertions;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

public class HardwareAnalyticsEngineTests
{
    private static AppSession Session(string process, DateTime start, DateTime end, string? app = null) => new()
    {
        ProcessName = process,
        AppName = app ?? process,
        Category = "开发",
        StartTime = start,
        EndTime = end
    };

    private static HardwareSample Sample(string ts, double cpu, double gpu, double? temp = null) => new()
    {
        Timestamp = ts,
        CpuLoadAvg = cpu,
        GpuLoadAvg = gpu,
        GpuTempMax = temp,
        SampleCount = 60
    };

    [Fact]
    public void ParseSamples_ValidTimestamp_ReturnsRows()
    {
        var parsed = HardwareAnalyticsEngine.ParseSamples(new List<HardwareSample>
        {
            Sample("2026-09-18 14:30", 40, 60, 75),
            new() { Timestamp = "bad", CpuLoadAvg = 1 }
        });
        parsed.Should().HaveCount(1);
        parsed[0].Start.Hour.Should().Be(14);
        parsed[0].GpuLoadAvg.Should().Be(60);
    }

    [Fact]
    public void AlignAppLoads_AttributesToOverlappingProcess()
    {
        var t = new DateTime(2026, 9, 18, 14, 0, 0);
        var sessions = new List<AppSession>
        {
            Session("dev", t, t.AddMinutes(5), "VS"),
            Session("chrome", t.AddMinutes(10), t.AddMinutes(20), "Chrome")
        };
        var parsed = new List<HardwareAnalyticsEngine.ParsedSample>
        {
            new(t, 50, 80, 88, null),           // 仅 dev
            new(t.AddMinutes(10), 20, 10, 60, null) // 仅 chrome
        };

        var loads = HardwareAnalyticsEngine.AlignAppLoads(sessions, parsed);
        var dev = loads.FirstOrDefault(x => x.ProcessName == "dev");
        var chrome = loads.FirstOrDefault(x => x.ProcessName == "chrome");
        dev.Should().NotBeNull();
        chrome.Should().NotBeNull();
        dev!.GpuLoadAvg.Should().BeApproximately(80, 0.5);
        dev.DisplayName.Should().Be("VS");
        chrome!.GpuLoadAvg.Should().BeApproximately(10, 0.5);
    }

    [Fact]
    public void Build_WithSamples_ProducesHoursAndSummary()
    {
        var t = new DateTime(2026, 9, 18, 9, 0, 0);
        var sessions = new List<AppSession> { Session("app", t, t.AddMinutes(120), "应用") };
        var samples = new List<HardwareSample>
        {
            Sample("2026-09-18 09:00", 30, 20, 60),
            Sample("2026-09-18 09:01", 40, 30, 70),
            Sample("2026-09-18 10:00", 90, 70, 90)
        };

        var report = HardwareAnalyticsEngine.Build(sessions, samples);
        report.HasData.Should().BeTrue();
        report.SampleMinutes.Should().Be(3);
        report.SampleDays.Should().Be(1);
        report.Hours.Should().HaveCount(24);
        report.Hours[9].SampleCount.Should().Be(2);
        report.AppLoads.Should().Contain(a => a.ProcessName == "app");
        report.GpuTempMax.Should().Be(90);
        report.SummaryText.Should().Contain("CPU");
    }

    [Fact]
    public void Build_NoSamples_HasDataFalse()
    {
        var report = HardwareAnalyticsEngine.Build(new List<AppSession>(), new List<HardwareSample>());
        report.HasData.Should().BeFalse();
        report.AppLoads.Should().BeEmpty();
    }

    [Fact]
    public void AnalyticsCompute_WithHardwareSamples_IncludesHardwareReport()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var prev = period.Previous();
        var start = new DateTime(2026, 9, 16, 10, 0, 0);
        var sessions = new List<AppSession>
        {
            Session("lmstudio", start, start.AddMinutes(120), "LM Studio")
        };
        var samples = new List<HardwareSample>
        {
            Sample("2026-09-16 10:00", 40, 85, 80),
            Sample("2026-09-16 10:01", 42, 88, 82)
        };

        var report = AnalyticsEngine.Compute(period, prev, sessions, sessions, null, null, samples);
        report.Hardware.Should().NotBeNull();
        report.Hardware!.HasData.Should().BeTrue();
        report.Insights.Should().Contain(i => i.Id == "gpu-app");
    }
}
