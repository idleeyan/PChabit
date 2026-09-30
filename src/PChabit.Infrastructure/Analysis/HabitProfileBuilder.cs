using PChabit.Core.ValueObjects;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// 习惯画像构建（纯函数）：滚动窗口内的 HabitDay → 你是谁（节奏/夜型/专注中位数）。
/// </summary>
public static class HabitProfileBuilder
{
    public const int MinMatureDays = 14;

    public static HabitProfile Build(IReadOnlyList<HabitDay> days)
    {
        var valid = days
            .Where(d => d.ActiveMinutes >= 5 || (d.FocusMinutes ?? 0) >= 5)
            .OrderBy(d => d.Date)
            .ToList();

        var weekday = valid.Where(d => !d.IsWeekend).ToList();
        var sampleDays = valid.Count;

        if (sampleDays == 0)
        {
            return new HabitProfile("unknown", null, null, null, 0, 0, 0, 0, null, 0, false);
        }

        var starts = ParseHours(valid.Select(d => d.FirstActiveTime).Where(x => x != null)!).ToList();
        var ends = ParseHours(valid.Select(d => d.LastActiveTime).Where(x => x != null)!).ToList();

        // 夜型判定：夜间中位数 / 典型结束时刻
        var medianNight = Median(valid.Select(d => d.NightMinutes));
        double? typicalEnd = ends.Count > 0 ? CircularMedian(ends) : null;
        double? typicalStart = starts.Count > 0 ? CircularMedian(starts) : null;

        string chronotype;
        if (sampleDays < 5)
            chronotype = "unknown";
        else if (medianNight >= 45 || (typicalEnd is { } e && e >= 24.5)) // ≥ 00:30
            chronotype = "night-leaning";
        else if (medianNight <= 15 && (typicalEnd is { } e2 && e2 <= 22.5) && (typicalStart is { } s && s <= 9.0))
            chronotype = "early";
        else
            chronotype = "balanced";

        // 峰值专注小时：按结束小时近似（会话结束落在该小时）
        double? peakDeep = null;
        var endHours = ends.Select(h => (int)Math.Floor(h % 24)).ToList();
        if (endHours.Count >= 3)
        {
            peakDeep = endHours
                .GroupBy(h => h)
                .OrderByDescending(g => g.Count())
                .Select(g => (double?)g.Key)
                .First();
        }

        var medianActive = Median((weekday.Count > 0 ? weekday : valid).Select(d => d.ActiveMinutes));
        var medianFocus = Median(valid.Select(d => d.FocusMinutes ?? 0).Where(x => x > 0));
        var switchesPerHour = valid
            .Where(d => d.ActiveMinutes > 0 && d.AppSwitches.HasValue)
            .Select(d => d.AppSwitches!.Value / (d.ActiveMinutes / 60.0))
            .ToList();
        var medianSwitch = Median(switchesPerHour);

        double? prodShare = null;
        var labelDays = valid.Where(d => d.LabelMinutes is { Count: > 0 }).ToList();
        if (labelDays.Count > 0)
        {
            var totalWork = 0.0;
            var totalAll = 0.0;
            foreach (var d in labelDays)
            {
                foreach (var kv in d.LabelMinutes!)
                {
                    totalAll += kv.Value;
                    if (kv.Key is ActivityLabels.WorkCode or ActivityLabels.WorkDoc or ActivityLabels.Meeting)
                        totalWork += kv.Value;
                }
            }
            if (totalAll > 0)
                prodShare = Math.Round(totalWork / totalAll * 100, 1);
        }

        return new HabitProfile(
            chronotype,
            typicalStart is { } ts ? Math.Round(ts, 1) : null,
            typicalEnd is { } te ? Math.Round(te, 1) : null,
            peakDeep,
            Math.Round(medianActive, 1),
            Math.Round(medianFocus, 1),
            Math.Round(medianNight, 1),
            Math.Round(medianSwitch, 1),
            prodShare,
            sampleDays,
            sampleDays >= MinMatureDays);
    }

    /// <summary>字符串 "HH:mm" → 小时（可 >24 表示次日凌晨，如 01:00 → 25）。</summary>
    public static IEnumerable<double> ParseHours(IEnumerable<string> times)
    {
        foreach (var t in times)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var parts = t.Split(':');
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[0], out var h)) continue;
            if (!int.TryParse(parts[1], out var m)) continue;
            // 若该时刻相对典型作息是凌晨，保留为 24+h 便于中位数（由调用方按窗决定）
            yield return h + m / 60.0;
        }
    }

    /// <summary>循环小时中位数：处理 23:30 与 00:30 接近的情形。</summary>
    public static double CircularMedian(IReadOnlyList<double> hours)
    {
        if (hours.Count == 0) return 0;
        // 映射到圆心在 12 点的坐标，避免午夜断裂
        var mapped = hours.Select(h => ((h + 12) % 24) + 24 * (h >= 12 ? 0 : 1)).ToList();
        // 简化：把 <5 的时刻 +24，使凌晨与深夜连续
        var shifted = hours.Select(h => h < 5 ? h + 24 : h).OrderBy(x => x).ToList();
        return Median(shifted);
    }

    public static double Median(IEnumerable<double> values)
    {
        var list = values.OrderBy(x => x).ToList();
        if (list.Count == 0) return 0;
        var mid = list.Count / 2;
        return list.Count % 2 == 1 ? list[mid] : (list[mid - 1] + list[mid]) / 2.0;
    }

    public static double Percentile(IEnumerable<double> values, double p)
    {
        var list = values.OrderBy(x => x).ToList();
        if (list.Count == 0) return 0;
        if (list.Count == 1) return list[0];
        var idx = (list.Count - 1) * p;
        var lo = (int)Math.Floor(idx);
        var hi = (int)Math.Ceiling(idx);
        if (lo == hi) return list[lo];
        return list[lo] + (list[hi] - list[lo]) * (idx - lo);
    }
}
