using FluentAssertions;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

public class AppStatsEngineTests
{
    private static AppSession S(string process, string? appName, string category, DateTime start, int minutes, bool ended = true) => new()
    {
        ProcessName = process,
        AppName = appName,
        Category = category,
        StartTime = start,
        EndTime = ended ? start.AddMinutes(minutes) : null
    };

    private static Dictionary<string, (string Name, string ColorHex)> DevMap() =>
        AppStatsEngine.BuildCategoryMap(new List<ProgramCategory>
        {
            new()
            {
                Id = 1,
                Name = "开发",
                Color = "#512BD4",
                IsActive = true,
                ProgramMappings = new List<ProgramCategoryMapping>
                {
                    new() { ProcessName = "Code.exe" }
                }
            }
        });

    private static (AppStatsReport Report, List<AppSession> Cur) ComputeWith(
        List<AppSession> cur,
        List<AppSession>? prev = null,
        string? categoryFilter = null,
        string? search = null,
        IReadOnlyDictionary<string, (string Name, string ColorHex)>? map = null)
    {
        var period = new AnalyticsPeriod(new DateTime(2026, 9, 14), new DateTime(2026, 9, 21), "本周");
        var report = AppStatsEngine.Compute(period, period.Previous(), cur, prev ?? new List<AppSession>(), map ?? DevMap(), categoryFilter, search);
        return (report, cur);
    }

    // ==================== 周期扩展 ====================

    [Fact]
    public void Today_Period_IsSingleDay()
    {
        var p = AnalyticsPeriod.FromKind(AnalyticsPeriodKind.Today, new DateTime(2026, 9, 16));
        p.Start.Should().Be(new DateTime(2026, 9, 16));
        p.EndExclusive.Should().Be(new DateTime(2026, 9, 17));
        p.DayCount.Should().Be(1);
    }

    [Fact]
    public void Yesterday_Period_IsPreviousDay()
    {
        var p = AnalyticsPeriod.FromKind(AnalyticsPeriodKind.Yesterday, new DateTime(2026, 9, 16));
        p.Start.Should().Be(new DateTime(2026, 9, 15));
        p.EndExclusive.Should().Be(new DateTime(2026, 9, 16));
    }

    [Fact]
    public void Custom_FromCustom_RespectsBounds()
    {
        var p = AnalyticsPeriod.FromCustom(new DateTime(2026, 9, 1), new DateTime(2026, 9, 11));
        p.Start.Should().Be(new DateTime(2026, 9, 1));
        p.EndExclusive.Should().Be(new DateTime(2026, 9, 11));
        p.DayCount.Should().Be(10);
    }

    [Fact]
    public void Custom_InvalidRange_Throws()
    {
        var act = () => AnalyticsPeriod.FromCustom(new DateTime(2026, 9, 11), new DateTime(2026, 9, 1));
        act.Should().Throw<ArgumentException>();
    }

    // ==================== 归一化与映射 ====================

    [Fact]
    public void NormalizeProcessName_RemovesExeSuffixAndLowercases()
    {
        AppStatsEngine.NormalizeProcessName("Code.EXE").Should().Be("code");
        AppStatsEngine.NormalizeProcessName("chrome").Should().Be("chrome");
        AppStatsEngine.NormalizeProcessName("  VSCode.exe  ").Should().Be("vscode");
    }

    [Fact]
    public void BuildCategoryMap_IncludesExeVariant()
    {
        var map = DevMap();
        map.Should().ContainKey("code");
        map["code"].Name.Should().Be("开发");
        map["code"].ColorHex.Should().Be("#512BD4");
    }

    // ==================== 计算 ====================

    [Fact]
    public void Compute_EmptySessions_ProducesEmptyReport()
    {
        var (report, _) = ComputeWith(new List<AppSession>());
        report.TotalMinutes.Should().Be(0);
        report.ActiveApps.Should().Be(0);
        report.Rows.Should().BeEmpty();
        report.TopAppName.Should().Be("无");
        report.FocusSessions.Should().Be(0);
    }

