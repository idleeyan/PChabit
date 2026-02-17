using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using PChabit.App.ViewModels;
using PChabit.Infrastructure.Services;

namespace PChabit.App.Views;

public sealed partial class ExportPage : Page
{
    public ExportPageViewModel ViewModel { get; }
    public ObservableCollection<BackupFileDisplay> BackupFiles { get; } = new();
    
    public ExportPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<ExportPageViewModel>();
        DataContext = ViewModel;
        
        Loaded += ExportPage_Loaded;
        
        LogList.ItemsSource = ViewModel.OperationLogs;
        BackupFileList.ItemsSource = BackupFiles;
    }
    
    private void ExportPage_Loaded(object sender, RoutedEventArgs e)
    {
        WebDAVUrlBox.Text = ViewModel.WebDAVUrl;
        WebDAVUsernameBox.Text = ViewModel.WebDAVUsername;
        WebDAVPasswordBox.Password = ViewModel.WebDAVPassword;
        WebDAVEnabledBox.IsChecked = ViewModel.WebDAVEnabled;
        LastSyncText.Text = $"上次同步: {ViewModel.LastSyncTime}";
        
        ViewModel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.StatusMessage))
            {
                StatusText.Text = ViewModel.StatusMessage;
            }
            
            if (args.PropertyName == nameof(ViewModel.LastSyncTime))
            {
                LastSyncText.Text = $"上次同步: {ViewModel.LastSyncTime}";
            }
        };
    }
    
    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportAsync();
    }
    
    private async void TestWebDAVButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateWebDAVSettingsFromUI();
        await ViewModel.TestWebDAVCommand.ExecuteAsync(null);
    }
    
    private async void SaveConfigButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateWebDAVSettingsFromUI();
        await ViewModel.SaveWebDAVSettingsAsync();
        StatusText.Text = "配置已保存";
    }
    
    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateWebDAVSettingsFromUI();
        await ViewModel.SyncToWebDAVCommand.ExecuteAsync(null);
    }
    
    private async void RefreshBackupsButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateWebDAVSettingsFromUI();
        
        if (string.IsNullOrWhiteSpace(ViewModel.WebDAVUrl))
        {
            StatusText.Text = "请先配置 WebDAV";
            return;
        }
        
        StatusText.Text = "正在加载备份列表...";
        AddLog("加载", "正在获取云端备份列表...");
        
        try
        {
            var files = await ViewModel.LoadBackupFilesAsync();
            
            BackupFiles.Clear();
            foreach (var file in files)
            {
                if (!file.IsDirectory && file.Name.EndsWith(".json"))
                {
                    BackupFiles.Add(new BackupFileDisplay
                    {
                        Name = file.Name,
                        FullPath = file.FullPath,
                        Size = file.Size,
                        LastModified = file.LastModified,
                        IsDirectory = file.IsDirectory
                    });
                }
            }
            
            StatusText.Text = $"找到 {BackupFiles.Count} 个备份文件";
            AddLog("成功", $"备份列表加载成功: {BackupFiles.Count} 个文件");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载失败: {ex.Message}";
            AddLog("错误", $"加载失败: {ex.Message}");
        }
    }
    
    private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedFile = BackupFileList.SelectedItem as BackupFileDisplay;
        
        if (selectedFile == null)
        {
            StatusText.Text = "请先选择一个备份文件";
            return;
        }
        
        var confirmDialog = new ContentDialog();
        confirmDialog.Title = "确认恢复";
        confirmDialog.Content = $"确定要从云端恢复备份: {selectedFile.Name} 吗？\n\n这将覆盖当前的本地数据。";
        confirmDialog.PrimaryButtonText = "恢复";
        confirmDialog.CloseButtonText = "取消";
        
        var result = await confirmDialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary)
        {
            StatusText.Text = "正在恢复备份...";
            DownloadStatusText.Text = "正在下载...";
            AddLog("恢复", $"开始恢复备份: {selectedFile.Name}");
            
            try
            {
                var success = await ViewModel.RestoreBackupAsync(selectedFile.FullPath, selectedFile.Name, progress =>
                {
                    DownloadProgressBar.Value = progress;
                    DownloadStatusText.Text = $"下载中... {progress}%";
                });
                
                if (success)
                {
                    StatusText.Text = "备份恢复成功！";
                    DownloadStatusText.Text = "恢复完成";
                    AddLog("成功", $"备份恢复成功: {selectedFile.Name}");
                }
                else
                {
                    StatusText.Text = "备份恢复失败";
                    DownloadStatusText.Text = "恢复失败";
                    AddLog("错误", "备份恢复失败");
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"恢复异常: {ex.Message}";
                DownloadStatusText.Text = "恢复异常";
                AddLog("错误", $"恢复异常: {ex.Message}");
            }
            
            DownloadProgressBar.Value = 0;
        }
    }
    
    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearLogsCommand.Execute(null);
    }
    
    private void UpdateWebDAVSettingsFromUI()
    {
        ViewModel.WebDAVUrl = WebDAVUrlBox.Text ?? "";
        ViewModel.WebDAVUsername = WebDAVUsernameBox.Text ?? "";
        ViewModel.WebDAVPassword = WebDAVPasswordBox.Password ?? "";
        ViewModel.WebDAVEnabled = WebDAVEnabledBox.IsChecked ?? false;
    }
    
    private void AddLog(string type, string message)
    {
        ViewModel.OperationLogs.Insert(0, new OperationLogItem
        {
            Time = DateTime.Now,
            Type = type,
            Message = message
        });
    }
}
