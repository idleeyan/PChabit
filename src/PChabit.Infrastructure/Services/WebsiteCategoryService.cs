using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Services;

public interface IWebsiteCategoryService
{
    Task<List<WebsiteCategory>> GetAllCategoriesAsync();
    Task<WebsiteCategory?> GetCategoryByIdAsync(int id);
    Task<WebsiteCategory> CreateCategoryAsync(WebsiteCategory category);
    Task<WebsiteCategory?> UpdateCategoryAsync(WebsiteCategory category);
    Task<bool> DeleteCategoryAsync(int id);
    Task<bool> CategoryExistsAsync(string name, int? excludeId = null);

    Task<List<WebsiteDomainMapping>> GetAllMappingsAsync();
    Task<WebsiteDomainMapping?> GetMappingByDomainAsync(string domain);
    Task<WebsiteDomainMapping> CreateMappingAsync(WebsiteDomainMapping mapping);
    Task<bool> UpdateMappingAsync(WebsiteDomainMapping mapping);
    Task<bool> DeleteMappingAsync(int id);
    Task<List<WebsiteDomainMapping>> GetMappingsByCategoryIdAsync(int categoryId);

    Task<string?> GetCategoryForDomainAsync(string domain);
    Task InitializeDefaultCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>把域名指到分类；categoryId 为 null 则删除映射。返回是否成功。</summary>
    Task<bool> AssignDomainToCategoryAsync(string domain, int? categoryId);

    List<WebsiteCategory> GetAllCategoriesSync();
    List<WebsiteDomainMapping> GetAllMappingsSync();
}

