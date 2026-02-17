using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;

namespace PChabit.Infrastructure.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(PChabitDbContext context)
    {
        var created = await context.Database.EnsureCreatedAsync();
        Log.Information("数据库创建状态: {Created}", created);
        
        await MigrateSchemaAsync(context);
        
        await SeedDefaultDataAsync(context);
    }
    
    private static async Task MigrateSchemaAsync(PChabitDbContext context)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();
            
            await MigrateTableColumnsAsync(connection, "AppSessions", "Duration", "INTEGER NOT NULL DEFAULT 0");
            await MigrateTableColumnsAsync(connection, "AppSessions", "ActiveDuration", "INTEGER NOT NULL DEFAULT 0");
            await MigrateTableColumnsAsync(connection, "WebSessions", "Duration", "INTEGER NOT NULL DEFAULT 0");
            await MigrateTableColumnsAsync(connection, "WebSessions", "ActiveDuration", "INTEGER NOT NULL DEFAULT 0");
            
            await MigrateTableColumnsAsync(connection, "KeyboardSessions", "KeyFrequency", "TEXT");
            await MigrateTableColumnsAsync(connection, "KeyboardSessions", "KeyCategoryFrequency", "TEXT");
            await MigrateTableColumnsAsync(connection, "KeyboardSessions", "Shortcuts", "TEXT");
            await MigrateTableColumnsAsync(connection, "KeyboardSessions", "TypingBursts", "TEXT");
            
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.CommandText = "UPDATE KeyboardSessions SET KeyFrequency = '{}' WHERE KeyFrequency IS NULL OR KeyFrequency = ''";
                await updateCmd.ExecuteNonQueryAsync();
            }
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.CommandText = "UPDATE KeyboardSessions SET KeyCategoryFrequency = '{}' WHERE KeyCategoryFrequency IS NULL OR KeyCategoryFrequency = ''";
                await updateCmd.ExecuteNonQueryAsync();
            }
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.CommandText = "UPDATE KeyboardSessions SET Shortcuts = '[]' WHERE Shortcuts IS NULL OR Shortcuts = ''";
                await updateCmd.ExecuteNonQueryAsync();
            }
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.CommandText = "UPDATE KeyboardSessions SET TypingBursts = '[]' WHERE TypingBursts IS NULL OR TypingBursts = ''";
                await updateCmd.ExecuteNonQueryAsync();
            }
            
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.CommandText = @"
                    UPDATE AppSessions 
                    SET Duration = CAST((julianday(EndTime) - julianday(StartTime)) * 864000000000 AS INTEGER)
                    WHERE Duration = 0 AND EndTime IS NOT NULL";
                var rowsUpdated = await updateCmd.ExecuteNonQueryAsync();
                Log.Information("修复 AppSessions Duration 数据，更新了 {Count} 条记录", rowsUpdated);
            }
            
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.CommandText = @"
                    UPDATE WebSessions 
                    SET Duration = CAST((julianday(EndTime) - julianday(StartTime)) * 864000000000 AS INTEGER)
                    WHERE Duration = 0 AND EndTime IS NOT NULL";
                var rowsUpdated = await updateCmd.ExecuteNonQueryAsync();
                Log.Information("修复 WebSessions Duration 数据，更新了 {Count} 条记录", rowsUpdated);
            }
            
            await MigrateProgramCategoryTablesAsync(connection);
            await MigrateWebsiteCategoryTablesAsync(connection);
            await MigrateGuidTablesAsync(connection);
            await MigrateBackupTablesAsync(connection);
            await MigrateAnalysisTablesAsync(connection);
            
            await connection.CloseAsync();
            Log.Information("数据库架构迁移完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据库架构迁移失败");
            throw;
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

        if (!tables.Contains("UserGoals"))
        {
            Log.Information("创建 UserGoals 表");
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE UserGoals (
                    Id TEXT PRIMARY KEY,
                    TargetType TEXT NOT NULL,
                    TargetId TEXT,
                    DailyLimitMinutes INTEGER,
                    DailyTargetMinutes INTEGER,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT
                );
                CREATE INDEX IX_UserGoals_TargetType ON UserGoals (TargetType);
                CREATE INDEX IX_UserGoals_IsActive ON UserGoals (IsActive);";
            await createCmd.ExecuteNonQueryAsync();
            Log.Information("UserGoals 表创建成功");
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
    }

    private static async Task MigrateGuidTablesAsync(System.Data.Common.DbConnection connection)
    {
        var guidTables = new[] { "UserGoals", "EfficiencyScores", "WorkPatterns", "InsightReports", "BackupRecords", "ArchiveRecords" };
        
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
