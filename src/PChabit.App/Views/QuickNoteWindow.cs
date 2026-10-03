using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PChabit.Core.Interfaces;
using Serilog;
using Windows.Graphics;
using WinRT.Interop;

namespace PChabit.App.Views;

/// <summary>
/// 全局热键呼出的快速录入窗（3.24.0）。单例复用（Hide 而非 Close）：
/// 无边框置顶小窗，定位在鼠标所在显示器；Ctrl+Enter 保存、Esc 隐藏留草稿、失焦 1.5s 自动保存。
/// 窗口操作与编辑均在 UI 线程（由 App 从热键回调派发后调用）。
/// </summary>
public sealed class QuickNoteWindow
{
    /// <summary>正式保存后通知 App（触发同步 debounce 等）。</summary>
    public event Action? NoteSaved;

    private const int LogicW = 420;
    private const int LogicH = 260;

    private readonly Window _window;
    private readonly QuickNoteView _view;
    private readonly IStickyNoteService _notes;
    private readonly ISettingsService _settings;
    private readonly AppWindow _appWindow;
    private readonly DispatcherQueueTimer _focusTimer;
    private readonly DispatcherQueueTimer _focusRetryTimer;
    private readonly DispatcherQueueTimer _draftTimer;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly string _draftPath;
    private string _color = "yellow";
    private bool _saving;
    private int _focusRetryCount;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointInterop lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointInterop { public int X; public int Y; }

    private sealed class DraftFile
    {
        public string Content { get; set; } = "";
        public string Color { get; set; } = "yellow";
    }

    public QuickNoteWindow(IStickyNoteService notes, ISettingsService settings)
    {
        _notes = notes;
        _settings = settings;
        _draftPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PChabit", "note-draft.json");

        _window = new Window();
        _view = new QuickNoteView();
        _window.Content = _view;

        var hwnd = WindowNative.GetWindowHandle(_window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        try { _appWindow.Title = "新建便签"; } catch { }

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        _appWindow.SetPresenter(presenter);

        // Alt+F4/系统关闭：拦截为隐藏，保证单例可复用
        _appWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            Hide();
        };

        _view.SaveRequested += OnSave;
        _view.HideRequested += () => Hide();
        _view.ColorChanged += c => _color = c;
        _view.DraftChanged += ScheduleDraftSave;

        var dq = _window.DispatcherQueue;
        _focusTimer = dq.CreateTimer();
        _focusTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _focusTimer.IsRepeating = false;
        _focusTimer.Tick += (_, _) => OnFocusLostSave();

        _draftTimer = dq.CreateTimer();
        _draftTimer.Interval = TimeSpan.FromMilliseconds(500);
        _draftTimer.IsRepeating = false;
        _draftTimer.Tick += (_, _) => PersistDraft();

        _statusTimer = dq.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, _) => _view.SetStatus("Ctrl+Enter 保存 · Esc 隐藏");

        // 窗口刚 Show 时 XAML 首帧布局未完成，Programmatic Focus 常返回 false，延迟重试若干次
        _focusRetryTimer = dq.CreateTimer();
        _focusRetryTimer.Interval = TimeSpan.FromMilliseconds(120);
        _focusRetryTimer.IsRepeating = true;
        _focusRetryCount = 0;
        _focusRetryTimer.Tick += (_, _) =>
        {
            if (_view.FocusEditor() || ++_focusRetryCount >= 6)
                _focusRetryTimer.Stop();
        };

