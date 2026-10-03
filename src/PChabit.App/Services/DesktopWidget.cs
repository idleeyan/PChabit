using System.Runtime.InteropServices;
using Serilog;

namespace PChabit.App.Services;

/// <summary>桌面悬浮窗一行：短标签 + 着色文本段（数值/温度）+ 可选迷你进度条。</summary>
public sealed record DesktopWidgetRow(
    string Label,
    IReadOnlyList<DesktopWidgetSegment> Segments,
    float? Fraction,
    TaskbarMetricColor BarColor = TaskbarMetricColor.Safe);

/// <summary>行内文本段（同一段同一种颜色，用于 "23%  42°" 负载/温度分别着色）。</summary>
public sealed record DesktopWidgetSegment(string Text, TaskbarMetricColor Color = TaskbarMetricColor.Safe);

/// <summary>悬浮窗启动/位置/尺寸参数。Screen 为显示器 DeviceName（\\.\DISPLAY1），Left/Top/Width/Height=-1 表示未保存。</summary>
public sealed record DesktopWidgetOptions(
    bool Topmost,
    bool ClickThrough,
    string? Screen,
    int Left,
    int Top,
    int Width = -1,
    int Height = -1);

/// <summary>
/// 桌面硬件信息悬浮窗（桌面小组件）。
/// 纯 Win32 分层窗口（不依赖 WinForms/WinUI 窗口）：3.23.2 起渲染为 WS_EX_LAYERED +
/// UpdateLayeredWindow(ULW_ALPHA) per-pixel alpha（GDI+ 32bpp PARGB，见 DesktopWidget.Render.cs）；
/// 3.23.0~3.23.1 的 LWA_COLORKEY 色键方案已淘汰（无法半透明阴影、AA 边缘带色键杂边）。
/// 行为规格照抄 LiteMonitor（MIT）MainForm_Transparent + MainFormBizHelper + MainFormWinHelper：
/// WS_EX_TOOLWINDOW 无任务栏按钮、WS_EX_TOPMOST 置顶（3s 强制重插 + 10s 轮询维护）、
/// WS_EX_TRANSPARENT+WS_EX_LAYERED 双位鼠标穿透、手动 offset 拖拽、MouseUp 时 ClampToScreen + SavePos、
/// ScreenDevice + 坐标位置记忆、WM_DISPLAYCHANGE 500ms 防抖恢复。
/// 3.23.1 增强：非穿透态右下/右/底边可调整大小（WM_NCHITTEST 原生 sizing），尺寸记忆；
/// 内容（字体/行高/药丸进度条）按窗口宽度等比缩放，拖大窗口即变大变清晰。
/// 所有窗口消息均由创建线程（UI 线程）的消息泵派发，禁止跨线程调用本类成员。
/// </summary>
public sealed partial class DesktopWidget
{
    // ---------- 窗口样式常量 ----------
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_USERDATA = -21;
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_DESTROY = 0x0002;
    private const int WM_TIMER = 0x0113;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_DPICHANGED = 0x02E3;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_SIZE = 0x0005;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int MK_LBUTTON = 0x0001;
    // 命中测试返回值（让系统接管原生 resize：光标、边界跟踪、最小跟踪尺寸）
    private const int HTCLIENT = 1;
    private const int HTRIGHT = 11;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMRIGHT = 17;
    private const int CS_DBLCLKS = 0x0008;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const uint MONITOR_DEFAULTTONULL = 0x00000000;
    private const int IDT_TOPFIRST = 1001;   // 启动 3s 后强制重插置顶（照抄上游 OnShown 3s 延迟）
    private const int IDT_TOPPOLL = 1002;    // 10s 轮询维护置顶样式（句柄重建后会丢）
    private const int IDT_DISPLAY = 1003;    // WM_DISPLAYCHANGE 500ms 防抖（SetTimer 重置即防抖）

