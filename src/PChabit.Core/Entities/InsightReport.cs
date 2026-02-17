namespace PChabit.Core.Entities;

public class InsightReport : EntityBase
{
    public string ReportType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Summary { get; set; }
    public string? Insights { get; set; }
    public string? Recommendations { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ReportType
{
    Daily,
    Weekly,
    Monthly
}
