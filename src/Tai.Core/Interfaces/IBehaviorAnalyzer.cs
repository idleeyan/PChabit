using Tai.Core.Entities;

namespace Tai.Core.Interfaces;

public interface IBehaviorAnalyzer
{
    Task<BehaviorAnalysisResult> AnalyzeAsync(DateTime startDate, DateTime endDate);
    Task<ProductivityReport> GetProductivityReportAsync(DateTime date);
    Task<List<FocusSession>> IdentifyFocusSessionsAsync(DateTime date);
    Task<Dictionary<string, double>> GetAppProductivityScoresAsync(DateTime date);
}

public class BehaviorAnalysisResult
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    
    public double OverallProductivity { get; set; }
    public double FocusScore { get; set; }
    public double ConsistencyScore { get; set; }
    
    public TimeSpan TotalActiveTime { get; set; }
    public TimeSpan TotalFocusTime { get; set; }
    public TimeSpan TotalIdleTime { get; set; }
    
    public List<PeakHour> PeakHours { get; set; } = [];
    public List<AppUsageSummary> TopApps { get; set; } = [];
    public List<CategorySummary> CategoryBreakdown { get; set; } = [];
    public List<BehaviorInsight> Insights { get; set; } = [];
    public List<TrendData> Trends { get; set; } = [];
}

public class PeakHour
{
    public int Hour { get; set; }
    public TimeSpan Duration { get; set; }
    public double Productivity { get; set; }
    public string? DominantActivity { get; set; }
}

public class AppUsageSummary
{
    public string ProcessName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public int SessionCount { get; set; }
    public double ProductivityScore { get; set; }
    public string Category { get; set; } = "Other";
}

public class CategorySummary
{
    public string Category { get; set; } = string.Empty;
    public TimeSpan TotalDuration { get; set; }
    public double Percentage { get; set; }
    public double ProductivityScore { get; set; }
}

public class BehaviorInsight
{
    public InsightType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string? Recommendation { get; set; }
}

public enum InsightType
{
    Productivity,
    Focus,
    Balance,
    Health,
    Efficiency,
    Pattern
}

public class TrendData
{
    public DateTime Date { get; set; }
    public double Productivity { get; set; }
    public TimeSpan ActiveTime { get; set; }
    public TimeSpan FocusTime { get; set; }
}

public class ProductivityReport
{
    public DateTime Date { get; set; }
    
    public double OverallScore { get; set; }
    public double FocusScore { get; set; }
    public double EfficiencyScore { get; set; }
    public double BalanceScore { get; set; }
    
    public TimeSpan TotalWorkTime { get; set; }
    public TimeSpan DeepWorkTime { get; set; }
    public TimeSpan ShallowWorkTime { get; set; }
    public TimeSpan BreakTime { get; set; }
    
    public int FocusSessionCount { get; set; }
    public int InterruptionCount { get; set; }
    public double AverageSessionLength { get; set; }
    
    public List<HourlyProductivity> HourlyBreakdown { get; set; } = [];
    public List<string> Achievements { get; set; } = [];
    public List<string> Improvements { get; set; } = [];
}

public class HourlyProductivity
{
    public int Hour { get; set; }
    public double Score { get; set; }
    public string? PrimaryActivity { get; set; }
    public TimeSpan Duration { get; set; }
}

public class FocusSession
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? PrimaryApp { get; set; }
    public double IntensityScore { get; set; }
    public int SwitchCount { get; set; }
    public bool IsDeepWork { get; set; }
}