    // ---------- 布局（逻辑像素，按 DPI × 用户缩放 _userScale；3.23.1 默认尺寸加大解决小字模糊）----------
    private const int BaseWidth = 272;    // 缩放基准宽：实际逻辑宽 / BaseWidth = _userScale
    private const int MinLogicalW = 196; // 最小跟踪宽（逻辑）
    private const int MinLogicalH = 118; // 最小跟踪高（逻辑）
    private const int Pad = 14;
    private const int PadV = 12;
    private const int TitleH = 26;
    private const int RowH = 26;
    private const int LabelW = 48;
    private const int Gap = 9;
    private const int BarH = 7;
    private const int Edge = 6;          // 右/底边 resize 热区（逻辑像素）
    private const int Corner = 17;       // 右下角斜向 resize 热区
    private const float MinScale = 0.78f;
    private const float MaxScale = 2.4f;

    // ---------- Win32 结构 ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    // ---------- Win32 P/Invoke（W 后缀必须显式 CharSet.Unicode，3.9.2 乱码教训）----------
    [DllImport("user32.dll")] private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetCapture(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hWnd, IntPtr nIDEvent);

    // GDI+ per-pixel alpha 渲染所需 P/Invoke（BeginPaint/UpdateLayeredWindow/CreateDIBSection 等）见 DesktopWidget.Render.cs

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);
    private static readonly WndProcDelegate WndProcHandler = WndProc;
    private const string ClassName = "PChabitDesktopWidget";
    private static bool _classRegistered;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    // ---------- 状态 ----------
    private IntPtr _hwnd;
    private GCHandle _gcHandle;
    private uint _dpi = 96;
    private float _userScale = 1f;       // 用户拖宽窗口驱动的内容缩放（1 = BaseWidth 基准）
    private bool _heightUserSet;         // 用户是否手动拖过高度（拖过后行数变化只在内容超高时扩高）
    private bool _light;
    private bool _topmost;
    private bool _clickThrough;
    private bool _clampToScreen = true;
    private bool _dragging;
    private bool _moved;
    private POINT _dragOffset;
    private RECT _sizeMoveStart;         // 系统 sizing/move loop 起点矩形（判断宽/高是否真的变化）
    private IReadOnlyList<DesktopWidgetRow> _rows = Array.Empty<DesktopWidgetRow>();
    /// <summary>3.24.0：上一拍内容签名，相同则跳过重绘（null=尚未提交过，首帧必画）。</summary>
    private string? _contentSig;
    private bool _disposed;

    /// <summary>双击悬浮窗（UI 线程触发）。PChabit 动作为打开主窗口硬件监控页。</summary>
    public event Action? DoubleClicked;

    /// <summary>拖拽/缩放结束保存位置与尺寸（UI 线程触发）：ScreenDevice + 屏幕坐标 + 物理宽高。</summary>
    public event Action<string, int, int, int, int>? LocationSaved;

    public DesktopWidget()
    {
        _gcHandle = GCHandle.Alloc(this);
    }

    /// <summary>窗口是否已创建并有效。</summary>
    public bool IsRunning => _hwnd != IntPtr.Zero && IsWindow(_hwnd);

    /// <summary>按 DPI × 用户缩放换算像素（布局/字体随窗口宽度缩放）。</summary>
    private int S(int v) => (int)Math.Round(v * _dpi / 96.0 * _userScale);

    /// <summary>仅按 DPI 换算（resize 热区/最小尺寸等不随用户缩放变化的量）。</summary>
    private int SP(int v) => (int)Math.Round(v * _dpi / 96.0);

    /// <summary>按当前物理客户宽反推用户缩放并钳制。返回是否发生变化。</summary>
    private bool UpdateScaleFromWidth(int physicalWidth)
    {
        float logical = physicalWidth * 96f / _dpi;
        float scale = Math.Clamp(logical / BaseWidth, MinScale, MaxScale);
        if (Math.Abs(scale - _userScale) < 0.005f) return false;
        _userScale = scale;
        return true;
    }

    /// <summary>当前行内容所需物理高（标题 + N 行 + 上下边距）。</summary>
    private int ContentHeight() => S(PadV * 2 + TitleH + RowH * _rows.Count);

    /// <summary>
    /// 创建独立悬浮窗（UI 线程调用，幂等）。已有效则直接返回；句柄失效则清掉重建。
    /// 创建后立即按 options 恢复位置/置顶/穿透，并启动置顶维护计时器。
    /// </summary>
    public void Start(DesktopWidgetOptions options)
    {
        if (_disposed) return;
        try
        {
            _topmost = options.Topmost;
            _clickThrough = options.ClickThrough;

            if (_hwnd != IntPtr.Zero)
            {
                if (IsWindow(_hwnd)) return;
                Log.Information("DesktopWidget 句柄失效，重建悬浮窗");
                try { DestroyWindow(_hwnd); } catch { /* 已失效 */ }
                _hwnd = IntPtr.Zero;
            }

            _dpi = GetDpiForSystem();
            if (_dpi == 0) _dpi = 96;
            int defaultW = S(BaseWidth);
            int defaultH = S(PadV * 2 + TitleH + RowH * 7); // 首次按 7 行占位，UpdateContent 立即校正
            int w = options.Width > 0 ? options.Width : defaultW;
            int h = options.Height > 0 ? options.Height : defaultH;
            // 保存尺寸恢复时同步缩放与"用户已定高度"状态
            UpdateScaleFromWidth(w);
            if (options.Height > 0) _heightUserSet = true;

            RegisterClass();
            uint ex = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (options.Topmost) ex |= WS_EX_TOPMOST;
            if (options.ClickThrough) ex |= WS_EX_TRANSPARENT;
            _hwnd = CreateWindowExW(ex, ClassName, "", WS_POPUP,
                0, 0, w, h, IntPtr.Zero, IntPtr.Zero,
                Marshal.GetHINSTANCE(typeof(DesktopWidget).Module), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Log.Warning("DesktopWidget 窗口创建失败");
                return;
            }

            SetWindowLongPtrW(_hwnd, GWLP_USERDATA, (IntPtr)_gcHandle);

            RestorePos(options.Screen, options.Left, options.Top, w, h);
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            Render();   // 首次提交 per-pixel alpha 层（分层窗口首次 UpdateLayeredWindow 前内容为空）

            // 置顶维护（照抄 MainFormWinHelper）：3s 强制重插一次，之后 10s 轮询补样式
            SetTimer(_hwnd, (IntPtr)IDT_TOPFIRST, 3000, IntPtr.Zero);
            SetTimer(_hwnd, (IntPtr)IDT_TOPPOLL, 10000, IntPtr.Zero);
            Log.Information("DesktopWidget 已创建 topmost={Topmost} clickThrough={ClickThrough}", options.Topmost, options.ClickThrough);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DesktopWidget.Start 失败");
        }
    }

    /// <summary>限制拖出屏幕开关（拖拽结束时生效）。</summary>
    public void SetClampToScreen(bool enabled) => _clampToScreen = enabled;

    /// <summary>切换置顶（设置变更即时生效；值未变不重插 Z 序，样式丢失由 10s 轮询补）。</summary>
    public void ApplyTopMost(bool enabled)
    {
        if (_topmost == enabled) return;
        _topmost = enabled;
        if (_hwnd == IntPtr.Zero) return;
        RefreshTopMost(enabled, forceReinsert: true);
    }

    /// <summary>切换鼠标穿透：WS_EX_TRANSPARENT 必须与 WS_EX_LAYERED 同时在位（本窗口 LAYERED 恒在）。</summary>
    public void ApplyClickThrough(bool enabled)
    {
        if (_clickThrough == enabled) return;
        _clickThrough = enabled;
        if (_hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtrW(_hwnd, GWL_EXSTYLE).ToInt64();
        ex = enabled ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, (IntPtr)ex);
    }

    /// <summary>深浅主题切换（相同则跳过；重绘走 per-pixel alpha，无需色键重设）。</summary>
    public void SetTheme(bool light)
    {
        if (_light == light) return;
        _light = light;
        if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    /// <summary>更新多行内容（UI 线程，建议 1s 节流）。行数变化时按规则校正窗口高度。</summary>
    public void UpdateContent(IReadOnlyList<DesktopWidgetRow> rows)
    {
        if (_hwnd == IntPtr.Zero) return;

        uint dpi = GetDpiForWindow(_hwnd);
        bool dpiChanged = dpi != 0 && dpi != _dpi;

        // 3.24.0 性能优化：内容签名（文字/颜色/量化后的条比例）与上一拍相同且 DPI 未变 → 跳过重绘。
        // 空闲时数值文本不变，每秒一次的 GDI+ 全表面重绘（约 30 次 DrawString）可整体省掉。
        string sig = BuildContentSignature(rows);
        if (!dpiChanged && sig == _contentSig)
        {
            _rows = rows;
            return;
        }
        _contentSig = sig;
        _rows = rows;

        try
        {
            if (dpiChanged)
            {
                _dpi = dpi;
                EnsureFonts(force: true);
            }
            if (GetWindowRect(_hwnd, out RECT r))
            {
                int curW = r.Right - r.Left;
                int curH = r.Bottom - r.Top;
                UpdateScaleFromWidth(curW);
                int needH = ContentHeight();
                // 用户未手动定高：高度始终贴合内容；手动定过：仅内容超出时扩高，减少不缩回
                int targetH = !_heightUserSet ? needH : Math.Max(curH, needH);
                if (targetH != curH)
                {
                    // 注意：SWP_NOMOVE 只屏蔽 x/y，cx 无 SWP_NOSIZE 时会生效——必须显式传当前宽，不能传 0
                    SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, curW, targetH, SWP_NOMOVE | SWP_NOACTIVATE);
                    ClampToScreen(force: false);
                }
            }
            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DesktopWidget.UpdateContent 失败");
        }
    }

    /// <summary>
    /// 内容签名：标签/文本段/颜色 + 进度条比例（2 位小数量化，亚像素变化不触发重绘）。
    /// </summary>
    private static string BuildContentSignature(IReadOnlyList<DesktopWidgetRow> rows)
    {
        var sb = new System.Text.StringBuilder(rows.Count * 24);
        foreach (var row in rows)
        {
            sb.Append(row.Label).Append('@');
            foreach (var seg in row.Segments)
                sb.Append(seg.Text).Append('#').Append((byte)seg.Color).Append(';');
            sb.Append(row.Fraction.HasValue ? row.Fraction.Value.ToString("F2") : "-").Append('^')
              .Append((byte)row.BarColor).Append('|');
        }
        return sb.ToString();
    }

    // ---------- 置顶维护（照抄 MainFormWinHelper.RefreshTopMost）----------

    private bool IsTopMostStyleApplied()
    {
        if (_hwnd == IntPtr.Zero) return false;
        return (GetWindowLongPtrW(_hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;
    }

    private void RefreshTopMost(bool enabled, bool forceReinsert)
    {
        if (_hwnd == IntPtr.Zero) return;
        uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
        if (enabled)
        {
            // 仅在强制重插时先脱离置顶再插回，避免轮询频繁抢其它置顶窗口的 Z 序
            if (forceReinsert) SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, flags);
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, flags);
        }
        else
        {
            SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        }
    }

    // ---------- 位置管理（照抄 MainFormBizHelper SavePos/RestorePos/ClampToScreen）----------

    /// <summary>保存当前位置与尺寸：窗口中心点所在屏 DeviceName + 左上角屏幕坐标 + 物理宽高。</summary>
    private void SavePos()
    {
        if (_hwnd == IntPtr.Zero || !GetWindowRect(_hwnd, out RECT r)) return;
        var center = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        IntPtr hMon = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        string device = MonitorDevice(hMon);
        ClampToScreen(force: false);
        GetWindowRect(_hwnd, out RECT c);
        try
        {
            LocationSaved?.Invoke(device, c.Left, c.Top, c.Right - c.Left, c.Bottom - c.Top);
        }
        catch (Exception ex) { Log.Warning(ex, "DesktopWidget LocationSaved 回调异常"); }
    }

    /// <summary>
    /// 恢复位置：按保存的 ScreenDevice 找屏并在其工作区内 clamp；
    /// 屏幕不存在/未保存位置时回默认锚点（右侧居中偏上，距右缘 50px）。
    /// </summary>
    private void RestorePos(string? screen, int left, int top, int w, int h)
    {
        IntPtr hMon = IntPtr.Zero;
        if (!string.IsNullOrEmpty(screen)) hMon = FindMonitor(screen);

        RECT area;
        int x, y;
        if (hMon != IntPtr.Zero)
        {
            area = WorkArea(hMon);
            x = left;
            y = top;
        }
        else
        {
            // 保存的屏幕已拔掉或从未保存：窗口中心当前所在屏（创建初始即主屏）
            hMon = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST);
            area = WorkArea(hMon);
            x = area.Right - w - 50;
            y = area.Top + (area.Bottom - area.Top - h) / 2;
        }

        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - w));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - h));
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// 拖后拉回（仅在窗口完全跑出工作区时）。照抄上游修复：贴边 +1px 不弹开，
    /// 否则会挡住靠边场景（自动隐藏为 P1，本修复语义先行保留）。
    /// </summary>
    private void ClampToScreen(bool force)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!_clampToScreen && !force) return;
        if (!GetWindowRect(_hwnd, out RECT r)) return;
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        var center = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        RECT area = WorkArea(MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST));
        int x = Math.Clamp(r.Left, area.Left, Math.Max(area.Left, area.Right - w));
        int y = Math.Clamp(r.Top, area.Top, Math.Max(area.Top, area.Bottom - h));
        if (x != r.Left || y != r.Top)
            SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private static string MonitorDevice(IntPtr hMon)
    {
        if (hMon == IntPtr.Zero) return "";
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfoW(hMon, ref mi) ? mi.szDevice : "";
    }

    private static RECT WorkArea(IntPtr hMon)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (hMon != IntPtr.Zero && GetMonitorInfoW(hMon, ref mi)) return mi.rcWork;
        // 兜底主屏工作区
        return new RECT { Left = 0, Top = 0, Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    /// <summary>按 DeviceName 枚举显示器（照抄 Screen.AllScreens.FirstOrDefault 语义）。</summary>
    private static IntPtr FindMonitor(string device)
    {
        IntPtr found = IntPtr.Zero;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, ref rc, data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoW(hMon, ref mi) && string.Equals(mi.szDevice, device, StringComparison.Ordinal))
            {
                found = hMon;
                return false; // 停止枚举
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // GDI+ 字体（EnsureFonts/EmPx）与全部绘制（OnPaint/Render/DrawTitle/DrawRow/DrawSizeGrip）见 DesktopWidget.Render.cs

    // ---------- 窗口过程 ----------

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var ptr = GetWindowLongPtrW(hWnd, GWLP_USERDATA);
        DesktopWidget? self = null;
        if (ptr != IntPtr.Zero)
        {
            try { self = (DesktopWidget?)GCHandle.FromIntPtr(ptr).Target; } catch { }
        }

        switch (msg)
        {
            case WM_PAINT:
                self?.OnPaint(hWnd);
                return IntPtr.Zero;
            case WM_ERASEBKGND:
                return new IntPtr(1);
            case WM_NCHITTEST:
                // 穿透态窗口本身收不到此消息（WS_EX_TRANSPARENT 直接穿手）；非穿透时给边缘 resize 热区
                if (self != null)
                {
                    int hit = self.HitTest(lParam);
                    if (hit != HTCLIENT) return new IntPtr(hit);
                }
                break;
            case WM_GETMINMAXINFO:
                self?.OnGetMinMaxInfo(lParam);
                return IntPtr.Zero;
            case WM_SIZE:
                self?.OnSize((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
                return IntPtr.Zero;
            case WM_ENTERSIZEMOVE:
                self?.OnEnterSizeMove();
                return IntPtr.Zero;
            case WM_EXITSIZEMOVE:
                self?.OnExitSizeMove();
                return IntPtr.Zero;
            case WM_LBUTTONDOWN:
                if (self != null && (wParam.ToInt64() & MK_LBUTTON) != 0) self.OnLeftButtonDown(lParam);
                return IntPtr.Zero;
            case WM_MOUSEMOVE:
                if (self != null && (wParam.ToInt64() & MK_LBUTTON) != 0) self.OnMouseMove();
                return IntPtr.Zero;
            case WM_LBUTTONUP:
                self?.OnLeftButtonUp();
                return IntPtr.Zero;
            case WM_LBUTTONDBLCLK:
                if (self != null) try { self.DoubleClicked?.Invoke(); } catch { /* 回调异常不影响窗口 */ }
                return IntPtr.Zero;
            case WM_DISPLAYCHANGE:
                // 500ms 防抖：重复 SetTimer 会重置计时（照抄 MainForm CancellationTokenSource 取消语义）
                if (self != null) SetTimer(hWnd, (IntPtr)IDT_DISPLAY, 500, IntPtr.Zero);
                return IntPtr.Zero;
            case WM_DPICHANGED:
                self?.OnDpiChanged();
                return IntPtr.Zero;
            case WM_TIMER:
                self?.OnTimer(wParam.ToInt32());
                return IntPtr.Zero;
            case WM_DESTROY:
                return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void OnLeftButtonDown(IntPtr lParam)
    {
        // 客户区坐标即光标相对窗口左上角的偏移（WS_POPUP 无边框）
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        _dragOffset = new POINT { X = x, Y = y };
        _dragging = true;
        _moved = false;
        SetCapture(_hwnd);
    }

    private void OnMouseMove()
    {
        if (!_dragging || _hwnd == IntPtr.Zero) return;
        if (!GetCursorPos(out POINT cursor)) return;
        int nx = cursor.X - _dragOffset.X;
        int ny = cursor.Y - _dragOffset.Y;
        if (!GetWindowRect(_hwnd, out RECT r) || (r.Left == nx && r.Top == ny)) return;
        // 照抄上游：SetWindowPos 直接跟随（不在拖拽过程中做 clamp，避免和边缘跳动）
        SetWindowPos(_hwnd, IntPtr.Zero, nx, ny, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        _moved = true;
    }

    private void OnLeftButtonUp()
    {
        if (!_dragging) return;
        _dragging = false;
        try { ReleaseCapture(); } catch { /* 捕获可能已失效 */ }
        if (_moved) SavePos();
    }

    // ---------- 调整大小（3.23.1）----------

    /// <summary>
    /// 命中测试：右下角 Corner 斜向拖、右/底边 Edge 拖，其余为客户区（走手动移动）。
    /// lParam 为屏幕物理坐标，需转客户区。穿透态本消息不会到达。
    /// </summary>
    private int HitTest(IntPtr lParam)
    {
        if (_hwnd == IntPtr.Zero) return HTCLIENT;
        int cx = (short)(lParam.ToInt64() & 0xFFFF);
        int cy = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var p = new POINT { X = cx, Y = cy };
        if (!ScreenToClient(_hwnd, ref p) || !GetClientRect(_hwnd, out RECT rc)) return HTCLIENT;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        int edge = SP(Edge), corner = SP(Corner);
        // 角优先（避免与边重叠歧义）
        if (p.X >= w - corner && p.Y >= h - corner) return HTBOTTOMRIGHT;
        if (p.X >= w - edge) return HTRIGHT;
        if (p.Y >= h - edge) return HTBOTTOM;
        return HTCLIENT;
    }

    /// <summary>限制系统原生 resize 的最小跟踪尺寸（物理像素，仅按 DPI）。</summary>
    private void OnGetMinMaxInfo(IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMinTrackSize.X = SP(MinLogicalW);
        mmi.ptMinTrackSize.Y = SP(MinLogicalH);
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    /// <summary>系统 sizing 实时回调：按新宽更新缩放与字体重绘（高度自动校正延迟到 loop 结束，避免与系统打架）。</summary>
    private void OnSize(int width, int height)
    {
        if (_hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        if (UpdateScaleFromWidth(width)) EnsureFonts(force: true);
        InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private void OnEnterSizeMove()
    {
        if (_hwnd != IntPtr.Zero) GetWindowRect(_hwnd, out _sizeMoveStart);
    }

    /// <summary>
    /// 系统 resize/move loop 结束：高被拖过 → 记住用户高度；只拖宽 → 高度自动贴合内容；
    /// 之后统一保存位置+尺寸（含 ClampToScreen）。
    /// </summary>
    private void OnExitSizeMove()
    {
        if (_hwnd == IntPtr.Zero || !GetWindowRect(_hwnd, out RECT r)) return;
        int w0 = _sizeMoveStart.Right - _sizeMoveStart.Left;
        int h0 = _sizeMoveStart.Bottom - _sizeMoveStart.Top;
        int w1 = r.Right - r.Left, h1 = r.Bottom - r.Top;
        bool widthChanged = w1 != w0;
        bool heightChanged = h1 != h0;

        if (heightChanged) _heightUserSet = true;
        if (widthChanged) UpdateScaleFromWidth(w1);

        int needH = ContentHeight();
        if (!_heightUserSet && needH != h1)
        {
            SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, w1, needH, SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
        else if (needH > h1)
        {
            // 内容超出当前高度（如刚打开更多显示项）：扩高到刚好容纳
            SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, w1, needH, SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
        SavePos();
    }

    private void OnDpiChanged()
    {
        if (_hwnd == IntPtr.Zero) return;
        uint dpi = GetDpiForWindow(_hwnd);
        if (dpi != 0 && dpi != _dpi)
        {
            _dpi = dpi;
            EnsureFonts(force: true);
        }
        InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private void OnTimer(int id)
    {
        switch (id)
        {
            case IDT_TOPFIRST:
                KillTimer(_hwnd, (IntPtr)IDT_TOPFIRST);
                if (_topmost) RefreshTopMost(true, forceReinsert: true);
                break;
            case IDT_TOPPOLL:
                if (_topmost != IsTopMostStyleApplied())
                    RefreshTopMost(_topmost, forceReinsert: true);
                break;
            case IDT_DISPLAY:
                KillTimer(_hwnd, (IntPtr)IDT_DISPLAY);
                if (GetWindowRect(_hwnd, out RECT r))
                {
                    int w = r.Right - r.Left;
                    int h = r.Bottom - r.Top;
                    var center = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
                    string device = MonitorDevice(MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST));
                    RestorePos(device, r.Left, r.Top, w, h);
                }
                break;
        }
    }

    private static void RegisterClass()
    {
        if (_classRegistered) return;
        var wc = new WNDCLASS
        {
            style = CS_DBLCLKS, // 需要双击消息必须注册 CS_DBLCLKS
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcHandler),
            hInstance = Marshal.GetHINSTANCE(typeof(DesktopWidget).Module),
            lpszClassName = ClassName
        };
        if (RegisterClassW(ref wc) != 0) _classRegistered = true;
    }

    // GDI+ 渲染（OnPaint/Render/字体/后备表面/配色）全部见 DesktopWidget.Render.cs

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                KillTimer(_hwnd, (IntPtr)IDT_TOPFIRST);
                KillTimer(_hwnd, (IntPtr)IDT_TOPPOLL);
                KillTimer(_hwnd, (IntPtr)IDT_DISPLAY);
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            ReleaseRenderResources();   // GDI+ 字体 + 32bpp 后备表面
            if (_gcHandle.IsAllocated) _gcHandle.Free();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DesktopWidget.Dispose 异常");
        }
    }
}
