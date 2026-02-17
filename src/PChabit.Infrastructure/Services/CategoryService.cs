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
        
        if (await dbContext.ProgramCategories.AnyAsync(cancellationToken))
        {
            return;
        }
        
        var defaultCategories = new List<ProgramCategory>
        {
            new() { Name = "开发", Description = "开发工具和IDE", Color = "#4A90E4", Icon = "💻", SortOrder = 1, IsSystem = true },
            new() { Name = "浏览", Description = "浏览器", Color = "#50C878", Icon = "🌐", SortOrder = 2, IsSystem = true },
            new() { Name = "沟通", Description = "即时通讯和邮件", Color = "#FF6B6B", Icon = "💬", SortOrder = 3, IsSystem = true },
            new() { Name = "娱乐", Description = "游戏和娱乐", Color = "#9B59B6", Icon = "🎮", SortOrder = 4, IsSystem = true },
            new() { Name = "办公", Description = "办公软件", Color = "#F39C12", Icon = "📊", SortOrder = 5, IsSystem = true },
            new() { Name = "设计", Description = "设计工具", Color = "#E74C3C", Icon = "🎨", SortOrder = 6, IsSystem = true },
            new() { Name = "其他", Description = "未分类程序", Color = "#95A5A6", Icon = "📁", SortOrder = 99, IsSystem = true }
        };
        
        var defaultMappings = new List<ProgramCategoryMapping>
        {
            new() { ProcessName = "code.exe", CategoryId = 1 },
            new() { ProcessName = "devenv.exe", CategoryId = 1 },
            new() { ProcessName = "idea64.exe", CategoryId = 1 },
            new() { ProcessName = "pycharm64.exe", CategoryId = 1 },
            new() { ProcessName = "chrome.exe", CategoryId = 2 },
            new() { ProcessName = "msedge.exe", CategoryId = 2 },
            new() { ProcessName = "firefox.exe", CategoryId = 2 },
            new() { ProcessName = "slack.exe", CategoryId = 3 },
            new() { ProcessName = "discord.exe", CategoryId = 3 },
            new() { ProcessName = "teams.exe", CategoryId = 3 },
            new() { ProcessName = "outlook.exe", CategoryId = 3 },
            new() { ProcessName = "spotify.exe", CategoryId = 4 },
            new() { ProcessName = "steam.exe", CategoryId = 4 },
            new() { ProcessName = "wmplayer.exe", CategoryId = 4 },
            new() { ProcessName = "WINWORD.EXE", CategoryId = 5 },
            new() { ProcessName = "EXCEL.EXE", CategoryId = 5 },
            new() { ProcessName = "POWERPNT.EXE", CategoryId = 5 },
            new() { ProcessName = "Photoshop.exe", CategoryId = 6 },
            new() { ProcessName = "Figma.exe", CategoryId = 6 }
        };
        
        dbContext.ProgramCategories.AddRange(defaultCategories);
        await dbContext.SaveChangesAsync(cancellationToken);
        
        foreach (var mapping in defaultMappings)
        {
            var category = defaultCategories.FirstOrDefault(c => c.Id == mapping.CategoryId);
            if (category != null)
            {
                mapping.ProcessAlias = category.Name;
            }
        }
        
        dbContext.ProgramCategoryMappings.AddRange(defaultMappings);
        await dbContext.SaveChangesAsync(cancellationToken);
        
        Log.Information("已初始化默认类别和映射");
    }
    
    public void InitializeDefaultCategoriesSync()
    {
        Log.Information("InitializeDefaultCategoriesSync: 开始初始化");
        using var dbContext = _dbContextFactory.CreateDbContext();
        Log.Information("InitializeDefaultCategoriesSync: DbContext 创建成功");
        
        if (dbContext.ProgramCategories.Any())
        {
            Log.Information("InitializeDefaultCategoriesSync: 分类已存在，跳过初始化");
            return;
        }
        
        Log.Information("InitializeDefaultCategoriesSync: 创建默认分类");
        var now = DateTime.Now;
        var defaultCategories = new List<ProgramCategory>
        {
            new() { Name = "开发", Description = "开发工具和IDE", Color = "#4A90E4", Icon = "💻", SortOrder = 1, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "浏览", Description = "浏览器", Color = "#50C878", Icon = "🌐", SortOrder = 2, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "沟通", Description = "即时通讯和邮件", Color = "#FF6B6B", Icon = "💬", SortOrder = 3, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "娱乐", Description = "游戏和娱乐", Color = "#9B59B6", Icon = "🎮", SortOrder = 4, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "办公", Description = "办公软件", Color = "#F39C12", Icon = "📊", SortOrder = 5, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "设计", Description = "设计工具", Color = "#E74C3C", Icon = "🎨", SortOrder = 6, IsSystem = true, IsActive = true, CreatedAt = now },
            new() { Name = "其他", Description = "未分类程序", Color = "#95A5A6", Icon = "📁", SortOrder = 99, IsSystem = true, IsActive = true, CreatedAt = now }
        };
        
        var defaultMappings = new List<ProgramCategoryMapping>
        {
            new() { ProcessName = "code.exe", CategoryId = 1 },
            new() { ProcessName = "devenv.exe", CategoryId = 1 },
            new() { ProcessName = "idea64.exe", CategoryId = 1 },
            new() { ProcessName = "pycharm64.exe", CategoryId = 1 },
            new() { ProcessName = "chrome.exe", CategoryId = 2 },
            new() { ProcessName = "msedge.exe", CategoryId = 2 },
            new() { ProcessName = "firefox.exe", CategoryId = 2 },
            new() { ProcessName = "slack.exe", CategoryId = 3 },
            new() { ProcessName = "discord.exe", CategoryId = 3 },
            new() { ProcessName = "teams.exe", CategoryId = 3 },
            new() { ProcessName = "outlook.exe", CategoryId = 3 },
            new() { ProcessName = "spotify.exe", CategoryId = 4 },
            new() { ProcessName = "steam.exe", CategoryId = 4 },
            new() { ProcessName = "wmplayer.exe", CategoryId = 4 },
            new() { ProcessName = "WINWORD.EXE", CategoryId = 5 },
            new() { ProcessName = "EXCEL.EXE", CategoryId = 5 },
            new() { ProcessName = "POWERPNT.EXE", CategoryId = 5 },
            new() { ProcessName = "Photoshop.exe", CategoryId = 6 },
            new() { ProcessName = "Figma.exe", CategoryId = 6 }
        };
        
        dbContext.ProgramCategories.AddRange(defaultCategories);
        dbContext.SaveChanges();
        Log.Information("InitializeDefaultCategoriesSync: 默认分类已保存");
        
        foreach (var mapping in defaultMappings)
        {
            var category = defaultCategories.FirstOrDefault(c => c.Id == mapping.CategoryId);
            if (category != null)
            {
                mapping.ProcessAlias = category.Name;
            }
        }
        
        dbContext.ProgramCategoryMappings.AddRange(defaultMappings);
        dbContext.SaveChanges();
        Log.Information("InitializeDefaultCategoriesSync: 默认映射已保存");
        
        Log.Information("已初始化默认类别和映射(同步)");
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
