using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PChabit.App.Views;

/// <summary>
/// 统一分类管理页：把原先分散在「应用统计-分类管理」与「网页访问-网站分类」的两套
/// 分类维护入口合并到一处，用页签切换应用 / 网站两类。
/// </summary>
public sealed partial class CategoryManagementPage : Page
{
    public CategoryManagementPage()
    {
        InitializeComponent();
    }
}
