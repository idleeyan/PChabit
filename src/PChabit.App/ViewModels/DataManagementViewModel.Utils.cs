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
    [RelayCommand]
    private void ClearLogs()
    {
        try
        {
            OperationLogs.Clear();
            GlobalLogs.Clear();
            PChabit.App.Services.GlobalOpLog.Action("数据管理", "已清空界面日志显示（磁盘 ops-*.log 保留）");
        }
        catch { }
    }
    private void OnBackupProgressChanged(object? sender, BackupProgressEventArgs e)
    {
        RunOnUIThread(() =>
        {
            BackupProgress = e.Progress;
            if (!string.IsNullOrEmpty(e.Status)) BackupStatus = e.Status;
        });
    }
    private void OnWebDAVProgressChanged(object? sender, WebDAVProgressEventArgs e)
    {
        RunOnUIThread(() =>
        {
            if (e.IsUpload) UploadProgress = e.Progress;
            else DownloadProgress = e.Progress;
            if (!string.IsNullOrEmpty(e.Status)) WebDAVStatus = e.Status;
        });
    }
    private void AddLog(string type, string message)
    {
        var logItem = new OperationLogItem
        {
            Time = DateTime.Now,
            Type = type,
            Message = message
        };

        RunOnUIThread(() =>
        {
            OperationLogs.Insert(0, logItem);
            if (OperationLogs.Count > 200) OperationLogs.RemoveAt(OperationLogs.Count - 1);
        });

        try
        {
            var level = type switch
            {
                "错误" => "error",
                "警告" => "warn",
                "成功" => "ok",
                _ => "op"
            };
            if (level == "error") PChabit.App.Services.GlobalOpLog.Error("数据管理", message);
            else if (level == "warn") PChabit.App.Services.GlobalOpLog.Warn("数据管理", message);
            else if (level == "ok") PChabit.App.Services.GlobalOpLog.Success("数据管理", message);
            else PChabit.App.Services.GlobalOpLog.Action("数据管理", message);
        }
        catch { }
    }
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}

