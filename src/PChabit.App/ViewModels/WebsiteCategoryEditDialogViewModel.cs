using PChabit.Core.Entities;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

/// <summary>
/// 网站分类编辑对话框（3.33.0 通用逻辑见
/// <see cref="CategoryEditDialogViewModelBase"/>，本类只提供差异）。
/// </summary>
public partial class WebsiteCategoryEditDialogViewModel : CategoryEditDialogViewModelBase
{
    private readonly IWebsiteCategoryService _websiteCategoryService;

    protected override string AddDialogTitle => "添加新网站分类";
    protected override string EditDialogTitle => "编辑网站分类";
    protected override string DefaultIcon => "🌐";

    /// <summary>网站分类的图标候选以网络类为主，应用类图标在后。</summary>
    protected override IEnumerable<string> IconCandidates => new[]
    {
        "🌐", "🔍", "💻", "🎬", "📱", "🛒", "📧", "📊", "📰",
        "📁", "💬", "🎮", "🎨", "📝", "📚", "🎵", "📷", "🔧",
        "⚙️", "🎯", "🖥️", "💼", "🔬", "🛠️", "📡", "🏠"
    };

    public WebsiteCategoryEditDialogViewModel(IWebsiteCategoryService websiteCategoryService)
    {
        _websiteCategoryService = websiteCategoryService;
    }

    protected override Task<bool> NameExistsAsync(string name, int? excludeCategoryId)
        => _websiteCategoryService.CategoryExistsAsync(name, excludeCategoryId);

    public WebsiteCategory GetCategory()
        => (WebsiteCategory)GetCategoryEntity();

    public override ICategoryEntity GetCategoryEntity()
    {
        var category = OriginalCategory as WebsiteCategory ?? new WebsiteCategory();
        category.Name = Name.Trim();
        category.Description = Description.Trim();
        category.Color = SelectedColor;
        category.Icon = SelectedIcon;
        category.UpdatedAt = DateTime.Now;

        if (!IsEditMode)
        {
            category.SortOrder = 99;
            category.IsSystem = false;
            category.IsActive = true;
        }

        return category;
    }
}