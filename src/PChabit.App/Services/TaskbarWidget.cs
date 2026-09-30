using System.Runtime.InteropServices;
using Serilog;

namespace PChabit.App.Services;

/// <summary>任务栏小窗条目颜色状态。</summary>
public enum TaskbarMetricColor
{
    Safe,
    Warn,
    Crit
}

/// <summary>
/// 任务栏小窗条目。主行数值放大加粗作为视觉焦点；次行整体弱化。
/// Safe 用正文色（不抢眼），仅 Warn/Crit 着色，让异常一眼跳出。
/// </summary>
public sealed record TaskbarMetricItem(string Label, string Value, TaskbarMetricColor Color);

/// <summary>
/// 嵌入 Windows 任务栏的两行文本监控小窗（Win11）。
/// 机制照抄 LiteMonitor（MIT）TaskbarStrategyWin11 + TaskbarRenderer：
/// 无边框子窗口 SetParent 挂载到 Shell_TrayWnd，LWA_COLORKEY 透明键，
/// GDI 双缓冲绘制两行文本，固定定位在任务栏最左侧（与左边缘对齐）。
/// </summary>
public sealed class TaskbarWidget : IDisposable
{
    // ---------- 样式常量 ----------
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_USERDATA = -21;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_COLORKEY = 0x00000001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_PAINT = 0x000F;
    private const int WM_DESTROY = 0x0002;
    private const int DEFAULT_CHARSET = 1;
    private const int OUT_DEFAULT_PRECIS = 0;
    private const int CLIP_DEFAULT_PRECIS = 0;
    private const int ANTIALIASED_QUALITY = 4;  // 灰度抗锯齿：色键透明表面上 ClearType 会产生彩色毛边，灰度边缘在任意任务栏底色上都干净
    private const int DEFAULT_PITCH = 0;
    private const int FF_DONTCARE = 0;
    private const uint TRANSPARENT = 1;
    private const uint SRCCOPY = 0x00CC0020;

    // ---------- 颜色（浅/深）----------
    // 对比度优先：任务栏底色浅≈#D2D2D2 / 深≈#292828，标签用中对比灰、数值用近黑白；
    // 仅偏高/过高着色。DIM 只给分隔符，正文绝不用过灰。
    private static readonly uint[] LABEL = { 0x3A3A3A, 0xC8C8C8 };
    private static readonly uint[] VALUE = { 0x111111, 0xF8F8F8 };
    private static readonly uint[] DIM = { 0x777777, 0x888888 };      // 仅「·」分隔符
    private static readonly uint[] WARN = { 0xB57500, 0xFFD666 };
    private static readonly uint[] CRIT = { 0xC03030, 0xFF6666 };
    private static readonly uint[] BG_KEY = { 0x00D3D2D2, 0x00292828 }; // 浅 210,210,211 / 深 40,40,41

    // ---------- 布局 ----------
    private const int ItemGap = 14;         // 条目间距（主行更松，避免挤成一团）
    private const int SecondaryItemGap = 10;
    private const int LabelValueGap = 3;
    private const int Padding = 8;
    private const int PrimaryLabelSize = 11;
    private const int PrimaryValueSize = 15; // 主行数值：视觉焦点
    private const int SecondarySize = 13;    // 次行可读性优先（过小+过灰会糊成一片）

    // ---------- 状态 ----------
    private IntPtr _hwnd;
    private IntPtr _hTaskbar;
    private IntPtr _fontLabel;
    private IntPtr _fontValue;
    private IntPtr _fontSecondary;
    private GCHandle _gcHandle;
    private bool _light;
    private int _width = 60;
    private List<TaskbarMetricItem> _row1 = new();
    private List<TaskbarMetricItem> _row2 = new();
    private bool _disposed;

