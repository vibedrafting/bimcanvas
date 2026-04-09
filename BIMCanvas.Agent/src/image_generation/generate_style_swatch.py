"""Generate an abstract style swatch for floor plan style transfer.

Produces a pure color/element reference card — NO room layout, NO spatial
relationships, NO furniture labels. Just isolated visual samples of each
element type arranged in horizontal strips.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


# ── Colors sampled from actual reference image (参考图.png) ──────────
BG = (10, 12, 14)               # near-black background
FLOOR = (22, 48, 38)            # dark muted green, floor fill
WALL_FILL = (70, 78, 82)        # dark gray, wall body
WALL_STROKE = (45, 155, 75)     # muted green, wall outline
FURNITURE = (195, 150, 75)      # warm muted orange, furniture blocks
FURNITURE_LABEL = (255, 255, 255)  # white text on furniture
WINDOW = (55, 100, 190)         # steel blue, window bars
DOOR_LEAF = (30, 125, 60)       # green, door leaf rectangle
DOOR_ARC = (210, 210, 210)      # light gray-white, door swing arc


def _try_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for name in ("msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def generate_style_swatch(output_path: str | Path, width: int = 800, height: int = 500) -> Path:
    output = Path(output_path).expanduser().resolve()
    output.parent.mkdir(parents=True, exist_ok=True)

    img = Image.new("RGB", (width, height), BG)
    draw = ImageDraw.Draw(img)
    font = _try_font(13)
    font_elem = _try_font(15)

    margin = 40
    row_h = 60
    swatch_w = 90
    gap = 20
    y = margin

    # ── Row 1: Color palette strips ─────────────────────────────────
    palette = [
        (BG, "background"),
        (FLOOR, "floor"),
        (WALL_FILL, "wall fill"),
        (WALL_STROKE, "wall stroke"),
        (FURNITURE, "furniture"),
        (WINDOW, "window"),
        (DOOR_LEAF, "door"),
    ]
    x = margin
    for color, label in palette:
        draw.rectangle([x, y, x + swatch_w, y + 35], fill=color, outline=WALL_STROKE, width=1)
        tw = draw.textlength(label, font=font)
        draw.text((x + (swatch_w - tw) / 2, y + 40), label, fill=FURNITURE_LABEL, font=font)
        x += swatch_w + gap

    y += row_h + 40

    # ── Row 2: Wall cross-section sample ────────────────────────────
    # A short horizontal wall segment showing fill + stroke + floor on both sides
    wx = margin
    seg_len = 300
    wall_t = 16
    # floor above
    draw.rectangle([wx, y, wx + seg_len, y + 40], fill=FLOOR)
    # wall body
    draw.rectangle([wx, y + 40, wx + seg_len, y + 40 + wall_t], fill=WALL_FILL)
    draw.rectangle([wx, y + 40, wx + seg_len, y + 40 + wall_t], outline=WALL_STROKE, width=2)
    # floor below
    draw.rectangle([wx, y + 40 + wall_t, wx + seg_len, y + 80 + wall_t], fill=FLOOR)
    # label
    draw.text((wx + seg_len + 15, y + 35), "wall section", fill=FURNITURE_LABEL, font=font)

    y += 80 + wall_t + 30

    # ── Row 3: Element samples (furniture / window / door) ──────────
    ex = margin

    # furniture sample
    fw, fh = 120, 55
    FURNITURE_STROKE = (145, 100, 40)
    draw.rectangle([ex, y, ex + fw, y + fh], fill=FURNITURE, outline=FURNITURE_STROKE, width=2)
    # furniture inner label
    label_text = "Label"
    tw = draw.textlength(label_text, font=font_elem)
    draw.text((ex + (fw - tw) / 2, y + (fh - 18) / 2), label_text, fill=FURNITURE_LABEL, font=font_elem)
    # sample description
    draw.text((ex + fw + 10, y + 18), "furniture block", fill=FURNITURE_LABEL, font=font)
    ex += fw + 130

    # window sample: window inset in a wall
    ww, wall_h = 100, 16
    w_wall_len = 40
    w_y = y + 20
    # wall left
    draw.rectangle([ex, w_y, ex + w_wall_len, w_y + wall_h], fill=WALL_FILL)
    draw.rectangle([ex, w_y, ex + w_wall_len, w_y + wall_h], outline=WALL_STROKE, width=2)
    # window sash
    wx_start = ex + w_wall_len
    glass_h = 6
    glass_y = w_y + (wall_h - glass_h) // 2
    # background floor underneath window
    draw.rectangle([wx_start, w_y, wx_start + ww, w_y + wall_h], fill=FLOOR)
    # glass and frame
    draw.rectangle([wx_start, glass_y, wx_start + ww, glass_y + glass_h], fill=WINDOW, outline=(150, 180, 220), width=1)
    # central division line (common in CAD to denote double sliding window)
    draw.line([wx_start + ww//2, glass_y, wx_start + ww//2, glass_y + glass_h], fill=(150, 180, 220), width=1)
    # wall right
    wx_end_wall = wx_start + ww
    draw.rectangle([wx_end_wall, w_y, wx_end_wall + w_wall_len, w_y + wall_h], fill=WALL_FILL)
    draw.rectangle([wx_end_wall, w_y, wx_end_wall + w_wall_len, w_y + wall_h], outline=WALL_STROKE, width=2)
    
    draw.text((wx_end_wall + w_wall_len + 10, y + 18), "window", fill=FURNITURE_LABEL, font=font)
    ex += w_wall_len*2 + ww + 120

    # door sample: horizontal wall with opening, leaf perpendicular to wall into room, arc from leaf tip
    door_x = ex
    leaf_len = 50         # leaf length (extends into room)
    gap_w = leaf_len      # door opening width in wall
    leaf_th = 6           # leaf visual thickness

    wall_y = y + 20
    # wall left of opening
    draw.rectangle([door_x, wall_y, door_x + 40, wall_y + wall_h], fill=WALL_FILL)
    draw.rectangle([door_x, wall_y, door_x + 40, wall_y + wall_h], outline=WALL_STROKE, width=2)
    # wall right of opening
    wall_right_x = door_x + 40 + gap_w
    draw.rectangle([wall_right_x, wall_y, wall_right_x + 40, wall_y + wall_h], fill=WALL_FILL)
    draw.rectangle([wall_right_x, wall_y, wall_right_x + 40, wall_y + wall_h], outline=WALL_STROKE, width=2)

    # green leaf: hinge at right side of opening, extends downward (into room)
    hinge_x = wall_right_x  # hinge at right edge of opening
    hinge_y = wall_y + wall_h  # bottom of wall = room side
    draw.rectangle([hinge_x - leaf_th, hinge_y, hinge_x, hinge_y + leaf_len], fill=DOOR_LEAF, outline=(20, 90, 40), width=1)

    # gray arc: pivot at hinge, radius = leaf_len, sweeps from leaf tip leftward to wall
    # thinner stroke for arc to show it's an indicator line
    draw.arc(
        [hinge_x - leaf_len, hinge_y - leaf_len, hinge_x + leaf_len, hinge_y + leaf_len],
        start=90, end=180, fill=DOOR_ARC, width=1,
    )
    draw.text((wall_right_x + 45, wall_y - 2), "door", fill=FURNITURE_LABEL, font=font)

    img.save(str(output))
    return output


if __name__ == "__main__":
    import os, subprocess
    # Use cwd (should be BIMCanvas.Agent) to derive relative path
    cwd = Path(os.getcwd())
    target = cwd / ".." / "references" / "style_swatch.png"
    out = generate_style_swatch(target)
    print(f"Saved to: {out}")
