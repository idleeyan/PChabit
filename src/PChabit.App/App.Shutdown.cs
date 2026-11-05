using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Diagnostics;
using PChabit.App.Services;
using PChabit.Infrastructure.Monitoring;
using PChabit.Infrastructure.Services;

namespace PChabit.App;

public partial class App : Microsoft.UI.Xaml.Application
{
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
                catch (Exception ex)
                {
                    // 预期：关机期子服务停止失败已无恢复动作，Debug 可观测即可
                    Log.Debug(ex, "停止进程网络监控失败（关机期）");
                }

                try
                {
                    _serviceProvider?.GetService<PChabit.HardwareMonitor.Hardware.ProcessResourceMonitor>()?.Stop();
                }
                catch (Exception ex)
                {
                    // 同上，关机期无恢复动作
                    Log.Debug(ex, "停止进程资源监控失败（关机期）");
                }

                try
                {
                    _serviceProvider?.GetService<NetworkTrafficPersistenceService>()?.Stop();
                }
                catch (Exception ex)
                {
                    // 同上，关机期无恢复动作
                    Log.Debug(ex, "停止网络流量持久化失败（关机期）");
                }

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
            // 日志已在 Log.CloseAndFlush（先于本方法调用）后关闭，物理上无法记录；
            // Kill 失败仅以 Exit 兜底——本处为异常处理规约登记的豁免点
            Environment.Exit(0);
        }
    }
}
