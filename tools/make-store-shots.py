#!/usr/bin/env python3
"""生成微软商店规格截图（1920x1080）。

依赖：Pillow（开发期用 DSH 自带 Python 即可，不进 MSIX）。
输入：docs/images/widbar-*.png（真机截图）、src/GachaWidget/Assets/Square150x150Logo.png
输出：store/screenshots/*.png（1920x1080；商店截图最小 1366x768）

用法：python tools/make-store-shots.py
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
IMAGES = ROOT / "docs" / "images"
ASSETS = ROOT / "src" / "GachaWidget" / "Assets"
OUT = ROOT / "store" / "screenshots"
OUT.mkdir(parents=True, exist_ok=True)

W, H = 1920, 1080
BG_TOP = (247, 250, 254)
BG_BOTTOM = (226, 235, 248)
INK = (17, 24, 39)
INK_SOFT = (71, 84, 103)
FONT_BOLD = "C:/Windows/Fonts/msyhbd.ttc"
FONT_REG = "C:/Windows/Fonts/msyh.ttc"
SHADOW = (23, 43, 77, 46)


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT_REG, size)


def vertical_gradient(size, top, bottom) -> Image.Image:
    w, h = size
    img = Image.new("RGB", (1, h))
    px = img.load()
    for y in range(h):
        t = y / max(1, h - 1)
        px[0, y] = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
    return img.resize((w, h), Image.Resampling.BILINEAR)


def glow(base: Image.Image, center, radius, color, alpha) -> None:
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse(
        [center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius],
        fill=color + (alpha,),
    )
    base.alpha_composite(layer.filter(ImageFilter.GaussianBlur(radius * 0.45)))


def card(base: Image.Image, x, y, w, h, radius=22, fill=(255, 255, 255, 246)) -> None:
    """带柔和阴影的圆角卡片，画在 (x, y)。"""
    pad = 70
    layer = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle(
        [pad, pad + 18, pad + w, pad + h + 18], radius=radius, fill=SHADOW
    )
    base.alpha_composite(layer.filter(ImageFilter.GaussianBlur(26)), (x - pad, y - pad))

    body = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(body).rounded_rectangle(
        [0, 0, w - 1, h - 1], radius=radius, fill=fill, outline=(221, 227, 236, 255), width=2
    )
    base.alpha_composite(body, (x, y))


def screenshot(base: Image.Image, path: Path, scale: float, x, y, radius=18) -> tuple[int, int]:
    shot = Image.open(path).convert("RGBA")
    shot = shot.resize((round(shot.width * scale), round(shot.height * scale)), Image.Resampling.LANCZOS)
    card(base, x, y, shot.width, shot.height, radius=radius, fill=(255, 255, 255, 0))
    base.alpha_composite(shot, (x, y))
    return shot.size


def badge(base: Image.Image) -> None:
    icon = Image.open(ASSETS / "Square150x150Logo.png").convert("RGBA").resize((64, 64), Image.Resampling.LANCZOS)
    base.alpha_composite(icon, (96, 72))
    d = ImageDraw.Draw(base)
    d.text((176, 74), "二游日历", font=font(30, True), fill=INK)
    d.text((178, 112), "WidBar 任务栏小组件", font=font(17), fill=INK_SOFT)


def caption(d, x, y, title_lines, bullets, title_size=46):
    for i, line in enumerate(title_lines):
        d.text((x, y + i * (title_size + 14)), line, font=font(title_size, True), fill=INK)
    by = y + len(title_lines) * (title_size + 14) + 30
    for i, (color, text) in enumerate(bullets):
        cy = by + i * 48
        d.ellipse([x + 2, cy + 13, x + 14, cy + 25], fill=color)
        d.text((x + 30, cy), text, font=font(23), fill=INK_SOFT)


def phase_bg() -> Image.Image:
    base = vertical_gradient((W, H), BG_TOP, BG_BOTTOM).convert("RGBA")
    glow(base, (1580, 120), 520, (79, 125, 243), 34)
    glow(base, (260, 980), 480, (224, 112, 159), 26)
    glow(base, (1100, 620), 640, (255, 255, 255), 90)
    return base


def shot_preview() -> None:
    base = phase_bg()
    badge(base)
    d = ImageDraw.Draw(base)
    d.text((96, 214), "任务栏上，直接看还剩多久", font=font(58, True), fill=INK)
    d.text((98, 302), "28 款二游的卡池与活动倒计时，常驻 Windows 11 任务栏", font=font(26), fill=INK_SOFT)

    # 真实任务栏截图（含 Windows 11 任务栏图标），按 1.2 倍放到画面中
    strip = Image.open(IMAGES / "widbar-taskbar.png").convert("RGBA")
    strip = strip.resize((round(strip.width * 1.2), round(strip.height * 1.2)), Image.Resampling.LANCZOS)
    mask = Image.new("L", strip.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, strip.width - 1, strip.height - 1], radius=14, fill=255)
    sx = (W - strip.width) // 2
    sy = 548
    card(base, sx - 2, sy - 2, strip.width + 4, strip.height + 4, radius=16, fill=(255, 255, 255, 0))
    base.paste(strip, (sx, sy), mask)
    d = ImageDraw.Draw(base)
    d.text((96, 700), "真实任务栏截图：3 条最近结束 · 色点 = 游戏 · 加粗 = 剩余时间", font=font(22), fill=INK_SOFT)
    d.text((96, 872), "断网自动回退到上次缓存；连示例数据都会标出来，不会假装最新。", font=font(22), fill=INK_SOFT)
    base.convert("RGB").save(OUT / "01-taskbar-preview.png", quality=95)


def shot_flyout() -> None:
    base = phase_bg()
    badge(base)
    d = ImageDraw.Draw(base)
    caption(
        d, 96, 190,
        ["点一下，", "展开 14 天日程"],
        [
            ((224, 112, 159), "近期：跨游戏按紧迫度排"),
            ((63, 169, 160), "下一个截止：大号剩余 + 随后两件"),
            ((74, 108, 247), "颜色 = 游戏，图案 = 状态"),
        ],
    )
    screenshot(base, IMAGES / "widbar-flyout.png", 0.90, 690, 120)
    base.convert("RGB").save(OUT / "02-schedule-panel.png", quality=95)


def shot_settings() -> None:
    base = phase_bg()
    badge(base)
    d = ImageDraw.Draw(base)
    caption(
        d, 96, 190,
        ["设置都在", "一张页里"],
        [
            ((74, 108, 247), "显示哪些游戏、谁在上面"),
            ((63, 169, 160), "预览条 1–3 条、刷新间隔"),
            ((224, 112, 159), "数据源可换、内置数据自检"),
        ],
    )
    # 真机 PrintWindow 截图带 1–2px 窗口黑边 + 右下 resize 角，裁掉后再加圆角
    shot = Image.open(IMAGES / "widbar-settings.png").convert("RGBA")
    shot = shot.crop((14, 4, shot.width - 16, shot.height - 20))
    shot = shot.resize((round(shot.width * 0.60), round(shot.height * 0.60)), Image.Resampling.LANCZOS)
    mask = Image.new("L", shot.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, shot.width - 1, shot.height - 1], radius=12, fill=255)
    sx, sy = 780, 170
    card(base, sx, sy, shot.width, shot.height, radius=12, fill=(255, 255, 255, 0))
    base.paste(shot, (sx, sy), mask)
    base.convert("RGB").save(OUT / "03-settings.png", quality=95)


def main() -> None:
    shot_preview()
    shot_flyout()
    shot_settings()
    for p in sorted(OUT.glob("*.png")):
        with Image.open(p) as img:
            print(f"{p.relative_to(ROOT)}  {img.width}x{img.height}  {p.stat().st_size // 1024} KB")


if __name__ == "__main__":
    main()
