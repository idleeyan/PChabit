using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tai.App.ViewModels;

namespace Tai.App.Views;

public sealed partial class KeyboardDetailsPage : Page
{
    public KeyboardDetailsViewModel ViewModel { get; }
    
    public KeyboardDetailsPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<KeyboardDetailsViewModel>();
    }
    
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await ViewModel.LoadDataAsync();
    }
    
    private async void PreviousDay_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ChangeDateAsync(-1);
    }
    
    private async void NextDay_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.ChangeDateAsync(1);
    }
}
