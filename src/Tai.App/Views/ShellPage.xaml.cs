using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Tai.App.Services;

namespace Tai.App.Views;

public sealed partial class ShellPage : Page
{
    private readonly NavigationService _navigationService;
    private bool _isMonitoring;
    
    public ShellPage()
    {
        InitializeComponent();
        _navigationService = new NavigationService();
        _navigationService.Initialize(ContentFrame);
        
        Loaded += ShellPage_Loaded;
    }
    
    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Dashboard");
    }
    
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        Log.Information("NavView_ItemInvoked: IsSettingsInvoked={IsSettings}", args.IsSettingsInvoked);
        
        if (args.IsSettingsInvoked)
        {
            Log.Information("NavView_ItemInvoked: 导航到设置页面");
            _navigationService.NavigateTo("Settings");
        }
        else if (args.InvokedItemContainer != null)
        {
            var tag = args.InvokedItemContainer.Tag?.ToString();
            Log.Information("NavView_ItemInvoked: Tag={Tag}", tag);
            if (!string.IsNullOrEmpty(tag))
            {
                _navigationService.NavigateTo(tag);
            }
        }
    }
    
    private void ToggleMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        _isMonitoring = !_isMonitoring;
        
        if (_isMonitoring)
        {
            ToggleMonitorButton.Content = "暂停监控";
            StatusText.Text = "监控中...";
            StatusIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
        }
        else
        {
            ToggleMonitorButton.Content = "开始监控";
            StatusText.Text = "已暂停";
            StatusIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
    }
}
