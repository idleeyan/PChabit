using PChabit.Core.Entities;
using PChabit.Core.ValueObjects;

namespace PChabit.Core.Interfaces;

public interface IPatternAnalyzer
{
    Task<WorkPattern?> AnalyzeDayAsync(DateTime date);
    Task<List<FocusBlockInfo>> IdentifyFocusBlocksAsync(DateTime date, int minDurationMinutes = 25);
    Task<List<TimeSlot>> IdentifyPeakHoursAsync(DateTime date);
    Task<WorkSession> IdentifyWorkSessionAsync(DateTime date);
    Task<Dictionary<int, double>> GetHourlyActivityDistributionAsync(DateTime date);
}

public interface IEfficiencyCalculator
{
    Task<double> CalculateDailyScoreAsync(DateTime date);
    Task<EfficiencyBreakdown> CalculateDetailedScoreAsync(DateTime date);
    Task<List<EfficiencyBreakdown>> GetWeeklyScoresAsync(DateTime weekStart);
}

public interface IInsightService
{
    Task<List<PatternInsight>> GenerateDailyInsightsAsync(DateTime date);
    Task<List<PatternInsight>> GenerateWeeklyInsightsAsync(DateTime weekStart);
    Task<string> GenerateWeeklyReportAsync(DateTime weekStart);
    Task<string> GenerateMonthlyReportAsync(DateTime monthStart);
}

public interface IGoalService
{
    Task<IEnumerable<UserGoal>> GetActiveGoalsAsync();
    Task<UserGoal> CreateGoalAsync(UserGoal goal);
    Task UpdateGoalAsync(UserGoal goal);
    Task DeleteGoalAsync(Guid goalId);
    Task<Dictionary<Guid, double>> GetGoalProgressAsync(DateTime date);
    Task<bool> CheckGoalViolationAsync(Guid goalId, DateTime date);
}

public interface INotificationService
{
    Task ShowReminderAsync(string title, string message, string? action = null);
    Task ShowGoalAlertAsync(string title, string message);
    Task ScheduleReminderAsync(string id, DateTime time, string title, string message);
    void CancelReminder(string id);
}
