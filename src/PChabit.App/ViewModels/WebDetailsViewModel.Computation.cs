using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;


namespace PChabit.App.ViewModels;

public partial class WebDetailsViewModel : DbSafeViewModel<WebDetailsViewModel.WebStatsData>
{
    private static double EffectiveMinutesValue(Core.Entities.WebSession s)
    {
        return s.ActiveDuration > TimeSpan.Zero ? s.ActiveDuration.TotalMinutes : s.Duration.TotalMinutes;
    }

    private static double EffectiveSecondsValue(Core.Entities.WebSession s)
    {
        return s.ActiveDuration > TimeSpan.Zero ? s.ActiveDuration.TotalSeconds : s.Duration.TotalSeconds;
    }

    private string ResolveCategory(Core.Entities.WebSession s)
    {
        return !string.IsNullOrEmpty(s.CategoryName) ? s.CategoryName! : GetCategory(s.Domain);
    }

    private List<DomainStatItem> ComputeDomainStats(List<Core.Entities.WebSession> sessions)
    {
        return sessions
            .GroupBy(s => s.Domain)
            .Select(g => new DomainStatItem
            {
                Domain = g.Key,
                VisitCount = g.Count(),
                TotalDuration = g.Sum(EffectiveMinutesValue),
                AvgDuration = g.Average(EffectiveSecondsValue),
                Category = ResolveCategory(g.First()),
                LastVisit = g.Max(s => s.StartTime)
            })
            .OrderByDescending(x => x.TotalDuration)
            .Take(20)
            .ToList();
    }

    private List<WebHourlyActivityItem> ComputeHourlyActivity(List<Core.Entities.WebSession> sessions)
    {
        var result = new List<WebHourlyActivityItem>();
        for (int hour = 0; hour < 24; hour++)
        {
            var hourSessions = sessions.Where(s => s.StartTime.Hour == hour).ToList();
            var visitCount = hourSessions.Count;
            var duration = hourSessions.Sum(s => s.Duration.TotalMinutes);
            
            result.Add(new WebHourlyActivityItem
            {
                Hour = hour,
                HourLabel = $"{hour}:00",
                VisitCount = visitCount,
                Duration = (int)duration,
                BarHeight = Math.Max(10, Math.Min(100, visitCount))
            });
        }
        return result;
    }

    private List<DailyTrendItem> ComputeDailyTrend(List<Core.Entities.WebSession> sessions)
    {
        return sessions
            .GroupBy(s => s.StartTime.Date)
            .Select(g => new DailyTrendItem
            {
                Date = g.Key,
                DateLabel = g.Key.ToString("MM/dd"),
                VisitCount = g.Count(),
                TotalDuration = g.Sum(s => s.Duration.TotalMinutes),
                UniqueDomains = g.Select(s => s.Domain).Distinct().Count()
            })
            .OrderBy(x => x.Date)
            .ToList();
    }

