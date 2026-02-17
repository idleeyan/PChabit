using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }
    
    public DashboardPage()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<DashboardViewModel>();
        DataContext = ViewModel;
    }
    
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            await ViewModel.LoadDataAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "加载仪表盘数据失败");
        }
    }
    
    private void StatCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
        }
    }
    
    private void StatCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }
    }
    
    private async void KeyboardCard_Click(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var scope = App.Services.CreateScope();
        var dialogViewModel = scope.ServiceProvider.GetRequiredService<DetailDialogViewModel>();
        await dialogViewModel.LoadKeyboardDetailsAsync(DateTime.Today);
        
        var dialog = new DetailDialog(dialogViewModel)
        {
            XamlRoot = XamlRoot
        };
        
        await dialog.ShowAsync();
    }
    
    private async void MouseCard_Click(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var scope = App.Services.CreateScope();
        var dialogViewModel = scope.ServiceProvider.GetRequiredService<DetailDialogViewModel>();
        await dialogViewModel.LoadMouseDetailsAsync(DateTime.Today);
        
        var dialog = new DetailDialog(dialogViewModel)
        {
            XamlRoot = XamlRoot
        };
        
        await dialog.ShowAsync();
    }
    
    private async void WebCard_Click(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var scope = App.Services.CreateScope();
        var dialogViewModel = scope.ServiceProvider.GetRequiredService<DetailDialogViewModel>();
        await dialogViewModel.LoadWebDetailsAsync(DateTime.Today);
        
        var dialog = new DetailDialog(dialogViewModel)
        {
            XamlRoot = XamlRoot
        };
        
        await dialog.ShowAsync();
    }
}
