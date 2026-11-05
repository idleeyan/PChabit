using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.App.Services;
using PChabit.Infrastructure.Monitoring;
using PChabit.Infrastructure.Services;
using PChabit.HardwareMonitor;

namespace PChabit.App;

public partial class App : Microsoft.UI.Xaml.Application
{
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
