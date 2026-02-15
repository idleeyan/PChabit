namespace Tai.Core.Entities;

public class UsagePattern
{
    public PatternType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ProcessName { get; set; }
    public string? RelatedProcessName { get; set; }
    public int? Hour { get; set; }
    public int Frequency { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public double Confidence { get; set; }
}

public enum PatternType
{
    FrequentApp,
    PeakHour,
    AppSequence,
    WorkPattern,
    IdlePattern,
    LongBreak,
    ContextSwitch,
    RapidSwitch,
    ProductivityRatio,
    HighProductivity,
    LowProductivity
}
