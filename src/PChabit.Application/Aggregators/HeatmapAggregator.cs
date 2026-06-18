using System.Diagnostics;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;

namespace PChabit.Application.Aggregators;

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
        _appSessionRepo = appSessionRepo;
        _keyboardSessionRepo = keyboardSessionRepo;
        _mouseSessionRepo = mouseSessionRepo;
    }

    public async Task<List<HeatmapCell>> GetWeeklyHeatmapDataAsync(DateTime weekStart)
    {
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var initialMemory = process.WorkingSet64 / 1024 / 1024;

        Log.Information("[HeatmapAggregator] 开始加载周热力图: {WeekStart}", weekStart.ToString("yyyy-MM-dd"));

        var heatmapCells = new List<HeatmapCell>(168);
        var weekEnd = weekStart.AddDays(7);

        try
        {
            var appSessionsTask = _appSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);
            var keyboardSessionsTask = _keyboardSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);
            var mouseSessionsTask = _mouseSessionRepo.GetByDateRangeAsync(weekStart, weekEnd);

            await Task.WhenAll(appSessionsTask, keyboardSessionsTask, mouseSessionsTask);

            // 在服务端截断，避免加载过多数据到内存
            var allAppSessions = appSessionsTask.Result.Take(MaxWeeklySessions).ToList();
            var allKeyboardSessions = keyboardSessionsTask.Result.Take(MaxWeeklySessions).ToList();
            var allMouseSessions = mouseSessionsTask.Result.Take(MaxWeeklySessions).ToList();

            Log.Information("[HeatmapAggregator] 数据库查询完成 - 应用:{AppCount}, 键盘:{KeyCount}, 鼠标:{MouseCount}",
                allAppSessions.Count, allKeyboardSessions.Count, allMouseSessions.Count);

            var appSessionsByDayHour = allAppSessions
                .GroupBy(s => (Date: DateOnly.FromDateTime(s.StartTime), Hour: s.StartTime.Hour))
                .ToDictionary(g => g.Key, g => g.ToList());

            var keyboardSessionsByDayHour = allKeyboardSessions
                .GroupBy(s => (Date: DateOnly.FromDateTime(s.Date), Hour: s.Hour))
                .ToDictionary(g => g.Key, g => g.ToList());

            var mouseSessionsByDayHour = allMouseSessions
                .GroupBy(s => (Date: DateOnly.FromDateTime(s.Date), Hour: s.Hour))
                .ToDictionary(g => g.Key, g => g.ToList());

            for (var dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                var currentDate = DateOnly.FromDateTime(weekStart.Date.AddDays(dayOffset));

                for (var hour = 0; hour < 24; hour++)
                {
                    var key = (Date: currentDate, Hour: hour);

                    var hourAppSessions = appSessionsByDayHour.TryGetValue(key, out var sessions) ? sessions : [];
                    var hourKeyboardSessions = keyboardSessionsByDayHour.TryGetValue(key, out var keyboardSessions) ? keyboardSessions : [];
                    var hourMouseSessions = mouseSessionsByDayHour.TryGetValue(key, out var mouseSessions) ? mouseSessions : [];

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

            stopwatch.Stop();
            var finalMemory = process.WorkingSet64 / 1024 / 1024;
            var memoryDelta = finalMemory - initialMemory;

            Log.Information("[HeatmapAggregator] 周热力图加载完成 - 耗时:{Elapsed}ms, 内存变化:{MemoryDelta} MB",
                stopwatch.ElapsedMilliseconds, memoryDelta);

            return heatmapCells;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error(ex, "[HeatmapAggregator] 加载周热力图失败");
            return heatmapCells;
        }
    }

    public async Task<List<DailyHeatmapCell>> GetMonthlyHeatmapDataAsync(DateTime monthStart)
    {
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var initialMemory = process.WorkingSet64 / 1024 / 1024;

        Log.Information("[HeatmapAggregator] 开始加载月热力图: {Month}", monthStart.ToString("yyyy-MM"));

        var year = monthStart.Year;
        var month = monthStart.Month;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var heatmapCells = new List<DailyHeatmapCell>(daysInMonth);
        var monthEnd = monthStart.AddMonths(1);

        try
        {
            var appSessionsTask = _appSessionRepo.GetByDateRangeAsync(monthStart, monthEnd);
            var keyboardSessionsTask = _keyboardSessionRepo.GetByDateRangeAsync(monthStart, monthEnd);
            var mouseSessionsTask = _mouseSessionRepo.GetByDateRangeAsync(monthStart, monthEnd);

            await Task.WhenAll(appSessionsTask, keyboardSessionsTask, mouseSessionsTask);

            var allAppSessions = appSessionsTask.Result.Take(MaxMonthlySessions).ToList();
            var allKeyboardSessions = keyboardSessionsTask.Result.Take(MaxMonthlySessions).ToList();
            var allMouseSessions = mouseSessionsTask.Result.Take(MaxMonthlySessions).ToList();

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

                var dayAppSessions = appSessionsByDay.TryGetValue(currentDate, out var sessions) ? sessions : [];
                var dayKeyboardSessions = keyboardSessionsByDay.TryGetValue(currentDate, out var keyboardSessions) ? keyboardSessions : [];
                var dayMouseSessions = mouseSessionsByDay.TryGetValue(currentDate, out var mouseSessions) ? mouseSessions : [];

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

            stopwatch.Stop();
            var finalMemory = process.WorkingSet64 / 1024 / 1024;
            var memoryDelta = finalMemory - initialMemory;

            Log.Information("[HeatmapAggregator] 月热力图加载完成 - 耗时:{Elapsed}ms, 内存变化:{MemoryDelta} MB",
                stopwatch.ElapsedMilliseconds, memoryDelta);

            return heatmapCells;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error(ex, "[HeatmapAggregator] 加载月热力图失败");
            return heatmapCells;
        }
    }

    public async Task<List<DailyHeatmapCell>> GetWeeklyDailyHeatmapDataAsync(DateTime weekStart)
    {
        Log.Information("[HeatmapAggregator] 开始加载周热力图（按天汇总）: {WeekStart}", weekStart.ToString("yyyy-MM-dd"));

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

                var dayAppSessions = appSessionsByDay.TryGetValue(currentDate, out var sessions) ? sessions : [];
                var dayKeyboardSessions = keyboardSessionsByDay.TryGetValue(currentDate, out var keyboardSessions) ? keyboardSessions : [];
                var dayMouseSessions = mouseSessionsByDay.TryGetValue(currentDate, out var mouseSessions) ? mouseSessions : [];

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

            Log.Information("[HeatmapAggregator] 周热力图（按天汇总）加载完成");
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
