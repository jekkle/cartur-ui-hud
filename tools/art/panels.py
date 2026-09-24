"""Rebuild every panel piece's interior from one fill.

Cartur's rule: nothing in the UI is a stretched low-resolution image. A 9-slice stretches
its centre across whatever rect it is given, so the only thing that keeps a pane sharp is
having enough pixels in that centre. Each piece below is therefore cut to cover the biggest
rect the game actually asks it for - measured from the mod's own skin listing in the BepInEx
log, not guessed - with 1.3x headroom for the screen, which renders about 1.3 device pixels
per canvas unit.

Nothing invents frame art. Every rail and rule here is lifted from art that already exists:
corners are copied pixel for pixel, edges are resized along their own run (which is exactly
what a 9-slice does to them at draw time), and only the centre is replaced.

The equipment panel is NOT touched by this script. It is one piece of painted art with the
slot boxes and their names in it, and it is measured, not assembled.

    python tools/art/panels.py

Source fill: tools/art/refs_fill.png, 1656x968.
"""
import os
from PIL import Image
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, "..", "..", "src", "Assets"))
FILL = Image.open(os.path.join(HERE, "refs_fill.png")).convert("RGBA")

# Cartur's frame: the rule and the corner knots on a fully transparent centre, so the fill
# goes in behind it and the two can never disagree at the join. Measured off the file - the
# rule is 19-20px deep and flush to the edge, the corner ornament reaches 57px in.
FRAME = Image.open(os.path.join(HERE, "refs_frame.png")).convert("RGBA")
CORNER = 57
RULE_DEEP = 20


def centre(w, h, top=0, left=0):
    """A crop of the fill, or the whole thing scaled down if more is asked for than exists."""
    fw, fh = FILL.size
    if left + w <= fw and top + h <= fh:
        return FILL.crop((left, top, left + w, top + h))
    return FILL.resize((w, h), Image.LANCZOS)


def reskin(name, border, size, crop_top=0):
    """Keep a piece's own rail, swap its interior. Corners copied, edges resized along the run."""
    path = os.path.join(ASSETS, name + ".png")
    old = Image.open(path).convert("RGBA")
    ow, oh = old.size
    b = border
    cw, ch = size
    new = Image.new("RGBA", (b * 2 + cw, b * 2 + ch))
    new.paste(old.crop((0, 0, b, b)), (0, 0))
    new.paste(old.crop((ow - b, 0, ow, b)), (b + cw, 0))
    new.paste(old.crop((0, oh - b, b, oh)), (0, b + ch))
    new.paste(old.crop((ow - b, oh - b, ow, oh)), (b + cw, b + ch))
    new.paste(old.crop((b, 0, ow - b, b)).resize((cw, b), Image.LANCZOS), (b, 0))
    new.paste(old.crop((b, oh - b, ow - b, oh)).resize((cw, b), Image.LANCZOS), (b, b + ch))
    new.paste(old.crop((0, b, b, oh - b)).resize((b, ch), Image.LANCZOS), (0, b))
    new.paste(old.crop((ow - b, b, ow, oh - b)).resize((b, ch), Image.LANCZOS), (b + cw, b))
    new.paste(centre(cw, ch, top=crop_top), (b, b))
    new.save(path, optimize=True)
    print(f"  {name:22} {ow}x{oh} -> {new.size[0]}x{new.size[1]}  border {b}  centre {cw}x{ch}")


