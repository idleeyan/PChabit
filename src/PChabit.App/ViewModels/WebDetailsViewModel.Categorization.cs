using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;


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

    /// <summary>一次性加载 域名映射(仅启用分类) 与 分类名→颜色，供整批会话解析。</summary>
    private void LoadCategoryLookups()
    {
        var categories = _websiteCategoryService.GetAllCategoriesSync();
        var colorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var catById = new Dictionary<int, string>();
        foreach (var c in categories)
        {
            colorMap[c.Name] = string.IsNullOrWhiteSpace(c.Color) ? "#9CA3AF" : c.Color;
            catById[c.Id] = c.Name;
        }

        var domainMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _websiteCategoryService.GetAllMappingsSync()
                     .OrderByDescending(m => m.DomainPattern.Length))
        {
            if (!catById.TryGetValue(m.CategoryId, out var name)) continue;
            var key = m.DomainPattern.ToLowerInvariant();
            domainMap.TryAdd(key, name); // 最长 pattern 已排在前面
        }

        _domainCategoryMap = domainMap;
        _categoryColorMap = colorMap;
    }

    /// <summary>解析域名分类：用户映射优先，否则服务内置关键词兜底（全项目唯一规则）。</summary>
    private string GetCategory(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return "浏览";

        var lowerDomain = domain.ToLowerInvariant();
        foreach (var kvp in _domainCategoryMap)
        {
            if (WebsiteCategoryService.DomainMatches(lowerDomain, kvp.Key))
                return kvp.Value;
        }
        return _websiteCategoryService.GetDefaultCategoryName(domain);
    }

    private string GetCategoryColor(string category) =>
        _categoryColorMap.TryGetValue(category, out var color) ? color : "#9CA3AF";
}
