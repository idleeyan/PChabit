namespace PChabit.Core.Entities;

public class UserGoal : EntityBase
{
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public int? DailyLimitMinutes { get; set; }
    public int? DailyTargetMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public enum GoalTargetType
{
    Application,
    Category,
    TotalTime
}