        _window.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                if (_settings.StickyNotesQuickDismissOnFocusLost && !_saving)
                    _focusTimer.Start();
            }
            else
            {
                _focusTimer.Stop();
            }
        };
    }

    /// <summary>窗口当前是否可见（热键按下时已可见则仅聚焦）。</summary>
    public bool IsVisible => _appWindow.IsVisible;

    public void Show()
    {
        LoadDraft();
        PositionNearCursor();
        _appWindow.Show();
        // OverlappedPresenter.IsAlwaysOnTop 对无边框窗口在部分场景不生效，用 Win32 兜底
        var hwnd = WindowNative.GetWindowHandle(_window);
        // 无边框窗口首次 Show 后偶发 DWM 不合成（IsWindowVisible=true 但画面不出），SW_RESTORE 兜底
        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        ForceForeground(hwnd);
        _focusRetryCount = 0;
        if (!_view.FocusEditor())
            _focusRetryTimer.Start();
    }

    /// <summary>
    /// 热键回调经 DispatcherQueue 派发到 UI 线程后系统的前台授权已丢失，
    /// 直接 SetForegroundWindow 会被前台锁定拒绝。借用前台线程输入队列（AttachThreadInput）
    /// 是 Win32 经典解法，使快速窗稳定拿到键盘焦点（否则失焦即自动隐藏，文字根本输不进来）。
    /// </summary>
    private static void ForceForeground(IntPtr hwnd)
    {
        try
        {
            uint currentThread = GetCurrentThreadId();
            IntPtr foreHwnd = GetForegroundWindow();
            uint foreThread = foreHwnd != IntPtr.Zero
                ? GetWindowThreadProcessId(foreHwnd, IntPtr.Zero)
                : 0;

            if (foreThread != 0 && foreThread != currentThread)
                AttachThreadInput(currentThread, foreThread, true);
            SetForegroundWindow(hwnd);
            if (foreThread != 0 && foreThread != currentThread)
                AttachThreadInput(currentThread, foreThread, false);
        }
        catch { /* 焦点争取失败时窗口仍置顶可见，可点击后输入 */ }
    }

    public void Hide()
    {
        _focusTimer.Stop();
        _focusRetryTimer.Stop();
        try { PersistDraft(); } catch { }
        _appWindow.Hide();
    }

    private void PositionNearCursor()
    {
        GetCursorPos(out var pt);
        var area = DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Nearest);
        var hwnd = WindowNative.GetWindowHandle(_window);
        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        int w = (int)(LogicW * dpi / 96.0);
        int h = (int)(LogicH * dpi / 96.0);

        int x = pt.X + 12;
        int y = pt.Y + 20;
        if (x + w > area.WorkArea.X + area.WorkArea.Width - 8)
            x = area.WorkArea.X + area.WorkArea.Width - w - 8;
        if (y + h > area.WorkArea.Y + area.WorkArea.Height - 8)
            y = area.WorkArea.Y + area.WorkArea.Height - h - 8;
        if (x < area.WorkArea.X + 8) x = area.WorkArea.X + 8;

        _appWindow.Resize(new SizeInt32(w, h));
        _appWindow.Move(new PointInt32(x, y));
    }

    private async void OnFocusLostSave()
    {
        if (_saving) return;
        if (string.IsNullOrWhiteSpace(_view.Content))
        {
            Hide(); // 空白窗失焦直接隐藏，草稿保留（也是空）
            return;
        }
        await SaveAsync();
    }

    private async System.Threading.Tasks.Task SaveAsync()
    {
        if (_saving) return;
        var content = _view.Content.Trim();
        if (string.IsNullOrEmpty(content)) return;
        _saving = true;
        _focusTimer.Stop();
        try
        {
            await _notes.CreateAsync(content, _color);
            try { if (File.Exists(_draftPath)) File.Delete(_draftPath); } catch { }
            _view.SetStatus("✓ 已保存");
            _statusTimer.Start();
            NoteSaved?.Invoke();
            _view.Content = "";
            _color = "yellow";
            _view.ApplyColor("yellow");
            Hide();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[QuickNote] 保存失败");
            _view.SetStatus("保存失败，内容已保留");
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnSave() => _ = SaveAsync();

    private void ScheduleDraftSave()
    {
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private void PersistDraft()
    {
        try
        {
            // 空白内容不保留草稿文件（保存成功后也不会被重建）
            if (string.IsNullOrWhiteSpace(_view.Content))
            {
                if (File.Exists(_draftPath)) File.Delete(_draftPath);
                return;
            }
            var json = JsonSerializer.Serialize(new DraftFile { Content = _view.Content, Color = _color });
            File.WriteAllText(_draftPath, json);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[QuickNote] 草稿写入失败");
        }
    }

    private void LoadDraft()
    {
        try
        {
            if (File.Exists(_draftPath))
            {
                var draft = JsonSerializer.Deserialize<DraftFile>(File.ReadAllText(_draftPath));
                _view.Content = draft?.Content ?? "";
                _color = string.IsNullOrWhiteSpace(draft?.Color) ? "yellow" : draft!.Color;
                _view.ApplyColor(_color);
                if (!string.IsNullOrWhiteSpace(draft?.Content))
                    _view.SetStatus("已恢复上次草稿");
                return;
            }
        }
        catch { }
        _view.Content = "";
        _color = "yellow";
        _view.ApplyColor("yellow");
    }
}
