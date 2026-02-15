using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Collections.ObjectModel;
using System.Text.Json;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;
using Tai.Infrastructure.Services;

namespace Tai.App.ViewModels;

public partial class ExportPageViewModel : ObservableObject
{
    private readonly TaiDbContext _dbContext;
    private readonly ISettingsService _settingsService;
    private readonly IWebDAVSyncService _webDAVSyncService;
    
    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddDays(-7);
    
    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;
    
    [ObservableProperty]
    private string _statusMessage = "";
    
    [ObservableProperty]
    private bool _isExporting;
    
    [ObservableProperty]
    private bool _isSyncing;
    
    [ObservableProperty]
    private string _webDAVUrl = "";
    
    [ObservableProperty]
    private string _webDAVUsername = "";
    
    [ObservableProperty]
    private string _webDAVPassword = "";
    
    [ObservableProperty]
    private bool _webDAVEnabled;
    
    [ObservableProperty]
    private string _lastSyncTime = "从未同步";
    
    [ObservableProperty]
    private int _uploadProgress;
    
    [ObservableProperty]
    private int _downloadProgress;
    
    [ObservableProperty]
    private bool _isUploading;
    
    [ObservableProperty]
    private bool _isDownloading;
    
    [ObservableProperty]
    private string _currentOperation = "";
    
    [ObservableProperty]
    private string _remotePath = "";
    
    public ObservableCollection<WebDAVFileInfo> RemoteFiles { get; } = new();
    public ObservableCollection<WebDAVFileItem> SelectedRemoteFiles { get; } = new();
    public ObservableCollection<OperationLogItem> OperationLogs { get; } = new();
    
    public ExportPageViewModel(TaiDbContext dbContext, ISettingsService settingsService, IWebDAVSyncService webDAVSyncService)
    {
        _dbContext = dbContext;
        _settingsService = settingsService;
        _webDAVSyncService = webDAVSyncService;
        
        _webDAVSyncService.ProgressChanged += OnWebDAVProgressChanged;
        
        LoadWebDAVSettings();
    }
    
    private void OnWebDAVProgressChanged(object? sender, WebDAVProgressEventArgs e)
    {
        if (e.IsUpload)
        {
            UploadProgress = e.Progress;
        }
        else
        {
            DownloadProgress = e.Progress;
        }
        CurrentOperation = e.Status;
    }
    
