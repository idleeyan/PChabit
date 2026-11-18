using FluentAssertions;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

/// <summary>
/// P3 第二块回归：C# → pchabit-datad `detail` 链路。
/// 依赖本机真实库；找不到 exe 时允许跳过，但已构建时必须拿到报告。
/// </summary>
public class RustDetailProviderTests
{
    private static AnalyticsPeriod Today() => new(DateTime.Today, DateTime.Today.AddDays(1), "今日");

    [Fact]
    public async Task 应用详情_结构应与引擎一致()
    {
        if (Environment.GetEnvironmentVariable("PCHABIT_RUST_DETAIL")?.Trim() == "0")
            return;

        var period = Today();

        // 用统计报告里时长最高的进程做样本，避免硬编码某个进程今天没数据
        var stats = await RustStatsProvider.TryBuildAsync(period, null, null);
        var process = stats?.Rows.FirstOrDefault()?.ProcessName;
        if (string.IsNullOrEmpty(process)) return;

        var report = await RustDetailProvider.TryBuildAsync(
            period, process!, process!, stats!.Rows[0].Category, stats.Rows[0].Category);

        if (report == null)
        {
            DatadExeExists().Should().BeFalse("已构建 pchabit-datad.exe 时不允许静默回退");
            return;
        }

        report.Period.Should().Be(period);
        report.ProcessName.Should().Be(process);
        report.Hourly.Should().HaveCount(24);
        report.DailyTrend.Should().HaveCount(1, "「今日」周期只有一天");
        report.Sessions.Should().BeGreaterThan(0);
        report.TotalMinutes.Should().BeGreaterThan(0);
        report.HasData.Should().BeTrue();
        report.LongestSessionMinutes.Should().BeGreaterThanOrEqualTo(report.AvgMinutes - 0.01);
        report.Hourly.Sum(h => h.Minutes).Should()
            .BeApproximately(report.TotalMinutes, 0.5, "小时分布之和应等于总时长");
        report.Hourly.Select(h => h.Label).Should().StartWith("00").And.EndWith("23");
        report.DailyTrend[0].IntensityLevel.Should().BeInRange(0, 4);
    }

    private static bool DatadExeExists()
    {
        var name = "pchabit-datad.exe";
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(AppContext.BaseDirectory, "rust", name)
        };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "rust", "pchabit-datad", "target", "release", name));

        return candidates.Any(File.Exists);
    }
}
