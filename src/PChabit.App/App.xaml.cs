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
    
    private const int WM_SETICON = 0x0080;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

[DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x1000;
    private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x0100;
    private DataCollectionService? _dataCollectionService;
    private HardwareMonitorService? _hardwareMonitorService;
    private HardwareSampleWriter? _hardwareSampleWriter;
    private TrayService? _trayService;
    private Microsoft.UI.Xaml.DispatcherTimer? _trayDisplayTimer;
    private TaskbarWidget? _taskbarWidget;
    // 桌面悬浮插件字段/驱动见 App.DesktopWidget.cs（3.23.0 分部类拆分）
    private double _todayActiveMinutes;
    private DateTime _todayUsageCacheTime = DateTime.MinValue;
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

    public App()
    {
        try
        {
            // 单实例保护：多实例并发写同一 SQLite 数据库会导致同步卡死（等锁），
            // 已有实例在跑时本次启动直接退出。
            _instanceMutex = new Mutex(true, @"Local\PChabit_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                Log.Warning("检测到已有 PChabit 实例运行，本次启动退出（单实例保护）");
                Environment.Exit(0);
                return;
            }
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
        Resources["FirstLetterConverter"] = new PChabit.App.Converters.FirstLetterConverter();
        Resources["HoursToStringConverter"] = new PChabit.App.Converters.HoursToStringConverter();
        Resources["TrendColorConverter"] = new PChabit.App.Converters.TrendColorConverter();
        Resources["InsightBackgroundConverter"] = new PChabit.App.Converters.InsightBackgroundConverter();
        Resources["PercentageConverter"] = new PChabit.App.Converters.PercentageConverter();
        Resources["IntToProgramCountTextConverter"] = new PChabit.App.Converters.IntToProgramCountTextConverter();
        Resources["ColorSelectionConverter"] = new PChabit.App.Converters.ColorSelectionConverter();
        Resources["InverseBoolConverter"] = new PChabit.App.Converters.InverseBoolConverter();
        Resources["CountToVisibilityConverter"] = new PChabit.App.Converters.CountToVisibilityConverter();
        Resources["InverseCountToVisibilityConverter"] = new PChabit.App.Converters.InverseCountToVisibilityConverter();
        Resources["CountToEnabledConverter"] = new PChabit.App.Converters.CountToEnabledConverter();
        Resources["SelectedCountConverter"] = new PChabit.App.Converters.SelectedCountConverter();
        Resources["StringToVisibilityConverter"] = new PChabit.App.Converters.StringToVisibilityConverter();
        Resources["SystemCategoryConverter"] = new PChabit.App.Converters.SystemCategoryConverter();
        Resources["InverseBooleanConverter"] = new PChabit.App.Converters.InverseBooleanConverter();
        Resources["TimeSpanToReadableConverter"] = new PChabit.App.Converters.TimeSpanToReadableConverter();
        Resources["DoubleToScoreConverter"] = new PChabit.App.Converters.DoubleToScoreConverter();
        Resources["HeatLevelToOpacityConverter"] = new PChabit.App.Converters.HeatLevelToOpacityConverter();
        Resources["ActivityToColorConverter"] = new PChabit.App.Converters.ActivityToColorConverter();
        Resources["ActivityToForegroundConverter"] = new PChabit.App.Converters.ActivityToForegroundConverter();
        Resources["DateToDetailConverter"] = new PChabit.App.Converters.DateToDetailConverter();
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

    private void SetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "pchabit.ico");
            if (File.Exists(iconPath))
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(_window!);
                var hIcon = LoadImage(
                    IntPtr.Zero,
                    iconPath,
                    IMAGE_ICON,
                    32, 32,
                    LR_LOADFROMFILE);
                
                if (hIcon != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_SETICON, IntPtr.Zero, hIcon);
                    SendMessage(hWnd, WM_SETICON, (IntPtr)1, hIcon);
                    Log.Information("窗口图标设置成功");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "设置窗口图标失败");
        }
    }
    
    /// <summary>
    /// 仅当窗口几乎完全离开主屏、或尺寸异常时才校正。
    /// 禁止用「75% 屏幕」当边界——最大化/靠右窗口会被误判并拽到左上角。
    /// </summary>
    private void CorrectWindowBoundsIfOffscreen()
    {
        try
        {
            var hWnd = FindMainWindowHandle();
            if (hWnd == IntPtr.Zero || !GetWindowRect(hWnd, out var rect)) return;

            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            if (w < 80 || h < 80) return;

            // 最大化/全屏窗口不干预
            if (IsZoomed(hWnd)) return;

            var screenW = GetSystemMetrics(0);
            var screenH = GetSystemMetrics(1);
            if (screenW <= 0 || screenH <= 0) return;

            // 窗口与主屏几乎无交集（露头 < 48px）或尺寸离谱才校正
            var visibleW = Math.Max(0, Math.Min(rect.Right, screenW) - Math.Max(rect.Left, 0));
            var visibleH = Math.Max(0, Math.Min(rect.Bottom, screenH) - Math.Max(rect.Top, 0));
            var mostlyOff = visibleW < 48 || visibleH < 48;
            var absurdSize = w > screenW * 1.25 || h > screenH * 1.25;
            if (!mostlyOff && !absurdSize) return;

            if (w > screenW * 0.95) w = (int)(screenW * 0.9);
            if (h > screenH * 0.95) h = (int)(screenH * 0.85);
            var x = Math.Max(0, (screenW - w) / 2);
            var y = Math.Max(0, (screenH - h) / 2);
            SetWindowPos(hWnd, IntPtr.Zero, x, y, w, h, 0x0040);
            Log.Information("窗口离屏/尺寸异常，已校正到 ({X},{Y}) {W}x{H}（原 {OL},{OT},{OR},{OB}）",
                x, y, w, h, rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "校正窗口位置失败");
        }
    }
    private static IntPtr FindMainWindowHandle()
    {
        var pid = (uint)Environment.ProcessId;
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint wndPid);
            if (wndPid != pid) return true;
            GetWindowRect(hWnd, out var r);
            if ((r.Right - r.Left) > 100 && (r.Bottom - r.Top) > 100)
            {
                result = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private void InitializeTrayService()
    {
        try
        {
            _trayService = _serviceProvider!.GetRequiredService<TrayService>();
            _trayService.Initialize(_window!);

            _trayService.ExitRequested += (s, e) =>
            {
                Log.Information("ExitRequested 事件触发");
                BeginShutdown();
            };

            _window!.Closed += OnWindowClosed;


            Log.Information("系统托盘服务初始化完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "初始化系统托盘服务失败");
        }
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
    
    private async void StartMonitoring()
    {
        try
        {
            _monitorManager = _serviceProvider!.GetRequiredService<MonitorManager>();

            // 低级输入钩子安装在专用消息泵线程上，避免 UI 线程布局/渲染阻塞导致
            // 全系统鼠标停顿。钩子重启也派发到该线程。
            var hookThread = _serviceProvider!.GetRequiredService<InputHookThread>();
            hookThread.Start();
            _monitorManager.HookThread = hookThread;
            // 兼容旧路径：钩子线程不可用时才回退 UI 调度器
            _monitorManager.UIDispatcher = action => Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => action());

            _dataCollectionService = _serviceProvider!.GetRequiredService<DataCollectionService>();

            // 书签/历史同步消息处理器：必须在 WebSocketServer 启动前完成订阅
            _serviceProvider!.GetRequiredService<BrowserSyncWebSocketHandler>();

            await _monitorManager.StartAllAsync();
            Log.Information("监控器已启动 - AppMonitor: {AppRunning}, KeyboardMonitor: {KeyboardRunning}, MouseMonitor: {MouseRunning}, HookThread: {HookThread}",
                _monitorManager.IsRunning,
                _serviceProvider!.GetRequiredService<Core.Interfaces.IKeyboardMonitor>().IsRunning,
                _serviceProvider!.GetRequiredService<Core.Interfaces.IMouseMonitor>().IsRunning,
                hookThread.IsRunning);

            _dataCollectionService.Start();
            Log.Information("数据收集服务已启动");

            // 周自动 AI 解读（延迟 2 分钟，避免抢启动 IO）
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2));
                    var weekly = _serviceProvider!.GetService<Infrastructure.Services.IWeeklyAiInsightService>();
                    if (weekly is not null)
                        await weekly.TryRunWeeklyAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "周自动 AI 解读调度失败");
                }
            });

            // 硬件监控（LiteMonitor 核心移植）
            _hardwareMonitorService = _serviceProvider!.GetRequiredService<HardwareMonitorService>();
            _hardwareMonitorService.Start();
            Log.Information("硬件监控服务已启动");

            // 进程网络流量监视（任务管理器式：最大占用进程 + 流量统计）
            var processNetwork = _serviceProvider!.GetRequiredService<PChabit.HardwareMonitor.Hardware.ProcessNetworkMonitor>();
            processNetwork.NetUpGetter = () => _hardwareMonitorService?.Get("NET.Up");
            processNetwork.NetDownGetter = () => _hardwareMonitorService?.Get("NET.Down");
            processNetwork.Start();
            Log.Information("进程网络流量监视已启动");

            // 进程 CPU/内存/磁盘/GPU 占用（硬件卡片最大占用进程）
            // 3.24.0 性能优化：不再随程序启动常驻（PDH GPU 计数器每轮约 0.18 核），
            // 改为硬件监控页打开时 Start、离开页面 Stop（见 HardwareMonitorViewModel）。
            Log.Information("进程资源占用监视：按需启动（硬件监控页）");

            // 网络流量历史落库（独立统计页数据源）
            _serviceProvider!.GetRequiredService<NetworkTrafficPersistenceService>().Start();
            Log.Information("网络流量落库服务已启动");

            // 硬件分钟样本落库（分析升级 P0，与硬件监控同生命周期）
            _hardwareSampleWriter = _serviceProvider!.GetRequiredService<HardwareSampleWriter>();
            _hardwareSampleWriter.Start();

            StartTrayDisplayTimer();
            StartDesktopWidgetTimer();

        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动监控服务失败");
        }
    }

    private void StartTrayDisplayTimer()
    {
        try
        {
            _trayDisplayTimer?.Stop();
            _trayDisplayTimer = new Microsoft.UI.Xaml.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _trayDisplayTimer.Tick += (_, _) => RefreshTrayDisplay();
            _trayDisplayTimer.Start();
            Log.Information("任务栏/托盘显示定时器已启动（5s 刷新）");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "启动任务栏/托盘显示定时器失败");
        }
    }

    private void RefreshTrayDisplay()
    {
        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            RefreshTaskbarWidget(settings);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "刷新任务栏小窗失败");
        }
    }

    /// <summary>驱动任务栏小窗：开关 + 两行文本（AI_MAINTENANCE 3.9.1 记录）。</summary>
    private void RefreshTaskbarWidget(ISettingsService settings)
    {
        if (!settings.TaskbarEnabled)
        {
            if (_taskbarWidget != null && _taskbarWidget.IsRunning)
            {
                _taskbarWidget.Dispose();
                _taskbarWidget = null;
                Log.Information("任务栏小窗已隐藏");
            }
            return;
        }

        if (_taskbarWidget == null)
        {
            _taskbarWidget = new TaskbarWidget();
        }
        // Start 幂等：失效句柄会重建，父窗口变化会重挂载（Explorer 重启后可恢复）
        _taskbarWidget.Start();
        if (!_taskbarWidget.IsRunning) return;

        double todayMinutes = GetTodayActiveMinutes();
        var (row1, row2) = BuildTaskbarRows(settings, todayMinutes);
        _taskbarWidget.UpdateContent(row1, row2);
        _taskbarWidget.SetTheme(IsSystemLightTheme());
    }

    /// <summary>
    /// 组装任务栏两行：行1 主指标（负载，大字焦点），行2 次指标（网速/磁盘/温度/今日，弱化）。
    /// 标签尽量短，温度用 C/G 消歧，避免和负载项的 CPU/GPU 撞名。
    /// </summary>
    private (List<TaskbarMetricItem> row1, List<TaskbarMetricItem> row2) BuildTaskbarRows(ISettingsService settings, double todayMinutes)
    {
        var hw = _hardwareMonitorService;
        var row1 = new List<TaskbarMetricItem>();
        var row2 = new List<TaskbarMetricItem>();

        // 主行：使用率大项
        if (settings.TaskbarShowCpu)
            row1.Add(new TaskbarMetricItem("CPU", FormatPercent(hw?.Get("CPU.Load")), LoadColor(hw?.Get("CPU.Load"))));
        if (settings.TaskbarShowMemory)
            row1.Add(new TaskbarMetricItem("内存", FormatPercent(hw?.Get("MEM.Load")), LoadColor(hw?.Get("MEM.Load"))));
        if (settings.TaskbarShowGpu)
            row1.Add(new TaskbarMetricItem("GPU", FormatPercent(hw?.Get("GPU.Load")), LoadColor(hw?.Get("GPU.Load"))));

        var vramUsed = hw?.Get("GPU.VRAM.Used");
        var vramTotal = hw?.Get("GPU.VRAM.Total");
        if (settings.TaskbarShowGpu && vramTotal.HasValue && vramTotal > 0)
        {
            float vramPct = (float)(vramUsed.GetValueOrDefault() / vramTotal.Value * 100.0);
            row1.Add(new TaskbarMetricItem("显存", FormatPercent(vramPct), LoadColor(vramPct)));
        }

        // 次行：次要信息，短前缀
        if (settings.TaskbarShowNet)
        {
            row2.Add(new TaskbarMetricItem("",
                $"↓{FormatSpeedShort(hw?.Get("NET.Down"))} ↑{FormatSpeedShort(hw?.Get("NET.Up"))}", TaskbarMetricColor.Safe));
        }
        if (settings.TaskbarShowDisk)
            row2.Add(new TaskbarMetricItem("盘", FormatPercent(hw?.Get("DISK.Activity")), LoadColor(hw?.Get("DISK.Activity"))));
        if (settings.TaskbarShowTemp)
        {
            row2.Add(new TaskbarMetricItem("C", FormatTempShort(hw?.Get("CPU.Temp")), TempColor(hw?.Get("CPU.Temp"))));
            row2.Add(new TaskbarMetricItem("G", FormatTempShort(hw?.Get("GPU.Temp")), TempColor(hw?.Get("GPU.Temp"))));
        }
        if (settings.TaskbarShowUsage)
            row2.Add(new TaskbarMetricItem("今", FormatUsage(todayMinutes, settings.DailyUsageGoalHours), TaskbarMetricColor.Safe));

        return (row1, row2);
    }

    // 桌面硬件悬浮插件（3.23.0）驱动已拆分至 App.DesktopWidget.cs

    private static TaskbarMetricColor LoadColor(float? v) =>
        v.HasValue && !float.IsNaN(v.Value)
            ? v.Value >= 90 ? TaskbarMetricColor.Crit : v.Value >= 70 ? TaskbarMetricColor.Warn : TaskbarMetricColor.Safe
            : TaskbarMetricColor.Safe;

    private static TaskbarMetricColor TempColor(float? v) =>
        v.HasValue && !float.IsNaN(v.Value)
            ? v.Value >= 85 ? TaskbarMetricColor.Crit : v.Value >= 70 ? TaskbarMetricColor.Warn : TaskbarMetricColor.Safe
            : TaskbarMetricColor.Safe;

    private static string FormatUsage(double minutes, double goalHours)
    {
        var goal = Math.Max(0.1, goalHours);
        return $"{minutes / 60:F1}/{goal:0.#}h";
    }

    /// <summary>系统深浅主题（任务栏小窗配色自适应）。</summary>
    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int i && i == 1;
        }
        catch { return false; }
    }

    /// <summary>今日活跃时长（分钟，60s 缓存，来自 AppSessions 实时查询；DailySummary 仅含昨日）。</summary>
    private double GetTodayActiveMinutes()
    {
        if ((DateTime.Now - _todayUsageCacheTime).TotalSeconds < 60) return _todayActiveMinutes;
        _todayUsageCacheTime = DateTime.Now;

        try
        {
            var today = DateTime.Today;
            using var scope = _serviceProvider!.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            var sessions = dbContext.AppSessions.AsNoTracking()
                .Where(s => s.StartTime >= today && s.StartTime < today.AddDays(1))
                .Select(s => new { s.StartTime, s.EndTime, s.ActiveDuration })
                .ToList();

            double minutes = 0;
            foreach (var s in sessions)
            {
                var d = s.ActiveDuration > TimeSpan.Zero
                    ? s.ActiveDuration
                    : (s.EndTime.HasValue ? s.EndTime.Value - s.StartTime : TimeSpan.Zero);
                if (d > TimeSpan.Zero) minutes += d.TotalMinutes;
            }
            _todayActiveMinutes = minutes;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "查询今日活跃时长失败");
        }
        return _todayActiveMinutes;
    }

    private static string FormatPercent(float? v) =>
        v.HasValue && !float.IsNaN(v.Value) ? $"{Math.Clamp(v.Value, 0, 999):F0}%" : "--";

    /// <summary>任务栏次行网速：去掉 /s 后缀，缩短占位。</summary>
    private static string FormatSpeedShort(float? bytesPerSec)
    {
        if (!bytesPerSec.HasValue || float.IsNaN(bytesPerSec.Value) || bytesPerSec.Value < 0) return "--";
        double v = bytesPerSec.Value;
        if (v < 1024) return $"{v:F0}B";
        if (v < 1024 * 1024) return $"{v / 1024:F0}K";
        if (v < 1024.0 * 1024 * 1024) return $"{v / 1024 / 1024:F1}M";
        return $"{v / 1024 / 1024 / 1024:F1}G";
    }

    /// <summary>任务栏次行温度：仅度数，来源用 C/G 短标签消歧。</summary>
    private static string FormatTempShort(float? v) =>
        v.HasValue && !float.IsNaN(v.Value) ? $"{v.Value:F0}°" : "--";



    private void StartBackupService()
    {
        try
        {
            var backupService = _serviceProvider!.GetRequiredService<Core.Interfaces.IBackupService>();
            var settings = _serviceProvider!.GetRequiredService<Core.Interfaces.ISettingsService>();

            if (settings.AutoBackupEnabled)
            {
                // 必须等数据库迁移真正完成再 VACUUM；迁移可能因锁卡住，超时后跳过本次启动备份
                _ = Task.Run(async () =>
                {
                    var finished = await Task.WhenAny(_dbInitCompleted.Task, Task.Delay(TimeSpan.FromSeconds(15)));
                    if (finished != _dbInitCompleted.Task)
                    {
                        Log.Warning("数据库初始化未在 15 秒内完成，跳过启动时自动备份（避免 VACUUM 与迁移争锁）");
                    }
                    else
                    {
                        Log.Information("执行启动时自动备份");
                        try { await backupService.CreateBackupAsync(); }
                        catch (Exception ex) { Log.Warning(ex, "启动时自动备份失败"); }
                    }

                    backupService.StartPeriodicBackupAsync(TimeSpan.FromHours(settings.AutoBackupIntervalHours));
                    Log.Information("定时备份服务已启动，间隔: {Hours} 小时", settings.AutoBackupIntervalHours);
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动备份服务失败");
        }
    }
    
    private void BeginShutdown()
    {
        lock (_exitLock)
        {
            if (_isExiting)
            {
                Log.Warning("已经在退出过程中，跳过重复请求");
                return;
            }
            _isExiting = true;
        }
        
        Log.Information("开始异步关闭流程");
        
        _ = Task.Run(async () =>
        {
            try
            {
                await PerformShutdownAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "异步关闭流程失败");
                ForceTerminate();
            }
        });
    }
    
    private async Task PerformShutdownAsync()
    {
        var processId = Environment.ProcessId;
        Log.Information("开始关闭应用程序 - 进程ID: {ProcessId}", processId);
        
        try
        {
            Log.Information("步骤 1/5: 停止数据收集服务...");
            _dataCollectionService?.Stop();
            Log.Information("数据收集服务已停止");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "停止数据收集服务失败");
        }
        
        try
        {
            Log.Information("步骤 2/5: 停止监控器...");
            if (_monitorManager != null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _monitorManager.StopAllAsync().WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log.Warning("停止监控器超时，继续关闭");
                }
            }
            Log.Information("监控器已停止");

            try
            {
                // 先停样本写入器（提交残余分钟桶），再停硬件采集
                if (_hardwareSampleWriter != null)
                {
                    await _hardwareSampleWriter.StopAsync();
                    _hardwareSampleWriter.Dispose();
                    _hardwareSampleWriter = null;
                }

                _hardwareMonitorService?.Stop();
                Log.Information("硬件监控服务已停止");

                try
                {
                    _serviceProvider?.GetService<PChabit.HardwareMonitor.Hardware.ProcessNetworkMonitor>()?.Stop();
                }
                catch { /* ignore */ }

                try
                {
                    _serviceProvider?.GetService<PChabit.HardwareMonitor.Hardware.ProcessResourceMonitor>()?.Stop();
                }
                catch { /* ignore */ }

                try
                {
                    _serviceProvider?.GetService<NetworkTrafficPersistenceService>()?.Stop();
                }
                catch { /* ignore */ }

                _trayDisplayTimer?.Stop();
                _trayDisplayTimer = null;
                _taskbarWidget?.Dispose();
                _taskbarWidget = null;

                StopDesktopWidgetTimer();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "停止硬件监控服务失败");
            }

            try
            {
                _serviceProvider?.GetService<InputHookThread>()?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "停止输入钩子线程失败");
            }

            try
            {
                StopStickyNotes();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "停止便签热键/同步失败");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "停止监控器失败");
        }
        
        try
        {
            Log.Information("步骤 3/5: 释放托盘服务...");
            _trayService?.Dispose();
            Log.Information("托盘服务已释放");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "释放托盘服务失败");
        }
        
        try
        {
            Log.Information("步骤 4/5: 关闭窗口...");
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // 悬浮窗由 UI 线程创建，DestroyWindow 必须同线程（步骤 2 已停 1s 定时器）
                    DisposeDesktopWidgetOnUiThread();
                    _window.Close();
                    Log.Information("窗口已关闭");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "关闭窗口失败");
                }
            });
            
            await Task.Delay(200);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "关闭窗口失败");
        }
        
        try
        {
            // 跳过 Dispose：SQLite WAL checkpoint 可能产生大量磁盘 IO 导致系统级卡顿
            // 进程退出时操作系统会回收所有资源，无需显式释放
            Log.Information("步骤 5/5: 跳过服务提供者释放，避免 SQLite WAL checkpoint 阻塞...");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "关闭流程记录失败");
        }
        
        Log.Information("所有清理步骤完成，准备退出");
        Log.CloseAndFlush();
        
        ForceTerminate();
    }
    
    private void ForceTerminate()
    {
        try
        {
            Log.Information("强制终止进程...");
            var currentProcess = Process.GetCurrentProcess();
            currentProcess.Kill();
        }
        catch
        {
            Environment.Exit(0);
        }
    }
    
    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        Log.Fatal(e.Exception, "导航失败: {Page}", e.SourcePageType.FullName);
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }
}

