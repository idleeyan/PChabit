using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

/// <summary>
/// 分类管理 CRUD 的公共骨架（3.33.0）。
///
/// 应用分类（ProgramCategory + ProgramCategoryMapping）与网站分类
/// （WebsiteCategory + WebsiteDomainMapping）两组实体结构同构，CRUD 流程逐行对称，
/// 此前是两个各 320+ 行的独立 ViewModel，改一处 UI 行为要记得同步改另一处，
/// 且 WebsiteCategoryManagementViewModel 里还内联了第二份种子数据副本。
/// 这里把同构部分上移，差异点由子类用少量委托提供。
///
/// 线程约定（陷阱 5.6 / 5.5）：DB 查询在线程池，ObservableCollection 填充在 UI 线程；
/// 禁止在 Task.Run 内直接动集合。
/// </summary>
/// <typeparam name="TCategory">分类实体，须实现 <see cref="ICategoryEntity"/></typeparam>
/// <typeparam name="TMapping">映射实体，须实现 <see cref="IMappingEntity"/></typeparam>
public abstract partial class CategoryCrudViewModelBase<TCategory, TMapping> : ViewModelBase
    where TCategory : class, ICategoryEntity
    where TMapping : class, IMappingEntity
{
    protected readonly IDbContextFactory<PChabitDbContext> DbFactory;

    /// <summary>日志前缀，用于区分两套分类的日志。</summary>
    protected abstract string LogPrefix { get; }

    /// <summary>分类副文本（网站分类无意义，可返回空）。</summary>
    protected virtual string GetCategorySubText(TCategory category) => string.Empty;

    /// <summary>映射的副文本（仅应用分类有进程别名）。</summary>
    protected virtual string GetMappingSubText(TMapping mapping) => string.Empty;

    // ===== 以下为差异点，由子类实现 =====

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

    /// <summary>映射的匹配文本（搜索过滤与列表显示共用）。</summary>
    protected abstract string GetMappingKey(TMapping mapping);

    // ===== 以下为同构逻辑，统一实现 =====

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalCategories;

    [ObservableProperty]
    private int _totalMappings;

    [ObservableProperty]
    private TCategory? _selectedCategory;

    [ObservableProperty]
    private bool _isCategorySelected;

    public ObservableCollection<TCategory> Categories { get; } = new();
    public ObservableCollection<TMapping> AllMappings { get; } = new();
    public ObservableCollection<TMapping> FilteredMappings { get; } = new();

    protected CategoryCrudViewModelBase(IDbContextFactory<PChabitDbContext> dbFactory) : base()
    {
        DbFactory = dbFactory;
    }

    public async Task InitializeAsync()
    {
        Log.Information("{Prefix}: InitializeAsync 开始", LogPrefix);
        IsLoading = true;

        try
        {
            // Phase 1: 线程池 — DB 查询
            List<TCategory> categories;
            List<TMapping> mappings;

            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                categories = await QueryCategoriesAsync(db);
                mappings = await QueryMappingsAsync(db);
            }

            // Phase 2: UI 线程 — ObservableCollection 更新（陷阱 5.6）
            await RunOnUIThreadAsync(() =>
            {
                Categories.Clear();
                foreach (var c in categories) Categories.Add(c);
                TotalCategories = Categories.Count;

                AllMappings.Clear();
                foreach (var m in mappings) AllMappings.Add(m);
                TotalMappings = AllMappings.Count;
                return Task.CompletedTask;
            });

            Log.Information("{Prefix}: 初始化完成，分类 {C} 个、映射 {M} 条",
                LogPrefix, Categories.Count, AllMappings.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Prefix}: 初始化失败", LogPrefix);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task RefreshAsync() => InitializeAsync();

    /// <summary>选中分类（供 XAML 的 SelectionChanged 调用）。</summary>
    [RelayCommand]
    public void SelectCategory(TCategory? category)
    {
        SelectedCategory = category;
        IsCategorySelected = category != null;
        ApplyFilter();
        Log.Information("{Prefix}: 选中分类 {Name}", LogPrefix, category?.Name ?? "无");
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>按「选中分类 + 搜索词」过滤映射列表。</summary>
    public void ApplyFilter()
    {
        var selectedId = SelectedCategory?.Id;
        var keyword = SearchText?.Trim();

        var query = AllMappings.AsEnumerable();

        if (selectedId.HasValue)
        {
            var id = selectedId.Value;
            query = query.Where(m => m.CategoryId == id);
        }

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(m =>
                GetMappingKey(m).Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        FilteredMappings.Clear();
        foreach (var m in query) FilteredMappings.Add(m);
    }

    public async Task AddCategoryAsync(TCategory category)
    {
        try
        {
            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                AddCategoryTo(db, category);
                await db.SaveChangesAsync();
            }
            await InitializeAsync();
            Log.Information("{Prefix}: 添加分类成功 {Name}", LogPrefix, category.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Prefix}: 添加分类失败", LogPrefix);
        }
    }

    public async Task UpdateCategoryAsync(TCategory category)
    {
        try
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
            await InitializeAsync();
            Log.Information("{Prefix}: 更新分类成功 {Name}", LogPrefix, category.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Prefix}: 更新分类失败", LogPrefix);
        }
    }

    /// <summary>软删除分类（置 IsActive=false，保留历史数据归属）。</summary>
    public async Task DeleteCategoryAsync(int categoryId)
    {
        try
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

            await InitializeAsync();

            if (SelectedCategory?.Id == categoryId)
            {
                SelectCategory(null);
            }

            Log.Information("{Prefix}: 删除分类成功 {Id}", LogPrefix, categoryId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Prefix}: 删除分类失败", LogPrefix);
        }
    }

    public async Task AddMappingAsync(TMapping mapping)
    {
        try
        {
            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                AddMappingTo(db, mapping);
                await db.SaveChangesAsync();
            }
            await InitializeAsync();
            ApplyFilter();
            Log.Information("{Prefix}: 添加映射成功 {Key}", LogPrefix, GetMappingKey(mapping));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Prefix}: 添加映射失败", LogPrefix);
        }
    }

    public async Task DeleteMappingAsync(int mappingId)
    {
        try
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
            await InitializeAsync();
            ApplyFilter();
            Log.Information("{Prefix}: 删除映射成功 {Id}", LogPrefix, mappingId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Prefix}: 删除映射失败", LogPrefix);
        }
    }

    // ===== 列表显示辅助 =====

    public string GetCategoryName(int categoryId)
        => Categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? "未知";

    public string GetCategoryColor(int categoryId)
        => Categories.FirstOrDefault(c => c.Id == categoryId)?.Color ?? "#95A5A6";

    public string GetCategoryIcon(int categoryId)
        => Categories.FirstOrDefault(c => c.Id == categoryId)?.Icon ?? "📁";
}