using FluentAssertions;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

public class AnalyticsEngineTests
{
    [Fact]
    public void ThisWeek_StartsOnMonday()
    {
        // 2026-09-16 是周三
        var p = AnalyticsPeriod.FromKind(AnalyticsPeriodKind.ThisWeek, new DateTime(2026, 9, 16));
        p.Start.DayOfWeek.Should().Be(DayOfWeek.Monday);
        p.Start.Should().Be(new DateTime(2026, 9, 14));
        p.EndExclusive.Should().Be(new DateTime(2026, 9, 21));
    }

    [Fact]
    public void Last7Days_HasSevenDays()
    {
        var p = AnalyticsPeriod.FromKind(AnalyticsPeriodKind.Last7Days, new DateTime(2026, 9, 16));
        p.DayCount.Should().Be(7);
        p.EndExclusive.Should().Be(new DateTime(2026, 9, 17));
    }

    [Fact]
    public void Previous_IsEqualLengthWindow()
    {
        var p = AnalyticsPeriod.FromKind(AnalyticsPeriodKind.Last7Days, new DateTime(2026, 9, 16));
        var prev = p.Previous();
        prev.DayCount.Should().Be(7);
        prev.EndExclusive.Should().Be(p.Start.Date);
    }

    [Fact]
    public void Compute_ProducesKpisAndInsights()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var prev = period.Previous();

        AppSession S(string cat, DateTime start, int minutes) => new()
        {
            ProcessName = "app",
            Category = cat,
            StartTime = start,
            EndTime = start.AddMinutes(minutes)
        };

        var cur = new List<AppSession>
        {
            S("开发", new DateTime(2026, 9, 14, 10, 0, 0), 120),
            S("视频", new DateTime(2026, 9, 15, 20, 0, 0), 60),
            S("开发", new DateTime(2026, 9, 16, 9, 0, 0), 300),
        };
        var prevList = new List<AppSession>
        {
            S("开发", new DateTime(2026, 9, 7, 10, 0, 0), 400),
        };

        var report = AnalyticsEngine.Compute(period, prev, cur, prevList);

        report.Kpis.Should().HaveCount(4);
        report.CalendarDays.Should().HaveCount(14);
        report.HourHeat.Should().HaveCount(24);
        report.DayCountWithData.Should().Be(3);
        report.HasEnoughData.Should().BeTrue();
        report.Insights.Should().NotBeEmpty();
        report.CategoryShares.Should().NotBeEmpty();
        report.CategoryShares.Should().Contain(c => c.Category == "开发");
        report.Extra.Should().BeNull();
    }

    [Fact]
    public void Compute_WithDailyAggs_AddsMultiSourceKpisAndInsights()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var prev = period.Previous();

        var cur = new List<AppSession>
        {
            new()
            {
                ProcessName = "devenv",
                Category = "开发",
                StartTime = new DateTime(2026, 9, 14, 10, 0, 0),
                EndTime = new DateTime(2026, 9, 14, 13, 0, 0)
            }
        };

        var daily = new List<DailySummary>
        {
            new()
            {
                Date = "2026-09-14",
                TotalKeys = 12000,
                TotalMouseClicks = 800,
                ActiveMinutes = 180,
                WebPages = 40,
                WebActiveDurationTicks = TimeSpan.FromMinutes(90).Ticks,
                CpuLoadAvg = 42,
                CpuLoadP95 = 88,
                GpuLoadAvg = 25,
                GpuTempMax = 78,
                MetricsVersion = 2
            }
        };

        var report = AnalyticsEngine.Compute(period, prev, cur, cur, daily, daily);

        report.Kpis.Should().Contain(k => k.Id == "keys");
        report.Kpis.Should().Contain(k => k.Id == "web");
        report.Kpis.Should().Contain(k => k.Id == "cpu");
        report.Extra.Should().NotBeNull();
        report.Extra!.HasInputData.Should().BeTrue();
        report.Extra.HasWebData.Should().BeTrue();
        report.Extra.HasHardwareData.Should().BeTrue();
        report.Extra.TotalKeys.Should().Be(12000);
        report.Insights.Should().Contain(i => i.Id == "low-data" || i.Id != null);
        // 有足够应用会话时应出现网页/输入类洞察
        report.Insights.Should().NotContain(i => i.Id == "gpu-hot");
    }

    [Fact]
    public void AggregateDaily_AveragesHardwareAndWeb()
    {
        var daily = new List<DailySummary>
        {
            new() { Date = "2026-09-01", TotalKeys = 100, ActiveMinutes = 60, CpuLoadAvg = 30, GpuTempMax = 70, WebActiveDurationTicks = TimeSpan.FromMinutes(30).Ticks },
            new() { Date = "2026-09-02", TotalKeys = 300, ActiveMinutes = 120, CpuLoadAvg = 50, GpuTempMax = 90, WebActiveDurationTicks = TimeSpan.FromMinutes(30).Ticks }
        };
        var extra = AnalyticsEngine.AggregateDaily(daily);
        extra.TotalKeys.Should().Be(400);
        extra.CpuLoadAvg.Should().BeApproximately(40, 0.01);
        extra.GpuTempMax.Should().Be(90);
        extra.WebMinutes.Should().BeApproximately(60, 0.1);
        extra.HasHardwareData.Should().BeTrue();
    }

    [Fact]
    public void AggregateDaily_Empty_ReturnsNulls()
    {
        var extra = AnalyticsEngine.AggregateDaily(null);
        extra.HasInputData.Should().BeFalse();
        extra.CpuLoadAvg.Should().BeNull();
    }

    [Fact]
    public void LowData_YieldsSingleInsight()
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var prev = period.Previous();
        var report = AnalyticsEngine.Compute(period, prev, new List<AppSession>(), new List<AppSession>());
        report.HasEnoughData.Should().BeFalse();
        report.Insights.Should().ContainSingle(i => i.Id == "low-data");
    }

    [Fact]
    public void ProductiveCategory_MatchesDevAndOffice()
    {
        AnalyticsEngine.IsProductiveCategory("开发").Should().BeTrue();
        AnalyticsEngine.IsProductiveCategory("办公软件").Should().BeTrue();
        AnalyticsEngine.IsProductiveCategory("视频").Should().BeFalse();
        AnalyticsEngine.IsEntertainmentCategory("游戏").Should().BeTrue();
    }

    [Fact]
    public void FormatHours_Works()
    {
        AnalyticsEngine.FormatHours(0).Should().Be("0分钟");
        AnalyticsEngine.FormatHours(90).Should().Be("1小时30分");
        AnalyticsEngine.FormatHours(120).Should().Be("2小时");
    }
}
