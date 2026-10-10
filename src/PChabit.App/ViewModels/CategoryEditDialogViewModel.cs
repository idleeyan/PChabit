using PChabit.Core.Entities;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

/// <summary>
/// 应用分类编辑对话框（3.33.0 通用逻辑见
/// <see cref="CategoryEditDialogViewModelBase{TCategory}"/>，本类只提供差异）。
/// </summary>
public partial class CategoryEditDialogViewModel : CategoryEditDialogViewModelBase
{
    private readonly ICategoryService _categoryService;

    protected override string AddDialogTitle => "添加新分类";
    protected override string EditDialogTitle => "编辑分类";
    protected override string DefaultIcon => "📁";

    public CategoryEditDialogViewModel(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    protected override Task<bool> NameExistsAsync(string name, int? excludeCategoryId)
        => _categoryService.CategoryExistsAsync(name, excludeCategoryId);

    public ProgramCategory GetCategory()
        => (ProgramCategory)GetCategoryEntity();

    public override ICategoryEntity GetCategoryEntity()
    {
        var category = OriginalCategory as ProgramCategory ?? new ProgramCategory();
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

    /// <summary>
    /// 同步验证（仅基本检查 + 内存外同步存在性查询）。
    /// 保留用于向后兼容；新代码应使用 ValidateBasic() / ValidateAsync()。
    /// </summary>
    public bool Validate()
    {
        if (!ValidateBasic())
        {
            return false;
        }

        try
        {
            return _categoryService.CategoryExists(Name);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Validate 同步检查分类名称失败: {Name}", Name);
            return true;
        }
    }
}