    private void LoadWebDAVSettings()
    {
        WebDAVUrl = _settingsService.WebDAVUrl ?? "";
        WebDAVUsername = _settingsService.WebDAVUsername ?? "";
        WebDAVPassword = _settingsService.WebDAVPassword ?? "";
        WebDAVEnabled = _settingsService.WebDAVEnabled;
        
        if (_settingsService.WebDAVLastSync.HasValue)
        {
            LastSyncTime = _settingsService.WebDAVLastSync.Value.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
    
    public async Task SaveWebDAVSettingsAsync()
    {
        _settingsService.WebDAVUrl = WebDAVUrl;
        _settingsService.WebDAVUsername = WebDAVUsername;
        _settingsService.WebDAVPassword = WebDAVPassword;
        _settingsService.WebDAVEnabled = WebDAVEnabled;
        
        await _settingsService.SaveAsync();
    }
    
    [RelayCommand]
    private async Task TestWebDAVAsync()
    {
        if (string.IsNullOrWhiteSpace(WebDAVUrl))
        {
            StatusMessage = "请输入 WebDAV 地址";
            AddLog("错误", "请输入 WebDAV 地址");
            return;
        }
        
        StatusMessage = "正在测试连接...";
        AddLog("测试", "正在测试 WebDAV 连接...");
        
        try
        {
            var success = await _webDAVSyncService.TestConnectionAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword);
            
            if (success)
            {
                StatusMessage = "连接成功！";
                AddLog("成功", "WebDAV 连接测试成功");
                await SaveWebDAVSettingsAsync();
            }
            else
            {
                StatusMessage = "连接失败，请检查配置";
                AddLog("错误", "WebDAV 连接测试失败");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"连接异常: {ex.Message}";
            AddLog("错误", $"连接异常: {ex.Message}");
            Log.Error(ex, "WebDAV 测试连接失败");
        }
    }
    
    [RelayCommand]
    private async Task SyncToWebDAVAsync()
    {
        if (!WebDAVEnabled || string.IsNullOrWhiteSpace(WebDAVUrl))
        {
            StatusMessage = "请先配置并启用 WebDAV";
            AddLog("错误", "请先配置并启用 WebDAV");
            return;
        }
        
        if (IsSyncing) return;
        
        StatusMessage = "正在同步数据...";
        IsSyncing = true;
        AddLog("同步", "正在备份数据到云端...");
        
        try
        {
            var appSessions = await _dbContext.AppSessions.ToListAsync();
            var keyboardSessions = await _dbContext.KeyboardSessions.ToListAsync();
            var mouseSessions = await _dbContext.MouseSessions.ToListAsync();
            var webSessions = await _dbContext.WebSessions.ToListAsync();
            
            var exportData = new
            {
                ExportDate = DateTime.Now,
                AppSessions = appSessions,
                KeyboardSessions = keyboardSessions,
                MouseSessions = mouseSessions,
                WebSessions = webSessions
            };
            
            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            var fileName = $"pchabit_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var content = System.Text.Encoding.UTF8.GetBytes(json);
            
            var result = await _webDAVSyncService.UploadFileAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword, fileName, content);
            
            if (result != null)
            {
                _settingsService.WebDAVLastSync = DateTime.Now;
                await _settingsService.SaveAsync();
                LastSyncTime = _settingsService.WebDAVLastSync!.Value.ToString("yyyy-MM-dd HH:mm:ss");
                StatusMessage = $"同步成功！文件: {fileName}";
                AddLog("成功", $"数据同步成功: {fileName}");
            }
            else
            {
                StatusMessage = "同步失败，请检查配置";
                AddLog("错误", "数据同步失败");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"同步异常: {ex.Message}";
            AddLog("错误", $"同步异常: {ex.Message}");
            Log.Error(ex, "WebDAV 同步失败");
        }
        finally
        {
            IsSyncing = false;
        }
    }
    
    [RelayCommand]
    private async Task BrowseRemoteAsync()
    {
        if (string.IsNullOrWhiteSpace(WebDAVUrl))
        {
            StatusMessage = "请先配置 WebDAV";
            AddLog("错误", "请先配置 WebDAV");
            return;
        }
        
        StatusMessage = "正在浏览远程文件...";
        AddLog("浏览", "正在获取远程文件列表...");
        
        try
        {
            var files = await _webDAVSyncService.ListFilesAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword, RemotePath);
            
            RemoteFiles.Clear();
            SelectedRemoteFiles.Clear();
            
            foreach (var file in files)
            {
                RemoteFiles.Add(file);
            }
            
            StatusMessage = $"找到 {files.Count} 个项目";
            AddLog("成功", $"远程文件列表加载成功: {files.Count} 个项目");
        }
        catch (Exception ex)
        {
            StatusMessage = $"浏览失败: {ex.Message}";
            AddLog("错误", $"浏览失败: {ex.Message}");
            Log.Error(ex, "WebDAV 浏览失败");
        }
    }
    
    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        if (!SelectedRemoteFiles.Any())
        {
            StatusMessage = "请选择要下载的文件";
            AddLog("错误", "未选择任何文件");
            return;
        }
        
        IsDownloading = true;
        DownloadProgress = 0;
        
        var successCount = 0;
        var failCount = 0;
        
        var downloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PChabit", "WebDAV");
        Directory.CreateDirectory(downloadPath);
        
        foreach (var file in SelectedRemoteFiles.ToList())
        {
            if (file.IsDirectory) continue;
            
            CurrentOperation = $"正在下载: {file.Name}";
            AddLog("下载", $"开始下载: {file.Name}");
            
            try
            {
                var content = await _webDAVSyncService.DownloadFileWithProgressAsync(
                    WebDAVUrl, WebDAVUsername, WebDAVPassword, file.FullPath,
                    new Progress<int>(p => DownloadProgress = p));
                
                if (content != null)
                {
                    var localPath = Path.Combine(downloadPath, file.Name);
                    await File.WriteAllBytesAsync(localPath, content);
                    successCount++;
                    AddLog("成功", $"下载完成: {file.Name}");
                }
                else
                {
                    failCount++;
                    AddLog("错误", $"下载失败: {file.Name}");
                }
            }
            catch (Exception ex)
            {
                failCount++;
                AddLog("错误", $"下载异常: {file.Name} - {ex.Message}");
                Log.Error(ex, "下载文件失败: {FileName}", file.Name);
            }
        }
        
        IsDownloading = false;
        CurrentOperation = "";
        DownloadProgress = 0;
        
        StatusMessage = $"下载完成: 成功 {successCount} 个, 失败 {failCount} 个";
        AddLog("完成", $"下载完成: 成功 {successCount} 个, 失败 {failCount} 个");
    }
    
