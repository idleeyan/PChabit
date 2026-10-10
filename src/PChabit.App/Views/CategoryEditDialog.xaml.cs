using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;

namespace PChabit.App.Views;

/// <summary>
/// 分类编辑对话框（3.33.0 统一应用分类与网站分类）。
///
/// 此前两者是两个独立 ContentDialog，XAML 相似度 98.9%、
/// code-behind 只差「用 Closing 还是 PrimaryButtonClick 做校验」——
/// 且应用版的 Closing + GetDeferral 写法才是正确做法（见陷阱 2）。
/// 现网站分类版 <see cref="WebsiteCategoryEditDialog"/> 纯代码继承本类，共用同一份 XAML。
/// </summary>
public partial class CategoryEditDialog : ContentDialog
{
    public CategoryEditDialogViewModelBase ViewModel { get; }

    /// <summary>校验通过并关闭时为 true。</summary>
    public bool Success { get; private set; }

    /// <summary>校验通过后产出的分类实体（未通过时为 null）。</summary>
    public ICategoryEntity? ResultCategory { get; private set; }

    public CategoryEditDialog(CategoryEditDialogViewModelBase viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        Closing += OnDialogClosing;
    }

    /// <summary>
    /// 异步验证流程：先做基本同步验证，再异步检查重名，防止 UI 线程阻塞。
    /// 用 Closing + GetDeferral 而非 PrimaryButtonClick（后者无法异步验证）。
    /// </summary>
    private async void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result != ContentDialogResult.Primary)
        {
            return;
        }

        // 第一步：基本验证（同步，不涉及数据库）
        if (!ViewModel.ValidateBasic())
        {
            args.Cancel = true;
            return;
        }

        // 第二步：异步重名检查（避免 UI 线程同步等待数据库）
        var deferral = args.GetDeferral();
        try
        {
            if (!await ViewModel.ValidateAsync())
            {
                args.Cancel = true;
                return;
            }

            ResultCategory = ViewModel.GetCategoryEntity();
            Success = true;
            Log.Information("CategoryEditDialog: 校验通过，产出「{Name}」", ResultCategory.Name);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "验证分类名称时发生异常");
            // 异常时不阻止用户操作
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnColorTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is string color)
        {
            ViewModel.SelectedColor = color;
        }
    }

    private void OnIconTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is string icon)
        {
            ViewModel.SelectedIcon = icon;
        }
    }
}