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
using PChabit.HardwareMonitor;

namespace PChabit.App.Services;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services, string databasePath)
    {
        // 注意：Microsoft.Data.Sqlite 连接串不支持 "Journal Mode" 关键字
        // （写入会抛 ArgumentException），WAL 仅通过 EnableWalModeAsync 的 PRAGMA 启用。
        var connectionString =
            $"Data Source={databasePath};Cache=Private;Mode=ReadWriteCreate;Default Timeout=5;Pooling=True;";

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

        services.AddSingleton<InputHookThread>();
        services.AddSingleton<IAppMonitor, AppMonitor>();
        services.AddSingleton<IKeyboardMonitor, KeyboardMonitor>();
        services.AddSingleton<IMouseMonitor, MouseMonitor>();
        services.AddSingleton<IWebMonitor, WebMonitor>();
        services.AddSingleton(sp =>
        {
            var port = 8765;
            try
            {
                var settings = sp.GetService<ISettingsService>();
                if (settings != null && int.TryParse(settings.WebSocketPort, out var configured) &&
                    configured > 0 && configured < 65536)
                {
                    port = configured;
                }
            }
            catch { /* settings 可能尚未就绪，退回默认端口 */ }

            return new WebSocketServer(port);
        });
        services.AddSingleton<MonitorManager>();
        
        // 硬件监控（LiteMonitor 核心移植，见 src/PChabit.HardwareMonitor/NOTICE.md）
        services.AddSingleton<HardwareMonitorService>();
        // 进程网络流量（IP Helper 连接表 + ESTATS，硬件页「最大占用进程/流量统计」）
        services.AddSingleton<PChabit.HardwareMonitor.Hardware.ProcessNetworkMonitor>();
        // 进程 CPU/内存/磁盘/GPU 占用（硬件卡片「最大占用进程」）
        services.AddSingleton<PChabit.HardwareMonitor.Hardware.ProcessResourceMonitor>();
        // 网络流量历史落库 + 独立统计页
        services.AddSingleton<NetworkTrafficPersistenceService>();
        // 硬件分钟样本落库（分析升级 P0，订阅 HardwareMonitorService.ValuesUpdated）
        services.AddSingleton<HardwareSampleWriter>();

        services.AddSingleton<DataCollectionService>();
        services.AddSingleton<IAppIconService, AppIconService>();
        services.AddSingleton<IBackgroundAppSettings, BackgroundAppSettings>();
        services.AddSingleton<ISettingsService, SettingsService>();
        
        services.AddSingleton<WebDAVSyncService>();
        services.AddSingleton<IWebDAVSyncService>(sp => sp.GetRequiredService<WebDAVSyncService>());
        
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IWebsiteCategoryService, WebsiteCategoryService>();
        
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<BrowserSyncWebSocketHandler>();
        services.AddSingleton<IHistoryIngestService, HistoryIngestService>();
        services.AddSingleton<HistorySyncService>();

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
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddMemoryCache();
        
        // 所有 ViewModel 使用 Transient，因为通过 App.GetService 从根容器解析
        // AddScoped 从根容器解析会导致俘定依赖和死锁
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<HardwareMonitorViewModel>();
        services.AddTransient<NetworkTrafficViewModel>();
        services.AddTransient<TimelineViewModel>();
        services.AddTransient<AnalyticsViewModel>();
        services.AddSingleton<IAiChatService, AiChatService>();
        services.AddSingleton<AiSettingsSyncService>();
        services.AddSingleton<IAnalyticsAiService, AnalyticsAiService>();
        services.AddSingleton<IAiInsightHistoryService, AiInsightHistoryService>();
        services.AddSingleton<IWeeklyAiInsightService, WeeklyAiInsightService>();
        services.AddTransient<DataManagementViewModel>();
        services.AddTransient<DetailDialogViewModel>();
        services.AddTransient<AppStatsViewModel>();
        services.AddTransient<AppDetailViewModel>();
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
        services.AddTransient<HistoryReportViewModel>();
        
        return services;
    }
    
    public static async Task EnableWalModeAsync(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Cache=Private;";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA cache_size=-20000; PRAGMA temp_store=MEMORY; PRAGMA wal_autocheckpoint=10000;";
        await command.ExecuteNonQueryAsync();
        
        Log.Information("SQLite WAL 模式已启用: {Path}", databasePath);
    }
}

