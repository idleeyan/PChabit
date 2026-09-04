using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;


namespace PChabit.App.ViewModels;

public partial class WebDetailsViewModel : DbSafeViewModel<WebDetailsViewModel.WebStatsData>
{
    private static bool DomainMatches(string domain, string pattern)
    {
        var lowerPattern = pattern.ToLower();
        if (lowerPattern.StartsWith("*."))
        {
            var suffix = lowerPattern[2..];
            return domain.EndsWith("." + suffix) || domain == suffix;
        }
        return domain == lowerPattern || domain.EndsWith("." + lowerPattern);
    }

    private List<DomainStatItem> ComputeDomainStats(List<Core.Entities.WebSession> sessions)
    {
        return sessions
            .GroupBy(s => s.Domain)
            .Select(g => new DomainStatItem
            {
                Domain = g.Key,
                VisitCount = g.Count(),
                TotalDuration = g.Sum(s => s.Duration.TotalMinutes),
                AvgDuration = g.Average(s => s.Duration.TotalSeconds),
                Category = GetCategory(g.Key),
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
        
        var shortVisits = sessions.Count(s => s.Duration.TotalSeconds < 30);
        var longVisits = sessions.Count(s => s.Duration.TotalMinutes >= 5);
        
        result.Add(new BrowsingPatternItem
        {
            Pattern = "访问时长分布",
            Description = $"快速浏览 {shortVisits}次 / 深度访问 {longVisits}次",
            Icon = "\uE9D9",
            Color = "#FF8C00"
        });
        
        var categoryGroups = sessions
            .GroupBy(s => GetCategory(s.Domain))
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        
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
                Duration = session.Duration.TotalSeconds > 0 
                    ? $"{(int)session.Duration.TotalMinutes}分{(int)session.Duration.Seconds}秒"
                    : "-",
                Category = GetCategory(session.Domain),
                CategoryColor = GetCategoryColor(GetCategory(session.Domain)),
                ScrollDepth = session.ScrollDepth,
                ClickCount = session.ClickCount,
                HasInteraction = session.HasFormInteraction || session.ClickCount > 0
            })
            .ToList();
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
                // 通过 WebsiteDomainMappings 查询该分类的域名模式
                var domainPatterns = await dbContext.WebsiteDomainMappings
                    .AsNoTracking()
                    .Include(m => m.Category)
                    .Where(m => m.Category != null && m.Category.Name == SelectedCategory)
                    .Select(m => m.DomainPattern)
                    .Distinct()
                    .ToListAsync();

                if (domainPatterns.Any())
                {
                    var lowerPatterns = domainPatterns.Select(p => p.ToLower()).ToList();
                    query = query.Where(s => lowerPatterns.Any(p => s.Domain.ToLower().Contains(p)));
                }
                else
                {
                    // 没有匹配的域名模式则返回空，避免错误的中文关键词硬匹配
                    return new List<Core.Entities.WebSession>();
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

    private async Task<Dictionary<string, string>> LoadDomainCategoryMapAsync()
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        var mappings = await dbContext.WebsiteDomainMappings
            .AsNoTracking()
            .Include(m => m.Category)
            .Where(m => m.Category != null && m.Category.IsActive)
            .ToListAsync();

        var map = new Dictionary<string, string>();
        foreach (var m in mappings.Where(m => m.Category != null))
        {
            map[m.DomainPattern] = m.Category!.Name;
        }
        return map;
    }

    private SummaryStatsResult ComputeSummaryStats(List<Core.Entities.WebSession> sessions)
    {
        var totalVisits = sessions.Count;
        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        var uniqueDomains = sessions.Select(s => s.Domain).Distinct().Count();
        var avgSeconds = totalVisits > 0 ? sessions.Average(s => s.Duration.TotalSeconds) : 0;
        
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
            topDomain?.Domain ?? "-"
        );
    }

}
