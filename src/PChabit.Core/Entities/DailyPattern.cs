using PChabit.Core.Interfaces;

namespace PChabit.Core.Entities;

public class DailyPattern : EntityBase, IAggregateRoot
{
    public DateTime Date { get; set; }
    
    public TimeSpan FirstActivityTime { get; set; }
    public TimeSpan LastActivityTime { get; set; }
    public TimeSpan TotalActiveTime { get; set; }
    public TimeSpan TotalIdleTime { get; set; }
    
    public double ProductivityScore { get; set; }
    public int InterruptionCount { get; set; }
    public TimeSpan DeepWorkTime { get; set; }
    
    public List<FocusBlock> FocusBlocks { get; set; } = [];
    public List<BreakPattern> Breaks { get; set; } = [];
    public Dictionary<int, ActivityLevel> HourlyActivity { get; set; } = new();
    
    public List<UsagePattern> Patterns { get; set; } = [];
}

public class FocusBlock
{
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? PrimaryActivity { get; set; }
    public int InterruptionCount { get; set; }
    public double IntensityScore { get; set; }
}

public class BreakPattern
{
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public BreakType Type { get; set; }
}

public class ActivityLevel
{
    public int Hour { get; set; }
    public double Score { get; set; }
    public string? DominantActivity { get; set; }
}

public enum BreakType
{
    Short,
    Medium,
    Long,
    Meal
}
