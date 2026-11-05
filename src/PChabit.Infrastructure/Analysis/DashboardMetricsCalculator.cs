using PChabit.Core.Entities;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// 仪表盘纯计算（3.69.0 从 <c>DashboardViewModel</c> 提取，便于单测）。
/// 全部为静态纯函数，不依赖数据库与 UI 类型；行为与原实现逐字一致。
/// </summary>
public static class DashboardMetricsCalculator
{
    /// <summary>环比百分比。基数为 0 时返回 null（不显示箭头，避免 +∞% 的误导）。</summary>
    public static double? ComparePct(double yesterday, double today)
    {
        if (yesterday <= 0) return null;
        return Math.Round((today - yesterday) / yesterday * 100, 1);
    }

    /// <summary>"1,234" → 1234。统计文本带千分位与中文单位，此处只取纯数字部分。</summary>
    public static double ParseNumber(string formatted)
    {
        var digits = new string(formatted.Where(char.IsDigit).ToArray());
        return double.TryParse(digits, out var v) ? v : 0;
    }

    /// <summary>会话有效时长（分钟）：优先 ActiveDuration；未结束会话仅今日按 clock 截断计入。</summary>
    public static double SessionActiveMinutes(AppSession s, DateTime clock)
    {
        if (s.ActiveDuration > TimeSpan.Zero)
            return Math.Max(0, s.ActiveDuration.TotalMinutes);
        return Math.Max(0, (SessionEnd(s, clock) - s.StartTime).TotalMinutes);
    }

    public static DateTime SessionEnd(AppSession s, DateTime clock)
    {
        if (s.EndTime.HasValue) return s.EndTime.Value;
        // 未结束：仅今日累计到当前时刻；历史日视为未闭合，不计
        if (clock > s.StartTime && s.StartTime.Date == DateTime.Today)
            return clock;
        return s.StartTime;
    }

    public static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue) return "0m";

        var totalMinutes = (int)duration.Value.TotalMinutes;
        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"{hours}h {minutes}m";
    }
}
