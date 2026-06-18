using Serilog;
using Serilog.Events;

namespace PChabit.Infrastructure.Services;

public static class SerilogConfigurator
{
    public static void Configure(string logPath)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Information,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }

    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
    }
}
