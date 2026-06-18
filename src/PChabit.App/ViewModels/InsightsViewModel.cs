using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PChabit.Core.Interfaces;
using PChabit.Core.ValueObjects;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class InsightsViewModel : DbSafeViewModel<InsightsViewModel.InsightsStats>
{
    private readonly IInsightService _insightService;
    private readonly IEfficiencyCalculator _efficiencyCalculator;
    private readonly IPatternAnalyzer _patternAnalyzer;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private double _todayScore;

    [ObservableProperty]
    private string _todayScoreLabel = "0";

    [ObservableProperty]
    private EfficiencyBreakdown _scoreBreakdown = new();

    [ObservableProperty]
    private string _weeklyReport = string.Empty;

    public ObservableCollection<PatternInsight> Insights { get; } = new();
    public ObservableCollection<DailyScoreItem> WeeklyScores { get; } = new();

    public InsightsViewModel(
        IInsightService insightService,
        IEfficiencyCalculator efficiencyCalculator,
        IPatternAnalyzer patternAnalyzer) : base()
    {
        _insightService = insightService;
        _efficiencyCalculator = efficiencyCalculator;
        _patternAnalyzer = patternAnalyzer;
        Title = "智能洞察";
    }

    // === Phase 1 中间数据 ===

    public sealed class InsightsStats
    {
        public EfficiencyBreakdown ScoreBreakdown = new();
        public List<PatternInsight> Insights = new();
        public List<(DateTime Date, double Score, string DayName)> WeeklyScores = new();
        public string WeeklyReport = string.Empty;
    }

    // === DbSafeViewModel 抽象方法 ===

    protected override async Task<InsightsStats> LoadStatsOnBackgroundAsync()
    {
        var selectedDate = SelectedDate;
        var weekStart = GetWeekStart(selectedDate);

        var breakdownTask = _efficiencyCalculator.CalculateDetailedScoreAsync(selectedDate);
        var insightsTask = _insightService.GenerateDailyInsightsAsync(selectedDate);
        var scoresTask = _efficiencyCalculator.GetWeeklyScoresAsync(weekStart);
        var reportTask = _insightService.GenerateWeeklyReportAsync(weekStart);

        await Task.WhenAll(breakdownTask, insightsTask, scoresTask, reportTask);

        var breakdown = await breakdownTask;
        var insights = await insightsTask;
        var scores = await scoresTask;
        var report = await reportTask;

        var weeklyScores = new List<(DateTime, double, string)>();
        for (int i = 0; i < scores.Count; i++)
        {
            weeklyScores.Add((weekStart.AddDays(i), scores[i].TotalScore, weekStart.AddDays(i).ToString("ddd")));
        }

        return new InsightsStats
        {
            ScoreBreakdown = breakdown,
            Insights = insights,
            WeeklyScores = weeklyScores,
            WeeklyReport = report
        };
    }

    protected override async Task ApplyStatsOnUIAsync(InsightsStats stats)
    {
        ScoreBreakdown = stats.ScoreBreakdown;
        TodayScore = stats.ScoreBreakdown.TotalScore;
        TodayScoreLabel = stats.ScoreBreakdown.TotalScore.ToString("F0");

        Insights.Clear();
        foreach (var insight in stats.Insights)
            Insights.Add(insight);

        WeeklyScores.Clear();
        foreach (var (date, score, dayName) in stats.WeeklyScores)
            WeeklyScores.Add(new DailyScoreItem { Date = date, Score = score, DayName = dayName });

        WeeklyReport = stats.WeeklyReport;
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var weekStart = date.AddDays(-(int)date.DayOfWeek);
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
            weekStart = weekStart.AddDays(-7);
        return weekStart.AddDays((int)DayOfWeek.Monday - (int)weekStart.DayOfWeek);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private async Task PreviousDayAsync()
    {
        SelectedDate = SelectedDate.AddDays(-1);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task NextDayAsync()
    {
        SelectedDate = SelectedDate.AddDays(1);
        await LoadDataAsync();
    }
}

public class DailyScoreItem
{
    public DateTime Date { get; set; }
    public double Score { get; set; }
    public string DayName { get; set; } = string.Empty;
}
