using PChabit.Core.Entities;
using PChabit.Core.Interfaces;

namespace PChabit.Application.Analysis;

public class BehaviorAnalyzer : IBehaviorAnalyzer
{
    private readonly IRepository<AppSession> _appSessionRepo;
    private readonly IRepository<KeyboardSession> _keyboardRepo;
    private readonly IRepository<MouseSession> _mouseRepo;
    
    private static readonly HashSet<string> ProductiveApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "devenv", "idea64", "pycharm64", "webstorm64", "rider64",
        "visualstudio", "vscode", "notepad++", "sublime_text",
        "unity", "blender", "figma", "xd",
        "excel", "word", "powerpnt", "onenote",
        "slack", "teams", "zoom", "discord"
    };
    
    private static readonly HashSet<string> DistractionApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube", "netflix", "spotify", "twitch",
        "facebook", "twitter", "instagram", "tiktok",
        "reddit", "tumblr", "pinterest"
    };
    
    private static readonly Dictionary<string, string> AppCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        { "code", "Development" }, { "devenv", "Development" }, { "idea64", "Development" },
        { "pycharm64", "Development" }, { "webstorm64", "Development" }, { "rider64", "Development" },
        { "visualstudio", "Development" }, { "vscode", "Development" },
        
        { "chrome", "Browser" }, { "msedge", "Browser" }, { "firefox", "Browser" },
        
        { "excel", "Office" }, { "word", "Office" }, { "powerpnt", "Office" },
        { "outlook", "Office" }, { "onenote", "Office" },
        
        { "slack", "Communication" }, { "teams", "Communication" },
        { "discord", "Communication" }, { "zoom", "Communication" },
        
        { "youtube", "Entertainment" }, { "netflix", "Entertainment" },
        { "spotify", "Entertainment" }, { "twitch", "Entertainment" }
    };
    
    public BehaviorAnalyzer(
        IRepository<AppSession> appSessionRepo,
        IRepository<KeyboardSession> keyboardRepo,
        IRepository<MouseSession> mouseRepo)
    {
        _appSessionRepo = appSessionRepo;
        _keyboardRepo = keyboardRepo;
        _mouseRepo = mouseRepo;
    }
    
    public async Task<BehaviorAnalysisResult> AnalyzeAsync(DateTime startDate, DateTime endDate)
    {
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime >= startDate && s.StartTime < endDate.AddDays(1));
        
        var orderedSessions = sessions.OrderBy(s => s.StartTime).ToList();
        
        var result = new BehaviorAnalysisResult
        {
            StartDate = startDate,
            EndDate = endDate,
            TotalActiveTime = TimeSpan.FromTicks(orderedSessions.Sum(s => s.Duration.Ticks)),
            PeakHours = CalculatePeakHours(orderedSessions),
            TopApps = CalculateTopApps(orderedSessions),
            CategoryBreakdown = CalculateCategoryBreakdown(orderedSessions)
        };
        
        result.OverallProductivity = CalculateOverallProductivity(result);
        result.FocusScore = CalculateFocusScore(orderedSessions);
        result.ConsistencyScore = CalculateConsistencyScore(orderedSessions, startDate, endDate);
        
        result.Insights = GenerateInsights(result, orderedSessions);
        result.Trends = CalculateTrends(orderedSessions, startDate, endDate);
        
        return result;
    }
    
    public async Task<ProductivityReport> GetProductivityReportAsync(DateTime date)
    {
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime.Date == date.Date);
        
        var orderedSessions = sessions.OrderBy(s => s.StartTime).ToList();
        var focusSessions = await IdentifyFocusSessionsAsync(date);
        
        var report = new ProductivityReport
        {
            Date = date,
            TotalWorkTime = TimeSpan.FromTicks(orderedSessions.Sum(s => s.Duration.Ticks)),
            FocusSessionCount = focusSessions.Count,
            DeepWorkTime = TimeSpan.FromTicks(focusSessions.Where(f => f.IsDeepWork).Sum(f => f.Duration.Ticks)),
            HourlyBreakdown = CalculateHourlyProductivity(orderedSessions)
        };
        
        report.OverallScore = CalculateDailyProductivityScore(orderedSessions, focusSessions);
        report.FocusScore = CalculateDailyFocusScore(focusSessions);
        report.EfficiencyScore = CalculateEfficiencyScore(orderedSessions);
        report.BalanceScore = CalculateBalanceScore(orderedSessions);
        
        report.AverageSessionLength = focusSessions.Any()
            ? focusSessions.Average(f => f.Duration.TotalMinutes)
            : 0;
        
        report.InterruptionCount = focusSessions.Sum(f => f.SwitchCount);
        report.ShallowWorkTime = report.TotalWorkTime - report.DeepWorkTime;
        
        report.Achievements = GenerateAchievements(report);
        report.Improvements = GenerateImprovements(report);
        
        return report;
    }
    
    public async Task<List<FocusSession>> IdentifyFocusSessionsAsync(DateTime date)
    {
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime.Date == date.Date);
        
        var orderedSessions = sessions.OrderBy(s => s.StartTime).ToList();
        var focusSessions = new List<FocusSession>();
        
        if (!orderedSessions.Any()) return focusSessions;
        
        var currentSession = new FocusSession
        {
            StartTime = orderedSessions[0].StartTime,
            PrimaryApp = orderedSessions[0].ProcessName
        };
        
        var appSwitches = 0;
        var lastApp = orderedSessions[0].ProcessName;
        
        foreach (var session in orderedSessions)
        {
            if (session.ProcessName != lastApp)
            {
                appSwitches++;
                lastApp = session.ProcessName;
            }
            
            var timeSinceStart = session.StartTime - currentSession.StartTime;
            var switchRate = appSwitches / Math.Max(timeSinceStart.TotalMinutes, 1);
            
            if (switchRate > 0.5 && timeSinceStart.TotalMinutes > 15)
            {
                currentSession.EndTime = session.StartTime;
                currentSession.Duration = currentSession.EndTime - currentSession.StartTime;
                currentSession.SwitchCount = appSwitches;
                currentSession.IntensityScore = CalculateIntensityScore(currentSession);
                currentSession.IsDeepWork = currentSession.Duration.TotalMinutes >= 25 && currentSession.IntensityScore > 0.7;
                
                if (currentSession.Duration.TotalMinutes >= 10)
                {
                    focusSessions.Add(currentSession);
                }
                
                currentSession = new FocusSession
                {
                    StartTime = session.StartTime,
                    PrimaryApp = session.ProcessName
                };
                appSwitches = 0;
            }
        }
        
        currentSession.EndTime = orderedSessions.Last().EndTime ?? DateTime.Now;
        currentSession.Duration = currentSession.EndTime - currentSession.StartTime;
        currentSession.SwitchCount = appSwitches;
        currentSession.IntensityScore = CalculateIntensityScore(currentSession);
        currentSession.IsDeepWork = currentSession.Duration.TotalMinutes >= 25 && currentSession.IntensityScore > 0.7;
        
        if (currentSession.Duration.TotalMinutes >= 10)
        {
            focusSessions.Add(currentSession);
        }
        
        return focusSessions;
    }
    
    public async Task<Dictionary<string, double>> GetAppProductivityScoresAsync(DateTime date)
    {
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime.Date == date.Date);
        
        var scores = new Dictionary<string, double>();
        
        var appGroups = sessions.GroupBy(s => s.ProcessName);
        
        foreach (var group in appGroups)
        {
            var processName = group.Key.ToLowerInvariant();
            var score = CalculateAppProductivityScore(processName, group.ToList());
            scores[group.Key] = score;
        }
        
        return scores;
    }
    
    private List<PeakHour> CalculatePeakHours(List<AppSession> sessions)
    {
        return sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new PeakHour
            {
                Hour = g.Key,
                Duration = TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks)),
                Productivity = g.Average(s => GetAppProductivity(s.ProcessName)),
                DominantActivity = g.OrderByDescending(s => s.Duration).FirstOrDefault()?.ProcessName
            })
            .OrderByDescending(p => p.Duration)
            .Take(5)
            .ToList();
    }
    
    private List<AppUsageSummary> CalculateTopApps(List<AppSession> sessions)
    {
        return sessions
            .GroupBy(s => s.ProcessName)
            .Select(g => new AppUsageSummary
            {
                ProcessName = g.Key,
                TotalDuration = TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks)),
                SessionCount = g.Count(),
                ProductivityScore = GetAppProductivity(g.Key),
                Category = GetAppCategory(g.Key)
            })
            .OrderByDescending(a => a.TotalDuration)
            .Take(10)
            .ToList();
    }
    
    private List<CategorySummary> CalculateCategoryBreakdown(List<AppSession> sessions)
    {
        var totalTicks = sessions.Sum(s => s.Duration.Ticks);
        
        return sessions
            .GroupBy(s => GetAppCategory(s.ProcessName))
            .Select(g => new CategorySummary
            {
                Category = g.Key,
                TotalDuration = TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks)),
                Percentage = totalTicks > 0 ? (double)g.Sum(s => s.Duration.Ticks) / totalTicks * 100 : 0,
                ProductivityScore = g.Average(s => GetAppProductivity(s.ProcessName))
            })
            .OrderByDescending(c => c.TotalDuration)
            .ToList();
    }
    
    private double CalculateOverallProductivity(BehaviorAnalysisResult result)
    {
        if (!result.CategoryBreakdown.Any()) return 0;
        
        var weightedSum = result.CategoryBreakdown.Sum(c => c.ProductivityScore * c.Percentage);
        return weightedSum / 100;
    }
    
    private double CalculateFocusScore(List<AppSession> sessions)
    {
        if (!sessions.Any()) return 0;
        
        var switches = 0;
        var lastApp = string.Empty;
        
        foreach (var session in sessions)
        {
            if (session.ProcessName != lastApp)
            {
                switches++;
                lastApp = session.ProcessName;
            }
        }
        
        var avgSessionLength = sessions.Average(s => s.Duration.TotalMinutes);
        var switchRate = switches / Math.Max(sessions.Count, 1);
        
        var lengthScore = Math.Min(avgSessionLength / 30, 1);
        var switchScore = Math.Max(1 - switchRate / 10, 0);
        
        return (lengthScore * 0.6 + switchScore * 0.4) * 100;
    }
    
    private double CalculateConsistencyScore(List<AppSession> sessions, DateTime startDate, DateTime endDate)
    {
        var days = (endDate - startDate).Days + 1;
        if (days <= 1) return 100;
        
        var dailyDurations = sessions
            .GroupBy(s => s.StartTime.Date)
            .Select(g => g.Sum(s => s.Duration.TotalMinutes))
            .ToList();
        
        if (dailyDurations.Count < 2) return 100;
        
        var avg = dailyDurations.Average();
        var variance = dailyDurations.Sum(d => Math.Pow(d - avg, 2)) / dailyDurations.Count;
        var stdDev = Math.Sqrt(variance);
        
        var coefficientOfVariation = avg > 0 ? stdDev / avg : 0;
        
        return Math.Max(0, (1 - coefficientOfVariation) * 100);
    }
    
    private List<BehaviorInsight> GenerateInsights(BehaviorAnalysisResult result, List<AppSession> sessions)
    {
        var insights = new List<BehaviorInsight>();
        
        if (result.PeakHours.Any())
        {
            var topPeak = result.PeakHours.First();
            insights.Add(new BehaviorInsight
            {
                Type = InsightType.Pattern,
                Title = "高效时段识别",
                Description = $"你在 {topPeak.Hour}:00 - {topPeak.Hour + 1}:00 期间最为活跃",
                Confidence = 0.85,
                Recommendation = "建议将重要任务安排在这个时段"
            });
        }
        
        if (result.FocusScore < 50)
        {
            insights.Add(new BehaviorInsight
            {
                Type = InsightType.Focus,
                Title = "专注度待提升",
                Description = "检测到频繁的应用切换，可能影响深度工作",
                Confidence = 0.75,
                Recommendation = "尝试使用番茄工作法，每25分钟专注后休息5分钟"
            });
        }
        
        var distractionTime = result.CategoryBreakdown
            .Where(c => c.Category == "Entertainment")
            .Sum(c => c.TotalDuration.TotalMinutes);
        
        if (distractionTime > 120)
        {
            insights.Add(new BehaviorInsight
            {
                Type = InsightType.Balance,
                Title = "娱乐时间较长",
                Description = $"今日娱乐应用使用时间超过 {distractionTime / 60:F1} 小时",
                Confidence = 0.9,
                Recommendation = "考虑设置娱乐应用的使用时间限制"
            });
        }
        
        return insights;
    }
    
    private List<TrendData> CalculateTrends(List<AppSession> sessions, DateTime startDate, DateTime endDate)
    {
        return sessions
            .GroupBy(s => s.StartTime.Date)
            .Select(g => new TrendData
            {
                Date = g.Key,
                Productivity = g.Average(s => GetAppProductivity(s.ProcessName)),
                ActiveTime = TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks)),
                FocusTime = TimeSpan.FromMinutes(g.Where(s => GetAppProductivity(s.ProcessName) > 0.7).Sum(s => s.Duration.TotalMinutes))
            })
            .OrderBy(t => t.Date)
            .ToList();
    }
    
    private List<HourlyProductivity> CalculateHourlyProductivity(List<AppSession> sessions)
    {
        return sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new HourlyProductivity
            {
                Hour = g.Key,
                Score = g.Average(s => GetAppProductivity(s.ProcessName)) * 100,
                PrimaryActivity = g.OrderByDescending(s => s.Duration).FirstOrDefault()?.ProcessName,
                Duration = TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks))
            })
            .OrderBy(h => h.Hour)
            .ToList();
    }
    
    private double CalculateDailyProductivityScore(List<AppSession> sessions, List<FocusSession> focusSessions)
    {
        if (!sessions.Any()) return 0;
        
        var appScore = sessions.Average(s => GetAppProductivity(s.ProcessName)) * 50;
        var focusScore = focusSessions.Any()
            ? Math.Min(focusSessions.Sum(f => f.Duration.TotalMinutes) / 240 * 50, 50)
            : 0;
        
        return appScore + focusScore;
    }
    
    private double CalculateDailyFocusScore(List<FocusSession> focusSessions)
    {
        if (!focusSessions.Any()) return 0;
        
        var deepWorkRatio = focusSessions.Count(f => f.IsDeepWork) / (double)focusSessions.Count;
        var avgIntensity = focusSessions.Average(f => f.IntensityScore);
        
        return (deepWorkRatio * 0.5 + avgIntensity * 0.5) * 100;
    }
    
    private double CalculateEfficiencyScore(List<AppSession> sessions)
    {
        if (!sessions.Any()) return 0;
        
        var productiveTime = sessions
            .Where(s => GetAppProductivity(s.ProcessName) > 0.5)
            .Sum(s => s.Duration.TotalMinutes);
        
        var totalTime = sessions.Sum(s => s.Duration.TotalMinutes);
        
        return totalTime > 0 ? productiveTime / totalTime * 100 : 0;
    }
    
    private double CalculateBalanceScore(List<AppSession> sessions)
    {
        if (!sessions.Any()) return 100;
        
        var categories = sessions
            .GroupBy(s => GetAppCategory(s.ProcessName))
            .Select(g => g.Sum(s => s.Duration.TotalMinutes))
            .ToList();
        
        if (categories.Count < 2) return 50;
        
        var total = categories.Sum();
        var idealRatio = 100.0 / categories.Count;
        
        var balanceScore = categories.Sum(c =>
        {
            var actualRatio = c / total * 100;
            return 100 - Math.Abs(actualRatio - idealRatio);
        }) / categories.Count;
        
        return Math.Max(0, balanceScore);
    }
    
    private double CalculateIntensityScore(FocusSession session)
    {
        var switchRate = session.SwitchCount / Math.Max(session.Duration.TotalMinutes, 1);
        return Math.Max(0, 1 - switchRate);
    }
    
    private double CalculateAppProductivityScore(string processName, List<AppSession> sessions)
    {
        var baseScore = GetAppProductivity(processName);
        var avgDuration = sessions.Average(s => s.Duration.TotalMinutes);
        var durationBonus = Math.Min(avgDuration / 30, 0.2);
        
        return Math.Min(baseScore + durationBonus, 1);
    }
    
    private double GetAppProductivity(string processName)
    {
        var name = processName.ToLowerInvariant();
        
        if (ProductiveApps.Any(p => name.Contains(p))) return 0.9;
        if (DistractionApps.Any(d => name.Contains(d))) return 0.2;
        
        return 0.5;
    }
    
    private string GetAppCategory(string processName)
    {
        var name = processName.ToLowerInvariant();
        
        foreach (var category in AppCategories)
        {
            if (name.Contains(category.Key.ToLowerInvariant()))
            {
                return category.Value;
            }
        }
        
        return "Other";
    }
    
    private List<string> GenerateAchievements(ProductivityReport report)
    {
        var achievements = new List<string>();
        
        if (report.DeepWorkTime.TotalHours >= 2)
            achievements.Add("深度工作超过2小时");
        
        if (report.FocusSessionCount >= 4)
            achievements.Add("完成4个专注时段");
        
        if (report.InterruptionCount <= 5)
            achievements.Add("干扰次数少于5次");
        
        if (report.OverallScore >= 80)
            achievements.Add("生产力评分超过80分");
        
        return achievements;
    }
    
    private List<string> GenerateImprovements(ProductivityReport report)
    {
        var improvements = new List<string>();
        
        if (report.FocusScore < 50)
            improvements.Add("尝试减少应用切换频率");
        
        if (report.DeepWorkTime.TotalHours < 1)
            improvements.Add("增加深度工作时间");
        
        if (report.InterruptionCount > 10)
            improvements.Add("减少工作中的干扰");
        
        if (report.BalanceScore < 50)
            improvements.Add("平衡不同类型的工作");
        
        return improvements;
    }
}
