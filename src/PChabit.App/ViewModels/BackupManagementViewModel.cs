using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PChabit.Core.Interfaces;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class BackupManagementViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private bool _isRestoring;

    [ObservableProperty]
    private int _backupProgress;

    [ObservableProperty]
    private string _backupStatus = string.Empty;

    [ObservableProperty]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    private bool _autoBackupEnabled = true;

    [ObservableProperty]
    private int _autoBackupIntervalHours = 4;

    [ObservableProperty]
    private int _maxBackupCount = 7;

    [ObservableProperty]
    private int _dataRetentionDays = 180;

    [ObservableProperty]
    private bool _archiveBeforeCleanup = true;

    [ObservableProperty]
    private bool _isCleaningUp;

    [ObservableProperty]
    private string _cleanupStatus = string.Empty;

    public ObservableCollection<BackupInfo> Backups { get; } = new();

    public BackupManagementViewModel(IBackupService backupService, ISettingsService settingsService) : base()
    {
        _backupService = backupService;
        _settingsService = settingsService;
        Title = "备份管理";

        _backupPath = string.IsNullOrEmpty(settingsService.BackupPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PChabit", "Backups")
            : settingsService.BackupPath;

        _autoBackupEnabled = settingsService.AutoBackupEnabled;
        _autoBackupIntervalHours = settingsService.AutoBackupIntervalHours;
        _maxBackupCount = settingsService.MaxBackupCount;
        _dataRetentionDays = settingsService.DataRetentionDays;
        _archiveBeforeCleanup = settingsService.ArchiveBeforeCleanup;

        _backupService.ProgressChanged += OnBackupProgressChanged;
    }

    public async Task LoadBackupsAsync()
    {
        Backups.Clear();
        var backups = await _backupService.GetBackupListAsync();
        foreach (var backup in backups)
        {
            Backups.Add(backup);
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (IsBackingUp) return;

        IsBackingUp = true;
        BackupStatus = "正在创建备份...";
        BackupProgress = 0;

        try
        {
            var result = await _backupService.CreateBackupAsync(BackupPath);
            if (result.Success)
            {
                BackupStatus = $"备份成功: {result.FilePath}";
                await LoadBackupsAsync();
            }
            else
            {
                BackupStatus = $"备份失败: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建备份失败");
            BackupStatus = $"备份失败: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupInfo? backup)
    {
        if (backup == null || IsRestoring) return;

        IsRestoring = true;
        BackupStatus = "正在恢复备份...";

        try
        {
            var result = await _backupService.RestoreFromBackupAsync(backup.FilePath);
            if (result.Success)
            {
                BackupStatus = "恢复成功，请重启应用以完成恢复";
            }
            else
            {
                BackupStatus = $"恢复失败: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "恢复备份失败");
            BackupStatus = $"恢复失败: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync(BackupInfo? backup)
    {
        if (backup == null) return;

        try
        {
            await _backupService.DeleteBackupAsync(backup.FilePath);
            Backups.Remove(backup);
            BackupStatus = "备份已删除";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除备份失败");
            BackupStatus = $"删除失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CleanupDataAsync()
    {
        if (IsCleaningUp) return;

        IsCleaningUp = true;
        CleanupStatus = "正在清理数据...";

        try
        {
            var result = await _backupService.CleanupOldDataAsync(DataRetentionDays, ArchiveBeforeCleanup);
            CleanupStatus = result.Success
                ? $"清理完成，删除了 {result.DeletedRecords} 条记录"
                : $"清理失败: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据清理失败");
            CleanupStatus = $"清理失败: {ex.Message}";
        }
        finally
        {
            IsCleaningUp = false;
        }
    }

    private void OnBackupProgressChanged(object? sender, BackupProgressEventArgs e)
    {
        RunOnUIThread(() =>
        {
            BackupProgress = e.Progress;
            BackupStatus = e.Status ?? string.Empty;
        });
    }

    public void SaveSettings()
    {
        _settingsService.BackupPath = BackupPath;
        _settingsService.AutoBackupEnabled = AutoBackupEnabled;
        _settingsService.AutoBackupIntervalHours = AutoBackupIntervalHours;
        _settingsService.MaxBackupCount = MaxBackupCount;
        _settingsService.DataRetentionDays = DataRetentionDays;
        _settingsService.ArchiveBeforeCleanup = ArchiveBeforeCleanup;
        _settingsService.Save();
    }
}
