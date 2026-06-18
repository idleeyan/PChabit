"""
PChabit 图标生成器
==================
方向 A：显示器 + 进度环
品牌色：#1B3A6F (深蓝)  |  进度环：白色  |  状态点：#22C55E (绿) / #F59E0B (橙暂停)

设计语言：
  - 圆角矩形容器，圆角 = 边长 × 20%（Win11 风格）
  - 显示器图形：从 LOGO 提取，描边粗细按比例缩放
  - 进度环：75% 圆周，从 12 点钟方向起笔 (rotate -90°)
  - 状态点：仅大尺寸（>= 64px）显示
"""

import os
import math
from PIL import Image, ImageDraw, ImageFilter

# ===== 品牌色板 =====
COLOR_BG       = (27, 58, 111, 255)   # #1B3A6F  主色
COLOR_FG       = (255, 255, 255, 255) # #FFFFFF  前景
COLOR_RUNNING  = (34, 197, 94, 255)   # #22C55E  运行中
COLOR_PAUSED   = (245, 158, 11, 255)  # #F59E0B  暂停
COLOR_DISABLED = (148, 163, 184, 255) # #94A3B8  不可用
COLOR_BG_LIGHT = (240, 244, 250, 255) # 浅色模式背景

# ===== 输出目录 =====
ROOT = os.path.dirname(os.path.abspath(__file__))
OUT_APP        = os.path.join(ROOT, "..", "src", "PChabit.App", "Assets")
OUT_EXT        = os.path.join(ROOT, "..", "extensions", "tai-browser-extension", "icons")
OUT_TRAY       = os.path.join(ROOT, "icons-tray")
OUT_SVG        = os.path.join(ROOT, "icons-svg")
OUT_PREVIEW    = os.path.join(ROOT, "preview")

for d in [OUT_APP, OUT_EXT, OUT_TRAY, OUT_SVG, OUT_PREVIEW]:
    os.makedirs(d, exist_ok=True)


