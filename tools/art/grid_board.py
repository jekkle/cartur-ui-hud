"""One grid board for the bag and every chest (Cartur's board_grid.png, 8x4, 2026-10-04).

The board is cut mid-gap into columns and rows and laid back together for any size, so no cell,
rail or corner is ever resized. The medallion and rune strips are painted out of a base copy
(their own rail laid over them) and put back once, at their own size, on the assembled board -
medallion centred on the top rail, runes centred on each side rail when the board is tall enough.
Chests get a band of the board's own top wood above the first row for their name and buttons.

    python tools/art/grid_board.py <outdir>     writes preview PNGs for every size in use
"""
import os, sys
from PIL import Image, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(os.path.dirname(os.path.dirname(HERE)), "src", "Assets")

# board_grid.png px, measured: cell interiors 158x153, pitch 186.5 x 178.7.
COL_CUTS = [277, 460, 645, 831, 1020, 1206, 1393]
ROW_CUTS = [314, 491, 669]
MEDAL = (755, 0, 905, 140)
RUNES_L = (15, 345, 52, 630)
RUNES_R = (1617, 345, 1657, 630)
BAND_AT = 140               # the band goes in here, above the first row of cells
BAND_PX = 84                 # one line of name and buttons (layout A); 120 left empty wood
RAIL_PX = 62                # side rails, x 0..62 and W-62..W (luminance profile through row one)
RAIL_SRC = (110, 340)       # a plain run of side rail, above the runes
# The band's wood: this board's own top wood, rows 102..138 - below the rail's shadow, above the
# cells (brightness profile). The window board's wood was tried first and read flat, no grain.
GRAIN = (102, 138)          # this board's top wood, for the colour to match
WOOD_SRC = (80, 110, 940, 320)   # open header planks of board_craftingfull.png


def base(art):
    b = art.copy()
    x0, y0, x1, y1 = MEDAL
    b.paste(art.crop((x0 - 200, y0, x1 - 200, y1)), (x0, y0))
    for (x0, y0, x1, y1) in (RUNES_L, RUNES_R):
        h = y1 - y0
        src = art.crop((x0, 110, x1, 110 + 230))          # plain side rail above the runes
        y = y0
        while y < y1:
            piece = src.crop((0, 0, x1 - x0, min(230, y1 - y)))
            b.paste(piece, (x0, y))
            y += 230
    return b


def band_strip(b, w, xs):
    """The band's wood is one continuous run of real planks: the open header wood of his crafting
    board (board_craftingfull.png, WOOD_SRC), shaded to this board's top wood. Wider boards take a
    second copy mirrored, which along horizontal grain leaves no visible join. Side rails on top."""
    import numpy as np
    src = Image.open(os.path.join(ASSETS, "board_craftingfull.png")).convert("RGBA").crop(WOOD_SRC)
    ref = np.asarray(b.crop((150, GRAIN[0], 700, GRAIN[1])).convert("RGB")).reshape(-1, 3).mean(0)
    have = np.asarray(src.convert("RGB")).reshape(-1, 3).mean(0)
    a = np.asarray(src).astype(float)
    a[..., :3] = np.clip(a[..., :3] * (ref / np.maximum(have, 1)), 0, 255)
    wood = Image.fromarray(a.astype("uint8"), "RGBA")
    top = (wood.height - BAND_PX) // 2
    wood = wood.crop((0, top, wood.width, top + BAND_PX))
    band = Image.new("RGBA", (w, BAND_PX))
    x, k = 0, 0
    while x < w:
        t = wood if k % 2 == 0 else ImageOps.mirror(wood)
        band.paste(t.crop((0, 0, min(t.width, w - x), BAND_PX)), (x, 0))
        x += t.width; k += 1
    W = b.width
    for (sx, dx) in ((0, 0), (W - RAIL_PX, w - RAIL_PX)):
        rail = b.crop((sx, RAIL_SRC[0], sx + RAIL_PX, RAIL_SRC[1]))
        yy = 0
        while yy < BAND_PX:
            band.paste(rail.crop((0, 0, RAIL_PX, min(rail.height, BAND_PX - yy))), (dx, yy))
            yy += rail.height
    return band


