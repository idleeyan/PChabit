using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using Xunit;

namespace PChabit.Tests;

/// <summary>
/// 仪表盘纯计算（3.69.0 从 DashboardViewModel 提取）单元测试。
/// 覆盖：环比基数为 0、文本解析、会话时长三种情形（ActiveDuration / 已结束 / 未结束未闭合）、
/// 时长格式化边界。
/// </summary>
public class DashboardMetricsCalculatorTests
{
    // ---------- ComparePct ----------

    [Fact]
    public void ComparePct_ZeroBase_ReturnsNull()
    {
        Assert.Null(DashboardMetricsCalculator.ComparePct(0, 42));
    }

    [Fact]
    public void ComparePct_NegativeBase_ReturnsNull()
    {
        Assert.Null(DashboardMetricsCalculator.ComparePct(-5, 10));
    }

    [Theory]
    [InlineData(100, 120, 20.0)]
    [InlineData(100, 80, -20.0)]
    [InlineData(3, 2, -33.3)]      // 四舍五入到 1 位小数
    public void ComparePct_Normal_RoundsToOneDecimal(double y, double t, double expected)
    {
        Assert.Equal(expected, DashboardMetricsCalculator.ComparePct(y, t));
    }

    // ---------- ParseNumber ----------

    [Theory]
    [InlineData("1,234", 1234)]
    [InlineData("12.5 万次", 125)]     // 仅取数字字符：1,2,5 -> 125
    [InlineData("无数据", 0)]
    [InlineData("", 0)]
    public void ParseNumber_ExtractsDigitsOnly(string input, double expected)
    {
        Assert.Equal(expected, DashboardMetricsCalculator.ParseNumber(input));
    }

    // ---------- SessionActiveMinutes ----------

    [Fact]
    public void SessionActiveMinutes_UsesActiveDuration_WhenPresent()
    {
        var s = new AppSession
        {
            StartTime = new DateTime(2026, 1, 1, 9, 0, 0),
            EndTime = new DateTime(2026, 1, 1, 10, 0, 0),
            ActiveDuration = TimeSpan.FromMinutes(30)
        };
        Assert.Equal(30.0, DashboardMetricsCalculator.SessionActiveMinutes(s, new DateTime(2026, 1, 1, 12, 0, 0)));
    }

    [Fact]
    public void SessionActiveMinutes_FallsBackToEndMinusStart_WhenNoActiveDuration()
    {
        var s = new AppSession
        {
            StartTime = new DateTime(2026, 1, 1, 9, 0, 0),
            EndTime = new DateTime(2026, 1, 1, 10, 15, 0),
            ActiveDuration = TimeSpan.Zero
        };
        Assert.Equal(75.0, DashboardMetricsCalculator.SessionActiveMinutes(s, new DateTime(2026, 1, 1, 12, 0, 0)));
    }

    [Fact]
    public void SessionActiveMinutes_UnclosedHistoricalSession_CountsZero()
    {
        var s = new AppSession
        {
            StartTime = new DateTime(2026, 1, 1, 9, 0, 0),
            EndTime = null,
            ActiveDuration = TimeSpan.Zero
        };
        // 历史日未闭合 → SessionEnd 返回 StartTime → 0 分钟
        Assert.Equal(0.0, DashboardMetricsCalculator.SessionActiveMinutes(s, DateTime.Now));
    }

    // ---------- SessionEnd ----------

    [Fact]
    public void SessionEnd_ReturnsEndTime_WhenPresent()
    {
        var s = new AppSession
        {
            StartTime = new DateTime(2026, 1, 1, 9, 0, 0),
            EndTime = new DateTime(2026, 1, 1, 9, 30, 0)
        };
        Assert.Equal(new DateTime(2026, 1, 1, 9, 30, 0), DashboardMetricsCalculator.SessionEnd(s, DateTime.Now));
    }

    [Fact]
    public void SessionEnd_UnclosedTodaySession_ReturnsClock()
    {
        var clock = DateTime.Today.AddHours(15);
        var s = new AppSession
        {
            StartTime = DateTime.Today.AddHours(9),
            EndTime = null
        };
        Assert.Equal(clock, DashboardMetricsCalculator.SessionEnd(s, clock));
    }

    [Fact]
    public void SessionEnd_UnclosedHistoricalSession_ReturnsStartTime()
    {
        var start = DateTime.Today.AddDays(-3).AddHours(9);
        var s = new AppSession { StartTime = start, EndTime = null };
        Assert.Equal(start, DashboardMetricsCalculator.SessionEnd(s, DateTime.Now));
    }

    // ---------- FormatDuration ----------

    [Fact]
    public void FormatDuration_Null_ReturnsZeroMinutes()
    {
        Assert.Equal("0m", DashboardMetricsCalculator.FormatDuration(null));
    }

    [Fact]
    public void FormatDuration_LessThanHour_ReturnsMinutesOnly()
    {
        Assert.Equal("45m", DashboardMetricsCalculator.FormatDuration(TimeSpan.FromMinutes(45)));
    }

    [Fact]
    public void FormatDuration_OverHour_ReturnsHoursAndMinutes()
    {
        Assert.Equal("1h 30m", DashboardMetricsCalculator.FormatDuration(TimeSpan.FromMinutes(90)));
        Assert.Equal("2h 0m", DashboardMetricsCalculator.FormatDuration(TimeSpan.FromMinutes(120)));
    }
}
