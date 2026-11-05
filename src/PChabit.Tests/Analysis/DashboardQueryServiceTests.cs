using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using PChabit.Application.Queries;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Data;
using Xunit;

namespace PChabit.Tests.Analysis;

/// <summary>
/// DashboardQueryService 单测（3.71.0，改造计划 D4 步骤 2b+3）。
/// 用内存 SQLite 覆盖计划验收点：今日无数据 / 昨日缺字段 / 聚合缺失时实时补齐 /
/// 降级路径（效率计算抛异常）/ 当前活动会话。
/// </summary>
public class DashboardQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PChabitDbContext> _options;

    public DashboardQueryServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PChabitDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    private PChabitDbContext CreateContext() => new(_options);

    private sealed class FakeFactory : IDbContextFactory<PChabitDbContext>
    {
        private readonly Func<PChabitDbContext> _factory;
        public FakeFactory(Func<PChabitDbContext> factory) => _factory = factory;
        public PChabitDbContext CreateDbContext() => _factory();
    }

    private DashboardQueryService CreateService(IEfficiencyCalculator? calculator = null)
    {
        // 每次调用新建 context，但共享同一条打开的内存连接（连接生命周期由测试托管）
        var factory = new FakeFactory(CreateContext);
        return new DashboardQueryService(factory, calculator ?? Mock.Of<IEfficiencyCalculator>());
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task EmptyDatabase_YieldsSafeDefaults_NoException()
    {
        var svc = CreateService();

        var stats = await svc.LoadAsync(DateTime.Today);

        Assert.NotNull(stats);
        Assert.Equal("0小时 0分钟", stats.TodayActiveTime);
        Assert.Equal("0", stats.TodayKeyPresses);
        Assert.Equal("无数据", stats.MostUsedApp);
        Assert.Empty(stats.TopAppsRaw);
        // 已流逝的小时仍补 0 活跃条（含「低效」分级），这是既有行为
        var elapsedHours = DateTime.Now.Hour + 1;
        Assert.Equal(elapsedHours, stats.HourlyActivity.Count);
        Assert.All(stats.HourlyActivity, h => Assert.Equal(0, h.Activity));
        Assert.Null(stats.ActiveDeviationPct);
        Assert.Equal("", stats.CurrentAppName);
        // 图表：已流逝小时为 0 值实柱，未来小时灰色占位，横轴恒 24 根
        Assert.Equal(24, stats.Chart.Bars.Count);
        Assert.Equal(elapsedHours, stats.Chart.Bars.Count(b => !b.IsFuture));
        Assert.All(stats.Chart.Bars.Take(elapsedHours), b => Assert.Equal(0, b.Value));
        Assert.Equal("今天还没有活动记录", stats.Chart.Summary);
    }

    [Fact]
    public async Task TodaySessions_FillSnapshot_AndDeriveMissingSummaryFields()
    {
        using var db = CreateContext();
        var today = DateTime.Today;
        // 一个有自定义分类映射的「办公」进程 + 一个未映射进程
        var cat = new ProgramCategory { Name = "办公", Color = "#123456", IsActive = true };
        cat.ProgramMappings.Add(new ProgramCategoryMapping { ProcessName = "WINWORD.EXE" });
        db.ProgramCategories.Add(cat);
        db.AppSessions.Add(new AppSession
        {
            StartTime = today.AddHours(9), EndTime = today.AddHours(10),
            ActiveDuration = TimeSpan.FromMinutes(60), ProcessName = "WINWORD.EXE",
            ExecutablePath = @"C:\Office\WINWORD.EXE", Category = "办公"
        });
        db.AppSessions.Add(new AppSession
        {
            StartTime = today.AddHours(13), EndTime = today.AddHours(13.5),
            ActiveDuration = TimeSpan.FromMinutes(30), ProcessName = "Unknown.exe",
            ExecutablePath = @"C:\x\Unknown.exe"
        });
        db.KeyboardSessions.Add(new KeyboardSession { Date = today.AddHours(9), TotalKeyPresses = 100 });
        db.MouseSessions.Add(new MouseSession { Date = today.AddHours(9), LeftClickCount = 7, RightClickCount = 3 });
        await db.SaveChangesAsync();

        var svc = CreateService();
        var stats = await svc.LoadAsync(today);

        Assert.Equal("1小时 30分钟", stats.TodayActiveTime);
        Assert.Equal("100", stats.TodayKeyPresses);
        Assert.Equal("10", stats.TodayMouseClicks);
        // DailySummaries 为空：AppSwitches / 首末活跃由今日会话实时补齐（不写库）
        Assert.Equal(1, stats.AppSwitches); // 会话数-1
        Assert.Equal("09:00", stats.FirstActiveTime);
        Assert.Equal("13:30", stats.LastActiveTime);
        // 专注分钟实时补齐：只有「办公」分类计入
        Assert.Equal(60, stats.FocusMinutes);
        Assert.Equal("WINWORD.EXE", stats.MostUsedApp);
        Assert.Equal(2, stats.TopAppsRaw.Count);
        // 分类解析走映射表：映射命中 → 办公；未映射回落快照 Category
        Assert.Equal("办公", stats.TopAppsRaw.First(x => x.ProcessName == "WINWORD.EXE").CategoryName);
        Assert.Equal("未分类", stats.TopAppsRaw.First(x => x.ProcessName == "Unknown.exe").CategoryName);
        // 分类构成两档：映射命中的「办公」+ 未映射回落的「未分类」，按时长降序
        Assert.Equal(2, stats.CategoryRaw.Count);
        Assert.Equal("办公", stats.CategoryRaw[0].Name);
        Assert.Equal(60, stats.CategoryRaw[0].Duration);
        Assert.Equal("#123456", stats.CategoryRaw[0].ColorHex);
        Assert.Equal("未分类", stats.CategoryRaw[1].Name);

        // 今日实时合并不写库：DailySummaries 仍为空
        using var verify = CreateContext();
        Assert.Empty(verify.DailySummaries);
    }

    [Fact]
    public async Task EfficiencyCalculator_Failure_DegradesToZero()
    {
        var calc = new Mock<IEfficiencyCalculator>();
        calc.Setup(c => c.CalculateDetailedScoreAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("db locked"));
        var svc = CreateService(calc.Object);

        var stats = await svc.LoadAsync(DateTime.Today);

        Assert.Equal(0, stats.EfficiencyScore);
    }

    [Fact]
    public async Task EfficiencyCalculator_Success_PropagatesScore()
    {
        var calc = new Mock<IEfficiencyCalculator>();
        calc.Setup(c => c.CalculateDetailedScoreAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new EfficiencyBreakdown { TotalScore = 88.5 });
        var svc = CreateService(calc.Object);

        var stats = await svc.LoadAsync(DateTime.Today);

        Assert.Equal(88.5, stats.EfficiencyScore);
    }

    [Fact]
    public async Task OpenSession_ReportsCurrentApp()
    {
        using var db = CreateContext();
        db.AppSessions.Add(new AppSession
        {
            StartTime = DateTime.Now.AddMinutes(-5), EndTime = null,
            ProcessName = "live.exe", AppName = "正在用的应用"
        });
        await db.SaveChangesAsync();

        var svc = CreateService();
        var stats = await svc.LoadAsync(DateTime.Today);

        Assert.Equal("正在用的应用", stats.CurrentAppName);
        Assert.Equal(DateTime.Now.AddMinutes(-5).ToString("HH:mm"), stats.CurrentAppSince);
    }

    [Fact]
    public async Task YesterdaySessions_ProduceComparePct_AgainstToday()
    {
        using var db = CreateContext();
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        var now = DateTime.Now;
        // 昨日会话必须落在「昨日同时刻截断点」之前，否则环比被跳过（cutoff = yesterday + now.TimeOfDay）
        var safeYesterdayTime = now.Hour >= 9 ? yesterday.AddHours(8) : yesterday;
        db.AppSessions.Add(new AppSession
        {
            StartTime = safeYesterdayTime,
            EndTime = safeYesterdayTime.AddHours(1),
            ActiveDuration = TimeSpan.FromMinutes(60), ProcessName = "a.exe", ExecutablePath = "a.exe"
        });
        db.KeyboardSessions.Add(new KeyboardSession { Date = safeYesterdayTime, TotalKeyPresses = 1000 });
        db.MouseSessions.Add(new MouseSession { Date = safeYesterdayTime, LeftClickCount = 9 });
        // 今日：活跃 60 分钟、100 键、10 击
        db.AppSessions.Add(new AppSession
        {
            StartTime = today.AddHours(8), EndTime = today.AddHours(9),
            ActiveDuration = TimeSpan.FromMinutes(60), ProcessName = "a.exe", ExecutablePath = "a.exe"
        });
        db.KeyboardSessions.Add(new KeyboardSession { Date = today.AddHours(8), TotalKeyPresses = 100 });
        db.MouseSessions.Add(new MouseSession { Date = today.AddHours(8), LeftClickCount = 10 });
        await db.SaveChangesAsync();

        var svc = CreateService();
        var stats = await svc.LoadAsync(today);

        Assert.NotNull(stats.KeysComparePct);
        Assert.Equal(-90, stats.KeysComparePct!.Value, 1);  // 100 vs 1000 = -90%
        Assert.NotNull(stats.ClicksComparePct);
        Assert.Equal(11.1, stats.ClicksComparePct!.Value, 1); // 10 vs 9 ≈ +11.1%
    }

    [Fact]
    public async Task NoYesterdayData_ComparePctsStayNull()
    {
        using var db = CreateContext();
        var today = DateTime.Today;
        db.AppSessions.Add(new AppSession
        {
            StartTime = today.AddHours(8), EndTime = today.AddHours(9),
            ActiveDuration = TimeSpan.FromMinutes(60), ProcessName = "a.exe", ExecutablePath = "a.exe"
        });
        db.KeyboardSessions.Add(new KeyboardSession { Date = today.AddHours(8), TotalKeyPresses = 100 });
        await db.SaveChangesAsync();

        var svc = CreateService();
        var stats = await svc.LoadAsync(today);

        Assert.Null(stats.ActiveComparePct);
        Assert.Null(stats.KeysComparePct);
        Assert.Null(stats.ClicksComparePct);
        Assert.Null(stats.WebComparePct);
    }

    [Fact]
    public async Task LegacyWebSessions_AreExcluded()
    {
        using var db = CreateContext();
        var today = DateTime.Today;
        db.WebSessions.Add(new WebSession
        {
            StartTime = today.AddHours(8), EndTime = today.AddHours(8).AddMinutes(5),
            Domain = "real.com", IsLegacy = false
        });
        db.WebSessions.Add(new WebSession
        {
            StartTime = today.AddHours(9), EndTime = today.AddHours(9).AddMinutes(5),
            Domain = "old.com", IsLegacy = true
        });
        await db.SaveChangesAsync();

        var svc = CreateService();
        var stats = await svc.LoadAsync(today);

        Assert.Equal("1", stats.TodayWebPages);
        Assert.Equal("real.com", stats.MostVisitedSite);
        Assert.Single(stats.TopWebsites);
    }
}