    [Fact]
    public void Compute_SumsDurationAndActiveApps()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("Code", "Visual Studio Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120),
            S("Code", "Visual Studio Code", "开发", new DateTime(2026, 9, 15, 10, 0, 0), 60),
            S("Chrome", "Google Chrome", "浏览", new DateTime(2026, 9, 14, 14, 0, 0), 30)
        });
        report.TotalMinutes.Should().Be(210);
        report.ActiveApps.Should().Be(2);
        report.Rows.Should().HaveCount(2);
        report.Rows[0].ProcessName.Should().Be("Code");
        report.Rows[0].DurationMinutes.Should().Be(180);
    }

    [Fact]
    public void Compute_TopApp_IsFirstRow()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("Chrome", "Chrome", "浏览", new DateTime(2026, 9, 14, 14, 0, 0), 30),
            S("Code", "VSCode", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120)
        });
        report.TopAppName.Should().Be("VSCode");
        report.TopAppMinutes.Should().Be(120);
    }

    [Fact]
    public void Compute_Percentage_SumsToHundred()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("A", "A", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 90),
            S("B", "B", "浏览", new DateTime(2026, 9, 14, 12, 0, 0), 60),
            S("C", "C", "娱乐", new DateTime(2026, 9, 14, 14, 0, 0), 50)
        });
        report.Rows.Sum(r => r.Percentage).Should().BeApproximately(100, 0.01);
    }

    [Fact]
    public void Compute_Delta_Up_WhenGrew()
    {
        var (report, _) = ComputeWith(
            new List<AppSession> { S("Code", "Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120) },
            new List<AppSession> { S("Code", "Code", "开发", new DateTime(2026, 9, 7, 10, 0, 0), 60) });
        report.Rows.Single().DeltaDirection.Should().Be("up");
        report.Rows.Single().DeltaText.Should().Be("+100%");
    }

    [Fact]
    public void Compute_Delta_New_WhenNoPrevious()
    {
        var (report, _) = ComputeWith(
            new List<AppSession> { S("Code", "Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120) },
            new List<AppSession>());
        report.Rows.Single().DeltaDirection.Should().Be("up");
        report.Rows.Single().DeltaText.Should().Be("新增");
    }

    [Fact]
    public void Compute_Delta_Down_WhenShrunk()
    {
        var (report, _) = ComputeWith(
            new List<AppSession> { S("Code", "Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 30) },
            new List<AppSession> { S("Code", "Code", "开发", new DateTime(2026, 9, 7, 10, 0, 0), 60) });
        report.Rows.Single().DeltaDirection.Should().Be("down");
        report.Rows.Single().DeltaText.Should().Be("-50%");
    }

    [Fact]
    public void Compute_Delta_Flat_WhenTinyChange()
    {
        var (report, _) = ComputeWith(
            new List<AppSession> { S("Code", "Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 61) },
            new List<AppSession> { S("Code", "Code", "开发", new DateTime(2026, 9, 7, 10, 0, 0), 60) });
        report.Rows.Single().DeltaDirection.Should().Be("flat");
        report.Rows.Single().DeltaText.Should().Be("—");
    }

    [Fact]
    public void Compute_EndTimeNull_NotCountedInDuration()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("Code", "Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120),
            S("Chrome", "Chrome", "浏览", new DateTime(2026, 9, 14, 14, 0, 0), 30, ended: false)
        });
        // 时长不计未结束会话；活跃应用数仍计入
        report.TotalMinutes.Should().Be(120);
        report.ActiveApps.Should().Be(2);
    }

    [Fact]
    public void Compute_CategoryFilter_FiltersRows()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("Code", "Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120),
            S("Chrome", "Chrome", "浏览", new DateTime(2026, 9, 14, 14, 0, 0), 60)
        }, categoryFilter: "开发");
        report.Rows.Should().ContainSingle(r => r.ProcessName == "Code");
        report.TotalMinutes.Should().Be(120);
    }

    [Fact]
    public void Compute_SearchFilter_MatchesDisplayName()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("Code", "Visual Studio Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120),
            S("Chrome", "Google Chrome", "浏览", new DateTime(2026, 9, 14, 14, 0, 0), 60)
        }, search: "Studio");
        report.Rows.Should().ContainSingle(r => r.ProcessName == "Code");
    }

    [Fact]
    public void Compute_FocusSessions_CountsProductiveSessionsOver25Min()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("Code", "Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120),
            S("Code", "Code", "开发", new DateTime(2026, 9, 14, 14, 0, 0), 10),
            S("Chrome", "Chrome", "浏览", new DateTime(2026, 9, 14, 15, 0, 0), 60)
        });
        report.FocusSessions.Should().Be(1); // 只有 120 分钟的开发会话算专注段
    }

    [Fact]
    public void Compute_PieSlices_Top8PlusOther()
    {
        var sessions = new List<AppSession>();
        for (var i = 1; i <= 10; i++)
        {
            sessions.Add(S($"App{i}", $"App{i}", "开发", new DateTime(2026, 9, 14, 10 + (i % 5), 0, 0), i * 10));
        }
        var (report, _) = ComputeWith(sessions);
        report.PieSlices.Should().HaveCount(9); // Top 8 + 其他
        report.PieSlices.Last().Name.Should().Be("其他");
    }

    [Fact]
    public void Compute_CategoryShares_Grouped()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("Code", "Code", "开发", new DateTime(2026, 9, 14, 10, 0, 0), 120),
            S("Chrome", "Chrome", "浏览", new DateTime(2026, 9, 14, 14, 0, 0), 60),
            S("Edge", "Edge", "浏览", new DateTime(2026, 9, 14, 16, 0, 0), 20)
        });
        report.CategoryShares.Should().HaveCount(2);
        report.CategoryShares.First().Category.Should().Be("开发");
        report.CategoryShares.First().Minutes.Should().Be(120);
    }

    [Fact]
    public void Compute_Hourly_24Points()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("Code", "Code", "开发", new DateTime(2026, 9, 14, 9, 30, 0), 60),
            S("Chrome", "Chrome", "浏览", new DateTime(2026, 9, 14, 9, 45, 0), 30)
        });
        report.Hourly.Should().HaveCount(24);
        report.Hourly[9].Minutes.Should().Be(90);
        report.Hourly[0].Minutes.Should().Be(0);
    }

    [Fact]
    public void Compute_UnknownCategory_FallsBackToSessionCategory()
    {
        var (report, _) = ComputeWith(new List<AppSession>
        {
            S("UnknownApp", "Unknown", "娱乐", new DateTime(2026, 9, 14, 10, 0, 0), 60)
        });
        report.Rows.Single().Category.Should().Be("娱乐");
        report.Rows.Single().CategoryColorHex.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ResolveDisplayName_FallsBackToProcessName()
    {
        var session = S("MyApp.exe", null, "开发", new DateTime(2026, 9, 14, 10, 0, 0), 60);
        AppStatsEngine.ResolveDisplayName(new[] { session }, "MyApp.exe").Should().Be("MyApp.exe");
    }
}
