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
    #region 云端同步

    [ObservableProperty]
    private string _webDAVUrl = "";

    [ObservableProperty]
    private string _webDAVUsername = "";

    [ObservableProperty]
    private string _webDAVPassword = "";

    [ObservableProperty]
    private bool _webDAVEnabled;

    [ObservableProperty]
    private int _maxCloudBackupCount = 5;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private int _uploadProgress;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private string _webDAVStatus = "";

    #endregion
    #region 云端同步（上传本地 ZIP，自动清理旧文件）

    [RelayCommand]
    private async Task TestWebDAVAsync()
    {
        if (string.IsNullOrWhiteSpace(WebDAVUrl))
        {
            WebDAVStatus = "请输入 WebDAV 地址";
            AddLog("错误", "请输入 WebDAV 地址");
            return;
        }

        WebDAVStatus = "正在测试连接...";
        AddLog("测试", "正在测试 WebDAV 连接...");

        try
        {
            var success = await _webDAVSyncService.TestConnectionAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword);
            if (success)
            {
                WebDAVStatus = "连接成功！";
                AddLog("成功", "WebDAV 连接测试成功");
                await SaveWebDAVSettingsAsync();
            }
            else
            {
                WebDAVStatus = "连接失败，请检查配置";
                AddLog("错误", "WebDAV 连接测试失败");
            }
        }
        catch (Exception ex)
        {
            WebDAVStatus = $"连接异常: {ex.Message}";
            AddLog("错误", $"连接异常: {ex.Message}");
            Log.Error(ex, "WebDAV 测试连接失败");
        }
    }

    [RelayCommand]
    private async Task SyncToWebDAVAsync()
    {
        if (!WebDAVEnabled || string.IsNullOrWhiteSpace(WebDAVUrl))
        {
            WebDAVStatus = "请先配置并启用 WebDAV";
            AddLog("错误", "请先配置并启用 WebDAV");
            return;
        }

        if (IsSyncing) return;

        WebDAVStatus = "正在准备本地备份...";
        IsSyncing = true;
        AddLog("同步", "开始云端同步...");

        try
        {
            // 先在本地创建一个备份（ZIP）
            WebDAVStatus = "正在压缩本地数据库...";
            var backupResult = await _backupService.CreateBackupAsync(BackupPath);
            if (!backupResult.Success || string.IsNullOrEmpty(backupResult.FilePath))
            {
                WebDAVStatus = $"本地备份创建失败: {backupResult.ErrorMessage}";
                AddLog("错误", $"本地备份失败: {backupResult.ErrorMessage}");
                return;
            }

            // 读取 ZIP 字节
            WebDAVStatus = "正在上传到云端...";
            var fileName = Path.GetFileName(backupResult.FilePath);
            var content = await File.ReadAllBytesAsync(backupResult.FilePath);

            var result = await _webDAVSyncService.UploadFileWithProgressAsync(
                WebDAVUrl, WebDAVUsername, WebDAVPassword, fileName, content,
                new Progress<int>(p => UploadProgress = p));

            if (result != null)
            {
                _settingsService.WebDAVLastSync = DateTime.Now;
                await _settingsService.SaveAsync();
                LastCloudSyncText = _settingsService.WebDAVLastSync!.Value.ToString("yyyy-MM-dd HH:mm:ss");
                WebDAVStatus = $"同步成功！文件: {fileName} ({FormatSize(content.Length)})";
                AddLog("成功", $"云端同步完成: {fileName}");

                // 清理云端旧文件
                await CleanupCloudBackupsAsync();
                await BrowseRemoteBackupsAsync(silent: true);
                await LoadOverviewAsync();
            }
            else
            {
                WebDAVStatus = "同步失败，请检查配置";
                AddLog("错误", "云端同步失败");
            }
        }
        catch (Exception ex)
        {
            WebDAVStatus = $"同步异常: {ex.Message}";
            AddLog("错误", $"同步异常: {ex.Message}");
            Log.Error(ex, "WebDAV 同步失败");
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private async Task BrowseRemoteBackupsAsync()
    {
        await BrowseRemoteBackupsAsync(silent: false);
    }

    private async Task BrowseRemoteBackupsAsync(bool silent)
    {
        if (string.IsNullOrWhiteSpace(WebDAVUrl))
        {
            if (!silent) AddLog("错误", "请先配置 WebDAV");
            return;
        }

        if (!silent) WebDAVStatus = "正在浏览远程文件...";

        try
        {
            var files = await _webDAVSyncService.ListFilesAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword, null);
            RemoteFiles.Clear();

            var cloudBackups = new List<WebDAVFileInfo>();
            foreach (var file in files)
            {
                if (!file.IsDirectory && (file.Name.EndsWith(".zip") || file.Name.EndsWith(".json")))
                {
                    RemoteFiles.Add(file);
                    if (file.Name.EndsWith(".zip")) cloudBackups.Add(file);
                }
            }

            CloudBackupsText = $"{cloudBackups.Count} 个，最新 {cloudBackups.OrderByDescending(f => f.LastModified).FirstOrDefault()?.LastModified?.ToString("MM-dd HH:mm") ?? "无"}";
            if (!silent) AddLog("成功", $"云端文件列表: {RemoteFiles.Count} 个");
        }
        catch (Exception ex)
        {
            if (!silent) AddLog("错误", $"浏览失败: {ex.Message}");
            Log.Error(ex, "WebDAV 浏览失败");
        }
    }

    [RelayCommand]
    private async Task RestoreCloudBackupAsync(WebDAVFileInfo? file)
    {
        if (file == null)
        {
            WebDAVStatus = "请先选择一个云端备份文件";
            return;
        }

        if (!file.Name.EndsWith(".zip"))
        {
            WebDAVStatus = "仅支持恢复新版 .zip 格式的云端备份（旧的 .json 备份已弃用）";
            AddLog("错误", "请选择 .zip 格式的云端备份");
            return;
        }

        AddLog("恢复", $"开始从云端恢复: {file.Name}");

        try
        {
            var progress = new Progress<int>(p => DownloadProgress = p);
            var content = await _webDAVSyncService.DownloadFileWithProgressAsync(
                WebDAVUrl, WebDAVUsername, WebDAVPassword, file.FullPath, progress);

            if (content == null)
            {
                WebDAVStatus = "下载云端备份失败";
                AddLog("错误", "下载云端备份失败");
                return;
            }

            // 保存到临时文件，然后调用 RestoreFromBackupAsync
            var tempPath = Path.Combine(Path.GetTempPath(), $"pchabit_cloud_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            await File.WriteAllBytesAsync(tempPath, content);

            var result = await _backupService.RestoreFromBackupAsync(tempPath);
            try { File.Delete(tempPath); } catch { }

            if (result.Success)
            {
                WebDAVStatus = "云端备份恢复成功！请重启应用。";
                AddLog("成功", $"云端备份恢复成功: {file.Name}，请重启应用");
            }
            else
            {
                WebDAVStatus = $"恢复失败: {result.ErrorMessage}";
                AddLog("错误", $"恢复失败: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            WebDAVStatus = $"恢复异常: {ex.Message}";
            AddLog("错误", $"恢复异常: {ex.Message}");
            Log.Error(ex, "恢复云端备份失败");
        }
    }

    [RelayCommand]
    private async Task DeleteCloudBackupAsync(WebDAVFileInfo? file)
    {
        if (file == null) return;
        AddLog("删除", $"开始删除云端文件: {file.Name}");
        var success = await _webDAVSyncService.DeleteFileAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword, file.FullPath);
        if (success)
        {
            RemoteFiles.Remove(file);
            AddLog("成功", $"已删除: {file.Name}");
            await BrowseRemoteBackupsAsync(silent: true);
        }
        else
        {
            AddLog("错误", $"删除失败: {file.Name}");
        }
    }

    private async Task CleanupCloudBackupsAsync()
    {
        try
        {
            var files = await _webDAVSyncService.ListFilesAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword, null);
            var zipFiles = files
                .Where(f => !f.IsDirectory && f.Name.EndsWith(".zip"))
                .OrderByDescending(f => f.LastModified ?? DateTime.MinValue)
                .ToList();

            if (zipFiles.Count <= MaxCloudBackupCount) return;

            var toDelete = zipFiles.Skip(MaxCloudBackupCount).ToList();
            int deleted = 0;
            foreach (var f in toDelete)
            {
                if (await _webDAVSyncService.DeleteFileAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword, f.FullPath))
                    deleted++;
            }
            if (deleted > 0)
            {
                AddLog("清理", $"已清理云端 {deleted} 个旧备份");
                Log.Information("WebDAV 旧文件清理: 删除 {Count} 个", deleted);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WebDAV 旧文件清理失败");
        }
    }

    #endregion
    public async Task SaveWebDAVSettingsAsync()
    {
        _settingsService.WebDAVUrl = WebDAVUrl;
        _settingsService.WebDAVUsername = WebDAVUsername;
        _settingsService.WebDAVPassword = WebDAVPassword;
        _settingsService.WebDAVEnabled = WebDAVEnabled;
        _settingsService.MaxCloudBackupCount = MaxCloudBackupCount;
        await _settingsService.SaveAsync();
    }
}

