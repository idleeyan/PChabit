using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tai.App.Services;
using Tai.App.ViewModels;
using Tai.App.Views;
using Tai.Infrastructure.Data;
using Tai.Infrastructure.Services;

namespace Tai.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private ServiceProvider? _serviceProvider;
    private MonitorManager? _monitorManager;
    private DataCollectionService? _dataCollectionService;
    private TrayService? _trayService;
    private bool _isExiting;
    private readonly object _exitLock = new();
    
    public static IServiceProvider Services => ((App)Current)._serviceProvider!;
    
    public static T GetService<T>() where T : class
    {
        return ((App)Current)._serviceProvider!.GetRequiredService<T>();
    }
    
    public static TrayService Tray => ((App)Current)._trayService!;
    
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
            "Tai", "Logs", "app-.log");
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        Log.Information("应用程序启动");
    }
    
    private void ConfigureServices()
    {
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tai", "Data", "tai.db");
        
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
        
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaiDbContext>();
            DatabaseInitializer.InitializeAsync(dbContext).Wait();
            Log.Information("数据库初始化完成: {Path}", databasePath);
        }
    }
    
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
            _window.Activate();
            
            Log.Information("窗口已激活");
            
            InitializeTrayService();
            StartMonitoring();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "OnLaunched 失败");
            throw;
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
    
    private void StartMonitoring()
    {
        try
        {
            _monitorManager = _serviceProvider!.GetRequiredService<MonitorManager>();
            _dataCollectionService = _serviceProvider!.GetRequiredService<DataCollectionService>();
            
            _monitorManager.StartAllAsync().Wait();
            Log.Information("监控器已启动 - AppMonitor: {AppRunning}, KeyboardMonitor: {KeyboardRunning}, MouseMonitor: {MouseRunning}", 
                _monitorManager.IsRunning, 
                _serviceProvider.GetRequiredService<Core.Interfaces.IKeyboardMonitor>().IsRunning,
                _serviceProvider.GetRequiredService<Core.Interfaces.IMouseMonitor>().IsRunning);
            
            _dataCollectionService.Start();
            Log.Information("数据收集服务已启动");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动监控服务失败");
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
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _monitorManager.StopAllAsync().WaitAsync(cts.Token);
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
            
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "关闭窗口失败");
        }
        
        try
        {
            Log.Information("步骤 5/5: 释放服务提供者...");
            _serviceProvider?.Dispose();
            Log.Information("服务提供者已释放");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "释放服务提供者失败");
        }
        
        Log.Information("所有清理步骤完成，准备退出");
        Log.CloseAndFlush();
        
        await Task.Delay(100);
        
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
