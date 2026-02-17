using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;
using PChabit.Core.Interfaces;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PChabit.App.Views;

public sealed partial class BackupManagementPage : Page
{
    public BackupManagementViewModel ViewModel { get; }

    public BackupManagementPage()
    {
        ViewModel = App.GetService<BackupManagementViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadBackupsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.SaveSettings();
    }

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
        {
            ViewModel.RestoreBackupCommand.Execute(backup);
        }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BackupInfo backup)
        {
            ViewModel.DeleteBackupCommand.Execute(backup);
        }
    }
}
