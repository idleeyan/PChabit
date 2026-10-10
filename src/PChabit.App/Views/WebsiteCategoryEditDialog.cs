using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;

namespace PChabit.App.Views;

/// <summary>
/// 网站分类编辑对话框（3.33.0）。
///
/// 界面与校验流程完全复用 <see cref="CategoryEditDialog"/>（XAML 相似度原本 98.9%），
/// 本类只做类型收窄，供 DI 注册与 <c>WebsiteCategoryTab</c> 使用。
/// </summary>
public sealed class WebsiteCategoryEditDialog : CategoryEditDialog
{
    public new WebsiteCategoryEditDialogViewModel ViewModel =>
        (WebsiteCategoryEditDialogViewModel)base.ViewModel;

    /// <summary>校验通过后产出的网站分类（未通过时为 null）。</summary>
    public WebsiteCategory? ResultWebsiteCategory =>
        ResultCategory as WebsiteCategory;

    public WebsiteCategoryEditDialog(WebsiteCategoryEditDialogViewModel viewModel)
        : base(viewModel)
    {
        Log.Information("WebsiteCategoryEditDialog: 构造完成（复用 CategoryEditDialog 界面）");
    }
}