"""Assembles the two-state inventory mockup from the rendered pieces.

    python tools/art/mockup.py

Built the way the game would build it, not painted as one picture - that is the whole point
of the exercise. Corners are placed at native size and never scaled by the panel. Edges are
TILED, which is what Image.Type.Tiled does to a 9-slice's edge slices, so the carving repeats
along any width instead of smearing. The centre is the rendered board, also tiled. Slots are
the mod's own slot.png, 9-sliced.

Two states, because the hotbar is a separate object from the inventory grid and stays on
screen when the inventory opens - read off HotkeyBar.Update, which gates input on
InventoryGui.IsVisible but never hides itself. So the hotbar is drawn as a finished bar that
stands alone, and the panel rises above it with a join rail along the bottom.
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
A = os.path.join(ROOT, "src", "Assets")
ORN = os.path.join(HERE, "out", "ornament")
WOOD = os.path.join(HERE, "out", "wood")

RAIL = 54          # edge thickness on screen
CELL, GAP = 74, 6


def load(p):
    return Image.open(p).convert("RGBA")


def tile(src, W, H):
    out = Image.new("RGBA", (W, H))
    for y in range(0, H, src.height):
        for x in range(0, W, src.width):
            out.alpha_composite(src, (x, y))
    return out


def slice9(src, b, W, H):
    s_w, s_h = src.size
    o = Image.new("RGBA", (W, H))
    box = {"tl": (0, 0, b, b), "tr": (s_w - b, 0, s_w, b), "bl": (0, s_h - b, b, s_h),
           "br": (s_w - b, s_h - b, s_w, s_h), "t": (b, 0, s_w - b, b), "bo": (b, s_h - b, s_w - b, s_h),
           "l": (0, b, b, s_h - b), "r": (s_w - b, b, s_w, s_h - b), "c": (b, b, s_w - b, s_h - b)}
    c = {k: src.crop(v) for k, v in box.items()}
    o.paste(c["c"].resize((max(W - 2 * b, 1), max(H - 2 * b, 1))), (b, b))
    o.paste(c["t"].resize((max(W - 2 * b, 1), b)), (b, 0))
    o.paste(c["bo"].resize((max(W - 2 * b, 1), b)), (b, H - b))
    o.paste(c["l"].resize((b, max(H - 2 * b, 1))), (0, b))
    o.paste(c["r"].resize((b, max(H - 2 * b, 1))), (W - b, b))
    for k, pos in (("tl", (0, 0)), ("tr", (W - b, 0)), ("bl", (0, H - b)), ("br", (W - b, H - b))):
        o.paste(c[k], pos)
    return o


def framed(W, H, board, edge_h, edge_v, corner):
    """A panel the way a 9-slice draws one: tiled centre, tiled edges, native corners."""
    p = Image.new("RGBA", (W, H))
    p.alpha_composite(tile(board, W, H))
    p.alpha_composite(tile(edge_h, W, RAIL), (0, 0))
    p.alpha_composite(tile(edge_h.transpose(Image.FLIP_TOP_BOTTOM), W, RAIL), (0, H - RAIL))
    p.alpha_composite(tile(edge_v, RAIL, H), (0, 0))
    p.alpha_composite(tile(edge_v.transpose(Image.FLIP_LEFT_RIGHT), RAIL, H), (W - RAIL, 0))
    c = corner
    p.alpha_composite(c, (0, 0))
    p.alpha_composite(c.transpose(Image.FLIP_LEFT_RIGHT), (W - c.width, 0))
    p.alpha_composite(c.transpose(Image.FLIP_TOP_BOTTOM), (0, H - c.height))
    p.alpha_composite(c.transpose(Image.ROTATE_180), (W - c.width, H - c.height))
    return p


def grid(cols, rows, slot):
    W = cols * CELL + (cols - 1) * GAP
    H = rows * CELL + (rows - 1) * GAP
    g = Image.new("RGBA", (W, H))
    cell = slice9(slot, 16, CELL, CELL)
    for r in range(rows):
        for c in range(cols):
            g.alpha_composite(cell, (c * (CELL + GAP), r * (CELL + GAP)))
    return g


def main():
    board = load(os.path.join(WOOD, "wood_1000.png")).resize((256, 256), Image.LANCZOS)
    edge_src = load(os.path.join(ORN, "edge_2000.png"))
    edge_h = edge_src.crop((0, 70, edge_src.width, 70 + 96)).resize((360, RAIL), Image.LANCZOS)
    edge_v = edge_h.rotate(90, expand=True)
    corner = load(os.path.join(ORN, "corner_2010.png")).resize((RAIL * 2, RAIL * 2), Image.LANCZOS)
    slot = load(os.path.join(A, "slot.png"))

    cols, rows = 8, 4
    g = grid(cols, rows, slot)
    pad = 26
    PW = g.width + RAIL * 2 + pad * 2
    PH = g.height + RAIL * 2 + pad * 2
    panel = framed(PW, PH, board, edge_h, edge_v, corner)
    panel.alpha_composite(g, (RAIL + pad, RAIL + pad))

    bar_g = grid(cols, 1, slot)
    BW = bar_g.width + RAIL * 2 + pad * 2
    BH = bar_g.height + RAIL * 2 + pad * 2
    bar = framed(BW, BH, board, edge_h, edge_v, corner)
    bar.alpha_composite(bar_g, (RAIL + pad, RAIL + pad))

    margin, gapy = 60, 10
    sheet_w = max(PW, BW) + margin * 2
    sheet_h = margin * 2 + BH + 120 + PH + BH + gapy + 60
    sheet = Image.new("RGB", (sheet_w, sheet_h), (18, 19, 21))
    from PIL import ImageDraw
    d = ImageDraw.Draw(sheet)

    y = margin
    d.text((margin, y - 26), "inventory closed - the hotbar stands alone", fill=(215, 210, 200))
    sheet.paste(bar.convert("RGB"), ((sheet_w - BW) // 2, y), bar)
    y += BH + 110
    d.text((margin, y - 26), "tab - the panel rises above it, joined at the rail", fill=(215, 210, 200))
    sheet.paste(panel.convert("RGB"), ((sheet_w - PW) // 2, y), panel)
    y += PH + gapy
    sheet.paste(bar.convert("RGB"), ((sheet_w - BW) // 2, y), bar)
    sheet.save(os.path.join(ROOT, "..", "..", "inventory_mockup.png"))
    print("wrote inventory_mockup.png  panel %dx%d  bar %dx%d" % (PW, PH, BW, BH))


if __name__ == "__main__":
    main()
