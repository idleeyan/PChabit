using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Analysis;

public class PatternAnalyzer : IPatternAnalyzer
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;

    public PatternAnalyzer(IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<WorkPattern?> AnalyzeDayAsync(DateTime date)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var sessions = await dbContext.AppSessions
            .Where(s => s.StartTime.Date == date.Date)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        if (!sessions.Any())
            return null;

        var workSession = await IdentifyWorkSessionAsync(date);
        var focusBlocks = await IdentifyFocusBlocksAsync(date);
        var peakHours = await IdentifyPeakHoursAsync(date);

        var breakCount = CalculateBreakCount(sessions);
        var totalBreakMinutes = CalculateTotalBreakMinutes(sessions);

        var pattern = new WorkPattern
        {
            Date = date,
            WorkStartTime = workSession.StartTime,
            WorkEndTime = workSession.EndTime,
            PeakHours = JsonSerializer.Serialize(peakHours),
            FocusBlocks = JsonSerializer.Serialize(focusBlocks),
            BreakCount = breakCount,
            TotalBreakMinutes = totalBreakMinutes,
            CreatedAt = DateTime.Now
        };

        return pattern;
    }

    public async Task<List<FocusBlockInfo>> IdentifyFocusBlocksAsync(DateTime date, int minDurationMinutes = 25)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var sessions = await dbContext.AppSessions
            .Where(s => s.StartTime.Date == date.Date)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        if (!sessions.Any())
            return new List<FocusBlockInfo>();

        var focusBlocks = new List<FocusBlockInfo>();
        var currentBlock = new FocusBlockInfo
        {
            StartTime = sessions.First().StartTime,
            PrimaryApplication = sessions.First().ProcessName,
            Category = sessions.First().Category
        };

        foreach (var session in sessions.Skip(1))
        {
            var gap = (session.StartTime - currentBlock.StartTime).TotalMinutes;
            
            if (gap <= 5 && session.Duration.TotalMinutes >= 1)
            {
                currentBlock.EndTime = session.StartTime + session.Duration;
                currentBlock.SwitchCount++;
                
                if (session.Duration.TotalMinutes > 5)
                {
                    currentBlock.PrimaryApplication = session.ProcessName;
                    currentBlock.Category = session.Category;
                }
            }
            else
            {
                if (currentBlock.Duration.TotalMinutes >= minDurationMinutes)
                {
                    focusBlocks.Add(currentBlock);
                }
                
                currentBlock = new FocusBlockInfo
                {
                    StartTime = session.StartTime,
                    EndTime = session.StartTime + session.Duration,
                    PrimaryApplication = session.ProcessName,
                    Category = session.Category
                };
            }
        }

        if (currentBlock.Duration.TotalMinutes >= minDurationMinutes)
        {
            focusBlocks.Add(currentBlock);
        }

        return focusBlocks;
    }

    public async Task<List<TimeSlot>> IdentifyPeakHoursAsync(DateTime date)
    {
        var hourlyDistribution = await GetHourlyActivityDistributionAsync(date);
        
        var peakHours = hourlyDistribution
            .Where(kvp => kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .Take(3)
            .Select(kvp => new TimeSlot
            {
                Hour = kvp.Key,
                Minute = 0,
                ActivityScore = kvp.Value,
                SessionCount = 0
            })
            .ToList();

        return peakHours;
    }

    public async Task<WorkSession> IdentifyWorkSessionAsync(DateTime date)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var sessions = await dbContext.AppSessions
            .Where(s => s.StartTime.Date == date.Date)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        if (!sessions.Any())
            return new WorkSession();

        var firstSession = sessions.First();
        var lastSession = sessions.Last();

        var productiveCategories = new[] { "开发工具", "办公软件", "生产力", "工作" };
        
        var workSessions = sessions
            .Where(s => !string.IsNullOrEmpty(s.Category) && 
                        productiveCategories.Any(c => 
                            s.Category!.Contains(c, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        TimeSpan? startTime = null;
        TimeSpan? endTime = null;

        if (workSessions.Any())
        {
            startTime = workSessions.Min(s => s.StartTime.TimeOfDay);
            endTime = workSessions.Max(s => (s.StartTime + s.Duration).TimeOfDay);
        }
        else
        {
            startTime = firstSession.StartTime.TimeOfDay;
            endTime = (lastSession.StartTime + lastSession.Duration).TimeOfDay;
        }

        return new WorkSession
        {
            StartTime = startTime,
            EndTime = endTime,
            SessionCount = sessions.Count
        };
    }

    public async Task<Dictionary<int, double>> GetHourlyActivityDistributionAsync(DateTime date)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var sessions = await dbContext.AppSessions
            .Where(s => s.StartTime.Date == date.Date)
            .ToListAsync();

        var hourlyDistribution = new Dictionary<int, double>();
        
        for (int hour = 0; hour < 24; hour++)
        {
            hourlyDistribution[hour] = 0;
        }

        foreach (var session in sessions)
        {
            var hour = session.StartTime.Hour;
            hourlyDistribution[hour] += session.Duration.TotalMinutes;
        }

        var maxMinutes = hourlyDistribution.Max(kvp => kvp.Value);
        if (maxMinutes > 0)
        {
            foreach (var key in hourlyDistribution.Keys.ToList())
            {
                hourlyDistribution[key] = hourlyDistribution[key] / maxMinutes * 100;
            }
        }

        return hourlyDistribution;
    }

    private int CalculateBreakCount(List<AppSession> sessions)
    {
        if (sessions.Count < 2)
            return 0;

        var breakCount = 0;
        for (int i = 1; i < sessions.Count; i++)
        {
            var gap = (sessions[i].StartTime - sessions[i - 1].StartTime - sessions[i - 1].Duration).TotalMinutes;
            if (gap >= 5)
            {
                breakCount++;
            }
        }

        return breakCount;
    }

    private int CalculateTotalBreakMinutes(List<AppSession> sessions)
    {
        if (sessions.Count < 2)
            return 0;

        var totalBreakMinutes = 0;
        for (int i = 1; i < sessions.Count; i++)
        {
            var gap = (sessions[i].StartTime - sessions[i - 1].StartTime - sessions[i - 1].Duration).TotalMinutes;
            if (gap >= 1)
            {
                totalBreakMinutes += (int)gap;
            }
        }

        return totalBreakMinutes;
    }
}
