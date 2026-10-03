using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Serilog;

namespace PChabit.App.Services;

/// <summary>
/// DesktopWidget 的 GDI+ 绘制分部（3.23.2 由 GDI 色键透明升级为 per-pixel alpha 分层窗口）。
/// 管线：32bpp 顶向下 PARGB DIB → GDI+ Graphics（灰阶抗锯齿 AntiAliasGridFit；分层窗口不能用
/// ClearType——子像素条纹经壁纸二次合成会出彩色毛边，WPF 透明窗口同理回退灰阶）→ UpdateLayeredWindow
/// (ULW_ALPHA) 整层提交。文字边缘写入真实 alpha，在任意壁纸上均光滑无色键杂边；
/// 所有文字带 1px 半透明黑色投影（深色主题不透明度更高），花哨壁纸下也清晰。
/// 尺寸经 S() 同时按 DPI 与用户缩放（_userScale，窗口宽度驱动）缩放，拖大窗口文字同步变大。
/// </summary>
public sealed partial class DesktopWidget
{
    // ---------- 色板（[0]=浅 [1]=深；以 RGB uint(0xRRGGBB) 定义，C() 转 GDI+ Color）----------
    private static readonly uint[] LABEL_RGB = { 0x3A3A3A, 0xC8C8C8 };
    private static readonly uint[] VALUE_RGB = { 0x111111, 0xF8F8F8 };
    private static readonly uint[] WARN_RGB = { 0xB57500, 0xFFD666 };   // 琥珀文字
    private static readonly uint[] CRIT_RGB = { 0xC03030, 0xFF6666 };   // 鲜红文字
    private static readonly uint[] BAR_SAFE = { 0x2E9E5B, 0x45B56A };   // 安全绿
    private static readonly uint[] BAR_WARN = { 0xE08A00, 0xF0B429 };   // 预警琥珀
    private static readonly uint[] BAR_CRIT = { 0xE53935, 0xFF5252 };   // 危险鲜红
    private static readonly uint[] BAR_TRACK = { 0xD8D8D8, 0x3A3838 };  // 药丸槽（灰度）
    private static readonly uint[] SEPARATOR_RGB = { 0xDEDEDE, 0x383636 };
    private const uint BRAND_RED = 0xE53935;

    // 文字投影不透明度（浅主题深字只需淡影；深主题白字靠重影在花壁纸上勾边）
    private const int ShadowAlphaLight = 78;
    private const int ShadowAlphaDark = 168;
    private const string FontFace = "Microsoft YaHei UI";
    // GDI lfHeight 是字符盒高，GDI+ emSize 是字身方；雅黑 ascent+descent ≈ 1.165em
    private const float CellToEm = 0.858f;

    // ---------- 绘制专用 P/Invoke ----------
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, [In] ref BITMAPINFO pbmi,
        uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    private const uint ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;   // 负数 = 顶向下
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    // ---------- 渲染资源（随窗口尺寸重建，UI 线程独占）----------
    private static readonly StringFormat _fmt = new(StringFormatFlags.NoWrap);

    private Font? _fontTitle;
    private Font? _fontText;
    private int _bufW = -1;
    private int _bufH = -1;
    private IntPtr _bufHdc;
    private IntPtr _bufDib;
    private Bitmap? _bufBmp;
    private Graphics? _gfx;

    // ---------- 主绘制 ----------

    private void OnPaint(IntPtr hWnd)
    {
        PAINTSTRUCT ps = new();
        IntPtr hdc = BeginPaint(hWnd, ref ps);
        if (hdc == IntPtr.Zero) return;
        try { Render(); }
        finally { EndPaint(hWnd, ref ps); }
    }

    /// <summary>按当前窗口尺寸与内容渲染一帧并提交分层窗口（UI 线程）。</summary>
    private void Render()
    {
        if (_hwnd == IntPtr.Zero || !GetClientRect(_hwnd, out RECT rc)) return;
        int w = rc.Right - rc.Left;
        int h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return;
        try
        {
            EnsureRenderBuffer(w, h);
            EnsureFonts();
            if (_gfx == null || _fontTitle == null || _fontText == null) return;

            _gfx.Clear(Color.Transparent);
            DrawTitle(w);
            int top = S(PadV) + S(TitleH);
            for (int i = 0; i < _rows.Count; i++)
                DrawRow(_rows[i], S(Pad), top + i * S(RowH), w - S(Pad) * 2, S(RowH));
            if (!_clickThrough) DrawSizeGrip(w, h);

            SubmitLayered(w, h);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DesktopWidget.Render 失败");
        }
    }

