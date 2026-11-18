using FluentAssertions;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Services;
using Xunit;

namespace PChabit.Tests.Analysis;

public class HabitTrajectoryAndFeedbackTests
{
    private static HabitDay Day(int day, double night = 20, double focus = 60, double workCode = 100, double fun = 50)
        => new(new DateTime(2026, 9, day), 300, night, "09:00", "18:30", focus, 2, 25, 15,
            new Dictionary<string, double>
            {
                [ActivityLabels.WorkCode] = workCode,
                [ActivityLabels.BrowseFun] = fun
            });

    [Fact]
    public void Trajectory_NightUp_IsWorse()
    {
        var days = new List<HabitDay>();
        // 前半低夜间，后半高夜间
        for (var i = 1; i <= 16; i++)
        {
            var night = i <= 8 ? 10 : 80;
            days.Add(Day(i, night: night));
        }
        var trends = HabitTrajectoryBuilder.Build(days);
        var nightTrend = trends.FirstOrDefault(t => t.Name == "夜间使用");
        nightTrend.Should().NotBeNull();
        nightTrend!.Direction.Should().Be("worse");
    }

    [Fact]
    public void Trajectory_FocusUp_IsBetter()
    {
        var days = new List<HabitDay>();
        for (var i = 1; i <= 16; i++)
        {
            var focus = i <= 8 ? 20 : 90;
            days.Add(Day(i, focus: focus, night: 5));
        }
        var trends = HabitTrajectoryBuilder.Build(days);
        var focusTrend = trends.FirstOrDefault(t => t.Name == "专注时长");
        focusTrend.Should().NotBeNull();
        focusTrend!.Direction.Should().Be("better");
    }

    [Fact]
    public void Trajectory_InsufficientDays_Empty()
    {
        var days = Enumerable.Range(1, 5).Select(i => Day(i)).ToList();
        HabitTrajectoryBuilder.Build(days).Should().BeEmpty();
    }
}
