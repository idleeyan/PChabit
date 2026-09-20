using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class AppDetailDialog : ContentDialog
{
    public AppDetailViewModel ViewModel { get; }
    private readonly Action<string>? _onChangeCategory;

    public AppDetailDialog(AppDetailViewModel viewModel, Action<string>? onChangeCategory = null)
    {
        ViewModel = viewModel;
        _onChangeCategory = onChangeCategory;
        InitializeComponent();
        SecondaryButtonClick += OnCopyClick;
    }

    private async void OnCopyClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        ViewModel.CopySummaryCommand.Execute(null);
        await Task.CompletedTask;
    }

    private void ChangeCategory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var process = ViewModel.ProcessText;
            if (string.IsNullOrEmpty(process)) return;
            Hide();
            _onChangeCategory?.Invoke(process);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "应用详情改分类失败");
        }
    }
}
