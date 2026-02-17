using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PChabit.App.ViewModels;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Monitoring;
using PChabit.Infrastructure.Services;
using PChabit.Infrastructure.Analysis;
using PChabit.Application;

namespace PChabit.App.Services;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services, string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Cache=Shared;Mode=ReadWriteCreate";
        
        services.AddDbContext<PChabitDbContext>(options =>
            options.UseSqlite(connectionString));
        
        services.AddDbContextFactory<PChabitDbContext>(options =>
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
        services.AddScoped<IWebsiteCategoryService, WebsiteCategoryService>();
        
        services.AddSingleton<IBackupService, BackupService>();
        
        services.AddSingleton<IPatternAnalyzer, PatternAnalyzer>();
        services.AddSingleton<IEfficiencyCalculator, EfficiencyCalculator>();
        services.AddSingleton<IInsightService, InsightService>();
        services.AddSingleton<IGoalService, GoalService>();
        services.AddSingleton<INotificationService, NotificationService>();
        
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
        services.AddScoped<WebsiteCategoryManagementViewModel>();
        services.AddTransient<WebsiteCategoryEditDialogViewModel>();
        services.AddTransient<WebsiteCategoryEditDialog>();
        services.AddScoped<HeatmapViewModel>();
        services.AddScoped<SankeyViewModel>();
        services.AddScoped<BackupManagementViewModel>();
        services.AddScoped<InsightsViewModel>();
        services.AddScoped<GoalsViewModel>();
        services.AddScoped<MouseDetailsViewModel>();
        
        return services;
    }
}
