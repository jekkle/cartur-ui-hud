"""Builds board_serverlist.png from board_world.png (2026-10-04).

The world and server screens share one panel and its tab plates, so the server board IS the
world board - same frame, ravens, Host/Join plates, title plate and list well, pixel for pixel -
with only the lower half rebuilt for the server controls out of the world art's own pieces: its
plain wood strip (tiled, every other copy mirrored, which reads as planks), its button plate and
its long field, each 3-sliced so the rims and corners keep their size, and its big box for Connect.
Box positions are written here and are the ones MenuBoards.Server seats the controls on.
"""
import os
from PIL import Image, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(os.path.dirname(os.path.dirname(HERE)), "src", "Assets")

# Pieces of board_world.png (638x774), px from its top-left.
WOOD = (278, 436, 322, 706)
PLATE = (80, 441, 272, 488)
FIELD = (328, 563, 538, 598)
BIGBOX = (328, 620, 568, 702)

# The server layout, same px space.
BOXES = {
    "Refresh": (80, 443, 126, 488, "plate"),
    "FilterField": (134, 446, 318, 486, "field"),
    "RemoveButton": (326, 443, 372, 488, "plate"),
    "Add server": (380, 443, 568, 488, "plate"),
    "Back": (80, 645, 300, 690, "plate"),
    # The favourites move-up/down buttons are not painted: they carry their own plate in game and
    # only show when they can be used (review, 2026-10-04: two empty boxes).
}


def slice3(piece, width, cap):
    w, h = piece.size
    left, mid, right = piece.crop((0, 0, cap, h)), piece.crop((cap, 0, w - cap, h)), piece.crop((w - cap, 0, w, h))
    out = Image.new("RGBA", (width, h))
    out.paste(left, (0, 0))
    out.paste(mid.resize((max(1, width - 2 * cap), h), Image.LANCZOS), (cap, 0))
    out.paste(right, (width - cap, 0))
    return out


def main():
    world = Image.open(os.path.join(ASSETS, "board_world.png")).convert("RGBA")
    out = world.copy()
    wood = world.crop(WOOD)
    # Clear the lower half to wood, staying off the rails and the bottom ravens.
    for x in range(52, 578, wood.width):
        tile = wood if (x // wood.width) % 2 == 0 else ImageOps.mirror(wood)
        tile = tile.crop((0, 0, min(wood.width, 578 - x), wood.height))
        top = tile.crop((0, 0, tile.width, 680 - 436))
        out.paste(top, (x, 436))
        lo, hi = max(x, 86), min(x + tile.width, 540)
        if hi > lo:
            out.paste(tile.crop((lo - x, 680 - 436, hi - x, 706 - 436)), (lo, 680))
    out.paste(world.crop(BIGBOX), BIGBOX[:2])          # Connect keeps the world's Start box
    plate, field = world.crop(PLATE), world.crop(FIELD)
    for name, (x0, y0, x1, y1, kind) in BOXES.items():
        src, cap = (plate, 14) if kind == "plate" else (field, 16)
        piece = slice3(src, x1 - x0, cap)
        if piece.height != y1 - y0:
            piece = piece.resize((piece.width, y1 - y0), Image.LANCZOS)
        out.alpha_composite(piece, (x0, y0))
    out.save(os.path.join(ASSETS, "board_serverlist.png"))
    print("board_serverlist.png", out.size)


if __name__ == "__main__":
    main()
