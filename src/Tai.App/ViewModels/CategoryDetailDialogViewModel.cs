using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tai.Core.Entities;
using Tai.Infrastructure.Services;

namespace Tai.App.ViewModels;

public partial class CategoryDetailDialogViewModel : ObservableObject
{
    private readonly ICategoryService _categoryService;
    
    [ObservableProperty]
    private string _categoryName = string.Empty;
    
    [ObservableProperty]
    private string _categoryDescription = string.Empty;
    
    [ObservableProperty]
    private string _categoryColor = "#4A90E4";
    
    [ObservableProperty]
    private string _categoryIcon = "📁";
    
    [ObservableProperty]
    private bool _isSystemCategory;
    
    [ObservableProperty]
    private int _programCount;
    
    [ObservableProperty]
    private DateTime _createdAt;
    
    [ObservableProperty]
    private DateTime? _updatedAt;
    
    public ObservableCollection<ProgramMappingItem> ProgramMappings { get; } = new();
    
    public CategoryDetailDialogViewModel(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }
    
    public async Task LoadCategoryAsync(int categoryId)
    {
        var category = await _categoryService.GetCategoryByIdAsync(categoryId);
        if (category == null) return;
        
        CategoryName = category.Name;
        CategoryDescription = category.Description ?? "暂无描述";
        CategoryColor = category.Color;
        CategoryIcon = category.Icon;
        IsSystemCategory = category.IsSystem;
        CreatedAt = category.CreatedAt;
        UpdatedAt = category.UpdatedAt;
        
        ProgramMappings.Clear();
        
        if (category.ProgramMappings != null)
        {
            foreach (var mapping in category.ProgramMappings)
            {
                ProgramMappings.Add(new ProgramMappingItem
                {
                    Id = mapping.Id,
                    ProcessName = mapping.ProcessName,
                    ProcessAlias = mapping.ProcessAlias ?? mapping.ProcessName,
                    ProcessPath = mapping.ProcessPath ?? ""
                });
            }
        }
        
        ProgramCount = ProgramMappings.Count;
    }
}

public class ProgramMappingItem
{
    public int Id { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string ProcessAlias { get; init; } = string.Empty;
    public string ProcessPath { get; init; } = string.Empty;
}