public class WebsiteCategoryService : IWebsiteCategoryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;

    public WebsiteCategoryService(IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<WebsiteCategory>> GetAllCategoriesAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.WebsiteCategories
            .Include(c => c.DomainMappings)
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public List<WebsiteCategory> GetAllCategoriesSync()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        return dbContext.WebsiteCategories
            .Include(c => c.DomainMappings)
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
    }

    public async Task<WebsiteCategory?> GetCategoryByIdAsync(int id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.WebsiteCategories
            .Include(c => c.DomainMappings)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<WebsiteCategory> CreateCategoryAsync(WebsiteCategory category)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        category.CreatedAt = DateTime.Now;
        dbContext.WebsiteCategories.Add(category);
        await dbContext.SaveChangesAsync();

        Log.Information("创建网站分类: {CategoryName}", category.Name);
        return category;
    }

    public async Task<WebsiteCategory?> UpdateCategoryAsync(WebsiteCategory category)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var existing = await dbContext.WebsiteCategories.FindAsync(category.Id);
        if (existing == null) return null;

        existing.Name = category.Name;
        existing.Description = category.Description;
        existing.Color = category.Color;
        existing.Icon = category.Icon;
        existing.SortOrder = category.SortOrder;
        existing.UpdatedAt = DateTime.Now;

        await dbContext.SaveChangesAsync();

        Log.Information("更新网站分类: {CategoryName}", category.Name);
        return existing;
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var category = await dbContext.WebsiteCategories.FindAsync(id);
        if (category == null) return false;

        category.IsActive = false;
        await dbContext.SaveChangesAsync();

        Log.Information("删除网站分类: {CategoryId}", id);
        return true;
    }

    public async Task<bool> CategoryExistsAsync(string name, int? excludeId = null)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var query = dbContext.WebsiteCategories.Where(c => c.Name == name && c.IsActive);
        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<List<WebsiteDomainMapping>> GetAllMappingsAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.WebsiteDomainMappings.ToListAsync();
    }

    public List<WebsiteDomainMapping> GetAllMappingsSync()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        return dbContext.WebsiteDomainMappings.ToList();
    }

    public async Task<WebsiteDomainMapping?> GetMappingByDomainAsync(string domain)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var lowerDomain = domain.ToLower();
        var mappings = await dbContext.WebsiteDomainMappings.ToListAsync();

        foreach (var mapping in mappings.OrderByDescending(m => m.DomainPattern.Length))
        {
            if (DomainMatches(lowerDomain, mapping.DomainPattern))
            {
                return mapping;
            }
        }

        return null;
    }

    public async Task<WebsiteDomainMapping> CreateMappingAsync(WebsiteDomainMapping mapping)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        mapping.CreatedAt = DateTime.Now;
        dbContext.WebsiteDomainMappings.Add(mapping);
        await dbContext.SaveChangesAsync();

        Log.Information("创建网站域名映射: {DomainPattern} -> {CategoryId}", mapping.DomainPattern, mapping.CategoryId);
        return mapping;
    }

    public async Task<bool> UpdateMappingAsync(WebsiteDomainMapping mapping)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var existing = await dbContext.WebsiteDomainMappings.FindAsync(mapping.Id);
        if (existing == null) return false;

        existing.CategoryId = mapping.CategoryId;
        existing.DomainPattern = mapping.DomainPattern;
        existing.UpdatedAt = DateTime.Now;

        await dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteMappingAsync(int id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var mapping = await dbContext.WebsiteDomainMappings.FindAsync(id);
        if (mapping == null) return false;

        dbContext.WebsiteDomainMappings.Remove(mapping);
        await dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<List<WebsiteDomainMapping>> GetMappingsByCategoryIdAsync(int categoryId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.WebsiteDomainMappings
            .Where(m => m.CategoryId == categoryId)
            .ToListAsync();
    }

    public async Task<string?> GetCategoryForDomainAsync(string domain)
    {
        var mapping = await GetMappingByDomainAsync(domain);
        if (mapping != null)
        {
            var category = await GetCategoryByIdAsync(mapping.CategoryId);
            return category?.Name;
        }

        return GetDefaultCategory(domain);
    }

    public async Task<bool> AssignDomainToCategoryAsync(string domain, int? categoryId)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        domain = domain.Trim().ToLowerInvariant();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var existing = await dbContext.WebsiteDomainMappings
            .FirstOrDefaultAsync(m => m.DomainPattern.ToLower() == domain);

        if (categoryId is null or <= 0)
        {
            if (existing == null) return true;
            dbContext.WebsiteDomainMappings.Remove(existing);
            await dbContext.SaveChangesAsync();
            Log.Information("清除域名分类映射: {Domain}", domain);
            return true;
        }

        var category = await dbContext.WebsiteCategories.FindAsync(categoryId.Value);
        if (category == null) return false;

        if (existing == null)
        {
            dbContext.WebsiteDomainMappings.Add(new WebsiteDomainMapping
            {
                DomainPattern = domain,
                CategoryId = categoryId.Value,
                CreatedAt = DateTime.Now
            });
        }
        else
        {
            existing.CategoryId = categoryId.Value;
            existing.UpdatedAt = DateTime.Now;
        }

        await dbContext.SaveChangesAsync();
        Log.Information("域名分类映射: {Domain} → {Category}", domain, category.Name);
        return true;
    }

    public async Task InitializeDefaultCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        if (await dbContext.WebsiteCategories.AnyAsync(cancellationToken))
        {
            return;
        }

        var defaultCategories = new List<WebsiteCategory>
        {
            new() { Name = "搜索", Description = "搜索引擎网站", Color = "#0078D4", Icon = "🔍", SortOrder = 1, IsSystem = true },
            new() { Name = "开发", Description = "开发文档和工具网站", Color = "#512BD4", Icon = "💻", SortOrder = 2, IsSystem = true },
            new() { Name = "视频", Description = "视频和流媒体网站", Color = "#FF8C00", Icon = "🎬", SortOrder = 3, IsSystem = true },
            new() { Name = "社交", Description = "社交媒体网站", Color = "#107C10", Icon = "👥", SortOrder = 4, IsSystem = true },
            new() { Name = "购物", Description = "电商和购物网站", Color = "#E81123", Icon = "🛒", SortOrder = 5, IsSystem = true },
            new() { Name = "邮件", Description = "电子邮件网站", Color = "#00B7C3", Icon = "📧", SortOrder = 6, IsSystem = true },
            new() { Name = "办公", Description = "办公协作网站", Color = "#6B7280", Icon = "📊", SortOrder = 7, IsSystem = true },
            new() { Name = "新闻", Description = "新闻和资讯网站", Color = "#8764B8", Icon = "📰", SortOrder = 8, IsSystem = true },
            new() { Name = "浏览", Description = "其他网站", Color = "#9CA3AF", Icon = "🌐", SortOrder = 99, IsSystem = true }
        };

        var defaultMappings = new List<WebsiteDomainMapping>
        {
            new() { DomainPattern = "google.com", CategoryId = 1 },
            new() { DomainPattern = "baidu.com", CategoryId = 1 },
            new() { DomainPattern = "bing.com", CategoryId = 1 },
            new() { DomainPattern = "github.com", CategoryId = 2 },
            new() { DomainPattern = "stackoverflow.com", CategoryId = 2 },
            new() { DomainPattern = "csdn.net", CategoryId = 2 },
            new() { DomainPattern = "juejin.cn", CategoryId = 2 },
            new() { DomainPattern = "youtube.com", CategoryId = 3 },
            new() { DomainPattern = "bilibili.com", CategoryId = 3 },
            new() { DomainPattern = "netflix.com", CategoryId = 3 },
            new() { DomainPattern = "twitter.com", CategoryId = 4 },
            new() { DomainPattern = "weibo.com", CategoryId = 4 },
            new() { DomainPattern = "zhihu.com", CategoryId = 4 },
            new() { DomainPattern = "amazon.com", CategoryId = 5 },
            new() { DomainPattern = "taobao.com", CategoryId = 5 },
            new() { DomainPattern = "jd.com", CategoryId = 5 },
            new() { DomainPattern = "mail.google.com", CategoryId = 6 },
            new() { DomainPattern = "outlook.com", CategoryId = 6 },
            new() { DomainPattern = "notion.so", CategoryId = 7 },
            new() { DomainPattern = "feishu.cn", CategoryId = 7 },
            new() { DomainPattern = "news.qq.com", CategoryId = 8 },
            new() { DomainPattern = "sina.com.cn", CategoryId = 8 }
        };

        dbContext.WebsiteCategories.AddRange(defaultCategories);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var mapping in defaultMappings)
        {
            var category = defaultCategories.FirstOrDefault(c => c.SortOrder == mapping.CategoryId);
            if (category != null)
            {
                mapping.CategoryId = category.Id;
            }
        }

        dbContext.WebsiteDomainMappings.AddRange(defaultMappings);
        await dbContext.SaveChangesAsync(cancellationToken);

        Log.Information("已初始化默认网站分类和映射");
    }

    private static bool DomainMatches(string domain, string pattern)
    {
        var lowerPattern = pattern.ToLower();

        if (lowerPattern.StartsWith("*."))
        {
            var suffix = lowerPattern.Substring(2);
            return domain.EndsWith("." + suffix) || domain == suffix;
        }

        return domain == lowerPattern || domain.EndsWith("." + lowerPattern);
    }

    private static string? GetDefaultCategory(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return "浏览";

        var lowerDomain = domain.ToLower();

        if (lowerDomain.Contains("google") || lowerDomain.Contains("baidu") ||
            lowerDomain.Contains("bing") || lowerDomain.Contains("sogou"))
            return "搜索";

        if (lowerDomain.Contains("github") || lowerDomain.Contains("gitlab") ||
            lowerDomain.Contains("stackoverflow") || lowerDomain.Contains("csdn") ||
            lowerDomain.Contains("juejin") || lowerDomain.Contains("segmentfault"))
            return "开发";

        if (lowerDomain.Contains("youtube") || lowerDomain.Contains("bilibili") ||
            lowerDomain.Contains("netflix") || lowerDomain.Contains("youku") ||
            lowerDomain.Contains("iqiyi") || lowerDomain.Contains("douyin"))
            return "视频";

        if (lowerDomain.Contains("twitter") || lowerDomain.Contains("weibo") ||
            lowerDomain.Contains("facebook") || lowerDomain.Contains("instagram") ||
            lowerDomain.Contains("linkedin") || lowerDomain.Contains("zhihu") ||
            lowerDomain.Contains("xiaohongshu") || lowerDomain.Contains("douban"))
            return "社交";

        if (lowerDomain.Contains("amazon") || lowerDomain.Contains("taobao") ||
            lowerDomain.Contains("jd") || lowerDomain.Contains("tmall") ||
            lowerDomain.Contains("pinduoduo"))
            return "购物";

        if (lowerDomain.Contains("mail") || lowerDomain.Contains("outlook") ||
            lowerDomain.Contains("gmail"))
            return "邮件";

        if (lowerDomain.Contains("notion") || lowerDomain.Contains("docs.qq") ||
            lowerDomain.Contains("yuque") || lowerDomain.Contains("confluence") ||
            lowerDomain.Contains("feishu") || lowerDomain.Contains("dingtalk"))
            return "办公";

        if (lowerDomain.Contains("news") || lowerDomain.Contains("bbc") ||
            lowerDomain.Contains("cnn") || lowerDomain.Contains("sina") ||
            lowerDomain.Contains("sohu") || lowerDomain.Contains("163"))
            return "新闻";

        return "浏览";
    }
}
