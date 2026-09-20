using FluentAssertions;
using PChabit.Core.Sync;
using Xunit;

namespace PChabit.Tests.Sync;

public class BrowserNameResolverTests
{
    [Fact]
    public void UserOverride_Wins_OverProcessName()
    {
        BrowserNameResolver.Resolve("工作豆包", isUserOverride: true, processName: "豆包浏览器")
            .Should().Be("工作豆包");
    }

    [Fact]
    public void NoOverride_UsesProcessName()
    {
        BrowserNameResolver.Resolve("Chromium", isUserOverride: false, processName: "Edge")
            .Should().Be("Edge");
    }

    [Fact]
    public void ProcessUnknown_FallsBackToReported()
    {
        BrowserNameResolver.Resolve("Chrome", isUserOverride: false, processName: "未知浏览器")
            .Should().Be("Chrome");
    }

    [Fact]
    public void AllEmpty_ReturnsUnknown()
    {
        BrowserNameResolver.Resolve(null, isUserOverride: false, processName: "未知浏览器")
            .Should().Be("Unknown");
    }

    [Fact]
    public void BlankOverride_FallsBackToProcess()
    {
        BrowserNameResolver.Resolve("   ", isUserOverride: true, processName: "Chrome")
            .Should().Be("Chrome");
    }

    [Fact]
    public void ChromiumFallback_Kept_WhenProcessUnknown()
    {
        BrowserNameResolver.Resolve("Chromium", isUserOverride: false, processName: null)
            .Should().Be("Chromium");
    }

    [Fact]
    public void Override_TrimsWhitespace()
    {
        BrowserNameResolver.Resolve("  工作 Edge  ", isUserOverride: true, processName: "Edge")
            .Should().Be("工作 Edge");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a", true)]
    [InlineData("工作豆包", true)]
    public void IsValidLabel(string? label, bool expected)
    {
        BrowserNameResolver.IsValidLabel(label).Should().Be(expected);
    }

    [Fact]
    public void NormalizeLabel_TruncatesLongNames()
    {
        var longName = new string('x', 40);
        var result = BrowserNameResolver.NormalizeLabel(longName);
        result.Should().NotBeNull();
        result!.Length.Should().Be(32);
    }

    [Fact]
    public void NormalizeLabel_RejectsEmpty()
    {
        BrowserNameResolver.NormalizeLabel("  ").Should().BeNull();
    }
}
