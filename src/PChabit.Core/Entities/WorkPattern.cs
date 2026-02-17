namespace PChabit.Core.Entities;

public class WorkPattern : EntityBase
{
    public DateTime Date { get; set; }
    public TimeSpan? WorkStartTime { get; set; }
    public TimeSpan? WorkEndTime { get; set; }
    public string? PeakHours { get; set; }
    public string? FocusBlocks { get; set; }
    public int BreakCount { get; set; }
    public int TotalBreakMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
}
