using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.UseCases;

/// <summary>
/// 分类 CRUD 通用服务（3.80.0 从 <c>CategoryCrudViewModelBase</c> 收口，
/// 改造计划阶段 E3-7）。模板方法与抽象钩子结构自原 VM 基类整体平移：
/// 读（Initialize）+ 增删改（写操作）+ SaveChanges 时点全部保持一致；
/// 线程约定（查询线程池）由调用方保证，本服务不做 UI 编排。
/// 子类用 EF DbSet 差异实现钩子（应用分类 / 网站分类各一份具体服务）。
/// </summary>
public abstract class CategoryCrudService<TCategory, TMapping>
    where TCategory : class, ICategoryEntity
    where TMapping : class, IMappingEntity
{
    protected readonly IDbContextFactory<PChabitDbContext> DbFactory;

    protected CategoryCrudService(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        DbFactory = dbFactory;
    }

    /// <summary>日志前缀，用于区分两套分类的日志。</summary>
    protected abstract string LogPrefix { get; }

    // ===== 差异点（由具体服务实现） =====

    /// <summary>读取分类列表（含导航集合 + 过滤 + 排序）。</summary>
    protected abstract Task<List<TCategory>> QueryCategoriesAsync(PChabitDbContext db);

    /// <summary>读取全部映射。</summary>
    protected abstract Task<List<TMapping>> QueryMappingsAsync(PChabitDbContext db);

    /// <summary>把新分类写入 DbContext（不保存）。</summary>
    protected abstract void AddCategoryTo(PChabitDbContext db, TCategory category);

    /// <summary>按 Id 找分类（用于更新 / 软删除）。</summary>
    protected abstract Task<TCategory?> FindCategoryAsync(PChabitDbContext db, int id);

    /// <summary>把新映射写入 DbContext（不保存）。</summary>
    protected abstract void AddMappingTo(PChabitDbContext db, TMapping mapping);

    /// <summary>按 Id 找映射（用于删除）。</summary>
    protected abstract Task<TMapping?> FindMappingAsync(PChabitDbContext db, int id);

    /// <summary>把映射从 DbContext 移除。</summary>
    protected abstract void RemoveMappingFrom(PChabitDbContext db, TMapping mapping);

    // ===== 同构逻辑（统一实现，与原 VM 基类逐字对齐） =====

    /// <summary>读取分类列表与全部映射（初始化/刷新共用）。</summary>
    public async Task<(List<TCategory> Categories, List<TMapping> Mappings)> LoadAsync()
    {
        List<TCategory> categories;
        List<TMapping> mappings;

        await using (var db = await DbFactory.CreateDbContextAsync())
        {
            categories = await QueryCategoriesAsync(db);
            mappings = await QueryMappingsAsync(db);
        }

        Serilog.Log.Information("{Prefix}: 加载完成，分类 {C} 个、映射 {M} 条",
            LogPrefix, categories.Count, mappings.Count);
        return (categories, mappings);
    }

    public async Task AddCategoryAsync(TCategory category)
    {
        await using (var db = await DbFactory.CreateDbContextAsync())
        {
            AddCategoryTo(db, category);
            await db.SaveChangesAsync();
        }
        Serilog.Log.Information("{Prefix}: 添加分类成功 {Name}", LogPrefix, category.Name);
    }

    public async Task UpdateCategoryAsync(TCategory category)
    {
        await using (var db = await DbFactory.CreateDbContextAsync())
        {
            var existing = await FindCategoryAsync(db, category.Id);
            if (existing != null)
            {
                existing.Name = category.Name;
                existing.Description = category.Description;
                existing.Color = category.Color;
                existing.Icon = category.Icon;
                existing.SortOrder = category.SortOrder;
                existing.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync();
            }
        }
        Serilog.Log.Information("{Prefix}: 更新分类成功 {Name}", LogPrefix, category.Name);
    }

    /// <summary>软删除分类（置 IsActive=false，保留历史数据归属）。</summary>
    public async Task DeleteCategoryAsync(int categoryId)
    {
        await using (var db = await DbFactory.CreateDbContextAsync())
        {
            var category = await FindCategoryAsync(db, categoryId);
            if (category != null)
            {
                category.IsActive = false;
                await db.SaveChangesAsync();
            }
        }
        Serilog.Log.Information("{Prefix}: 删除分类成功 {Id}", LogPrefix, categoryId);
    }

    public async Task AddMappingAsync(TMapping mapping)
    {
        await using (var db = await DbFactory.CreateDbContextAsync())
        {
            AddMappingTo(db, mapping);
            await db.SaveChangesAsync();
        }
        Serilog.Log.Information("{Prefix}: 添加映射成功 Id={Id}", LogPrefix, mapping.Id);
    }

    public async Task DeleteMappingAsync(int mappingId)
    {
        await using (var db = await DbFactory.CreateDbContextAsync())
        {
            var mapping = await FindMappingAsync(db, mappingId);
            if (mapping != null)
            {
                RemoveMappingFrom(db, mapping);
                await db.SaveChangesAsync();
            }
        }
        Serilog.Log.Information("{Prefix}: 删除映射成功 {Id}", LogPrefix, mappingId);
    }
}
