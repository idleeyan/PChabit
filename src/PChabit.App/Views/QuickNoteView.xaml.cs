using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace PChabit.App.Views;

/// <summary>
/// 快速录入视图（3.24.0）：纸质便签外观；交互逻辑在 QuickNoteWindow。
/// 背景随所选颜色变化（浅色卡片），选中色点显示描边。
/// </summary>
public sealed partial class QuickNoteView : UserControl
{
    public event Action? SaveRequested;
    public event Action? HideRequested;
    public event Action? DraftChanged;
    public event Action<string>? ColorChanged;

    private static readonly SolidColorBrush YellowPaper = new(Windows.UI.Color.FromArgb(0xFF, 0xFE, 0xF3, 0xC4));
    private static readonly SolidColorBrush PinkPaper = new(Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xE4, 0xEC));
    private static readonly SolidColorBrush BluePaper = new(Windows.UI.Color.FromArgb(0xFF, 0xDD, 0xEC, 0xFB));
    private static readonly SolidColorBrush GreenPaper = new(Windows.UI.Color.FromArgb(0xFF, 0xE3, 0xF4, 0xE3));
    private static readonly SolidColorBrush GrayPaper = new(Windows.UI.Color.FromArgb(0xFF, 0xF1, 0xF1, 0xF1));
    private readonly SolidColorBrush _selectionBrush = new(Windows.UI.Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;

    public QuickNoteView()
    {
        InitializeComponent();
        RootGrid.Background = YellowPaper;
        MarkSelection("yellow");
    }

    public string Content
    {
        get => ContentBox.Text;
        set
        {
            if (ContentBox.Text != value) ContentBox.Text = value;
        }
    }

    public bool FocusEditor()
    {
        bool focused = ContentBox.Focus(FocusState.Programmatic);
        if (focused) ContentBox.SelectionStart = ContentBox.Text.Length;
        return focused;
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void ApplyColor(string colorKey)
    {
        RootGrid.Background = colorKey switch
        {
            "pink" => PinkPaper,
            "blue" => BluePaper,
            "green" => GreenPaper,
            "gray" => GrayPaper,
            _ => YellowPaper
        };
        MarkSelection(colorKey);
        ColorChanged?.Invoke(colorKey);
    }

    private void MarkSelection(string colorKey)
    {
        Border? dot = colorKey switch
        {
            "pink" => DotPink,
            "blue" => DotBlue,
            "green" => DotGreen,
            "gray" => DotGray,
            _ => DotYellow
        };
        foreach (var b in new[] { DotYellow, DotPink, DotBlue, DotGreen, DotGray })
            b.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x00, 0, 0, 0));
        dot.BorderBrush = _selectionBrush;
        dot.BorderThickness = new Thickness(2);
    }

    private void ContentBox_TextChanged(object sender, TextChangedEventArgs e) => DraftChanged?.Invoke();

    private void ContentBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // 热键呼出窗口后，XAML 的 GetKeyStateForCurrentThread 在部分时序下取不到 Ctrl
        // （Enter 被多行 TextBox 当成换行），并用 Win32 全局异步物理键状态兜底
        var ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

        if (e.Key == Windows.System.VirtualKey.Enter && ctrl)
        {
            e.Handled = true;
            SaveRequested?.Invoke();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            HideRequested?.Invoke();
        }
    }

    private void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SaveRequested?.Invoke();
    }

    private void ColorDot_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string color) ApplyColor(color);
    }
}
