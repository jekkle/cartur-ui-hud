"""Layout guides for Epic Loot's enchanting table, one per tab, for grok/gen.mjs.

Every rect is the pilot's dump of that tab (results.txt, 2026-10-05, screen px at 2560x1440, y up),
keeping only what the tab actually shows. Panel (533.33,253.33)..(2026.67,1186.67) = 1120x700 panel
units, drawn at 1 px per unit inside a 40 px margin - the same mapping guide_enchant.png used.
Dark grey = bronze plate / socket, light grey = recessed well / field.

Writes grok/guide_enchant_<tab>.png and grok/sheet_enchant_<tab>.png (guide left, the current
board_enchant.png on white right, as the style sample).
"""
from PIL import Image, ImageDraw

PX0, PY1, K, M = 533.33, 1186.67, 0.75, 40
DARK, LIGHT, BG, EDGE = (140, 140, 140), (176, 176, 176), (100, 100, 100), (55, 55, 55)

TABS = [("d", 577, y, 760, y + 106) for y in (1027, 907, 787, 667, 547, 427, 307)]
LEFT = [("l", 767, 345, 1193, 1051), ("l", 980, 1057, 1193, 1087), ("l", 980, 300, 1193, 340)]
MIDDLE = [("d", 1233, 345, 1513, 405), ("d", 1233, 420, 1513, 473)]
RIGHT = [("l", 1553, 345, 1980, 1051)]

def toggles(*ys):
    return [("d", 1233, a, 1513, b) for a, b in ys]

LAYOUTS = {
    "sacrifice": TABS + LEFT + MIDDLE + RIGHT + [("d", 783, 1061, 804, 1082)]
                 + toggles((1014, 1074), (940, 1000)),
    "convert": TABS + LEFT + MIDDLE + RIGHT + toggles((1025, 1079), (964, 1017), (903, 956), (841, 895)),
    "enchant": TABS + LEFT + MIDDLE + RIGHT
               + toggles((1003, 1050), (943, 990), (883, 930), (823, 870), (763, 810), (703, 750)),
    "augment": TABS + LEFT + MIDDLE + RIGHT + [("l", 1203, 620, 1544, 1049)],
    "disenchant": TABS + LEFT + MIDDLE + RIGHT,
    "rune": TABS + LEFT + MIDDLE + toggles((990, 1050), (916, 976))
            # The second list's filter (1767,697..1980,727) is in the dump but hidden on this tab, and
            # painting it put an empty plate under "Select Enchantment to overwrite." - left out.
            + [("l", 1553, 731, 1980, 1100), ("l", 1553, 367, 1980, 691)],
    "upgrade": TABS + [("l", 767, 345, 1193, 1051)] + MIDDLE + RIGHT + [("d", 1331, 955, 1416, 1040)],
}

def px(x, y):
    return M + (x - PX0) * K, M + (PY1 - y) * K

def guide(rects):
    im = Image.new("RGB", (1120 + 2 * M, 700 + 2 * M), (255, 255, 255))
    d = ImageDraw.Draw(im)
    d.rectangle([M, M, M + 1120, M + 700], fill=EDGE)
    d.rectangle([M + 22, M + 22, M + 1098, M + 678], fill=BG)
    for kind, x0, y0, x1, y1 in rects:
        (a, b), (c, e) = px(x0, y1), px(x1, y0)
        d.rectangle([a, b, c, e], fill=DARK if kind == "d" else LIGHT)
    return im

def sheet(g, style):
    s = style.convert("RGBA")
    s = s.resize((round(s.width * g.height / s.height), g.height))
    out = Image.new("RGB", (g.width + 40 + s.width, g.height), (255, 255, 255))
    out.paste(g, (0, 0))
    out.paste(s, (g.width + 40, 0), s)
    return out

if __name__ == "__main__":
    style = Image.open("../../src/Assets/board_enchant.png")
    for tab, rects in LAYOUTS.items():
        g = guide(rects)
        g.save(f"grok/guide_enchant_{tab}.png")
        sheet(g, style).save(f"grok/sheet_enchant_{tab}.png")
        print("wrote", tab)
