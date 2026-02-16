using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tai.App.ViewModels;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;
using Tai.Infrastructure.Monitoring;
using Tai.Infrastructure.Services;
using Tai.Application;

namespace Tai.App.Services;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services, string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Cache=Shared;Mode=ReadWriteCreate";
        
        services.AddDbContext<TaiDbContext>(options =>
            options.UseSqlite(connectionString));
        
        services.AddDbContextFactory<TaiDbContext>(options =>
            options.UseSqlite(connectionString));
        
        services.AddTaiApplication();
        
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        
        services.AddScoped<IAppSessionRepository, AppSessionRepository>();
        services.AddScoped<IKeyboardSessionRepository, KeyboardSessionRepository>();
        services.AddScoped<IMouseSessionRepository, MouseSessionRepository>();
        services.AddScoped<IWebSessionRepository, WebSessionRepository>();
        services.AddScoped<IDailyPatternRepository, DailyPatternRepository>();
        
        services.AddSingleton<IAppMonitor, AppMonitor>();
        services.AddSingleton<IKeyboardMonitor, KeyboardMonitor>();
        services.AddSingleton<IMouseMonitor, MouseMonitor>();
        services.AddSingleton<IWebMonitor, WebMonitor>();
        services.AddSingleton<WebSocketServer>();
        services.AddSingleton<MonitorManager>();
        
        services.AddSingleton<DataCollectionService>();
        services.AddSingleton<IAppIconService, AppIconService>();
        services.AddSingleton<IBackgroundAppSettings, BackgroundAppSettings>();
        services.AddSingleton<ISettingsService, SettingsService>();
        
        services.AddSingleton<WebDAVSyncService>();
        services.AddSingleton<IWebDAVSyncService>(sp => sp.GetRequiredService<WebDAVSyncService>());
        
        services.AddScoped<ICategoryService, CategoryService>();
        
        services.AddScoped<SettingsViewModel>();
        
        services.AddScoped<DashboardViewModel>();
        services.AddScoped<TimelineViewModel>();
        services.AddScoped<AnalyticsViewModel>();
        services.AddScoped<ExportViewModel>();
        services.AddScoped<ExportPageViewModel>();
        services.AddScoped<DetailDialogViewModel>();
        services.AddScoped<AppStatsViewModel>();
        services.AddScoped<KeyboardDetailsViewModel>();
        services.AddScoped<WebDetailsViewModel>();
        services.AddScoped<CategoryEditDialogViewModel>();
        services.AddScoped<CategoryDetailDialogViewModel>();
        services.AddScoped<CategoryManagementViewModel>();
        services.AddScoped<HeatmapViewModel>();
        services.AddScoped<SankeyViewModel>();
        
        return services;
    }
}
