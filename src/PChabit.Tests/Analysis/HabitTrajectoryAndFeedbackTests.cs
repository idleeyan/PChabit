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

    [Fact]
    public void PlanFeedback_CountsStatus()
    {
        var items = new List<AiPlanItem>
        {
            new() { Title = "A", Status = "done" },
            new() { Title = "B", Status = "skipped" },
            new() { Title = "C", Status = "pending" }
        };
        var fb = AiPlanFeedbackBuilder.FromPlan(items);
        fb.Total.Should().Be(3);
        fb.Done.Should().Be(1);
        fb.Skipped.Should().Be(1);
        fb.OftenDone.Should().Contain("A");
        fb.OftenSkipped.Should().Contain("B");
    }

    [Fact]
    public void MarkdownExporter_IncludesSections()
    {
        var raw = """{"summary":"本周专注下滑","findings":[{"title":"f","detail":"d"}],"plan":[{"title":"早收工","detail":"x"}],"risks":["r"]}""";
        var parsed = PChabit.Infrastructure.Formatters.AnalyticsAiResponseParser.Parse(raw);
        var md = PChabit.Infrastructure.Formatters.AiInsightMarkdownExporter.Export(
            "本周", new DateTime(2026, 9, 22), new DateTime(2026, 9, 29), parsed);
        md.Should().Contain("结论");
        md.Should().Contain("周计划");
        md.Should().Contain("早收工");
        md.Should().Contain("风险");
    }
}
