using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Analysis;

public class EfficiencyCalculator : IEfficiencyCalculator
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    private readonly IPatternAnalyzer _patternAnalyzer;

    private const double FocusWeight = 0.30;
    private const double TaskCompletionWeight = 0.25;
    private const double BalanceWeight = 0.20;
    private const double InterruptionWeight = 0.15;
    private const double GoalWeight = 0.10;

    public EfficiencyCalculator(
        IDbContextFactory<PChabitDbContext> dbContextFactory,
        IPatternAnalyzer patternAnalyzer)
    {
        _dbContextFactory = dbContextFactory;
        _patternAnalyzer = patternAnalyzer;
    }

    public async Task<double> CalculateDailyScoreAsync(DateTime date)
    {
        var breakdown = await CalculateDetailedScoreAsync(date);
        return breakdown.TotalScore;
    }

    public async Task<EfficiencyBreakdown> CalculateDetailedScoreAsync(DateTime date)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var sessions = await dbContext.AppSessions
            .Where(s => s.StartTime.Date == date.Date)
            .ToListAsync();

        if (!sessions.Any())
        {
            return new EfficiencyBreakdown();
        }

        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        var focusBlocks = await _patternAnalyzer.IdentifyFocusBlocksAsync(date);
        var deepWorkMinutes = focusBlocks.Where(b => b.IsDeepWork).Sum(b => b.Duration.TotalMinutes);
        var focusTimeMinutes = focusBlocks.Sum(b => b.Duration.TotalMinutes);

        var focusScore = CalculateFocusScore(focusTimeMinutes, totalMinutes, deepWorkMinutes);
        var taskScore = await CalculateTaskCompletionScoreAsync(sessions);
        var balanceScore = CalculateBalanceScore(sessions, totalMinutes);
        var interruptionScore = CalculateInterruptionScore(sessions);
        var goalScore = await CalculateGoalScoreAsync(date, sessions);

        var totalScore = (focusScore * FocusWeight) +
                        (taskScore * TaskCompletionWeight) +
                        (balanceScore * BalanceWeight) +
                        (interruptionScore * InterruptionWeight) +
                        (goalScore * GoalWeight);

        return new EfficiencyBreakdown
        {
            FocusScore = Math.Round(focusScore, 1),
            TaskCompletionScore = Math.Round(taskScore, 1),
            BalanceScore = Math.Round(balanceScore, 1),
            InterruptionScore = Math.Round(interruptionScore, 1),
            GoalScore = Math.Round(goalScore, 1),
            TotalScore = Math.Round(totalScore, 1)
        };
    }

    public async Task<List<EfficiencyBreakdown>> GetWeeklyScoresAsync(DateTime weekStart)
    {
        var scores = new List<EfficiencyBreakdown>();
        
        for (int i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var score = await CalculateDetailedScoreAsync(date);
            scores.Add(score);
        }

        return scores;
    }

    private double CalculateFocusScore(double focusMinutes, double totalMinutes, double deepWorkMinutes)
    {
        if (totalMinutes <= 0)
            return 0;

        var focusRatio = focusMinutes / totalMinutes;
        var deepWorkBonus = Math.Min(deepWorkMinutes / 120.0, 1.0) * 20;

        var baseScore = Math.Min(focusRatio * 100, 80);
        return Math.Min(baseScore + deepWorkBonus, 100);
    }

    private async Task<double> CalculateTaskCompletionScoreAsync(List<AppSession> sessions)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var productiveCategoryNames = await dbContext.ProgramCategories
            .Where(c => c.Name.Contains("开发") || 
                       c.Name.Contains("办公") || 
                       c.Name.Contains("生产力") ||
                       c.Name.Contains("工作"))
            .Select(c => c.Name)
            .ToListAsync();

        var productiveMinutes = sessions
            .Where(s => !string.IsNullOrEmpty(s.Category) && 
                        productiveCategoryNames.Any(cn => s.Category!.Contains(cn, StringComparison.OrdinalIgnoreCase)))
            .Sum(s => s.Duration.TotalMinutes);

        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        
        if (totalMinutes <= 0)
            return 0;

        var productiveRatio = productiveMinutes / totalMinutes;
        return Math.Min(productiveRatio * 100, 100);
    }

    private double CalculateBalanceScore(List<AppSession> sessions, double totalMinutes)
    {
        if (totalMinutes <= 0)
            return 50;

        var breakMinutes = 0.0;
        for (int i = 1; i < sessions.Count; i++)
        {
            var gap = (sessions[i].StartTime - sessions[i - 1].StartTime - sessions[i - 1].Duration).TotalMinutes;
            if (gap > 0)
                breakMinutes += gap;
        }

        var breakRatio = breakMinutes / totalMinutes;
        
        var idealBreakRatio = 0.15;
        var deviation = Math.Abs(breakRatio - idealBreakRatio);
        
        if (deviation <= 0.05)
            return 100;
        else if (deviation <= 0.10)
            return 80;
        else if (deviation <= 0.20)
            return 60;
        else
            return Math.Max(40, 100 - deviation * 200);
    }

    private double CalculateInterruptionScore(List<AppSession> sessions)
    {
        if (sessions.Count <= 1)
            return 100;

        var switchCount = sessions.Count - 1;
        var totalHours = (sessions.Max(s => s.StartTime) - sessions.Min(s => s.StartTime)).TotalHours;
        
        if (totalHours <= 0)
            return 100;

        var switchesPerHour = switchCount / totalHours;

        if (switchesPerHour <= 5)
            return 100;
        else if (switchesPerHour <= 10)
            return 90;
        else if (switchesPerHour <= 20)
            return 70;
        else if (switchesPerHour <= 30)
            return 50;
        else
            return Math.Max(20, 100 - switchesPerHour * 2);
    }

    private async Task<double> CalculateGoalScoreAsync(DateTime date, List<AppSession> sessions)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var goals = await dbContext.UserGoals
            .Where(g => g.IsActive)
            .ToListAsync();

        if (!goals.Any())
            return 70;

        var achievedCount = 0;
        foreach (var goal in goals)
        {
            var minutes = 0.0;
            
            if (goal.TargetType == nameof(GoalTargetType.Application))
            {
                minutes = sessions
                    .Where(s => s.ProcessName.Equals(goal.TargetId, StringComparison.OrdinalIgnoreCase))
                    .Sum(s => s.Duration.TotalMinutes);
            }
            else if (goal.TargetType == nameof(GoalTargetType.Category))
            {
                minutes = sessions
                    .Where(s => !string.IsNullOrEmpty(s.Category) && 
                                s.Category!.Equals(goal.TargetId, StringComparison.OrdinalIgnoreCase))
                    .Sum(s => s.Duration.TotalMinutes);
            }
            else if (goal.TargetType == nameof(GoalTargetType.TotalTime))
            {
                minutes = sessions.Sum(s => s.Duration.TotalMinutes);
            }

            if (goal.DailyLimitMinutes.HasValue && minutes <= goal.DailyLimitMinutes.Value)
                achievedCount++;
            else if (goal.DailyTargetMinutes.HasValue && minutes >= goal.DailyTargetMinutes.Value)
                achievedCount++;
        }

        return goals.Any() ? (double)achievedCount / goals.Count * 100 : 70;
    }
}
