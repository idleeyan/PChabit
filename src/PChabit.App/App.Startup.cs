using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using PChabit.App.Services;
using PChabit.Infrastructure.Monitoring;
using PChabit.Infrastructure.Services;
using PChabit.HardwareMonitor;

namespace PChabit.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    /// <summary>
    /// P5-4：采集交给 Rust 宿主时，本进程只做界面。
    /// 开关来源：环境变量 `PCHABIT_COLLECT=shell`（调试用）优先，其次 `settings.json` 的 `CollectInShell`。
    /// 硬件内存采样必须留着——托盘与桌面小部件读的是它，它只采样不落库。
    /// </summary>
    private static bool CollectInShell
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("PCHABIT_COLLECT");
            if (!string.IsNullOrWhiteSpace(env))
            {
                return env.Equals("shell", StringComparison.OrdinalIgnoreCase);
            }
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PChabit",
                    "settings.json");
                if (!File.Exists(path)) return false;
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.TryGetProperty("CollectInShell", out var v)
                       && v.ValueKind == System.Text.Json.JsonValueKind.True;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "读取 CollectInShell 开关失败，按默认（WPF 采集）处理");
                return false;
            }
        }
    }

    private const string CollectHeartbeatName = ".wpf-collecting";

    /// <summary>
    /// 写入/刷新「WPF 正在采集」心跳。Rust 宿主凭这个文件判断谁是写者：
    /// 没有心跳 = WPF 只读模式，宿主可以启动。
    /// </summary>
    private static void WriteCollectHeartbeat()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PChabit",
                "Data");
            Directory.CreateDirectory(dir);
            var ms = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();
            File.WriteAllText(Path.Combine(dir, CollectHeartbeatName), ms);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "写采集心跳失败");
        }
    }

    private static void ClearCollectHeartbeat()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PChabit",
                "Data",
                CollectHeartbeatName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 删不掉不影响功能：宿主 5 分钟后会认为心跳作废
        }
    }

    private async Task StartMonitoringAsync()
    {
        try
        {
            if (CollectInShell)
            {
                StartUiOnlyMode();
                await Task.CompletedTask;
                return;
            }

            WriteCollectHeartbeat();
            _collectHeartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            _collectHeartbeatTimer.Tick += (_, _) => WriteCollectHeartbeat();
            _collectHeartbeatTimer.Start();

            _monitorManager = _serviceProvider!.GetRequiredService<MonitorManager>();

            // 低级输入钩子安装在专用消息泵线程上，避免 UI 线程布局/渲染阻塞导致
            // 全系统鼠标停顿。钩子重启也派发到该线程。
            var hookThread = _serviceProvider!.GetRequiredService<InputHookThread>();
            hookThread.Start();
            _monitorManager.HookThread = hookThread;
            // 兼容旧路径：钩子线程不可用时才回退 UI 调度器
            _monitorManager.UIDispatcher = action => Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => action());

            _dataCollectionService = _serviceProvider!.GetRequiredService<Core.Interfaces.IDataCollectionService>();

            // 书签/历史同步消息处理器：必须在 WebSocketServer 启动前完成订阅
            _serviceProvider!.GetRequiredService<BrowserSyncWebSocketHandler>();

            await _monitorManager.StartAllAsync();
            Log.Information("监控器已启动 - AppMonitor: {AppRunning}, KeyboardMonitor: {KeyboardRunning}, MouseMonitor: {MouseRunning}, HookThread: {HookThread}",
                _monitorManager.IsRunning,
                _serviceProvider!.GetRequiredService<Core.Interfaces.IKeyboardMonitor>().IsRunning,
                _serviceProvider!.GetRequiredService<Core.Interfaces.IMouseMonitor>().IsRunning,
                hookThread.IsRunning);

            // 任务栏小窗点击（3.86.0）：小窗是 Shell_TrayWnd 子窗口，任务栏 XAML 层压在它上面，
            // 真实点击/光标消息到不了它（实测 WM_SETCURSOR、WM_LBUTTONUP 均不触发）。
            // 因此借已有的全局鼠标钩子拦「画了文字的区域」：命中即弹进程网速浮窗，并吞掉该次点击，
            // 空白像素不拦，点击照常落到任务栏控件上。
            try
            {
                var mouseMonitor = _serviceProvider!.GetRequiredService<Core.Interfaces.IMouseMonitor>();
                mouseMonitor.ClickInterceptor = (x, y) =>
                    _taskbarWidget != null
                    && _taskbarWidget.HitTestScreenPoint(x, y)
                    && OnTaskbarWidgetClicked();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "注册任务栏小窗点击拦截失败");
            }

            _dataCollectionService.Start();
            Log.Information("数据收集服务已启动");

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

    /// <summary>
    /// P5-4 只读模式：采集交给 Rust 宿主（`PChabitShell.exe --host`），本进程只保留
    /// 界面、托盘、桌面小部件需要的内存采样与维护任务。**不写任何采集表**。
    /// </summary>
    private void StartUiOnlyMode()
    {
        try
        {
            // 只读模式没有心跳 → Rust 宿主看到「PChabit 在跑但没心跳」会放行
            ClearCollectHeartbeat();
            Log.Information("采集已交给 Rust 宿主（PCHABIT_COLLECT=shell）：本进程只做界面与内存采样");

            // 任务栏小窗点击拦截是纯 UI 交互（吞掉命中区域的点击再弹网速浮窗），不算采集：
            // 只装一个鼠标低级钩子，复用与正常模式同一条输入钩子线程，键鼠统计仍归 Rust 宿主。
            try
            {
                var hookThread = _serviceProvider!.GetRequiredService<InputHookThread>();
                hookThread.Start();
                _uiOnlyHookThread = hookThread;

                var mouseMonitor = _serviceProvider!.GetRequiredService<Core.Interfaces.IMouseMonitor>();
                mouseMonitor.ClickInterceptor = (x, y) =>
                    _taskbarWidget != null
                    && _taskbarWidget.HitTestScreenPoint(x, y)
                    && OnTaskbarWidgetClicked();
                _uiOnlyMouseMonitor = mouseMonitor;
                hookThread.Post(() => mouseMonitor.Start());
                Log.Information("任务栏小窗点击拦截已启用（只读模式专用鼠标钩子，不采集键鼠）");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "只读模式下注册任务栏小窗点击拦截失败");
            }

            _hardwareMonitorService = _serviceProvider!.GetRequiredService<HardwareMonitorService>();
            _hardwareMonitorService.Start();
            Log.Information("硬件内存采样已启动（托盘与桌面小部件数据源，不落库）");

            var processNetwork = _serviceProvider!.GetRequiredService<PChabit.HardwareMonitor.Hardware.ProcessNetworkMonitor>();
            processNetwork.NetUpGetter = () => _hardwareMonitorService?.Get("NET.Up");
            processNetwork.NetDownGetter = () => _hardwareMonitorService?.Get("NET.Down");
            processNetwork.Start();
            Log.Information("进程网络流量监视已启动（内存）");

            StartTrayDisplayTimer();
            StartDesktopWidgetTimer();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "只读模式启动失败");
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
}
