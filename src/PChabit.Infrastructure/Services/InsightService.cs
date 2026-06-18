using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Services;

public class InsightService : IInsightService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    private readonly IPatternAnalyzer _patternAnalyzer;
    private readonly IEfficiencyCalculator _efficiencyCalculator;

    public InsightService(
        IDbContextFactory<PChabitDbContext> dbContextFactory,
        IPatternAnalyzer patternAnalyzer,
        IEfficiencyCalculator efficiencyCalculator)
    {
        _dbContextFactory = dbContextFactory;
        _patternAnalyzer = patternAnalyzer;
        _efficiencyCalculator = efficiencyCalculator;
    }

    public async Task<List<PatternInsight>> GenerateDailyInsightsAsync(DateTime date)
    {
        var insights = new List<PatternInsight>();
        
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var sessions = await dbContext.AppSessions
            .Where(s => s.StartTime >= date.Date && s.StartTime < date.Date.AddDays(1))
            .ToListAsync();

        if (!sessions.Any())
        {
            insights.Add(new PatternInsight
            {
                Type = "NoData",
                Title = "暂无数据",
                Description = "今天还没有记录到任何活动数据",
                Severity = "info"
            });
            return insights;
        }

        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        var focusBlocks = await _patternAnalyzer.IdentifyFocusBlocksAsync(date);
        var efficiency = await _efficiencyCalculator.CalculateDetailedScoreAsync(date);

        if (efficiency.TotalScore >= 80)
        {
            insights.Add(new PatternInsight
            {
                Type = "HighEfficiency",
                Title = "高效的一天",
                Description = $"今日效率评分 {efficiency.TotalScore} 分，表现出色！",
                Severity = "success"
            });
        }
        else if (efficiency.TotalScore < 50)
        {
            insights.Add(new PatternInsight
            {
                Type = "LowEfficiency",
                Title = "效率有待提升",
                Description = $"今日效率评分仅 {efficiency.TotalScore} 分，建议关注专注时间",
                Severity = "warning"
            });
        }

        var deepWorkBlocks = focusBlocks.Where(b => b.IsDeepWork).ToList();
        if (deepWorkBlocks.Any())
        {
            var totalDeepWork = deepWorkBlocks.Sum(b => b.Duration.TotalMinutes);
            insights.Add(new PatternInsight
            {
                Type = "DeepWork",
                Title = "深度工作时段",
                Description = $"今天有 {deepWorkBlocks.Count} 个深度工作时段，总计 {FormatMinutes(totalDeepWork)}",
                Severity = "success",
                Data = new Dictionary<string, object>
                {
                    ["count"] = deepWorkBlocks.Count,
                    ["totalMinutes"] = totalDeepWork
                }
            });
        }
        else if (totalMinutes > 60)
        {
            insights.Add(new PatternInsight
            {
                Type = "NoDeepWork",
                Title = "缺少深度工作",
                Description = "今天还没有超过25分钟的专注时段，尝试减少干扰",
                Severity = "info"
            });
        }

        var switchesPerHour = CalculateSwitchesPerHour(sessions);
        if (switchesPerHour > 20)
        {
            insights.Add(new PatternInsight
            {
                Type = "HighSwitchRate",
                Title = "应用切换频繁",
                Description = $"每小时切换 {switchesPerHour:F1} 次，建议减少干扰源",
                Severity = "warning",
                Data = new Dictionary<string, object>
                {
                    ["switchesPerHour"] = switchesPerHour
                }
            });
        }

        var entertainmentMinutes = sessions
            .Where(s => !string.IsNullOrEmpty(s.Category) && 
                       (s.Category!.Contains("娱乐") || s.Category.Contains("游戏")))
            .Sum(s => s.Duration.TotalMinutes);
        
        var entertainmentRatio = totalMinutes > 0 ? entertainmentMinutes / totalMinutes : 0;
        if (entertainmentRatio > 0.3)
        {
            insights.Add(new PatternInsight
            {
                Type = "HighEntertainment",
                Title = "娱乐时间较多",
                Description = $"娱乐类应用占比 {entertainmentRatio:P1}，注意平衡工作与休息",
                Severity = "info"
            });
        }

        return insights;
    }

    public async Task<List<PatternInsight>> GenerateWeeklyInsightsAsync(DateTime weekStart)
    {
        var insights = new List<PatternInsight>();
        
        var weekEnd = weekStart.AddDays(7);
        var dailyScores = await _efficiencyCalculator.GetWeeklyScoresAsync(weekStart);
        var avgScore = dailyScores.Average(s => s.TotalScore);

        insights.Add(new PatternInsight
        {
            Type = "WeeklyAverage",
            Title = "周效率平均分",
            Description = $"本周平均效率评分 {avgScore:F1} 分",
            Severity = avgScore >= 70 ? "success" : "warning"
        });

        var bestDay = dailyScores.Select((score, index) => new { Score = score, Day = weekStart.AddDays(index) })
            .OrderByDescending(x => x.Score.TotalScore)
            .First();
        
        insights.Add(new PatternInsight
        {
            Type = "BestDay",
            Title = "最佳表现日",
            Description = $"{bestDay.Day:MM月dd日} 表现最佳，效率评分 {bestDay.Score.TotalScore} 分",
            Severity = "success"
        });

        var trend = CalculateTrend(dailyScores);
        if (trend > 5)
        {
            insights.Add(new PatternInsight
            {
                Type = "ImprovingTrend",
                Title = "效率上升趋势",
                Description = "本周效率呈上升趋势，继续保持！",
                Severity = "success"
            });
        }
        else if (trend < -5)
        {
            insights.Add(new PatternInsight
            {
                Type = "DecliningTrend",
                Title = "效率下降趋势",
                Description = "本周效率有所下降，注意调整工作节奏",
                Severity = "warning"
            });
        }

        return insights;
    }

    public async Task<string> GenerateWeeklyReportAsync(DateTime weekStart)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var weekEnd = weekStart.AddDays(7);
        var sessions = await dbContext.AppSessions
            .Where(s => s.StartTime >= weekStart && s.StartTime < weekEnd)
            .ToListAsync();

        var dailyScores = await _efficiencyCalculator.GetWeeklyScoresAsync(weekStart);
        var avgScore = dailyScores.Average(s => s.TotalScore);
        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        var topApps = sessions
            .GroupBy(s => s.ProcessName)
            .OrderByDescending(g => g.Sum(s => s.Duration.TotalMinutes))
            .Take(5)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# PChabit 周报 ({weekStart:MM月dd日} - {weekStart.AddDays(6):MM月dd日})");
        sb.AppendLine();
        sb.AppendLine("## 📊 总体概览");
        sb.AppendLine($"- **总使用时长**: {FormatMinutes(totalMinutes)}");
        sb.AppendLine($"- **平均效率评分**: {avgScore:F1} 分");
        sb.AppendLine($"- **活跃天数**: {sessions.Select(s => s.StartTime.Date).Distinct().Count()} 天");
        sb.AppendLine();

        sb.AppendLine("## 🏆 Top 5 应用");
        for (int i = 0; i < topApps.Count; i++)
        {
            var app = topApps[i];
            var minutes = app.Sum(s => s.Duration.TotalMinutes);
            sb.AppendLine($"{i + 1}. **{app.Key}** - {FormatMinutes(minutes)}");
        }
        sb.AppendLine();

        sb.AppendLine("## 📈 每日效率评分");
        for (int i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var score = dailyScores[i];
            var bar = GenerateScoreBar(score.TotalScore);
            sb.AppendLine($"- {date:MM月dd日 ddd}: {bar} {score.TotalScore:F0}分");
        }
        sb.AppendLine();

        var insights = await GenerateWeeklyInsightsAsync(weekStart);
        sb.AppendLine("## 💡 本周洞察");
        foreach (var insight in insights)
        {
            sb.AppendLine($"- **{insight.Title}**: {insight.Description}");
        }

        return sb.ToString();
    }

    public async Task<string> GenerateMonthlyReportAsync(DateTime monthStart)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var monthEnd = monthStart.AddMonths(1);
        var sessions = await dbContext.AppSessions
            .Where(s => s.StartTime >= monthStart && s.StartTime < monthEnd)
            .ToListAsync();

        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        var activeDays = sessions.Select(s => s.StartTime.Date).Distinct().Count();
        var avgDailyMinutes = activeDays > 0 ? totalMinutes / activeDays : 0;

        var topApps = sessions
            .GroupBy(s => s.ProcessName)
            .OrderByDescending(g => g.Sum(s => s.Duration.TotalMinutes))
            .Take(10)
            .ToList();

        var topCategories = sessions
            .Where(s => !string.IsNullOrEmpty(s.Category))
            .GroupBy(s => s.Category!)
            .OrderByDescending(g => g.Sum(s => s.Duration.TotalMinutes))
            .Take(5)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# PChabit 月报 ({monthStart:yyyy年MM月})");
        sb.AppendLine();
        sb.AppendLine("## 📊 月度概览");
        sb.AppendLine($"- **总使用时长**: {FormatMinutes(totalMinutes)}");
        sb.AppendLine($"- **活跃天数**: {activeDays} 天");
        sb.AppendLine($"- **日均使用**: {FormatMinutes(avgDailyMinutes)}");
        sb.AppendLine();

        sb.AppendLine("## 🏆 Top 10 应用");
        for (int i = 0; i < topApps.Count; i++)
        {
            var app = topApps[i];
            var minutes = app.Sum(s => s.Duration.TotalMinutes);
            sb.AppendLine($"{i + 1}. **{app.Key}** - {FormatMinutes(minutes)}");
        }
        sb.AppendLine();

        sb.AppendLine("## 📂 分类统计");
        foreach (var cat in topCategories)
        {
            var minutes = cat.Sum(s => s.Duration.TotalMinutes);
            var percentage = totalMinutes > 0 ? minutes / totalMinutes * 100 : 0;
            sb.AppendLine($"- **{cat.Key}**: {FormatMinutes(minutes)} ({percentage:F1}%)");
        }

        return sb.ToString();
    }

    private double CalculateSwitchesPerHour(List<AppSession> sessions)
    {
        if (sessions.Count <= 1)
            return 0;

        var totalHours = (sessions.Max(s => s.StartTime) - sessions.Min(s => s.StartTime)).TotalHours;
        if (totalHours <= 0)
            return 0;

        return (sessions.Count - 1) / totalHours;
    }

    private double CalculateTrend(List<EfficiencyBreakdown> scores)
    {
        if (scores.Count < 2)
            return 0;

        var firstHalf = scores.Take(scores.Count / 2).Average(s => s.TotalScore);
        var secondHalf = scores.Skip(scores.Count / 2).Average(s => s.TotalScore);
        
        return secondHalf - firstHalf;
    }

    private static string FormatMinutes(double minutes)
    {
        if (minutes < 60)
            return $"{(int)minutes} 分钟";
        
        var hours = (int)(minutes / 60);
        var mins = (int)(minutes % 60);
        return mins > 0 ? $"{hours} 小时 {mins} 分钟" : $"{hours} 小时";
    }

    private static string GenerateScoreBar(double score)
    {
        var filled = (int)(score / 10);
        var empty = 10 - filled;
        return new string('█', filled) + new string('░', empty);
    }
}
