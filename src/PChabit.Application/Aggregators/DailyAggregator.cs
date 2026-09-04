namespace PChabit.Application.Aggregators;


public class DailySummary
{
    public DateTime Date { get; set; }
    public TimeSpan TotalActiveTime { get; set; }
    public int TotalKeyPresses { get; set; }
    public int TotalMouseClicks { get; set; }
    public double TotalMouseDistance { get; set; }
    public int UniqueAppsCount { get; set; }
    public List<AppUsageSummary> AppBreakdown { get; set; } = [];
    public List<HourlyActivity> HourlyBreakdown { get; set; } = [];
}

public class WeeklySummary
{
    public DateTime WeekStart { get; set; }
    public List<DailySummary> DailySummaries { get; set; } = [];
    public TimeSpan TotalActiveTime { get; set; }
    public int TotalKeyPresses { get; set; }
    public int TotalMouseClicks { get; set; }
    public TimeSpan AverageDailyActiveTime { get; set; }
}

public class AppUsageSummary
{
    public string ProcessName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public TimeSpan TotalDuration { get; set; }
    public int SessionCount { get; set; }
    public string? Category { get; set; }
}

public class HourlyActivity
{
    public int Hour { get; set; }
    public int KeyPresses { get; set; }
    public int MouseClicks { get; set; }
    public TimeSpan ActiveTime { get; set; }
}
