"""
PChabit icon generator
======================
Design: rounded deep-teal tile + 3 ascending orange bars + cyan trend line

Brand palette (kept from the existing logo):
  tile gradient  #16424D -> #0A262E
  bars           #F59E0B / #FBBF24
  trend accent   #67E8F9

Rules:
  - rounded tile radius = 22% of edge (Win11 style)
  - 4x supersampling then LANCZOS downscale for clean edges
  - trend line is dropped below 32px so taskbar icons stay legible
  - output: app assets, tray states, browser extension icons, pchabit.ico
"""

import os
from PIL import Image, ImageDraw, ImageEnhance

# ===== palette =====
BG_TOP = (22, 66, 77, 255)       # #16424D
BG_BOTTOM = (10, 38, 46, 255)    # #0A262E
BAR = (245, 158, 11, 255)        # #F59E0B
BAR_TOP = (251, 191, 36, 255)    # #FBBF24
CYAN = (103, 232, 249, 255)      # #67E8F9

SS = 4  # supersampling factor

# ===== output directories =====
ROOT = os.path.dirname(os.path.abspath(__file__))
OUT_APP = os.path.join(ROOT, "..", "src", "PChabit.App", "Assets")
OUT_APP_ICO = os.path.join(ROOT, "..", "src", "PChabit.App")
OUT_EXT = os.path.join(ROOT, "..", "extensions", "tai-browser-extension", "icons")
OUT_TRAY = os.path.join(ROOT, "icons-tray")
OUT_PREVIEW = os.path.join(ROOT, "preview")

for d in [OUT_APP, OUT_APP_ICO, OUT_EXT, OUT_TRAY, OUT_PREVIEW]:
    os.makedirs(d, exist_ok=True)


def _vertical_gradient(size):
    """Vertical gradient used as the tile background."""
    img = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(img)
    for y in range(size):
        t = y / max(1, size - 1)
        color = tuple(
            int(BG_TOP[i] * (1 - t) + BG_BOTTOM[i] * t) for i in range(4)
        )
        draw.line([(0, y), (size, y)], fill=color)
    return img


def draw_icon(size, status="running", show_trend=None):
    """Render one square icon tile at the requested pixel size."""
    big = max(size * SS, 64)
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # --- rounded tile with vertical gradient ---
    radius = int(big * 0.22)
    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [(0, 0), (big - 1, big - 1)], radius=radius, fill=255
    )
    img.paste(_vertical_gradient(big), (0, 0), mask)

    # --- three ascending bars ---
    bar_w = big * 0.155
    gap = big * 0.085
    base_y = big * 0.775
    heights = (big * 0.28, big * 0.42, big * 0.56)
    total_w = bar_w * 3 + gap * 2
    x0 = (big - total_w) / 2

    centers = []
    for i, h in enumerate(heights):
        x = x0 + i * (bar_w + gap)
        fill = BAR_TOP if i == 1 else BAR
        draw.rounded_rectangle(
            [(x, base_y - h), (x + bar_w, base_y)],
            radius=bar_w * 0.35,
            fill=fill,
        )
        centers.append((x + bar_w / 2, base_y - h))

    # --- cyan trend line (large sizes only) ---
    if show_trend is None:
        show_trend = size >= 48
    if show_trend:
        line_w = max(1, int(big * 0.03))
        start_x, start_y = centers[0][0] + big * 0.01, centers[0][1] - big * 0.075
        end_x, end_y = centers[2][0], centers[2][1] - big * 0.075
        draw.line([(start_x, start_y), (end_x, end_y)], fill=CYAN, width=line_w)
        # round caps at both ends
        r = line_w / 2
        for cx, cy in ((start_x, start_y), (end_x, end_y)):
            draw.ellipse([(cx - r, cy - r), (cx + r, cy + r)], fill=CYAN)
        # accent dot at the top of the trend line
        dot_r = big * 0.042
        draw.ellipse(
            [(end_x - dot_r, end_y - dot_r), (end_x + dot_r, end_y + dot_r)],
            fill=CYAN,
        )

    img = img.resize((size, size), Image.LANCZOS)

    # --- state variants (tray) ---
    if status == "paused":
        rgb = ImageEnhance.Color(img).enhance(0.25)
        img = rgb
    elif status == "disabled":
        gray = img.convert("L").convert("RGBA")
        img = ImageEnhance.Brightness(gray).enhance(0.78)

    return img


