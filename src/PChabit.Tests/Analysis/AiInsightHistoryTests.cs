using FluentAssertions;
using PChabit.Infrastructure.Formatters;
using PChabit.Infrastructure.Services;
using Xunit;

namespace PChabit.Tests.Analysis;

public class AiInsightHistoryTests
{
    [Fact]
    public void SnapshotFactory_FromParsed_RoundTripsPlan()
    {
        var raw = """
            {"summary":"本周专注下滑","findings":[{"title":"f","detail":"d"}],
             "plan":[{"title":"早收工","detail":"23点前","targetMetricId":"nightMinutes","targetValue":30,"effort":"low"}],
             "risks":["夜间偏多"]}
            """;
        var parsed = AnalyticsAiResponseParser.Parse(raw);
        parsed.Parsed.Should().BeTrue();

        var snap = AiInsightSnapshotFactory.FromParsed(
            "2026-09-22", "本周", new DateTime(2026, 9, 22), new DateTime(2026, 9, 29), parsed);

        snap.PeriodKey.Should().Be("2026-09-22");
        snap.Summary.Should().Contain("专注");
        snap.PlanJson.Should().NotBeNullOrEmpty();
        snap.PlanJson.Should().Contain("早收工");
        snap.PlanJson.Should().Contain("pending");
        snap.RisksJson.Should().Contain("夜间");
        snap.PromptVersion.Should().Be("v2");
    }

    [Fact]
    public void ToPackPlan_MapsFields()
    {
        var items = new List<AiPlanItem>
        {
            new() { Title = "A", Detail = "B", TargetMetricId = "nightMinutes", TargetValue = 30, Status = "done" }
        };
        var pack = AiInsightHistoryService.ToPackPlan(items);
        pack.Should().HaveCount(1);
        pack[0].Title.Should().Be("A");
        pack[0].TargetMetricId.Should().Be("nightMinutes");
        pack[0].Status.Should().Be("done");
    }

    [Fact]
    public void Parse_ThenFactory_KeepsActionKey()
    {
        var raw = """{"summary":"s","plan":[{"title":"t","detail":"d","actionKey":"rhythm"}]}""";
        var parsed = AnalyticsAiResponseParser.Parse(raw);
        var snap = AiInsightSnapshotFactory.FromParsed("k", "l", DateTime.Today, DateTime.Today.AddDays(7), parsed);
        snap.PlanJson.Should().Contain("rhythm");
    }
}
