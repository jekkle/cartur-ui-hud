"""Builds the enemy bars' fill from the HUD bar's fill.

    python tools/art/enemy_fill.py            # -> tools/art/out/bar_fill_enemy.png
    python tools/art/enemy_fill.py --install  # and copy into src/Assets/

Same knotwork, different tonal range, and the reason is arithmetic rather than taste.
A Unity tint multiplies, so it can only darken. bar_fill.png is built to be tinted by
the HUD's own bar colours and sits low - mean 77 of 255, peak 193 - which is right for
a bar drawn over the HUD's dark window. Enemy bars take their colour from the game
instead, and those colours are bright: 255,85,85 for a hostile, 67,255,32 for a tamed
one, 255,0,100 for a boss. Multiplied by a texture averaging 77 they come out around
56,18,18 - near black, on a bar read at a glance mid-fight.

Lifting the same art into 150..255 inverts which part carries the tone: the knotwork
becomes dark marks on a bright ground rather than bright marks on black, which is the
only shape a multiply tint can keep bright. Measured after: the hostile bar lands at
192,64,64 against vanilla's 255,85,85.

It is a separate file, not an edit of bar_fill.png, because that one is shared with the
HUD's health, stamina and eitr bars and the menu bars, and none of those asked to change.
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(HERE, "out")

FLOOR, CEIL = 150.0, 255.0


def build():
    src = Image.open(os.path.join(ASSETS, "bar_fill.png")).convert("RGBA")
    a = np.asarray(src).astype(np.float64).copy()
    peak = a[..., :3].max()
    a[..., :3] = FLOOR + a[..., :3] * ((CEIL - FLOOR) / peak)
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "bar_fill_enemy.png")
    img.save(path)
    print("bar_fill_enemy %dx%d, mean %.1f (was %.1f)"
          % (img.size[0], img.size[1], np.asarray(img).astype(float)[..., :3].mean(),
             np.asarray(src).astype(float)[..., :3].mean()))
    if "--install" in sys.argv:
        import shutil
        shutil.copy2(path, os.path.join(ASSETS, "bar_fill_enemy.png"))
        print("installed -> src/Assets/bar_fill_enemy.png")


if __name__ == "__main__":
    build()
