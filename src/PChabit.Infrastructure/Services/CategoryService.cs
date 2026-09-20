using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Services;

public interface ICategoryService
{
    Task<List<ProgramCategory>> GetAllCategoriesAsync();
    Task<ProgramCategory?> GetCategoryByIdAsync(int id);
    Task<ProgramCategory> CreateCategoryAsync(ProgramCategory category);
    Task<ProgramCategory?> UpdateCategoryAsync(ProgramCategory category);
    Task<bool> DeleteCategoryAsync(int id);
    Task<int> DeleteCategoriesAsync(IEnumerable<int> ids);
    Task<bool> CategoryExistsAsync(string name, int? excludeId = null);
    Task UpdateCategorySortOrderAsync(int categoryId, int newSortOrder);
    Task ReorderCategoriesAsync(IEnumerable<(int Id, int SortOrder)> orders);
    
    Task<List<ProgramCategoryMapping>> GetAllMappingsAsync();
    Task<ProgramCategoryMapping?> GetMappingByProcessNameAsync(string processName);
    Task<ProgramCategoryMapping> CreateMappingAsync(ProgramCategoryMapping mapping);
    Task<bool> UpdateMappingAsync(ProgramCategoryMapping mapping);
    Task<bool> DeleteMappingAsync(int id);
    Task<bool> DeleteMappingByProcessNameAsync(string processName);
    Task<List<ProgramCategoryMapping>> GetMappingsByCategoryIdAsync(int categoryId);
    Task BulkCreateMappingsAsync(IEnumerable<ProgramCategoryMapping> mappings);
    
    Task InitializeDefaultCategoriesAsync(CancellationToken cancellationToken = default);
    Task<string> ExportCategoriesAsync();
    Task<int> ImportCategoriesAsync(string json);
    
    void InitializeDefaultCategoriesSync();
    List<ProgramCategory> GetAllCategoriesSync();
    List<ProgramCategoryMapping> GetAllMappingsSync();
    bool CategoryExists(string name);
    void CreateCategory(ProgramCategory category);
    void UpdateCategory(ProgramCategory category);
    void DeleteCategory(int id);
}

