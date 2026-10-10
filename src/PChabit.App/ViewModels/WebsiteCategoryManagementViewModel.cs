using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

/// <summary>
/// 网站分类管理（3.33.0 由 CategoryCrudViewModelBase 承接通用 CRUD，
/// 本类只提供「域名映射」相关的差异实现）。
///
/// 3.33.0 移除了此处内联的第二份默认分类种子数据——
/// 种子补齐统一由 <see cref="PChabit.Infrastructure.Services.WebsiteCategoryService.EnsureCatalogSeededAsync"/>
/// 在数据库初始化阶段完成（见 DatabaseInitializer.SeedDefaultDataAsync）。
/// </summary>
public partial class WebsiteCategoryManagementViewModel
    : CategoryCrudViewModelBase<WebsiteCategory, WebsiteDomainMapping>
{
    protected override string LogPrefix => "WebsiteCategoryManagementViewModel";

    public WebsiteCategoryManagementViewModel(IDbContextFactory<PChabitDbContext> dbFactory) : base(dbFactory)
    {
    }

    protected override Task<List<WebsiteCategory>> QueryCategoriesAsync(PChabitDbContext db)
        => db.WebsiteCategories
            .Include(c => c.DomainMappings)
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

    protected override Task<List<WebsiteDomainMapping>> QueryMappingsAsync(PChabitDbContext db)
        => db.WebsiteDomainMappings.ToListAsync();

    protected override void AddCategoryTo(PChabitDbContext db, WebsiteCategory category)
    {
        category.IsActive = true;
        category.CreatedAt = DateTime.Now;
        db.WebsiteCategories.Add(category);
    }

    protected override Task<WebsiteCategory?> FindCategoryAsync(PChabitDbContext db, int id)
        => Task.FromResult(db.WebsiteCategories.Find(id));

    protected override void AddMappingTo(PChabitDbContext db, WebsiteDomainMapping mapping)
    {
        mapping.CreatedAt = DateTime.Now;
        db.WebsiteDomainMappings.Add(mapping);
    }

    protected override Task<WebsiteDomainMapping?> FindMappingAsync(PChabitDbContext db, int id)
        => Task.FromResult(db.WebsiteDomainMappings.Find(id));

    protected override void RemoveMappingFrom(PChabitDbContext db, WebsiteDomainMapping mapping)
        => db.WebsiteDomainMappings.Remove(mapping);

    /// <summary>搜索键 = 域名模式。</summary>
    protected override string GetMappingKey(WebsiteDomainMapping mapping) => mapping.DomainPattern;
}