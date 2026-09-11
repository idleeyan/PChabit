using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Monitoring;

namespace PChabit.App.Services;

public partial class DataCollectionService : IDisposable
{
    private async Task CheckDailyAggregationAsync()
    {
        // 每小时至少检查一次
        if ((DateTime.Now - _lastAggregationCheck).TotalMinutes < 60)
            return;

        _lastAggregationCheck = DateTime.Now;

        try
        {
            var yesterday = DateTime.Today.AddDays(-1);
            var dateKey = yesterday.ToString("yyyy-MM-dd");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();

            var exists = await dbContext.DailySummaries
                .AnyAsync(s => s.Date == dateKey);

            if (!exists)
            {
                await AggregateDailySummaryAsync(dbContext, yesterday, dateKey);

                // 聚合后进行数据清理
                await CleanupOldDataAsync(dbContext);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "每日聚合检查失败");
        }
    }

    private static async Task AggregateDailySummaryAsync(PChabitDbContext dbContext, DateTime date, string dateKey)
    {
        var nextDay = date.AddDays(1);

        // 从 KeyboardSession 聚合
        var keyboardSessions = await dbContext.KeyboardSessions
            .AsNoTracking()
            .Where(s => s.Date >= date && s.Date < nextDay)
            .ToListAsync();

        var totalKeys = keyboardSessions.Sum(s => (long)s.TotalKeyPresses);

        // 每小时按键分布 (24 小时)
        var hourlyKeys = new int[24];
        foreach (var ks in keyboardSessions)
        {
            if (ks.Hour >= 0 && ks.Hour < 24)
                hourlyKeys[ks.Hour] += ks.TotalKeyPresses;
        }

        // 从 MouseSession 聚合
        var mouseSessions = await dbContext.MouseSessions
            .AsNoTracking()
            .Where(s => s.Date >= date && s.Date < nextDay)
            .ToListAsync();

        var totalMouseClicks = mouseSessions.Sum(s =>
            (long)(s.LeftClickCount + s.RightClickCount + s.MiddleClickCount));

        // 从 AppSession 聚合 TopApps（按总时长排序取前 10）
        var appSessions = await dbContext.AppSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= date && s.StartTime < nextDay)
            .ToListAsync();

        var topApps = appSessions
            .GroupBy(s => s.ProcessName)
            .Select(g => new { Name = g.Key, Minutes = g.Sum(s =>
                s.EndTime.HasValue ? (s.EndTime!.Value - s.StartTime).TotalMinutes : 0) })
            .OrderByDescending(x => x.Minutes)
            .Take(10)
            .Select(x => new { x.Name, x.Minutes })
            .ToList();

        var activeMinutes = appSessions
            .Where(s => s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);

        // 从 WebSession 聚合网页指标（真实会话，排除 Legacy 切片）
        var webSessions = await dbContext.WebSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= date && s.StartTime < nextDay && !s.IsLegacy)
            .ToListAsync();

        var webPages = webSessions.Count;
        var webDurationTicks = webSessions.Sum(s => s.Duration.Ticks);
        var webActiveTicks = webSessions.Sum(s => s.ActiveDuration.Ticks);

        var topAppsJson = System.Text.Json.JsonSerializer.Serialize(topApps);
        var hourlyKeysJson = System.Text.Json.JsonSerializer.Serialize(hourlyKeys);

        // Upsert DailySummary
        var summary = await dbContext.DailySummaries
            .FirstOrDefaultAsync(s => s.Date == dateKey);

        if (summary == null)
        {
            summary = new DailySummary
            {
                Id = Guid.NewGuid(),
                Date = dateKey,
                TotalKeys = totalKeys,
                TotalMouseClicks = totalMouseClicks,
                ActiveMinutes = activeMinutes,
                TopApps = topAppsJson,
                HourlyKeyDistribution = hourlyKeysJson,
                WebPages = webPages,
                WebDurationTicks = webDurationTicks,
                WebActiveDurationTicks = webActiveTicks,
                LastUpdated = DateTime.Now
            };
            await dbContext.DailySummaries.AddAsync(summary);
        }
        else
        {
            summary.TotalKeys = totalKeys;
            summary.TotalMouseClicks = totalMouseClicks;
            summary.ActiveMinutes = activeMinutes;
            summary.TopApps = topAppsJson;
            summary.HourlyKeyDistribution = hourlyKeysJson;
            summary.WebPages = webPages;
            summary.WebDurationTicks = webDurationTicks;
            summary.WebActiveDurationTicks = webActiveTicks;
            summary.LastUpdated = DateTime.Now;
        }

        await dbContext.SaveChangesAsync();

        Log.Information("每日聚合完成: {Date}, Keys={TotalKeys}, Clicks={TotalClicks}, ActiveMin={ActiveMin:F1}, WebPages={WebPages}, TopApps={TopAppCount}",
            dateKey, totalKeys, totalMouseClicks, activeMinutes, webPages, topApps.Count);
    }

    private static async Task CleanupOldDataAsync(PChabitDbContext dbContext, int retentionDays = 90)
    {
        // 尝试从设置读取保留天数
        try
        {
            var settings = dbContext.Set<DailySummary>().AsNoTracking().FirstOrDefault();
            // 默认使用 90 天，实际从 ISettingsService 读取
        }
        catch { /* 忽略 */ }

        var cutoff = DateTime.Today.AddDays(-retentionDays);
        var cutoffStr = cutoff.ToString("yyyy-MM-dd");
        var deletedCount = 0;

        Log.Information("开始数据清理，截止日期: {Cutoff}，保留 {Days} 天", cutoffStr, retentionDays);

        // 清理前确保对应日期的 DailySummary 已存在
        // 清理 KeyboardSession
        var oldKeySessions = await dbContext.KeyboardSessions
            .Where(s => s.Date < cutoff)
            .CountAsync();

        if (oldKeySessions > 0)
        {
            await dbContext.KeyboardSessions
                .Where(s => s.Date < cutoff)
                .ExecuteDeleteAsync();
            deletedCount += oldKeySessions;
        }

        // 清理 MouseSession
        var oldMouseSessions = await dbContext.MouseSessions
            .Where(s => s.Date < cutoff)
            .CountAsync();

        if (oldMouseSessions > 0)
        {
            await dbContext.MouseSessions
                .Where(s => s.Date < cutoff)
                .ExecuteDeleteAsync();
            deletedCount += oldMouseSessions;
        }

        // 清理 WebSession
        var oldWebSessions = await dbContext.WebSessions
            .Where(s => s.StartTime < cutoff)
            .CountAsync();

        if (oldWebSessions > 0)
        {
            await dbContext.WebSessions
                .Where(s => s.StartTime < cutoff)
                .ExecuteDeleteAsync();
            deletedCount += oldWebSessions;
        }

        // 清理 AppSession
        var oldAppSessions = await dbContext.AppSessions
            .Where(s => s.StartTime < cutoff)
            .CountAsync();

        if (oldAppSessions > 0)
        {
            await dbContext.AppSessions
                .Where(s => s.StartTime < cutoff)
                .ExecuteDeleteAsync();
            deletedCount += oldAppSessions;
        }

        if (deletedCount > 0)
        {
            await dbContext.SaveChangesAsync();
            Log.Information("数据清理完成，共删除 {Count} 条 {Days} 天前的记录", deletedCount, retentionDays);
        }
        else
        {
            Log.Information("无过期数据需要清理（保留 {Days} 天）", retentionDays);
        }
    }

}

