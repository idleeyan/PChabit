using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.UseCases;

/// <summary>应用分类 CRUD 服务（进程名映射差异实现）。</summary>
public sealed class ProgramCategoryCrudService
    : CategoryCrudService<ProgramCategory, ProgramCategoryMapping>
{
    public ProgramCategoryCrudService(IDbContextFactory<PChabitDbContext> dbFactory)
        : base(dbFactory) { }

    protected override string LogPrefix => "ProgramCategoryCrud";

    protected override Task<List<ProgramCategory>> QueryCategoriesAsync(PChabitDbContext db)
        => db.ProgramCategories
            .Include(c => c.ProgramMappings)
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

    protected override Task<List<ProgramCategoryMapping>> QueryMappingsAsync(PChabitDbContext db)
        => db.ProgramCategoryMappings.ToListAsync();

    protected override void AddCategoryTo(PChabitDbContext db, ProgramCategory category)
    {
        category.IsActive = true;
        category.CreatedAt = DateTime.Now;
        db.ProgramCategories.Add(category);
    }

    protected override Task<ProgramCategory?> FindCategoryAsync(PChabitDbContext db, int id)
        => Task.FromResult(db.ProgramCategories.Find(id));

    protected override void AddMappingTo(PChabitDbContext db, ProgramCategoryMapping mapping)
    {
        mapping.CreatedAt = DateTime.Now;
        db.ProgramCategoryMappings.Add(mapping);
    }

    protected override Task<ProgramCategoryMapping?> FindMappingAsync(PChabitDbContext db, int id)
        => Task.FromResult(db.ProgramCategoryMappings.Find(id));

    protected override void RemoveMappingFrom(PChabitDbContext db, ProgramCategoryMapping mapping)
        => db.ProgramCategoryMappings.Remove(mapping);
}

/// <summary>网站分类 CRUD 服务（域名映射差异实现）。</summary>
public sealed class WebsiteCategoryCrudService
    : CategoryCrudService<WebsiteCategory, WebsiteDomainMapping>
{
    public WebsiteCategoryCrudService(IDbContextFactory<PChabitDbContext> dbFactory)
        : base(dbFactory) { }

    protected override string LogPrefix => "WebsiteCategoryCrud";

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
}
