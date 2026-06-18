using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PChabit.App.Services;
using PChabit.App.ViewModels;
using PChabit.App.Views;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;

namespace PChabit.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private ServiceProvider? _serviceProvider;
    private MonitorManager? _monitorManager;
    
    private const int WM_SETICON = 0x0080;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);
    
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    private DataCollectionService? _dataCollectionService;
    private TrayService? _trayService;
    private TrayProgressRefresher? _trayProgressRefresher;
    private bool _isExiting;
    private readonly object _exitLock = new();
    
    public static IServiceProvider Services => ((App)Current)._serviceProvider!;
    
    public static T GetService<T>() where T : class
    {
        return ((App)Current)._serviceProvider!.GetRequiredService<T>();
    }
    
    public static TrayService Tray => ((App)Current)._trayService!;
    
    public static Window MainWindow => ((App)Current)._window!;
    
    public App()
    {
        try
        {
            InitializeComponent();
            
            ConfigureLogging();
            ConfigureServices();
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "应用程序初始化失败");
            throw;
        }
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
        });
    }
    
    private bool _startupServicesInitialized;

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        try
        {
            Log.Information("OnLaunched 开始");
            
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
        
        // 使用低优先级延迟初始化，确保 UI 已完全渲染
        _ = Task.Run(async () =>
        {
            // 短暂延迟让 UI 线程完成首次渲染
            await Task.Delay(100);
            
            // 回到 UI 线程初始化托盘和监控（Win32 钩子需要 UI 线程的消息循环）
            _window!.DispatcherQueue.TryEnqueue(() =>
            {
                InitializeTrayService();
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

            // 启动托盘进度刷新器（60s 一次）
            _trayProgressRefresher = _serviceProvider!.GetRequiredService<TrayProgressRefresher>();
            _trayProgressRefresher.Start();

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
            // 健康检查定时器回调在线程池执行，重启钩子时必须派发回 UI 线程
            _monitorManager.UIDispatcher = action => Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => action());
            _dataCollectionService = _serviceProvider!.GetRequiredService<DataCollectionService>();
            
            // Win32 低级钩子 (WH_KEYBOARD_LL, WH_MOUSE_LL) 必须安装在有消息循环的线程上。
            // 后台线程池线程没有消息泵，钩子回调不会被调用。
            // 因此必须在 UI 线程上启动监控器，但不得用 .Wait() 阻塞消息泵。
            await _monitorManager.StartAllAsync();
            Log.Information("监控器已启动 - AppMonitor: {AppRunning}, KeyboardMonitor: {KeyboardRunning}, MouseMonitor: {MouseRunning}", 
                _monitorManager.IsRunning, 
                _serviceProvider!.GetRequiredService<Core.Interfaces.IKeyboardMonitor>().IsRunning,
                _serviceProvider!.GetRequiredService<Core.Interfaces.IMouseMonitor>().IsRunning);
            
            _dataCollectionService.Start();
            Log.Information("数据收集服务已启动");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动监控服务失败");
        }
    }

    private void StartBackupService()
    {
        try
        {
            var backupService = _serviceProvider!.GetRequiredService<Core.Interfaces.IBackupService>();
            var settings = _serviceProvider!.GetRequiredService<Core.Interfaces.ISettingsService>();
            
            if (settings.AutoBackupEnabled)
            {
                Log.Information("执行启动时自动备份");
                _ = backupService.CreateBackupAsync();
                
                backupService.StartPeriodicBackupAsync(TimeSpan.FromHours(settings.AutoBackupIntervalHours));
                Log.Information("定时备份服务已启动，间隔: {Hours} 小时", settings.AutoBackupIntervalHours);
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
        }
        catch (Exception ex)
        {
            Log.Error(ex, "停止监控器失败");
        }
        
        try
        {
            Log.Information("步骤 3/5: 释放托盘服务...");
            _trayProgressRefresher?.Dispose();
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
