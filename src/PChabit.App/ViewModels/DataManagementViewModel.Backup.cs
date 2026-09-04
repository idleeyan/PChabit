using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class DataManagementViewModel : ViewModelBase
{
    #region 本地数据库备份

    [ObservableProperty]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    private bool _autoBackupEnabled = true;

    [ObservableProperty]
    private int _autoBackupIntervalHours = 4;

    [ObservableProperty]
    private int _maxBackupCount = 7;

    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private int _backupProgress;

    [ObservableProperty]
    private string _backupStatus = string.Empty;

    #endregion
    #region 本地数据库备份

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (IsBackingUp) return;

        IsBackingUp = true;
        BackupStatus = "正在创建备份...";
        BackupProgress = 0;
        AddLog("备份", "开始创建本地数据库备份...");

        try
        {
            var result = await _backupService.CreateBackupAsync(BackupPath);
            if (result.Success)
            {
                BackupStatus = $"备份成功: {result.FilePath}";
                AddLog("成功", $"本地备份完成 ({FormatSize(result.FileSize)})");
                await LoadBackupsAsync();
                await LoadOverviewAsync();
            }
            else
            {
                BackupStatus = $"备份失败: {result.ErrorMessage}";
                AddLog("错误", $"备份失败: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建备份失败");
            BackupStatus = $"备份失败: {ex.Message}";
            AddLog("错误", $"备份失败: {ex.Message}");
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupInfo? backup)
    {
        if (backup == null) return;

        IsBackingUp = true;
        BackupStatus = "正在恢复备份...";
        AddLog("恢复", $"开始从本地备份恢复: {backup.FilePath}");

        try
        {
            var result = await _backupService.RestoreFromBackupAsync(backup.FilePath);
            if (result.Success)
            {
                BackupStatus = "恢复成功，请重启应用以完成恢复";
                AddLog("成功", "本地备份恢复成功，请重启应用");
            }
            else
            {
                BackupStatus = $"恢复失败: {result.ErrorMessage}";
                AddLog("错误", $"恢复失败: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "恢复备份失败");
            BackupStatus = $"恢复失败: {ex.Message}";
            AddLog("错误", $"恢复失败: {ex.Message}");
        }
        finally
        {
            IsBackingUp = false;
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
            AddLog("成功", $"已删除备份: {Path.GetFileName(backup.FilePath)}");
            await LoadOverviewAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除备份失败");
            BackupStatus = $"删除失败: {ex.Message}";
            AddLog("错误", $"删除失败: {ex.Message}");
        }
    }

    #endregion
    public void SaveBackupSettings()
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

