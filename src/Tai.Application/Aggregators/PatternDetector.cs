using Tai.Core.Entities;
using Tai.Core.Interfaces;

namespace Tai.Application.Aggregators;

public class PatternDetector
{
    private readonly IRepository<AppSession> _appSessionRepo;
    private readonly IRepository<DailyPattern> _patternRepo;
    
    public PatternDetector(
        IRepository<AppSession> appSessionRepo,
        IRepository<DailyPattern> patternRepo)
    {
        _appSessionRepo = appSessionRepo;
        _patternRepo = patternRepo;
    }
    
    public async Task<List<UsagePattern>> DetectPatternsAsync(DateTime date)
    {
        var patterns = new List<UsagePattern>();
        
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime.Date == date.Date);
        
        var orderedSessions = sessions.OrderBy(s => s.StartTime).ToList();
        
        patterns.AddRange(DetectFrequentApps(orderedSessions));
        patterns.AddRange(DetectPeakHours(orderedSessions));
        patterns.AddRange(DetectAppSequences(orderedSessions));
        patterns.AddRange(DetectWorkPatterns(orderedSessions));
        patterns.AddRange(DetectIdlePatterns(orderedSessions));
        patterns.AddRange(DetectContextSwitches(orderedSessions));
        patterns.AddRange(DetectProductivityPatterns(orderedSessions));
        
