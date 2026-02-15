using FluentAssertions;
using Moq;
using Tai.Application.Analysis;
using Tai.Core.Entities;
using Tai.Core.Interfaces;
using Xunit;

namespace Tai.Tests.Analysis;

public class BehaviorAnalyzerTests
{
    private readonly Mock<IRepository<AppSession>> _appSessionRepoMock;
    private readonly Mock<IRepository<KeyboardSession>> _keyboardRepoMock;
    private readonly Mock<IRepository<MouseSession>> _mouseRepoMock;
    private readonly BehaviorAnalyzer _analyzer;
    
    public BehaviorAnalyzerTests()
    {
        _appSessionRepoMock = new Mock<IRepository<AppSession>>();
        _keyboardRepoMock = new Mock<IRepository<KeyboardSession>>();
        _mouseRepoMock = new Mock<IRepository<MouseSession>>();
        
        _analyzer = new BehaviorAnalyzer(
            _appSessionRepoMock.Object,
            _keyboardRepoMock.Object,
            _mouseRepoMock.Object);
    }
    
    [Fact]
    public async Task AnalyzeAsync_WithNoSessions_ShouldReturnEmptyResult()
    {
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(new List<AppSession>());
        
        var result = await _analyzer.AnalyzeAsync(DateTime.Today, DateTime.Today);
        
        result.Should().NotBeNull();
        result.TotalActiveTime.Should().Be(TimeSpan.Zero);
        result.TopApps.Should().BeEmpty();
        result.PeakHours.Should().BeEmpty();
    }
    
    [Fact]
    public async Task AnalyzeAsync_WithSessions_ShouldReturnCorrectStats()
    {
        var sessions = new List<AppSession>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ProcessName = "code",
                StartTime = DateTime.Today.AddHours(9),
                EndTime = DateTime.Today.AddHours(10),
                Duration = TimeSpan.FromHours(1)
            },
            new()
            {
                Id = Guid.NewGuid(),
                ProcessName = "chrome",
                StartTime = DateTime.Today.AddHours(10),
                EndTime = DateTime.Today.AddHours(11),
                Duration = TimeSpan.FromHours(1)
            }
        };
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(sessions);
        
        var result = await _analyzer.AnalyzeAsync(DateTime.Today, DateTime.Today);
        
        result.Should().NotBeNull();
        result.TotalActiveTime.Should().Be(TimeSpan.FromHours(2));
        result.TopApps.Should().HaveCount(2);
    }
    
    [Fact]
    public async Task GetProductivityReportAsync_WithNoSessions_ShouldReturnEmptyReport()
    {
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(new List<AppSession>());
        
        var result = await _analyzer.GetProductivityReportAsync(DateTime.Today);
        
        result.Should().NotBeNull();
        result.Date.Should().Be(DateTime.Today);
        result.TotalWorkTime.Should().Be(TimeSpan.Zero);
        result.FocusSessionCount.Should().Be(0);
    }
    
    [Fact]
    public async Task GetAppProductivityScoresAsync_ShouldReturnScores()
    {
        var sessions = new List<AppSession>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ProcessName = "code",
                StartTime = DateTime.Today,
                EndTime = DateTime.Today.AddHours(1),
                Duration = TimeSpan.FromHours(1)
            },
            new()
            {
                Id = Guid.NewGuid(),
                ProcessName = "youtube",
                StartTime = DateTime.Today.AddHours(1),
                EndTime = DateTime.Today.AddHours(2),
                Duration = TimeSpan.FromHours(1)
            }
        };
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(sessions);
        
        var result = await _analyzer.GetAppProductivityScoresAsync(DateTime.Today);
        
        result.Should().NotBeNull();
        result.Should().ContainKey("code");
        result.Should().ContainKey("youtube");
        result["code"].Should().BeGreaterThan(result["youtube"]);
    }
}

public class ContextResolverTests
{
    private readonly Mock<IRepository<AppSession>> _appSessionRepoMock;
    private readonly ContextResolver _resolver;
    
    public ContextResolverTests()
    {
        _appSessionRepoMock = new Mock<IRepository<AppSession>>();
        _resolver = new ContextResolver(_appSessionRepoMock.Object);
    }
    
    [Theory]
    [InlineData("code", "Development")]
    [InlineData("devenv", "Development")]
    [InlineData("chrome", "Research")]
    [InlineData("slack", "Communication")]
    [InlineData("youtube", "Entertainment")]
    public async Task ResolveContextAsync_ShouldReturnCorrectCategory(string processName, string expectedCategory)
    {
        var result = await _resolver.ResolveContextAsync(processName, null, DateTime.Now);
        
        result.Should().NotBeNull();
        result.Category.Should().Be(expectedCategory);
    }
    
    [Theory]
    [InlineData("code", 0.9)]
    [InlineData("youtube", 0.2)]
    [InlineData("slack", 0.5)]
    public async Task ResolveContextAsync_ShouldReturnCorrectProductivityScore(string processName, double minScore)
    {
        var result = await _resolver.ResolveContextAsync(processName, null, DateTime.Now);
        
        result.ProductivityScore.Should().BeApproximately(minScore, 0.3);
    }
    
    [Fact]
    public async Task ResolveContextAsync_WithWindowTitle_ShouldExtractProject()
    {
        var result = await _resolver.ResolveContextAsync(
            "code",
            "MyProject - Visual Studio Code",
            DateTime.Now);
        
        result.Should().NotBeNull();
        result.Project.Should().NotBeNullOrEmpty();
    }
    
    [Fact]
    public async Task GetContextTagsAsync_ShouldReturnTags()
    {
        var sessions = new List<AppSession>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ProcessName = "code",
                WindowTitle = "debug - Visual Studio Code",
                StartTime = DateTime.Today,
                EndTime = DateTime.Today.AddHours(1),
                Duration = TimeSpan.FromHours(1)
            }
        };
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(sessions);
        
        var result = await _resolver.GetContextTagsAsync(DateTime.Today, DateTime.Today.AddDays(1));
        
        result.Should().NotBeNull();
    }
}