# The frame's rule, taken from the middle of each side - never a corner, which is where the
# knot lives. Used for every piece that carries the rule without the knots, so a small panel's
# edge is the same edge as a big one's, just with nothing in the corners.
_F = np.asarray(FRAME).astype(np.uint8)
RULE = {
    "top": _F[0:RULE_DEEP, _F.shape[1] // 2, :],
    "bottom": _F[_F.shape[0] - 1:_F.shape[0] - 1 - RULE_DEEP:-1, _F.shape[1] // 2, :],
    "left": _F[_F.shape[0] // 2, 0:RULE_DEEP, :],
    "right": _F[_F.shape[0] // 2, _F.shape[1] - 1:_F.shape[1] - 1 - RULE_DEEP:-1, :],
}


def framed(name, size, crop_top=0):
    """The frame laid over the fill: knots in the corners, rule down the edges, fill behind.

    The frame art carries a transparent centre, so the fill is laid down across the WHOLE
    canvas first and the frame composited on top. That is what removes the join Cartur was
    looking at - there is no second fill inside the frame to disagree with ours, and the rule
    sits directly on the same texture the panel is made of.

    Corners are pasted at their own size and never scaled; the edges are stretched along their
    own run, which is what the 9-slice does to them at draw time anyway.
    """
    cw, ch = size
    B = CORNER
    W, H = cw, ch
    base = centre(W, H, top=crop_top).convert("RGBA")

    fw, fh = FRAME.size
    over = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    over.paste(FRAME.crop((0, 0, B, B)), (0, 0))
    over.paste(FRAME.crop((fw - B, 0, fw, B)), (W - B, 0))
    over.paste(FRAME.crop((0, fh - B, B, fh)), (0, H - B))
    over.paste(FRAME.crop((fw - B, fh - B, fw, fh)), (W - B, H - B))
    over.paste(FRAME.crop((B, 0, fw - B, B)).resize((W - 2 * B, B), Image.LANCZOS), (B, 0))
    over.paste(FRAME.crop((B, fh - B, fw - B, fh)).resize((W - 2 * B, B), Image.LANCZOS), (B, H - B))
    over.paste(FRAME.crop((0, B, B, fh - B)).resize((B, H - 2 * B), Image.LANCZOS), (0, B))
    over.paste(FRAME.crop((fw - B, B, fw, fh - B)).resize((B, H - 2 * B), Image.LANCZOS), (W - B, B))

    out = Image.alpha_composite(base, over)
    out.save(os.path.join(ASSETS, name + ".png"), optimize=True)
    print(f"  {name:22} {W}x{H}  border {B}  frame over fill")


def mitred(name, size, tint=(1.0, 1.0, 1.0), crop_top=0, band=RULE_DEEP):
    """The pack's double rule with the knots left off, mitred at the corners.

    The corner is decided by which edge is nearest, so the rules turn the corner on the
    diagonal instead of crossing each other - crossing them reads as a plaid, which is what
    the first cut of this looked like.
    """
    b = band
    cw, ch = size
    w, h = b * 2 + cw, b * 2 + ch
    out = np.zeros((h, w, 4), dtype=np.uint8)
    out[b:b + ch, b:b + cw] = np.asarray(centre(cw, ch, top=crop_top)).astype(np.uint8)
    for y in range(h):
        for x in range(w):
            d = {"top": y, "bottom": h - 1 - y, "left": x, "right": w - 1 - x}
            side = min(d, key=d.get)
            if d[side] < b:
                out[y, x] = RULE[side][d[side]]
    out[:, :, 3] = 255
    if tint != (1.0, 1.0, 1.0):
        f = out.astype(float)
        for c in range(3):
            f[:, :, c] = np.clip(f[:, :, c] * tint[c], 0, 255)
        out = f.astype(np.uint8)
    Image.fromarray(out, "RGBA").save(os.path.join(ASSETS, name + ".png"), optimize=True)
    print(f"  {name:22} {w}x{h}  border {b}  centre {cw}x{ch}")


# Biggest rect each piece is asked to cover, from the skin listing, x 1.3 for the screen.
print("knotted panels (Cartur's frame over the fill)")
framed("panel_ornate", (1656, 968))              # biggest use 1220x820 units
framed("panel_ornate_bar", (1656, 162), crop_top=400)   # the hotbar: wide and short

# The frame with nothing behind it, for drawing OVER something - the map and the minimap.
# Kept exactly as Cartur drew it: transparent centre, and transparent between the rule and the
# centre too, so the map shows through right up to the gold.
print("frame alone, for overlays")
FRAME.save(os.path.join(ASSETS, "panel_frame.png"), optimize=True)
print(f'  {"panel_frame":22} {FRAME.size[0]}x{FRAME.size[1]}  border {CORNER}  transparent centre')

print("the rule without knots")
mitred("panel_thin", (1344, 256))                # biggest use 1024x187 units, no knots

# Tones measured off the kit's own button set, so the states read the same under the hand.
print("buttons and tabs")
for piece, ratio in {
    "button_thin": (1.000, 1.000, 1.000),
    "button_thin_hover": (1.141, 1.110, 1.058),
    "button_thin_pressed": (0.833, 0.823, 0.815),
    "button_thin_disabled": (0.960, 0.993, 1.029),
    "tab_thin": (0.958, 0.964, 0.970),
    "tab_thin_hover": (1.034, 1.044, 1.054),
    "tab_thin_selected": (1.065, 1.044, 1.004),
}.items():
    mitred(piece, (448, 96), ratio, crop_top=400)   # biggest use 334x64 units

# These four used to carry the old kit's own rail, which was authored with a grey-lit top
# edge. Beside the pack's gold rule that read as a grey outline around every pane, cell and
# textbox - measured on Cartur's screenshot, the window's rule runs 92,74,49 (43 apart) while
# those rails ran 99,89,77 (22 apart). They take the same rule as everything else now, cut at
# the same thickness in units as the rail they replace, so nothing moves - only the colour.
def cell_frame():
    """The slot outline, lifted straight off the equipment panel's own painted boxes.

    Cartur asked for the cells to wear the same border those slots do, and the honest way to
    do that is to take it rather than draw something like it. The Head box is cropped out of
    equipment_panel.png and everything that is not the gold line is keyed to transparent, so a
    cell shows the panel behind it exactly the way an equipment slot does.

    Measured on that art: the line is 3px thick and the cut corner reaches about 24px in, so
    the 9-slice border is 26 - enough to carry the corner without touching the straight run.
    """
    art = Image.open(os.path.join(ASSETS, "equipment_panel.png")).convert("RGB")
    crop = np.asarray(art.crop((294, 94, 438, 235))).astype(float)   # the Head box, 144x141
    r, g, b = crop[:, :, 0], crop[:, :, 1], crop[:, :, 2]

    # Gold is the only warm thing in the crop: the fill either side of the line is neutral.
    warm = np.clip(((r - b) - 6.0) / 30.0, 0.0, 1.0)
    out = np.zeros((crop.shape[0], crop.shape[1], 4), dtype=np.uint8)
    out[:, :, 0:3] = crop.astype(np.uint8)
    out[:, :, 3] = (warm * 255).astype(np.uint8)
    Image.fromarray(out, "RGBA").save(os.path.join(ASSETS, "slot.png"), optimize=True)
    print(f'  {"slot":22} {crop.shape[1]}x{crop.shape[0]}  border 26  outline only, from the equipment art')


print("panes, cells and boxes - same rule as the panels")
mitred("well", (1656, 968))                      # biggest use 1260x734 units
mitred("tooltip", (544, 800))
cell_frame()                                     # a cell is 64 units
for f in ("field", "field_hover", "field_disabled"):
    mitred(f, (256, 256))                        # boxes, and 180x180 achievement tiles
