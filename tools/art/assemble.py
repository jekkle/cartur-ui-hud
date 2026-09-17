"""Builds every frame piece straight out of the HUD's own art, so the menus are the
same bronze pixel for pixel as the bars.

    python tools/art/assemble.py            # all pieces -> tools/art/out/
    python tools/art/assemble.py --install  # and copy passing pieces into src/Assets/

Source measurements (bar_frame.png, 1181x82, opaque rows 5..76): the top rail is
rows 5..18 (grey-lit bronze), the bottom rail rows 64..77 (warm bronze), and the
window between is a dark gradient ~18 at the top to ~40 at the bottom. The bar draws
at 2.56 px per game unit; pieces here are authored at 2 px per unit (AssetLoader
creates them at 2x the canvas reference ppu), so the rail is scaled by 2/2.56.
Corners are mitred: the bar has no 90-degree corner to borrow, and a 45-degree join
of the two rails is what a bronze frame would do anyway.
"""
import json
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(HERE, "out")
sys.path.insert(0, HERE)
import check_piece  # noqa: E402
PIECES = json.load(open(os.path.join(HERE, "pieces.json")))["pieces"]

BAR = Image.open(os.path.join(ASSETS, "bar_frame.png")).convert("RGBA")
SCALE = 2 / 2.56
TOP_RAIL = (5, 19)       # rows of the grey-lit rail
BOTTOM_RAIL = (64, 78)   # rows of the warm rail, faded edge included
RAIL_X = (330, 900)      # plain run of rail, no ornament
RAIL = int(round((TOP_RAIL[1] - TOP_RAIL[0]) * SCALE))  # ~11 px at authoring scale

OAK = (58, 42, 28)          # dark oak, keyed off the bar rail's own warm tone
GOLD = (1.35, 1.15, 0.75)  # per-channel multiplier for the selected/hover state


def rail_strip(rows, length):
    """A horizontal rail `length` px long, RAIL px tall, tiled from the bar's plain run."""
    src = BAR.crop((RAIL_X[0], rows[0], RAIL_X[1], rows[1]))
    src = src.resize((int(src.width * SCALE), RAIL), Image.LANCZOS)
    strip = Image.new("RGBA", (length, RAIL))
    for x in range(0, length, src.width):
        strip.paste(src, (x, 0))
    return match_rail(strip)


DIAMOND = Image.open(os.path.join(ASSETS, "food_frame.png")).convert("RGBA")

# The diamond rotated 45 degrees puts its own border and interior on straight edges, which is
# how both are sampled here. Cartur picked that diamond as the look; matching it means taking
# its actual pixels rather than something chosen to sit near them.
_ROT = np.asarray(DIAMOND.rotate(-45, resample=Image.BICUBIC, expand=True)).astype(np.float32)
_OPAQUE = np.nonzero(_ROT[..., 3] > 200)
_Y0, _Y1 = _OPAQUE[0].min(), _OPAQUE[0].max()
_X0, _X1 = _OPAQUE[1].min(), _OPAQUE[1].max()
_MY, _MX = (_Y0 + _Y1) // 2, (_X0 + _X1) // 2

DIAMOND_FILL = _ROT[_MY - 28:_MY + 28, _MX - 28:_MX + 28, :3].copy()      # its interior, texture and all
DIAMOND_RAIL = _ROT[_Y0 + 2:_Y0 + 16, _X0 + 60:_X1 - 60, :3].reshape(-1, 3)


def wood(w, h, border, lift=0.0, grain=True, tile=False):
    """The diamond's own interior, tiled. Flat colour read as black in game; this is the exact
    surface of the piece Cartur kept."""
    fill = DIAMOND_FILL
    fh, fw, _ = fill.shape
    px = np.zeros((h, w, 3), np.float32)
    for y in range(0, h, fh):
        for x in range(0, w, fw):
            ph, pw = min(fh, h - y), min(fw, w - x)
            px[y:y + ph, x:x + pw] = fill[:ph, :pw]
    px = np.clip(px * (1.0 + lift), 0, 255)
    im = Image.new("RGBA", (w, h))
    im.paste(Image.fromarray(px.astype(np.uint8), "RGB"))
    return im


def match_rail(strip):
    """Puts the bar's rail into the diamond's bronze. The bar rail sits at mean 67,62,56 and the
    diamond's band at 118,105,93 - side by side that reads as two different metals, which is
    what made the panels look dead next to the food boxes."""
    a = np.asarray(strip).astype(np.float32)
    rgb, alpha = a[..., :3], a[..., 3:]
    lit = alpha[..., 0] > 8
    if lit.any():
        src = rgb[lit]
        for c in range(3):
            sm, ss = src[:, c].mean(), max(src[:, c].std(), 1.0)
            dm, ds = DIAMOND_RAIL[:, c].mean(), DIAMOND_RAIL[:, c].std()
            rgb[lit, c] = np.clip((src[:, c] - sm) * (ds / ss) + dm, 0, 255)
    return Image.fromarray(np.concatenate([rgb, alpha], axis=2).astype(np.uint8), "RGBA")


