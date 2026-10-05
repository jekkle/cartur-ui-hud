"""Installs Cartur's 2026-10-03 panel boards: background cut from white, nothing else.

    python tools/art/boards.py

His rule for these: the art is not edited. The only change is that the white AROUND the frame
becomes transparent, and the file is cropped to the art's own bounding box.

He re-rendered the set on white after the black versions would not cut cleanly - a raven's
chest measured under brightness 8, the same as the black around it, so no rule could find the
bird's outline (memory: concept-art-background). On white there is no such ambiguity: the art
is dark, the background is near 255, and nothing inside the art is white. So:

- Background is the light pixels CONNECTED TO THE IMAGE EDGE. A flood from the edge cannot
  reach the art's interior, because it would have to cross dark art to get there - so glowing
  runes and eyes, however bright, stay put.
- The edge is antialiased honestly. A pixel on the outline is art mixed with white:
  c = a*F + (1-a)*255. Its alpha is how far it sits from white against a solid art pixel, and
  its colour is unmixed back to F, so the rim keeps the art's real colour with no white fringe.

Writes src/Assets/board_<name>.png and tools/art/out/boards_check.png (each over mid grey).
"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(HERE, "refs", "boards_2026-10-03_white")
ASSETS = os.path.join(ROOT, "src", "Assets")
NAMES = ["inventory", "hotbar", "equipment", "crafting", "wide", "tall", "small", "banner", "button",
         "mainmenu", "charselect", "world", "serverlist", "eula", "newworld", "modifiers", "namepanel", "custom",
         # 2026-10-04: Grok edits of inventory/wide/small/tall with the rails at half thickness, his picks.
         "chest", "thinwide", "thinsmall", "thintall",
         # 2026-10-04: a chest board per real container size - ChatGPT art from his chest board, the three
         # it miscounted rebuilt from its own cells, all upscaled 4x (4x-UltraSharp, ComfyUI).
         "chest_3x2", "chest_4x2", "chest_5x2", "chest_5x3", "chest_6x2", "chest_6x3", "chest_6x4",
         "chest_8x3", "chest_8x4", "chest_8x5", "chest_5x10",   # the last three are Grok renders, saved as PNG
         # 2026-10-05: Epic Loot's enchanting table, one board per tab layout - Grok renders from
         # enchant_guides.py, upscaled 2x (upscale.py). Picks: sacrifice 1, convert 2, enchant 2,
         # augment 3, disenchant 2, rune 2 (re-rendered without the hidden filter field), upgrade 1.
         "enchant_sacrifice", "enchant_convert", "enchant_enchant", "enchant_augment",
         "enchant_disenchant", "enchant_rune", "enchant_upgrade",
         # 2026-10-05: the build menu (BuildUIV2), Grok render from build_guide.py, option 2, 2x.
         "build"]

# Light enough to be background, if it is joined to the edge. Measured: every border is >= 252.
BG_LUM = 235
# The edge band, in px from the background, where alpha is solved instead of being 1.
EDGE_PX = 3
# Brightness of solid art at the rim, for the coverage solve. The frames are dark bronze and
# wood; a rim pixel at this brightness or darker is fully covered.
ART_LUM = 90.0


def cut(path):
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    lum = rgb.mean(axis=2)

    light = lum > BG_LUM
    labels, _ = ndimage.label(light)
    edge_ids = np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))
    background = np.isin(labels, edge_ids[edge_ids > 0])
    art = ~background

    # Rim band: art pixels within EDGE_PX of the background.
    near = ndimage.binary_dilation(background, iterations=EDGE_PX)
    band = art & near

    alpha = art.astype(np.float32)
    cover = np.clip((255.0 - lum) / (255.0 - ART_LUM), 0.0, 1.0)
    alpha[band] = cover[band]

    # Unmix from white where partially covered: F = (c - (1-a)*255) / a.
    a = np.maximum(alpha, 1e-3)[..., None]
    unmixed = np.clip((rgb - (1.0 - a) * 255.0) / a, 0, 255)
    out_rgb = np.where(band[..., None], unmixed, rgb)

    out = np.dstack([out_rgb, alpha * 255.0]).round().astype(np.uint8)
    img = Image.fromarray(out, "RGBA")
    ys, xs = np.where(alpha > 0)
    box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
    return img.crop(box), box, background.mean()


def main():
    checks = []
    for name in NAMES:
        path = os.path.join(SRC, name + ".jpg")
        if not os.path.exists(path):
            path = os.path.join(SRC, name + ".png")
        out, box, bg = cut(path)
        out.save(os.path.join(ASSETS, "board_" + name + ".png"))
        print("%-10s %4dx%-4d  crop origin (%d, %d)  background %.1f%% of file"
              % (name, out.width, out.height, box[0], box[1], 100 * bg))
        grey = Image.new("RGBA", out.size, (128, 128, 128, 255))
        grey.alpha_composite(out)
        checks.append(grey.convert("RGB"))
    h = 600
    row = [c.resize((int(c.width * h / c.height), h)) for c in checks]
    sheet = Image.new("RGB", (sum(r.width for r in row) + 20 * (len(row) - 1), h), (128, 128, 128))
    x = 0
    for r in row:
        sheet.paste(r, (x, 0))
        x += r.width + 20
    os.makedirs(os.path.join(HERE, "out"), exist_ok=True)
    sheet.save(os.path.join(HERE, "out", "boards_check.png"))


if __name__ == "__main__":
    main()
