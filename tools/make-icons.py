"""生成 WidBar 小组件的图标素材（MSIX 需要的各个尺寸 + 商店用的 620）。

用法：
    python tools/make-icons.py

设计：深蓝紫渐变圆角底 + 白色日历卡片 + 顶部强调色条 + 一个橙色"当天"格子。
全部按 4 倍超采样绘制再缩放，保证小尺寸边缘干净。
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parent.parent
ASSETS = REPO / "src" / "GachaWidget" / "Assets"
DOC_IMAGES = REPO / "docs" / "images"

MASTER = 2480  # 620 * 4
BG_TOP = (91, 91, 247)      # #5B5BF7
BG_BOTTOM = (61, 123, 253)  # #3D7BFD
CARD = (255, 255, 255)
CARD_SHADE = (236, 240, 255)
ACCENT = (77, 107, 254)     # #4D6BFE
TODAY = (255, 176, 32)      # #FFB020
DOT = (176, 187, 214)


def vertical_gradient(size: int, top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    grad = Image.new("RGB", (1, size))
    for y in range(size):
        t = y / max(1, size - 1)
        grad.putpixel(
            (0, y),
            (
                round(top[0] + (bottom[0] - top[0]) * t),
                round(top[1] + (bottom[1] - top[1]) * t),
                round(top[2] + (bottom[2] - top[2]) * t),
            ),
        )
    return grad.resize((size, size), Image.BICUBIC)


def draw_master() -> Image.Image:
    size = MASTER
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # 圆角渐变底
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=int(size * 0.22), fill=255)
    img.paste(vertical_gradient(size, BG_TOP, BG_BOTTOM), (0, 0), mask)

    draw = ImageDraw.Draw(img)

    # 日历卡片
    card_w, card_h = int(size * 0.62), int(size * 0.58)
    card_x = (size - card_w) // 2
    card_y = int(size * 0.24)
    draw.rounded_rectangle(
        (card_x, card_y, card_x + card_w, card_y + card_h),
        radius=int(size * 0.05),
        fill=CARD,
    )

    # 卡片顶部强调色条
    bar_h = int(size * 0.13)
    draw.rounded_rectangle(
        (card_x, card_y, card_x + card_w, card_y + bar_h),
        radius=int(size * 0.05),
        fill=ACCENT,
    )
    draw.rectangle(
        (card_x, card_y + bar_h - int(size * 0.05), card_x + card_w, card_y + bar_h),
        fill=ACCENT,
    )

    # 两个"挂环"
    ring_w = int(size * 0.035)
    ring_h = int(size * 0.11)
    for cx in (card_x + int(card_w * 0.3), card_x + int(card_w * 0.7)):
        draw.rounded_rectangle(
            (cx - ring_w // 2, card_y - int(size * 0.045), cx + ring_w // 2, card_y + ring_h),
            radius=ring_w // 2,
            fill=CARD_SHADE,
        )

    # 3 x 2 的日期格；右下角一格用橙色表示"今天有更新"
    cols, rows = 3, 2
    grid_top = card_y + bar_h + int(size * 0.075)
    grid_bottom = card_y + card_h - int(size * 0.075)
    grid_left = card_x + int(card_w * 0.12)
    grid_right = card_x + card_w - int(card_w * 0.12)
    cell_w = (grid_right - grid_left) / cols
    cell_h = (grid_bottom - grid_top) / rows
    dot_r = int(size * 0.032)
    for r in range(rows):
        for c in range(cols):
            cx = int(grid_left + cell_w * (c + 0.5))
            cy = int(grid_top + cell_h * (r + 0.5))
            color = TODAY if (r, c) == (rows - 1, cols - 1) else DOT
            draw.ellipse((cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r), fill=color)

    return img


def main() -> None:
    master = draw_master()
    ASSETS.mkdir(parents=True, exist_ok=True)
    DOC_IMAGES.mkdir(parents=True, exist_ok=True)

    targets = {
        ASSETS / "StoreLogo.png": (50, 50),
        ASSETS / "Square44x44Logo.png": (44, 44),
        ASSETS / "Square44x44Logo.targetsize-48_altform-unplated.png": (48, 48),
        ASSETS / "Square71x71Logo.png": (71, 71),
        ASSETS / "Square150x150Logo.png": (150, 150),
        ASSETS / "Square310x310Logo.png": (310, 310),
        DOC_IMAGES / "icon-620.png": (620, 620),
    }

    for path, size in targets.items():
        master.resize(size, Image.LANCZOS).save(path, "PNG", optimize=True)
        print(f"[icon] {path.relative_to(REPO)}  {size[0]}x{size[1]}  {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()
