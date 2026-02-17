using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Formatters;
using PChabit.Infrastructure.Monitoring;
using PChabit.Infrastructure.Services;

namespace PChabit.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaiInfrastructure(this IServiceCollection services, string dbPath, int webSocketPort = 8765)
    {
        services.AddDbContext<PChabitDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));
        
        services.AddDbContextFactory<PChabitDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));
        
        services.AddSingleton<IEventBus, EventBus>();
        
        services.AddSingleton<WebSocketServer>(sp => new WebSocketServer(webSocketPort));
        services.AddSingleton<AppInfoResolver>();
        services.AddSingleton<AppCategoryResolver>();
        
        services.AddSingleton<IAppMonitor, AppMonitor>();
        services.AddSingleton<IKeyboardMonitor, KeyboardMonitor>();
        services.AddSingleton<IMouseMonitor, MouseMonitor>();
        services.AddSingleton<IWebMonitor, WebMonitor>();
        
        services.AddScoped<IAppSessionRepository, AppSessionRepository>();
        services.AddScoped<IKeyboardSessionRepository, KeyboardSessionRepository>();
        services.AddScoped<IMouseSessionRepository, MouseSessionRepository>();
        services.AddScoped<IWebSessionRepository, WebSessionRepository>();
        services.AddScoped<IDailyPatternRepository, DailyPatternRepository>();
        
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        
        services.AddSingleton<IExportFormatter, JsonExportFormatter>();
        services.AddSingleton<IExportFormatter, MarkdownExportFormatter>();
        services.AddSingleton<IExportFormatter, AiPromptExportFormatter>();
        services.AddSingleton<IExportFormatter, CsvExportFormatter>();
        services.AddSingleton<IExportFormatter, ExcelExportFormatter>();
        services.AddScoped<IExportService, ExportService>();
        
        services.AddSingleton<IBackupService, BackupService>();
        
        services.AddSingleton<MonitorManager>();
        
        return services;
    }
}
