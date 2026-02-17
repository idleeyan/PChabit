namespace PChabit.Core.ValueObjects;

public class TimeSlot
{
    public int Hour { get; set; }
    public int Minute { get; set; }
    public double ActivityScore { get; set; }
    public int SessionCount { get; set; }
}

public class WorkSession
{
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public TimeSpan Duration => EndTime.HasValue && StartTime.HasValue 
        ? EndTime.Value - StartTime.Value 
        : TimeSpan.Zero;
    public int SessionCount { get; set; }
}

public class PatternInsight
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public Dictionary<string, object> Data { get; set; } = new();
}

public class EfficiencyBreakdown
{
    public double FocusScore { get; set; }
    public double TaskCompletionScore { get; set; }
    public double BalanceScore { get; set; }
    public double InterruptionScore { get; set; }
    public double GoalScore { get; set; }
    public double TotalScore { get; set; }
}

public class FocusBlockInfo
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public string? PrimaryApplication { get; set; }
    public string? Category { get; set; }
    public int SwitchCount { get; set; }
    public bool IsDeepWork => Duration.TotalMinutes >= 25 && SwitchCount <= 2;
}
