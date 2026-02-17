using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;

namespace PChabit.App.Views;

public sealed partial class WebsiteCategoryEditDialog : ContentDialog
{
    public WebsiteCategoryEditDialogViewModel ViewModel { get; }
    public bool Success { get; private set; }
    public WebsiteCategory? ResultCategory { get; private set; }

    public WebsiteCategoryEditDialog()
    {
        Log.Information("WebsiteCategoryEditDialog: 构造函数开始");
        
        ViewModel = App.GetService<WebsiteCategoryEditDialogViewModel>();
        
        InitializeComponent();
        
        Log.Information("WebsiteCategoryEditDialog: 构造函数完成");
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

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Log.Information("WebsiteCategoryEditDialog: 确定按钮点击");
        
        var deferral = args.GetDeferral();
        
        try
        {
            var isValid = await ViewModel.ValidateAsync();
            if (!isValid)
            {
                args.Cancel = true;
                return;
            }
            
            ResultCategory = ViewModel.GetCategory();
            Success = true;
            Log.Information("WebsiteCategoryEditDialog: 验证通过，准备关闭");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebsiteCategoryEditDialog: 验证失败");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
