"""Draws the 256x256 Thunderstore icon.

Thunderstore rejects anything that is not exactly 256x256 PNG. Unlike the other Cartur
mods this one has real art to work from, so the icon is built from the shipped assets
rather than drawn: one food diamond over a bar, which is what the HUD actually looks
like at a glance.

    python tools/make_icon.py
"""
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(ROOT, "package", "icon.png")

SIZE = 256
SCALE = 4                      # drawn large and downsampled, so the bronze stays crisp
BACKGROUND = (26, 24, 22, 255)
FILL_TINT = (198, 62, 56, 255)  # health red, the colour the mod is recognised by

# 9-slice cuts, the same numbers AssetLoader uses.
L_PX, R_PX, RAIL_PX = 118, 95, 16


def sliced(frame, width, height):
    """Frame stretched to width x height, with the centre left open as the mod draws it."""
    fw, fh = frame.size
    sy = height / fh
    lw, rw = max(1, round(L_PX * sy)), max(1, round(R_PX * sy))
    rail = max(1, round(RAIL_PX * sy))
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    midw, midh = width - lw - rw, height - rail * 2
    if midw < 1 or midh < 1:
        return out

    def put(box, size, at):
        out.alpha_composite(frame.crop(box).resize(size, Image.LANCZOS), at)

    put((0, 0, L_PX, RAIL_PX), (lw, rail), (0, 0))
    put((fw - R_PX, 0, fw, RAIL_PX), (rw, rail), (width - rw, 0))
    put((0, fh - RAIL_PX, L_PX, fh), (lw, rail), (0, height - rail))
    put((fw - R_PX, fh - RAIL_PX, fw, fh), (rw, rail), (width - rw, height - rail))
    put((0, RAIL_PX, L_PX, fh - RAIL_PX), (lw, midh), (0, rail))
    put((fw - R_PX, RAIL_PX, fw, fh - RAIL_PX), (rw, midh), (width - rw, rail))
    put((L_PX, 0, fw - R_PX, RAIL_PX), (midw, rail), (lw, 0))
    put((L_PX, fh - RAIL_PX, fw - R_PX, fh), (midw, rail), (lw, height - rail))
    return out


def main():
    big = SIZE * SCALE
    canvas = Image.new("RGBA", (big, big), BACKGROUND)

    frame = Image.open(os.path.join(ASSETS, "bar_frame.png")).convert("RGBA")
    fill = Image.open(os.path.join(ASSETS, "bar_fill.png")).convert("RGBA")
    diamond = Image.open(os.path.join(ASSETS, "food_frame.png")).convert("RGBA")

    # Bar across the lower third, filled about two thirds along.
    bar_w, bar_h = int(big * 0.88), int(big * 0.17)
    bar_x, bar_y = (big - bar_w) // 2, int(big * 0.60)

    inner_w = bar_w - round(L_PX * bar_h / frame.size[1]) - round(R_PX * bar_h / frame.size[1])
    inner_h = bar_h - round(RAIL_PX * bar_h / frame.size[1]) * 2
    strip = fill.resize((max(1, int(inner_w * 0.66)), max(1, inner_h)), Image.LANCZOS)
    tint = Image.new("RGBA", strip.size, FILL_TINT)
    strip = Image.composite(tint, strip, strip.split()[3].point(lambda a: 255 if a else 0))
    canvas.alpha_composite(strip, (bar_x + round(L_PX * bar_h / frame.size[1]),
                                   bar_y + round(RAIL_PX * bar_h / frame.size[1])))
    canvas.alpha_composite(sliced(frame, bar_w, bar_h), (bar_x, bar_y))

    # One diamond above it, overlapping slightly so the two read as one object.
    d = int(big * 0.52)
    canvas.alpha_composite(diamond.resize((d, d), Image.LANCZOS),
                           ((big - d) // 2, int(big * 0.10)))

    canvas.convert("RGB").resize((SIZE, SIZE), Image.LANCZOS).save(OUT)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
