using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Serilog;

namespace PChabit.App.Services;

/// <summary>
/// PChabit 托盘图标渲染器
/// 内存中生成 16×16 HICON，进度环实时反映"今日已用时长 / 每日总目标"
///
/// 设计规范见 docs/图标系统设计规范.md
/// 几何按 16px 基准绘制，其他尺寸按比例缩放：
///   - 圆角 = 边长 × 18%
///   - 显示器 = 边长 × 50% × 32%
///   - 进度环半径 = 边长 × 18%
///   - 描边 = 边长 × 5%
/// </summary>
public static class IconRenderer
{
    // 品牌色（与 docs/图标系统设计规范.md 第三节一致）
    private static readonly Color Primary   = Color.FromArgb(255, 27, 58, 111);   // #1B3A6F
    private static readonly Color Running   = Color.FromArgb(255, 34, 197, 94);  // #22C55E
    private static readonly Color Paused    = Color.FromArgb(255, 245, 158, 11); // #F59E0B
    private static readonly Color Disabled  = Color.FromArgb(255, 148, 163, 184);// #94A3B8
    private static readonly Color Foreground = Color.FromArgb(255, 255, 255, 255);

    /// <summary>
    /// 在内存中生成托盘图标 HICON
    /// </summary>
    /// <param name="progress">今日进度 0.0~1.0（null=不画进度环）</param>
    /// <param name="status">运行/暂停/禁用</param>
    /// <param name="size">像素（推荐 16，托盘实际显示尺寸）</param>
    /// <returns>HICON 句柄；调用方负责 DestroyIcon</returns>
    public static IntPtr CreateTrayIcon(double? progress, TrayStatus status, int size = 16)
    {
        try
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode    = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode  = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);

                DrawIcon(g, size, progress, status);
            }

            // Bitmap.GetHicon() 会创建一份 HICON 副本，调用方需 DestroyIcon
            return bmp.GetHicon();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "IconRenderer.CreateTrayIcon 失败: size={Size}", size);
            return IntPtr.Zero;
        }
    }

    private static void DrawIcon(Graphics g, int size, double? progress, TrayStatus status)
    {
        var radius = size * 0.18f;
        var bg = status switch
        {
            TrayStatus.Paused   => Color.FromArgb(255, 90, 100, 130),   // 主色降饱和
            TrayStatus.Disabled => Disabled,
            _                   => Primary
        };

        // 1. 圆角矩形容器
        using (var bgPath = RoundedRectPath(0, 0, size - 1, size - 1, radius))
        using (var bgBrush = new SolidBrush(bg))
        {
            g.FillPath(bgBrush, bgPath);
        }

        // 2. 显示器外框 + 底座
        var monW    = size * 0.50f;
        var monH    = size * 0.32f;
        var monX    = (size - monW) / 2f;
        var monY    = size * 0.18f;
        var monR    = size * 0.06f;
        var stroke  = Math.Max(1f, size * 0.050f);
        var standCx = size / 2f;
        var standTopY = monY + monH;
        var standBottomY = standTopY + size * 0.08f;
        var baseW   = monW * 0.50f;

        using (var pen = new Pen(Foreground, stroke))
        {
            pen.LineJoin = LineJoin.Round;
            pen.StartCap = LineCap.Round;
            pen.EndCap   = LineCap.Round;

            // 显示器
            g.DrawPath(pen, RoundedRectPath(monX, monY, monW, monH, monR));
            // 底座立柱
            g.DrawLine(pen, standCx, standTopY, standCx, standBottomY);
            // 底座横线
            g.DrawLine(pen, standCx - baseW / 2f, standBottomY,
                                 standCx + baseW / 2f, standBottomY);
        }

        // 3. 进度环（围绕在底座下方）
        if (progress.HasValue && size >= 24)
        {
            var ringCx = size / 2f;
            var ringCy = standBottomY + size * 0.22f;
            var ringR  = size * 0.18f;
            var ringStroke = Math.Max(1f, size * 0.052f);

            // 进度环颜色按状态切换
            var arcColor = status switch
            {
                TrayStatus.Running  => Running,
                TrayStatus.Paused   => Paused,
                TrayStatus.Disabled => Disabled,
                _                   => Running
            };

            // 3a. 背景环（淡）
            using (var trackPen = new Pen(Color.FromArgb(50, 255, 255, 255), ringStroke))
            {
                trackPen.StartCap = LineCap.Round;
                trackPen.EndCap   = LineCap.Round;
                g.DrawEllipse(trackPen, ringCx - ringR, ringCy - ringR, ringR * 2, ringR * 2);
            }

            // 3b. 进度弧（从 12 点钟方向顺时针）
            var p = Math.Clamp(progress.Value, 0.0, 1.0);
            if (p > 0.001)
            {
                using var arcPen = new Pen(arcColor, ringStroke);
                arcPen.StartCap = LineCap.Round;
                arcPen.EndCap   = LineCap.Round;
                // Graphics.DrawArc 的 startAngle 是按顺时针 0° = 3 点钟方向
                // 我们要 12 点钟起笔 → startAngle = -90
                g.DrawArc(arcPen, ringCx - ringR, ringCy - ringR, ringR * 2, ringR * 2,
                    -90f, 360f * (float)p);
            }
        }
    }

    private static GraphicsPath RoundedRectPath(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        if (w <= 0 || h <= 0) return path;
        if (r <= 0)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
            return path;
        }
        var d = r * 2f;
        path.AddArc(x,         y,         d, d, 180, 90);
        path.AddArc(x + w - d, y,         d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d,   0, 90);
        path.AddArc(x,         y + h - d, d, d,  90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// 托盘图标运行状态
/// </summary>
public enum TrayStatus
{
    Running  = 0,  // 绿色进度环
    Paused   = 1,  // 橙色进度环
    Disabled = 2   // 灰色（无可用数据）
}
