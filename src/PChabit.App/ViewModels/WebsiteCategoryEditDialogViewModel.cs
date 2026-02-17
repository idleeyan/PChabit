using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

public partial class WebsiteCategoryEditDialogViewModel : ObservableObject
{
    private readonly IWebsiteCategoryService _websiteCategoryService;
    private WebsiteCategory? _originalCategory;
    private bool _isEditMode;
    
    [ObservableProperty]
    private string _dialogTitle = "添加新分类";
    
    [ObservableProperty]
    private string _name = string.Empty;
    
    [ObservableProperty]
    private string _description = string.Empty;
    
    [ObservableProperty]
    private string _selectedColor = "#4A90E4";
    
    [ObservableProperty]
    private string _selectedIcon = "🌐";
    
    [ObservableProperty]
    private bool _isSystemCategory;
    
    [ObservableProperty]
    private bool _canEditName = true;
    
    [ObservableProperty]
    private string _nameError = string.Empty;
    
    [ObservableProperty]
    private bool _hasNameError;
    
    public ObservableCollection<string> AvailableColors { get; } = new()
    {
        "#4A90E4", "#50C878", "#FF6B6B", "#9B59B6", "#F39C12",
        "#E74C3C", "#1ABC9C", "#3498DB", "#E67E22", "#95A5A6",
        "#2ECC71", "#34495E", "#16A085", "#27AE60", "#2980B9",
        "#8E44AD", "#2C3E50", "#F1C40F", "#D35400", "#C0392B"
    };
    
    public ObservableCollection<string> AvailableIcons { get; } = new()
    {
        "🌐", "🔍", "💻", "🎬", "📱", "🛒", "📧", "📊", "📰",
        "📁", "💬", "🎮", "🎨", "📝", "📚", "🎵", "📷", "🔧",
        "⚙️", "🎯", "🖥️", "💼", "🔬", "🛠️", "📡", "🏠"
    };
    
    public WebsiteCategoryEditDialogViewModel(IWebsiteCategoryService websiteCategoryService)
    {
        _websiteCategoryService = websiteCategoryService;
    }
    
    public void InitializeForAdd()
    {
        _isEditMode = false;
        _originalCategory = null;
        DialogTitle = "添加新网站分类";
        Name = string.Empty;
        Description = string.Empty;
        SelectedColor = AvailableColors[0];
        SelectedIcon = "🌐";
        IsSystemCategory = false;
        CanEditName = true;
        ClearErrors();
    }
    
    public void InitializeForEdit(WebsiteCategory category)
    {
        _isEditMode = true;
        _originalCategory = category;
        DialogTitle = "编辑网站分类";
        Name = category.Name;
        Description = category.Description ?? string.Empty;
        SelectedColor = category.Color;
        SelectedIcon = category.Icon;
        IsSystemCategory = category.IsSystem;
        CanEditName = !category.IsSystem;
        ClearErrors();
    }
    
    public async Task<bool> ValidateAsync()
    {
        ClearErrors();
        
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "分类名称不能为空";
            HasNameError = true;
            return false;
        }
        
        if (Name.Length > 50)
        {
            NameError = "分类名称不能超过50个字符";
            HasNameError = true;
            return false;
        }
        
        if (_isEditMode && _originalCategory != null)
        {
            if (await _websiteCategoryService.CategoryExistsAsync(Name, _originalCategory.Id))
            {
                NameError = "分类名称已存在";
                HasNameError = true;
                return false;
            }
        }
        else
        {
            if (await _websiteCategoryService.CategoryExistsAsync(Name))
            {
                NameError = "分类名称已存在";
                HasNameError = true;
                return false;
            }
        }
        
        return true;
    }
    
    public WebsiteCategory GetCategory()
    {
        var category = _originalCategory ?? new WebsiteCategory();
        category.Name = Name.Trim();
        category.Description = Description.Trim();
        category.Color = SelectedColor;
        category.Icon = SelectedIcon;
        category.UpdatedAt = DateTime.Now;
        
        if (!_isEditMode)
        {
            category.SortOrder = 99;
            category.IsSystem = false;
            category.IsActive = true;
        }
        
        return category;
    }
    
    private void ClearErrors()
    {
        NameError = string.Empty;
        HasNameError = false;
    }
}
