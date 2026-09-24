"""Cuts real alpha from Cartur's second concept board and measures its grid.

    python tools/art/board2.py

The board arrives as opaque RGB with the transparency PAINTED as a checkerboard - 29% of
the file. That has to become a real alpha channel before any of it can be used, and the
checkerboard is regular and near-white, so it keys out on luminance plus the fact that it
never appears inside the panel.

The grid is measured rather than counted by eye, because the whole point is to rebuild it
at vanilla's four rows and let it grow from there.
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(HERE, "out", "board2")
SRC = r"C:\Users\Tool1\.claude\uploads\603ce7e7-6dff-4749-8f0d-4aa2295ccee8\6919263f-image.jpg"


def main():
    os.makedirs(OUT, exist_ok=True)
    im = Image.open(SRC).convert("RGB")
    a = np.asarray(im).astype(float)
    lum = a.mean(axis=2)
    sat = a.max(axis=2) - a.min(axis=2)

    # Checkerboard: bright and colourless. Nothing in the art is both.
    board = (lum > 185) & (sat < 22)

    # Keep only checkerboard connected to the outside, so a bright colourless highlight
    # inside the panel is not punched out. Flood from the border.
    h, w = board.shape
    keep = np.zeros_like(board)
    stack = [(0, x) for x in range(w)] + [(h - 1, x) for x in range(w)] \
          + [(y, 0) for y in range(h)] + [(y, w - 1) for y in range(h)]
    stack = [p for p in stack if board[p]]
    seen = set(stack)
    while stack:
        y, x = stack.pop()
        keep[y, x] = True
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and board[ny, nx] and (ny, nx) not in seen:
                seen.add((ny, nx))
                stack.append((ny, nx))

    alpha = np.where(keep, 0, 255).astype(np.uint8)
    rgba = np.dstack([np.asarray(im), alpha])
    cut = Image.fromarray(rgba, "RGBA")
    cut.save(os.path.join(OUT, "board2_alpha.png"))
    print("alpha cut: %.1f%% transparent" % (100 * (alpha == 0).mean())) 

    # Grid geometry: the cell borders are lighter than the field, so column and row edges
    # show up as peaks in the gradient inside the grid area.
    ys, xs = np.where(alpha > 0)
    print("content bbox x %d-%d y %d-%d" % (xs.min(), xs.max(), ys.min(), ys.max()))
    g = lum[300:700, 150:690]
    dx = np.abs(np.diff(g, axis=1)).mean(axis=0)
    dy = np.abs(np.diff(g, axis=0)).mean(axis=1)

    def peaks(v, thr):
        out = []
        for i in range(1, len(v) - 1):
            if v[i] > thr and v[i] >= v[i - 1] and v[i] > v[i + 1]:
                if not out or i - out[-1] > 8:
                    out.append(i)
        return out
    px = peaks(dx, dx.mean() * 1.8)
    py = peaks(dy, dy.mean() * 1.8)
    print("column edges at x+150:", [p + 150 for p in px])
    print("row edges at y+300   :", [p + 300 for p in py])
    if len(px) > 1:
        print("column pitch: %.1f px" % np.mean(np.diff(px)))
    if len(py) > 1:
        print("row pitch   : %.1f px" % np.mean(np.diff(py)))


if __name__ == "__main__":
    main()
