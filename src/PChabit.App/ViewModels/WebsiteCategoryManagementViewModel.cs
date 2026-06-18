using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public partial class WebsiteCategoryManagementViewModel : ViewModelBase
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private WebsiteCategory? _selectedCategory;

    [ObservableProperty]
    private bool _isCategorySelected;

    [ObservableProperty]
    private int _totalCategories;

    [ObservableProperty]
    private int _totalMappings;

    [ObservableProperty]
    private string _newDomainPattern = string.Empty;

    public ObservableCollection<WebsiteCategory> Categories { get; } = new();
    public ObservableCollection<WebsiteDomainMapping> DomainMappings { get; } = new();
    public ObservableCollection<WebsiteDomainMapping> FilteredMappings { get; } = new();

    public WebsiteCategoryManagementViewModel(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task InitializeAsync()
    {
        Log.Information("WebsiteCategoryManagementViewModel: InitializeAsync 开始");
        IsLoading = true;

        try
        {
            // Phase 1: 线程池 — 初始化默认分类 + DB 查询
            var (categories, mappings) = await Task.Run(async () =>
            {
                await using var dbContext = await _dbFactory.CreateDbContextAsync();

                // 初始化默认分类（首次运行）
                if (!await dbContext.WebsiteCategories.AnyAsync())
                {
                    var defaultCategories = new List<WebsiteCategory>
                    {
                        new() { Name = "搜索", Description = "搜索引擎网站", Color = "#0078D4", Icon = "🔍", SortOrder = 1, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now },
                        new() { Name = "开发", Description = "开发文档和工具网站", Color = "#512BD4", Icon = "💻", SortOrder = 2, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now },
                        new() { Name = "视频", Description = "视频和流媒体网站", Color = "#FF8C00", Icon = "🎬", SortOrder = 3, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now },
                        new() { Name = "社交", Description = "社交媒体网站", Color = "#107C10", Icon = "👥", SortOrder = 4, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now },
                        new() { Name = "购物", Description = "电商和购物网站", Color = "#E81123", Icon = "🛒", SortOrder = 5, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now },
                        new() { Name = "邮件", Description = "电子邮件网站", Color = "#00B7C3", Icon = "📧", SortOrder = 6, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now },
                        new() { Name = "办公", Description = "办公协作网站", Color = "#6B7280", Icon = "📊", SortOrder = 7, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now },
                        new() { Name = "新闻", Description = "新闻和资讯网站", Color = "#8764B8", Icon = "📰", SortOrder = 8, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now },
                        new() { Name = "浏览", Description = "其他网站", Color = "#9CA3AF", Icon = "🌐", SortOrder = 99, IsSystem = true, IsActive = true, CreatedAt = DateTime.Now }
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
                    await dbContext.SaveChangesAsync();

                    foreach (var mapping in defaultMappings)
                    {
                        var category = defaultCategories.FirstOrDefault(c => c.SortOrder == mapping.CategoryId);
                        if (category != null)
                        {
                            mapping.CategoryId = category.Id;
                        }
                    }

                    dbContext.WebsiteDomainMappings.AddRange(defaultMappings);
                    await dbContext.SaveChangesAsync();

                    Log.Information("已初始化默认网站分类和映射");
                }

                var cats = await dbContext.WebsiteCategories
                    .Include(c => c.DomainMappings)
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .ThenBy(c => c.Name)
                    .ToListAsync();

                var maps = await dbContext.WebsiteDomainMappings.ToListAsync();

                return (cats, maps);
            });

            // Phase 2: UI 线程 — ObservableCollection 更新
            await RunOnUIThreadAsync(() =>
            {
                Categories.Clear();
                foreach (var category in categories)
                {
                    Categories.Add(category);
                }
                TotalCategories = Categories.Count;

                DomainMappings.Clear();
                foreach (var mapping in mappings)
                {
                    DomainMappings.Add(mapping);
                }
                TotalMappings = DomainMappings.Count;
                return Task.CompletedTask;
            });

            Log.Information("WebsiteCategoryManagementViewModel: 初始化完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebsiteCategoryManagementViewModel: 初始化失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectCategory(WebsiteCategory? category)
    {
        SelectedCategory = category;
        IsCategorySelected = category != null;

        FilterMappings();

        Log.Information("选中网站分类: {CategoryName}", category?.Name ?? "无");
    }

    private void FilterMappings()
    {
        FilteredMappings.Clear();

        var query = DomainMappings.AsEnumerable();

        if (SelectedCategory != null)
        {
            query = query.Where(m => m.CategoryId == SelectedCategory.Id);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(m =>
                m.DomainPattern.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var mapping in query)
        {
            FilteredMappings.Add(mapping);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterMappings();
    }

    public async Task RefreshAsync()
    {
        await InitializeAsync();
    }

    public async Task AddCategoryAsync(WebsiteCategory category)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            category.IsActive = true;
            category.CreatedAt = DateTime.Now;
            dbContext.WebsiteCategories.Add(category);
            await dbContext.SaveChangesAsync();
            await InitializeAsync();
            Log.Information("添加网站分类成功: {CategoryName}", category.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "添加网站分类失败");
        }
    }

    public async Task UpdateCategoryAsync(WebsiteCategory category)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            var existing = await dbContext.WebsiteCategories.FindAsync(category.Id);
            if (existing != null)
            {
                existing.Name = category.Name;
                existing.Description = category.Description;
                existing.Color = category.Color;
                existing.Icon = category.Icon;
                existing.SortOrder = category.SortOrder;
                existing.UpdatedAt = DateTime.Now;
                await dbContext.SaveChangesAsync();
            }
            await InitializeAsync();
            Log.Information("更新网站分类成功: {CategoryName}", category.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新网站分类失败");
        }
    }

    public async Task DeleteCategoryAsync(int categoryId)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            var category = await dbContext.WebsiteCategories.FindAsync(categoryId);
            if (category != null)
            {
                category.IsActive = false;
                await dbContext.SaveChangesAsync();
            }
            await InitializeAsync();

            if (SelectedCategory?.Id == categoryId)
            {
                SelectCategory(null);
            }

            Log.Information("删除网站分类成功: {CategoryId}", categoryId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除网站分类失败");
        }
    }

    public async Task AddMappingAsync(WebsiteDomainMapping mapping)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            mapping.CreatedAt = DateTime.Now;
            dbContext.WebsiteDomainMappings.Add(mapping);
            await dbContext.SaveChangesAsync();
            await InitializeAsync();
            FilterMappings();

            Log.Information("添加域名映射成功: {DomainPattern} -> {CategoryId}", mapping.DomainPattern, mapping.CategoryId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "添加域名映射失败");
        }
    }

    public async Task DeleteMappingAsync(int mappingId)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            var mapping = await dbContext.WebsiteDomainMappings.FindAsync(mappingId);
            if (mapping != null)
            {
                dbContext.WebsiteDomainMappings.Remove(mapping);
                await dbContext.SaveChangesAsync();
            }
            await InitializeAsync();
            FilterMappings();

            Log.Information("删除域名映射成功: {MappingId}", mappingId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除域名映射失败");
        }
    }

    public string GetCategoryName(int categoryId)
    {
        return Categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? "未知";
    }

    public string GetCategoryColor(int categoryId)
    {
        return Categories.FirstOrDefault(c => c.Id == categoryId)?.Color ?? "#95A5A6";
    }

    public string GetCategoryIcon(int categoryId)
    {
        return Categories.FirstOrDefault(c => c.Id == categoryId)?.Icon ?? "🌐";
    }
}
