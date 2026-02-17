using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class DetailDialog : ContentDialog
{
    public DetailDialogViewModel ViewModel { get; }
    
    public DetailDialog()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<DetailDialogViewModel>();
        DataContext = ViewModel;
    }
    
    public DetailDialog(DetailDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }
}