    private List<BrowsingPatternItem> ComputeBrowsingPatterns(List<Core.Entities.WebSession> sessions)
    {
        var result = new List<BrowsingPatternItem>();
        
        var peakHours = sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(3)
            .ToList();
        
        if (peakHours.Any())
        {
            result.Add(new BrowsingPatternItem
            {
                Pattern = "活跃时段",
                Description = string.Join(", ", peakHours.Select(h => $"{h.Hour}:00")),
                Icon = "\uE823",
                Color = "#0078D4"
            });
        }
        
        var topDomains = sessions
            .GroupBy(s => s.Domain)
            .Select(g => new { Domain = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(3)
            .ToList();
        
        if (topDomains.Any())
        {
            result.Add(new BrowsingPatternItem
            {
                Pattern = "高频访问",
                Description = string.Join(", ", topDomains.Select(d => d.Domain)),
                Icon = "\uE774",
                Color = "#107C10"
            });
        }
        
        var shortVisits = sessions.Count(s => EffectiveSecondsValue(s) < 30);
        var longVisits = sessions.Count(s => EffectiveMinutesValue(s) >= 5);
        
        result.Add(new BrowsingPatternItem
        {
            Pattern = "访问时长分布",
            Description = $"快速浏览 {shortVisits}次 / 深度访问 {longVisits}次（按有效浏览）",
            Icon = "\uE9D9",
            Color = "#FF8C00"
        });
        
        var categoryGroups = sessions
            .GroupBy(ResolveCategory)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        
        
        var focusAvg = sessions.Count > 0
            ? sessions.Average(s => s.Duration > TimeSpan.Zero ? s.FocusRatio : 1.0)
            : 1.0;
        result.Add(new BrowsingPatternItem
        {
            Pattern = "有效浏览率",
            Description = $"{(int)(focusAvg * 100)}%",
            Icon = "",
            Color = focusAvg >= 0.6 ? "#107C10" : "#E81123"
        });
        if (categoryGroups != null)
        {
            result.Add(new BrowsingPatternItem
            {
                Pattern = "主要活动类型",
                Description = categoryGroups.Category,
                Icon = "\uE8FD",
                Color = "#8764B8"
            });
        }
        
        return result;
    }

    private List<WebSessionDetailItem> ComputeRecentVisits(List<Core.Entities.WebSession> sessions)
    {
        return sessions
            .Skip(_currentPage * PageSize)
            .Take(PageSize)
            .Select(session => new WebSessionDetailItem
            {
                Domain = session.Domain,
                Title = session.Title,
                Url = session.Url,
                VisitTime = session.StartTime.ToString("HH:mm:ss"),
                Duration = EffectiveMinutesValue(session) > 0
                    ? $"{(int)EffectiveMinutesValue(session)}分{(int)(EffectiveSecondsValue(session) % 60)}秒"
                    : "-",
                Category = ResolveCategory(session),
                CategoryColor = GetCategoryColor(ResolveCategory(session)),
                ScrollDepth = session.ScrollDepth,
                ClickCount = session.ClickCount,
                HasInteraction = session.HasFormInteraction || session.ClickCount > 0
            })
            .ToList();
    }

    /// <summary>
    /// 网页流量归因（估算）：浏览器进程下行流量按「同日期同小时各域名有效浏览秒数」权重拆分。
    /// 口径：浏览器流量合计只统计 chrome/msedge/firefox；会话按 StartTime 归属小时；
    /// 某小时无浏览器流量或网页会话时不计入。结果为估算，仅供参考。
    /// </summary>
    private List<TrafficAttributionItem> ComputeTrafficAttribution(
        List<Core.Entities.WebSession> sessions,
        List<Core.Entities.ProcessNetworkUsage> browserUsage)
    {
        // 1. 浏览器下行按 (Date, Hour) 聚合
        var downByHour = new Dictionary<(string Date, int Hour), long>();
        foreach (var u in browserUsage)
        {
            if (u.BytesDown <= 0) continue;
            var key = (u.Date, u.Hour);
            downByHour.TryGetValue(key, out var acc);
            downByHour[key] = acc + u.BytesDown;
        }

        if (downByHour.Count == 0) return new List<TrafficAttributionItem>();

        // 2. 会话按 (Date, Hour) 聚合各域名有效秒数
        var secByDomainHour = new Dictionary<string, Dictionary<(string Date, int Hour), double>>();
        foreach (var s in sessions)
        {
            var sec = EffectiveSecondsValue(s);
            if (sec <= 0) continue;
            var key = (s.StartTime.ToString("yyyy-MM-dd"), s.StartTime.Hour);
            if (!secByDomainHour.TryGetValue(s.Domain, out var byHour))
            {
                byHour = new Dictionary<(string, int), double>();
                secByDomainHour[s.Domain] = byHour;
            }

            byHour.TryGetValue(key, out var acc);
            byHour[key] = acc + sec;
        }

        // 3. 该小时总有效秒（仅当该小时存在浏览器流量才参与）
        var totalSecByHour = new Dictionary<(string, int), double>();
        foreach (var byHour in secByDomainHour.Values)
        {
            foreach (var hkv in byHour)
            {
                if (!downByHour.ContainsKey(hkv.Key)) continue;
                totalSecByHour.TryGetValue(hkv.Key, out var acc);
                totalSecByHour[hkv.Key] = acc + hkv.Value;
            }
        }

        // 4. 按小时加权拆分到域名
        var downByDomain = new Dictionary<string, double>();
        foreach (var kv in secByDomainHour)
        {
            foreach (var hkv in kv.Value)
            {
                if (!downByHour.TryGetValue(hkv.Key, out var downBytes)) continue;
                if (!totalSecByHour.TryGetValue(hkv.Key, out var totalSec) || totalSec <= 0) continue;
                var share = hkv.Value / totalSec;
                downByDomain.TryGetValue(kv.Key, out var acc);
                downByDomain[kv.Key] = acc + downBytes * share;
            }
        }

        if (downByDomain.Count == 0) return new List<TrafficAttributionItem>();

        // 5. Top 12 + 汇总
        var ranked = downByDomain.OrderByDescending(x => x.Value).ToList();
        var total = ranked.Sum(x => x.Value);
        var max = ranked.Max(x => x.Value);

        var result = new List<TrafficAttributionItem>();
        foreach (var r in ranked.Take(12))
        {
            var category = GetCategory(r.Key);
            result.Add(new TrafficAttributionItem
            {
                Domain = r.Key,
                Category = category,
                CategoryColor = GetCategoryColor(category),
                DownText = FormatGb(r.Value),
                SharePercent = total > 0 ? Math.Round(100.0 * r.Value / total, 1) : 0,
                BarWidth = Math.Max(4, 320.0 * r.Value / max)
            });
        }

        return result;
    }

    private static string FormatGb(double bytes)
    {
        if (bytes < 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("F2") + " MB";
        return (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
    }

    private async Task<List<Core.Entities.WebSession>> LoadSessionsAsync()
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            Log.Debug("WebDetails: 构建查询");
            var query = dbContext.WebSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= StartDate && s.StartTime <= EndDate.AddDays(1));
            
            if (!string.IsNullOrEmpty(SearchText))
            {
                var searchLower = SearchText.ToLower();
                query = query.Where(s => 
                    s.Domain.ToLower().Contains(searchLower) || 
                    s.Title.ToLower().Contains(searchLower) ||
                    s.Url.ToLower().Contains(searchLower));
            }
            
            if (SelectedCategory != "全部分类")
            {
                // 优先按物化 CategoryName 过滤；历史行回退域名映射
                var category = await dbContext.WebsiteCategories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Name == SelectedCategory);

                if (category != null)
                {
                    query = query.Where(s => s.CategoryId == category.Id || s.CategoryName == SelectedCategory);
                }
                else
                {
                    query = query.Where(s => s.CategoryName == SelectedCategory);
                }
            }
            
            Log.Debug("WebDetails: 执行查询");
            var result = await query.OrderByDescending(s => s.StartTime).ToListAsync();
            Log.Debug("WebDetails: 查询返回 {Count} 条记录", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 WebSessions 失败");
            return new List<Core.Entities.WebSession>();
        }
    }

    private async Task<List<Core.Entities.ProcessNetworkUsage>> LoadBrowserUsageAsync()
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var startKey = StartDate.ToString("yyyy-MM-dd");
            var endKey = EndDate.ToString("yyyy-MM-dd");
            var rows = await dbContext.ProcessNetworkUsages.AsNoTracking()
                .Where(x => string.Compare(x.Date, startKey) >= 0 && string.Compare(x.Date, endKey) <= 0)
                .ToListAsync();

            return rows
                .Where(x => BrowserProcesses.Contains(AppStatsEngine.NormalizeProcessName(x.ProcessName)))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载浏览器流量归因数据失败");
            return new List<Core.Entities.ProcessNetworkUsage>();
        }
    }

    private SummaryStatsResult ComputeSummaryStats(List<Core.Entities.WebSession> sessions)
    {
        var totalVisits = sessions.Count;
        var totalMinutes = sessions.Sum(EffectiveMinutesValue);
        var wallMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        var uniqueDomains = sessions.Select(s => s.Domain).Distinct().Count();
        var avgSeconds = totalVisits > 0 ? sessions.Average(EffectiveSecondsValue) : 0;
        var focusRatio = wallMinutes > 0 ? totalMinutes / wallMinutes : 1.0;

        var hourlyGroups = sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        var topDomain = sessions
            .GroupBy(s => s.Domain)
            .Select(g => new { Domain = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        return new SummaryStatsResult(
            totalVisits.ToString("N0"),
            $"{(int)totalMinutes}分钟",
            uniqueDomains.ToString("N0"),
            $"{(int)avgSeconds}秒",
            hourlyGroups != null ? $"{hourlyGroups.Hour}:00" : "-",
            topDomain?.Domain ?? "-",
            $"{(int)(focusRatio * 100)}%"
        );
    }

}
