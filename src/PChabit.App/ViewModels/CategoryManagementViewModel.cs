using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public class RunningProcessItem
{
    public string ProcessName { get; set; } = string.Empty;
    public string? MainWindowTitle { get; set; }
    public string? ProcessPath { get; set; }
}

public partial class CategoryManagementViewModel : ViewModelBase
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ProgramCategory? _selectedCategory;

    [ObservableProperty]
    private bool _isCategorySelected;

    [ObservableProperty]
    private int _totalCategories;

    [ObservableProperty]
    private int _totalMappings;

    public ObservableCollection<ProgramCategory> Categories { get; } = new();
    public ObservableCollection<ProgramCategoryMapping> CategoryMappings { get; } = new();
    public ObservableCollection<ProgramCategoryMapping> FilteredMappings { get; } = new();
    public ObservableCollection<RunningProcessItem> RunningProcesses { get; } = new();

    public CategoryManagementViewModel(IDbContextFactory<PChabitDbContext> dbFactory) : base()
    {
        _dbFactory = dbFactory;
    }
    
    public List<RunningProcessItem> GetRunningProcesses()
    {
        var processes = new List<RunningProcessItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (string.IsNullOrEmpty(process.ProcessName)) continue;
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    
                    var processName = process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                        ? process.ProcessName 
                        : $"{process.ProcessName}.exe";
                    
                    if (seen.Contains(processName)) continue;
                    seen.Add(processName);
                    
                    var path = process.MainModule?.FileName;
                    
                    processes.Add(new RunningProcessItem
                    {
                        ProcessName = processName,
                        MainWindowTitle = process.MainWindowTitle,
                        ProcessPath = path
                    });
                }
                catch
                {
                    // 跳过无法访问的进程
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "获取运行中的进程失败");
        }
        
        return processes.OrderBy(p => p.ProcessName).ToList();
    }

    public async Task InitializeAsync()
    {
        Log.Information("CategoryManagementViewModel: InitializeAsync 开始");
        IsLoading = true;

        try
        {
            // Phase 1: 线程池 — DB 查询 + 纯数据获取
            var categories = await Task.Run(async () =>
            {
                await using var dbContext = await _dbFactory.CreateDbContextAsync();
                return await dbContext.ProgramCategories
                    .Include(c => c.ProgramMappings)
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .ThenBy(c => c.Name)
                    .ToListAsync();
            });

            var mappings = await Task.Run(async () =>
            {
                await using var dbContext = await _dbFactory.CreateDbContextAsync();
                return await dbContext.ProgramCategoryMappings.ToListAsync();
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

                CategoryMappings.Clear();
                foreach (var mapping in mappings)
                {
                    CategoryMappings.Add(mapping);
                }
                TotalMappings = CategoryMappings.Count;
                return Task.CompletedTask;
            });

            Log.Information("CategoryManagementViewModel: 初始化完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CategoryManagementViewModel: 初始化失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectCategory(ProgramCategory? category)
    {
        SelectedCategory = category;
        IsCategorySelected = category != null;
        
        FilterMappings();
        
        Log.Information("选中分类: {CategoryName}", category?.Name ?? "无");
    }

    [RelayCommand]
    private Task AddCategory()
    {
        Log.Information("AddCategoryCommand: 执行");
        return Task.CompletedTask;
    }

    private void FilterMappings()
    {
        FilteredMappings.Clear();

        var query = CategoryMappings.AsEnumerable();

        if (SelectedCategory != null)
        {
            query = query.Where(m => m.CategoryId == SelectedCategory.Id);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(m => 
                m.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (m.ProcessAlias?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
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

    public async Task AddCategoryAsync(ProgramCategory category)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            category.IsActive = true;
            category.CreatedAt = DateTime.Now;
            dbContext.ProgramCategories.Add(category);
            await dbContext.SaveChangesAsync();
            await InitializeAsync();
            Log.Information("添加分类成功: {CategoryName}", category.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "添加分类失败");
        }
    }

    public async Task UpdateCategoryAsync(ProgramCategory category)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
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
            await InitializeAsync();
            Log.Information("更新分类成功: {CategoryName}", category.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新分类失败");
        }
    }

    public async Task DeleteCategoryAsync(int categoryId)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            var category = await dbContext.ProgramCategories.FindAsync(categoryId);
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
            
            Log.Information("删除分类成功: {CategoryId}", categoryId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除分类失败");
        }
    }

    public async Task AddMappingAsync(ProgramCategoryMapping mapping)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            mapping.CreatedAt = DateTime.Now;
            dbContext.ProgramCategoryMappings.Add(mapping);
            await dbContext.SaveChangesAsync();
            await InitializeAsync();
            FilterMappings();
            
            Log.Information("添加映射成功: {ProcessName} -> {CategoryId}", mapping.ProcessName, mapping.CategoryId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "添加映射失败");
        }
    }

    public async Task DeleteMappingAsync(int mappingId)
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            var mapping = await dbContext.ProgramCategoryMappings.FindAsync(mappingId);
            if (mapping != null)
            {
                dbContext.ProgramCategoryMappings.Remove(mapping);
                await dbContext.SaveChangesAsync();
            }
            await InitializeAsync();
            FilterMappings();
            
            Log.Information("删除映射成功: {MappingId}", mappingId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除映射失败");
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
        return Categories.FirstOrDefault(c => c.Id == categoryId)?.Icon ?? "📁";
    }
}