def make_ico(images, out_path):
    """Write a multi-size Windows ICO from per-size rendered images."""
    sizes = sorted(images.keys())
    images[sizes[-1]].save(
        out_path,
        format="ICO",
        sizes=[(s, s) for s in sizes],
        append_images=[images[s] for s in sizes[:-1]],
    )


def gen_app_icons():
    print("== app assets ==")
    targets = {
        "StoreLogo.png": 50,
        "Square44x44Logo.png": 44,
        "Square44x44Logo.targetsize-24_altform-unplated.png": 24,
        "LockScreenLogo.png": 24,
        "Square150x150Logo.png": 150,
        "Wide310x150Logo.png": 310,
        "SplashScreen.png": 620,
        "Square44x44Logo.scale-200.png": 88,
        "Square150x150Logo.scale-200.png": 300,
        "Wide310x150Logo.scale-200.png": 620,
        "LockScreenLogo.scale-200.png": 48,
        "SplashScreen.scale-200.png": 1240,
    }

    for name, size in targets.items():
        if "SplashScreen" in name and size >= 600:
            # full-bleed background with a centered mark
            canvas = _vertical_gradient(size).resize((size, size), Image.LANCZOS)
            inner = draw_icon(size // 4)
            canvas.alpha_composite(inner, ((size - inner.width) // 2, (size - inner.height) // 2))
            img = canvas
        else:
            img = draw_icon(size)
        img.save(os.path.join(OUT_APP, name), "PNG")
        print(f"   ok {name} ({size}px)")

    # shell navigation header
    draw_icon(96).save(os.path.join(OUT_APP, "Logo.png"), "PNG")
    print("   ok Logo.png (96px)")

    # executable / window / taskbar icon
    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    ico_images = {s: draw_icon(s) for s in ico_sizes}
    make_ico(ico_images, os.path.join(OUT_APP_ICO, "pchabit.ico"))
    print("   ok pchabit.ico")


def gen_extension_icons():
    print("== extension icons ==")
    for name, size in {"icon16.png": 16, "icon48.png": 48, "icon128.png": 128}.items():
        draw_icon(size).save(os.path.join(OUT_EXT, name), "PNG")
        print(f"   ok {name} ({size}px)")


def gen_tray_icons():
    print("== tray icons ==")
    sizes = [16, 24, 32, 48, 64, 128, 256]
    for state in ("running", "paused", "disabled"):
        imgs = {s: draw_icon(s, status=state) for s in sizes}
        make_ico(imgs, os.path.join(OUT_TRAY, f"tray-{state}.ico"))
        print(f"   ok tray-{state}.ico")

    for s in (16, 32, 48):
        draw_icon(s).save(os.path.join(OUT_TRAY, f"tray-{s}.png"), "PNG")


def gen_preview():
    """Contact sheet for quick visual review."""
    print("== preview ==")
    widths, heights = 1100, 460
    canvas = Image.new("RGBA", (widths, heights), (245, 246, 248, 255))
    draw = ImageDraw.Draw(canvas)
    draw.text((32, 24), "PChabit icon preview", fill=(32, 36, 40, 255))

    x = 32
    for s in (256, 128, 64):
        img = draw_icon(s)
        canvas.alpha_composite(img, (x, 60))
        draw.text((x, 60 + 266), f"{s}px", fill=(90, 96, 104, 255))
        x += 300

    x = 620
    for s in (44, 32, 16):
        img = draw_icon(s)
        canvas.alpha_composite(img, (x, 60 + (128 - s) // 2))
        draw.text((x, 60 + 266), f"{s}px", fill=(90, 96, 104, 255))
        x += 90

    y = 340
    x = 32
    for label, state in (("running", "running"), ("paused", "paused"), ("disabled", "disabled")):
        img = draw_icon(64, status=state)
        canvas.alpha_composite(img, (x, y))
        draw.text((x, y + 72), label, fill=(90, 96, 104, 255))
        x += 92

    canvas.save(os.path.join(OUT_PREVIEW, "overview.png"), "PNG")
    print("   ok overview.png")


if __name__ == "__main__":
    print("PChabit icon generator")
    gen_app_icons()
    gen_extension_icons()
    gen_tray_icons()
    gen_preview()
    print("done")
