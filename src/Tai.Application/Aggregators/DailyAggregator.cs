using Tai.Core.Entities;
using Tai.Core.Interfaces;

namespace Tai.Application.Aggregators;

public class DailyAggregator
{
    private readonly IRepository<AppSession> _appSessionRepo;
    private readonly IRepository<KeyboardSession> _keyboardSessionRepo;
    private readonly IRepository<MouseSession> _mouseSessionRepo;
    
    public DailyAggregator(
        IRepository<AppSession> appSessionRepo,
        IRepository<KeyboardSession> keyboardSessionRepo,
        IRepository<MouseSession> mouseSessionRepo)
    {
        _appSessionRepo = appSessionRepo;
        _keyboardSessionRepo = keyboardSessionRepo;
        _mouseSessionRepo = mouseSessionRepo;
    }
    
    public async Task<DailySummary> GetDailySummaryAsync(DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);
        
        var appSessions = await _appSessionRepo.FindAsync(
            s => s.StartTime >= startOfDay && s.StartTime < endOfDay);
        
        var keyboardSessions = await _keyboardSessionRepo.FindAsync(
            s => s.Date == date.Date);
        
        var mouseSessions = await _mouseSessionRepo.FindAsync(
            s => s.Date == date.Date);
        
        var totalActiveTime = appSessions.Sum(s => s.Duration.TotalSeconds);
        var totalKeyPresses = keyboardSessions.Sum(s => s.TotalKeyPresses);
        var totalMouseClicks = mouseSessions.Sum(s => s.TotalClicks);
        var totalMouseDistance = mouseSessions.Sum(s => s.TotalDistance);
        
        var appBreakdown = appSessions
            .GroupBy(s => s.ProcessName)
            .Select(g => new AppUsageSummary
            {
                ProcessName = g.Key,
                AppName = g.First().AppName ?? g.Key,
                TotalDuration = TimeSpan.FromSeconds(g.Sum(s => s.Duration.TotalSeconds)),
                SessionCount = g.Count(),
                Category = g.First().Category
            })
            .OrderByDescending(a => a.TotalDuration)
            .ToList();
        
        var hourlyBreakdown = Enumerable.Range(0, 24)
            .Select(hour => new HourlyActivity
            {
                Hour = hour,
                KeyPresses = keyboardSessions.Where(s => s.Hour == hour).Sum(s => s.TotalKeyPresses),
                MouseClicks = mouseSessions.Where(s => s.Hour == hour).Sum(s => s.TotalClicks),
                ActiveTime = TimeSpan.FromSeconds(
                    appSessions.Where(s => s.StartTime.Hour == hour).Sum(s => s.Duration.TotalSeconds))
            })
            .ToList();
        
        return new DailySummary
        {
            Date = date.Date,
            TotalActiveTime = TimeSpan.FromSeconds(totalActiveTime),
            TotalKeyPresses = totalKeyPresses,
            TotalMouseClicks = totalMouseClicks,
            TotalMouseDistance = totalMouseDistance,
            AppBreakdown = appBreakdown,
            HourlyBreakdown = hourlyBreakdown,
            UniqueAppsCount = appBreakdown.Count
        };
    }
    
    public async Task<WeeklySummary> GetWeeklySummaryAsync(DateTime weekStart)
    {
        var summaries = new List<DailySummary>();
        
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            summaries.Add(await GetDailySummaryAsync(day));
        }
        
        return new WeeklySummary
        {
            WeekStart = weekStart,
            DailySummaries = summaries,
            TotalActiveTime = TimeSpan.FromSeconds(summaries.Sum(s => s.TotalActiveTime.TotalSeconds)),
            TotalKeyPresses = summaries.Sum(s => s.TotalKeyPresses),
            TotalMouseClicks = summaries.Sum(s => s.TotalMouseClicks),
            AverageDailyActiveTime = TimeSpan.FromSeconds(summaries.Average(s => s.TotalActiveTime.TotalSeconds))
        };
    }
}

public class DailySummary
{
    public DateTime Date { get; set; }
    public TimeSpan TotalActiveTime { get; set; }
    public int TotalKeyPresses { get; set; }
    public int TotalMouseClicks { get; set; }
    public double TotalMouseDistance { get; set; }
    public int UniqueAppsCount { get; set; }
    public List<AppUsageSummary> AppBreakdown { get; set; } = [];
    public List<HourlyActivity> HourlyBreakdown { get; set; } = [];
}

public class WeeklySummary
{
    public DateTime WeekStart { get; set; }
    public List<DailySummary> DailySummaries { get; set; } = [];
    public TimeSpan TotalActiveTime { get; set; }
    public int TotalKeyPresses { get; set; }
    public int TotalMouseClicks { get; set; }
    public TimeSpan AverageDailyActiveTime { get; set; }
}

public class AppUsageSummary
{
    public string ProcessName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public TimeSpan TotalDuration { get; set; }
    public int SessionCount { get; set; }
    public string? Category { get; set; }
}

public class HourlyActivity
{
    public int Hour { get; set; }
    public int KeyPresses { get; set; }
    public int MouseClicks { get; set; }
    public TimeSpan ActiveTime { get; set; }
}
