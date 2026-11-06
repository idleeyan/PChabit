using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>
/// 网页访问详情页查询服务（3.79.0 从 <c>WebDetailsViewModel</c> 四分部收口，
/// 改造计划阶段 E3-6）。VM 不再注入 <c>IDbContextFactory</c>；
/// 分类选项 / 应用小时活动 / 网页会话 / 浏览器流量归因四类查询收口至此，
/// 各自的容错降级与过滤语义逐字保留。
/// </summary>
public sealed class WebDetailsQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    public WebDetailsQueryService(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>加载启用中的网站分类名（按 SortOrder 排序）。</summary>
    public async Task<List<string>> LoadCategoryNamesAsync()
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        return await dbContext.WebsiteCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Name)
            .ToListAsync();
    }

    /// <summary>按时间段加载应用小时聚合点（只取时间与时长；失败降级空列表）。</summary>
    public async Task<List<(int Hour, string Label, double Minutes)>> LoadAppHourlyAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var end = endDate.AddDays(1);

            var rows = await db.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= startDate && s.StartTime < end)
                .Select(s => new { s.StartTime, s.Duration })
                .ToListAsync();

            var result = new List<(int Hour, string Label, double Minutes)>(24);
            for (var h = 0; h < 24; h++)
            {
                var mins = rows
                    .Where(s => s.StartTime.Hour == h)
                    .Sum(s => s.Duration.TotalMinutes);
                result.Add((h, $"{h:00}:00", mins));
            }
            return result;
        }
        catch (Exception ex)
        {
            // 应用侧数据缺失不应影响网页统计展示
            Serilog.Log.Warning(ex, "WebDetails: 应用小时活动查询失败，网页侧照常展示");
            return new List<(int Hour, string Label, double Minutes)>();
        }
    }

    /// <summary>按日期/搜索词/分类加载网页会话（StartTime 倒序；失败降级空列表）。</summary>
    public async Task<List<WebSession>> LoadSessionsAsync(
        DateTime startDate,
        DateTime endDate,
        string? searchText,
        string selectedCategory)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            Serilog.Log.Debug("WebDetails: 构建查询");
            var query = dbContext.WebSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= startDate && s.StartTime <= endDate.AddDays(1));

            if (!string.IsNullOrEmpty(searchText))
            {
                var searchLower = searchText.ToLower();
                query = query.Where(s =>
                    s.Domain.ToLower().Contains(searchLower) ||
                    s.Title.ToLower().Contains(searchLower) ||
                    s.Url.ToLower().Contains(searchLower));
            }

            if (selectedCategory != "全部分类")
            {
                // 优先按物化 CategoryName 过滤；历史行回退域名映射
                var category = await dbContext.WebsiteCategories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Name == selectedCategory);

                if (category != null)
                {
                    query = query.Where(s => s.CategoryId == category.Id || s.CategoryName == selectedCategory);
                }
                else
                {
                    query = query.Where(s => s.CategoryName == selectedCategory);
                }
            }

            Serilog.Log.Debug("WebDetails: 执行查询");
            var result = await query.OrderByDescending(s => s.StartTime).ToListAsync();
            Serilog.Log.Debug("WebDetails: 查询返回 {Count} 条记录", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载 WebSessions 失败");
            return new List<WebSession>();
        }
    }

    /// <summary>按日期范围加载浏览器进程流量行（失败降级空列表）。</summary>
    public async Task<List<ProcessNetworkUsage>> LoadBrowserUsageAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var startKey = startDate.ToString("yyyy-MM-dd");
            var endKey = endDate.ToString("yyyy-MM-dd");
            return await dbContext.ProcessNetworkUsages.AsNoTracking()
                .Where(x => string.Compare(x.Date, startKey) >= 0 && string.Compare(x.Date, endKey) <= 0)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载浏览器流量归因数据失败");
            return new List<ProcessNetworkUsage>();
        }
    }
}
