"""Measures each chest board's cell grid and title plate (tools/art/out/chest_boards.json).

The cell count is known from the board's name, so a regular grid - start, cell size, gap - is
fitted to the bronze rim lines: rims are the warm, bright pixels (luminance > 50, red - blue > 12);
summed along a band they give a profile with a peak at every cell edge, and the grid that lands on
the most rim wins. The plate is the strongest pair of horizontal rims above the first row.
"""
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SIZES = ["3x2", "4x2", "5x2", "5x3", "6x2", "6x3", "6x4", "8x3", "8x4", "8x5", "5x10"]


def fit(profile, n, lo, hi, cell_range, gap_range):
    p = ndimage.maximum_filter1d(profile, 5)
    best = None
    for c in range(*cell_range):
        for g in range(*gap_range):
            span = n * c + (n - 1) * g
            for s in range(lo, hi - span):
                idx = np.array([s + i * (c + g) for i in range(n)])
                score = p[idx].sum() + p[idx + c].sum()
                if best is None or score > best[0]:
                    best = (score, s, c, g)
    return best[1:]


def measure(name, out_dir, lum=50):
    cols, rows = map(int, name.split("x"))
    img = Image.open(os.path.join(ROOT, "src", "Assets", "board_chest_%s.png" % name)).convert("RGBA")
    a = np.asarray(img).astype(float)
    W, H = img.size
    raw = (a[..., :3].mean(2) > lum) & (a[..., 0] - a[..., 2] > 12)
    # Only long straight rim runs count: the planks' grain is horizontal and wavy and faked rows
    # (6x2, 5x10 fitted to grain on the first pass); a cell edge is ruled.
    hl, vl = max(5, int(W / cols * 0.4)), max(5, int(H / (rows + 1.5) * 0.4))
    rimh = ndimage.binary_opening(ndimage.binary_dilation(raw, iterations=1), structure=np.ones((1, hl))).astype(float)
    rimv = ndimage.binary_opening(ndimage.binary_dilation(raw, iterations=1), structure=np.ones((vl, 1))).astype(float)
    rim = np.maximum(rimh, rimv)
    hprof = rimh[:, int(W * .15):int(W * .85)].sum(1)
    # rows: the cells take the lower part; cell height and gap as fractions of the board
    ch_lo, ch_hi = int(H * 0.5 / (rows + 1.5)), int(H * 0.9 / (rows + 0.3))
    y0, ch, gy = fit(hprof, rows, int(H * 0.15), int(H * 0.97), (ch_lo, ch_hi, 2), (2, max(4, int(H * 0.06)), 2))
    y1 = y0 + rows * ch + (rows - 1) * gy
    vprof = rimv[y0:y1].sum(0)
    cw_lo, cw_hi = int(W * 0.5 / (cols + 1.5)), int(W * 0.9 / (cols + 0.3))
    x0, cw, gx = fit(vprof, cols, int(W * 0.03), int(W * 0.97), (cw_lo, cw_hi, 2), (2, max(4, int(W * 0.05)), 2))
    # Each edge then snaps to the strongest rim within a few px: the art is drawn, not ruled.
    hs, vs = ndimage.uniform_filter1d(hprof, 3), ndimage.uniform_filter1d(rimv[y0:y1].sum(0), 3)
    def snap(prof, e, r=10):
        lo, hi = max(0, int(e) - r), min(len(prof), int(e) + r + 1)
        return lo + int(np.argmax(prof[lo:hi]))
    X, Y, ws, hs_ = [], [], [], []
    for i in range(cols):
        l, r = snap(vs, x0 + i * (cw + gx)), snap(vs, x0 + i * (cw + gx) + cw)
        X.append((l + r) / 2); ws.append(r - l)
    for j in range(rows):
        t, b = snap(hs, y0 + j * (ch + gy)), snap(hs, y0 + j * (ch + gy) + ch)
        Y.append((t + b) / 2); hs_.append(b - t)
    cw, ch = float(np.median(ws)), float(np.median(hs_))
    y0 = int(Y[0] - ch / 2)
    # plate: two strongest separated horizontal rims above the first row, x span from the cells
    hp = ndimage.maximum_filter1d(hprof, 3)
    above = hp[:max(1, y0 - 4)]
    top_lim = int(H * 0.04)
    # the pair of rims, plate-height apart (6 to 25% of the board), with the most rim between them
    # and its bottom rim close above the first row - the top rail's knotwork is brighter still
    pairs = [(above[t] + above[b], t, b) for b in range(max(0, y0 - int(H * 0.12)), len(above))
             for t in range(max(top_lim, b - int(H * 0.25)), b - int(H * 0.06))]
    _, pt, pb = max(pairs)
    plate = [int(x0), int(pt), int(x0 + cols * cw + (cols - 1) * gx), int(pb)]
    d = ImageDraw.Draw(img)
    for x in X:
        for y in Y:
            d.rectangle([x - cw / 2, y - ch / 2, x + cw / 2, y + ch / 2], outline=(255, 0, 0, 255), width=3)
    d.rectangle(plate, outline=(0, 255, 0, 255), width=4)
    img.thumbnail((500, 500))
    img.save(os.path.join(out_dir, "meas_%s.png" % name))
    return dict(size=[W, H], X=X, Y=Y, cw=cw, ch=ch, plate=plate)


if __name__ == "__main__":
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "tools", "art", "out")
    res = {}
    for n in SIZES:
        res[n] = measure(n, out_dir)
        r = res[n]
        print(n, r["size"], "cell %dx%d" % (r["cw"], r["ch"]), "x0 %.0f y0 %.0f" % (r["X"][0], r["Y"][0]), "plate", r["plate"])
    json.dump(res, open(os.path.join(ROOT, "tools", "art", "out", "chest_boards.json"), "w"), indent=1)
