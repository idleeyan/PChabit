using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Serilog;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using PChabit.App.Services;
using PChabit.App.ViewModels;
using PChabit.App.Views;
using PChabit.Core.Interfaces;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Monitoring;
using PChabit.Infrastructure.Services;
using PChabit.HardwareMonitor;

namespace PChabit.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private ServiceProvider? _serviceProvider;
    private MonitorManager? _monitorManager;
    private readonly TaskCompletionSource _dbInitCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DataCollectionService? _dataCollectionService;
    private HardwareMonitorService? _hardwareMonitorService;
    private HardwareSampleWriter? _hardwareSampleWriter;
    private TrayService? _trayService;
    private Microsoft.UI.Xaml.DispatcherTimer? _trayDisplayTimer;
    private TaskbarWidget? _taskbarWidget;
    // 桌面悬浮插件字段/驱动见 App.DesktopWidget.cs（3.23.0 分部类拆分）
    private double _todayActiveMinutes;
    private DateTime _todayUsageCacheTime = DateTime.MinValue;
    private long _todayDownBytes;
    private DateTime _todayDownCacheTime = DateTime.MinValue;
    private bool _isExiting;
    private readonly object _exitLock = new();
    
    public static IServiceProvider Services => ((App)Current)._serviceProvider!;
    
    public static T GetService<T>() where T : class
    {
        return ((App)Current)._serviceProvider!.GetRequiredService<T>();
    }
    
    public static TrayService Tray => ((App)Current)._trayService!;
    
    public static Window MainWindow => ((App)Current)._window!;
    
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _activationEvent;
    private static Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcherQueue;

    public App()
    {
        try
        {
            // 捕获 UI 线程调度器（后台监听线程 GetForCurrentThread 会返回 null，须在此捕获）
            _uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            // 单实例保护：多实例并发写同一 SQLite 数据库会导致同步卡死（等锁），
            // 已有实例在跑时本次启动直接退出。
            // 3.52.0 修复：Local\ 前缀仅同终端会话互斥，跨会话（如 RDP/切用户残留）会并存双实例，
            // 两个实例同时 HttpListener 绑定 8765 → 后启动实例 WebSocket 失败 → 监控链整体中断。
            // 改用 Global\ 前缀跨会话互斥，并用命名事件唤起已有实例窗口。
            _instanceMutex = new Mutex(true, @"Global\PChabit_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    using var evt = EventWaitHandle.OpenExisting(@"Global\PChabit_SingleInstance_Activate");
                    evt.Set();
                }
                catch (Exception ex)
                {
                    // 预期：已有实例可能是旧版本、未启用唤起监听，仅影响唤起不影响退出
                    Log.Debug(ex, "唤起已有实例失败（旧实例无监听句柄）");
                }
                Log.Warning("检测到已有 PChabit 实例运行，本次启动退出（单实例保护）");
                Environment.Exit(0);
                return;
            }

            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\PChabit_SingleInstance_Activate");
            // 后台监听唤起信号：另一会话/用户再次启动时把本实例窗口带回前台
            _ = Task.Run(() =>
            {
                try
                {
                    while (_activationEvent.WaitOne())
                    {
                        try
                        {
                            var dq = _uiDispatcherQueue;
                            if (dq != null)
                            {
                                dq.TryEnqueue(() =>
                                {
                                    try
                                    {
                                        if (_trayService != null) _trayService.RestoreWindow();
                                        _window?.Activate();
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Warning(ex, "唤起已有实例窗口失败");
                                    }
                                });
                            }
                            else
                            {
                                try
                                {
                                    if (_trayService != null) _trayService.RestoreWindow();
                                    _window?.Activate();
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning(ex, "唤起已有实例窗口失败");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "唤起监听异常");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 预期：事件句柄被释放（进程退出）时结束监听，属正常退出路径
                    Log.Debug(ex, "唤起监听已结束（句柄释放）");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "单实例互斥检查失败，继续启动");
        }

        try
        {
            // 关键：手动初始化 WindowsAppRuntime Bootstrap（双保险，配合 csproj 的
            // WindowsAppSDKBootstrapInitializeAtStartup=true）
            // 必须在 InitializeComponent 之前调用，否则 WinUI 控件创建会失败
            // （COMException 0x80004005 E_FAIL 或 0xc000027b STATUS_STOWED_EXCEPTION）
            // 使用反射调用，避免编译时对 Microsoft.WindowsAppRuntime.Bootstrap 命名空间的依赖
            try
            {
                var bootstrapAsm = Assembly.LoadFrom(AppContext.BaseDirectory + "Microsoft.WindowsAppRuntime.Bootstrap.Net.dll");
                var bootstrapType = bootstrapAsm.GetType("Microsoft.WindowsAppRuntime.Bootstrap.Bootstrap")
                    ?? bootstrapAsm.GetType("Microsoft.Windows.ApplicationModel.WindowsAppRuntime.Bootstrap");
                if (bootstrapType != null)
                {
                    var initMethod = bootstrapType.GetMethod("Initialize", new[] { typeof(uint) })
                              ?? bootstrapType.GetMethod("Initialize", Type.EmptyTypes);
                    if (initMethod != null)
                    {
                        var args = initMethod.GetParameters().Length == 1 ? new object[] { (uint)0 } : Array.Empty<object>();
                        initMethod.Invoke(null, args);
                        System.Diagnostics.Debug.WriteLine("Bootstrap.Initialize 成功 (反射调用)");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bootstrap.Initialize 失败（可能已自动初始化）: {ex.Message}");
            }

            InitializeComponent();

            ConfigureLogging();
            Log.Information("应用程序启动 (手动 Bootstrap 初始化已完成)");
            ConfigureServices();

            // 注册未处理异常处理器，捕获 XAML/后台线程的崩溃信息
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "应用程序初始化失败");
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "App_UnhandledException: {Message}", e.Message);
        Log.Fatal("  StackTrace: {StackTrace}", e.Exception?.StackTrace);
        Log.Fatal("  Source: {Source}", e.Exception?.Source);
        Log.Fatal("  InnerException: {Inner}", e.Exception?.InnerException);
    }

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log.Fatal(ex, "CurrentDomain_UnhandledException (IsTerminating={IsTerminating})", e.IsTerminating);
        Log.Fatal("  StackTrace: {StackTrace}", ex?.StackTrace);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "TaskScheduler_UnobservedTaskException");
        Log.Fatal("  StackTrace: {StackTrace}", e.Exception.StackTrace);
        e.SetObserved();
    }

    /// <summary>在 XAML 资源字典中注册转换器。App.xaml 中不再实例化，
    /// 避免 XAML 编译器 pass1 全量解析自身程序集类型时失败（WMC0001/WMC1509）。</summary>
    private void RegisterAppConverters()
    {
        Resources["InverseBooleanToVisibilityConverter"] = new PChabit.App.Converters.InverseBooleanToVisibilityConverter();
        Resources["BooleanToVisibilityConverter"] = new PChabit.App.Converters.BooleanToVisibilityConverter();
        Resources["NullToVisibilityConverter"] = new PChabit.App.Converters.NullToVisibilityConverter();
        Resources["DateToStringConverter"] = new PChabit.App.Converters.DateToStringConverter();
        Resources["StringToBrushConverter"] = new PChabit.App.Converters.StringToBrushConverter();
        Resources["TrendColorConverter"] = new PChabit.App.Converters.TrendColorConverter();
        Resources["InsightBackgroundConverter"] = new PChabit.App.Converters.InsightBackgroundConverter();
        Resources["PercentageConverter"] = new PChabit.App.Converters.PercentageConverter();
        Resources["InverseCountToVisibilityConverter"] = new PChabit.App.Converters.InverseCountToVisibilityConverter();
        Resources["StringToVisibilityConverter"] = new PChabit.App.Converters.StringToVisibilityConverter();
        Resources["SystemCategoryConverter"] = new PChabit.App.Converters.SystemCategoryConverter();
        Resources["InverseBooleanConverter"] = new PChabit.App.Converters.InverseBooleanConverter();
        Resources["HeatLevelToOpacityConverter"] = new PChabit.App.Converters.HeatLevelToOpacityConverter();
        Resources["PercentageToGridLengthConverter"] = new PChabit.App.Converters.PercentageToGridLengthConverter();
    }

    private void ConfigureLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PChabit", "Logs", "app-.log");
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Async(a => a.File(logPath, rollingInterval: RollingInterval.Day))
            .CreateLogger();
        
        Log.Information("应用程序启动");
    }
    
    private void ConfigureServices()
    {
        // 生产环境性能优化
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        ThreadPool.SetMinThreads(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        ThreadPool.SetMaxThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 4);
        Log.Information("已应用生产环境性能优化设置");

        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PChabit", "Data", "pchabit.db");
        
        var databaseDir = Path.GetDirectoryName(databasePath)!;
        if (!Directory.Exists(databaseDir))
        {
            Directory.CreateDirectory(databaseDir);
        }
        
        var services = new ServiceCollection();
        
        services.ConfigureServices(databasePath);
        
        services.AddSingleton<NavigationService>();
        services.AddSingleton<TrayService>();
        
        _serviceProvider = services.BuildServiceProvider();
        
        // 启动时立即加载设置，确保所有 ViewModel 构造函数可以读取到已保存的配置
        // （此前仅在 SettingsViewModel.InitializeAsync 中加载，若用户直接导航到数据管理页，
        //  WebDAV 等配置尚未从 JSON 加载，ViewModel 构造时会读到默认空值）
        try
        {
            _serviceProvider.GetRequiredService<Core.Interfaces.ISettingsService>().Load();
            Log.Information("启动时设置加载完成");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "启动时加载设置失败，使用默认值");
        }
        
        // 数据库初始化异步执行，不阻塞 UI 线程
        _ = Task.Run(async () =>
        {
            try
            {
                await ServiceConfiguration.EnableWalModeAsync(databasePath);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
                    await DatabaseInitializer.InitializeAsync(dbContext);
                    Log.Information("数据库初始化完成: {Path}", databasePath);
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "数据库初始化失败");
            }
            finally
            {
                _dbInitCompleted.TrySetResult();
            }
        });
    }

    private bool _startupServicesInitialized;

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        try
        {
            Log.Information("OnLaunched 开始");

            // 注册转换器到应用资源（在导航前完成，页面 XAML 的 StaticResource 依赖这些键）
            try
            {
                RegisterAppConverters();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "注册应用转换器失败");
            }

            _window = new Window();
            
            if (_window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                _window.Content = rootFrame;
            }
            
            Log.Information("导航到 ShellPage");
            _ = rootFrame.Navigate(typeof(ShellPage), e.Arguments);
            
            SetWindowIcon();
            
            // 在窗口首次激活后延迟初始化非关键服务，确保 UI 先渲染
            _window.Activated += OnWindowFirstActivated;
            
            _window.Activate();

            // 校正窗口位置：仅在窗口几乎完全离开屏幕时干预，避免把正常靠右/最大化窗口拽到左上角
            CorrectWindowBoundsIfOffscreen();

            Log.Information("窗口已激活");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "OnLaunched 失败");
            throw;
        }
    }

    private void OnWindowFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_startupServicesInitialized) return;
        _startupServicesInitialized = true;

        // 注销一次性事件处理
        if (sender is Window window)
        {
            window.Activated -= OnWindowFirstActivated;
        }

        Log.Information("窗口首次激活，延迟初始化后台服务");

        CorrectWindowBoundsIfOffscreen();

        // 使用低优先级延迟初始化，确保 UI 已完全渲染
        _ = Task.Run(async () =>
        {
            // 短暂延迟让 UI 线程完成首次渲染
            await Task.Delay(100);

            // 回到 UI 线程初始化托盘和监控（Win32 钩子需要 UI 线程的消息循环）
            _window!.DispatcherQueue.TryEnqueue(() =>
            {
                InitializeTrayService();
                InitializeStickyNotes();
                StartMonitoring();
            });

            // 后台服务可以在任意线程启动
            StartBackupService();
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isExiting)
        {
            Log.Information("窗口关闭 - 正在退出中，允许关闭");
            return;
        }
        
        var settings = _serviceProvider!.GetRequiredService<SettingsViewModel>();
        if (settings.MinimizeToTray)
        {
            Log.Information("窗口关闭 - 最小化到托盘");
            args.Handled = true;
            _trayService!.MinimizeToTray();
        }
    }

    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        Log.Fatal(e.Exception, "导航失败: {Page}", e.SourcePageType.FullName);
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }
}