    /// <summary>确保 32bpp PARGB 后备表面与 GDI+ Graphics 与窗口同尺寸（尺寸变化即重建）。</summary>
    private void EnsureRenderBuffer(int w, int h)
    {
        if (_bufDib != IntPtr.Zero && _bufW == w && _bufH == h) return;
        ReleaseRenderBuffer();

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,       // 顶向下，坐标直觉与 GDI 一致
                biPlanes = 1,
                biBitCount = 32,
                biCompression = (int)BI_RGB
            }
        };
        _bufHdc = CreateCompatibleDC(IntPtr.Zero);
        _bufDib = CreateDIBSection(_bufHdc, ref bmi, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (_bufDib == IntPtr.Zero || bits == IntPtr.Zero)
            throw new InvalidOperationException("CreateDIBSection(32bpp) 失败");
        SelectObject(_bufHdc, _bufDib);
        _bufBmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
        _gfx = Graphics.FromImage(_bufBmp);
        _gfx.SmoothingMode = SmoothingMode.AntiAlias;       // 药丸条/圆点边缘光滑
        _gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
        _gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        _bufW = w;
        _bufH = h;
    }

    /// <summary>把后备表面以 per-pixel alpha 提交到分层窗口（位置/尺寸取当前窗口实际值，不引发移动/缩放）。</summary>
    private void SubmitLayered(int w, int h)
    {
        if (_gfx == null || !GetWindowRect(_hwnd, out RECT wr)) return;
        var pos = new POINT { X = wr.Left, Y = wr.Top };
        var size = new SIZE { cx = w, cy = h };
        var src = new POINT();
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA
        };
        UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref pos, ref size, _bufHdc, ref src, 0, ref blend, ULW_ALPHA);
    }

    private void ReleaseRenderBuffer()
    {
        _gfx?.Dispose();
        _gfx = null;
        _bufBmp?.Dispose();   // 不拥有 scan0，DIB 单独释放
        _bufBmp = null;
        if (_bufHdc != IntPtr.Zero) { DeleteDC(_bufHdc); _bufHdc = IntPtr.Zero; }
        if (_bufDib != IntPtr.Zero) { DeleteObject(_bufDib); _bufDib = IntPtr.Zero; }
        _bufW = _bufH = -1;
    }

    /// <summary>释放全部绘制资源（Dispose 用）。</summary>
    private void ReleaseRenderResources()
    {
        ReleaseRenderBuffer();
        _fontTitle?.Dispose();
        _fontTitle = null;
        _fontText?.Dispose();
        _fontText = null;
    }

    // ---------- 字体 ----------

    private void EnsureFonts(bool force = false)
    {
        if (!force && _fontTitle != null && _fontText != null) return;
        _fontTitle?.Dispose();
        _fontText?.Dispose();
        _fontTitle = new Font(FontFace, EmPx(13), FontStyle.Bold, GraphicsUnit.Pixel);
        _fontText = new Font(FontFace, EmPx(12), FontStyle.Regular, GraphicsUnit.Pixel);
    }

    /// <summary>GDI 时代的字符盒像素高 → GDI+ emSize（仍按 DPI × 用户缩放双重放大）。</summary>
    private float EmPx(int cellPx) => (float)(cellPx * _dpi / 96.0 * _userScale) * CellToEm;

    // ---------- 绘制元素 ----------

    private void DrawTitle(int w)
    {
        if (_gfx == null || _fontTitle == null) return;
        Graphics g = _gfx;
        int theme = _light ? 0 : 1;
        float titleH = g.MeasureString("PChabit", _fontTitle, 100000, _fmt).Height;
        float y = S(PadV) + (S(TitleH) - titleH) / 2f;

        DrawText(g, "PChabit", _fontTitle, S(Pad), y, C(VALUE_RGB[theme]));

        // 右侧：实心圆点 + "硬件"
        string right = "硬件";
        float textW = g.MeasureString(right, _fontTitle, 100000, _fmt).Width;
        float dot = S(7);
        float textX = w - S(Pad) - textW;
        float dotX = textX - S(6) - dot;
        float dotY = S(PadV) + (S(TitleH) - dot) / 2f;
        using (var dotBrush = new SolidBrush(C(BRAND_RED)))
            g.FillEllipse(dotBrush, dotX, dotY, dot, dot);
        DrawText(g, right, _fontTitle, textX, y, C(LABEL_RGB[theme]));

        // 标题底部分隔线（非 AA 1px 线更干净，GDI+ 像素中心 +0.5）
        float lineY = S(PadV) + S(TitleH) - S(6) + 0.5f;
        var oldSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using (var pen = new Pen(C(SEPARATOR_RGB[theme]), 1f))
            g.DrawLine(pen, S(Pad), lineY, w - S(Pad), lineY);
        g.SmoothingMode = oldSmoothing;
    }

    private void DrawRow(DesktopWidgetRow row, float x, float y, float width, float height)
    {
        if (_gfx == null || _fontText == null) return;
        Graphics g = _gfx;
        int theme = _light ? 0 : 1;
        float textH = g.MeasureString(row.Label, _fontText, 100000, _fmt).Height;
        float textY = y + (height - textH) / 2f;

        // 左侧短标签（带投影）
        DrawText(g, row.Label, _fontText, x, textY, C(LABEL_RGB[theme]));

        // 右侧数值段（先量总宽再右对齐，每段独立着色）
        float valueW = 0;
        foreach (var seg in row.Segments) valueW += Measure(g, _fontText, seg.Text);
        float valueX = x + width - valueW;
        foreach (var seg in row.Segments)
        {
            if (seg.Text.Length == 0) continue;
            DrawText(g, seg.Text, _fontText, valueX, textY, C(SegmentColorRgb(seg.Color, theme)));
            valueX += Measure(g, _fontText, seg.Text);
        }

        // 药丸进度条：标签后 → 数值前
        float barX1 = x + S(LabelW) + S(Gap);
        float barX2 = x + width - valueW - S(Gap);
        float barW = barX2 - barX1;
        if (row.Fraction.HasValue && barW > S(18))
        {
            float barH = S(BarH);
            float barY = y + (height - barH) / 2f;

            using (var trackBrush = new SolidBrush(C(BAR_TRACK[theme])))
            using (var trackPath = PillPath(barX1, barY, barW, barH))
                g.FillPath(trackBrush, trackPath);

            float f = Math.Clamp(row.Fraction.Value, 0f, 1f);
            float fillW = (float)Math.Round(barW * f);
            if (fillW >= 1.5f)
            {
                // 裁剪到实际填充宽再画整条药丸：任意窄宽都是圆头小段，与旧 GDI 视觉一致
                GraphicsState state = g.Save();
                g.SetClip(new RectangleF(barX1, barY, fillW, barH), CombineMode.Replace);
                using (var fillBrush = new SolidBrush(C(BarColorRgb(row.BarColor, theme))))
                using (var fillPath = PillPath(barX1, barY, barW, barH))
                    g.FillPath(fillBrush, fillPath);
                g.Restore(state);
            }
        }
    }

    /// <summary>右下角调整大小手柄：三条 45° 斜纹（经典 Windows grip），仅非穿透态绘制。</summary>
    private void DrawSizeGrip(int w, int h)
    {
        if (_gfx == null) return;
        int theme = _light ? 0 : 1;
        float g0 = S(12);
        float x0 = w - S(Pad) + S(2);
        float y0 = h - S(PadV) + S(2);
        using var pen = new Pen(C(LABEL_RGB[theme]), Math.Max(1f, S(1)));
        for (int i = 0; i < 3; i++)
        {
            float baseOff = g0 - S(3) - i * S(4);
            _gfx.DrawLine(pen, x0 + baseOff - S(3), y0 + g0 - S(2), x0 + g0 - S(2), y0 + baseOff - S(3));
        }
    }

    // ---------- 文字（带半透明投影）----------

    private void DrawText(Graphics g, string text, Font font, float x, float y, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        float off = Math.Max(1f, S(1));
        int shadowAlpha = _light ? ShadowAlphaLight : ShadowAlphaDark;
        using (var shadow = new SolidBrush(Color.FromArgb(shadowAlpha, Color.Black)))
            g.DrawString(text, font, shadow, x + off, y + off, _fmt);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y, _fmt);
    }

    private float Measure(Graphics g, Font font, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return g.MeasureString(text, font, 100000, _fmt).Width;
    }

    // ---------- 形状/配色助手 ----------

    /// <summary>药丸形（两端半圆）路径；w ≤ h 时退化为椭圆。</summary>
    private static GraphicsPath PillPath(float x, float y, float w, float h)
    {
        var p = new GraphicsPath();
        if (w <= h)
        {
            p.AddEllipse(x, y, w, h);
            return p;
        }
        float d = h;
        p.AddArc(x, y, d, d, 180, 90);                    // 左上
        p.AddArc(x + w - d, y, d, d, 270, 90);           // 右上
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);     // 右下
        p.AddArc(x, y + h - d, d, d, 90, 90);            // 左下
        p.CloseFigure();
        return p;
    }

    /// <summary>0xRRGGBB → 不透明 GDI+ Color（本分部色板统一 RGB 定义，无需 BGR 换算）。</summary>
    private static Color C(uint rgb, int alpha = 255) =>
        Color.FromArgb(alpha, (int)(rgb >> 16) & 0xFF, (int)(rgb >> 8) & 0xFF, (int)rgb & 0xFF);

    private static uint SegmentColorRgb(TaskbarMetricColor color, int theme) => color switch
    {
        TaskbarMetricColor.Warn => WARN_RGB[theme],
        TaskbarMetricColor.Crit => CRIT_RGB[theme],
        _ => VALUE_RGB[theme]
    };

    private static uint BarColorRgb(TaskbarMetricColor color, int theme) => color switch
    {
        TaskbarMetricColor.Warn => BAR_WARN[theme],
        TaskbarMetricColor.Crit => BAR_CRIT[theme],
        _ => BAR_SAFE[theme]
    };
}
