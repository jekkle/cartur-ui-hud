"""Cuts the standalone hotbar out of the inventory asset.

    python tools/art/hotbar.py [--install]

Cartur's plan: closed, the hotbar is the inventory's first row and nothing else; opening the
inventory draws the rest around it. So the bar is the asset down to the bottom of row one,
with the panel's OWN bottom rail spliced underneath so it is a finished object rather than a
torn edge.

Row boundaries measured on the asset by its cell bezel gradients, then confirmed by drawing
the lines on it and looking: rows divide at y 330, 493, 665, 841, 1023, and row one's lower
bezel finishes at 514. The bottom rail runs 1023 to the foot of the art.
"""
import os
import sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(HERE, "out", "assets")

ROW1_BOTTOM = 514      # below row one's lower bezel
RAIL_TOP = 1023        # the panel's own bottom rail


def main():
    src = Image.open(os.path.join(OUT, "inventory_hotbar.png")).convert("RGBA")
    top = src.crop((0, 0, src.width, ROW1_BOTTOM))
    rail = src.crop((0, RAIL_TOP, src.width, src.height))

    bar = Image.new("RGBA", (src.width, top.height + rail.height), (0, 0, 0, 0))
    bar.alpha_composite(top, (0, 0))
    bar.alpha_composite(rail, (0, top.height))
    bar.save(os.path.join(OUT, "hotbar.png"))
    print("hotbar %dx%d  (row one %d tall, rail %d)" % (bar.width, bar.height, top.height, rail.height))

    if "--install" in sys.argv:
        import shutil
        shutil.copy2(os.path.join(OUT, "hotbar.png"), os.path.join(ASSETS, "hotbar_panel.png"))
        print("installed -> src/Assets/hotbar_panel.png")


if __name__ == "__main__":
    main()
