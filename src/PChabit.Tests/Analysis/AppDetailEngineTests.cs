using FluentAssertions;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

public class AppDetailEngineTests
{
    private static AppSession S(string process, DateTime start, int minutes, string? title = null, string? cat = "开发")
        => new()
        {
            ProcessName = process,
            AppName = process,
            Category = cat,
            WindowTitle = title ?? "",
            StartTime = start,
            EndTime = start.AddMinutes(minutes)
        };

    [Fact]
    public void Compute_BasicKpis()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var prev = period.Previous();
        var t = new DateTime(2026, 9, 16, 10, 0, 0);
        var cur = new List<AppSession>
        {
            S("devenv", t, 40, "Program.cs - VS"),
            S("devenv", t.AddHours(2), 30, "Program.cs - VS"),
            S("devenv", t.AddDays(1), 60, "docs")
        };
        var prevList = new List<AppSession> { S("devenv", t.AddDays(-7), 100) };

        var report = AppDetailEngine.Compute(
            period, prev, cur, prevList, "devenv", "VS", "开发", "开发",
            keyPresses: 5000, hardwareSamples: null);

        report.HasData.Should().BeTrue();
        report.TotalMinutes.Should().BeApproximately(130, 0.1);
        report.Sessions.Should().Be(3);
        report.FocusCount.Should().Be(3); // 40/30/60 均 ≥25 且开发类
        report.TopWindowTitles.Should().Contain("Program.cs - VS");
        report.Hourly.Should().HaveCount(24);
        report.DailyTrend.Should().NotBeEmpty();
        report.KeysPerHour.Should().NotBeNull();
        report.HasInput.Should().BeTrue();
        report.HasHardware.Should().BeFalse();
    }

    [Fact]
    public void Compute_Empty_NoData()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var report = AppDetailEngine.Compute(
            period, period.Previous(), new List<AppSession>(), new List<AppSession>(),
            "x", "X", "其他", "其他", null, null);
        report.HasData.Should().BeFalse();
        report.TotalMinutes.Should().Be(0);
    }

    [Fact]
    public void Compute_WithHardware_FlagsSet()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var t = new DateTime(2026, 9, 16, 10, 0, 0);
        var cur = new List<AppSession> { S("lmstudio", t, 90, cat: "AI 助手") };
        var hw = new List<(double Cpu, double Gpu, double? Temp)>
        {
            (40, 80, 75), (50, 85, 82)
        };
        var report = AppDetailEngine.Compute(
            period, period.Previous(), cur, new List<AppSession>(),
            "lmstudio", "LM Studio", "AI 助手", "AI 助手", null, hw);
        report.HasHardware.Should().BeTrue();
        report.GpuLoadAvg.Should().BeApproximately(82.5, 0.2);
        report.GpuTempMax.Should().Be(82);
    }

    [Fact]
    public void CollectProcessSamples_OnlyTargetProcess()
    {
        var t = new DateTime(2026, 9, 16, 10, 0, 0);
        var sessions = new List<AppSession>
        {
            S("dev", t, 10),
            S("chrome", t.AddMinutes(20), 10)
        };
        var samples = new List<HardwareSample>
        {
            new() { Timestamp = "2026-09-16 10:00", CpuLoadAvg = 50, GpuLoadAvg = 10, GpuTempMax = 60 },
            new() { Timestamp = "2026-09-16 10:20", CpuLoadAvg = 20, GpuLoadAvg = 90, GpuTempMax = 80 }
        };
        var list = AppDetailEngine.CollectProcessSamples(sessions, samples, "dev", "dev");
        list.Should().HaveCount(1);
        list[0].Gpu.Should().Be(10);
    }

    [Fact]
    public void WithShare_SetsPercent()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var r = AppDetailEngine.Compute(
            period, period.Previous(), new List<AppSession>(), new List<AppSession>(),
            "a", "A", "x", "x", null, null);
        AppDetailEngine.WithShare(r, 12.34).SharePct.Should().Be(12.3);
    }

    [Fact]
    public void WindowTitles_Truncated()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var longTitle = new string('测', 80);
        var cur = new List<AppSession> { S("app", new DateTime(2026, 9, 16, 9, 0, 0), 30, longTitle) };
        var r = AppDetailEngine.Compute(
            period, period.Previous(), cur, new List<AppSession>(),
            "app", "App", "x", "x", null, null);
        r.TopWindowTitles.Single().Length.Should().BeLessThan(80);
        r.TopWindowTitles.Single().Should().EndWith("…");
    }
}