def interior(w, h, lift=0.0, grain=1.5):
    """Kept for the pieces that want the bar's own flat dark rather than oak."""
    win = np.asarray(BAR.crop((RAIL_X[0], TOP_RAIL[1], RAIL_X[1], BOTTOM_RAIL[0])).convert("RGB")).astype(np.float32)
    tone = np.median(win.reshape(-1, 3), axis=0)
    px = np.broadcast_to(tone, (h, w, 3)).copy()
    if grain:
        px += np.random.default_rng(0).normal(0, grain, (h, w, 1)).astype(np.float32)
    px = np.clip(px * (1 + lift) + lift * 12, 0, 255)
    im = Image.new("RGBA", (w, h))
    im.paste(Image.fromarray(px.astype(np.uint8), "RGB"))
    return im


def frame(w, h, border=24, sides=("top", "bottom", "left", "right"), tint=None, hollow=False, lift=0.0, tile=False):
    im = wood(w, h, border, lift, True, tile) if not hollow else Image.new("RGBA", (w, h), (0, 0, 0, 0))
    top = rail_strip(TOP_RAIL, w)
    bottom = rail_strip(BOTTOM_RAIL, w)
    left = rail_strip(TOP_RAIL, h).rotate(90, expand=True)
    right = rail_strip(BOTTOM_RAIL, h).rotate(-90, expand=True)
    # mitre masks: each rail only owns the pixels nearer to its own edge
    def mitre(size, which):
        m = Image.new("L", size, 0)
        d = ImageDraw.Draw(m)
        W, H = size
        r = RAIL
        poly = {
            "top": [(0, 0), (W, 0), (W - r, r), (r, r)],
            "bottom": [(0, H), (W, H), (W - r, H - r), (r, H - r)],
            "left": [(0, 0), (0, H), (r, H - r), (r, r)],
            "right": [(W, 0), (W, H), (W - r, H - r), (W - r, r)],
        }[which]
        d.polygon(poly, fill=255)
        return m
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for name, strip, pos in (("top", top, (0, 0)), ("bottom", bottom, (0, h - RAIL)),
                             ("left", left, (0, 0)), ("right", right, (w - RAIL, 0))):
        if name not in sides:
            continue
        piece = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        piece.paste(strip, pos)
        layer.paste(piece, (0, 0), mitre((w, h), name))
    if tint:
        px = np.asarray(layer).astype(np.float32)
        px[..., :3] = np.clip(px[..., :3] * np.array(tint, np.float32), 0, 255)
        layer = Image.fromarray(px.astype(np.uint8), "RGBA")
    im.alpha_composite(layer)
    return im


def build(name, size, border=24):
    w, h = size
    if name == "panel" or name == "tooltip":
        return frame(w, h, border)
    if name == "panel_selected":
        # vanilla draws the gamepad focus as a tinted copy of the panel shape; a hollow gold rail does the same job over ours
        return frame(w, h, border, tint=GOLD, hollow=True)
    if name == "well":
        return frame(w, h, PIECES[name]["border"], lift=-0.25, tile=True)
    if name == "slot":
        return frame(w, h, border)
    if name == "slot_selected":
        return frame(w, h, border, tint=GOLD, hollow=True)
    if name == "button":
        return frame(w, h, border)
    if name == "button_hover":
        return frame(w, h, border, tint=(1.15, 1.08, 0.95), lift=0.15)
    if name == "button_pressed":
        return frame(w, h, border, lift=-0.3)
    if name == "button_disabled":
        im = frame(w, h, border)
        px = np.asarray(im).astype(np.float32)
        g = px[..., :3].mean(axis=2, keepdims=True)
        px[..., :3] = px[..., :3] * 0.4 + g * 0.6
        px[..., 3] *= 0.7
        return Image.fromarray(px.astype(np.uint8), "RGBA")
    if name == "tab":
        return frame(w, h, border, sides=("top", "left", "right"))
    if name == "tab_hover":
        return frame(w, h, border, sides=("top", "left", "right"), lift=0.15)
    if name == "tab_selected":
        return frame(w, h, border, sides=("top", "left", "right"), tint=(1.15, 1.08, 0.95), lift=0.1)
    if name == "field":
        return frame(w, h, border, lift=-0.2)
    if name == "field_hover":
        return frame(w, h, border, tint=(1.15, 1.08, 0.95), lift=-0.1)
    if name == "field_disabled":
        return frame(w, h, border, lift=-0.45)
    raise KeyError(name)


def main():
    install = "--install" in sys.argv
    pieces = json.load(open(os.path.join(HERE, "pieces.json")))["pieces"]
    os.makedirs(OUT, exist_ok=True)
    rc = 0
    for name, spec in pieces.items():
        if spec.get("kind", "frame") != "frame":
            continue
        size = pieces[spec.get("variant_of", name)]["size"]
        im = build(name, size, spec["border"])
        path = os.path.join(OUT, f"{name}.png")
        im.save(path)
        fails = check_piece.check(path)
        if fails:
            rc = 1
            print(f"FAIL {name}: " + "; ".join(fails))
            continue
        print(f"PASS {name} {im.size}")
        if install:
            shutil.copyfile(path, os.path.join(ASSETS, f"{name}.png"))
    return rc


if __name__ == "__main__":
    sys.exit(main())
