"""Fits the concept's frame to vanilla's real inventory geometry.

    python tools/art/fit.py

Read from IngameGui_Inventory.prefab, not guessed: the Player panel is 570x287 and the grid
cell pitch is 70. Eight cells is 560 of that 570, so there are five units of margin inside
the panel and nothing ornate fits there. The frame is therefore drawn as a backdrop BEHIND
the panel, larger than it, rather than as the panel's own 9-slice.

Width never changes - only rows do, +70 each - so the top and bottom carvings are single
fixed pieces that never tile or stretch, which is where repeats normally give a UI away.
Only the side rails repeat, and they are cut from a plain run of the column.
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
A = os.path.join(ROOT, "src", "Assets")
BOARD = r"D:\Ai\Modding\concept_blank.png"

PITCH, COLS = 70, 8
PANEL_W = 570
ROWS_BASE = 4

# Cuts measured off concept_blank.png by cropping and looking at them.
# Measured, not eyeballed: the board's painted checkerboard starts at y=990, so the bottom
# cut stops at 988 or it drags a chequered strip into the asset. The top band runs to 246,
# where the first row of cells begins.
TOP = (120, 200, 845, 246)      # outer edge plus the carved band above the grid
BOT = (120, 878, 845, 988)      # carved band with the centre rosette, above the checkerboard
LEFT = (118, 430, 178, 700)     # plain run of the left column
RIGHT = (812, 430, 872, 700)    # plain run of the right column


def main():
    board = Image.open(BOARD).convert("RGBA")
    top = board.crop(TOP)
    bot = board.crop(BOT)
    left = board.crop(LEFT)
    right = board.crop(RIGHT)

    # One scale for everything: the cut frame's width becomes the panel width plus its margins.
    side = 44
    scale = (PANEL_W + side * 2) / top.width
    def sc(img):
        return img.resize((max(1, int(img.width * scale)), max(1, int(img.height * scale))), Image.LANCZOS)
    top, bot, left, right = sc(top), sc(bot), sc(left), sc(right)

    slot = Image.open(os.path.join(A, "slot.png")).convert("RGBA")
    cell = slot.resize((PITCH - 4, PITCH - 4), Image.LANCZOS)

    def build(rows):
        grid_h = rows * PITCH
        W = PANEL_W + side * 2
        H = top.height + grid_h + bot.height
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))

        # side rails first, repeated down the height, then the fixed carvings over them
        # Alternate repeats are flipped. A plain column of wood has no up or down, so this
        # breaks the march of an identical tile without inventing any new art.
        y, n = top.height, 0
        while y < top.height + grid_h:
            l = left if n % 2 == 0 else left.transpose(Image.FLIP_TOP_BOTTOM)
            r = right if n % 2 == 0 else right.transpose(Image.FLIP_TOP_BOTTOM)
            img.alpha_composite(l, (0, y))
            img.alpha_composite(r, (W - r.width, y))
            y += left.height
            n += 1
        interior = Image.new("RGBA", (PANEL_W, grid_h), (26, 22, 18, 255))
        img.alpha_composite(interior, (side, top.height))
        img.alpha_composite(top, (0, 0))
        img.alpha_composite(bot, (0, top.height + grid_h))

        gx = side + (PANEL_W - COLS * PITCH) // 2
        for r in range(rows):
            for c in range(COLS):
                img.alpha_composite(cell, (gx + c * PITCH + 2, top.height + r * PITCH + 2))
        return img

    four, six = build(4), build(6)
    M, GAP = 60, 70
    sheet = Image.new("RGB", (M * 2 + four.width + GAP + six.width, M * 2 + max(four.height, six.height) + 40), (18, 19, 21))
    d = ImageDraw.Draw(sheet)
    d.text((M, M - 24), "vanilla 4 rows - panel 570x287", fill=(215, 210, 200))
    sheet.paste(four.convert("RGB"), (M, M), four)
    d.text((M + four.width + GAP, M - 24), "6 rows with bag rows - grows +70 a row, width unchanged", fill=(215, 210, 200))
    sheet.paste(six.convert("RGB"), (M + four.width + GAP, M), six)
    sheet.save(os.path.join(ROOT, "..", "..", "inventory_fitted.png"))
    print("wrote inventory_fitted.png   4 rows %dx%d   6 rows %dx%d   scale %.3f"
          % (four.width, four.height, six.width, six.height, scale))


if __name__ == "__main__":
    main()
