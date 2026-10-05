"""Layout guide for the build menu (BuildUIV2) board, for grok/gen.mjs.

Rects are the pilot's dump of BuildUIV2 (results.txt, 2026-10-05, screen px at 2560x1440, y up):
panel BuildUIV2/bar/SelectionWindow/Background 556.67..2003.33 x 460..1113.33. Drawn at the same
1120-px-wide mapping as enchant_guides.py. Dark grey = plate / socket, light grey = well / field.

The hammer button right of the filter is the one rect not in the dump (it carries only a gamepad
hint there); 805..855 x 972..1022 is read off the screenshot - checked on the overlay afterwards.

Writes grok/guide_build.png and grok/sheet_build.png (guide left, the enchanting board right as
the style sample).
"""
from PIL import Image, ImageDraw

PX0, PX1, PY0, PY1 = 556.67, 2003.33, 460.0, 1113.33
W = 1120
K = W / (PX1 - PX0)
H = round((PY1 - PY0) * K)
M = 40
DARK, LIGHT, BG, EDGE = (140, 140, 140), (176, 176, 176), (100, 100, 100), (55, 55, 55)

RECTS = [
    ("d", 594, 1039, 927, 1084), ("d", 940, 1039, 1273, 1084),
    ("d", 1287, 1039, 1620, 1084), ("d", 1633, 1039, 1966, 1084),   # the four tabs
    ("d", 565, 1051, 607, 1094), ("d", 1953, 1051, 1995, 1094),     # Q / E key sockets
    ("l", 593, 972, 798, 1023),                                     # filter field
    ("d", 805, 972, 855, 1022),                                     # hammer button (screenshot)
    ("l", 593, 495, 860, 961),                                      # category list
    ("l", 873, 495, 1966, 1023),                                    # piece grid
]


def px(x, y):
    return M + (x - PX0) * K, M + (PY1 - y) * K


def guide():
    im = Image.new("RGB", (W + 2 * M, H + 2 * M), (255, 255, 255))
    d = ImageDraw.Draw(im)
    d.rectangle([M, M, M + W, M + H], fill=EDGE)
    d.rectangle([M + 22, M + 22, M + W - 22, M + H - 22], fill=BG)
    for kind, x0, y0, x1, y1 in RECTS:
        (a, b), (c, e) = px(x0, y1), px(x1, y0)
        d.rectangle([a, b, c, e], fill=DARK if kind == "d" else LIGHT)
    return im


if __name__ == "__main__":
    g = guide()
    g.save("grok/guide_build.png")
    s = Image.open("../../src/Assets/board_enchant_sacrifice.png").convert("RGBA")
    s = s.resize((round(s.width * g.height / s.height), g.height))
    out = Image.new("RGB", (g.width + 40 + s.width, g.height), (255, 255, 255))
    out.paste(g, (0, 0))
    out.paste(s, (g.width + 40, 0), s)
    out.save("grok/sheet_build.png")
    print("guide", g.size, "sheet", out.size)
