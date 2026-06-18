using PChabit.Core.Entities;
using PChabit.Core.Interfaces;

namespace PChabit.Application.Aggregators;

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
            s => s.Date >= date.Date && s.Date < date.Date.AddDays(1));

        var mouseSessions = await _mouseSessionRepo.FindAsync(
            s => s.Date >= date.Date && s.Date < date.Date.AddDays(1));
        
        return BuildDailySummary(date, appSessions, keyboardSessions, mouseSessions);
    }
    
    public async Task<WeeklySummary> GetWeeklySummaryAsync(DateTime weekStart)
    {
        var weekEnd = weekStart.AddDays(7);
        
        // 一次性查询整周数据，避免 N+1
        var weekAppSessions = await _appSessionRepo.FindAsync(
            s => s.StartTime >= weekStart && s.StartTime < weekEnd);
        
        var weekKeyboardSessions = await _keyboardSessionRepo.FindAsync(
            s => s.Date >= weekStart.Date && s.Date < weekEnd.Date);
        
        var weekMouseSessions = await _mouseSessionRepo.FindAsync(
            s => s.Date >= weekStart.Date && s.Date < weekEnd.Date);
        
        // 按日期分组
        var appByDate = weekAppSessions.GroupBy(s => s.StartTime.Date).ToDictionary(g => g.Key, g => g.ToList());
        var keyboardByDate = weekKeyboardSessions.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.ToList());
        var mouseByDate = weekMouseSessions.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.ToList());
        
        var summaries = new List<DailySummary>();
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i).Date;
            var dayAppSessions = appByDate.GetValueOrDefault(day, []);
            var dayKeyboardSessions = keyboardByDate.GetValueOrDefault(day, []);
            var dayMouseSessions = mouseByDate.GetValueOrDefault(day, []);
            
            summaries.Add(BuildDailySummary(day, dayAppSessions, dayKeyboardSessions, dayMouseSessions));
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
    
    private static DailySummary BuildDailySummary(
        DateTime date,
        List<AppSession> appSessions,
        List<KeyboardSession> keyboardSessions,
        List<MouseSession> mouseSessions)
    {
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
        
        // 优化：使用 GroupBy 替代 24 次线性扫描
        var keyboardByHour = keyboardSessions.GroupBy(s => s.Hour).ToDictionary(g => g.Key, g => g.Sum(s => s.TotalKeyPresses));
        var mouseByHour = mouseSessions.GroupBy(s => s.Hour).ToDictionary(g => g.Key, g => g.Sum(s => s.TotalClicks));
        var appByHour = appSessions.GroupBy(s => s.StartTime.Hour).ToDictionary(g => g.Key, g => g.Sum(s => s.Duration.TotalSeconds));
        
        var hourlyBreakdown = Enumerable.Range(0, 24)
            .Select(hour => new HourlyActivity
            {
                Hour = hour,
                KeyPresses = keyboardByHour.GetValueOrDefault(hour),
                MouseClicks = mouseByHour.GetValueOrDefault(hour),
                ActiveTime = TimeSpan.FromSeconds(appByHour.GetValueOrDefault(hour))
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
