using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PChabit.Core.Entities;

namespace PChabit.App.ViewModels;

/// <summary>
/// 分类编辑对话框 ViewModel 的公共骨架（3.33.0）。
///
/// 应用分类与网站分类两个编辑对话框的 XAML 相似度 98.9%（仅差一个事件），
/// ViewModel 也是逐行对称，此前是两个独立文件——改一次校验规则要记得改两处。
/// 这里把同构部分上移，差异点由子类实现。
///
/// 本类**刻意不泛型化**：ContentDialog 的 XAML 需与 x:Class 一一对应，
/// 泛型视图无法生成 XAML。故对外暴露非泛型接口成员（名称 / 颜色 / 图标…），
/// 让同一份 XAML 能绑定两类实例。
/// </summary>
public abstract partial class CategoryEditDialogViewModelBase : ObservableObject
{
    private ICategoryEntity? _originalCategory;
    private bool _isEditMode;

    /// <summary>「添加」模式的对话框标题。</summary>
    protected abstract string AddDialogTitle { get; }

    /// <summary>「编辑」模式的对话框标题。</summary>
    protected abstract string EditDialogTitle { get; }

    /// <summary>新建分类时选中的默认图标。</summary>
    protected abstract string DefaultIcon { get; }

    /// <summary>图标候选集（子类可按场景给出更贴切的前排项）。</summary>
    protected virtual IEnumerable<string> IconCandidates => DefaultIcons;

    /// <summary>通用图标候选集。</summary>
    protected static readonly string[] DefaultIcons =
    {
        "📁", "💻", "🌐", "💬", "🎮", "📊", "🎨", "📝", "📚",
        "🎵", "🎬", "📷", "🔧", "⚙️", "🎯", "📱", "🖥️", "💼",
        "📧", "🔬", "🛠️", "📡", "🎪", "🏠", "✈️", "🚗"
    };

    /// <summary>异步校验分类名是否已存在（各服务查询不同表）。</summary>
    protected abstract Task<bool> NameExistsAsync(string name, int? excludeCategoryId);

    [ObservableProperty]
    private string _dialogTitle = "添加新分类";

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _selectedColor = "#4A90E4";

    [ObservableProperty]
    private string _selectedIcon = "📁";

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

    public ObservableCollection<string> AvailableIcons { get; } = new();

    protected CategoryEditDialogViewModelBase()
    {
        foreach (var icon in IconCandidates)
        {
            AvailableIcons.Add(icon);
        }
    }

    // ===== 同构部分 =====

    public void InitializeForAdd()
    {
        _isEditMode = false;
        _originalCategory = null;
        DialogTitle = AddDialogTitle;
        Name = string.Empty;
        Description = string.Empty;
        SelectedColor = AvailableColors[0];
        SelectedIcon = DefaultIcon;
        IsSystemCategory = false;
        CanEditName = true;
        ClearErrors();
    }

    public void InitializeForEdit(ICategoryEntity category)
    {
        _isEditMode = true;
        _originalCategory = category;
        DialogTitle = EditDialogTitle;
        Name = category.Name;
        Description = category.Description ?? string.Empty;
        SelectedColor = category.Color;
        SelectedIcon = category.Icon;
        IsSystemCategory = category.IsSystem;
        CanEditName = !category.IsSystem;
        ClearErrors();
    }

    /// <summary>非数据库校验（空值 / 长度）。</summary>
    public bool ValidateBasic()
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

        return true;
    }

    /// <summary>完整校验：基本规则 + 名称唯一性。</summary>
    public async Task<bool> ValidateAsync()
    {
        if (!ValidateBasic())
        {
            return false;
        }

        try
        {
            var excludeId = _isEditMode ? _originalCategory?.Id : null;
            if (await NameExistsAsync(Name, excludeId))
            {
                NameError = "分类名称已存在";
                HasNameError = true;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "检查分类名称是否存在失败: {Name}", Name);
            // DB 查询失败时不阻止用户操作，由 SaveChanges 时的 UNIQUE 约束兜底
            return true;
        }
    }

    /// <summary>把编辑结果物化为分类实体（类型由子类决定）。</summary>
    public abstract ICategoryEntity GetCategoryEntity();

    /// <summary>当前是否为编辑模式（新建时为 false）。</summary>
    protected bool IsEditMode => _isEditMode;

    /// <summary>编辑前的原始实体（新建时为 null），子类物化时复用以保留 Id。</summary>
    protected ICategoryEntity? OriginalCategory => _originalCategory;

    protected void ClearErrors()
    {
        NameError = string.Empty;
        HasNameError = false;
    }
}