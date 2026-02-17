using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Services;

public class BackupService : IBackupService, IDisposable
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    private readonly string _defaultBackupPath;
    private System.Timers.Timer? _periodicBackupTimer;
    private bool _disposed;

    public event EventHandler<BackupProgressEventArgs>? ProgressChanged;

    public BackupService(IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
        _defaultBackupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PChabit", "Backups");
    }

    public async Task<BackupResult> CreateBackupAsync(string? customPath = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var backupPath = customPath ?? _defaultBackupPath;
            Directory.CreateDirectory(backupPath);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"pchabit_backup_{timestamp}.zip";
            var backupFilePath = Path.Combine(backupPath, backupFileName);

            ProgressChanged?.Invoke(this, new BackupProgressEventArgs { Progress = 10, Status = "正在准备备份..." });

            var dbPath = GetDatabasePath();
            if (!File.Exists(dbPath))
            {
                return new BackupResult { Success = false, ErrorMessage = "数据库文件不存在" };
            }

            ProgressChanged?.Invoke(this, new BackupProgressEventArgs { Progress = 30, Status = "正在压缩数据库..." });

            var tempDbPath = Path.Combine(Path.GetTempPath(), $"pchabit_temp_{timestamp}.db");
            File.Copy(dbPath, tempDbPath, true);

            using (var zip = ZipFile.Open(backupFilePath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(tempDbPath, "pchabit.db");
            }

            File.Delete(tempDbPath);

            ProgressChanged?.Invoke(this, new BackupProgressEventArgs { Progress = 70, Status = "正在记录备份信息..." });

            var fileInfo = new FileInfo(backupFilePath);
            var recordCount = await GetRecordCountAsync(cancellationToken);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var record = new BackupRecord
            {
                FilePath = backupFilePath,
                CreatedAt = DateTime.Now,
                FileSize = fileInfo.Length,
                RecordCount = recordCount,
                IsAutomatic = customPath == null
            };
            dbContext.BackupRecords.Add(record);
            await dbContext.SaveChangesAsync(cancellationToken);

            await CleanupOldBackupsAsync(backupPath, cancellationToken);

            ProgressChanged?.Invoke(this, new BackupProgressEventArgs { Progress = 100, Status = "备份完成" });

            Log.Information("备份创建成功: {FilePath}, 大小: {Size} bytes", backupFilePath, fileInfo.Length);

            return new BackupResult
            {
                Success = true,
                FilePath = backupFilePath,
                FileSize = fileInfo.Length
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建备份失败");
            return new BackupResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public Task<RestoreResult> RestoreFromBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(backupPath))
            {
                return Task.FromResult(new RestoreResult { Success = false, ErrorMessage = "备份文件不存在" });
            }

            var dbPath = GetDatabasePath();
            var currentBackupPath = Path.Combine(
                Path.GetDirectoryName(backupPath)!,
                $"pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            using (var zip = ZipFile.OpenRead(backupPath))
            {
                var entry = zip.GetEntry("pchabit.db");
                if (entry == null)
                {
                    return Task.FromResult(new RestoreResult { Success = false, ErrorMessage = "备份文件格式无效" });
                }

                using (var currentDb = File.OpenRead(dbPath))
                using (var currentZip = ZipFile.Open(currentBackupPath, ZipArchiveMode.Create))
                {
                    currentZip.CreateEntryFromFile(dbPath, "pchabit.db");
                }

                entry.ExtractToFile(dbPath, true);
            }

            Log.Information("数据已从备份恢复: {BackupPath}", backupPath);

            return Task.FromResult(new RestoreResult
            {
                Success = true,
                RequiresRestart = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "恢复备份失败");
            return Task.FromResult(new RestoreResult { Success = false, ErrorMessage = ex.Message });
        }
    }

    public async Task<IEnumerable<BackupInfo>> GetBackupListAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var records = await dbContext.BackupRecords
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync();

        return records.Select(r => new BackupInfo
        {
            FilePath = r.FilePath,
            CreatedAt = r.CreatedAt,
            FileSize = r.FileSize,
            RecordCount = r.RecordCount,
            IsAutomatic = r.IsAutomatic
        });
    }

    public async Task DeleteBackupAsync(string backupPath)
    {
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var record = await dbContext.BackupRecords
            .FirstOrDefaultAsync(r => r.FilePath == backupPath);
        
        if (record != null)
        {
            dbContext.BackupRecords.Remove(record);
            await dbContext.SaveChangesAsync();
        }

        Log.Information("备份已删除: {BackupPath}", backupPath);
    }

    public Task StartPeriodicBackupAsync(TimeSpan interval)
    {
        StopPeriodicBackup();
        
        _periodicBackupTimer = new System.Timers.Timer(interval.TotalMilliseconds);
        _periodicBackupTimer.Elapsed += async (s, e) =>
        {
            await CreateBackupAsync();
        };
        _periodicBackupTimer.Start();

        Log.Information("定时备份已启动，间隔: {Interval}", interval);
        return Task.CompletedTask;
    }

    public void StopPeriodicBackup()
    {
        if (_periodicBackupTimer != null)
        {
            _periodicBackupTimer.Stop();
            _periodicBackupTimer.Dispose();
            _periodicBackupTimer = null;
            Log.Information("定时备份已停止");
        }
    }

    public async Task<ArchiveResult> ArchiveOldDataAsync(DateTime beforeDate, CancellationToken cancellationToken = default)
    {
        try
        {
            var archivePath = Path.Combine(_defaultBackupPath, "Archives");
            Directory.CreateDirectory(archivePath);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var archiveFileName = $"pchabit_archive_{timestamp}.db";
            var archiveFilePath = Path.Combine(archivePath, archiveFileName);

            await using var sourceContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            
            var recordCount = await sourceContext.AppSessions
                .Where(s => s.StartTime < beforeDate)
                .CountAsync(cancellationToken);

            if (recordCount == 0)
            {
                return new ArchiveResult { Success = true, Message = "没有需要归档的数据" };
            }

            var dateRangeStart = await sourceContext.AppSessions.MinAsync(s => s.StartTime, cancellationToken);
            var dateRangeEnd = beforeDate;

            File.Copy(GetDatabasePath(), archiveFilePath, true);

            await using var archiveContext = new PChabitDbContext(
                new DbContextOptionsBuilder<PChabitDbContext>()
                    .UseSqlite($"Data Source={archiveFilePath}")
                    .Options);

            await archiveContext.AppSessions
                .Where(s => s.StartTime >= beforeDate)
                .ExecuteDeleteAsync(cancellationToken);
            await archiveContext.KeyboardSessions
                .Where(s => s.Date >= beforeDate)
                .ExecuteDeleteAsync(cancellationToken);
            await archiveContext.MouseSessions
                .Where(s => s.Date >= beforeDate)
                .ExecuteDeleteAsync(cancellationToken);
            await archiveContext.WebSessions
                .Where(s => s.StartTime >= beforeDate)
                .ExecuteDeleteAsync(cancellationToken);

            var record = new ArchiveRecord
            {
                FilePath = archiveFilePath,
                CreatedAt = DateTime.Now,
                FileSize = new FileInfo(archiveFilePath).Length,
                DateRangeStart = dateRangeStart,
                DateRangeEnd = dateRangeEnd,
                RecordCount = recordCount
            };
            sourceContext.ArchiveRecords.Add(record);
            await sourceContext.SaveChangesAsync(cancellationToken);

            Log.Information("数据归档完成: {FilePath}, 记录数: {Count}", archiveFilePath, recordCount);

            return new ArchiveResult
            {
                Success = true,
                FilePath = archiveFilePath,
                RecordCount = recordCount
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据归档失败");
            return new ArchiveResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<CleanupResult> CleanupOldDataAsync(int retentionDays, bool archiveFirst = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoffDate = DateTime.Now.AddDays(-retentionDays);

            if (archiveFirst)
            {
                await ArchiveOldDataAsync(cutoffDate, cancellationToken);
            }

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var deletedAppSessions = await dbContext.AppSessions
                .Where(s => s.StartTime < cutoffDate)
                .ExecuteDeleteAsync(cancellationToken);

            var deletedKeyboardSessions = await dbContext.KeyboardSessions
                .Where(s => s.Date < cutoffDate)
                .ExecuteDeleteAsync(cancellationToken);

            var deletedMouseSessions = await dbContext.MouseSessions
                .Where(s => s.Date < cutoffDate)
                .ExecuteDeleteAsync(cancellationToken);

            var deletedWebSessions = await dbContext.WebSessions
                .Where(s => s.StartTime < cutoffDate)
                .ExecuteDeleteAsync(cancellationToken);

            var totalDeleted = deletedAppSessions + deletedKeyboardSessions + deletedMouseSessions + deletedWebSessions;

            Log.Information("数据清理完成: 删除 {Count} 条记录, 保留 {Days} 天数据", totalDeleted, retentionDays);

            return new CleanupResult
            {
                Success = true,
                DeletedRecords = totalDeleted
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据清理失败");
            return new CleanupResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private string GetDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PChabit", "Data", "pchabit.db");
    }

    private async Task<int> GetRecordCountAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.AppSessions.CountAsync(cancellationToken) +
               await dbContext.KeyboardSessions.CountAsync(cancellationToken) +
               await dbContext.MouseSessions.CountAsync(cancellationToken) +
               await dbContext.WebSessions.CountAsync(cancellationToken);
    }

    private async Task CleanupOldBackupsAsync(string backupPath, CancellationToken cancellationToken)
    {
        var maxBackups = 7;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var oldRecords = await dbContext.BackupRecords
            .OrderByDescending(r => r.CreatedAt)
            .Skip(maxBackups)
            .ToListAsync(cancellationToken);

        foreach (var record in oldRecords)
        {
            if (File.Exists(record.FilePath))
            {
                File.Delete(record.FilePath);
            }
            dbContext.BackupRecords.Remove(record);
        }

        if (oldRecords.Any())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            Log.Information("已清理 {Count} 个旧备份", oldRecords.Count);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopPeriodicBackup();
            _disposed = true;
        }
    }
}
