using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;


namespace PChabit.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static async Task InitializeDefaultCategoriesAsync(PChabitDbContext dbContext)
    {
        if (await dbContext.ProgramCategories.AnyAsync())
        {
            return;
        }

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
        await dbContext.SaveChangesAsync();

        foreach (var mapping in defaultMappings)
        {
            var category = defaultCategories.FirstOrDefault(c => c.Id == mapping.CategoryId);
            if (category != null)
            {
                mapping.ProcessAlias = category.Name;
            }
        }

        dbContext.ProgramCategoryMappings.AddRange(defaultMappings);
        await dbContext.SaveChangesAsync();

        Log.Information("已初始化默认类别和映射");
    }

    public async Task LoadCategoriesAsync()
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            await InitializeDefaultCategoriesAsync(dbContext);

            var categories = await dbContext.ProgramCategories
                .Include(c => c.ProgramMappings)
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            var mappings = await dbContext.ProgramCategoryMappings.ToListAsync();

            await RunOnUIThreadAsync(() =>
            {
                Categories.Clear();
                foreach (var category in categories)
                {
                    var programCount = mappings.Count(m => m.CategoryId == category.Id);
                    Categories.Add(new CategoryDisplayItem
                    {
                        Id = category.Id,
                        Name = category.Name,
                        Description = category.Description ?? "",
                        Icon = category.Icon,
                        Color = category.Color,
                        IsSystem = category.IsSystem,
                        ProgramCount = programCount,
                        SortOrder = category.SortOrder
                    });
                }

                UpdateSelectedCategoryCount();
                StatusMessage = $"已加载 {Categories.Count} 个类别";
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载类别失败");
            StatusMessage = "加载类别失败";
        }
    }

    [RelayCommand]
    private async Task AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            if (await dbContext.ProgramCategories.AnyAsync(c => c.Name == NewCategoryName && c.IsActive))
            {
                StatusMessage = "类别名称已存在";
                return;
            }

            var category = new ProgramCategory
            {
                Name = NewCategoryName,
                Description = "",
                Color = "#4A90E4",
                Icon = "📁",
                SortOrder = Categories.Count + 1,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            dbContext.ProgramCategories.Add(category);
            await dbContext.SaveChangesAsync();
            await LoadCategoriesAsync();

            NewCategoryName = "";
            StatusMessage = "类别创建成功";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建类别失败");
            StatusMessage = "创建类别失败";
        }
    }

    public async Task SaveCategoryFromDialogAsync(ProgramCategory category, bool isEditMode)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            if (isEditMode)
            {
                var existing = await dbContext.ProgramCategories.FindAsync(category.Id);
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
                StatusMessage = $"分类 \"{category.Name}\" 已更新";
            }
            else
            {
                category.IsActive = true;
                category.CreatedAt = DateTime.Now;
                dbContext.ProgramCategories.Add(category);
                await dbContext.SaveChangesAsync();
                StatusMessage = $"分类 \"{category.Name}\" 已创建";
            }

            await LoadCategoriesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存分类失败");
            StatusMessage = "保存分类失败";
        }
    }

    [RelayCommand]
    private async Task DeleteCategory(CategoryDisplayItem? item)
    {
        if (item == null) return;

        if (item.IsSystem)
        {
            StatusMessage = "系统分类不能删除";
            return;
        }

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var category = await dbContext.ProgramCategories.FindAsync(item.Id);
            if (category != null)
            {
                category.IsActive = false;
                await dbContext.SaveChangesAsync();
            }

            await LoadCategoriesAsync();
            StatusMessage = $"分类 \"{item.Name}\" 已删除";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除分类失败");
            StatusMessage = "删除分类失败";
        }
    }

    [RelayCommand]
    private void ToggleCategorySelection(CategoryDisplayItem? item)
    {
        if (item == null) return;
        
        item.IsSelected = !item.IsSelected;
        UpdateSelectedCategoryCount();
    }

    [RelayCommand]
    private void SelectAllCategories()
    {
        foreach (var category in Categories.Where(c => !c.IsSystem))
        {
            category.IsSelected = true;
        }
        UpdateSelectedCategoryCount();
    }

    [RelayCommand]
    private void DeselectAllCategories()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = false;
        }
        UpdateSelectedCategoryCount();
    }

    [RelayCommand]
    private async Task DeleteSelectedCategories()
    {
        var selectedIds = Categories.Where(c => c.IsSelected && !c.IsSystem).Select(c => c.Id).ToList();

        if (selectedIds.Count == 0)
        {
            StatusMessage = "请选择要删除的分类";
            return;
        }

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var categories = await dbContext.ProgramCategories
                .Where(c => selectedIds.Contains(c.Id) && !c.IsSystem)
                .ToListAsync();

            foreach (var category in categories)
            {
                category.IsActive = false;
            }

            await dbContext.SaveChangesAsync();
            await LoadCategoriesAsync();
            StatusMessage = $"已删除 {categories.Count} 个分类";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "批量删除分类失败");
            StatusMessage = "批量删除失败";
        }
    }

    [RelayCommand]
    private async Task MoveCategoryUp(CategoryDisplayItem? item)
    {
        if (item == null) return;

        var index = Categories.IndexOf(item);
        if (index <= 0) return;

        var previousItem = Categories[index - 1];
        var tempOrder = item.SortOrder;
        item.SortOrder = previousItem.SortOrder;
        previousItem.SortOrder = tempOrder;

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var cat1 = await dbContext.ProgramCategories.FindAsync(item.Id);
            var cat2 = await dbContext.ProgramCategories.FindAsync(previousItem.Id);
            if (cat1 != null) cat1.SortOrder = item.SortOrder;
            if (cat2 != null) cat2.SortOrder = previousItem.SortOrder;
            await dbContext.SaveChangesAsync();

            Categories.Move(index, index - 1);
            StatusMessage = "排序已更新";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新排序失败");
            StatusMessage = "更新排序失败";
        }
    }

    [RelayCommand]
    private async Task MoveCategoryDown(CategoryDisplayItem? item)
    {
        if (item == null) return;

        var index = Categories.IndexOf(item);
        if (index < 0 || index >= Categories.Count - 1) return;

        var nextItem = Categories[index + 1];
        var tempOrder = item.SortOrder;
        item.SortOrder = nextItem.SortOrder;
        nextItem.SortOrder = tempOrder;

        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var cat1 = await dbContext.ProgramCategories.FindAsync(item.Id);
            var cat2 = await dbContext.ProgramCategories.FindAsync(nextItem.Id);
            if (cat1 != null) cat1.SortOrder = item.SortOrder;
            if (cat2 != null) cat2.SortOrder = nextItem.SortOrder;
            await dbContext.SaveChangesAsync();

            Categories.Move(index, index + 1);
            StatusMessage = "排序已更新";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新排序失败");
            StatusMessage = "更新排序失败";
        }
    }

    [RelayCommand]
    private async Task ExportCategories()
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            var categories = await dbContext.ProgramCategories.Where(c => c.IsActive).ToListAsync();
            var mappings = await dbContext.ProgramCategoryMappings.ToListAsync();

            var export = new
            {
                Categories = categories,
                Mappings = mappings,
                ExportedAt = DateTime.Now
            };

            var json = System.Text.Json.JsonSerializer.Serialize(export);
            var exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PChabit", "categories_export.json");
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            await File.WriteAllTextAsync(exportPath, json);

            StatusMessage = $"分类已导出到: {exportPath}";
            Log.Information("分类已导出到: {Path}", exportPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出分类失败");
            StatusMessage = "导出分类失败";
        }
    }

    [RelayCommand]
    private async Task ImportCategories()
    {
        try
        {
            var importPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PChabit", "categories_export.json");
            if (!File.Exists(importPath))
            {
                StatusMessage = "未找到导入文件";
                return;
            }

            var json = await File.ReadAllTextAsync(importPath);

            await using var dbContext = await _dbFactory.CreateDbContextAsync();

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
                                IsSystem = false,
                                IsActive = true,
                                CreatedAt = DateTime.Now
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

            await LoadCategoriesAsync();
            StatusMessage = $"已导入 {count} 个分类";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导入分类失败");
            StatusMessage = "导入分类失败";
        }
    }

    private void UpdateSelectedCategoryCount()
    {
        SelectedCategoryCount = Categories.Count(c => c.IsSelected);
    }

public partial class CategoryDisplayItem : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "📁";
    public string Color { get; set; } = "#4A90E4";
    public bool IsSystem { get; set; }
    public int ProgramCount { get; set; }
    public int SortOrder { get; set; }
    
    [ObservableProperty]
    private bool _isSelected;
}

}

