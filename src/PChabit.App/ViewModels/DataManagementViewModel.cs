using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Monitoring;
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
    public ObservableCollection<BackupInfo> Backups { get; } = new();
    public ObservableCollection<WebDAVFileInfo> RemoteFiles { get; } = new();
    public ObservableCollection<OperationLogItem> OperationLogs { get; } = new();

    /// <summary>全局日志（含书签库推送等，来自 GlobalOpLog）。</summary>
    public ObservableCollection<string> GlobalLogs { get; } = new();

    public bool HasGlobalLogs => GlobalLogs.Count > 0;

    private void OnGlobalOpLogged(PChabit.App.Services.GlobalOpLog.OpLogItem item)
    {
        try
        {
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                GlobalLogs.Insert(0, item.Display);
                if (GlobalLogs.Count > 300) GlobalLogs.RemoveAt(GlobalLogs.Count - 1);
                OnPropertyChanged(nameof(HasGlobalLogs));
            });
        }
        catch { }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RefreshGlobalLogsAsync()
    {
        try
        {
            GlobalLogs.Clear();
            foreach (var i in PChabit.App.Services.GlobalOpLog.Snapshot())
                GlobalLogs.Insert(0, i.Display);
            // 磁盘更早记录
            foreach (var line in PChabit.App.Services.GlobalOpLog.ReadTodayFile().Take(50))
            {
                if (!GlobalLogs.Contains(line))
                    GlobalLogs.Add(line);
            }
            OnPropertyChanged(nameof(HasGlobalLogs));
            await Task.CompletedTask;
        }
        catch { }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task CopyGlobalLogsAsync()
    {
        try
        {
            var text = PChabit.App.Services.GlobalOpLog.ExportToday();
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            AddLog("信息", "已复制全局操作日志到剪贴板");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AddLog("错误", "复制全局日志失败：" + ex.Message);
        }
    }
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
        IExportService exportService,
        IBookmarkSyncService bookmarkSyncService) : base()
    {
        _backupService = backupService;
        _settingsService = settingsService;
        _dbFactory = dbFactory;
        _webDAVSyncService = webDAVSyncService;
        _exportService = exportService;
        _bookmarkSyncService = bookmarkSyncService;
        Title = "数据管理";

        // 订阅全局操作日志（书签库推送等也会写入）
        try
        {
            foreach (var i in PChabit.App.Services.GlobalOpLog.Snapshot())
                GlobalLogs.Insert(0, i.Display);
            PChabit.App.Services.GlobalOpLog.Logged += OnGlobalOpLogged;
        }
        catch { }

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

        _browserSyncEnabled = settingsService.BrowserSyncEnabled;
        _browserBookmarkSyncEnabled = settingsService.BrowserBookmarkSyncEnabled;
        _browserSyncIntervalMinutes = settingsService.BrowserSyncIntervalMinutes;
        _browserHistoryIngestEnabled = settingsService.BrowserHistoryIngestEnabled;

        // 书签整理服务
        try { _tidyService = App.GetService<BookmarkTidyService>(); }
        catch { _tidyService = null; }
        try { _historySyncService = App.GetService<HistorySyncService>(); }
        catch { _historySyncService = null; }

        // 浏览器连接状态
        try
        {
            var wsHandler = App.GetService<BrowserSyncWebSocketHandler>();
            UpdateConnectedBrowsers(wsHandler.ReadyBrowsers);
            wsHandler.BrowserSyncReady += (_, e) =>
            {
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                {
                    UpdateConnectedBrowsers(wsHandler.ReadyBrowsers);
                });
            };
        }
        catch { /* handler 可能未注册 */ }

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
}

