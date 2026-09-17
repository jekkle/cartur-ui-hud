"""Gate for art pieces: nothing enters src/Assets/ unless it passes here.

    python tools/art/check_piece.py tools/art/out/panel.png [--install]

Checks, from pieces.json: exact size; 9-slice border symmetry (the left band must be
the mirror of the right band and top the mirror of bottom, or the sliced sprite shows
a seam where the frame meets the stretched middle); tileability of the centre for
pieces that ship Tiled; alpha sanity (corners clear, centre opaque). --install copies
a passing piece into src/Assets/.
"""
import json
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
PIECES = json.load(open(os.path.join(HERE, "pieces.json")))["pieces"]
TOL = 6 / 255  # mean abs diff allowed between a band and its mirror


def band_diff(a, b):
    return float(np.abs(a.astype(np.float32) - b.astype(np.float32)).mean()) / 255


def check(path):
    name = os.path.splitext(os.path.basename(path))[0]
    spec = PIECES.get(name)
    if spec is None:
        return [f"{name}: not in pieces.json"]
    im = Image.open(path).convert("RGBA")
    if "variant_of" in spec:
        spec = dict(PIECES[spec["variant_of"]], **spec)
    w, h = spec["size"]
    fails = []
    if im.size != (w, h):
        return [f"size {im.size} != {(w, h)}"]
    px = np.asarray(im)
    rgb, alpha = px[..., :3], px[..., 3]
    b = spec["border"]
    kind = spec.get("kind", "frame")
    if kind == "strip":
        d = band_diff(rgb[:, :4], rgb[:, -4:])
        return [] if d <= TOL else [f"strip ends do not tile {d:.4f}"]
    if kind == "disc":
        return []

    # 9-slice invariant: the stretched middle of each edge band must be uniform along
    # its stretch axis, or the frame shows a seam where it meets the corners. Top and
    # bottom rails are allowed to differ (the bar's own are: grey-lit top, warm bottom).
    def run_var(band, axis):
        return float(band.astype(np.float32).std(axis=axis).mean()) / 255
    d = max(run_var(rgb[:b, b:w - b], 1), run_var(rgb[h - b:, b:w - b], 1))
    if d > TOL:
        fails.append(f"top/bottom band not uniform along x {d:.4f} > {TOL:.4f}")
    d = max(run_var(rgb[b:h - b, :b], 0), run_var(rgb[b:h - b, w - b:], 0))
    if d > TOL:
        fails.append(f"left/right band not uniform along y {d:.4f} > {TOL:.4f}")

    if spec.get("tile"):
        c = rgb[b:h - b, b:w - b]
        d = band_diff(c[:, :4], c[:, -4:])
        if d > TOL:
            fails.append(f"centre columns do not tile {d:.4f}")
        d = band_diff(c[:4], c[-4:])
        if d > TOL:
            fails.append(f"centre rows do not tile {d:.4f}")

    if spec.get("alpha") == "rounded":
        if alpha[0, 0] > 8 or alpha[0, -1] > 8 or alpha[-1, 0] > 8 or alpha[-1, -1] > 8:
            fails.append("corner pixels not transparent")
    if alpha[h // 2, w // 2] < 150 and not spec.get("hollow"):
        fails.append("centre not opaque")
    return fails


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    install = "--install" in sys.argv
    if not args:
        print(__doc__)
        return 2
    rc = 0
    for path in args:
        fails = check(path)
        if fails:
            rc = 1
            print(f"FAIL {path}")
            for f in fails:
                print(f"     {f}")
            continue
        print(f"PASS {path}")
        if install:
            dst = os.path.join(ROOT, "src", "Assets", os.path.basename(path))
            shutil.copyfile(path, dst)
            print(f"     -> {dst}")
    return rc


if __name__ == "__main__":
    sys.exit(main())
