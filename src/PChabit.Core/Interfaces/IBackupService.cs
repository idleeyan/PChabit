namespace PChabit.Core.Interfaces;

public interface IBackupService
{
    Task<BackupResult> CreateBackupAsync(string? customPath = null, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreFromBackupAsync(string backupPath, CancellationToken cancellationToken = default);
    Task<IEnumerable<BackupInfo>> GetBackupListAsync();
    Task DeleteBackupAsync(string backupPath);
    Task StartPeriodicBackupAsync(TimeSpan interval);
    void StopPeriodicBackup();
    Task<ArchiveResult> ArchiveOldDataAsync(DateTime beforeDate, CancellationToken cancellationToken = default);
    Task<CleanupResult> CleanupOldDataAsync(int retentionDays, bool archiveFirst = true, CancellationToken cancellationToken = default);
    event EventHandler<BackupProgressEventArgs>? ProgressChanged;
}

public class BackupResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public long FileSize { get; set; }
    public string? ErrorMessage { get; set; }
}

public class RestoreResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresRestart { get; set; }
}

public class BackupInfo
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long FileSize { get; set; }
    public int? RecordCount { get; set; }
    public bool IsAutomatic { get; set; }
}

public class BackupProgressEventArgs : EventArgs
{
    public int Progress { get; set; }
    public string? Status { get; set; }
}

public class ArchiveResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public int RecordCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
}

public class CleanupResult
{
    public bool Success { get; set; }
    public int DeletedRecords { get; set; }
    public string? ErrorMessage { get; set; }
}
