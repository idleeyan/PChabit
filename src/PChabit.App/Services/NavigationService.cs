using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;
using PChabit.App.Views;

namespace PChabit.App.Services;

public class NavigationService
{
    private Frame? _frame;
    private readonly Dictionary<string, Type> _pages = new();

    /// <summary>
    /// 3.34.0：合并页的旧导航 key → 默认页签。
    /// 导航到别名时自动补 <see cref="AppUsageNavArgs"/>，使旧调用落到原页面内容。
    /// </summary>
    private static readonly Dictionary<string, string> _aliasPivotTags = new()
    {
        ["AppStats"] = "ranking",
        ["KeyboardDetails"] = "keyboard",
    };

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

        // 3.34.0：「应用统计」与「键鼠详情」合并为「应用使用」单页双页签。
        // 旧 key 保留为别名，避免仪表盘「查看全部 →」等既有调用失效。
        Register<AppUsagePage>("AppUsage");
        Register<AppUsagePage>("AppStats");        // 兼容旧调用，落「应用排行」页签
        Register<AppUsagePage>("KeyboardDetails");  // 兼容旧调用，落「键鼠详情」页签

        Register<WebAccessPage>("WebAccess");
        Register<HardwareMonitorPage>("Monitor");
        Register<NotesPage>("Notes");
        Register<CategoryManagementPage>("CategoryManagement");
        Register<AnalyticsPage>("Analytics");
        Register<DataManagementPage>("DataManagement");
        Register<SettingsPage>("Settings");
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

            // 3.34.0：旧别名自动补上页签参数，使NavigateTo("KeyboardDetails")
            // 落到「键鼠详情」页签、NavigateTo("AppStats") 落到「应用排行」。
            if (parameter == null && pageType == typeof(AppUsagePage) && _aliasPivotTags.TryGetValue(key, out var tag))
            {
                parameter = new AppUsageNavArgs(tag);
            }

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

