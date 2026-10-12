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

    private void EditDescription_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 内联编辑：WinUI 3 同一时间只允许一个 ContentDialog 打开，
            // 详情弹窗内不能再 ShowAsync 第二个对话框（0x80000019），故改为卡片内切换编辑态。
            DescriptionEditor.Text = ViewModel.Description;
            DescriptionEditor.Visibility = Visibility.Visible;
            EditDescriptionActions.Visibility = Visibility.Visible;
            EditDescriptionButton.Visibility = Visibility.Collapsed;
            DescriptionText.Visibility = Visibility.Collapsed;
            DescriptionEmptyText.Visibility = Visibility.Collapsed;
            DescriptionEditor.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "进入编辑说明模式失败");
        }
    }

    private async void SaveDescription_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = await ViewModel.SaveDescriptionAsync(DescriptionEditor.Text);
            if (saved) ExitEditDescriptionMode();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存应用说明失败");
        }
    }

    private void CancelEditDescription_Click(object sender, RoutedEventArgs e)
    {
        ExitEditDescriptionMode();
    }

    private void ExitEditDescriptionMode()
    {
        DescriptionEditor.Visibility = Visibility.Collapsed;
        EditDescriptionActions.Visibility = Visibility.Collapsed;
        EditDescriptionButton.Visibility = Visibility.Visible;
        DescriptionText.Visibility = ViewModel.HasDescription ? Visibility.Visible : Visibility.Collapsed;
        DescriptionEmptyText.Visibility = ViewModel.HasDescription ? Visibility.Collapsed : Visibility.Visible;
    }
}
