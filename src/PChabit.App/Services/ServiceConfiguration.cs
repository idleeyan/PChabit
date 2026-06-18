using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Formatters;
using PChabit.Infrastructure.Monitoring;
using PChabit.Infrastructure.Services;
using PChabit.Infrastructure.Analysis;
using PChabit.Application;

namespace PChabit.App.Services;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services, string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Cache=Shared;Mode=ReadWriteCreate;";
        
        services.AddDbContext<PChabitDbContext>(options =>
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(15);
            }));
        
        services.AddDbContextFactory<PChabitDbContext>(options =>
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(15);
            }));
        
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

        // 数据导出服务（原在 AddTaiInfrastructure 中但该方法未被调用）
        services.AddSingleton<IExportFormatter, JsonExportFormatter>();
        services.AddSingleton<IExportFormatter, MarkdownExportFormatter>();
        services.AddSingleton<IExportFormatter, AiPromptExportFormatter>();
        services.AddSingleton<IExportFormatter, CsvExportFormatter>();
        services.AddSingleton<IExportFormatter, ExcelExportFormatter>();
        services.AddScoped<IExportService, ExportService>();

        services.AddSingleton<IPatternAnalyzer, PatternAnalyzer>();
        services.AddSingleton<IEfficiencyCalculator, EfficiencyCalculator>();
        services.AddSingleton<IInsightService, InsightService>();
        services.AddSingleton<IGoalService, GoalService>();
        services.AddSingleton<INotificationService, NotificationService>();

        // 托盘进度刷新器（TrayService 的依赖）
        services.AddSingleton<TrayProgressRefresher>();
        
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddMemoryCache();
        
        // 所有 ViewModel 使用 Transient，因为通过 App.GetService 从根容器解析
        // AddScoped 从根容器解析会导致俘定依赖和死锁
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<TimelineViewModel>();
        services.AddTransient<AnalyticsViewModel>();
        services.AddTransient<DataManagementViewModel>();
        services.AddTransient<DetailDialogViewModel>();
        services.AddTransient<AppStatsViewModel>();
        services.AddTransient<KeyboardDetailsViewModel>();
        services.AddTransient<WebDetailsViewModel>();
        services.AddTransient<CategoryEditDialogViewModel>();
        services.AddTransient<CategoryEditDialog>();
        services.AddTransient<CategoryDetailDialogViewModel>();
        services.AddTransient<CategoryManagementViewModel>();
        services.AddTransient<WebsiteCategoryManagementViewModel>();
        services.AddTransient<WebsiteCategoryEditDialogViewModel>();
        services.AddTransient<WebsiteCategoryEditDialog>();
        services.AddTransient<HeatmapViewModel>();
        services.AddTransient<SankeyViewModel>();
        services.AddTransient<InsightsViewModel>();
        services.AddTransient<GoalsViewModel>();
        
        return services;
    }
    
    public static async Task EnableWalModeAsync(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Cache=Shared;";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA cache_size=-20000; PRAGMA temp_store=MEMORY; PRAGMA wal_autocheckpoint=10000;";
        await command.ExecuteNonQueryAsync();
        
        Log.Information("SQLite WAL 模式已启用: {Path}", databasePath);
    }
}
