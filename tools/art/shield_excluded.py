"""Builds the mark drawn over a one-handed weapon that has been told not to call the shield.

    python tools/art/shield_excluded.py            # -> tools/art/out/shield_excluded.png
    python tools/art/shield_excluded.py --install  # and copy into src/Assets/

Drawn rather than rendered, and the reason is what the thing is for: it is a status mark
sitting on top of an item icon at about 64 units square, so it has to read instantly against
whatever is underneath it - a bright sword, a dark axe, a blue-tinted quality bar. That wants
flat colour and hard geometry, not texture. Every ornament in this mod comes from the art
pipeline; this one is not an ornament.

Three things it does that a plain X does not:

  - A dark casing under the red. Pure red on a dark icon disappears; red with a near-black
    edge reads on both. The casing is drawn first and wider, so it shows as an outline.
  - Square caps, mitred at the crossing. Round caps read as a scribble at this size.
  - Drawn at 4x and downsampled. The diagonals are the whole mark, so aliased stair-stepping
    on them is the one flaw that would be visible; supersampling is cheaper than antialiased
    line code.

ShieldSlot draws this Simple at the cell's own rect, so the source only has to be big enough
not to soften - 256 is four times the size it is ever shown at.
"""
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(HERE, "out")

SIZE = 256
SS = 4                      # supersample factor

MARGIN = 0.22               # fraction of the square left clear at each corner
STROKE = 0.065              # red stroke width, as a fraction of the square
CASING = 0.026              # extra width of the dark casing, each side

RED = (204, 34, 34, 255)          # deep enough to sit under the red the game uses for danger
CASE = (18, 10, 10, 235)          # near-black, slightly warm, not fully opaque


def cross(draw, size, width, colour):
    lo = size * MARGIN
    hi = size - lo
    for a, b in (((lo, lo), (hi, hi)), ((lo, hi), (hi, lo))):
        draw.line([a, b], fill=colour, width=int(round(width)))
    # No cap drawing. The first attempt squared the ends off with an axis-aligned box at each
    # corner, on the reasoning that PIL has no cap style; looked at, those boxes read as four
    # notches stuck on the arms, because a square laid on the end of a 45 degree stroke
    # overhangs it on both sides. PIL's own butt cap is perpendicular to the stroke, which is
    # the cap that was wanted in the first place.


def build():
    size = SIZE * SS
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    cross(draw, size, size * (STROKE + CASING * 2), CASE)
    cross(draw, size, size * STROKE, RED)

    img = img.resize((SIZE, SIZE), Image.LANCZOS)

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "shield_excluded.png")
    img.save(path)
    print("shield_excluded %dx%d, stroke %d px red inside %d px casing"
          % (SIZE, SIZE, round(SIZE * STROKE), round(SIZE * (STROKE + CASING * 2))))

    if "--install" in sys.argv:
        import shutil
        shutil.copy2(path, os.path.join(ASSETS, "shield_excluded.png"))
        print("installed -> src/Assets/shield_excluded.png")


if __name__ == "__main__":
    build()