    // ---------- Win32 ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }
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
    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore; public bool fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll")]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextExtentPoint32W(IntPtr hdc, string text, int len, out SIZE size);
    [DllImport("gdi32.dll")]
    private static extern bool SetTextColor(IntPtr hdc, uint crColor);
    [DllImport("gdi32.dll")]
    private static extern uint SetBkMode(IntPtr hdc, uint mode);
    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(IntPtr hdc, int x, int y, string text, int len);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("user32.dll")]
    private static extern IntPtr FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly WndProcDelegate WndProcHandler = WndProc;
    private static readonly string ClassName = "PChabitTaskbarWidget";
    private static bool _classRegistered;

    public TaskbarWidget()
    {
        _gcHandle = GCHandle.Alloc(this);
    }

    /// <summary>窗口是否已创建并有效（Explorer 重启等场景下会失效）。</summary>
    public bool IsRunning => _hwnd != IntPtr.Zero && IsWindow(_hwnd);

    /// <summary>
    /// 创建并挂载到任务栏（UI 线程调用）。幂等：
    /// - 有效窗口则检查父窗口，必要时重新挂载（Explorer 重启后 Shell_TrayWnd 会换代）
    /// - 失效句柄先销毁再重建，避免旧句柄挡住恢复
    /// </summary>
    public void Start()
    {
        if (_disposed) return;
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                if (IsWindow(_hwnd))
                {
                    EnsureAttached();
                    return;
                }
                // 句柄已失效（典型：Explorer 重启销毁了子窗口），清掉后重建
                Log.Information("TaskbarWidget 句柄失效，重建小窗");
                try { DestroyWindow(_hwnd); } catch { /* 已失效，忽略 */ }
                _hwnd = IntPtr.Zero;
            }

            RegisterClass();
            _hwnd = CreateWindowExW(
                WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                ClassName, "", WS_POPUP,
                0, 0, _width, 40, IntPtr.Zero, IntPtr.Zero,
                Marshal.GetHINSTANCE(typeof(TaskbarWidget).Module), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Log.Warning("TaskbarWidget 窗口创建失败");
                return;
            }

            SetWindowLongPtrW(_hwnd, GWLP_USERDATA, (IntPtr)_gcHandle);
            AttachToTaskbar();
            Log.Information("TaskbarWidget 已创建并挂载任务栏");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TaskbarWidget.Start 失败");
        }
    }

    /// <summary>
    /// 确保仍挂在当前任务栏上。Explorer 重启后可能：
    /// 1) 子窗口被销毁（IsWindow=false）→ 由 Start() 重建
    /// 2) 窗口还在但父句柄已换 → 这里重新 SetParent
    /// </summary>
    private void EnsureAttached()
    {
        if (_hwnd == IntPtr.Zero) return;
        FindHandles();
        if (_hTaskbar == IntPtr.Zero) return;

        var parent = GetParent(_hwnd);
        if (parent != _hTaskbar)
        {
            Log.Information("TaskbarWidget 父窗口变化，重新挂载任务栏");
            AttachToTaskbar();
        }
        else
        {
            Position();
        }
    }

    private void RegisterClass()
    {
        if (_classRegistered) return;
        var wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcHandler),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = Marshal.GetHINSTANCE(typeof(TaskbarWidget).Module),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = ClassName
        };
        if (RegisterClassW(ref wc) != 0) _classRegistered = true;
    }

    private void FindHandles()
    {
        _hTaskbar = FindWindow("Shell_TrayWnd", null);
    }

    private void AttachToTaskbar()
    {
        FindHandles();
        if (_hTaskbar == IntPtr.Zero || _hwnd == IntPtr.Zero) return;

        SetParent(_hwnd, _hTaskbar);
        // 改为 CHILD 样式并显示
        var style = GetWindowLongPtrW(_hwnd, GWL_STYLE).ToInt64();
        style &= ~(long)WS_POPUP;
        style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
        SetWindowLongPtrW(_hwnd, GWL_STYLE, (IntPtr)style);
        ApplyLayeredStyle();

        Position();
    }

    private void Position()
    {
        if (_hTaskbar == IntPtr.Zero || _hwnd == IntPtr.Zero) return;
        if (!GetWindowRect(_hTaskbar, out RECT taskbarRect)) return;

        int height = taskbarRect.Bottom - taskbarRect.Top;
        if (height <= 0) return;

        // 固定最左侧：与任务栏左边缘对齐（留 8px 边距），不再贴托盘
        int left = taskbarRect.Left + 8;
        int top = taskbarRect.Top;

        // 挂载后转换为任务栏 client 坐标
        int clientX = left - taskbarRect.Left;
        int clientY = top - taskbarRect.Top;
        // hWndInsertAfter=HWND_TOP：任务栏左侧覆盖开始按钮等 XAML 层（EnumChildWindows 实测小窗按 Z 序
        // 从底枚举排第一，说明不置顶会被兄弟窗口盖住，右侧无遮挡才能显示）
        SetWindowPos(_hwnd, IntPtr.Zero, clientX, clientY, _width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void ApplyLayeredStyle()
    {
        if (_hwnd == IntPtr.Zero) return;
        uint key = BG_KEY[_light ? 0 : 1];
        SetLayeredWindowAttributes(_hwnd, key, 0, LWA_COLORKEY);
    }

    /// <summary>更新显示内容（UI 线程，5s 刷新）。</summary>
    public void UpdateContent(List<TaskbarMetricItem> row1, List<TaskbarMetricItem> row2)
    {
        if (_hwnd == IntPtr.Zero) return;
        _row1 = row1;
        _row2 = row2;
        MeasureAndResize();
        InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    /// <summary>深浅主题切换。</summary>
    public void SetTheme(bool light)
    {
        if (_light == light) return;
        _light = light;
        ApplyLayeredStyle();
        InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private void MeasureAndResize()
    {
        if (_hwnd == IntPtr.Zero) return;
        IntPtr hdc = GetDC(_hwnd);
        if (hdc == IntPtr.Zero) return;
        try
        {
            EnsureFonts();
            int total = 0;
            if (_row1.Count > 0) total = Math.Max(total, MeasurePrimaryRow(hdc, _row1));
            if (_row2.Count > 0) total = Math.Max(total, MeasureSecondaryRow(hdc, _row2));
            _width = Math.Max(total + Padding * 2, 72);
            Position();
        }
        finally
        {
            ReleaseDC(_hwnd, hdc);
        }
    }

    private int MeasurePrimaryRow(IntPtr hdc, List<TaskbarMetricItem> items)
    {
        int w = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Label.Length > 0)
            {
                w += MeasureText(hdc, _fontLabel, it.Label);
                w += LabelValueGap;
            }
            w += MeasureText(hdc, _fontValue, it.Value);
            if (i < items.Count - 1) w += ItemGap;
        }
        return w;
    }

    private int MeasureSecondaryRow(IntPtr hdc, List<TaskbarMetricItem> items)
    {
        int w = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Label.Length > 0)
            {
                w += MeasureText(hdc, _fontSecondary, it.Label);
                w += LabelValueGap;
            }
            w += MeasureText(hdc, _fontSecondary, it.Value);
            if (i < items.Count - 1) w += SecondaryItemGap;
        }
        return w;
    }

    private static int MeasureText(IntPtr hdc, IntPtr font, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var old = SelectObject(hdc, font);
        int w = GetTextExtentPoint32W(hdc, text, text.Length, out SIZE sz) ? sz.cx : text.Length * 8;
        SelectObject(hdc, old);
        return w;
    }

    private void EnsureFonts()
    {
        if (_fontLabel != IntPtr.Zero) return;
        uint dpi = GetDpiForWindow(_hwnd);
        if (dpi == 0) dpi = 96;
        // 微软雅黑：中英文统一；ANTIALIASED 避免色键透明面 ClearType 彩边
        // 主行：小标签 + 大号半粗数值形成焦点；次行：整体小一号弱化
        _fontLabel = CreateScaledFont(PrimaryLabelSize, 500, dpi);
        _fontValue = CreateScaledFont(PrimaryValueSize, 700, dpi);
        _fontSecondary = CreateScaledFont(SecondarySize, 500, dpi);
    }

    private static IntPtr CreateScaledFont(int sizePt, int weight, uint dpi)
    {
        int px = -(int)(sizePt * dpi / 96.0);
        return CreateFontW(px, 0, 0, 0, weight, 0, 0, 0, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, (uint)(DEFAULT_PITCH | FF_DONTCARE), "Microsoft YaHei UI");
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var ptr = GetWindowLongPtrW(hWnd, GWLP_USERDATA);
        TaskbarWidget? self = null;
        if (ptr != IntPtr.Zero)
        {
            try { self = (TaskbarWidget?)GCHandle.FromIntPtr(ptr).Target; } catch { }
        }

        switch (msg)
        {
            case WM_PAINT:
                self?.OnPaint(hWnd);
                return IntPtr.Zero;
            case WM_ERASEBKGND:
                return new IntPtr(1);
            case WM_DESTROY:
                return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void OnPaint(IntPtr hWnd)
    {
        PAINTSTRUCT ps = new();
        IntPtr hdc = BeginPaint(hWnd, ref ps);
        if (hdc == IntPtr.Zero) return;
        try
        {
            if (!GetClientRect(hWnd, out RECT rc)) return;
            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0) return;

            IntPtr memDc = CreateCompatibleDC(hdc);
            IntPtr bmp = CreateCompatibleBitmap(hdc, w, h);
            var oldBmp = SelectObject(memDc, bmp);
            EnsureFonts();

            // 背景填透明键
            IntPtr brush = CreateSolidBrush(BG_KEY[_light ? 0 : 1]);
            FillRect(memDc, ref rc, brush);
            DeleteObject(brush);

            SetBkMode(memDc, TRANSPARENT);

            // 有主行时上主下次（主行略高，数值更大）；只有一行则垂直居中
            bool has1 = _row1.Count > 0;
            bool has2 = _row2.Count > 0;
            if (has1 && has2)
            {
                int primaryH = (int)(h * 0.56);
                DrawPrimaryRow(memDc, _row1, new RECT { Left = Padding, Top = 0, Right = w - Padding, Bottom = primaryH });
                DrawSecondaryRow(memDc, _row2, new RECT { Left = Padding, Top = primaryH, Right = w - Padding, Bottom = h });
            }
            else if (has1)
            {
                DrawPrimaryRow(memDc, _row1, new RECT { Left = Padding, Top = 0, Right = w - Padding, Bottom = h });
            }
            else if (has2)
            {
                DrawSecondaryRow(memDc, _row2, new RECT { Left = Padding, Top = 0, Right = w - Padding, Bottom = h });
            }

            BitBlt(hdc, 0, 0, w, h, memDc, 0, 0, SRCCOPY);

            SelectObject(memDc, oldBmp);
            DeleteObject(bmp);
            DeleteDC(memDc);
        }
        finally
        {
            EndPaint(hWnd, ref ps);
        }
    }

    private static uint ValueColor(TaskbarMetricColor color, int theme) => color switch
    {
        TaskbarMetricColor.Warn => WARN[theme],
        TaskbarMetricColor.Crit => CRIT[theme],
        _ => VALUE[theme]
    };

    /// <summary>主行：小灰标签 + 大号粗体数值（焦点），条目间距拉开。</summary>
    private void DrawPrimaryRow(IntPtr hdc, List<TaskbarMetricItem> items, RECT rc)
    {
        int x = rc.Left;
        int theme = _light ? 0 : 1;
        // 大数字（15px/700）字高约 22px，基线在字顶偏下 17px 处
        int yValue = TextTop(rc, 22);
        int yLabel = TextTop(rc, 15);

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Label.Length > 0)
            {
                SelectObject(hdc, _fontLabel);
                SetTextColor(hdc, LABEL[theme]);
                TextOutW(hdc, x, yLabel, it.Label, it.Label.Length);
                x += MeasureText(hdc, _fontLabel, it.Label) + LabelValueGap;
            }

            SelectObject(hdc, _fontValue);
            SetTextColor(hdc, ValueColor(it.Color, theme));
            TextOutW(hdc, x, yValue, it.Value, it.Value.Length);
            x += MeasureText(hdc, _fontValue, it.Value);

            if (i < items.Count - 1) x += ItemGap;
        }
    }

    /// <summary>次行：字号与主行标签接近，标签/数值用高对比色，条目间「·」弱化分组。</summary>
    private void DrawSecondaryRow(IntPtr hdc, List<TaskbarMetricItem> items, RECT rc)
    {
        int x = rc.Left;
        int theme = _light ? 0 : 1;
        int y = TextTop(rc, 18);
        SelectObject(hdc, _fontSecondary);

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Label.Length > 0)
            {
                SetTextColor(hdc, LABEL[theme]);
                TextOutW(hdc, x, y, it.Label, it.Label.Length);
                x += MeasureText(hdc, _fontSecondary, it.Label) + LabelValueGap;
            }

            SetTextColor(hdc, ValueColor(it.Color, theme));
            TextOutW(hdc, x, y, it.Value, it.Value.Length);
            x += MeasureText(hdc, _fontSecondary, it.Value);

            if (i < items.Count - 1)
            {
                SetTextColor(hdc, DIM[theme]);
                TextOutW(hdc, x + SecondaryItemGap / 2 - 2, y, "·", 1);
                x += SecondaryItemGap;
            }
        }
    }

    private static int TextTop(RECT rc, int textHeight)
    {
        int h = rc.Bottom - rc.Top;
        return rc.Top + Math.Max(0, (h - textHeight) / 2);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_fontLabel != IntPtr.Zero) { DeleteObject(_fontLabel); _fontLabel = IntPtr.Zero; }
            if (_fontValue != IntPtr.Zero) { DeleteObject(_fontValue); _fontValue = IntPtr.Zero; }
            if (_fontSecondary != IntPtr.Zero) { DeleteObject(_fontSecondary); _fontSecondary = IntPtr.Zero; }
            if (_gcHandle.IsAllocated) _gcHandle.Free();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TaskbarWidget.Dispose 异常");
        }
    }
}
