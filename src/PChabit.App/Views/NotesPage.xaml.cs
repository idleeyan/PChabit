using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;
using Serilog;

namespace PChabit.App.Views;

public sealed partial class NotesPage : Page
{
    public NotesViewModel ViewModel { get; }
    private readonly IServiceScope _scope;
    private bool _editOpened;

    public NotesPage()
    {
        InitializeComponent();
        _scope = App.Services.CreateScope();
        ViewModel = _scope.ServiceProvider.GetRequiredService<NotesViewModel>();
        DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Subscribe();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Unsubscribe();
        _scope.Dispose();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.OnSearchTextChanged();

    private static Guid TagId(object sender) =>
        sender is FrameworkElement fe && fe.Tag is Guid g ? g : Guid.Empty;

    private void CardPin_Click(object sender, RoutedEventArgs e) => _ = ViewModel.TogglePinCommand.ExecuteAsync(TagId(sender));
    private void CardColor_Click(object sender, RoutedEventArgs e) => _ = ViewModel.CycleColorCommand.ExecuteAsync(TagId(sender));
    private void CardArchive_Click(object sender, RoutedEventArgs e) => _ = ViewModel.ArchiveOrUnarchiveCommand.ExecuteAsync(TagId(sender));
    private void CardRestore_Click(object sender, RoutedEventArgs e) => _ = ViewModel.RestoreCommand.ExecuteAsync(TagId(sender));

    private void CardDelete_Click(object sender, RoutedEventArgs e)
    {
        var id = TagId(sender);
        if (id == Guid.Empty) return;
        if (ViewModel.IsTrashView)
            _ = PurgeWithConfirmAsync(id);
        else
            _ = ViewModel.DeleteCommand.ExecuteAsync(id);
    }

    private async void NotesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel.IsTrashView) return; // 回收站只提供还原/彻底删除，不进入编辑
        if (e.ClickedItem is NoteItemViewModel item)
            await OpenEditAsync(item.Id);
    }

    private async System.Threading.Tasks.Task OpenEditAsync(Guid id)
    {
        if (_editOpened) return;
        await ViewModel.BeginEditAsync(id);
        if (ViewModel.EditingNote == null) return;
        _editOpened = true;
        try
        {
            await EditDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 编辑对话框打开失败");
        }
        finally
        {
            _editOpened = false;
        }
    }

    private async void EditDialog_Primary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 空内容不允许保存（提示并保持打开）
        if (string.IsNullOrWhiteSpace(ViewModel.EditContent))
        {
            args.Cancel = true;
            return;
        }
        await ViewModel.SaveEditCommand.ExecuteAsync(null);
    }

    private async void EditDialog_Delete(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var editing = ViewModel.EditingNote;
        if (editing == null) return;
        // 新建的空白便签：物理清理；其余移入回收站
        if (string.IsNullOrWhiteSpace(ViewModel.EditContent) && editing.Source.Version <= 1)
            await ViewModel.PurgeCommand.ExecuteAsync(editing.Id);
        else
            await ViewModel.DeleteCommand.ExecuteAsync(editing.Id);
        ViewModel.CancelEdit();
    }

    private async void EditDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // 保存/删除路径已将 EditingNote 置空；此处仅兜底「取消/遮罩」——新建的空便签物理清理
        var editing = ViewModel.EditingNote;
        if (editing == null) return;
        bool blank = string.IsNullOrWhiteSpace(ViewModel.EditContent) && editing.Source.Version <= 1;
        var id = editing.Id;
        ViewModel.CancelEdit();
        if (blank)
            await ViewModel.PurgeCommand.ExecuteAsync(id);
    }

    private void ColorPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string color)
            ViewModel.EditColor = color;
    }

    private async void EditBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Ctrl+Enter 保存并关闭
        if (e.Key == Windows.System.VirtualKey.Enter && InputKeyboardType())
        {
            e.Handled = true;
            if (string.IsNullOrWhiteSpace(ViewModel.EditContent)) return;
            await ViewModel.SaveEditCommand.ExecuteAsync(null);
            EditDialog.Hide();
        }
    }

    private static bool InputKeyboardType() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private async void EmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        ConfirmDialog.Title = "清空回收站？";
        ConfirmDialog.Content = "回收站中的全部便签将被彻底删除，且无法通过云同步找回。";
        var r = await ConfirmDialog.ShowAsync();
        if (r == ContentDialogResult.Primary)
            await ViewModel.EmptyTrashCommand.ExecuteAsync(null);
    }

    private async System.Threading.Tasks.Task PurgeWithConfirmAsync(Guid id)
    {
        ConfirmDialog.Title = "彻底删除这条便签？";
        ConfirmDialog.Content = "删除后不可恢复，其他设备同步后也会移除。";
        var r = await ConfirmDialog.ShowAsync();
        if (r == ContentDialogResult.Primary)
            await ViewModel.PurgeCommand.ExecuteAsync(id);
    }
}