public class CategoryService : ICategoryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    
    public CategoryService(IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }
    
    public async Task<List<ProgramCategory>> GetAllCategoriesAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.ProgramCategories
            .Include(c => c.ProgramMappings)
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }
    
    public List<ProgramCategory> GetAllCategoriesSync()
    {
        Log.Information("GetAllCategoriesSync: 开始获取分类");
        using var dbContext = _dbContextFactory.CreateDbContext();
        Log.Information("GetAllCategoriesSync: DbContext 创建成功");
        var result = dbContext.ProgramCategories
            .Include(c => c.ProgramMappings)
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
        Log.Information("GetAllCategoriesSync: 获取到 {Count} 个分类", result.Count);
        return result;
    }
    
    public async Task<ProgramCategory?> GetCategoryByIdAsync(int id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.ProgramCategories
            .Include(c => c.ProgramMappings)
            .FirstOrDefaultAsync(c => c.Id == id);
    }
    
    public async Task<ProgramCategory> CreateCategoryAsync(ProgramCategory category)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        category.CreatedAt = DateTime.Now;
        dbContext.ProgramCategories.Add(category);
        await dbContext.SaveChangesAsync();
        
        Log.Information("创建类别: {CategoryName}", category.Name);
        return category;
    }
    
    public void CreateCategory(ProgramCategory category)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        
        category.CreatedAt = DateTime.Now;
        dbContext.ProgramCategories.Add(category);
        dbContext.SaveChanges();
        
        Log.Information("创建类别: {CategoryName}", category.Name);
    }
    
    public void UpdateCategory(ProgramCategory category)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        
        var existing = dbContext.ProgramCategories.Find(category.Id);
        if (existing == null) return;
        
        existing.Name = category.Name;
        existing.Description = category.Description;
        existing.Color = category.Color;
        existing.Icon = category.Icon;
        existing.SortOrder = category.SortOrder;
        existing.UpdatedAt = DateTime.Now;
        
        dbContext.SaveChanges();
        
        Log.Information("更新类别: {CategoryName}", category.Name);
    }
    
    public void DeleteCategory(int id)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        
        var category = dbContext.ProgramCategories.Find(id);
        if (category == null) return;
        
        category.IsActive = false;
        dbContext.SaveChanges();
        
        Log.Information("删除类别: {CategoryId}", id);
    }
    
    public async Task<ProgramCategory?> UpdateCategoryAsync(ProgramCategory category)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var existing = await dbContext.ProgramCategories.FindAsync(category.Id);
        if (existing == null) return null;
        
        existing.Name = category.Name;
        existing.Description = category.Description;
        existing.Color = category.Color;
        existing.Icon = category.Icon;
        existing.SortOrder = category.SortOrder;
        
        await dbContext.SaveChangesAsync();
        
        Log.Information("更新类别: {CategoryName}", category.Name);
        return existing;
    }
    
    public async Task<bool> DeleteCategoryAsync(int id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var category = await dbContext.ProgramCategories.FindAsync(id);
        if (category == null) return false;
        
        category.IsActive = false;
        await dbContext.SaveChangesAsync();
        
        Log.Information("删除类别: {CategoryId}", id);
        return true;
    }
    
    public async Task<int> DeleteCategoriesAsync(IEnumerable<int> ids)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var idList = ids.ToList();
        var categories = await dbContext.ProgramCategories
            .Where(c => idList.Contains(c.Id) && !c.IsSystem)
            .ToListAsync();
        
        foreach (var category in categories)
        {
            category.IsActive = false;
        }
        
        await dbContext.SaveChangesAsync();
        
        Log.Information("批量删除类别: {Count} 个", categories.Count);
        return categories.Count;
    }
    
    public async Task UpdateCategorySortOrderAsync(int categoryId, int newSortOrder)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var category = await dbContext.ProgramCategories.FindAsync(categoryId);
        if (category == null) return;
        
        category.SortOrder = newSortOrder;
        category.UpdatedAt = DateTime.Now;
        await dbContext.SaveChangesAsync();
        
        Log.Information("更新类别排序: {CategoryId} -> {SortOrder}", categoryId, newSortOrder);
    }
    
    public async Task ReorderCategoriesAsync(IEnumerable<(int Id, int SortOrder)> orders)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        foreach (var (id, sortOrder) in orders)
        {
            var category = await dbContext.ProgramCategories.FindAsync(id);
            if (category != null)
            {
                category.SortOrder = sortOrder;
                category.UpdatedAt = DateTime.Now;
            }
        }
        
        await dbContext.SaveChangesAsync();
        Log.Information("重新排序类别完成");
    }
    
    public async Task<bool> CategoryExistsAsync(string name, int? excludeId = null)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var query = dbContext.ProgramCategories.Where(c => c.Name == name && c.IsActive);
        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }
        
        return await query.AnyAsync();
    }
    
    public bool CategoryExists(string name)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        
        return dbContext.ProgramCategories.Any(c => c.Name == name && c.IsActive);
    }
    
    public async Task<List<ProgramCategoryMapping>> GetAllMappingsAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.ProgramCategoryMappings.ToListAsync();
    }
    
    public List<ProgramCategoryMapping> GetAllMappingsSync()
    {
        Log.Information("GetAllMappingsSync: 开始获取映射");
        using var dbContext = _dbContextFactory.CreateDbContext();
        Log.Information("GetAllMappingsSync: DbContext 创建成功");
        var result = dbContext.ProgramCategoryMappings.ToList();
        Log.Information("GetAllMappingsSync: 获取到 {Count} 个映射", result.Count);
        return result;
    }
    
    public async Task<ProgramCategoryMapping?> GetMappingByProcessNameAsync(string processName)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.ProgramCategoryMappings
            .FirstOrDefaultAsync(m => m.ProcessName.ToLower() == processName.ToLower());
    }
    
    public async Task<ProgramCategoryMapping> CreateMappingAsync(ProgramCategoryMapping mapping)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        mapping.CreatedAt = DateTime.Now;
        dbContext.ProgramCategoryMappings.Add(mapping);
        await dbContext.SaveChangesAsync();
        
        Log.Information("创建映射: {ProcessName} -> {CategoryId}", mapping.ProcessName, mapping.CategoryId);
        return mapping;
    }
    
    public async Task<bool> UpdateMappingAsync(ProgramCategoryMapping mapping)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var existing = await dbContext.ProgramCategoryMappings.FindAsync(mapping.Id);
        if (existing == null) return false;
        
        existing.CategoryId = mapping.CategoryId;
        existing.ProcessPath = mapping.ProcessPath;
        existing.ProcessAlias = mapping.ProcessAlias;
        existing.UpdatedAt = DateTime.Now;
        
        await dbContext.SaveChangesAsync();
        
        return true;
    }
    
    public async Task<bool> DeleteMappingAsync(int id)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var mapping = await dbContext.ProgramCategoryMappings.FindAsync(id);
        if (mapping == null) return false;
        
        dbContext.ProgramCategoryMappings.Remove(mapping);
        await dbContext.SaveChangesAsync();
        
        return true;
    }
    
    public async Task<bool> DeleteMappingByProcessNameAsync(string processName)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var mappings = await dbContext.ProgramCategoryMappings
            .Where(m => m.ProcessName.ToLower() == processName.ToLower())
            .ToListAsync();
        
        if (!mappings.Any()) return false;
        
        dbContext.ProgramCategoryMappings.RemoveRange(mappings);
        await dbContext.SaveChangesAsync();
        
        return true;
    }
    
    public async Task<List<ProgramCategoryMapping>> GetMappingsByCategoryIdAsync(int categoryId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.ProgramCategoryMappings
            .Where(m => m.CategoryId == categoryId)
            .ToListAsync();
    }
    
    public async Task BulkCreateMappingsAsync(IEnumerable<ProgramCategoryMapping> mappings)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        foreach (var mapping in mappings)
        {
            var existing = await dbContext.ProgramCategoryMappings
                .FirstOrDefaultAsync(m => m.ProcessName.ToLower() == mapping.ProcessName.ToLower());
            
            if (existing != null)
            {
                existing.CategoryId = mapping.CategoryId;
                existing.ProcessPath = mapping.ProcessPath;
                existing.ProcessAlias = mapping.ProcessAlias;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                mapping.CreatedAt = DateTime.Now;
                dbContext.ProgramCategoryMappings.Add(mapping);
            }
        }
        
        await dbContext.SaveChangesAsync();
    }
    
    public async Task InitializeDefaultCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        await EnsureCatalogSeededAsync(dbContext, cancellationToken);
    }

    public void InitializeDefaultCategoriesSync()
    {
        Log.Information("InitializeDefaultCategoriesSync: 开始初始化");
        using var dbContext = _dbContextFactory.CreateDbContext();
        EnsureCatalogSeededAsync(dbContext, CancellationToken.None).GetAwaiter().GetResult();
        Log.Information("InitializeDefaultCategoriesSync: 完成");
    }

    /// <summary>增量补齐分类/映射，不覆盖用户数据。</summary>
    public static async Task EnsureCatalogSeededAsync(PChabitDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var changed = false;

        var existingCats = await dbContext.ProgramCategories.ToListAsync(cancellationToken);
        var catByName = existingCats
            .Where(c => !string.IsNullOrEmpty(c.Name))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var (name, description, color, icon, sortOrder) in DefaultAppCatalog.Categories)
        {
            if (catByName.ContainsKey(name)) continue;
            var cat = new ProgramCategory
            {
                Name = name,
                Description = description,
                Color = color,
                Icon = icon,
                SortOrder = sortOrder,
                IsSystem = true,
                IsActive = true,
                CreatedAt = now
            };
            dbContext.ProgramCategories.Add(cat);
            catByName[name] = cat;
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            changed = false;
        }

        var existingMappings = await dbContext.ProgramCategoryMappings.ToListAsync(cancellationToken);
        var mappedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in existingMappings)
        {
            if (string.IsNullOrEmpty(m.ProcessName)) continue;
            mappedKeys.Add(m.ProcessName);
            if (m.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                mappedKeys.Add(m.ProcessName[..^4]);
            else
                mappedKeys.Add(m.ProcessName + ".exe");
        }

        foreach (var app in DefaultAppCatalog.Apps)
        {
            if (mappedKeys.Contains(app.ProcessKey) || mappedKeys.Contains(app.ProcessKey + ".exe"))
                continue;
            if (!catByName.TryGetValue(app.CategoryKey, out var category))
                continue;

            dbContext.ProgramCategoryMappings.Add(new ProgramCategoryMapping
            {
                ProcessName = app.ProcessKey.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? app.ProcessKey
                    : app.ProcessKey + ".exe",
                ProcessAlias = app.DisplayName,
                CategoryId = category.Id
            });
            mappedKeys.Add(app.ProcessKey);
            mappedKeys.Add(app.ProcessKey + ".exe");
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            Log.Information("已初始化/补齐默认类别和映射（DefaultAppCatalog）");
        }
        else
        {
            Log.Information("默认类别和映射已是最新");
        }
    }
    
    public async Task<string> ExportCategoriesAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var categories = await dbContext.ProgramCategories.Where(c => c.IsActive).ToListAsync();
        var mappings = await dbContext.ProgramCategoryMappings.ToListAsync();
        
        var export = new
        {
            Categories = categories,
            Mappings = mappings,
            ExportedAt = DateTime.Now
        };
        
        return System.Text.Json.JsonSerializer.Serialize(export);
    }
    
    public async Task<int> ImportCategoriesAsync(string json)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        if (string.IsNullOrEmpty(json)) return 0;
        
        int count = 0;
        
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("Categories", out var categoriesElement))
            {
                foreach (var category in categoriesElement.EnumerateArray())
                {
                    var name = category.GetProperty("Name").GetString() ?? "";
                    var existing = await dbContext.ProgramCategories
                        .FirstOrDefaultAsync(c => c.Name == name);
                    
                    if (existing == null)
                    {
                        dbContext.ProgramCategories.Add(new ProgramCategory
                        {
                            Name = name,
                            Description = category.TryGetProperty("Description", out var desc) ? desc.GetString() ?? "" : "",
                            Color = category.TryGetProperty("Color", out var color) ? color.GetString() ?? "#4A90E4" : "#4A90E4",
                            Icon = category.TryGetProperty("Icon", out var icon) ? icon.GetString() ?? "📁" : "📁",
                            SortOrder = category.TryGetProperty("SortOrder", out var sort) ? sort.GetInt32() : 99,
                            IsSystem = false
                        });
                        count++;
                    }
                }
            }
            
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "导入类别失败");
        }
        
        return count;
    }
}
