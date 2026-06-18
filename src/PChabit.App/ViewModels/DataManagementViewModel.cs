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
    private readonly IBackupService _backupService;
    private readonly ISettingsService _settingsService;
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly IWebDAVSyncService _webDAVSyncService;
    private readonly IExportService _exportService;

    #region 数据概览

    [ObservableProperty]
    private string _databaseSizeText = "—";

    [ObservableProperty]
    private string _totalRecordsText = "—";

    [ObservableProperty]
    private string _earliestRecordText = "—";

    [ObservableProperty]
    private string _latestRecordText = "—";

    [ObservableProperty]
    private string _localBackupsText = "—";

    [ObservableProperty]
    private string _cloudBackupsText = "—";

    [ObservableProperty]
    private string _lastCloudSyncText = "从未同步";

    #endregion

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

    #region 数据导出

    [ObservableProperty]
    private DateTime _exportStartDate = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _exportEndDate = DateTime.Today;

    public DateTimeOffset? ExportStartDateOffset
    {
        get => new DateTimeOffset(ExportStartDate);
        set { if (value.HasValue) ExportStartDate = value.Value.Date; }
    }

    public DateTimeOffset? ExportEndDateOffset
    {
        get => new DateTimeOffset(ExportEndDate);
        set { if (value.HasValue) ExportEndDate = value.Value.Date; }
    }

    [ObservableProperty]
    private string _exportFormat = "json";

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _exportStatus = string.Empty;

    [ObservableProperty]
    private string _lastExportPath = string.Empty;

    public IReadOnlyList<string> ExportFormats { get; } = new[] { "json", "markdown", "csv", "ai-prompt" };

    #endregion

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

    public ObservableCollection<BackupInfo> Backups { get; } = new();
    public ObservableCollection<WebDAVFileInfo> RemoteFiles { get; } = new();
    public ObservableCollection<OperationLogItem> OperationLogs { get; } = new();
}

public class OperationLogItem
{
    public DateTime Time { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string FormattedTime => Time.ToString("HH:mm:ss");
}

public partial class DataManagementViewModel
{
    public DataManagementViewModel(
        IBackupService backupService,
        ISettingsService settingsService,
        IDbContextFactory<PChabitDbContext> dbFactory,
        IWebDAVSyncService webDAVSyncService,
        IExportService exportService) : base()
    {
        _backupService = backupService;
        _settingsService = settingsService;
        _dbFactory = dbFactory;
        _webDAVSyncService = webDAVSyncService;
        _exportService = exportService;
        Title = "数据管理";

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

        _webDAVUrl = settingsService.WebDAVUrl ?? "";
        _webDAVUsername = settingsService.WebDAVUsername ?? "";
        _webDAVPassword = settingsService.WebDAVPassword ?? "";
        _webDAVEnabled = settingsService.WebDAVEnabled;

        _maxCloudBackupCount = settingsService.MaxCloudBackupCount;

        if (settingsService.WebDAVLastSync.HasValue)
        {
            _lastCloudSyncText = settingsService.WebDAVLastSync.Value.ToString("yyyy-MM-dd HH:mm:ss");
        }

        _backupService.ProgressChanged += OnBackupProgressChanged;
        _webDAVSyncService.ProgressChanged += OnWebDAVProgressChanged;
    }

    public async Task LoadOverviewAsync()
    {
        Log.Information("DM: LoadOverviewAsync 开始");
        try
        {
            // Phase 1: 线程池 — 文件 I/O + DB 查询
            var (dbSizeText, totalRecordsText, earliestText, latestText, localBackupsText) = await Task.Run(async () =>
            {
                // 数据库大小
                var dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PChabit", "Data", "pchabit.db");
                string sizeText;
                if (File.Exists(dbPath))
                {
                    var size = new FileInfo(dbPath).Length;
                    sizeText = FormatSize(size);
                }
                else
                {
                    sizeText = "未找到";
                }

                // 总记录数 + 最早/最新记录
                await using var dbContext = await _dbFactory.CreateDbContextAsync();
                var appCount = await dbContext.AppSessions.CountAsync();
                var keyCount = await dbContext.KeyboardSessions.CountAsync();
                var mouseCount = await dbContext.MouseSessions.CountAsync();
                var webCount = await dbContext.WebSessions.CountAsync();
                var totalCount = appCount + keyCount + mouseCount + webCount;
                var recordsText = $"{totalCount:N0} (应用 {appCount:N0} / 键鼠 {keyCount + mouseCount:N0} / 网页 {webCount:N0})";

                var earliest = await dbContext.AppSessions.MinAsync(s => (DateTime?)s.StartTime);
                var latest = await dbContext.AppSessions.MaxAsync(s => (DateTime?)s.StartTime);
                var earliestStr = earliest?.ToString("yyyy-MM-dd") ?? "无";
                var latestStr = latest?.ToString("yyyy-MM-dd") ?? "无";

                // 本地备份数
                var backups = await _backupService.GetBackupListAsync();
                var backupText = $"{backups.Count()} 个，最新 {backups.FirstOrDefault()?.CreatedAt.ToString("MM-dd HH:mm") ?? "无"}";

                return (sizeText, recordsText, earliestStr, latestStr, backupText);
            });

            // Phase 2: UI 线程 — 属性赋值
            DatabaseSizeText = dbSizeText;
            TotalRecordsText = totalRecordsText;
            EarliestRecordText = earliestText;
            LatestRecordText = latestText;
            LocalBackupsText = localBackupsText;
            Log.Information("DM: LoadOverview 完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载数据概览失败");
            AddLog("错误", $"加载概览失败: {ex.Message}");
        }
        Log.Information("DM: LoadOverviewAsync 结束");
    }

