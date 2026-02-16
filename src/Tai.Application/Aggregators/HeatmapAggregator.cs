using System.Diagnostics;
using Serilog;
using Tai.Core.Entities;
using Tai.Core.Interfaces;

namespace Tai.Application.Aggregators;

public class HeatmapAggregator
{
    private readonly IAppSessionRepository _appSessionRepo;
    private readonly IKeyboardSessionRepository _keyboardSessionRepo;
    private readonly IMouseSessionRepository _mouseSessionRepo;
    private const int MaxWeeklySessions = 1000;
    private const int MaxMonthlySessions = 5000;

    public HeatmapAggregator(
        IAppSessionRepository appSessionRepo,
        IKeyboardSessionRepository keyboardSessionRepo,
        IMouseSessionRepository mouseSessionRepo)
    {
        Log.Information("[HeatmapAggregator] ===== 构造函数开始 =====");
        
        _appSessionRepo = appSessionRepo;
        _keyboardSessionRepo = keyboardSessionRepo;
        _mouseSessionRepo = mouseSessionRepo;
        
        Log.Information("[HeatmapAggregator] ===== 构造函数完成 =====");
    }

    public async Task<List<HeatmapCell>> GetWeeklyHeatmapDataAsync(DateTime weekStart)
    {
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var initialMemory = process.WorkingSet64 / 1024 / 1024;
        
        Log.Information("[HeatmapAggregator] ===== 开始加载周热力图 =====");
        Log.Information("[HeatmapAggregator] 周起始日期: {WeekStart}", weekStart.ToString("yyyy-MM-dd"));
        Log.Information("[HeatmapAggregator] 初始内存: {Memory} MB", initialMemory);
        
        var heatmapCells = new List<HeatmapCell>(168);
        var weekEnd = weekStart.AddDays(7);

        try
        {
            Log.Information("[HeatmapAggregator] 步骤 1/5: 查询应用会话...");
            var dbStopwatch = Stopwatch.StartNew();
            
            Log.Information("[HeatmapAggregator] 查询 AppSessions...");
            var appSessionsTask = _appSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);
            
            Log.Information("[HeatmapAggregator] 查询 KeyboardSessions...");
            var keyboardSessionsTask = _keyboardSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);
            
