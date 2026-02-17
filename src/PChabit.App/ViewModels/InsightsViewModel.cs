using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PChabit.Core.Interfaces;
using PChabit.Core.ValueObjects;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class InsightsViewModel : ViewModelBase
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

    [ObservableProperty]
    private bool _isLoading;

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

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            await LoadTodayInsightsAsync();
            await LoadWeeklyScoresAsync();
            await LoadWeeklyReportAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载洞察数据失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadTodayInsightsAsync()
    {
        var breakdown = await _efficiencyCalculator.CalculateDetailedScoreAsync(SelectedDate);
        ScoreBreakdown = breakdown;
        TodayScore = breakdown.TotalScore;
        TodayScoreLabel = breakdown.TotalScore.ToString("F0");

        Insights.Clear();
        var insights = await _insightService.GenerateDailyInsightsAsync(SelectedDate);
        foreach (var insight in insights)
        {
            Insights.Add(insight);
        }
    }

    private async Task LoadWeeklyScoresAsync()
    {
        var weekStart = SelectedDate.AddDays(-(int)SelectedDate.DayOfWeek);
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            weekStart = weekStart.AddDays(-7);
        }
        weekStart = weekStart.AddDays((int)DayOfWeek.Monday - (int)weekStart.DayOfWeek);

        WeeklyScores.Clear();
        var scores = await _efficiencyCalculator.GetWeeklyScoresAsync(weekStart);
        
        for (int i = 0; i < scores.Count; i++)
        {
            WeeklyScores.Add(new DailyScoreItem
            {
                Date = weekStart.AddDays(i),
                Score = scores[i].TotalScore,
                DayName = weekStart.AddDays(i).ToString("ddd")
            });
        }
    }

    private async Task LoadWeeklyReportAsync()
    {
        var weekStart = SelectedDate.AddDays(-(int)SelectedDate.DayOfWeek);
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            weekStart = weekStart.AddDays(-7);
        }
        weekStart = weekStart.AddDays((int)DayOfWeek.Monday - (int)weekStart.DayOfWeek);

        WeeklyReport = await _insightService.GenerateWeeklyReportAsync(weekStart);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

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
