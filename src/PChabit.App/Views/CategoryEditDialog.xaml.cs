using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    }
    
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ViewModel.Validate())
        {
            args.Cancel = true;
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
