"""board_window_base.png: Cartur's small-border window board (board_window.png, 2026-10-04) with
its medallion, diamond and rune strips painted over by plain stretches of its own rail, so it can
be 9-sliced to any window. AutoBoard lays those four pieces back on top at their own size, cut
from board_window.png at the boxes below (px from its top-left, read off zoomed crops).
"""
import os
from PIL import Image

ASSETS = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "src", "Assets")
# piece -> (box, plain source x/y shift)
PATCHES = [
    ((415, 0, 567, 180), (-165, 0)),       # medallion and its shadow <- top rail to its left
    ((415, 1480, 567, 1635), (-165, 0)),   # diamond   <- bottom rail to its left
    # Runes span y 555..1085; the plain rail above them is shorter, so in two lifts from y 120..
    ((0, 545, 66, 945), (0, -425)),        # left runes  <- left rail above them
    ((0, 945, 66, 1100), (0, -825)),
    ((916, 545, 982, 945), (0, -425)),     # right runes <- right rail above them
    ((916, 945, 982, 1100), (0, -825)),
]

im = Image.open(os.path.join(ASSETS, "board_window.png")).convert("RGBA")
out = im.copy()
for (x0, y0, x1, y1), (dx, dy) in PATCHES:
    out.paste(im.crop((x0 + dx, y0 + dy, x1 + dx, y1 + dy)), (x0, y0))
out.save(os.path.join(ASSETS, "board_window_base.png"))
print("board_window_base.png", out.size)
