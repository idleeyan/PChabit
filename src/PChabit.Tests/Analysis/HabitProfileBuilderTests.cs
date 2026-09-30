using FluentAssertions;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

public class HabitProfileBuilderTests
{
    private static HabitDay Day(int y, int m, int d, double active, double night, string? first = "09:30",
        string? last = "19:00", double? focus = null, int? switches = null, Dictionary<string, double>? labels = null)
    {
        labels ??= new Dictionary<string, double>
        {
            [ActivityLabels.WorkCode] = active * 0.5,
            [ActivityLabels.BrowseFun] = active * 0.2,
            [ActivityLabels.Comms] = active * 0.3
        };
        return new HabitDay(new DateTime(y, m, d), active, night, first, last, focus, null, switches, 20, labels);
    }

    [Fact]
    public void EmptyDays_UnknownChronotype()
    {
        var p = HabitProfileBuilder.Build(new List<HabitDay>());
        p.Chronotype.Should().Be("unknown");
        p.Mature.Should().BeFalse();
        p.SampleDays.Should().Be(0);
    }

    [Fact]
    public void NightOwls_NightLeaning()
    {
        var days = new List<HabitDay>();
        for (var i = 1; i <= 20; i++)
            days.Add(Day(2026, 9, Math.Min(i, 28), active: 400, night: 90, first: "10:00", last: "01:00", focus: 80));
        var p = HabitProfileBuilder.Build(days);
        p.Chronotype.Should().Be("night-leaning");
        p.Mature.Should().BeTrue();
        p.MedianNightMinutes.Should().BeGreaterThan(45);
        p.TypicalEndHour.Should().BeGreaterThanOrEqualTo(24);
    }

    [Fact]
    public void EarlyRisers_Early()
    {
        var days = new List<HabitDay>();
        for (var i = 1; i <= 20; i++)
            days.Add(Day(2026, 9, Math.Min(i, 28), active: 400, night: 5, first: "08:00", last: "18:00", focus: 100));
        var p = HabitProfileBuilder.Build(days);
        p.Chronotype.Should().Be("early");
        p.TypicalStartHour.Should().BeApproximately(8.0, 0.2);
        p.TypicalEndHour.Should().BeApproximately(18.0, 0.2);
    }

    [Fact]
    public void Median_HandlesEvenCount()
    {
        HabitProfileBuilder.Median(new[] { 1.0, 2.0, 3.0, 4.0 }).Should().Be(2.5);
        HabitProfileBuilder.Median(new[] { 5.0 }).Should().Be(5.0);
    }

    [Fact]
    public void Percentile_P50AndP90()
    {
        var vals = Enumerable.Range(1, 10).Select(x => (double)x).ToList(); // 1..10
        HabitProfileBuilder.Percentile(vals, 0.5).Should().BeApproximately(5.5, 0.1);
        HabitProfileBuilder.Percentile(vals, 0.9).Should().BeApproximately(9.1, 0.2);
    }

    [Fact]
    public void ParseHours_HHMM()
    {
        var h = HabitProfileBuilder.ParseHours(new[] { "09:30", "18:00" }).ToList();
        h.Should().HaveCount(2);
        h[0].Should().BeApproximately(9.5, 0.01);
    }

    [Fact]
    public void ProductiveShare_FromLabels()
    {
        var days = new List<HabitDay>();
        for (var i = 1; i <= 15; i++)
        {
            days.Add(new HabitDay(new DateTime(2026, 9, Math.Min(i, 28)), 300, 10, "09:00", "18:00", 60, 3, 20, 10,
                new Dictionary<string, double>
                {
                    [ActivityLabels.WorkCode] = 120,
                    [ActivityLabels.BrowseFun] = 80,
                    [ActivityLabels.Comms] = 100
                }));
        }
        var p = HabitProfileBuilder.Build(days);
        p.ProductiveSharePct.Should().BeApproximately(40.0, 0.1); // 120/300
        p.Mature.Should().BeTrue();
    }
}

public class PersonalBaselineBuilderTests
{
    private static HabitDay Day(DateTime date, double active, double night = 20, double focus = 60, int switches = 30)
        => new(date, active, night, "09:00", "18:30", focus, 2, switches, 15, null);

    [Fact]
    public void BuildsStats_WeekdayAndWeekend()
    {
        var days = new List<HabitDay>();
        // 2026-09-01 是周二：工作日 + 两个周末
        for (var i = 0; i < 14; i++)
        {
            var d = new DateTime(2026, 9, 1).AddDays(i);
            var active = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 200 : 400 + i * 5;
            days.Add(Day(d, active));
        }
        var b = PersonalBaselineBuilder.Build(days);
        b.SampleDays.Should().Be(14);
        b.ActiveMinutesWeekday.Count.Should().BeGreaterThan(5);
        b.ActiveMinutesWeekend.Count.Should().Be(4);
        b.ActiveMinutesWeekday.P50.Should().BeGreaterThan(0);
        b.Mature.Should().BeTrue(); // ≥10 weekdays with data
    }

    [Fact]
    public void DeviationPct_PositiveWhenAboveMedian()
    {
        var b = new BaselineStat(P50: 100, P75: 120, P90: 140, Min: 50, Max: 160, Count: 10);
        b.DeviationPct(120).Should().BeApproximately(20.0, 0.1);
        b.IsElevated(145).Should().BeTrue();
        b.IsLow(70).Should().BeTrue();
    }

    [Fact]
    public void ComputeDeviations_ContainsKeys()
    {
        var days = new List<HabitDay>();
        for (var i = 0; i < 12; i++)
            days.Add(Day(new DateTime(2026, 9, 1).AddDays(i), active: 380 + i, night: 15 + i, focus: 50 + i, switches: 20 + i));
        var b = PersonalBaselineBuilder.Build(days);
        var devs = PersonalBaselineBuilder.ComputeDeviations(b, activeMinutes: 420, focusMinutes: 70, nightMinutes: 30, switchPerHour: 25);
        devs.Select(d => d.MetricId).Should().Contain(new[] { "activeMinutes", "focusMinutes", "nightMinutes", "switchPerHour" });

        var summary = PersonalBaselineBuilder.ToAiSummary(b);
        summary.Should().ContainKey("mature");
        summary.Should().ContainKey("nightMinutesWeekday");
    }

    [Fact]
    public void ImmatureBaseline_NoFalseAlarm()
    {
        var b = new BaselineStat(100, 110, 120, 90, 130, Count: 1);
        b.DeviationPct(200).Should().BeNull();
        b.IsElevated(200).Should().BeFalse();
    }
}
