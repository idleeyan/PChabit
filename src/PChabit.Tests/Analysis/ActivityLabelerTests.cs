using FluentAssertions;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

public class ActivityLabelerTests
{
    private static ActivityLabelInput Input(
        string? process = null,
        string? appCat = null,
        string? title = null,
        string? webCat = null,
        string? domain = null,
        double keysPerMin = 30,
        double minutes = 30) =>
        new(process, appCat, title, webCat, domain, keysPerMin, minutes);

    [Fact]
    public void Ide_WithTyping_IsWorkCode()
    {
        var r = ActivityLabeler.Label(Input(process: "devenv", appCat: "开发"));
        r.Label.Should().Be(ActivityLabels.WorkCode);
        r.Confidence.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void MeetingProcess_IsMeeting()
    {
        var r = ActivityLabeler.Label(Input(process: "Teams", title: "站会", keysPerMin: 5));
        r.Label.Should().Be(ActivityLabels.Meeting);
    }

    [Fact]
    public void MeetingTitle_IsMeeting()
    {
        var r = ActivityLabeler.Label(Input(process: "chrome", title: "项目周会 - 会议", keysPerMin: 2));
        r.Label.Should().Be(ActivityLabels.Meeting);
    }

    [Fact]
    public void WeChat_IsComms()
    {
        var r = ActivityLabeler.Label(Input(process: "WeChat", appCat: "沟通"));
        r.Label.Should().Be(ActivityLabels.Comms);
    }

    [Fact]
    public void OfficeDoc_IsWorkDoc()
    {
        var r = ActivityLabeler.Label(Input(process: "WINWORD", appCat: "办公", keysPerMin: 25));
        r.Label.Should().Be(ActivityLabels.WorkDoc);
        r.Confidence.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void WebVideo_IsBrowseFun()
    {
        var r = ActivityLabeler.Label(Input(process: "chrome", webCat: "视频", keysPerMin: 3));
        r.Label.Should().Be(ActivityLabels.BrowseFun);
    }

    [Fact]
    public void WebSearch_IsBrowseInfo()
    {
        var r = ActivityLabeler.Label(Input(process: "msedge", webCat: "搜索", keysPerMin: 8));
        r.Label.Should().Be(ActivityLabels.BrowseInfo);
    }

    [Fact]
    public void WebDevDocs_LowTyping_IsLearn()
    {
        var r = ActivityLabeler.Label(Input(process: "chrome", webCat: "开发", keysPerMin: 5));
        r.Label.Should().Be(ActivityLabels.Learn);
    }

    [Fact]
    public void WebDevDocs_HighTyping_IsWorkCode()
    {
        var r = ActivityLabeler.Label(Input(process: "chrome", webCat: "开发", keysPerMin: 40));
        r.Label.Should().Be(ActivityLabels.WorkCode);
    }

    [Fact]
    public void GameProcess_IsGame()
    {
        var r = ActivityLabeler.Label(Input(process: "steam", appCat: "娱乐", keysPerMin: 10));
        r.Label.Should().Be(ActivityLabels.Game);
    }

    [Fact]
    public void LongIdleSession_IsIdle()
    {
        var r = ActivityLabeler.Label(Input(process: "unknown-saver", appCat: null, keysPerMin: 0, minutes: 45));
        r.Label.Should().Be(ActivityLabels.Idle);
    }

    [Fact]
    public void Explorer_IsAdmin()
    {
        var r = ActivityLabeler.Label(Input(process: "explorer", appCat: "系统工具", keysPerMin: 2));
        r.Label.Should().Be(ActivityLabels.Admin);
    }

    [Fact]
    public void AggregateLabelMinutes_SumsByLabel()
    {
        var inputs = new[]
        {
            Input(process: "devenv", appCat: "开发", minutes: 60),
            Input(process: "WeChat", appCat: "沟通", minutes: 20),
            Input(process: "chrome", webCat: "视频", minutes: 30)
        };
        var map = ActivityLabeler.AggregateLabelMinutes(inputs);
        map[ActivityLabels.WorkCode].Should().Be(60);
        map[ActivityLabels.Comms].Should().Be(20);
        map[ActivityLabels.BrowseFun].Should().Be(30);
        map[ActivityLabels.Meeting].Should().Be(0);
    }

    [Fact]
    public void NightOverlap_CountsLateAndEarly()
    {
        // 22:00–24:00 → 夜间 60 分钟
        var late = new DateTime(2026, 9, 22, 22, 0, 0);
        ActivityLabeler.OverlapNightMinutes(late, late.AddHours(2)).Should().Be(60);

        // 05:00–07:00 → 夜间 60 分钟（05–06）
        var early = new DateTime(2026, 9, 23, 5, 0, 0);
        ActivityLabeler.OverlapNightMinutes(early, early.AddHours(2)).Should().Be(60);

        // 10:00–11:00 → 0
        var noon = new DateTime(2026, 9, 23, 10, 0, 0);
        ActivityLabeler.OverlapNightMinutes(noon, noon.AddHours(1)).Should().Be(0);
    }

    [Fact]
    public void AllLabels_AreKnownConstants()
    {
        foreach (var l in ActivityLabels.All)
            ActivityLabels.IsKnown(l).Should().BeTrue();
    }
}
