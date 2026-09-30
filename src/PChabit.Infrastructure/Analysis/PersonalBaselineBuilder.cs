using PChabit.Core.ValueObjects;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// 个人基线：按日类型（工作日/周末）维护核心指标分布，供「相对你自己」的偏离判断。
/// 样本 &lt; 3 的统计量 Count=0，Mature 要求工作日有效天 ≥ 10。
/// </summary>
public static class PersonalBaselineBuilder
{
    public const int MinMatureWeekdays = 10;

    public static PersonalBaseline Build(IReadOnlyList<HabitDay> days)
    {
        var valid = days
            .Where(d => d.ActiveMinutes >= 5 || (d.FocusMinutes ?? 0) >= 5)
            .ToList();

        var weekday = valid.Where(d => !d.IsWeekend).ToList();
        var weekend = valid.Where(d => d.IsWeekend).ToList();

        static BaselineStat Stat(IEnumerable<double> values)
        {
            var arr = values.ToList();
            if (arr.Count == 0)
                return new BaselineStat(0, 0, 0, 0, 0, 0);
            return new BaselineStat(
                P50: Math.Round(HabitProfileBuilder.Percentile(arr, 0.5), 2),
                P75: Math.Round(HabitProfileBuilder.Percentile(arr, 0.75), 2),
                P90: Math.Round(HabitProfileBuilder.Percentile(arr, 0.9), 2),
                Min: Math.Round(arr.Min(), 2),
                Max: Math.Round(arr.Max(), 2),
                Count: arr.Count);
        }

        static IEnumerable<double> SwitchPerHour(IEnumerable<HabitDay> days)
        {
            foreach (var d in days)
            {
                if (d.ActiveMinutes <= 0 || d.AppSwitches is not { } sw) continue;
                yield return Math.Round(sw / (d.ActiveMinutes / 60.0), 2);
            }
        }

        return new PersonalBaseline(
            ActiveMinutesWeekday: Stat(weekday.Select(d => d.ActiveMinutes)),
            ActiveMinutesWeekend: Stat(weekend.Select(d => d.ActiveMinutes)),
            FocusMinutesWeekday: Stat(weekday.Select(d => d.FocusMinutes ?? 0).Where(x => x > 0)),
            NightMinutesWeekday: Stat(weekday.Select(d => d.NightMinutes)),
            SwitchPerHourWeekday: Stat(SwitchPerHour(weekday)),
            SampleDays: valid.Count,
            Mature: weekday.Count >= MinMatureWeekdays);
    }

    /// <summary>把基线压成 AI 可读摘要（无原文）。</summary>
    public static Dictionary<string, object> ToAiSummary(PersonalBaseline b)
    {
        static Dictionary<string, object> S(BaselineStat s) => new()
        {
            ["p50"] = s.P50,
            ["p90"] = s.P90,
            ["count"] = s.Count
        };

        return new Dictionary<string, object>
        {
            ["mature"] = b.Mature,
            ["sampleDays"] = b.SampleDays,
            ["activeMinutesWeekday"] = S(b.ActiveMinutesWeekday),
            ["focusMinutesWeekday"] = S(b.FocusMinutesWeekday),
            ["nightMinutesWeekday"] = S(b.NightMinutesWeekday),
            ["switchPerHourWeekday"] = S(b.SwitchPerHourWeekday),
            ["note"] = b.Mature
                ? "偏离请相对 p50/p90 解读，不要用绝对好坏评判"
                : "基线样本不足，只做描述，不下「异常」结论"
        };
    }

    /// <summary>本周相对基线的关键偏离（供 ruleInsights / AI evidence）。</summary>
    public static List<AiDeviation> ComputeDeviations(
        PersonalBaseline baseline,
        double activeMinutes,
        double focusMinutes,
        double nightMinutes,
        double switchPerHour,
        bool isWeekendDay = false)
    {
        var list = new List<AiDeviation>();

        void Add(string id, BaselineStat stat, double value, string name)
        {
            var dev = stat.DeviationPct(value);
            if (dev is null && stat.Count < 3) return;
            list.Add(new AiDeviation
            {
                MetricId = id,
                Name = name,
                Value = Math.Round(value, 2),
                BaselineP50 = stat.P50,
                DeviationPct = dev,
                Elevated = stat.IsElevated(value),
                Low = stat.IsLow(value)
            });
        }

        var activeStat = isWeekendDay ? baseline.ActiveMinutesWeekend : baseline.ActiveMinutesWeekday;
        Add("activeMinutes", activeStat, activeMinutes, "活跃时长");
        Add("focusMinutes", baseline.FocusMinutesWeekday, focusMinutes, "专注时长");
        Add("nightMinutes", baseline.NightMinutesWeekday, nightMinutes, "夜间时长");
        Add("switchPerHour", baseline.SwitchPerHourWeekday, switchPerHour, "每小时切换");

        return list;
    }
}

/// <summary>相对个人基线的偏离项。</summary>
public sealed class AiDeviation
{
    public string MetricId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public double BaselineP50 { get; set; }
    public double? DeviationPct { get; set; }
    public bool Elevated { get; set; }
    public bool Low { get; set; }
}
