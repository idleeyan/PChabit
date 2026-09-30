namespace PChabit.Core.ValueObjects;

/// <summary>画像/基线用的单日聚合（来源 DailySummary + 会话回填，不含原文）。</summary>
public sealed record HabitDay(
    DateTime Date,
    double ActiveMinutes,
    double NightMinutes,
    string? FirstActiveTime,
    string? LastActiveTime,
    double? FocusMinutes,
    double? FocusCount,
    int? AppSwitches,
    double? WebSharePct,
    IReadOnlyDictionary<string, double>? LabelMinutes)
{
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public string DayType => IsWeekend ? "weekend" : "weekday";
}

/// <summary>长期习惯画像：「你是谁」而非「本周多少」。</summary>
public sealed record HabitProfile(
    string Chronotype,           // early | balanced | night-leaning | unknown
    double? TypicalStartHour,    // 小时（可带小数）
    double? TypicalEndHour,
    double? PeakDeepHour,        // 专注最集中小时（0-23）
    double MedianActiveMinutes,  // 工作日中位活跃
    double MedianFocusMinutes,
    double MedianNightMinutes,
    double MedianSwitchPerHour,
    double? ProductiveSharePct,
    int SampleDays,
    bool Mature);                // 样本是否足够（≥14 有数据日）

/// <summary>个人基线：相对「你自己的正常」。</summary>
public sealed record PersonalBaseline(
    BaselineStat ActiveMinutesWeekday,
    BaselineStat ActiveMinutesWeekend,
    BaselineStat FocusMinutesWeekday,
    BaselineStat NightMinutesWeekday,
    BaselineStat SwitchPerHourWeekday,
    int SampleDays,
    bool Mature);

public sealed record BaselineStat(
    double P50,
    double P75,
    double P90,
    double Min,
    double Max,
    int Count)
{
    /// <summary>相对基线的偏离：负数=低于你的正常。样本不足返回 null。</summary>
    public double? DeviationPct(double value)
    {
        if (Count < 3 || P50 == 0) return null;
        return Math.Round((value - P50) / Math.Abs(P50) * 100, 1);
    }

    /// <summary>是否高于个人 P90（你的「偏高」）。</summary>
    public bool IsElevated(double value) => Count >= 3 && value >= P90;

    /// <summary>是否低于个人 P25 等价（P50 下方更远，用 P50- (P75-P50) 近似）。</summary>
    public bool IsLow(double value) => Count >= 3 && value < P50 - Math.Max(1e-6, (P75 - P50));
}
