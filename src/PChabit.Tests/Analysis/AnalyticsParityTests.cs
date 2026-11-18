using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;
using Xunit;

namespace PChabit.Tests.Analysis;

/// <summary>
/// P3 收口块回归：Rust 数据面的分析页计算必须与 C# `AnalyticsEngine` 逐项一致。
/// 做法是同一个临时库跑两条路径（C# 引擎 / pchabit-datad analytics）再对账，
/// 入库数据是人工构造的小样本，指标都能手算核对。
/// </summary>
public class AnalyticsParityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PChabitDbContext> _options;

    private static readonly AnalyticsPeriod Period =
        new(new DateTime(2026, 11, 10), new DateTime(2026, 11, 17), "近 7 天");

    public AnalyticsParityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pchabit-parity-{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PChabitDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* 临时库 */ }
    }

    private async Task SeedAsync()
    {
        await using var db = new PChabitDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var t = (int y, int m, int d, int h, int mi) => new DateTime(y, m, d, h, mi, 0);

        // 当前期：4 段会话（含一段跨夜）
        db.AppSessions.AddRange(
            Session("code.exe", "VS Code", "开发", t(2026, 11, 10, 9, 0), t(2026, 11, 10, 10, 30)),
            Session("browser.exe", "Microsoft Edge", "浏览", t(2026, 11, 10, 14, 0), t(2026, 11, 10, 15, 0)),
            Session("code.exe", "VS Code", "开发", t(2026, 11, 12, 9, 0), t(2026, 11, 12, 10, 0)),
            Session("game.exe", "Steam Game", "游戏", t(2026, 11, 12, 20, 0), t(2026, 11, 12, 21, 30)),
            Session("code.exe", "VS Code", "开发", t(2026, 11, 14, 23, 30), t(2026, 11, 15, 0, 30))
        );

        // 上一期：2 段
        db.AppSessions.AddRange(
            Session("code.exe", "VS Code", "开发", t(2026, 11, 5, 9, 0), t(2026, 11, 5, 10, 40)),
            Session("game.exe", "Steam Game", "游戏", t(2026, 11, 5, 20, 0), t(2026, 11, 5, 21, 0))
        );

        db.DailySummaries.AddRange(
            Daily("2026-11-10", 5000, 300, 480, 120, 20, 40, 80, 30, 70, 50, 1_000_000_000, 2_000_000_000, 3),
            Daily("2026-11-12", 3000, 200, 500, 60, 10, 45, 85, 25, 65, 55, 500_000_000, 900_000_000, 1),
            Daily("2026-11-05", 2000, 150, 400, 50, 8, 50, 70, 35, 72, 52, 400_000_000, 700_000_000, 0)
        );

        db.HardwareSamples.AddRange(
            Hw("2026-11-10 09:30", 40, 20, 60, 50),
            Hw("2026-11-10 09:31", 42, 22, 61, 51),
            Hw("2026-11-12 20:30", 80, 50, 75, 60)
        );

        // WebSessions：Duration/ActiveDuration 是 100ns ticks（1 分钟 = 6e8）
        db.WebSessions.AddRange(
            Web(t(2026, 11, 10, 14, 10), 10, 8, "浏览"),
            Web(t(2026, 11, 12, 20, 5), 6, 6, "视频")
        );

        db.ProcessNetworkUsages.AddRange(
            Net("2026-11-10", 14, 10_485_760),
            Net("2026-11-12", 20, 5_242_880)
        );

        await db.SaveChangesAsync();
    }

    private static AppSession Session(string proc, string app, string cat, DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        ProcessName = proc,
        AppName = app,
        Category = cat,
        WindowTitle = app,
        StartTime = start,
        EndTime = end
    };

    private static DailySummary Daily(string date, long keys, long clicks, double active, double webMin,
        int webPages, double cpuAvg, double cpuP95, double gpuAvg, double gpuTemp, double mem,
        long netUp, long netDown, long search) => new()
    {
        Date = date,
        TotalKeys = keys,
        TotalMouseClicks = clicks,
        ActiveMinutes = active,
        WebMinutes = webMin,
        WebPages = webPages,
        CpuLoadAvg = cpuAvg,
        CpuLoadP95 = cpuP95,
        GpuLoadAvg = gpuAvg,
        GpuTempMax = gpuTemp,
        MemLoadAvg = mem,
        NetBytesUp = netUp,
        NetBytesDown = netDown,
        SearchCount = search,
        LastUpdated = new DateTime(2026, 11, 18)
    };

    private static HardwareSample Hw(string ts, double cpu, double gpu, double temp, double mem) => new()
    {
        Timestamp = ts,
        CpuLoadAvg = cpu,
        GpuLoadAvg = gpu,
        GpuTempMax = temp,
        MemLoadAvg = mem,
        SampleCount = 1
    };

    private static WebSession Web(DateTime start, int durationMin, int activeMin, string category) => new()
    {
        Id = Guid.NewGuid(),
        StartTime = start,
        EndTime = start.AddMinutes(durationMin),
        Duration = TimeSpan.FromMinutes(durationMin),
        ActiveDuration = TimeSpan.FromMinutes(activeMin),
        Url = "https://example.com",
        Title = "example",
        Domain = "example.com",
        Browser = "Edge",
        CategoryName = category,
        IsLegacy = false
    };

    private static ProcessNetworkUsage Net(string date, int hour, long down) => new()
    {
        Date = date,
        Hour = hour,
        ProcessName = "msedge",
        BytesUp = 0,
        BytesDown = down
    };

    private sealed class Factory(DbContextOptions<PChabitDbContext> options) : IDbContextFactory<PChabitDbContext>
    {
        public PChabitDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task Rust分析页_应与CSharp引擎逐项一致()
    {
        var exe = ResolveDatad();
        if (exe == null) Assert.Fail("找不到 pchabit-datad.exe，Parity 用例必须真跑 Rust 路径");

        // 先跑 C# 引擎（关掉 Rust 开关）
        Environment.SetEnvironmentVariable("PCHABIT_RUST_ANALYTICS", "0");
        var csharp = await AnalyticsEngine.BuildAsync(new Factory(_options), Period);

        // 再跑 Rust 路径（指向同一个临时库）
        Environment.SetEnvironmentVariable("PCHABIT_RUST_ANALYTICS", null);
        Environment.SetEnvironmentVariable("PCHABIT_DATAD_PATH", exe);
        Environment.SetEnvironmentVariable("PCHABIT_DATAD_DB", _dbPath);
        var rust = await RustAnalyticsProvider.TryBuildAsync(Period);
        rust.Should().NotBeNull("Rust 路径不可用时必须让它失败，而不是静默回退");

        // KPI：id / 展示值 / 环比文案 / 方向 全部对齐
        rust!.Kpis.Select(k => k.Id).Should().BeEquivalentTo(csharp.Kpis.Select(k => k.Id));
        foreach (var expected in csharp.Kpis)
        {
            var actual = rust.Kpis.Single(k => k.Id == expected.Id);
            actual.Value.Should().Be(expected.Value, $"KPI {expected.Id} 展示值");
            actual.DeltaText.Should().Be(expected.DeltaText, $"KPI {expected.Id} 环比文案");
            actual.DeltaDirection.Should().Be(expected.DeltaDirection, $"KPI {expected.Id} 环比方向");
            actual.Hint.Should().Be(expected.Hint, $"KPI {expected.Id} 说明");
        }

        // 分类构成
        rust.CategoryShares.Select(c => (c.Category, c.Minutes, c.Percentage)).Should()
            .BeEquivalentTo(csharp.CategoryShares.Select(c => (c.Category, c.Minutes, c.Percentage)));

        // Top 变化
        rust.TopChanges.Select(t => (t.Name, t.CurrentMinutes, t.PreviousMinutes, t.DeltaText)).Should()
            .BeEquivalentTo(csharp.TopChanges.Select(t => (t.Name, t.CurrentMinutes, t.PreviousMinutes, t.DeltaText)));

        // 小时热力
        foreach (var expected in csharp.HourHeat)
        {
            var actual = rust.HourHeat.Single(h => h.Hour == expected.Hour);
            actual.Minutes.Should().Be(expected.Minutes, $"小时 {expected.Hour} 分钟");
            actual.IntensityLevel.Should().Be(expected.IntensityLevel, $"小时 {expected.Hour} 强度");
            actual.Label.Should().Be(expected.Label, $"小时 {expected.Hour} 标签");
        }

        // 日历（14 天）
        rust.CalendarDays.Should().HaveCount(csharp.CalendarDays.Count);
        foreach (var expected in csharp.CalendarDays)
        {
            var actual = rust.CalendarDays.Single(d => d.Date == expected.Date);
            actual.Hours.Should().Be(expected.Hours, $"{expected.Date:yyyy-MM-dd} 小时");
            actual.IntensityLevel.Should().Be(expected.IntensityLevel, $"{expected.Date:yyyy-MM-dd} 强度");
            actual.DayLabel.Should().Be(expected.DayLabel);
            actual.DateLabel.Should().Be(expected.DateLabel);
            actual.InPeriod.Should().Be(expected.InPeriod);
        }

        // 洞察：id 集合 + 文案
        rust.Insights.Select(i => i.Id).Should().BeEquivalentTo(csharp.Insights.Select(i => i.Id));
        foreach (var expected in csharp.Insights)
        {
            var actual = rust.Insights.Single(i => i.Id == expected.Id);
            actual.Severity.Should().Be(expected.Severity);
            actual.Message.Should().Be(expected.Message, $"洞察 {expected.Id} 文案");
            actual.ActionKey.Should().Be(expected.ActionKey);
        }

        // 多源指标
        (rust.Extra is null).Should().Be(csharp.Extra is null);
        if (csharp.Extra is { } ce)
        {
            var re = rust.Extra!;
            re.TotalKeys.Should().Be(ce.TotalKeys);
            re.TotalClicks.Should().Be(ce.TotalClicks);
            re.KeysPerActiveHour.Should().BeApproximately(ce.KeysPerActiveHour, 1e-9);
            re.WebMinutes.Should().BeApproximately(ce.WebMinutes, 1e-9);
            re.WebPages.Should().Be(ce.WebPages);
            re.WebSharePct.Should().BeApproximately(ce.WebSharePct, 1e-9);
            re.CpuLoadAvg.Should().Be(ce.CpuLoadAvg);
            re.CpuLoadP95.Should().Be(ce.CpuLoadP95);
            re.GpuLoadAvg.Should().Be(ce.GpuLoadAvg);
            re.GpuTempMax.Should().Be(ce.GpuTempMax);
            re.MemLoadAvg.Should().Be(ce.MemLoadAvg);
            re.NetBytesUp.Should().Be(ce.NetBytesUp);
            re.NetBytesDown.Should().Be(ce.NetBytesDown);
            re.SearchCount.Should().Be(ce.SearchCount);
        }

        // 硬件画像
        (rust.Hardware is null).Should().Be(csharp.Hardware is null);
        if (csharp.Hardware is { } ch)
        {
            var rh = rust.Hardware!;
            rh.SampleMinutes.Should().Be(ch.SampleMinutes);
            rh.SampleDays.Should().Be(ch.SampleDays);
            rh.CpuLoadAvg.Should().Be(ch.CpuLoadAvg);
            rh.CpuLoadP95.Should().Be(ch.CpuLoadP95);
            rh.GpuLoadAvg.Should().Be(ch.GpuLoadAvg);
            rh.GpuTempMax.Should().Be(ch.GpuTempMax);
            rh.AppLoads.Select(a => (a.ProcessName, a.OverlapMinutes, a.CpuLoadAvg, a.GpuLoadAvg, a.SampleCount)).Should()
                .BeEquivalentTo(ch.AppLoads.Select(a => (a.ProcessName, a.OverlapMinutes, a.CpuLoadAvg, a.GpuLoadAvg, a.SampleCount)));
            rh.Hours.Select(h => (h.Hour, h.CpuLoadAvg, h.GpuLoadAvg, h.SampleCount)).Should()
                .BeEquivalentTo(ch.Hours.Select(h => (h.Hour, h.CpuLoadAvg, h.GpuLoadAvg, h.SampleCount)));
        }

        // 时段画像
        rust.HourWebMinutes.Should().BeEquivalentTo(csharp.HourWebMinutes);
        rust.HourNetDownMb.Should().BeEquivalentTo(csharp.HourNetDownMb);
        rust.WebCategoryShares.Select(c => (c.Category, c.Minutes)).Should()
            .BeEquivalentTo(csharp.WebCategoryShares.Select(c => (c.Category, c.Minutes)));

        rust.DayCountWithData.Should().Be(csharp.DayCountWithData);
        rust.HasEnoughData.Should().Be(csharp.HasEnoughData);
    }

    private static string? ResolveDatad()
    {
        var env = Environment.GetEnvironmentVariable("PCHABIT_DATAD_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 7 && dir != null; i++, dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "rust", "pchabit-datad", "target", "release", "pchabit-datad.exe");
            if (File.Exists(c)) return c;
        }
        return null;
    }
}
