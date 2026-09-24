"""Builds a seamless wood grain tile, keyed to Cartur's reference texture.

    python tools/art/grain.py            # -> tools/art/out/grain.png
    python tools/art/grain.py --install  # and copy into src/Assets/

Ours, not his reference image: that one is a stock photo and this mod is published.
It is also not tileable, and the whole point of the grain is to sit in the CENTRE of a
9-slice drawn Image.Type.Tiled, where a seam would repeat across the panel forever.

Measured off the reference and matched here: mean 36.2,29.8,28.6, hue about 10 degrees,
saturation 0.12, 5th to 95th percentile luminance 16 to 56. Fine grain running across,
low contrast, faint darker streaks rather than strong rings.

Seamless by construction. The noise is convolved with an anisotropic kernel through an
FFT, which wraps at the edges, and the banding is sums of sinusoids at whole-number
frequencies, which close on themselves. Nothing is blended or mirrored, so there is no
seam line and no repeated feature down the middle.
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(HERE, "out")

SIZE = 512
TARGET_MEAN = np.array([36.2, 29.8, 28.6])
TARGET_P5, TARGET_P95 = 16.0, 56.3
SEED = 7


def wrapped_blur(field, sigma_x, sigma_y):
    """Gaussian blur through the FFT, so it wraps - the tile stays seamless."""
    h, w = field.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    kernel = np.exp(-2 * (np.pi ** 2) * ((sigma_x ** 2) * fx ** 2 + (sigma_y ** 2) * fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(field) * kernel))


def build():
    rng = np.random.default_rng(SEED)
    y = np.arange(SIZE)[:, None] / SIZE
    x = np.arange(SIZE)[None, :] / SIZE

    # Grain: noise smeared along x so it reads as fibres running across, not blobs.
    fibre = wrapped_blur(rng.standard_normal((SIZE, SIZE)), sigma_x=26.0, sigma_y=0.7)
    fibre /= np.abs(fibre).max()

    # Growth banding: whole-number frequencies close on themselves, so the tile wraps.
    band = np.zeros((SIZE, SIZE))
    for freq, amp, phase in ((3, 0.55, 0.0), (7, 0.28, 1.1), (13, 0.16, 2.3), (23, 0.09, 0.6)):
        band += amp * np.sin(2 * np.pi * freq * y + phase + 1.6 * fibre)
    band /= np.abs(band).max()

    # A slow drift along the length so no two stretches of the tile look identical.
    drift = wrapped_blur(rng.standard_normal((SIZE, SIZE)), sigma_x=90.0, sigma_y=40.0)
    drift /= np.abs(drift).max()

    v = 0.55 * band + 0.30 * fibre + 0.15 * drift
    v = (v - v.min()) / (v.max() - v.min())

    # Match the reference's spread by its own percentiles, not by min/max: the raw field is
    # bunched round the middle, and a straight min/max stretch left it at 21..41 against the
    # reference's 16..56 - flat, which is the thing being fixed.
    lo, hi = np.percentile(v, 5), np.percentile(v, 95)
    lum = TARGET_P5 + (v - lo) * ((TARGET_P95 - TARGET_P5) / (hi - lo))
    rgb = lum[..., None] * (TARGET_MEAN / TARGET_MEAN.mean())[None, None, :]
    rgb *= TARGET_MEAN / rgb.reshape(-1, 3).mean(axis=0)

    out = np.clip(rgb, 0, 255).astype(np.uint8)
    img = Image.fromarray(np.dstack([out, np.full((SIZE, SIZE), 255, np.uint8)]), "RGBA")

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "grain.png")
    img.save(path)

    a = np.asarray(img).astype(float)[..., :3]

    # A seamless tile's wrap edge is not IDENTICAL, it is ordinary: putting the last row next to
    # the first should look like any other pair of neighbouring rows. So the test is the wrap
    # difference against the typical interior difference, and a ratio near 1 is seamless. Testing
    # for equality instead would fail every tile that has a gradient in it, which is all of them.
    wrap_x = np.abs(a[:, 0] - a[:, -1]).mean()
    step_x = np.abs(np.diff(a, axis=1)).mean()
    wrap_y = np.abs(a[0, :] - a[-1, :]).mean()
    step_y = np.abs(np.diff(a, axis=0)).mean()
    print("grain %dx%d  mean %.1f %.1f %.1f  p5 %.1f p95 %.1f"
          % (SIZE, SIZE, a[..., 0].mean(), a[..., 1].mean(), a[..., 2].mean(),
             np.percentile(a.mean(axis=2), 5), np.percentile(a.mean(axis=2), 95)))
    print("seam: across %.2f vs %.2f typical (x%.2f), down %.2f vs %.2f typical (x%.2f)"
          % (wrap_x, step_x, wrap_x / max(step_x, 1e-6), wrap_y, step_y, wrap_y / max(step_y, 1e-6)))
    if "--install" in sys.argv:
        import shutil
        shutil.copy2(path, os.path.join(ASSETS, "grain.png"))
        print("installed -> src/Assets/grain.png")


if __name__ == "__main__":
    build()
