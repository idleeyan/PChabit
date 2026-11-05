using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.App.Services;

namespace PChabit.App;

public partial class App : Microsoft.UI.Xaml.Application
{
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
}
