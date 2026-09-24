"""Cuts real alpha from the new assets, which arrive on black.

    python tools/art/cutalpha.py

Black is harder than the checkerboard the earlier board came on: the art's own shadows and
the stone in the archway are near-black too, so a plain luminance key eats the carving. The
key is therefore luminance AND connectivity - only black that reaches the border is cut, so
an enclosed dark interior survives because the frame around it is not black.

Prints the coverage so the cut can be judged by a number as well as by eye: a cut that
takes far more than the visible surround has eaten art.
"""
import os
import numpy as np
from PIL import Image
from collections import deque

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(HERE, "out", "assets")
UP = r"C:\Users\Tool1\.claude\uploads\603ce7e7-6dff-4749-8f0d-4aa2295ccee8"

# Threshold 3, not 16. Measured: the art's shadows run continuously down into the background
# with no gap, but 37.6% of the equipment panel sits below lum 1 - that is the true black.
# At 16 the flood crawled in through every carved shadow and ate the panel to lace.
JOBS = [("equipment_panel", os.path.join(UP, "92781bac-image.jpg"), 3),
        ("inventory_hotbar", os.path.join(UP, "80ffc8e5-image.jpg"), 3)]


def cut(path, thr):
    im = Image.open(path).convert("RGB")
    a = np.asarray(im).astype(np.int16)
    lum = a.mean(axis=2)
    black = lum < thr
    h, w = black.shape

    # Flood-filling black from the border does not work on this art: the arch's stone and the
    # deepest carving ARE black and they touch the background, so the flood walks straight in
    # and holes the panel. Closing the holes afterwards failed too - they are bands twenty
    # pixels wide, not pinholes.
    #
    # So nothing is cut inward at all. The panel's silhouette is taken as the span between the
    # first and last lit pixel on every row AND on every column; anything outside both spans is
    # background. A panel is convex enough for that to hold, and it cannot punch a hole in the
    # middle of the art by construction.
    lit = ~black
    rowfill = np.zeros_like(lit)
    for y in range(h):
        xs = np.flatnonzero(lit[y])
        if xs.size:
            rowfill[y, xs[0]:xs[-1] + 1] = True
    colfill = np.zeros_like(lit)
    for x in range(w):
        ys = np.flatnonzero(lit[:, x])
        if ys.size:
            colfill[ys[0]:ys[-1] + 1, x] = True
    keep = ~(rowfill & colfill)

    alpha = np.where(keep, 0, 255).astype(np.uint8)
    return Image.fromarray(np.dstack([np.asarray(im), alpha]), "RGBA"), keep, black


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, path, thr in JOBS:
        img, keep, black = cut(path, thr)
        img.save(os.path.join(OUT, name + ".png"))
        ys, xs = np.where(np.asarray(img)[..., 3] > 0)
        print("%-18s cut %5.1f%% of the file, of %5.1f%% that is near-black; kept bbox x %d-%d y %d-%d"
              % (name, 100 * keep.mean(), 100 * black.mean(), xs.min(), xs.max(), ys.min(), ys.max()))


if __name__ == "__main__":
    main()