    public async Task UploadFilesAsync(IEnumerable<string> filePaths)
    {
        if (string.IsNullOrWhiteSpace(WebDAVUrl))
        {
            StatusMessage = "请先配置 WebDAV";
            AddLog("错误", "请先配置 WebDAV");
            return;
        }
        
        IsUploading = true;
        UploadProgress = 0;
        
        var files = filePaths.ToList();
        var totalFiles = files.Count;
        var successCount = 0;
        var failCount = 0;
        
        for (int i = 0; i < files.Count; i++)
        {
            var filePath = files[i];
            var fileName = Path.GetFileName(filePath);
            
            CurrentOperation = $"正在上传: {fileName} ({i + 1}/{totalFiles})";
            AddLog("上传", $"开始上传: {fileName}");
            
            try
            {
                var content = await File.ReadAllBytesAsync(filePath);
                
                var remoteFileName = fileName;
                if (!string.IsNullOrEmpty(RemotePath))
                {
                    remoteFileName = RemotePath.TrimStart('/') + "/" + fileName;
                }
                
                var result = await _webDAVSyncService.UploadFileWithProgressAsync(
                    WebDAVUrl, WebDAVUsername, WebDAVPassword, remoteFileName, content,
                    new Progress<int>(p => UploadProgress = p));
                
                if (result != null)
                {
                    successCount++;
                    AddLog("成功", $"上传完成: {fileName}");
                }
                else
                {
                    failCount++;
                    AddLog("错误", $"上传失败: {fileName}");
                }
            }
            catch (Exception ex)
            {
                failCount++;
                AddLog("错误", $"上传异常: {fileName} - {ex.Message}");
                Log.Error(ex, "上传文件失败: {FileName}", fileName);
            }
        }
        
        IsUploading = false;
        CurrentOperation = "";
        UploadProgress = 0;
        
        StatusMessage = $"上传完成: 成功 {successCount} 个, 失败 {failCount} 个";
        AddLog("完成", $"上传完成: 成功 {successCount} 个, 失败 {failCount} 个");
    }
    
    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (!SelectedRemoteFiles.Any())
        {
            StatusMessage = "请选择要删除的文件";
            AddLog("错误", "未选择任何文件");
            return;
        }
        
        var confirmMessage = $"确定要删除 {SelectedRemoteFiles.Count} 个文件吗？此操作不可恢复。";
        