def strips(cuts, total, n):
    """Source ranges for n cells: first, (n-2) plain middles cycling, last."""
    edges = [0] + cuts + [total]
    first, last = (edges[0], edges[1]), (edges[-2], edges[-1])
    middles = [(edges[i], edges[i + 1]) for i in range(1, len(edges) - 2)]
    return [first] + [middles[i % len(middles)] for i in range(max(0, n - 2))] + [last]


def assemble(cols, rows, band):
    art = Image.open(os.path.join(ASSETS, "board_grid.png")).convert("RGBA")
    b = base(art)
    W, H = b.size
    xs, ys = strips(COL_CUTS, W, cols), strips(ROW_CUTS, H, rows)
    w = sum(x1 - x0 for x0, x1 in xs)
    if band:
        # the top row slice splits at BAND_AT; the band goes between
        top0, top1 = ys[0]
        ys = [(top0, BAND_AT), "band", (BAND_AT, top1)] + ys[1:]
    h = sum(BAND_PX if r == "band" else r[1] - r[0] for r in ys)
    out = Image.new("RGBA", (w, h))
    y = 0
    for r in ys:
        x = 0
        for (x0, x1) in xs:
            if r == "band":
                pass        # laid in one piece below, across the whole width
            else:
                out.paste(b.crop((x0, r[0], x1, r[1])), (x, y))
            x += x1 - x0
        y += BAND_PX if r == "band" else r[1] - r[0]
    if band:
        out.paste(band_strip(b, w, xs), (0, BAND_AT))
    m = art.crop(MEDAL)
    out.alpha_composite(m, ((w - m.width) // 2, 0))
    rl, rr = art.crop(RUNES_L), art.crop(RUNES_R)
    if h - 2 * 200 > rl.height:
        cy = (h - rl.height) // 2
        out.alpha_composite(rl, (RUNES_L[0], cy))
        out.alpha_composite(rr, (w - (W - RUNES_R[0]), cy))
    return out


def export():
    """The three files the mod assembles from: the art with medallion and runes painted out, and
    the band's planks (shaded, no rails) at the widest board's interior width."""
    art = Image.open(os.path.join(ASSETS, "board_grid.png")).convert("RGBA")
    b = base(art)
    b.save(os.path.join(ASSETS, "board_grid_base.png"))
    wide = band_strip(b, art.width, [(0, art.width)])
    wide.crop((RAIL_PX, 0, art.width - RAIL_PX, BAND_PX)).save(os.path.join(ASSETS, "board_grid_band.png"))
    # Plain grained wood for covering a cell that cannot be used (a backpack row's dead cells):
    # the band's planks, uncropped to their full height.
    import numpy as np
    src = Image.open(os.path.join(ASSETS, "board_craftingfull.png")).convert("RGBA").crop(WOOD_SRC)
    ref = np.asarray(b.crop((150, GRAIN[0], 700, GRAIN[1])).convert("RGB")).reshape(-1, 3).mean(0)
    have = np.asarray(src.convert("RGB")).reshape(-1, 3).mean(0)
    w = np.asarray(src).astype(float)
    w[..., :3] = np.clip(w[..., :3] * (ref / np.maximum(have, 1)), 0, 255)
    Image.fromarray(w.astype("uint8"), "RGBA").save(os.path.join(ASSETS, "board_grid_wood.png"))
    print("board_grid_base.png", b.size, "board_grid_band.png", (art.width - 2 * RAIL_PX, BAND_PX))


if __name__ == "__main__":
    if sys.argv[1] == "export":
        export(); sys.exit()
    outdir = sys.argv[1]
    sizes = [("bag_8x3", 8, 3, False), ("bag_8x4", 8, 4, False), ("bag_8x5", 8, 5, False)] + \
            [("chest_%dx%d" % (c, r), c, r, True) for c, r in
             [(3, 2), (4, 2), (5, 2), (5, 3), (6, 2), (6, 3), (6, 4), (8, 3), (8, 4), (8, 5), (5, 10)]]
    for name, c, r, band in sizes:
        im = assemble(c, r, band)
        bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
        bg.convert("RGB").save(os.path.join(outdir, name + ".png"))
        print(name, im.size)
