using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

public class RunningProcessItem
{
    public string ProcessName { get; set; } = string.Empty;
    public string? MainWindowTitle { get; set; }
    public string? ProcessPath { get; set; }
}

public partial class CategoryManagementViewModel : ObservableObject
{
    private readonly ICategoryService _categoryService;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty]
    private bool _isLoading;

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

    public CategoryManagementViewModel(ICategoryService categoryService)
    {
        _categoryService = categoryService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
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
            await LoadCategoriesAsync();
            await LoadMappingsAsync();
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

    private async Task LoadCategoriesAsync()
    {
        var categories = await _categoryService.GetAllCategoriesAsync();
        
        _dispatcherQueue.TryEnqueue(() =>
        {
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
            TotalCategories = Categories.Count;
        });
    }

    private async Task LoadMappingsAsync()
    {
        var mappings = await _categoryService.GetAllMappingsAsync();
        
        _dispatcherQueue.TryEnqueue(() =>
        {
            CategoryMappings.Clear();
            foreach (var mapping in mappings)
            {
                CategoryMappings.Add(mapping);
            }
            TotalMappings = CategoryMappings.Count;
        });
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
            await _categoryService.CreateCategoryAsync(category);
            await LoadCategoriesAsync();
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
            await _categoryService.UpdateCategoryAsync(category);
            await LoadCategoriesAsync();
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
            await _categoryService.DeleteCategoryAsync(categoryId);
            await LoadCategoriesAsync();
            
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
            await _categoryService.CreateMappingAsync(mapping);
            await LoadMappingsAsync();
            
            _dispatcherQueue.TryEnqueue(() =>
            {
                FilterMappings();
            });
            
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
            await _categoryService.DeleteMappingAsync(mappingId);
            await LoadMappingsAsync();
            
            _dispatcherQueue.TryEnqueue(() =>
            {
                FilterMappings();
            });
            
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
