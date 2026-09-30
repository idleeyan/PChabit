using PChabit.Core.ValueObjects;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// 习惯养成轨迹：连续稳定 / 正在漂移的习惯条目（供 AI 长期叙事）。
/// 基于滚动窗口标签分钟与夜间/专注，不碰原文。
/// </summary>
public static class HabitTrajectoryBuilder
{
    public sealed record HabitTrend(
        string Name,
        string Direction, // stable | better | worse
        double Strength,  // 0-1：稳定度或效应强度
        int SupportWeeks,
        string Detail);

    public static List<HabitTrend> Build(IReadOnlyList<HabitDay> days)
    {
        var trends = new List<HabitTrend>();
        if (days.Count < 14) return trends;

        var ordered = days.OrderBy(d => d.Date).ToList();
        var mid = ordered.Count / 2;
        var first = ordered.Take(mid).ToList();
        var second = ordered.Skip(mid).ToList();

        static double Avg(IEnumerable<HabitDay> ds, Func<HabitDay, double> sel)
        {
            var list = ds.Select(sel).Where(x => x > 0 || true).ToList();
            return list.Count == 0 ? 0 : list.Average();
        }

        // 夜间：越低越好
        var night1 = Avg(first, d => d.NightMinutes);
        var night2 = Avg(second, d => d.NightMinutes);
        if (night1 + night2 > 10)
        {
            var deltaPct = night1 > 0 ? (night2 - night1) / night1 * 100 : 0;
            trends.Add(new HabitTrend(
                "夜间使用",
                deltaPct > 15 ? "worse" : deltaPct < -15 ? "better" : "stable",
                Math.Min(1, Math.Abs(deltaPct) / 100),
                CountSupportWeeks(ordered, d => d.NightMinutes > 30),
                $"前半均 {night1:F0} 分 → 后半均 {night2:F0} 分"));
        }

        // 专注：越高越好
        var focus1 = Avg(first, d => d.FocusMinutes ?? 0);
        var focus2 = Avg(second, d => d.FocusMinutes ?? 0);
        if (focus1 + focus2 > 10)
        {
            var deltaPct = focus1 > 0 ? (focus2 - focus1) / focus1 * 100 : 0;
            trends.Add(new HabitTrend(
                "专注时长",
                deltaPct > 15 ? "better" : deltaPct < -15 ? "worse" : "stable",
                Math.Min(1, Math.Abs(deltaPct) / 100),
                CountSupportWeeks(ordered, d => (d.FocusMinutes ?? 0) >= 30),
                $"前半均 {focus1:F0} 分 → 后半均 {focus2:F0} 分"));
        }

        // 标签占比趋势
        foreach (var label in new[] { ActivityLabels.WorkCode, ActivityLabels.BrowseFun, ActivityLabels.Comms })
        {
            double Share(IEnumerable<HabitDay> ds)
            {
                double sumLabel = 0, sumAll = 0;
                foreach (var d in ds)
                {
                    if (d.LabelMinutes is null) continue;
                    foreach (var kv in d.LabelMinutes)
                    {
                        sumAll += kv.Value;
                        if (kv.Key == label) sumLabel += kv.Value;
                    }
                }
                return sumAll > 0 ? sumLabel / sumAll * 100 : 0;
            }

            var s1 = Share(first);
            var s2 = Share(second);
            if (s1 + s2 < 1) continue;
            var delta = s2 - s1;
            // work-code 越高越好；browse-fun 越高越差；comms 中性
            var betterWhenUp = label == ActivityLabels.WorkCode;
            var dir = Math.Abs(delta) < 3 ? "stable"
                : ((delta > 0) == betterWhenUp ? "better" : "worse");
            var name = label switch
            {
                ActivityLabels.WorkCode => "写代码占比",
                ActivityLabels.BrowseFun => "娱乐浏览占比",
                ActivityLabels.Comms => "通讯占比",
                _ => label
            };
            trends.Add(new HabitTrend(
                name, dir, Math.Min(1, Math.Abs(delta) / 20),
                CountSupportWeeks(ordered, d => d.LabelMinutes != null
                    && d.LabelMinutes.TryGetValue(label, out var m) && m >= 20),
                $"前半 {s1:F0}% → 后半 {s2:F0}%"));
        }

        return trends;
    }

    /// <summary>连续满足条件的「周」数（按 7 日窗计）。</summary>
    private static int CountSupportWeeks(List<HabitDay> days, Func<HabitDay, bool> predicate)
    {
        if (days.Count == 0) return 0;
        var weeks = 0;
        var start = days[0].Date;
        for (var w = 0; w < 8; w++)
        {
            var from = start.AddDays(w * 7);
            var to = from.AddDays(7);
            var window = days.Where(d => d.Date >= from && d.Date < to).ToList();
            if (window.Count == 0) continue;
            if (window.Count(predicate) >= Math.Max(1, window.Count / 2))
                weeks++;
            else if (weeks > 0) break;
        }
        return weeks;
    }

    public static List<Dictionary<string, object>> ToAiList(IEnumerable<HabitTrend> trends)
        => trends.Select(t => new Dictionary<string, object>
        {
            ["name"] = t.Name,
            ["direction"] = t.Direction,
            ["strength"] = Math.Round(t.Strength, 2),
            ["supportWeeks"] = t.SupportWeeks,
            ["detail"] = t.Detail
        }).ToList();
}
