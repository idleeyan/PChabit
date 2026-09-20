using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Analysis;
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

                // 应用统计日预聚合（AppDailyStats），与 DailySummaries 同源
                await AggregateAppDailyStatsAsync(dbContext, yesterday, dateKey);

                // 聚合后进行数据清理（保留天数读设置，钳制到 1-3650 天）
                var retentionDays = Math.Clamp(_settingsService.DataRetentionDays, 1, 3650);
                await CleanupOldDataAsync(dbContext, retentionDays);
            }

            // 首次升级回填：AppDailyStats 为空且存在历史 AppSessions 时，回填最近 30 天
            await EnsureAppDailyStatsBackfillAsync();

            // 分析升级 P0：最近 30 天 DailySummaries 扩展指标（硬件日指标/WebMinutes）回填
            await EnsureHardwareMetricsBackfillAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "每日聚合检查失败");
        }
    }

    /// <summary>首次升级回填：AppDailyStats 无行且 AppSessions 有历史数据时，聚合最近 30 天。</summary>
    private async Task EnsureAppDailyStatsBackfillAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();

            var hasAny = await dbContext.AppDailyStats.AnyAsync();
            if (hasAny) return;

            var hasHistory = await dbContext.AppSessions.AnyAsync(s => s.StartTime < DateTime.Today);
            if (!hasHistory) return;

            Log.Information("AppDailyStats 为空，开始回填最近 30 天应用统计…");
            for (var i = 1; i <= 30; i++)
            {
                var day = DateTime.Today.AddDays(-i);
                await AggregateAppDailyStatsAsync(dbContext, day, day.ToString("yyyy-MM-dd"));
            }
            Log.Information("AppDailyStats 回填完成");
        }
        catch (Exception ex)
        {
            // 表缺失/写锁竞争等异常不阻断主流程，后续每日聚合会自动补齐
            Log.Error(ex, "AppDailyStats 回填失败（将由每日聚合自动补齐）");
        }
    }

    /// <summary>聚合某日 AppSessions 到 AppDailyStats（Date + 规范化进程名 Upsert）。</summary>
    private static async Task AggregateAppDailyStatsAsync(PChabitDbContext dbContext, DateTime date, string dateKey)
    {
        var nextDay = date.AddDays(1);

        var appSessions = await dbContext.AppSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= date && s.StartTime < nextDay)
            .ToListAsync();

        // 分类映射（专注判断需要生产力分类）
        var categories = await dbContext.ProgramCategories
            .AsNoTracking()
            .Include(c => c.ProgramMappings)
            .Where(c => c.IsActive)
            .ToListAsync();
        var categoryMap = AppStatsEngine.BuildCategoryMap(categories);

        var groups = appSessions
            .GroupBy(s => AppStatsEngine.NormalizeProcessName(s.ProcessName ?? ""))
            .Where(g => g.Key.Length > 0)
            .Select(g =>
            {
                var first = g.First();
                var (catName, _) = AppStatsEngine.ResolveCategory(first, categoryMap);
                var minutes = g.Where(s => s.EndTime.HasValue)
                    .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
                var focusMinutes = g.Where(s => s.EndTime.HasValue &&
                        (s.EndTime!.Value - s.StartTime).TotalMinutes >= 25 &&
                        AnalyticsEngine.IsProductiveCategory(catName))
                    .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);

                // 小时分布：跨小时会话按重叠裁剪（与 AppStats 现状口径一致）
                var hourly = new double[24];
                foreach (var s in g.Where(x => x.EndTime.HasValue))
                {
                    var sStart = s.StartTime;
                    var sEnd = s.EndTime!.Value;
                    for (var h = 0; h < 24; h++)
                    {
                        var hs = date.AddHours(h);
                        var he = hs.AddHours(1);
                        if (sStart < he && sEnd > hs)
                        {
                            var st = sStart < hs ? hs : sStart;
                            var en = sEnd > he ? he : sEnd;
                            hourly[h] += (en - st).TotalMinutes;
                        }
                    }
                }

                return new
                {
                    Key = g.Key,
                    Minutes = minutes,
                    Sessions = g.Count(),
                    FocusMinutes = focusMinutes,
                    Hourly = hourly
                };
            })
            .ToList();

        foreach (var g in groups)
        {
            var row = await dbContext.AppDailyStats
                .FirstOrDefaultAsync(x => x.Date == dateKey && x.ProcessName == g.Key);

            var hourlyJson = System.Text.Json.JsonSerializer.Serialize(
                g.Hourly.Select(m => Math.Round(m, 1)).ToArray());

            if (row == null)
            {
                dbContext.AppDailyStats.Add(new AppDailyStats
                {
                    Date = dateKey,
                    ProcessName = g.Key,
                    Minutes = g.Minutes,
                    Sessions = g.Sessions,
                    FocusMinutes = g.FocusMinutes,
                    HourlyJson = hourlyJson,
                    LastUpdated = DateTime.Now
                });
            }
            else
            {
                row.Minutes = g.Minutes;
                row.Sessions = g.Sessions;
                row.FocusMinutes = g.FocusMinutes;
                row.HourlyJson = hourlyJson;
                row.LastUpdated = DateTime.Now;
            }
        }

        await dbContext.SaveChangesAsync();
        Log.Information("应用统计日预聚合完成: {Date}, 应用数={Count}", dateKey, groups.Count);
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

        // 分析升级 P0：硬件分钟样本二次聚合 + 网页有效浏览分钟
        var hardware = await AggregateHardwareDayAsync(dbContext, date, dateKey);
        var webMinutes = webActiveTicks > 0
            ? Math.Round(webActiveTicks / (double)TimeSpan.TicksPerMinute, 1)
            : 0;

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
                WebMinutes = webMinutes,
                CpuLoadAvg = hardware?.CpuLoadAvg,
                CpuLoadP95 = hardware?.CpuLoadP95,
                GpuLoadAvg = hardware?.GpuLoadAvg,
                GpuTempMax = hardware?.GpuTempMax,
                MemLoadAvg = hardware?.MemLoadAvg,
                MetricsVersion = 2,
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
            summary.WebMinutes = webMinutes;
            summary.CpuLoadAvg = hardware?.CpuLoadAvg;
            summary.CpuLoadP95 = hardware?.CpuLoadP95;
            summary.GpuLoadAvg = hardware?.GpuLoadAvg;
            summary.GpuTempMax = hardware?.GpuTempMax;
            summary.MemLoadAvg = hardware?.MemLoadAvg;
            summary.MetricsVersion = 2;
            summary.LastUpdated = DateTime.Now;
        }

        await dbContext.SaveChangesAsync();

        Log.Information("每日聚合完成: {Date}, Keys={TotalKeys}, Clicks={TotalClicks}, ActiveMin={ActiveMin:F1}, WebPages={WebPages}, TopApps={TopAppCount}",
            dateKey, totalKeys, totalMouseClicks, activeMinutes, webPages, topApps.Count);
    }

    private static async Task CleanupOldDataAsync(PChabitDbContext dbContext, int retentionDays = 90)
    {
        var cutoff = DateTime.Today.AddDays(-retentionDays);
        var cutoffStr = cutoff.ToString("yyyy-MM-dd");
        // HardwareSamples.Timestamp 为 "yyyy-MM-dd HH:mm"，按字典序与当日 00:00 比较
        var sampleCutoff = cutoffStr + " 00:00";
        var deletedCount = 0;

        Log.Information("开始数据清理，截止日期: {Cutoff}，保留 {Days} 天", cutoffStr, retentionDays);

        // 清理 KeyboardSession
        var oldKeySessions = await dbContext.KeyboardSessions
            .Where(s => s.Date < cutoff)
            .CountAsync();

        if (oldKeySessions > 0)
        {
            deletedCount += await dbContext.KeyboardSessions
                .Where(s => s.Date < cutoff)
                .ExecuteDeleteAsync();
        }

        // 清理 MouseSession
        var oldMouseSessions = await dbContext.MouseSessions
            .Where(s => s.Date < cutoff)
            .CountAsync();

        if (oldMouseSessions > 0)
        {
            deletedCount += await dbContext.MouseSessions
                .Where(s => s.Date < cutoff)
                .ExecuteDeleteAsync();
        }

        // 清理 WebSession
        var oldWebSessions = await dbContext.WebSessions
            .Where(s => s.StartTime < cutoff)
            .CountAsync();

        if (oldWebSessions > 0)
        {
            deletedCount += await dbContext.WebSessions
                .Where(s => s.StartTime < cutoff)
                .ExecuteDeleteAsync();
        }

        // 清理 AppSession
        var oldAppSessions = await dbContext.AppSessions
            .Where(s => s.StartTime < cutoff)
            .CountAsync();

        if (oldAppSessions > 0)
        {
            deletedCount += await dbContext.AppSessions
                .Where(s => s.StartTime < cutoff)
                .ExecuteDeleteAsync();
        }

        // 清理 AppDailyStats 预聚合（与 AppSessions 同一保留口径）
        // 注意：EF Core SQLite 不翻译 string.CompareOrdinal（历史上此处导致"每日聚合检查失败"），
        // 定长日期键可直接用 SQL 字符串字典序比较，参数化一条完成删除并取回影响行数
        deletedCount += await dbContext.Database
            .ExecuteSqlInterpolatedAsync($"DELETE FROM AppDailyStats WHERE Date < {cutoffStr}");

        // 分析升级 P0：清理过期硬件分钟样本（分钟粒度原始数据，跟随保留天数）
        deletedCount += await dbContext.Database
            .ExecuteSqlInterpolatedAsync($"DELETE FROM HardwareSamples WHERE Timestamp < {sampleCutoff}");

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
