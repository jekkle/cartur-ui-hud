"""One frame for all seven enchanting-table boards.

Grok drew the frame, medallions and tab column a little differently on every tab's board, so
switching tabs made the window jump up to 14 px sideways (both panel reviews, 2026-10-05). The
Sacrifice board is kept whole; every other board gives only its window interior - right of the
tab column, inside the frame rim - pasted into a copy of it at the same place, feathered so no
seam shows. Run after boards.py (which cuts src/Assets/board_enchant_*.png from refs/); it
overwrites those six files.

Interior in the pilot dump's screen px (panel 533.33..2026.67 x 253.33..1186.67, y up): x
762..1995, y 285..1155 - the left well starts at 767, the tab boxes end by 757.
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(os.path.dirname(os.path.dirname(HERE)), "src", "Assets")
# frame = knotwork rectangle in board px (x, y, w, h), as EpicBoards.EnchantTabs had them.
FRAMES = {
    "sacrifice": (4, 24, 1665, 1043), "convert": (1, 26, 1666, 1040), "enchant": (2, 26, 1664, 1045),
    "augment": (1, 26, 1666, 1042), "disenchant": (1, 26, 1666, 1039), "rune": (4, 27, 1666, 1040),
    "upgrade": (2, 26, 1665, 1040),
}
X0, X1, Y0, Y1 = 762.0, 1995.0, 285.0, 1155.0
FEATHER = 10
MEDAL_R = 70   # board px around a medallion centre left to the Sacrifice board


def box(frame):
    fx, fy, fw, fh = frame
    bx = lambda x: fx + (x - 533.33) / 1493.33 * fw
    by = lambda y: fy + (1186.67 - y) / 933.33 * fh
    return round(bx(X0)), round(by(Y1)), round(bx(X1)), round(by(Y0))


def main():
    master = Image.open(os.path.join(ASSETS, "board_enchant_sacrifice.png")).convert("RGBA")
    mx0, my0, mx1, my1 = box(FRAMES["sacrifice"])
    w, h = mx1 - mx0, my1 - my0
    # Feathered mask: 0 at the paste edge, 1 FEATHER px in.
    ramp_x = np.minimum(np.arange(w), np.arange(w)[::-1]) / FEATHER
    ramp_y = np.minimum(np.arange(h), np.arange(h)[::-1]) / FEATHER
    mask = np.clip(np.minimum(ramp_x[None, :], ramp_y[:, None]), 0, 1)
    # The top and bottom medallions reach into the interior; around each, the Sacrifice board
    # stays as it is, or the two boards' medallions blend into a ghost (first run, 2026-10-05).
    fx, fy, fw, fh = FRAMES["sacrifice"]
    yy, xx = np.mgrid[my0:my1, mx0:mx1]
    cx = fx + (1280.0 - 533.33) / 1493.33 * fw
    for cy in (fy, fy + fh):
        d = np.hypot(xx - cx, yy - cy)
        mask = np.minimum(mask, np.clip((d - MEDAL_R) / FEATHER, 0, 1))
    for tab, frame in FRAMES.items():
        if tab == "sacrifice":
            continue
        path = os.path.join(ASSETS, f"board_enchant_{tab}.png")
        src = Image.open(path).convert("RGBA")
        inner = src.crop(box(frame)).resize((w, h), Image.LANCZOS)
        out = np.asarray(master).astype(np.float32).copy()
        region = out[my0:my1, mx0:mx1]
        out[my0:my1, mx0:mx1] = region * (1 - mask[..., None]) + np.asarray(inner).astype(np.float32) * mask[..., None]
        Image.fromarray(out.round().astype(np.uint8), "RGBA").save(path)
        print(f"{tab}: interior {box(frame)} -> sacrifice frame {mx0},{my0},{mx1},{my1}")


if __name__ == "__main__":
    main()
