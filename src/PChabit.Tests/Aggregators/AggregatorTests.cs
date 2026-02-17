using FluentAssertions;
using Moq;
using PChabit.Application.Aggregators;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using Xunit;

namespace PChabit.Tests.Aggregators;

public class PatternDetectorTests
{
    private readonly Mock<IRepository<AppSession>> _appSessionRepoMock;
    private readonly Mock<IRepository<DailyPattern>> _patternRepoMock;
    private readonly PatternDetector _detector;
    
    public PatternDetectorTests()
    {
        _appSessionRepoMock = new Mock<IRepository<AppSession>>();
        _patternRepoMock = new Mock<IRepository<DailyPattern>>();
        
        _detector = new PatternDetector(
            _appSessionRepoMock.Object,
            _patternRepoMock.Object);
    }
    
    [Fact]
    public async Task DetectPatternsAsync_WithNoSessions_ShouldReturnEmptyList()
    {
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(new List<AppSession>());
        
        var result = await _detector.DetectPatternsAsync(DateTime.Today);
        
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
    
    [Fact]
    public async Task DetectPatternsAsync_WithFrequentApps_ShouldDetectFrequentAppPattern()
    {
        var sessions = CreateTestSessions("code", 5);
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(sessions);
        
        var result = await _detector.DetectPatternsAsync(DateTime.Today);
        
        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Type == PatternType.FrequentApp);
    }
    
    [Fact]
    public async Task DetectPatternsAsync_WithPeakHours_ShouldDetectPeakHourPattern()
    {
        var sessions = new List<AppSession>();
        
        for (var i = 0; i < 10; i++)
        {
            sessions.Add(new AppSession
            {
                Id = Guid.NewGuid(),
                ProcessName = "code",
                StartTime = DateTime.Today.AddHours(9).AddMinutes(i * 5),
                EndTime = DateTime.Today.AddHours(9).AddMinutes((i + 1) * 5),
                Duration = TimeSpan.FromMinutes(5)
            });
        }
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(sessions);
        
        var result = await _detector.DetectPatternsAsync(DateTime.Today);
        
        result.Should().Contain(p => p.Type == PatternType.PeakHour);
    }
    
    [Fact]
    public async Task DetectPatternsAsync_WithAppSequences_ShouldDetectSequencePattern()
    {
        var sessions = new List<AppSession>
        {
            CreateSession("code", 0),
            CreateSession("chrome", 1),
            CreateSession("code", 2),
            CreateSession("chrome", 3),
            CreateSession("code", 4),
            CreateSession("chrome", 5)
        };
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(sessions);
        
        var result = await _detector.DetectPatternsAsync(DateTime.Today);
        
        result.Should().Contain(p => p.Type == PatternType.AppSequence);
    }
    
    [Fact]
    public async Task DetectPatternsAsync_WithContextSwitches_ShouldDetectSwitchPattern()
    {
        var sessions = new List<AppSession>();
        
        for (var i = 0; i < 20; i++)
        {
            sessions.Add(new AppSession
            {
                Id = Guid.NewGuid(),
                ProcessName = i % 2 == 0 ? "code" : "chrome",
                StartTime = DateTime.Today.AddMinutes(i * 2),
                EndTime = DateTime.Today.AddMinutes((i + 1) * 2),
                Duration = TimeSpan.FromMinutes(2)
            });
        }
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(sessions);
        
        var result = await _detector.DetectPatternsAsync(DateTime.Today);
        
        result.Should().Contain(p => p.Type == PatternType.ContextSwitch);
    }
    
    [Fact]
    public async Task GetWeeklySummaryAsync_ShouldReturnSummary()
    {
        var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(new List<AppSession>());
        
        var result = await _detector.GetWeeklySummaryAsync(weekStart);
        
        result.Should().NotBeNull();
        result.WeekStart.Should().Be(weekStart);
        result.DailyPatterns.Should().HaveCount(7);
    }
    
    private static List<AppSession> CreateTestSessions(string processName, int count)
    {
        var sessions = new List<AppSession>();
        
        for (var i = 0; i < count; i++)
        {
            sessions.Add(new AppSession
            {
                Id = Guid.NewGuid(),
                ProcessName = processName,
                StartTime = DateTime.Today.AddHours(9).AddMinutes(i * 30),
                EndTime = DateTime.Today.AddHours(9).AddMinutes((i + 1) * 30),
                Duration = TimeSpan.FromMinutes(30)
            });
        }
        
        return sessions;
    }
    
    private static AppSession CreateSession(string processName, int offsetMinutes)
    {
        return new AppSession
        {
            Id = Guid.NewGuid(),
            ProcessName = processName,
            StartTime = DateTime.Today.AddMinutes(offsetMinutes * 10),
            EndTime = DateTime.Today.AddMinutes((offsetMinutes + 1) * 10),
            Duration = TimeSpan.FromMinutes(10)
        };
    }
}

public class DailyAggregatorTests
{
    private readonly Mock<IRepository<AppSession>> _appSessionRepoMock;
    private readonly Mock<IRepository<KeyboardSession>> _keyboardRepoMock;
    private readonly Mock<IRepository<MouseSession>> _mouseRepoMock;
    private readonly DailyAggregator _aggregator;
    
    public DailyAggregatorTests()
    {
        _appSessionRepoMock = new Mock<IRepository<AppSession>>();
        _keyboardRepoMock = new Mock<IRepository<KeyboardSession>>();
        _mouseRepoMock = new Mock<IRepository<MouseSession>>();
        
        _aggregator = new DailyAggregator(
            _appSessionRepoMock.Object,
            _keyboardRepoMock.Object,
            _mouseRepoMock.Object);
    }
    
    [Fact]
    public async Task GetDailySummaryAsync_ShouldReturnValidStats()
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
            }
        };
        
        _appSessionRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<AppSession, bool>>>()))
            .ReturnsAsync(sessions);
        
        _keyboardRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<KeyboardSession, bool>>>()))
            .ReturnsAsync(new List<KeyboardSession>());
        
        _mouseRepoMock
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<MouseSession, bool>>>()))
            .ReturnsAsync(new List<MouseSession>());
        
        var result = await _aggregator.GetDailySummaryAsync(DateTime.Today);
        
        result.Should().NotBeNull();
        result.Date.Should().Be(DateTime.Today);
        result.TotalActiveTime.Should().Be(TimeSpan.FromHours(1));
    }
}
