"""Inventory beside the equipment and character panel, at vanilla's geometry.

    python tools/art/fit2.py

The tall-right, short-left arrangement is not a liberty - it is what the game already does.
Read from IngameGui_Inventory.prefab: Player is 570x287, Crafting is 570x650, Info 570x130,
and Crafting and Info are anchored top-RIGHT while Player is anchored top-LEFT. So a short
inventory beside a tall right-hand panel is vanilla's own layout, and the concept happens to
match it.

Four rows only here, as asked - no bag rows.
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
A = os.path.join(ROOT, "src", "Assets")
BOARD = "tools/art/out/blank/board_clearcol.png"   # right column cleared of recesses

PITCH, COLS, ROWS = 70, 8, 4
PANEL_W = 570
SIDE = 44

TOP = (120, 200, 845, 246)
BOT = (120, 878, 845, 988)
LEFT = (118, 430, 178, 700)
RIGHT = (812, 430, 872, 700)
# The right-hand half of the board: equipment columns and the character alcove, whole.
# Content bounds measured on the blank board - x stops at 1483, y at 995, before the
# painted checkerboard starts.
EQUIP = (845, 150, 1483, 990)


FIELD_A = 255      # alpha of the grid field
SHOT = "C:/Users/Tool1/.claude/uploads/603ce7e7-6dff-4749-8f0d-4aa2295ccee8/2c75394a-image.jpg"


def main():
    board = Image.open(BOARD).convert("RGBA")
    top, bot = board.crop(TOP), board.crop(BOT)
    left, right = board.crop(LEFT), board.crop(RIGHT)
    equip = board.crop(EQUIP)

    # The inpaint rebuilt the right column several shades lighter and warmer than the left one.
    # Rather than re-roll the render and hope, its tone is matched to the left column's by
    # mean and spread, per channel - the carving it generated is fine, only the colour drifted.
    import numpy as np
    ex0, ey0 = EQUIP[0], EQUIP[1]
    def region(x0, x1, y0, y1):
        return (int(x0 - ex0), int(y0 - ey0), int(x1 - ex0), int(y1 - ey0))
    left_box = region(846, 980, 249, 806)
    right_box = region(1286, 1420, 249, 806)
    arr = np.asarray(equip).astype(np.float64)
    lx0, ly0, lx1, ly1 = left_box
    rx0, ry0, rx1, ry1 = right_box
    src = arr[ry0:ry1, rx0:rx1, :3]
    ref = arr[ly0:ly1, lx0:lx1, :3]
    for c in range(3):
        sm, ss = src[..., c].mean(), src[..., c].std() or 1.0
        rm, rs = ref[..., c].mean(), ref[..., c].std()
        src[..., c] = np.clip((src[..., c] - sm) * (rs / ss) + rm, 0, 255)
    arr[ry0:ry1, rx0:rx1, :3] = src
    equip = Image.fromarray(arr.astype(np.uint8), "RGBA")

    # The concept's eight equipment slots came out of the inpaint mangled - eight different
    # bezels, green and magenta fringing, the right column blue-grey against the left's black.
    # Rendering a replacement slot gave wall sockets. So they are stamped with the mod's own
    # slot.png, which is what vanilla's item_background maps to everywhere else: the equipment
    # slots then match the inventory cells instead of being their own species.
    def slice9(src, b, W, H):
        sw, sh = src.size
        o = Image.new("RGBA", (W, H))
        box = {"tl": (0,0,b,b), "tr": (sw-b,0,sw,b), "bl": (0,sh-b,b,sh), "br": (sw-b,sh-b,sw,sh),
               "t": (b,0,sw-b,b), "bo": (b,sh-b,sw-b,sh), "l": (0,b,b,sh-b), "r": (sw-b,b,sw,sh-b),
               "c": (b,b,sw-b,sh-b)}
        c = {k: src.crop(v) for k, v in box.items()}
        o.paste(c["c"].resize((max(W-2*b,1), max(H-2*b,1))), (b,b))
        o.paste(c["t"].resize((max(W-2*b,1), b)), (b,0))
        o.paste(c["bo"].resize((max(W-2*b,1), b)), (b,H-b))
        o.paste(c["l"].resize((b, max(H-2*b,1))), (0,b))
        o.paste(c["r"].resize((b, max(H-2*b,1))), (W-b,b))
        for k, pos in (("tl",(0,0)), ("tr",(W-b,0)), ("bl",(0,H-b)), ("br",(W-b,H-b))):
            o.paste(c[k], pos)
        return o

    slot_src = Image.open(os.path.join(A, "slot.png")).convert("RGBA")
    ex, ey = EQUIP[0], EQUIP[1]
    SHRINK = 0.84          # a little smaller than its cell, so the bezel stops dominating
    SHRINK = 0.84          # a little smaller than its cell, so the bezel stops dominating
    # The concept's two columns were not symmetrical: the left ran 255..745 and the right
    # 255..800, so its cells were 136 tall against the left's 122 and the slots came out
    # different sizes. Both columns now use one span and one cell height.
    # Seven slots: head, chest, legs, back on the left; waist, trinket, shield on the right.
    #
    # Each new slot is stamped EXACTLY over one of the concept's own slot rects, so it replaces
    # a slot with a slot and the carved column around it is never painted over. The earlier
    # version covered whole columns with a dark strap to hide the mangled originals, which
    # buried the carving - that was the wrong trade.
    #
    # The interior is dropped to let the wood behind show through, so the slots read as recesses
    # cut into the column rather than tiles laid on top of it.
    # The left column keeps the concept's own four recesses, so its slots land exactly on them.
    L_SPAN = (255, 745)
    CELL = (L_SPAN[1] - L_SPAN[0]) / 4
    L_RECTS = [(852, 972, L_SPAN[0] + CELL * i, L_SPAN[0] + CELL * (i + 1)) for i in range(4)]

    # The right column was cleared of its recesses, so its three sit at the LEFT's cell size,
    # centred on the left's span. Same size, same pitch, and the group's centre lines up with
    # the group opposite - which is as symmetrical as three against four gets.
    r_top = L_SPAN[0] + ((L_SPAN[1] - L_SPAN[0]) - CELL * 3) / 2
    R_RECTS = [(1292, 1412, r_top + CELL * i, r_top + CELL * (i + 1)) for i in range(3)]

    def stamp(rect):
        x0, x1, y0, y1 = rect
        cw, ch = int(x1 - x0), int(y1 - y0) - 8
        piece = slice9(slot_src, 16, cw, ch)
        inner = piece.crop((16, 16, cw - 16, ch - 16))
        inner.putalpha(inner.getchannel("A").point(lambda v: int(v * 0.45)))
        piece.paste(inner, (16, 16))
        equip.alpha_composite(piece, (int(x0 - ex), int(y0 - ey) + 4))

    for r in L_RECTS + R_RECTS:
        stamp(r)

    scale = (PANEL_W + SIDE * 2) / top.width
    def sc(i):
        return i.resize((max(1, int(i.width * scale)), max(1, int(i.height * scale))), Image.LANCZOS)
    top, bot, left, right, equip = sc(top), sc(bot), sc(left), sc(right), sc(equip)

    slot = Image.open(os.path.join(A, "slot.png")).convert("RGBA")
    cell = slot.resize((PITCH - 4, PITCH - 4), Image.LANCZOS)

    grid_h = ROWS * PITCH
    W = PANEL_W + SIDE * 2
    H = top.height + grid_h + bot.height
    inv = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    y, n = top.height, 0
    while y < top.height + grid_h:
        l = left if n % 2 == 0 else left.transpose(Image.FLIP_TOP_BOTTOM)
        r = right if n % 2 == 0 else right.transpose(Image.FLIP_TOP_BOTTOM)
        inv.alpha_composite(l, (0, y))
        inv.alpha_composite(r, (W - r.width, y))
        y += left.height
        n += 1
    inv.alpha_composite(Image.new("RGBA", (PANEL_W, grid_h), (26, 22, 18, FIELD_A)), (SIDE, top.height))
    field_box = (SIDE, top.height, PANEL_W, grid_h)
    inv.alpha_composite(top, (0, 0))
    inv.alpha_composite(bot, (0, top.height + grid_h))
    gx = SIDE + (PANEL_W - COLS * PITCH) // 2
    for r_ in range(ROWS):
        for c in range(COLS):
            inv.alpha_composite(cell, (gx + c * PITCH + 2, top.height + r_ * PITCH + 2))

    M, GAP = 55, 18
    # Three stops for the right panel, all the same cut at different scales, so the choice is
    # about size and nothing else. Heights are in the same units as the inventory art, which is
    # 1 unit per pixel here: the inventory is 658 wide for 570 of panel plus its two 44 margins.
    h = 600
    w = int(equip.width * h / equip.height)
    e = equip.resize((w, h), Image.LANCZOS)

    # Translucency only means anything against a scene, so these go over a real game frame.
    # A clean patch of scene: the full screenshot has the old UI open in it, and vanilla's
    # panels bleeding through a translucency test tells you nothing about the translucency.
    shot = Image.open(SHOT).convert("RGB").crop((100, 1120, 1340, 1700))
    GAPX = 18
    block_w = inv.width + GAPX + e.width
    block_h = max(inv.height, e.height)

    def fade(img, a):
        out = img.copy()
        out.putalpha(out.getchannel("A").point(lambda v: int(v * a)))
        return out

    def plate(inv_img, eq_img):
        bg = shot.resize((block_w + 80, block_h + 80), Image.LANCZOS).convert("RGBA")
        bg.alpha_composite(inv_img, (40, 40))
        bg.alpha_composite(eq_img, (40 + inv_img.width + GAPX, 40))
        return bg.convert("RGB")

    def field_only(img, a):
        """Frame stays solid; only the grid field behind the cells lets the scene through."""
        out = img.copy()
        fx, fy, fw, fh = field_box
        patch = out.crop((fx, fy, fx + fw, fy + fh))
        patch.putalpha(patch.getchannel("A").point(lambda v: int(v * a)))
        out.paste(patch, (fx, fy))
        return out

    plates = [("solid, as it is now", plate(inv, e)),
              ("frame solid, field at 55%", plate(field_only(inv, 0.55), e)),
              ("whole panel at 80%", plate(fade(inv, 0.80), fade(e, 0.80)))]

    M = 46
    sheet = Image.new("RGB", (M * 2 + plates[0][1].width,
                              M + sum(p[1].height + M + 26 for p in plates)), (18, 19, 21))
    d = ImageDraw.Draw(sheet)
    y = M + 20
    for note, im in plates:
        d.text((M, y - 22), note, fill=(215, 210, 200))
        sheet.paste(im, (M, y))
        y += im.height + M + 26
    sheet.save(os.path.join(ROOT, "..", "..", "inventory_full.png"))
    print("wrote inventory_full.png   inv %dx%d   equip %dx%d" % (inv.width, inv.height, equip.width, equip.height))


if __name__ == "__main__":
    main()
