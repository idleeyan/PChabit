using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;
using Xunit;

namespace PChabit.Tests;

/// <summary>
/// 数据库迁移双路径测试（改造计划阶段 G1，陷阱 20）。
/// 新库走 EnsureCreated；老库（缺后加的表/列）走 DatabaseInitializer 的手动 SQL 迁移。
/// 老库模拟方式：先用 EnsureCreated 建出与 EF 模型一致的「当前最新库」，
/// 再 DROP 后加的表 / 重建精简列版本的既有表，构造历史版本库状态——
/// 这与真实升级场景一致（真实老表的列集合是其年代 EF 模型的子集）。
/// 断言：两条路径殊途同归——表与关键列齐全、迁移幂等可重复执行、老数据保留。
/// </summary>
public class DatabaseMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PChabitDbContext> _options;

    public DatabaseMigrationTests()
    {
        // 每实例唯一库名 + Shared=True：迁移器会开第二条独立连接（真实行为），
        // 内存库必须跨连接共享；唯一名避免 xUnit 并行时与其他测试类共享同一内存库
        _connection = new SqliteConnection(
            $"DataSource=file:memdb_migration_{Guid.NewGuid():N}?mode=memory&cache=shared");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PChabitDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    private PChabitDbContext CreateContext() => new(_options);

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static async Task<HashSet<string>> GetTablesAsync(SqliteConnection connection)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        return columns;
    }

    /// <summary>构造「3.36 时代老库」：基础表列齐全，但缺全部后加的表、既有表缺后加的列。</summary>
    private async Task CreateLegacyDatabaseAsync()
    {
        // 先建当前模型（真实老库的基础表列集合是当年模型的子集，这里取当前全集再裁剪）
        using (var context = CreateContext())
        {
            await context.Database.EnsureCreatedAsync();
        }

        // 重建 WebSessions 为精简版（模拟 3.36 前的老表：无 CategoryName/IsLegacy/CategoryId 等后加列）
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"
                DROP TABLE WebSessions;
                CREATE TABLE WebSessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Domain TEXT, Title TEXT, Url TEXT, Browser TEXT,
                    StartTime TEXT NOT NULL, EndTime TEXT
                );
                INSERT INTO WebSessions (Domain, Title, Url, Browser, StartTime, EndTime) VALUES
                    ('github.com', 'GitHub', 'https://github.com', 'edge', '2026-11-06 10:00:00', NULL);
                DROP TABLE KeyboardSessions;
                CREATE TABLE KeyboardSessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL, Hour INTEGER NOT NULL,
                    TotalKeyPresses INTEGER NOT NULL,
                    AverageTypingSpeed REAL NOT NULL,
                    PeakTypingSpeed INTEGER NOT NULL,
                    BackspaceCount INTEGER NOT NULL DEFAULT 0,
                    DeleteCount INTEGER NOT NULL DEFAULT 0
                );
                DROP TABLE IF EXISTS EfficiencyScores;
                DROP TABLE IF EXISTS WorkPatterns;
                DROP TABLE IF EXISTS InsightReports;
                DROP TABLE IF EXISTS ProgramCategories;
                DROP TABLE IF EXISTS ProgramCategoryMappings;
                DROP TABLE IF EXISTS BrowserBookmarks;";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ---------- 路径一：新库 EnsureCreated ----------

    [Fact]
    public async Task NewDatabase_EnsureCreated_HasCoreTables()
    {
        await using var context = CreateContext();
        var created = await context.Database.EnsureCreatedAsync();
        Assert.True(created);

        var tables = await GetTablesAsync(_connection);
        Assert.Contains("AppSessions", tables);
        Assert.Contains("WebSessions", tables);
        Assert.Contains("KeyboardSessions", tables);
        Assert.Contains("MouseSessions", tables);
        Assert.Contains("DailySummaries", tables);
    }

    [Fact]
    public async Task NewDatabase_InitializeAsync_AlsoCreatesAnalysisTables()
    {
        // InitializeAsync = EnsureCreated + 手动迁移 + 种子；分析表走手动路径补充
        await using var context = CreateContext();
        await DatabaseInitializer.InitializeAsync(context);

        var tables = await GetTablesAsync(_connection);
        Assert.Contains("EfficiencyScores", tables);
        Assert.Contains("WorkPatterns", tables);
        Assert.Contains("InsightReports", tables);
    }

    // ---------- 路径二：老库手动 SQL 迁移（缺表补表、缺列补列） ----------

    [Fact]
    public async Task LegacyDatabase_InitializeAsync_CreatesMissingTables()
    {
        await CreateLegacyDatabaseAsync();

        await using var context = CreateContext();
        await DatabaseInitializer.InitializeAsync(context);

        var tables = await GetTablesAsync(_connection);
        Assert.Contains("ProgramCategories", tables);
        Assert.Contains("ProgramCategoryMappings", tables);
        Assert.Contains("BrowserBookmarks", tables);
        Assert.Contains("EfficiencyScores", tables);
    }

    [Fact]
    public async Task LegacyDatabase_Migration_AddsMissingColumnsToExistingTable()
    {
        await CreateLegacyDatabaseAsync();

        await using var context = CreateContext();
        await DatabaseInitializer.InitializeAsync(context);

        var columns = await GetColumnsAsync(_connection, "WebSessions");
        Assert.Contains("IsLegacy", columns);
        Assert.Contains("CategoryName", columns);
        Assert.Contains("CategoryId", columns);

        using var verify = _connection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*), MAX(IsLegacy) FROM WebSessions WHERE Domain='github.com'";
        using var reader = await verify.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1)); // 补列默认 0，旧行保留
    }

    [Fact]
    public async Task LegacyDatabase_Migration_AddsDailySummaryAnalysisColumns()
    {
        using (var seedContext = CreateContext())
        {
            await seedContext.Database.EnsureCreatedAsync();
        }
        // DailySummaries 裁掉分析升级 P0 扩展列，模拟 3.45 前老表
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"
                DROP TABLE DailySummaries;
                CREATE TABLE DailySummaries (
                    Date TEXT PRIMARY KEY, TotalMinutes REAL NOT NULL,
                    KeyCount INTEGER NOT NULL, MouseClicks INTEGER NOT NULL,
                    SwitchCount INTEGER NOT NULL, FocusMinutes REAL NOT NULL)";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var context = CreateContext();
        await DatabaseInitializer.InitializeAsync(context);

        var columns = await GetColumnsAsync(_connection, "DailySummaries");
        Assert.Contains("CpuLoadAvg", columns);
        Assert.Contains("GpuTempMax", columns);
        Assert.Contains("MetricsVersion", columns);
        Assert.Contains("NightMinutes", columns);
    }

    // ---------- 幂等性：迁移可重复执行 ----------

    [Fact]
    public async Task Migration_IsIdempotent_RunTwiceKeepsSchema()
    {
        await using (var context = CreateContext())
        {
            await DatabaseInitializer.InitializeAsync(context);
        }
        var tablesAfterFirst = await GetTablesAsync(_connection);

        // 第二次 InitializeAsync 不应抛异常、表集合不缩
        await using (var context = CreateContext())
        {
            await DatabaseInitializer.InitializeAsync(context);
        }
        var tablesAfterSecond = await GetTablesAsync(_connection);
        Assert.True(tablesAfterFirst.IsSubsetOf(tablesAfterSecond));
    }

    [Fact]
    public async Task Migration_Idempotent_DataSurvivesRepeatRun()
    {
        await using (var context = CreateContext())
        {
            await DatabaseInitializer.InitializeAsync(context);
            context.AppSessions.Add(new AppSession
            {
                ProcessName = "test.exe", AppName = "Test",
                StartTime = DateTime.Today.AddHours(9)
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            await DatabaseInitializer.InitializeAsync(context);
            Assert.Equal(1, await context.AppSessions.CountAsync());
        }
    }

    // ---------- 关键列完整性（计划验收：迁移后关键列存在） ----------

    [Theory]
    [InlineData("AppSessions", new[] { "Duration", "ActiveDuration" })]
    [InlineData("KeyboardSessions", new[] { "KeyFrequency", "Shortcuts", "TypingBursts" })]
    public async Task MigratedSchema_HasKeyColumns(string table, string[] expectedColumns)
    {
        await using var context = CreateContext();
        await DatabaseInitializer.InitializeAsync(context);

        var columns = await GetColumnsAsync(_connection, table);
        foreach (var column in expectedColumns)
            Assert.Contains(column, columns);
    }

    [Fact]
    public async Task Migration_CreatesGuidTables_ForStickyNotes()
    {
        await using var context = CreateContext();
        await DatabaseInitializer.InitializeAsync(context);

        var tables = await GetTablesAsync(_connection);
        Assert.Contains("StickyNotes", tables);
    }
}