        return patterns;
    }
    
    private List<UsagePattern> DetectFrequentApps(List<AppSession> sessions)
    {
        var patterns = new List<UsagePattern>();
        
        var appCounts = sessions
            .GroupBy(s => s.ProcessName)
            .Select(g => new { ProcessName = g.Key, Count = g.Count(), TotalDuration = g.Sum(s => s.Duration.TotalMinutes) })
            .Where(a => a.Count >= 3)
            .OrderByDescending(a => a.Count)
            .Take(5)
            .ToList();
        
        foreach (var app in appCounts)
        {
            patterns.Add(new UsagePattern
            {
                Type = PatternType.FrequentApp,
                Description = $"频繁使用 {app.ProcessName}",
                ProcessName = app.ProcessName,
                Frequency = app.Count,
                TotalDuration = TimeSpan.FromMinutes(app.TotalDuration),
                Confidence = Math.Min(app.Count / 10.0, 1.0)
            });
        }
        
        return patterns;
    }
    
    private List<UsagePattern> DetectPeakHours(List<AppSession> sessions)
    {
        var patterns = new List<UsagePattern>();
        
        var hourlyActivity = sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Duration = g.Sum(s => s.Duration.TotalMinutes) })
            .OrderByDescending(h => h.Duration)
            .Take(3)
            .ToList();
        
        foreach (var hour in hourlyActivity)
        {
            if (hour.Duration > 30)
            {
                patterns.Add(new UsagePattern
                {
                    Type = PatternType.PeakHour,
                    Description = $"高峰时段: {hour.Hour}:00 - {hour.Hour + 1}:00",
                    Hour = hour.Hour,
                    TotalDuration = TimeSpan.FromMinutes(hour.Duration),
                    Confidence = Math.Min(hour.Duration / 60.0, 1.0)
                });
            }
        }
        
        return patterns;
    }
    
    private List<UsagePattern> DetectAppSequences(List<AppSession> sessions)
    {
        var patterns = new List<UsagePattern>();
        
        var sequences = new Dictionary<string, int>();
        
        for (var i = 0; i < sessions.Count - 1; i++)
        {
            var sequence = $"{sessions[i].ProcessName} -> {sessions[i + 1].ProcessName}";
            
            if (!sequences.TryAdd(sequence, 1))
            {
                sequences[sequence]++;
            }
        }
        
        var frequentSequences = sequences
            .Where(s => s.Value >= 3)
            .OrderByDescending(s => s.Value)
            .Take(5)
            .ToList();
        
        foreach (var seq in frequentSequences)
        {
            var parts = seq.Key.Split(" -> ");
            patterns.Add(new UsagePattern
            {
                Type = PatternType.AppSequence,
                Description = $"常用切换: {seq.Key}",
                ProcessName = parts[0],
                RelatedProcessName = parts[1],
                Frequency = seq.Value,
                Confidence = Math.Min(seq.Value / 5.0, 1.0)
            });
        }
        
        return patterns;
    }
    
    private List<UsagePattern> DetectWorkPatterns(List<AppSession> sessions)
    {
        var patterns = new List<UsagePattern>();
        
        var morningSessions = sessions.Where(s => s.StartTime.Hour >= 6 && s.StartTime.Hour < 12).ToList();
        var afternoonSessions = sessions.Where(s => s.StartTime.Hour >= 12 && s.StartTime.Hour < 18).ToList();
        var eveningSessions = sessions.Where(s => s.StartTime.Hour >= 18 && s.StartTime.Hour < 24).ToList();
        
        if (morningSessions.Any())
        {
            var topMorningApp = morningSessions
                .GroupBy(s => s.ProcessName)
                .OrderByDescending(g => g.Sum(s => s.Duration.TotalMinutes))
                .First();
            
            patterns.Add(new UsagePattern
            {
                Type = PatternType.WorkPattern,
                Description = $"上午主要使用: {topMorningApp.Key}",
                ProcessName = topMorningApp.Key,
                TotalDuration = TimeSpan.FromMinutes(topMorningApp.Sum(s => s.Duration.TotalMinutes)),
                Confidence = 0.7
            });
        }
        
        return patterns;
    }
    
    private List<UsagePattern> DetectIdlePatterns(List<AppSession> sessions)
    {
        var patterns = new List<UsagePattern>();
        
        var gaps = new List<TimeSpan>();
        
        for (var i = 1; i < sessions.Count; i++)
        {
            var prevEndTime = sessions[i - 1].EndTime ?? sessions[i - 1].StartTime;
            var gap = sessions[i].StartTime - prevEndTime;
            if (gap > TimeSpan.FromMinutes(5))
            {
                gaps.Add(gap);
            }
        }
        
        if (gaps.Any())
        {
            var avgGap = gaps.Average(g => g.TotalMinutes);
            var maxGap = gaps.Max(g => g.TotalMinutes);
            
            patterns.Add(new UsagePattern
            {
                Type = PatternType.IdlePattern,
                Description = $"平均休息间隔: {avgGap:F0} 分钟",
                Frequency = gaps.Count,
                TotalDuration = TimeSpan.FromMinutes(gaps.Sum(g => g.TotalMinutes)),
                Confidence = Math.Min(gaps.Count / 5.0, 1.0)
            });
            
            if (maxGap > 60)
            {
                patterns.Add(new UsagePattern
                {
                    Type = PatternType.LongBreak,
                    Description = $"最长休息时间: {maxGap:F0} 分钟",
                    TotalDuration = TimeSpan.FromMinutes(maxGap),
                    Confidence = 0.8
                });
            }
        }
        
        return patterns;
    }
    
    private List<UsagePattern> DetectContextSwitches(List<AppSession> sessions)
    {
        var patterns = new List<UsagePattern>();
        
        var switches = 0;
        var rapidSwitches = 0;
        var lastApp = string.Empty;
        var lastSwitchTime = DateTime.MinValue;
        
        foreach (var session in sessions)
        {
            if (session.ProcessName != lastApp)
            {
                switches++;
                
                if (lastSwitchTime != DateTime.MinValue)
                {
                    var timeSinceLastSwitch = session.StartTime - lastSwitchTime;
                    if (timeSinceLastSwitch < TimeSpan.FromMinutes(2))
                    {
                        rapidSwitches++;
                    }
                }
                
                lastSwitchTime = session.StartTime;
                lastApp = session.ProcessName;
            }
        }
        
        if (switches > 0)
        {
            var rapidSwitchRatio = (double)rapidSwitches / switches;
            
            patterns.Add(new UsagePattern
            {
                Type = PatternType.ContextSwitch,
                Description = $"应用切换次数: {switches}",
                Frequency = switches,
                Confidence = 0.9
            });
            
            if (rapidSwitchRatio > 0.3)
            {
                patterns.Add(new UsagePattern
                {
                    Type = PatternType.RapidSwitch,
                    Description = $"快速切换比例: {rapidSwitchRatio:P0}",
                    Frequency = rapidSwitches,
                    Confidence = 0.85
                });
            }
        }
        
        return patterns;
    }
    
    private List<UsagePattern> DetectProductivityPatterns(List<AppSession> sessions)
    {
        var patterns = new List<UsagePattern>();
        
        var productiveApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "code", "devenv", "idea64", "pycharm64", "visualstudio", "vscode",
            "excel", "word", "powerpnt", "onenote", "notepad++"
        };
        
        var productiveTime = sessions
            .Where(s => productiveApps.Any(p => s.ProcessName.ToLowerInvariant().Contains(p)))
            .Sum(s => s.Duration.TotalMinutes);
        
        var totalTime = sessions.Sum(s => s.Duration.TotalMinutes);
        
        if (totalTime > 0)
        {
            var productivityRatio = productiveTime / totalTime;
            
            patterns.Add(new UsagePattern
            {
                Type = PatternType.ProductivityRatio,
                Description = $"生产力应用占比: {productivityRatio:P0}",
                TotalDuration = TimeSpan.FromMinutes(productiveTime),
                Confidence = 0.95
            });
            
            if (productivityRatio > 0.7)
            {
                patterns.Add(new UsagePattern
                {
                    Type = PatternType.HighProductivity,
                    Description = "高效工作日",
                    Confidence = 0.9
                });
            }
            else if (productivityRatio < 0.3)
            {
                patterns.Add(new UsagePattern
                {
                    Type = PatternType.LowProductivity,
                    Description = "低效工作日",
                    Confidence = 0.9
                });
            }
        }
        
        return patterns;
    }
    
    public async Task SaveDailyPatternAsync(DateTime date)
    {
        var patterns = await DetectPatternsAsync(date);
        
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime.Date == date.Date);
        
        var orderedSessions = sessions.OrderBy(s => s.StartTime).ToList();
        
        var dailyPattern = new DailyPattern
        {
            Date = date.Date,
            Patterns = patterns,
            TotalActiveTime = TimeSpan.FromTicks(orderedSessions.Sum(s => s.Duration.Ticks)),
            FirstActivityTime = orderedSessions.FirstOrDefault()?.StartTime.TimeOfDay ?? TimeSpan.Zero,
            LastActivityTime = orderedSessions.LastOrDefault()?.EndTime?.TimeOfDay ?? TimeSpan.Zero,
            InterruptionCount = patterns.FirstOrDefault(p => p.Type == PatternType.ContextSwitch)?.Frequency ?? 0,
            ProductivityScore = CalculateProductivityScore(patterns),
            FocusBlocks = DetectFocusBlocks(orderedSessions),
            Breaks = DetectBreaks(orderedSessions),
            HourlyActivity = CalculateHourlyActivity(orderedSessions)
        };
        
        var existingPattern = await _patternRepo.FindAsync(p => p.Date == date.Date);
        var existing = existingPattern.FirstOrDefault();
        
        if (existing == null)
        {
            await _patternRepo.AddAsync(dailyPattern);
        }
        else
        {
            existing.Patterns = dailyPattern.Patterns;
            existing.TotalActiveTime = dailyPattern.TotalActiveTime;
            existing.FirstActivityTime = dailyPattern.FirstActivityTime;
            existing.LastActivityTime = dailyPattern.LastActivityTime;
            existing.InterruptionCount = dailyPattern.InterruptionCount;
            existing.ProductivityScore = dailyPattern.ProductivityScore;
            existing.FocusBlocks = dailyPattern.FocusBlocks;
            existing.Breaks = dailyPattern.Breaks;
            existing.HourlyActivity = dailyPattern.HourlyActivity;
            await _patternRepo.UpdateAsync(existing);
        }
    }
    
    private double CalculateProductivityScore(List<UsagePattern> patterns)
    {
        var productivityPattern = patterns.FirstOrDefault(p => p.Type == PatternType.ProductivityRatio);
        if (productivityPattern == null) return 0;
        
        var totalTime = patterns.FirstOrDefault(p => p.Type == PatternType.FrequentApp)?.TotalDuration.TotalMinutes ?? 0;
        var switchCount = patterns.FirstOrDefault(p => p.Type == PatternType.ContextSwitch)?.Frequency ?? 0;
        
        var baseScore = productivityPattern.TotalDuration.TotalMinutes / Math.Max(totalTime, 1) * 100;
        var switchPenalty = Math.Min(switchCount / 50.0 * 20, 20);
        
        return Math.Max(0, Math.Min(100, baseScore - switchPenalty));
    }
    
    private List<FocusBlock> DetectFocusBlocks(List<AppSession> sessions)
    {
        var blocks = new List<FocusBlock>();
        
        if (!sessions.Any()) return blocks;
        
        var currentBlock = new FocusBlock
        {
            StartTime = sessions[0].StartTime,
            PrimaryActivity = sessions[0].ProcessName
        };
        
        var appCount = new Dictionary<string, int>();
        var switches = 0;
        var lastApp = sessions[0].ProcessName;
        
        foreach (var session in sessions)
        {
            var timeInBlock = session.StartTime - currentBlock.StartTime;
            
            if (timeInBlock > TimeSpan.FromMinutes(90) || switches > 5)
            {
                currentBlock.Duration = session.StartTime - currentBlock.StartTime;
                currentBlock.IntensityScore = CalculateIntensityScore(appCount, switches, currentBlock.Duration);
                currentBlock.InterruptionCount = switches;
                
                if (currentBlock.Duration.TotalMinutes >= 15)
                {
                    blocks.Add(currentBlock);
                }
                
                currentBlock = new FocusBlock
                {
                    StartTime = session.StartTime,
                    PrimaryActivity = session.ProcessName
                };
                appCount.Clear();
                switches = 0;
            }
            
            if (!appCount.TryAdd(session.ProcessName, 1))
            {
                appCount[session.ProcessName]++;
            }
            
            if (session.ProcessName != lastApp)
            {
                switches++;
                lastApp = session.ProcessName;
            }
        }
        
        currentBlock.Duration = (sessions.Last().EndTime ?? sessions.Last().StartTime) - currentBlock.StartTime;
        currentBlock.IntensityScore = CalculateIntensityScore(appCount, switches, currentBlock.Duration);
        currentBlock.InterruptionCount = switches;
        
        if (currentBlock.Duration.TotalMinutes >= 15)
        {
            blocks.Add(currentBlock);
        }
        
        return blocks;
    }
    
    private double CalculateIntensityScore(Dictionary<string, int> appCount, int switches, TimeSpan duration)
    {
        var dominantAppRatio = appCount.Values.Max() / (double)appCount.Values.Sum();
        var switchRate = switches / Math.Max(duration.TotalMinutes, 1);
        
        return dominantAppRatio * 0.6 + Math.Max(0, 1 - switchRate / 5) * 0.4;
    }
    
    private List<BreakPattern> DetectBreaks(List<AppSession> sessions)
    {
        var breaks = new List<BreakPattern>();
        
        for (var i = 1; i < sessions.Count; i++)
        {
            var prevEndTime = sessions[i - 1].EndTime ?? sessions[i - 1].StartTime;
            var gap = sessions[i].StartTime - prevEndTime;
            
            if (gap > TimeSpan.FromMinutes(5))
            {
                breaks.Add(new BreakPattern
                {
                    StartTime = prevEndTime,
                    Duration = gap,
                    Type = ClassifyBreak(gap)
                });
            }
        }
        
        return breaks;
    }
    
    private BreakType ClassifyBreak(TimeSpan duration)
    {
        return duration.TotalMinutes switch
        {
            < 15 => BreakType.Short,
            < 30 => BreakType.Medium,
            < 60 => BreakType.Long,
            _ => BreakType.Meal
        };
    }
    
    private Dictionary<int, ActivityLevel> CalculateHourlyActivity(List<AppSession> sessions)
    {
        var hourlyActivity = new Dictionary<int, ActivityLevel>();
        
        var grouped = sessions.GroupBy(s => s.StartTime.Hour);
        
        foreach (var group in grouped)
        {
            var dominantApp = group
                .GroupBy(s => s.ProcessName)
                .OrderByDescending(g => g.Sum(s => s.Duration.TotalMinutes))
                .FirstOrDefault();
            
            hourlyActivity[group.Key] = new ActivityLevel
            {
                Hour = group.Key,
                Score = group.Sum(s => s.Duration.TotalMinutes) / 60.0 * 100,
                DominantActivity = dominantApp?.Key
            };
        }
        
        return hourlyActivity;
    }
    
    public async Task<WeeklyPatternSummary> GetWeeklySummaryAsync(DateTime weekStart)
    {
        var summary = new WeeklyPatternSummary
        {
            WeekStart = weekStart
        };
        
        for (var i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var patterns = await DetectPatternsAsync(date);
            
            summary.DailyPatterns[date] = patterns;
            
            var productivityPattern = patterns.FirstOrDefault(p => p.Type == PatternType.ProductivityRatio);
            if (productivityPattern != null)
            {
                summary.DailyProductivity[date] = productivityPattern.TotalDuration.TotalMinutes;
            }
        }
        
        summary.TopApps = summary.DailyPatterns.Values
            .SelectMany(p => p)
            .Where(p => p.Type == PatternType.FrequentApp)
            .GroupBy(p => p.ProcessName)
            .Select(g => new { App = g.Key, Count = g.Count(), Duration = g.Sum(p => p.TotalDuration.TotalMinutes) })
            .OrderByDescending(a => a.Duration)
            .Take(5)
            .ToDictionary(a => a.App!, a => a.Duration);
        
        summary.PeakHours = summary.DailyPatterns.Values
            .SelectMany(p => p)
            .Where(p => p.Type == PatternType.PeakHour && p.Hour.HasValue)
            .GroupBy(p => p.Hour!.Value)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(h => h.Count)
            .Select(h => h.Hour)
            .Take(3)
            .ToList();
        
        return summary;
    }
}

public class WeeklyPatternSummary
{
    public DateTime WeekStart { get; set; }
    public Dictionary<DateTime, List<UsagePattern>> DailyPatterns { get; set; } = new();
    public Dictionary<DateTime, double> DailyProductivity { get; set; } = new();
    public Dictionary<string, double> TopApps { get; set; } = new();
    public List<int> PeakHours { get; set; } = [];
}
