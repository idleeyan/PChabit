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
        OperationLogs.Clear();
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
            if (OperationLogs.Count > 100) OperationLogs.RemoveAt(OperationLogs.Count - 1);
        });
    }
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}

