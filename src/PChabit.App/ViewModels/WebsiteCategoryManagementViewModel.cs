using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

public partial class WebsiteCategoryManagementViewModel : ObservableObject
{
    private readonly IWebsiteCategoryService _websiteCategoryService;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty]
    private bool _isLoading;

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

    public WebsiteCategoryManagementViewModel(IWebsiteCategoryService websiteCategoryService)
    {
        _websiteCategoryService = websiteCategoryService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        Log.Information("WebsiteCategoryManagementViewModel: InitializeAsync 开始");
        IsLoading = true;

        try
        {
            await _websiteCategoryService.InitializeDefaultCategoriesAsync();
            await LoadCategoriesAsync();
            await LoadMappingsAsync();
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

    private async Task LoadCategoriesAsync()
    {
        var categories = await _websiteCategoryService.GetAllCategoriesAsync();

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
        var mappings = await _websiteCategoryService.GetAllMappingsAsync();

        _dispatcherQueue.TryEnqueue(() =>
        {
            DomainMappings.Clear();
            foreach (var mapping in mappings)
            {
                DomainMappings.Add(mapping);
            }
            TotalMappings = DomainMappings.Count;
        });
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
            await _websiteCategoryService.CreateCategoryAsync(category);
            await LoadCategoriesAsync();
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
            await _websiteCategoryService.UpdateCategoryAsync(category);
            await LoadCategoriesAsync();
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
            await _websiteCategoryService.DeleteCategoryAsync(categoryId);
            await LoadCategoriesAsync();
            await LoadMappingsAsync();

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
            await _websiteCategoryService.CreateMappingAsync(mapping);
            await LoadMappingsAsync();

            _dispatcherQueue.TryEnqueue(() =>
            {
                FilterMappings();
            });

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
            await _websiteCategoryService.DeleteMappingAsync(mappingId);
            await LoadMappingsAsync();

            _dispatcherQueue.TryEnqueue(() =>
            {
                FilterMappings();
            });

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
