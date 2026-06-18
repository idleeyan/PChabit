using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class CategoryEditDialog : ContentDialog
{
    public CategoryEditDialogViewModel ViewModel { get; }
    
    public CategoryEditDialog(CategoryEditDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        Closing += OnDialogClosing;
    }
    
    /// <summary>
    /// 异步验证流程：先做基本同步验证，再异步检查重名，防止 UI 线程阻塞。
    /// </summary>
    private async void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // 只在点击"确定"按钮时验证
        if (args.Result != ContentDialogResult.Primary)
            return;
        
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
            if (!await ViewModel.ValidateExistsAsync())
            {
                args.Cancel = true;
            }
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
