using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace PChabit.App.Views;

/// <summary>「实时网速 · 进程排行」弹窗视图（外观与交互事件在 NetworkProcessWindow）。</summary>
public sealed partial class NetworkProcessView : UserControl
{
    /// <summary>用户按 Esc 关闭。</summary>
    public event Action? CloseRequested;

    public NetworkProcessView()
    {
        InitializeComponent();
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseRequested?.Invoke();
    }
}
