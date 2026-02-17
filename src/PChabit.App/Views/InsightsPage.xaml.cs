using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class InsightsPage : Page
{
    public InsightsViewModel ViewModel { get; }

    public InsightsPage()
    {
        ViewModel = App.GetService<InsightsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadDataAsync();
    }
}
