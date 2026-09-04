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
    private async Task EnsureCategoriesInitializedAsync()
    {
        if (_isCategoriesInitialized) return;
        _isCategoriesInitialized = true;
    }

    private async Task LoadCategoryOptionsAsync()
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        var categoryNames = await dbContext.WebsiteCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Name)
            .ToListAsync();

        var options = new List<string> { "全部分类" };
        options.AddRange(categoryNames);

        await RunOnUIThreadAsync(() =>
        {
            CategoryOptions.Clear();
            CategoryOptions.AddRange(options);
            return Task.CompletedTask;
        });
    }

    private string GetCategory(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return "浏览";
        
        var lowerDomain = domain.ToLower();
        foreach (var kvp in _domainCategoryMap)
        {
            if (DomainMatches(lowerDomain, kvp.Key))
                return kvp.Value;
        }
        return GetCategoryFallback(domain);
    }

    private static string GetCategoryFallback(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return "浏览";
        
        var lowerDomain = domain.ToLower();
        
        if (lowerDomain.Contains("google") || lowerDomain.Contains("baidu") || 
            lowerDomain.Contains("bing") || lowerDomain.Contains("sogou") ||
            lowerDomain.Contains("duckduckgo"))
            return "搜索";
        
        if (lowerDomain.Contains("github") || lowerDomain.Contains("gitlab") ||
            lowerDomain.Contains("stackoverflow") || lowerDomain.Contains("csdn") ||
            lowerDomain.Contains("juejin") || lowerDomain.Contains("segmentfault") ||
            lowerDomain.Contains("npmjs") || lowerDomain.Contains("nuget"))
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
            lowerDomain.Contains("gmail") || (lowerDomain.Contains("qq") && lowerDomain.Contains("mail")))
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

    private string GetCategoryColor(string category)
    {
        return GetCategoryColorHex(category);
    }

    private static string GetCategoryColorHex(string category)
    {
        return category switch
        {
            "搜索" => "#0078D4",
            "开发" => "#512BD4",
            "视频" => "#FF8C00",
            "社交" => "#107C10",
            "购物" => "#E81123",
            "邮件" => "#00B7C3",
            "办公" => "#6B7280",
            "新闻" => "#8764B8",
            _ => "#9CA3AF"
        };
    }

}

