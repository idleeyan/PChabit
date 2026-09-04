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
    #region 数据清理

    [ObservableProperty]
    private int _dataRetentionDays = 180;

    [ObservableProperty]
    private bool _archiveBeforeCleanup = true;

    [ObservableProperty]
    private bool _isCleaningUp;

    [ObservableProperty]
    private string _cleanupStatus = string.Empty;

    #endregion
    #region 数据清理

    [RelayCommand]
    private async Task CleanupDataAsync()
    {
        if (IsCleaningUp) return;

        IsCleaningUp = true;
        CleanupStatus = "正在清理数据...";
        AddLog("清理", $"开始清理 {DataRetentionDays} 天前的数据...");

        try
        {
            var result = await _backupService.CleanupOldDataAsync(DataRetentionDays, ArchiveBeforeCleanup);
            CleanupStatus = result.Success
                ? $"清理完成，删除了 {result.DeletedRecords} 条记录"
                : $"清理失败: {result.ErrorMessage}";
            AddLog(result.Success ? "成功" : "错误",
                result.Success ? $"清理完成，删除 {result.DeletedRecords} 条记录" : $"清理失败: {result.ErrorMessage}");
            if (result.Success) await LoadOverviewAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据清理失败");
            CleanupStatus = $"清理失败: {ex.Message}";
            AddLog("错误", $"清理失败: {ex.Message}");
        }
        finally
        {
            IsCleaningUp = false;
        }
    }

    #endregion
}

