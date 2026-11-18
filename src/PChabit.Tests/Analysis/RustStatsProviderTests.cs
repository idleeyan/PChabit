using FluentAssertions;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests.Analysis;

/// <summary>
/// P3 数据面回归：验证 C# → pchabit-datad 这条链路（找不到 exe 时自动跳过，不误报）。
/// 依赖本机真实库，属于「环境相关」测试，CI 上会因缺 exe 而 Skip。
/// </summary>
public class RustStatsProviderTests
{
    [Fact]
    public async Task RustStatsProvider_应能拿到与引擎同结构的报告()
    {
        if (Environment.GetEnvironmentVariable("PCHABIT_RUST_STATS")?.Trim() == "0")
            return; // 显式关闭时不算失败

        var today = DateTime.Today;
        var period = new AnalyticsPeriod(today, today.AddDays(1), "今日");

        var report = await RustStatsProvider.TryBuildAsync(period, null, null);

        if (report == null)
        {
            // 找不到 pchabit-datad.exe（未构建 Rust）时回退是设计行为；
            // 但本机已经构建过，此时仍拿不到报告说明链路有问题，必须失败。
            RustExeExists().Should().BeFalse("已构建 pchabit-datad.exe 时不允许静默回退");
            return;
        }

        report.Period.Should().Be(period);
        report.TotalMinutes.Should().BeGreaterThanOrEqualTo(0);
        report.Rows.Should().NotBeNull();
        report.Hourly.Should().HaveCount(24);
        report.Rows.Should().OnlyContain(r => !string.IsNullOrEmpty(r.ProcessName));
        report.Rows.Sum(r => r.DurationMinutes).Should()
            .BeApproximately(report.TotalMinutes, 0.5, "排行时长之和应等于总时长（误差来自浮点累加）");
        report.PieSlices.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RustStatsProvider_分类过滤应与引擎口径一致()
    {
        var today = DateTime.Today;
        var period = new AnalyticsPeriod(today, today.AddDays(1), "今日");

        var full = await RustStatsProvider.TryBuildAsync(period, null, null);
        if (full == null) return;

        var category = full.CategoryShares.FirstOrDefault()?.Category;
        if (string.IsNullOrEmpty(category)) return;

        var filtered = await RustStatsProvider.TryBuildAsync(period, category, null);
        filtered.Should().NotBeNull();
        filtered!.Rows.Should().OnlyContain(r => r.Category == category);
        filtered.TotalMinutes.Should().BeLessThanOrEqualTo(full.TotalMinutes + 0.5);
    }

    /// <summary>与 RustStatsProvider.ResolveExe 同样的向上查找规则，用于判断「找不到 exe 是否正常」。</summary>
    private static bool RustExeExists()
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