        foreach (var file in SelectedRemoteFiles.ToList())
        {
            AddLog("删除", $"正在删除: {file.Name}");
            
            try
            {
                var success = await _webDAVSyncService.DeleteFileAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword, file.FullPath);
                
                if (success)
                {
                    RemoteFiles.Remove(file);
                    SelectedRemoteFiles.Remove(file);
                    AddLog("成功", $"删除成功: {file.Name}");
                }
                else
                {
                    AddLog("错误", $"删除失败: {file.Name}");
                }
            }
            catch (Exception ex)
            {
                AddLog("错误", $"删除异常: {file.Name} - {ex.Message}");
                Log.Error(ex, "删除文件失败: {FileName}", file.Name);
            }
        }
        
        StatusMessage = "删除操作完成";
        AddLog("完成", "批量删除操作完成");
    }
    
    [RelayCommand]
    private void ClearLogs()
    {
        OperationLogs.Clear();
    }
    
    private void AddLog(string type, string message)
    {
        var logItem = new OperationLogItem
        {
            Time = DateTime.Now,
            Type = type,
            Message = message
        };
        
        OperationLogs.Insert(0, logItem);
        
        if (OperationLogs.Count > 100)
        {
            OperationLogs.RemoveAt(OperationLogs.Count - 1);
        }
    }
    
    public async Task ExportAsync()
    {
        if (IsExporting) return;
        
        Log.Information("开始导出数据");
        IsExporting = true;
        StatusMessage = "正在导出...";
        AddLog("导出", "正在导出本地数据...");
        
        try
        {
            var appSessions = await _dbContext.AppSessions
                .Where(s => s.StartTime >= StartDate && s.StartTime <= EndDate.AddDays(1))
                .ToListAsync();
            
            var keyboardSessions = await _dbContext.KeyboardSessions
                .Where(s => s.Date >= StartDate && s.Date <= EndDate)
                .ToListAsync();
            
            var mouseSessions = await _dbContext.MouseSessions
                .Where(s => s.Date >= StartDate && s.Date <= EndDate)
                .ToListAsync();
            
            var webSessions = await _dbContext.WebSessions
                .Where(s => s.StartTime >= StartDate && s.StartTime <= EndDate.AddDays(1))
                .ToListAsync();
            
            var exportData = new
            {
                ExportDate = DateTime.Now,
                DateRange = new { Start = StartDate, End = EndDate },
                AppSessions = appSessions,
                KeyboardSessions = keyboardSessions,
                MouseSessions = mouseSessions,
                WebSessions = webSessions
            };
            
            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            var fileName = $"pchabit_export_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(documentsPath, "PChabit", fileName);
            
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await File.WriteAllTextAsync(filePath, json);
            
            StatusMessage = $"导出成功！文件保存到: {filePath}";
            AddLog("成功", $"本地导出完成: {filePath}");
            Log.Information("数据已导出到: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败: {ex.Message}";
            AddLog("错误", $"导出失败: {ex.Message}");
            Log.Error(ex, "导出数据失败");
        }
        finally
        {
            IsExporting = false;
        }
    }
    
    public async Task<List<WebDAVFileInfo>> LoadBackupFilesAsync()
    {
        try
        {
            var files = await _webDAVSyncService.ListFilesAsync(WebDAVUrl, WebDAVUsername, WebDAVPassword, null);
            return files;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载备份文件列表失败");
            throw;
        }
    }
    
    public async Task<bool> RestoreBackupAsync(string fileName, string displayName, Action<int>? progressCallback = null)
    {
        try
        {
            var progress = new Progress<int>(p => progressCallback?.Invoke(p));
            
            var content = await _webDAVSyncService.DownloadFileWithProgressAsync(
                WebDAVUrl, WebDAVUsername, WebDAVPassword, fileName, progress);
            
            if (content == null)
            {
                Log.Warning("下载备份文件失败: {FileName}", fileName);
                return false;
            }
            
            var json = System.Text.Encoding.UTF8.GetString(content);
            var backupData = System.Text.Json.JsonSerializer.Deserialize<BackupData>(json);
            
            if (backupData == null)
            {
                Log.Warning("解析备份文件失败: {FileName}", fileName);
                return false;
            }
            
            await RestoreDataAsync(backupData);
            
            Log.Information("备份恢复成功: {FileName}", displayName);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "恢复备份失败: {FileName}", fileName);
            return false;
        }
    }
    
    private async Task RestoreDataAsync(BackupData data)
    {
        if (data.AppSessions != null)
        {
            var existingApps = await _dbContext.AppSessions.ToListAsync();
            _dbContext.AppSessions.RemoveRange(existingApps);
            await _dbContext.AppSessions.AddRangeAsync(data.AppSessions);
        }
        
        if (data.KeyboardSessions != null)
        {
            var existingKeys = await _dbContext.KeyboardSessions.ToListAsync();
            _dbContext.KeyboardSessions.RemoveRange(existingKeys);
            await _dbContext.KeyboardSessions.AddRangeAsync(data.KeyboardSessions);
        }
        
        if (data.MouseSessions != null)
        {
            var existingMice = await _dbContext.MouseSessions.ToListAsync();
            _dbContext.MouseSessions.RemoveRange(existingMice);
            await _dbContext.MouseSessions.AddRangeAsync(data.MouseSessions);
        }
        
        if (data.WebSessions != null)
        {
            var existingWeb = await _dbContext.WebSessions.ToListAsync();
            _dbContext.WebSessions.RemoveRange(existingWeb);
            await _dbContext.WebSessions.AddRangeAsync(data.WebSessions);
        }
        
        await _dbContext.SaveChangesAsync();
    }
}

public class BackupData
{
    public DateTime ExportDate { get; set; }
    public List<Core.Entities.AppSession>? AppSessions { get; set; }
    public List<Core.Entities.KeyboardSession>? KeyboardSessions { get; set; }
    public List<Core.Entities.MouseSession>? MouseSessions { get; set; }
    public List<Core.Entities.WebSession>? WebSessions { get; set; }
}

public class WebDAVFileItem : WebDAVFileInfo
{
    public bool IsSelected { get; set; }
}

public class BackupFileDisplay
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long Size { get; set; }
    public DateTime? LastModified { get; set; }
    public bool IsDirectory { get; set; }
    
    public string SizeText
    {
        get
        {
            if (IsDirectory) return "文件夹";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            return $"{Size / 1024.0 / 1024.0:F1} MB";
        }
    }
    
    public string LastModifiedText => LastModified?.ToString("yyyy-MM-dd HH:mm") ?? "";
}

public class OperationLogItem
{
    public DateTime Time { get; init; }
    public string Type { get; init; } = "";
    public string Message { get; init; } = "";
    
    public string TimeString => Time.ToString("HH:mm:ss");
}
