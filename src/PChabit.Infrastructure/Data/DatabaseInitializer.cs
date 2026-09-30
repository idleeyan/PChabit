using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;

namespace PChabit.Infrastructure.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(PChabitDbContext context)
    {
        Log.Information("数据库初始化开始");
        var created = await context.Database.EnsureCreatedAsync();
        Log.Information("数据库创建状态: {Created}", created);

        await MigrateSchemaAsync(context);

        await SeedDefaultDataAsync(context);
        Log.Information("数据库初始化全部完成");
    }
    
    private static async Task MigrateSchemaAsync(PChabitDbContext context)
    {
        try
        {
            // 使用独立连接，避免与 DataCollectionService 的 DbContext 写事务互锁
            var cs = context.Database.GetConnectionString();
            if (string.IsNullOrEmpty(cs))
            {
                Log.Warning("无法获取连接串，跳过架构迁移");
                return;
            }

            await using var connection = new SqliteConnection(cs);
            await connection.OpenAsync();
            // 锁等待最多 3 秒，避免迁移被其它写连接无限阻塞
            using (var busy = connection.CreateCommand())
            {
                busy.CommandText = "PRAGMA busy_timeout = 3000;";
                await busy.ExecuteNonQueryAsync();
            }
            Log.Information("迁移独立连接已打开");

            // 书签/历史表优先创建（短连接、独立 try）
            try
            {
                await MigrateBrowserBookmarkTablesAsync(connection);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "创建书签同步表失败");
            }

            // DailySummary 实际表名可能是 DailySummaries；表不存在时跳过
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "WebPages", "INTEGER NOT NULL DEFAULT 0");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "WebDurationTicks", "INTEGER NOT NULL DEFAULT 0");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "WebActiveDurationTicks", "INTEGER NOT NULL DEFAULT 0");

            // 分析升级 P0：DailySummaries 扩展列（均可空；脚本可重复执行）
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "AppSwitches", "INTEGER");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "FocusMinutesV2", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "FocusCountV2", "INTEGER");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "FocusQualityAvg", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "WebMinutes", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "CpuLoadAvg", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "CpuLoadP95", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "GpuLoadAvg", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "GpuTempMax", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "MemLoadAvg", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "NetBytesUp", "INTEGER");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "NetBytesDown", "INTEGER");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "MetricsVersion", "INTEGER");
            // 行为语义层 P0：活动标签 / 夜间 / 起止（均可空，脚本可重复执行）
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "LabelMinutesJson", "TEXT");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "NightMinutes", "REAL");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "FirstActiveTime", "TEXT");
            await TryMigrateTableColumnsAsync(connection, "DailySummaries", "LastActiveTime", "TEXT");

            await MigrateTableColumnsAsync(connection, "AppSessions", "Duration", "INTEGER NOT NULL DEFAULT 0");
            await MigrateTableColumnsAsync(connection, "AppSessions", "ActiveDuration", "INTEGER NOT NULL DEFAULT 0");
            await MigrateTableColumnsAsync(connection, "WebSessions", "Duration", "INTEGER NOT NULL DEFAULT 0");
            await MigrateTableColumnsAsync(connection, "WebSessions", "ActiveDuration", "INTEGER NOT NULL DEFAULT 0");
            await MigrateTableColumnsAsync(connection, "WebSessions", "IdleDuration", "INTEGER NOT NULL DEFAULT 0");
            await MigrateTableColumnsAsync(connection, "WebSessions", "CategoryId", "INTEGER");
            await MigrateTableColumnsAsync(connection, "WebSessions", "CategoryName", "TEXT");
            await MigrateTableColumnsAsync(connection, "WebSessions", "CategorySource", "TEXT");
            await MigrateTableColumnsAsync(connection, "WebSessions", "IsLegacy", "INTEGER NOT NULL DEFAULT 0");

            await MigrateTableColumnsAsync(connection, "KeyboardSessions", "KeyFrequency", "TEXT");
            await MigrateTableColumnsAsync(connection, "KeyboardSessions", "KeyCategoryFrequency", "TEXT");
            await MigrateTableColumnsAsync(connection, "KeyboardSessions", "Shortcuts", "TEXT");
            await MigrateTableColumnsAsync(connection, "KeyboardSessions", "TypingBursts", "TEXT");

            // 跳过全表 UPDATE 修复（历史数据已处理过；与数据收集写锁竞争会导致启动假死）

            await SafeMigrateAsync(connection, MigrateProgramCategoryTablesAsync, "ProgramCategory");
            await SafeMigrateAsync(connection, MigrateWebsiteCategoryTablesAsync, "WebsiteCategory");
            await SafeMigrateAsync(connection, MigrateGuidTablesAsync, "GuidTables");
            await SafeMigrateAsync(connection, MigrateBackupTablesAsync, "BackupTables");
            await SafeMigrateAsync(connection, MigrateAnalysisTablesAsync, "AnalysisTables");

            await connection.CloseAsync();
            Log.Information("数据库架构迁移完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据库架构迁移失败");
            throw;
        }
    }

    private static async Task SafeMigrateAsync(System.Data.Common.DbConnection connection, Func<System.Data.Common.DbConnection, Task> action, string name)
    {
        try
        {
            Log.Information("迁移步骤开始: {Name}", name);
            await action(connection);
            Log.Information("迁移步骤完成: {Name}", name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "迁移步骤失败（继续）: {Name}", name);
        }
    }

    private static async Task TryExecuteSqlAsync(System.Data.Common.DbConnection connection, string sql)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SQL 修复语句跳过");
        }
    }
    
    private static async Task MigrateProgramCategoryTablesAsync(System.Data.Common.DbConnection connection)
    {
        var tables = new HashSet<string>();
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }
        
        if (!tables.Contains("ProgramCategories"))
        {
            Log.Information("创建 ProgramCategories 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE ProgramCategories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT,
                    Color TEXT NOT NULL DEFAULT '#4A90E4',
                    Icon TEXT NOT NULL DEFAULT '📁',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsSystem INTEGER NOT NULL DEFAULT 0,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    UpdatedAt TEXT
                );
                CREATE UNIQUE INDEX IX_ProgramCategories_Name ON ProgramCategories (Name);
                CREATE INDEX IX_ProgramCategories_SortOrder ON ProgramCategories (SortOrder);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("ProgramCategories 表创建成功");
        }
        
        if (!tables.Contains("ProgramCategoryMappings"))
        {
            Log.Information("创建 ProgramCategoryMappings 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE ProgramCategoryMappings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProcessName TEXT NOT NULL,
                    ProcessPath TEXT,
                    ProcessAlias TEXT,
                    CategoryId INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    UpdatedAt TEXT,
                    FOREIGN KEY (CategoryId) REFERENCES ProgramCategories (Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_ProgramCategoryMappings_ProcessName ON ProgramCategoryMappings (ProcessName);
                CREATE UNIQUE INDEX IX_ProgramCategoryMappings_ProcessName_CategoryId ON ProgramCategoryMappings (ProcessName, CategoryId);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("ProgramCategoryMappings 表创建成功");
        }
    }
    
    private static async Task MigrateWebsiteCategoryTablesAsync(System.Data.Common.DbConnection connection)
    {
        var tables = new HashSet<string>();
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }
        
        if (!tables.Contains("WebsiteCategories"))
        {
            Log.Information("创建 WebsiteCategories 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE WebsiteCategories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT,
                    Color TEXT NOT NULL DEFAULT '#4A90E4',
                    Icon TEXT NOT NULL DEFAULT '🌐',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsSystem INTEGER NOT NULL DEFAULT 0,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    UpdatedAt TEXT
                );
                CREATE UNIQUE INDEX IX_WebsiteCategories_Name ON WebsiteCategories (Name);
                CREATE INDEX IX_WebsiteCategories_SortOrder ON WebsiteCategories (SortOrder);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("WebsiteCategories 表创建成功");
        }
        
        if (!tables.Contains("WebsiteDomainMappings"))
        {
            Log.Information("创建 WebsiteDomainMappings 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE WebsiteDomainMappings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DomainPattern TEXT NOT NULL,
                    CategoryId INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    UpdatedAt TEXT,
                    FOREIGN KEY (CategoryId) REFERENCES WebsiteCategories (Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_WebsiteDomainMappings_DomainPattern ON WebsiteDomainMappings (DomainPattern);
                CREATE UNIQUE INDEX IX_WebsiteDomainMappings_DomainPattern_CategoryId ON WebsiteDomainMappings (DomainPattern, CategoryId);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("WebsiteDomainMappings 表创建成功");
        }
    }
    
    private static async Task MigrateTableColumnsAsync(System.Data.Common.DbConnection connection, string tableName, string columnName, string columnDefinition)
    {
        var columns = new HashSet<string>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains(columnName))
        {
            Log.Information("添加列 {TableName}.{ColumnName}", tableName, columnName);
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
            await alterCmd.ExecuteNonQueryAsync();
            Log.Information("列 {TableName}.{ColumnName} 添加成功", tableName, columnName);
        }
    }

    /// <summary>表不存在时静默跳过，不中断整体迁移。</summary>
    private static async Task TryMigrateTableColumnsAsync(System.Data.Common.DbConnection connection, string tableName, string columnName, string columnDefinition)
    {
        try
        {
            // 先确认表存在，避免对不存在的表发 ALTER（可能在锁等待上耗尽超时）
            var exists = false;
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1";
                var p = check.CreateParameter();
                p.ParameterName = "@n";
                p.Value = tableName;
                check.Parameters.Add(p);
                exists = await check.ExecuteScalarAsync() != null;
            }
            if (!exists) return;

            await MigrateTableColumnsAsync(connection, tableName, columnName, columnDefinition);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "跳过列迁移 {TableName}.{ColumnName}", tableName, columnName);
        }
    }

    private static async Task MigrateBackupTablesAsync(System.Data.Common.DbConnection connection)
    {
        var tables = new HashSet<string>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (!tables.Contains("BackupRecords"))
        {
            Log.Information("创建 BackupRecords 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE BackupRecords (
                    Id TEXT PRIMARY KEY,
                    FilePath TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    RecordCount INTEGER,
                    IsAutomatic INTEGER NOT NULL DEFAULT 0,
                    Description TEXT
                );
                CREATE INDEX IX_BackupRecords_CreatedAt ON BackupRecords (CreatedAt);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("BackupRecords 表创建成功");
        }

        if (!tables.Contains("ArchiveRecords"))
        {
            Log.Information("创建 ArchiveRecords 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE ArchiveRecords (
                    Id TEXT PRIMARY KEY,
                    FilePath TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    DateRangeStart TEXT NOT NULL,
                    DateRangeEnd TEXT NOT NULL,
                    RecordCount INTEGER,
                    Description TEXT
                );
                CREATE INDEX IX_ArchiveRecords_CreatedAt ON ArchiveRecords (CreatedAt);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("ArchiveRecords 表创建成功");
        }
    }

    private static async Task MigrateAnalysisTablesAsync(System.Data.Common.DbConnection connection)
    {
        var tables = new HashSet<string>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }


        if (!tables.Contains("EfficiencyScores"))
        {
            Log.Information("创建 EfficiencyScores 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE EfficiencyScores (
                    Id TEXT PRIMARY KEY,
                    Date TEXT NOT NULL,
                    Score REAL NOT NULL,
                    FocusTimeMinutes INTEGER NOT NULL,
                    DeepWorkMinutes INTEGER NOT NULL,
                    InterruptionCount INTEGER NOT NULL,
                    ProductivityRatio REAL NOT NULL,
                    BreakRatio REAL NOT NULL,
                    Details TEXT,
                    CreatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_EfficiencyScores_Date ON EfficiencyScores (Date);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("EfficiencyScores 表创建成功");
        }

        if (!tables.Contains("WorkPatterns"))
        {
            Log.Information("创建 WorkPatterns 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE WorkPatterns (
                    Id TEXT PRIMARY KEY,
                    Date TEXT NOT NULL,
                    WorkStartTime TEXT,
                    WorkEndTime TEXT,
                    PeakHours TEXT,
                    FocusBlocks TEXT,
                    BreakCount INTEGER NOT NULL,
                    TotalBreakMinutes INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_WorkPatterns_Date ON WorkPatterns (Date);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("WorkPatterns 表创建成功");
        }

        if (!tables.Contains("InsightReports"))
        {
            Log.Information("创建 InsightReports 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE InsightReports (
                    Id TEXT PRIMARY KEY,
                    ReportType TEXT NOT NULL,
                    StartDate TEXT NOT NULL,
                    EndDate TEXT NOT NULL,
                    Summary TEXT,
                    Insights TEXT,
                    Recommendations TEXT,
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IX_InsightReports_ReportType ON InsightReports (ReportType);
                CREATE INDEX IX_InsightReports_StartDate ON InsightReports (StartDate);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("InsightReports 表创建成功");
        }

        if (!tables.Contains("AiInsightSnapshots"))
        {
            Log.Information("创建 AiInsightSnapshots 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE AiInsightSnapshots (
                    Id TEXT PRIMARY KEY,
                    PeriodKey TEXT NOT NULL,
                    PeriodLabel TEXT NOT NULL DEFAULT '',
                    PeriodStart TEXT NOT NULL,
                    PeriodEnd TEXT NOT NULL,
                    Summary TEXT,
                    FindingsJson TEXT,
                    PlanJson TEXT,
                    DiagnosisJson TEXT,
                    RisksJson TEXT,
                    Model TEXT,
                    PromptVersion TEXT NOT NULL DEFAULT 'v2',
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IX_AiInsightSnapshots_PeriodKey ON AiInsightSnapshots (PeriodKey);
                CREATE INDEX IX_AiInsightSnapshots_CreatedAt ON AiInsightSnapshots (CreatedAt);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("AiInsightSnapshots 表创建成功");
        }

        if (!tables.Contains("DailySummaries"))
        {
            Log.Information("创建 DailySummaries 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE DailySummaries (
                    Id TEXT PRIMARY KEY,
                    Date TEXT NOT NULL,
                    TotalKeys INTEGER NOT NULL DEFAULT 0,
                    TotalMouseClicks INTEGER NOT NULL DEFAULT 0,
                    ActiveMinutes REAL NOT NULL DEFAULT 0,
                    TopApps TEXT NOT NULL DEFAULT '[]',
                    HourlyKeyDistribution TEXT NOT NULL DEFAULT '[]',
                    LastUpdated TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_DailySummaries_Date ON DailySummaries (Date);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("DailySummaries 表创建成功");
        }

        if (!tables.Contains("AppDailyStats"))
        {
            Log.Information("创建 AppDailyStats 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE AppDailyStats (
                    Date TEXT NOT NULL,
                    ProcessName TEXT NOT NULL,
                    Minutes REAL NOT NULL DEFAULT 0,
                    Sessions INTEGER NOT NULL DEFAULT 0,
                    FocusMinutes REAL NOT NULL DEFAULT 0,
                    HourlyJson TEXT NOT NULL DEFAULT '[]',
                    LastUpdated TEXT NOT NULL,
                    PRIMARY KEY (Date, ProcessName)
                );
                CREATE INDEX IX_AppDailyStats_Date ON AppDailyStats (Date);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("AppDailyStats 表创建成功");
        }

        // 分析升级 P0：硬件分钟样本表（列必须与 PChabitDbContext 模型产物一致，陷阱 12：双路径同改）
        if (!tables.Contains("HardwareSamples"))
        {
            Log.Information("创建 HardwareSamples 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE HardwareSamples (
                    Id TEXT NOT NULL CONSTRAINT PK_HardwareSamples PRIMARY KEY,
                    Timestamp TEXT NOT NULL,
                    CpuLoadAvg REAL,
                    CpuLoadMax REAL,
                    CpuTempMax REAL,
                    GpuLoadAvg REAL,
                    GpuLoadMax REAL,
                    GpuTempMax REAL,
                    VramUsedMax REAL,
                    VramTotal REAL,
                    MemLoadAvg REAL,
                    MemLoadMax REAL,
                    MemUsedMax REAL,
                    DiskActivityAvg REAL,
                    DiskReadMax REAL,
                    DiskWriteMax REAL,
                    NetUpAvg REAL,
                    NetDownAvg REAL,
                    SampleCount INTEGER NOT NULL,
                    SensorFlags INTEGER NOT NULL DEFAULT 0
                );
                CREATE UNIQUE INDEX IX_HardwareSamples_Timestamp ON HardwareSamples (Timestamp);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("HardwareSamples 表创建成功");
        }

        // 网络流量统计：系统分钟样本 + 进程日/小时聚合
        if (!tables.Contains("NetworkTrafficSamples"))
        {
            Log.Information("创建 NetworkTrafficSamples 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE NetworkTrafficSamples (
                    Id TEXT NOT NULL CONSTRAINT PK_NetworkTrafficSamples PRIMARY KEY,
                    Timestamp TEXT NOT NULL,
                    BytesUp INTEGER NOT NULL DEFAULT 0,
                    BytesDown INTEGER NOT NULL DEFAULT 0,
                    PeakUpBps REAL NOT NULL DEFAULT 0,
                    PeakDownBps REAL NOT NULL DEFAULT 0,
                    SampleCount INTEGER NOT NULL DEFAULT 0,
                    LastUpdated TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_NetworkTrafficSamples_Timestamp ON NetworkTrafficSamples (Timestamp);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("NetworkTrafficSamples 表创建成功");
        }

        if (!tables.Contains("ProcessNetworkUsages"))
        {
            Log.Information("创建 ProcessNetworkUsages 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE ProcessNetworkUsages (
                    Date TEXT NOT NULL,
                    Hour INTEGER NOT NULL,
                    ProcessName TEXT NOT NULL,
                    BytesUp INTEGER NOT NULL DEFAULT 0,
                    BytesDown INTEGER NOT NULL DEFAULT 0,
                    PeakBytesPerSec INTEGER NOT NULL DEFAULT 0,
                    LastUpdated TEXT NOT NULL,
                    CONSTRAINT PK_ProcessNetworkUsages PRIMARY KEY (Date, Hour, ProcessName)
                );
                CREATE INDEX IX_ProcessNetworkUsages_Date ON ProcessNetworkUsages (Date);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("ProcessNetworkUsages 表创建成功");
        }
    }

    private static async Task MigrateBrowserBookmarkTablesAsync(System.Data.Common.DbConnection connection)
    {
        var tables = new HashSet<string>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (!tables.Contains("BrowserBookmarks"))
        {
            Log.Information("创建 BrowserBookmarks 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE BrowserBookmarks (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Type TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Url TEXT,
                    PathJson TEXT NOT NULL DEFAULT '[]',
                    DateAdded INTEGER NOT NULL DEFAULT 0,
                    DateModified INTEGER NOT NULL DEFAULT 0,
                    SourceBrowser TEXT NOT NULL DEFAULT '',
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IX_BrowserBookmarks_Url ON BrowserBookmarks (Url);
                CREATE INDEX IX_BrowserBookmarks_IsDeleted ON BrowserBookmarks (IsDeleted);
                CREATE INDEX IX_BrowserBookmarks_Type_Title ON BrowserBookmarks (Type, Title);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("BrowserBookmarks 表创建成功");
        }

        if (!tables.Contains("BookmarkSyncBaselines"))
        {
            Log.Information("创建 BookmarkSyncBaselines 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE BookmarkSyncBaselines (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    SavedAt INTEGER NOT NULL DEFAULT 0,
                    ItemsJson TEXT NOT NULL DEFAULT '[]'
                );";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("BookmarkSyncBaselines 表创建成功");
        }

        if (!tables.Contains("BrowserSyncMetas"))
        {
            Log.Information("创建 BrowserSyncMetas 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE BrowserSyncMetas (
                    Key TEXT NOT NULL PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("BrowserSyncMetas 表创建成功");
        }

        if (!tables.Contains("BrowserHistoryItems"))
        {
            Log.Information("创建 BrowserHistoryItems 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE BrowserHistoryItems (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Url TEXT NOT NULL,
                    Title TEXT NOT NULL DEFAULT '',
                    VisitTime INTEGER NOT NULL DEFAULT 0,
                    VisitCount INTEGER NOT NULL DEFAULT 0,
                    SourceBrowser TEXT NOT NULL DEFAULT '',
                    IngestedAt INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IX_BrowserHistoryItems_VisitTime ON BrowserHistoryItems (VisitTime);
                CREATE INDEX IX_BrowserHistoryItems_Url ON BrowserHistoryItems (Url);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("BrowserHistoryItems 表创建成功");
        }
    }

    private static async Task MigrateGuidTablesAsync(System.Data.Common.DbConnection connection)
    {
        var guidTables = new[] { "EfficiencyScores", "WorkPatterns", "InsightReports", "BackupRecords", "ArchiveRecords" };
        
        foreach (var tableName in guidTables)
        {
            var idType = await GetColumnTypeInfoAsync(connection, tableName, "Id");
            if (idType != null && idType.Contains("INTEGER", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("迁移表 {TableName} 的 Id 列从 INTEGER 到 TEXT", tableName);
                
                using var dropCmd = connection.CreateCommand();
                dropCmd.CommandText = $"DROP TABLE IF EXISTS {tableName}";
                await dropCmd.ExecuteNonQueryAsync();
                
                Log.Information("表 {TableName} 已删除，将在下次访问时重新创建", tableName);
            }
        }
    }
    
    private static async Task<string?> GetColumnTypeInfoAsync(System.Data.Common.DbConnection connection, string tableName, string columnName)
    {
        try
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }
            
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return reader.GetString(2);
                }
            }
        }
        catch
        {
        }
        return null;
    }
    
    private static async Task SeedDefaultDataAsync(PChabitDbContext context)
    {
        if (!await context.DailyPatterns.AnyAsync())
        {
            var today = DateTime.Today;
            var pattern = new DailyPattern
            {
                Date = today,
                FirstActivityTime = TimeSpan.Zero,
                LastActivityTime = TimeSpan.Zero,
                TotalActiveTime = TimeSpan.Zero,
                TotalIdleTime = TimeSpan.Zero,
                ProductivityScore = 0,
                InterruptionCount = 0,
                DeepWorkTime = TimeSpan.Zero
            };
            
            context.DailyPatterns.Add(pattern);
            await context.SaveChangesAsync();
        }
    }
    
    public static async Task MigrateAsync(PChabitDbContext context)
    {
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
        
        if (pendingMigrations.Any())
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }
    }
}
