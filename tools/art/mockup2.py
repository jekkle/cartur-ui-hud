"""The minimal-hotbar version: bare icons closed, frame drawn around them on Tab.

    python tools/art/mockup2.py

Cartur's simplification, and it removes the join entirely. The hotbar keeps no frame of its
own, so there is no seam to line up and no shared-scale problem between two framed pieces.

The row of icons does not move between the two states - that is the whole trick - so the
panel is placed by its FIRST ROW rather than by its own corner. Everything else grows
upward and outward from there.

Note for the code later: the grid's first row and the HUD hotbar are different objects
showing the same eight items, so with the panel placed over the bar the HUD one is hidden.
That is safe - HotkeyBar.Update already gates its input on InventoryGui.IsVisible, so a
hidden bar cannot swallow the number keys.
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
A = os.path.join(ROOT, "src", "Assets")
ORN = os.path.join(HERE, "out", "ornament")
WOOD = os.path.join(HERE, "out", "wood")

RAIL, CELL, GAP, PAD = 44, 74, 6, 20
import importlib.util
spec = importlib.util.spec_from_file_location("mk", os.path.join(HERE, "mockup.py"))
mk = importlib.util.module_from_spec(spec); spec.loader.exec_module(mk)


def main():
    board = mk.load(os.path.join(WOOD, "wood_1000.png")).resize((256, 256), Image.LANCZOS)
    e = mk.load(os.path.join(ORN, "edge_2000.png"))
    edge_h = e.crop((0, 70, e.width, 70 + 96)).resize((360, RAIL), Image.LANCZOS)
    edge_v = edge_h.rotate(90, expand=True)
    corner = mk.load(os.path.join(ORN, "corner_2010.png")).resize((RAIL * 2, RAIL * 2), Image.LANCZOS)
    slot = mk.load(os.path.join(A, "slot.png"))
    mk.RAIL, mk.CELL, mk.GAP = RAIL, CELL, GAP

    cols, rows = 8, 4
    bar_row = mk.grid(cols, 1, slot)
    full_grid = mk.grid(cols, rows, slot)

    PW = full_grid.width + RAIL * 2 + PAD * 2
    PH = full_grid.height + RAIL * 2 + PAD * 2
    panel = mk.framed(PW, PH, board, edge_h, edge_v, corner)
    panel.alpha_composite(full_grid, (RAIL + PAD, RAIL + PAD))

    # Where the first row sits inside the panel, so the closed bar can be drawn at the same place.
    row_in_panel = (RAIL + PAD, RAIL + PAD)

    M = 60
    gap = 70
    sheet_w = M * 2 + PW * 2 + gap
    sheet_h = M * 2 + PH + 60
    sheet = Image.new("RGB", (sheet_w, sheet_h), (18, 19, 21))
    d = ImageDraw.Draw(sheet)

    # Both states drawn with the first row at the SAME height, because that is the claim:
    # the icons do not move, the frame arrives around them.
    row_y = M + 60 + row_in_panel[1]

    left = M
    d.text((left, M + 20), "closed - just the icons", fill=(215, 210, 200))
    sheet.paste(bar_row.convert("RGB"), (left + row_in_panel[0], row_y), bar_row)

    right = M + PW + gap
    d.text((right, M + 20), "tab - frame and rows draw around them", fill=(215, 210, 200))
    sheet.paste(panel.convert("RGB"), (right, row_y - row_in_panel[1]), panel)

    for yy in (row_y, row_y + bar_row.height):
        d.line([(M - 30, yy), (sheet_w - M + 30, yy)], fill=(125, 100, 62), width=1)

    out = os.path.join(ROOT, "..", "..", "inventory_mockup2.png")
    sheet.save(out)
    print("wrote inventory_mockup2.png  panel %dx%d  row lands at +%d,%d inside it"
          % (PW, PH, row_in_panel[0], row_in_panel[1]))


if __name__ == "__main__":
    main()
