namespace PChabit.Core.Entities;

public class EfficiencyScore : EntityBase
{
    public DateTime Date { get; set; }
    public double Score { get; set; }
    public int FocusTimeMinutes { get; set; }
    public int DeepWorkMinutes { get; set; }
    public int InterruptionCount { get; set; }
    public double ProductivityRatio { get; set; }
    public double BreakRatio { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
