using Microsoft.UI.Xaml.Controls;
using Tai.App.ViewModels;

namespace Tai.App.Views;

public sealed partial class CategoryDetailDialog : ContentDialog
{
    public CategoryDetailDialogViewModel ViewModel { get; }
    
    public CategoryDetailDialog(CategoryDetailDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }
    
    public async Task LoadCategoryAsync(int categoryId)
    {
        await ViewModel.LoadCategoryAsync(categoryId);
    }
}
