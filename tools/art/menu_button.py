"""Builds the menu row button: the wood panel's own grain in the button piece's shape.

    python tools/art/menu_button.py            # -> tools/art/out/button_light.png
    python tools/art/menu_button.py --install  # and copy into src/Assets/

Cartur wanted the pause menu's rows to read as lighter wood than the panel behind them.
Nothing in src/Assets was usable as-is: every piece is the same near-black grey, measured
at centre ~35 (button, panel, slot, tooltip all within a point of each other), and the
only real wood is panel_wood.png at 40,29,21 - darker still.

So the grain comes from panel_wood's interior - x/y 150..430, inside its 101px border and
clear of the corner knotwork, so it is wood and nothing else - lifted into 46..120. The
button's own alpha is kept, so the piece keeps its shape and its 9-slice border lines up
with the 20px the loader already uses for button.png. Its rail highlights are kept too,
brightened, so the wood still sits inside a rail instead of being a flat plank.
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(HERE, "out")

WOOD_FIELD = (150, 430)      # inside panel_wood's 101px border, clear of the knotwork
FLOOR, CEIL = 46.0, 120.0
RAIL_ABOVE = 60.0            # button.png is near-black but for its rail; this splits them
RAIL_GAIN = 1.6


def build():
    btn = Image.open(os.path.join(ASSETS, "button.png")).convert("RGBA")
    wood = np.asarray(Image.open(os.path.join(ASSETS, "panel_wood.png")).convert("RGBA")).astype(np.float64)

    lo, hi = WOOD_FIELD
    field = wood[lo:hi, lo:hi]
    tile = np.asarray(Image.fromarray(field.astype(np.uint8), "RGBA").resize(btn.size, Image.LANCZOS)).astype(np.float64)
    tile[..., :3] = FLOOR + tile[..., :3] * ((CEIL - FLOOR) / tile[..., :3].max())

    b = np.asarray(btn).astype(np.float64)
    rail = (b[..., :3].mean(axis=2) > RAIL_ABOVE)[..., None]
    out = tile.copy()
    out[..., :3] = np.where(rail, b[..., :3] * RAIL_GAIN, tile[..., :3])
    out[..., 3] = b[..., 3]

    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "button_light.png")
    img.save(path)
    c = np.asarray(img).astype(float)
    h, w = c.shape[:2]
    mid = c[h // 3:2 * h // 3, w // 3:2 * w // 3]
    print("button_light %dx%d, centre %.1f %.1f %.1f (panel.png centre is 35.9)"
          % (img.size[0], img.size[1], mid[..., 0].mean(), mid[..., 1].mean(), mid[..., 2].mean()))
    if "--install" in sys.argv:
        import shutil
        shutil.copy2(path, os.path.join(ASSETS, "button_light.png"))
        print("installed -> src/Assets/button_light.png")


if __name__ == "__main__":
    build()