    public async Task LoadBackupsAsync()
    {
        Log.Information("DM: LoadBackupsAsync 开始");
        // Phase 1: 线程池 — 获取备份列表
        var backups = await Task.Run(async () =>
        {
            return (await _backupService.GetBackupListAsync()).ToList();
        });
        Log.Information("DM: LoadBackupsAsync GetBackupListAsync 完成, {Count} 条", backups.Count);

        // Phase 2: UI 线程 — ObservableCollection 必须在 UI 线程修改
        await RunOnUIThreadAsync(() =>
        {
            Log.Information("DM: LoadBackupsAsync RunOnUIThread callback, Backups.Clear+Add");
            Backups.Clear();
            foreach (var backup in backups)
            {
                Backups.Add(backup);
            }
            return Task.CompletedTask;
        });
        Log.Information("DM: LoadBackupsAsync 结束");
    }

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

    #region 数据导出（统一用 IExportService）

    [RelayCommand]
    private async Task ExportDataAsync()
    {
        if (IsExporting) return;

        if (ExportEndDate < ExportStartDate)
        {
            ExportStatus = "结束日期不能早于开始日期";
            AddLog("错误", "结束日期不能早于开始日期");
            return;
        }

        Log.Information("开始导出数据");
        IsExporting = true;
        ExportStatus = "正在导出...";
        AddLog("导出", $"正在导出 {ExportStartDate:yyyy-MM-dd} 至 {ExportEndDate:yyyy-MM-dd} 的 {ExportFormat} 数据...");

        try
        {
            var ext = ExportFormat switch
            {
                "json" => ".json",
                "markdown" => ".md",
                "csv" => ".csv",
                "ai-prompt" => ".md",
                _ => ".txt"
            };

            var request = new ExportRequest
            {
                StartTime = ExportStartDate,
                EndTime = ExportEndDate.AddDays(1).AddSeconds(-1),
                Format = ExportFormat,
                Options = new ExportOptions
                {
                    IncludeMetadata = true,
                    IncludeStatistics = true,
                    IncludePatterns = true,
                    GroupByDay = true,
                    MaxItems = 100000
                }
            };

            var content = await _exportService.ExportAsync(request);

            var fileName = $"pchabit_export_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(documentsPath, "PChabit", fileName);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, content);

            LastExportPath = filePath;
            ExportStatus = $"导出成功！文件: {fileName} ({FormatSize(new FileInfo(filePath).Length)})";
            AddLog("成功", $"导出完成: {fileName}");
            Log.Information("数据已导出到: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ExportStatus = $"导出失败: {ex.Message}";
            AddLog("错误", $"导出失败: {ex.Message}");
            Log.Error(ex, "导出数据失败");
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task OpenExportFolderAsync()
    {
        try
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documentsPath, "PChabit");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开导出文件夹失败");
            AddLog("错误", $"打开导出文件夹失败: {ex.Message}");
        }
        await Task.CompletedTask;
    }

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

    #region 持久化 & 工具

    public async Task SaveWebDAVSettingsAsync()
    {
        _settingsService.WebDAVUrl = WebDAVUrl;
        _settingsService.WebDAVUsername = WebDAVUsername;
        _settingsService.WebDAVPassword = WebDAVPassword;
        _settingsService.WebDAVEnabled = WebDAVEnabled;
        _settingsService.MaxCloudBackupCount = MaxCloudBackupCount;
        await _settingsService.SaveAsync();
    }

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

    #endregion
}
