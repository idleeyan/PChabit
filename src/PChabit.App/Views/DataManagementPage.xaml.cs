using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Services;
using Serilog;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PChabit.App.Views;

public sealed partial class DataManagementPage : Page
{
    public DataManagementViewModel ViewModel { get; }

    public DataManagementPage()
    {
        Log.Information("DataManagementPage: 1-构造函数开始");
        try
        {
            Log.Information("DataManagementPage: 2-InitializeComponent开始");
            InitializeComponent();
            Log.Information("DataManagementPage: 3-InitializeComponent完成");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DataManagementPage: InitializeComponent崩溃");
            throw;
        }

        try
        {
            Log.Information("DataManagementPage: 4-App.GetService开始");
            ViewModel = App.GetService<DataManagementViewModel>();
            Log.Information("DataManagementPage: 5-App.GetService完成");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DataManagementPage: App.GetService崩溃");
            throw;
        }

        try
        {
            Log.Information("DataManagementPage: 6-DataContext设置开始");
            DataContext = ViewModel;
            Log.Information("DataManagementPage: 7-DataContext设置完成");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DataManagementPage: DataContext崩溃");
            throw;
        }

        Log.Information("DataManagementPage: 8-构造函数完成");
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        try
        {
            // 缓存 DispatcherQueue：延续执行可能不在 UI 线程，必须在 await 前获取
            var dq = DispatcherQueue;

            await ViewModel.LoadBackupsAsync();
            await ViewModel.LoadOverviewAsync();

            // WinUI 3 中 async void 的 await 延续未必回到 UI 线程，
            // 必须显式调度到 UI 线程，否则设置控件属性会抛出 RPC_E_WRONG_THREAD
            dq.TryEnqueue(() =>
            {
                if (WebDAVUrlBox != null) WebDAVUrlBox.Text = ViewModel.WebDAVUrl;
                if (WebDAVUsernameBox != null) WebDAVUsernameBox.Text = ViewModel.WebDAVUsername;
                if (WebDAVPasswordBox != null) WebDAVPasswordBox.Password = ViewModel.WebDAVPassword;
                if (WebDAVEnabledBox != null) WebDAVEnabledBox.IsChecked = ViewModel.WebDAVEnabled;
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DataManagementPage.OnNavigatedTo 加载失败");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.SaveBackupSettings();
    }

    private async void RefreshOverview_Click(object sender, RoutedEventArgs e)
    {
        Log.Information("DM: RefreshOverview_Click 开始");
        try
        {
            Log.Information("DM: 开始加载概览+备份");
            await ViewModel.LoadOverviewAsync();
            Log.Information("DM: LoadOverviewAsync 完成");
            await ViewModel.LoadBackupsAsync();
            Log.Information("DM: LoadBackupsAsync 完成");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DM: RefreshOverview_Click 崩溃");
        }
        Log.Information("DM: RefreshOverview_Click 结束");
    }

    #region 本地备份

    private async void BrowseBackupPath_Click(object sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        folderPicker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(folderPicker, hwnd);

        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            ViewModel.BackupPath = folder.Path;
        }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BackupInfo backup)
            ViewModel.RestoreBackupCommand.Execute(backup);
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BackupInfo backup)
            ViewModel.DeleteBackupCommand.Execute(backup);
    }

    #endregion

    #region WebDAV

    private async void SaveWebDAVConfig_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.WebDAVUrl = WebDAVUrlBox?.Text ?? "";
        ViewModel.WebDAVUsername = WebDAVUsernameBox?.Text ?? "";
        ViewModel.WebDAVPassword = WebDAVPasswordBox?.Password ?? "";
        ViewModel.WebDAVEnabled = WebDAVEnabledBox?.IsChecked ?? false;
        await ViewModel.SaveWebDAVSettingsAsync();
    }

    private void RestoreCloud_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is WebDAVFileInfo file)
            ViewModel.RestoreCloudBackupCommand.Execute(file);
    }

    private void DeleteCloud_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is WebDAVFileInfo file)
            ViewModel.DeleteCloudBackupCommand.Execute(file);
    }

    #endregion
}
