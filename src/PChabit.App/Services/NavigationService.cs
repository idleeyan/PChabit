using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;
using PChabit.App.Views;

namespace PChabit.App.Services;

public class NavigationService
{
    private Frame? _frame;
    private readonly Dictionary<string, Type> _pages = new();
    
    public event EventHandler<NavigatedEventArgs>? Navigated;
    
    public void Initialize(Frame frame)
    {
        _frame = frame;
        _frame.Navigated += OnFrameNavigated;
        
        RegisterPages();
    }
    
    private void RegisterPages()
    {
        Register<DashboardPage>("Dashboard");
        Register<TimelinePage>("Timeline");
        Register<AppStatsPage>("AppStats");
        Register<KeyboardDetailsPage>("KeyboardDetails");
        Register<WebDetailsPage>("WebDetails");
        Register<AnalyticsPage>("Analytics");
        Register<ExportPage>("Export");
        Register<CategoryManagementPage>("CategoryManagement");
        Register<SettingsPage>("Settings");
        Register<HeatmapPage>("Heatmap");
    }
    
    public void Register<T>(string key) where T : Microsoft.UI.Xaml.Controls.Page
    {
        _pages[key] = typeof(T);
    }
    
    public bool NavigateTo(string key, object? parameter = null)
    {
        Log.Information("NavigateTo: 开始导航到 {Key}", key);
        
        if (_frame == null)
        {
            Log.Warning("NavigateTo: Frame 为空");
            return false;
        }
        
        if (_pages.TryGetValue(key, out var pageType))
        {
            Log.Information("NavigateTo: 找到页面类型 {PageType}", pageType.Name);
            
            var transition = new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            };
            
            Log.Information("NavigateTo: 开始执行导航");
            _frame.Navigate(pageType, parameter, transition);
            Log.Information("NavigateTo: 导航完成");
            return true;
        }
        
        Log.Warning("NavigateTo: 未找到页面 {Key}", key);
        return false;
    }
    
    public bool GoBack()
    {
        if (_frame == null) return false;
        
        if (_frame.CanGoBack)
        {
            _frame.GoBack();
            return true;
        }
        
        return false;
    }
    
    public bool CanGoBack => _frame?.CanGoBack ?? false;
    
    public string? CurrentPageKey
    {
        get
        {
            if (_frame?.Content == null) return null;
            
            var currentPage = _frame.Content.GetType();
            return _pages.FirstOrDefault(p => p.Value == currentPage).Key;
        }
    }
    
    private void OnFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        Navigated?.Invoke(this, new NavigatedEventArgs
        {
            PageKey = CurrentPageKey,
            Parameter = e.Parameter
        });
    }
}

public class NavigatedEventArgs : EventArgs
{
    public string? PageKey { get; init; }
    public object? Parameter { get; init; }
}