# ===== 核心绘制函数 =====
def draw_icon(size, progress=0.75, status="running", light=False, show_status_dot=True, png_alpha=True):
    """
    绘制单个图标
    :param size: 画布尺寸
    :param progress: 进度环进度 0.0~1.0 (None 表示无进度环)
    :param status: running | paused | disabled
    :param light: 是否浅色模式（白底蓝前景）
    :param show_status_dot: 是否显示状态点
    :return: PIL Image (RGBA)
    """
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    bg = COLOR_BG_LIGHT if light else COLOR_BG
    fg = COLOR_BG if light else COLOR_FG

    # --- 1. 圆角矩形容器 ---
    radius = int(size * 0.20)
    if light:
        # 浅色模式：白底 + 描边
        draw.rounded_rectangle(
            [(0, 0), (size - 1, size - 1)],
            radius=radius,
            fill=bg,
            outline=COLOR_BG,
            width=max(1, size // 32),
        )
    else:
        draw.rounded_rectangle(
            [(0, 0), (size - 1, size - 1)],
            radius=radius,
            fill=bg,
        )

    # 暂停/禁用状态降饱和
    if status == "paused":
        bg_dim = tuple(int(c * 0.65) for c in bg[:3]) + (bg[3],)
        draw.rounded_rectangle([(0, 0), (size - 1, size - 1)], radius=radius, fill=bg_dim)
    elif status == "disabled":
        bg_dim = (148, 163, 184, 255)
        draw.rounded_rectangle([(0, 0), (size - 1, size - 1)], radius=radius, fill=bg_dim)

    # --- 2. 显示器（核心符号）---
    # 显示器外框
    mon_w = size * 0.50
    mon_h = size * 0.32
    mon_x = (size - mon_w) / 2
    mon_y = size * 0.18
    mon_radius = size * 0.06
    stroke_w = max(1, int(size * 0.050))
    draw.rounded_rectangle(
        [(mon_x, mon_y), (mon_x + mon_w, mon_y + mon_h)],
        radius=mon_radius,
        outline=fg,
        width=stroke_w,
    )

    # 显示器底座（梯形简化成 T）
    stand_top_x = size / 2
    stand_top_y = mon_y + mon_h
    stand_bottom_y = stand_top_y + size * 0.08
    draw.line(
        [(stand_top_x, stand_top_y), (stand_top_x, stand_bottom_y)],
        fill=fg,
        width=stroke_w,
    )
    # 底座横线
    base_w = mon_w * 0.50
    draw.line(
        [(stand_top_x - base_w / 2, stand_bottom_y), (stand_top_x + base_w / 2, stand_bottom_y)],
        fill=fg,
        width=stroke_w,
    )

    # --- 3. 进度环（围绕在显示器下方，与底座融合）---
    # 小尺寸 (< 24px) 跳过进度环，避免糊掉
    if progress is not None and size >= 24:
        cx, cy = size / 2, stand_bottom_y + size * 0.22
        r = size * 0.18
        ring_w = max(1, int(size * 0.052))

        # 背景环（淡）
        track_color = (*fg[:3], 50)
        draw.arc(
            [(cx - r, cy - r), (cx + r, cy + r)],
            start=0, end=360,
            fill=track_color,
            width=ring_w,
        )
        # 进度弧
        if progress > 0:
            end_angle = -90 + 360 * min(progress, 1.0)
            arc_color = COLOR_RUNNING if status == "running" else (COLOR_PAUSED if status == "paused" else COLOR_DISABLED)
            draw.arc(
                [(cx - r, cy - r), (cx + r, cy + r)],
                start=-90, end=end_angle,
                fill=arc_color,
                width=ring_w,
            )

    # --- 4. 状态点 ---
    if show_status_dot and size >= 64 and status == "running":
        dot_r = size * 0.06
        dot_cx = size - size * 0.18
        dot_cy = size * 0.18
        # 外圈描边（与背景同色，制造切割感）
        draw.ellipse(
            [(dot_cx - dot_r - size * 0.012, dot_cy - dot_r - size * 0.012),
             (dot_cx + dot_r + size * 0.012, dot_cy + dot_r + size * 0.012)],
            fill=COLOR_BG,
        )
        draw.ellipse(
            [(dot_cx - dot_r, dot_cy - dot_r),
             (dot_cx + dot_r, dot_cy + dot_r)],
            fill=COLOR_RUNNING,
        )

    return img


def make_ico(images_dict, out_path):
    """
    生成 Windows ICO 多尺寸文件
    images_dict: {size: PIL.Image}
    """
    sizes = sorted(images_dict.keys())
    base = images_dict[sizes[-1]]
    base.save(
        out_path,
        format="ICO",
        sizes=[(s, s) for s in sizes],
        append_images=[images_dict[s] for s in sizes[:-1]],
    )


# ===== 应用图标（替换 WinUI 资源）=====
def gen_app_icons():
    print("== 生成主应用图标 ==")
    sizes = {
        "StoreLogo.png":            50,
        "Square44x44Logo.png":      44,
        "Square44x44Logo.targetsize-24_altform-unplated.png": 24,
        "LockScreenLogo.png":       24,
        "Square150x150Logo.png":    150,
        "Wide310x150Logo.png":      310,
        "SplashScreen.png":         620,
    }
    scale200 = {
        "Square44x44Logo.scale-200.png":            88,
        "Square150x150Logo.scale-200.png":          300,
        "Wide310x150Logo.scale-200.png":            620,
        "LockScreenLogo.scale-200.png":             48,
        "SplashScreen.scale-200.png":              1240,
    }
    for name, size in {**sizes, **scale200}.items():
        show_dot = size >= 64
        img = draw_icon(size, progress=0.75, status="running", show_status_dot=show_dot)
        # SplashScreen 全屏适配（WinUI 要求）
        if "SplashScreen" in name and size >= 600:
            img = draw_icon(size // 4, progress=0.75, show_status_dot=True)
            canvas = Image.new("RGBA", (size, size), COLOR_BG)
            inner = img
            cx = (size - inner.width) // 2
            cy = (size - inner.height) // 2
            canvas.alpha_composite(inner, (cx, cy))
            img = canvas
        img.save(os.path.join(OUT_APP, name), "PNG")
        print(f"   ✓ {name}  ({size}px)")

    # 同时存一份 Logo.png（ShellPage 导航头引用）
    img = draw_icon(96, progress=0.75, show_status_dot=True)
    img.save(os.path.join(OUT_APP, "Logo.png"), "PNG")
    print(f"   ✓ Logo.png  (96px)")


# ===== 浏览器扩展图标 =====
def gen_extension_icons():
    print("→ 生成浏览器扩展图标 ...")
    sizes = {"icon16.png": 16, "icon48.png": 48, "icon128.png": 128}
    for name, size in sizes.items():
        show_dot = size >= 48
        img = draw_icon(size, progress=0.75, show_status_dot=show_dot)
        img.save(os.path.join(OUT_EXT, name), "PNG")
        print(f"   ✓ {name}  ({size}px)")


# ===== 托盘 ICO =====
def gen_tray_icons():
    print("→ 生成系统托盘 ICO ...")
    base_sizes = [16, 24, 32, 48, 64, 128, 256]

    # 1. 默认/运行中 状态
    running_imgs = {s: draw_icon(s, progress=0.75, status="running", show_status_dot=(s >= 64)) for s in base_sizes}
    make_ico(running_imgs, os.path.join(OUT_TRAY, "tray-running.ico"))
    print("   ✓ tray-running.ico")

    # 2. 暂停状态
    paused_imgs = {s: draw_icon(s, progress=0.75, status="paused", show_status_dot=False) for s in base_sizes}
    make_ico(paused_imgs, os.path.join(OUT_TRAY, "tray-paused.ico"))
    print("   ✓ tray-paused.ico")

    # 3. 禁用状态
    disabled_imgs = {s: draw_icon(s, progress=0.0, status="disabled", show_status_dot=False) for s in base_sizes}
    make_ico(disabled_imgs, os.path.join(OUT_TRAY, "tray-disabled.ico"))
    print("   ✓ tray-disabled.ico")

    # 4. 单帧 PNG（备用）
    for s in [16, 32, 48]:
        draw_icon(s, progress=0.75, status="running", show_status_dot=(s >= 64)).save(
            os.path.join(OUT_TRAY, f"tray-{s}.png"), "PNG"
        )


# ===== SVG 源文件 =====
def gen_svg_sources():
    """生成可缩放的 SVG 源文件，方便后续修改"""
    print("→ 生成 SVG 源文件 ...")
    svg_template = '''<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">
  <title>PChabit icon</title>
  <rect width="{size}" height="{size}" rx="{radius}" ry="{radius}" fill="#1B3A6F"/>
  <!-- 显示器 -->
  <rect x="{mon_x}" y="{mon_y}" width="{mon_w}" height="{mon_h}" rx="{mon_r}" ry="{mon_r}"
        fill="none" stroke="#FFFFFF" stroke-width="{sw}" stroke-linejoin="round"/>
  <line x1="{stand_cx}" y1="{stand_top_y}" x2="{stand_cx}" y2="{stand_bottom_y}"
        stroke="#FFFFFF" stroke-width="{sw}" stroke-linecap="round"/>
  <line x1="{base_x1}" y1="{stand_bottom_y}" x2="{base_x2}" y2="{stand_bottom_y}"
        stroke="#FFFFFF" stroke-width="{sw}" stroke-linecap="round"/>
  <!-- 进度环 -->
  <circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="#FFFFFF" stroke-opacity="0.2" stroke-width="{rw}"/>
  <circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="#22C55E" stroke-width="{rw}"
          stroke-dasharray="{arc_len} {full_len}" stroke-linecap="round"
          transform="rotate(-90 {cx} {cy})"/>
  <!-- 状态点 -->
  <circle cx="{dot_cx}" cy="{dot_cy}" r="{dot_r_outer}" fill="#1B3A6F"/>
  <circle cx="{dot_cx}" cy="{dot_cy}" r="{dot_r}" fill="#22C55E"/>
</svg>
'''

    def calc(size, progress=0.75, show_dot=True):
        radius = size * 0.20
        mon_w = size * 0.46
        mon_h = size * 0.30
        mon_x = (size - mon_w) / 2
        mon_y = size * 0.24
        mon_r = size * 0.05
        sw = max(1, size * 0.045)
        stand_cx = size / 2
        stand_top_y = mon_y + mon_h
        stand_bottom_y = stand_top_y + size * 0.10
        base_w = mon_w * 0.42
        base_x1 = stand_cx - base_w / 2
        base_x2 = stand_cx + base_w / 2
        cx = size / 2
        cy = mon_y + mon_h + size * 0.18
        r = size * 0.16
        rw = max(1, size * 0.045)
        full_len = 2 * math.pi * r
        arc_len = full_len * progress
        dot_r = size * 0.06 if show_dot and size >= 64 else 0
        dot_r_outer = dot_r + size * 0.012 if show_dot and size >= 64 else 0
        dot_cx = size - size * 0.18
        dot_cy = size * 0.18
        return dict(
            size=int(size), radius=f"{radius:.2f}",
            mon_x=f"{mon_x:.2f}", mon_y=f"{mon_y:.2f}",
            mon_w=f"{mon_w:.2f}", mon_h=f"{mon_h:.2f}", mon_r=f"{mon_r:.2f}",
            sw=f"{sw:.2f}", stand_cx=f"{stand_cx:.2f}",
            stand_top_y=f"{stand_top_y:.2f}", stand_bottom_y=f"{stand_bottom_y:.2f}",
            base_x1=f"{base_x1:.2f}", base_x2=f"{base_x2:.2f}",
            cx=f"{cx:.2f}", cy=f"{cy:.2f}", r=f"{r:.2f}", rw=f"{rw:.2f}",
            arc_len=f"{arc_len:.2f}", full_len=f"{full_len:.2f}",
            dot_r=f"{dot_r:.2f}", dot_r_outer=f"{dot_r_outer:.2f}",
            dot_cx=f"{dot_cx:.2f}", dot_cy=f"{dot_cy:.2f}",
        )

    # 主应用 SVG
    img = draw_icon(256, progress=0.75, show_status_dot=True)
    img.save(os.path.join(OUT_PREVIEW, "app-256.png"), "PNG")

    # 扩展 SVG 三件套
    for name, size in [("icon16", 16), ("icon48", 48), ("icon128", 128)]:
        params = calc(size, show_dot=size >= 48)
        svg = svg_template.format(**params)
        with open(os.path.join(OUT_EXT, f"{name}.svg"), "w", encoding="utf-8") as f:
            f.write(svg)
        print(f"   ✓ {name}.svg  ({size}px)")


# ===== 预览拼图 =====
def gen_preview():
    print("→ 生成预览拼图 ...")
    canvas_w, canvas_h = 1200, 700
    canvas = Image.new("RGBA", (canvas_w, canvas_h), (245, 244, 240, 255))
    draw = ImageDraw.Draw(canvas)

    # 标题
    draw.text((40, 30), "PChabit Icon System", fill=(44, 44, 42, 255))
    draw.text((40, 52), "方向 A · 显示器 + 进度环 · WinUI + 扩展 + 托盘", fill=(95, 94, 90, 255))

    # 应用图标三件套
    draw.text((40, 100), "App", fill=(44, 44, 42, 255))
    y0 = 120
    sizes = [(256, 0), (150, 280), (44, 460), (32, 540), (16, 600)]
    for s, x in sizes:
        img = draw_icon(s, show_status_dot=(s >= 64))
        canvas.paste(img, (40 + x, y0 + (256 - s) // 2), img)
        draw.text((40 + x + s // 2 - 12, y0 + 256 + 10), f"{s}", fill=(95, 94, 90, 255))

    # 状态对比
    draw.text((40, 410), "Tray States", fill=(44, 44, 42, 255))
    states = [("Running", "running", 0.78), ("Paused", "paused", 0.78), ("Disabled", "disabled", 0.0)]
    for i, (label, st, prog) in enumerate(states):
        img = draw_icon(96, progress=prog, status=st, show_status_dot=(st == "running"))
        canvas.paste(img, (40 + i * 120, 430), img)
        draw.text((40 + i * 120 + 20, 540), label, fill=(95, 94, 90, 255))

    # 扩展图标
    draw.text((40, 580), "Browser Extension", fill=(44, 44, 42, 255))
    for i, s in enumerate([16, 48, 128]):
        img = draw_icon(s, show_status_dot=(s >= 48))
        canvas.paste(img, (40 + i * 140, 600), img)
        draw.text((40 + i * 140 + 20, 600 + 130 + 10), f"{s}px", fill=(95, 94, 90, 255))

    # 进度环对比
    draw.text((500, 100), "Progress Ring", fill=(44, 44, 42, 255))
    for i, p in enumerate([0.0, 0.25, 0.5, 0.75, 1.0]):
        img = draw_icon(96, progress=p, show_status_dot=True)
        canvas.paste(img, (500 + i * 110, 120), img)
        draw.text((500 + i * 110 + 30, 230), f"{int(p*100)}%", fill=(95, 94, 90, 255))

    # 色板
    draw.text((500, 280), "Palette", fill=(44, 44, 42, 255))
    colors = [("Primary", COLOR_BG), ("Running", COLOR_RUNNING), ("Paused", COLOR_PAUSED), ("FG", COLOR_FG)]
    for i, (label, c) in enumerate(colors):
        x = 500 + i * 110
        sw = Image.new("RGBA", (80, 80), c)
        canvas.paste(sw, (x, 300), sw)
        draw.text((x + 5, 390), label, fill=(95, 94, 90, 255))
        hex_code = "#{:02X}{:02X}{:02X}".format(*c[:3])
        draw.text((x + 5, 405), hex_code, fill=(44, 44, 42, 255))

    canvas.save(os.path.join(OUT_PREVIEW, "overview.png"), "PNG")
    print(f"   ✓ overview.png  ({canvas_w}×{canvas_h})")


if __name__ == "__main__":
    print("=" * 60)
    print("  PChabit 图标生成器")
    print("=" * 60)
    gen_app_icons()
    gen_extension_icons()
    gen_tray_icons()
    gen_svg_sources()
    gen_preview()
    print()
    print("✅ 全部完成")
    print(f"   预览图: {OUT_PREVIEW}/overview.png")
    print(f"   托盘 ICO: {OUT_TRAY}/")
    print(f"   扩展 SVG: {OUT_EXT}/")