            Log.Information("[HeatmapAggregator] 查询 MouseSessions...");
            var mouseSessionsTask = _mouseSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);
            
            Log.Information("[HeatmapAggregator] 等待所有查询完成...");
            await Task.WhenAll(appSessionsTask, keyboardSessionsTask, mouseSessionsTask);
            Log.Information("[HeatmapAggregator] 所有查询完成");
            
            var allAppSessions = appSessionsTask.Result.Take(MaxWeeklySessions).ToList();
            var allKeyboardSessions = keyboardSessionsTask.Result.Take(MaxWeeklySessions).ToList();
            var allMouseSessions = mouseSessionsTask.Result.Take(MaxWeeklySessions).ToList();
            
            dbStopwatch.Stop();
            Log.Information("[HeatmapAggregator] 数据库查询完成 - 应用:{AppCount}, 键盘:{KeyCount}, 鼠标:{MouseCount}, 耗时:{Elapsed}ms", 
                allAppSessions.Count, allKeyboardSessions.Count, allMouseSessions.Count, dbStopwatch.ElapsedMilliseconds);

            if (allAppSessions.Count > 0)
            {
                var sample = allAppSessions.First();
                Log.Information("[HeatmapAggregator] [诊断] 样本会话 - ProcessName:{ProcessName}, StartTime:{StartTime}, EndTime:{EndTime}, Duration:{Duration}, DurationTicks:{DurationTicks}",
                    sample.ProcessName, sample.StartTime, sample.EndTime, sample.Duration, sample.Duration.Ticks);
                
                var totalDuration = allAppSessions.Sum(s => s.Duration.TotalSeconds);
                var sessionsWithEndTime = allAppSessions.Count(s => s.EndTime.HasValue);
                var sessionsWithDuration = allAppSessions.Count(s => s.Duration.TotalSeconds > 0);
                Log.Information("[HeatmapAggregator] [诊断] 总时长:{TotalDuration}s, 有EndTime的会话:{WithEndTime}, 有Duration的会话:{WithDuration}",
                    totalDuration, sessionsWithEndTime, sessionsWithDuration);
                
                var sampleWithDuration = allAppSessions.FirstOrDefault(s => s.Duration.TotalSeconds > 0);
                if (sampleWithDuration != null)
                {
                    Log.Information("[HeatmapAggregator] [诊断] 有Duration的样本 - ProcessName:{ProcessName}, Duration:{Duration}, EndTime:{EndTime}",
                        sampleWithDuration.ProcessName, sampleWithDuration.Duration, sampleWithDuration.EndTime);
                }
            }

            Log.Information("[HeatmapAggregator] 步骤 2/5: 预处理数据...");
            var processStopwatch = Stopwatch.StartNew();

            var appSessionsByDayHour = allAppSessions
                .GroupBy(s => (Date: DateOnly.FromDateTime(s.StartTime), Hour: s.StartTime.Hour))
                .ToDictionary(g => g.Key, g => g.ToList());

            var keyboardSessionsByDayHour = allKeyboardSessions
                .GroupBy(s => (Date: DateOnly.FromDateTime(s.Date), Hour: s.Hour))
                .ToDictionary(g => g.Key, g => g.ToList());

            var mouseSessionsByDayHour = allMouseSessions
                .GroupBy(s => (Date: DateOnly.FromDateTime(s.Date), Hour: s.Hour))
                .ToDictionary(g => g.Key, g => g.ToList());

            processStopwatch.Stop();
            Log.Information("[HeatmapAggregator] 数据预处理完成 - 耗时:{Elapsed}ms", processStopwatch.ElapsedMilliseconds);

            Log.Information("[HeatmapAggregator] 步骤 3/5: 生成热力图单元格...");
            processStopwatch.Restart();

            for (var dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                var currentDate = DateOnly.FromDateTime(weekStart.Date.AddDays(dayOffset));

                for (var hour = 0; hour < 24; hour++)
                {
                    var key = (Date: currentDate, Hour: hour);

                    List<AppSession> hourAppSessions = [];
                    if (appSessionsByDayHour.TryGetValue(key, out var sessions))
                    {
                        hourAppSessions = sessions;
                    }

                    List<KeyboardSession> hourKeyboardSessions = [];
                    if (keyboardSessionsByDayHour.TryGetValue(key, out var keyboardSessions))
                    {
                        hourKeyboardSessions = keyboardSessions;
                    }

                    List<MouseSession> hourMouseSessions = [];
                    if (mouseSessionsByDayHour.TryGetValue(key, out var mouseSessions))
                    {
                        hourMouseSessions = mouseSessions;
                    }

                    var appUsage = TimeSpan.FromSeconds(hourAppSessions.Sum(s => s.Duration.TotalSeconds));
                    var keyPresses = hourKeyboardSessions.Sum(s => s.TotalKeyPresses);
                    var mouseClicks = hourMouseSessions.Sum(s => s.TotalClicks);

                    var topApps = hourAppSessions
                        .GroupBy(s => s.AppName ?? s.ProcessName)
                        .OrderByDescending(g => g.Sum(s => s.Duration.TotalSeconds))
                        .Take(3)
                        .Select(g => g.Key ?? "Unknown")
                        .ToList();

                    var activityScore = CalculateActivityScore(appUsage, keyPresses, mouseClicks);

                    heatmapCells.Add(new HeatmapCell
                    {
                        Date = currentDate.ToDateTime(TimeOnly.MinValue),
                        Hour = hour,
                        DayOfWeek = currentDate.DayOfWeek,
                        ActivityScore = activityScore,
                        AppUsage = appUsage,
                        KeyPresses = keyPresses,
                        MouseClicks = mouseClicks,
                        TopApps = topApps
                    });
                }
            }

            processStopwatch.Stop();
            Log.Information("[HeatmapAggregator] 单元格生成完成 - 耗时:{Elapsed}ms, 数量:{Count}", 
                processStopwatch.ElapsedMilliseconds, heatmapCells.Count);

            stopwatch.Stop();
            var finalMemory = process.WorkingSet64 / 1024 / 1024;
            var memoryDelta = finalMemory - initialMemory;
            
            Log.Information("[HeatmapAggregator] 周热力图加载完成 - 总耗时:{TotalElapsed}ms, 最终内存:{FinalMemory} MB, 内存变化:{MemoryDelta} MB",
                stopwatch.ElapsedMilliseconds, finalMemory, memoryDelta);
            
            return heatmapCells;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error(ex, "[HeatmapAggregator] 加载周热力图失败 - 耗时:{Elapsed}ms", stopwatch.ElapsedMilliseconds);
            return heatmapCells;
        }
    }

    public async Task<List<DailyHeatmapCell>> GetMonthlyHeatmapDataAsync(DateTime monthStart)
    {
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var initialMemory = process.WorkingSet64 / 1024 / 1024;
        
        Log.Information("[HeatmapAggregator] ===== 开始加载月热力图 =====");
        Log.Information("[HeatmapAggregator] 月份: {Month}", monthStart.ToString("yyyy-MM"));
        Log.Information("[HeatmapAggregator] 初始内存: {Memory} MB", initialMemory);
        
        var year = monthStart.Year;
        var month = monthStart.Month;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var heatmapCells = new List<DailyHeatmapCell>(daysInMonth);
        var monthEnd = monthStart.AddMonths(1);

        try
        {
            Log.Information("[HeatmapAggregator] 步骤 1/4: 查询应用会话...");
            var dbStopwatch = Stopwatch.StartNew();
            
            Log.Information("[HeatmapAggregator] 查询 AppSessions...");
            var appSessionsTask = _appSessionRepo.GetByDateRangeAsync(monthStart, monthEnd);
            
            Log.Information("[HeatmapAggregator] 查询 KeyboardSessions...");
            var keyboardSessionsTask = _keyboardSessionRepo.GetByDateRangeAsync(monthStart, monthEnd);
            
            Log.Information("[HeatmapAggregator] 查询 MouseSessions...");
            var mouseSessionsTask = _mouseSessionRepo.GetByDateRangeAsync(monthStart, monthEnd);
            
            Log.Information("[HeatmapAggregator] 等待所有查询完成...");
            await Task.WhenAll(appSessionsTask, keyboardSessionsTask, mouseSessionsTask);
            Log.Information("[HeatmapAggregator] 所有查询完成");
            
            var allAppSessions = appSessionsTask.Result.Take(MaxMonthlySessions).ToList();
            var allKeyboardSessions = keyboardSessionsTask.Result.Take(MaxMonthlySessions).ToList();
            var allMouseSessions = mouseSessionsTask.Result.Take(MaxMonthlySessions).ToList();
            
            dbStopwatch.Stop();
            Log.Information("[HeatmapAggregator] 数据库查询完成 - 应用:{AppCount}, 键盘:{KeyCount}, 鼠标:{MouseCount}, 耗时:{Elapsed}ms", 
                allAppSessions.Count, allKeyboardSessions.Count, allMouseSessions.Count, dbStopwatch.ElapsedMilliseconds);

            if (allAppSessions.Count > 0)
            {
                var sample = allAppSessions.First();
                Log.Information("[HeatmapAggregator] [诊断-月] 样本会话 - ProcessName:{ProcessName}, StartTime:{StartTime}, EndTime:{EndTime}, Duration:{Duration}, DurationTicks:{DurationTicks}",
                    sample.ProcessName, sample.StartTime, sample.EndTime, sample.Duration, sample.Duration.Ticks);
                
                var totalDuration = allAppSessions.Sum(s => s.Duration.TotalSeconds);
                var sessionsWithEndTime = allAppSessions.Count(s => s.EndTime.HasValue);
                var sessionsWithDuration = allAppSessions.Count(s => s.Duration.TotalSeconds > 0);
                Log.Information("[HeatmapAggregator] [诊断-月] 总时长:{TotalDuration}s, 有EndTime的会话:{WithEndTime}, 有Duration的会话:{WithDuration}",
                    totalDuration, sessionsWithEndTime, sessionsWithDuration);
            }

            Log.Information("[HeatmapAggregator] 步骤 2/4: 预处理数据...");
            var processStopwatch = Stopwatch.StartNew();

            var appSessionsByDay = allAppSessions
                .GroupBy(s => DateOnly.FromDateTime(s.StartTime))
                .ToDictionary(g => g.Key, g => g.ToList());

            var keyboardSessionsByDay = allKeyboardSessions
                .GroupBy(s => DateOnly.FromDateTime(s.Date))
                .ToDictionary(g => g.Key, g => g.ToList());

            var mouseSessionsByDay = allMouseSessions
                .GroupBy(s => DateOnly.FromDateTime(s.Date))
                .ToDictionary(g => g.Key, g => g.ToList());

            processStopwatch.Stop();
            Log.Information("[HeatmapAggregator] 数据预处理完成 - 耗时:{Elapsed}ms", processStopwatch.ElapsedMilliseconds);

            Log.Information("[HeatmapAggregator] 步骤 3/4: 生成热力图单元格...");
            processStopwatch.Restart();

            var firstDayOfWeek = (int)monthStart.DayOfWeek;
            if (firstDayOfWeek == 0) firstDayOfWeek = 7;
            var leadingEmptyDays = firstDayOfWeek - 1;
            
            for (var i = 0; i < leadingEmptyDays; i++)
            {
                heatmapCells.Add(new DailyHeatmapCell
                {
                    Date = DateTime.MinValue,
                    ActivityScore = -1,
                    TotalUsage = TimeSpan.Zero,
                    KeyPresses = 0,
                    MouseClicks = 0,
                    HasData = false
                });
            }

            for (var day = 1; day <= daysInMonth; day++)
            {
                var currentDate = new DateOnly(year, month, day);

                List<AppSession> dayAppSessions = [];
                if (appSessionsByDay.TryGetValue(currentDate, out var sessions))
                {
                    dayAppSessions = sessions;
                }

                List<KeyboardSession> dayKeyboardSessions = [];
                if (keyboardSessionsByDay.TryGetValue(currentDate, out var keyboardSessions))
                {
                    dayKeyboardSessions = keyboardSessions;
                }

                List<MouseSession> dayMouseSessions = [];
                if (mouseSessionsByDay.TryGetValue(currentDate, out var mouseSessions))
                {
                    dayMouseSessions = mouseSessions;
                }

                var totalUsage = TimeSpan.FromSeconds(dayAppSessions.Sum(s => s.Duration.TotalSeconds));
                var keyPresses = dayKeyboardSessions.Sum(s => s.TotalKeyPresses);
                var mouseClicks = dayMouseSessions.Sum(s => s.TotalClicks);

                var hasData = totalUsage.TotalSeconds > 0 || keyPresses > 0 || mouseClicks > 0;
                var activityScore = hasData ? CalculateDailyActivityScore(totalUsage, keyPresses, mouseClicks) : 0;

                heatmapCells.Add(new DailyHeatmapCell
                {
                    Date = currentDate.ToDateTime(TimeOnly.MinValue),
                    ActivityScore = activityScore,
                    TotalUsage = totalUsage,
                    KeyPresses = keyPresses,
                    MouseClicks = mouseClicks,
                    HasData = hasData
                });
            }

            processStopwatch.Stop();
            Log.Information("[HeatmapAggregator] 单元格生成完成 - 耗时:{Elapsed}ms, 数量:{Count}", 
                processStopwatch.ElapsedMilliseconds, heatmapCells.Count);

            stopwatch.Stop();
            var finalMemory = process.WorkingSet64 / 1024 / 1024;
            var memoryDelta = finalMemory - initialMemory;
            
            Log.Information("[HeatmapAggregator] 月热力图加载完成 - 总耗时:{TotalElapsed}ms, 最终内存:{FinalMemory} MB, 内存变化:{MemoryDelta} MB",
                stopwatch.ElapsedMilliseconds, finalMemory, memoryDelta);

            return heatmapCells;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error(ex, "[HeatmapAggregator] 加载月热力图失败 - 耗时:{Elapsed}ms", stopwatch.ElapsedMilliseconds);
            return heatmapCells;
        }
    }

    public async Task<List<DailyHeatmapCell>> GetWeeklyDailyHeatmapDataAsync(DateTime weekStart)
    {
        Log.Information("[HeatmapAggregator] ===== 开始加载周热力图（按天汇总） =====");
        Log.Information("[HeatmapAggregator] 周起始日期: {WeekStart}", weekStart.ToString("yyyy-MM-dd"));
        
        var heatmapCells = new List<DailyHeatmapCell>(7);
        var weekEnd = weekStart.AddDays(7);

        try
        {
            var appSessionsTask = _appSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);
            var keyboardSessionsTask = _keyboardSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);
            var mouseSessionsTask = _mouseSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);
            
            await Task.WhenAll(appSessionsTask, keyboardSessionsTask, mouseSessionsTask);
            
            var allAppSessions = appSessionsTask.Result.Take(MaxWeeklySessions).ToList();
            var allKeyboardSessions = keyboardSessionsTask.Result.Take(MaxWeeklySessions).ToList();
            var allMouseSessions = mouseSessionsTask.Result.Take(MaxWeeklySessions).ToList();
            
            Log.Information("[HeatmapAggregator] 数据库查询完成 - 应用:{AppCount}, 键盘:{KeyCount}, 鼠标:{MouseCount}", 
                allAppSessions.Count, allKeyboardSessions.Count, allMouseSessions.Count);

            var appSessionsByDay = allAppSessions
                .GroupBy(s => DateOnly.FromDateTime(s.StartTime))
                .ToDictionary(g => g.Key, g => g.ToList());

            var keyboardSessionsByDay = allKeyboardSessions
                .GroupBy(s => DateOnly.FromDateTime(s.Date))
                .ToDictionary(g => g.Key, g => g.ToList());

            var mouseSessionsByDay = allMouseSessions
                .GroupBy(s => DateOnly.FromDateTime(s.Date))
                .ToDictionary(g => g.Key, g => g.ToList());

            for (var dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                var currentDate = DateOnly.FromDateTime(weekStart.Date.AddDays(dayOffset));

                List<AppSession> dayAppSessions = [];
                if (appSessionsByDay.TryGetValue(currentDate, out var sessions))
                {
                    dayAppSessions = sessions;
                }

                List<KeyboardSession> dayKeyboardSessions = [];
                if (keyboardSessionsByDay.TryGetValue(currentDate, out var keyboardSessions))
                {
                    dayKeyboardSessions = keyboardSessions;
                }

                List<MouseSession> dayMouseSessions = [];
                if (mouseSessionsByDay.TryGetValue(currentDate, out var mouseSessions))
                {
                    dayMouseSessions = mouseSessions;
                }

                var totalUsage = TimeSpan.FromSeconds(dayAppSessions.Sum(s => s.Duration.TotalSeconds));
                var keyPresses = dayKeyboardSessions.Sum(s => s.TotalKeyPresses);
                var mouseClicks = dayMouseSessions.Sum(s => s.TotalClicks);

                var hasData = totalUsage.TotalSeconds > 0 || keyPresses > 0 || mouseClicks > 0;
                var activityScore = hasData ? CalculateDailyActivityScore(totalUsage, keyPresses, mouseClicks) : 0;

                heatmapCells.Add(new DailyHeatmapCell
                {
                    Date = currentDate.ToDateTime(TimeOnly.MinValue),
                    ActivityScore = activityScore,
                    TotalUsage = totalUsage,
                    KeyPresses = keyPresses,
                    MouseClicks = mouseClicks,
                    HasData = hasData
                });
            }

            Log.Information("[HeatmapAggregator] 周热力图（按天汇总）加载完成 - 数量:{Count}", heatmapCells.Count);
            return heatmapCells;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HeatmapAggregator] 加载周热力图（按天汇总）失败");
            return heatmapCells;
        }
    }

    private static double CalculateActivityScore(TimeSpan appUsage, int keyPresses, int mouseClicks)
    {
        var usageScore = Math.Min(appUsage.TotalMinutes / 60.0, 1.0) * 40;
        var keyScore = Math.Min(keyPresses / 1000.0, 1.0) * 30;
        var mouseScore = Math.Min(mouseClicks / 500.0, 1.0) * 30;

        return usageScore + keyScore + mouseScore;
    }

    private static double CalculateDailyActivityScore(TimeSpan totalUsage, int keyPresses, int mouseClicks)
    {
        var usageScore = Math.Min(totalUsage.TotalHours / 12.0, 1.0) * 40;
        var keyScore = Math.Min(keyPresses / 10000.0, 1.0) * 30;
        var mouseScore = Math.Min(mouseClicks / 5000.0, 1.0) * 30;

        return usageScore + keyScore + mouseScore;
    }
}

public class HeatmapCell
{
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public double ActivityScore { get; set; }
    public TimeSpan AppUsage { get; set; }
    public int KeyPresses { get; set; }
    public int MouseClicks { get; set; }
    public List<string> TopApps { get; set; } = [];
}

public class DailyHeatmapCell
{
    public DateTime Date { get; set; }
    public double ActivityScore { get; set; }
    public TimeSpan TotalUsage { get; set; }
    public int KeyPresses { get; set; }
    public int MouseClicks { get; set; }
    public bool HasData { get; set; }
}
