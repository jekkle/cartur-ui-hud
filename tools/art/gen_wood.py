"""Generates the oak surface the panels are filled with.

    python tools/art/gen_wood.py [--seed N]

A texture is the one job diffusion is actually better at than drawing by hand - procedural
grain kept coming out as corduroy. The frames are still cut from bar_frame.png; only the
wood inside them comes from here.

Three things happen after the render, and all three matter:
  - seamless: the tile is offset by half and the cross seam is blended out, so a panel of
    any size can tile it without a visible join.
  - tone: the render is pushed to the same dark oak the rail's warm bronze sits against.
    Anything lighter and the bronze stops reading as bronze.
  - flat: strong highlights are pulled down, or every panel looks lit from the same angle
    no matter where it sits on screen.
"""
import json
import os
import sys
import time
import urllib.request

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
ASSETS = os.path.join(os.path.dirname(os.path.dirname(HERE)), "cartur-ui-hud", "src", "Assets")
SERVER = "http://127.0.0.1:8188"
CKPT = "RealVisXL_V5.0_fp16.safetensors"
SIZE = 1024
TARGET_MEAN = (58, 42, 27)     # measured off bar_frame.png's warm rail, taken to a surface tone

POSITIVE = ("one continuous sheet of dark oak, long unbroken straight grain running edge to "
            "edge, fine even grain lines, matte aged timber, evenly lit, flat orthographic "
            "texture, no objects, no text")
NEGATIVE = ("planks, plank seams, floorboards, parquet, tiles, brick pattern, joints, gaps, "
            "staggered boards, perspective, vignette, shadow, spotlight, gloss, varnish, shine, "
            "knots, text, watermark, frame, border, metal, people")


def workflow(seed):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CKPT}},
        "2": {"class_type": "CLIPTextEncode", "inputs": {"text": POSITIVE, "clip": ["1", 1]}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["1", 1]}},
        "4": {"class_type": "EmptyLatentImage", "inputs": {"width": SIZE, "height": SIZE, "batch_size": 1}},
        "5": {"class_type": "KSampler", "inputs": {
            "model": ["1", 0], "seed": seed, "steps": 28, "cfg": 5.0,
            "sampler_name": "dpmpp_2m", "scheduler": "karras",
            "positive": ["2", 0], "negative": ["3", 0], "latent_image": ["4", 0], "denoise": 1.0}},
        "6": {"class_type": "VAEDecode", "inputs": {"samples": ["5", 0], "vae": ["1", 2]}},
        "7": {"class_type": "SaveImage", "inputs": {"images": ["6", 0], "filename_prefix": "cartur/wood"}},
    }


def render(seed):
    body = json.dumps({"prompt": workflow(seed), "client_id": "cartur-wood"}).encode()
    req = urllib.request.Request(SERVER + "/prompt", data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req) as resp:
        prompt_id = json.load(resp)["prompt_id"]

    start = time.time()
    while time.time() - start < 600:
        with urllib.request.urlopen(SERVER + "/history/" + prompt_id) as resp:
            hist = json.load(resp)
        if prompt_id in hist:
            info = hist[prompt_id]["outputs"]["7"]["images"][0]
            q = urllib.parse.urlencode({"filename": info["filename"], "subfolder": info["subfolder"], "type": info["type"]})
            with urllib.request.urlopen(SERVER + "/view?" + q) as r:
                data = r.read()
            path = os.path.join(OUT, "wood_raw_%d.png" % seed)
            open(path, "wb").write(data)
            print("rendered in %.0fs -> %s" % (time.time() - start, path))
            return path
        time.sleep(2)
    sys.exit("timed out waiting for the render")


def seamless(im):
    """Offset by half, then blend the cross seam away with a feathered mirror of itself."""
    a = np.asarray(im).astype(np.float32)
    h, w = a.shape[:2]
    a = np.roll(np.roll(a, h // 2, axis=0), w // 2, axis=1)

    feather = max(8, w // 16)
    ramp = np.linspace(0, 1, feather * 2)[:, None, None]

    band = a[h // 2 - feather:h // 2 + feather].copy()
    a[h // 2 - feather:h // 2 + feather] = band * ramp + band[::-1] * (1 - ramp)

    ramp = ramp.transpose(1, 0, 2)
    band = a[:, w // 2 - feather:w // 2 + feather].copy()
    a[:, w // 2 - feather:w // 2 + feather] = band * ramp + band[:, ::-1] * (1 - ramp)
    return a


def grade(a):
    """Flatten the lighting, then put the whole thing on the target oak tone."""
    grey = a.mean(axis=2, keepdims=True)
    a = a + (grey.mean() - grey) * 0.75              # kill the across-image light gradient
    a = grey.mean() + (a - grey.mean()) * 0.85       # ease the contrast a shade

    for c in range(3):
        channel = a[..., c]
        spread = channel.std() if channel.std() > 1 else 1
        a[..., c] = TARGET_MEAN[c] + (channel - channel.mean()) * (9.0 / spread)
    return np.clip(a, 0, 255)


def main():
    seed = 4200
    if "--seed" in sys.argv:
        seed = int(sys.argv[sys.argv.index("--seed") + 1])
    os.makedirs(OUT, exist_ok=True)

    raw = Image.open(render(seed)).convert("RGB")
    out = Image.fromarray(grade(seamless(raw)).astype(np.uint8), "RGB")
    path = os.path.join(OUT, "wood.png")
    out.save(path)

    a = np.asarray(out).astype(np.float32)
    print("wood -> %s  mean %s  seam dx %.2f dy %.2f"
          % (path, a.reshape(-1, 3).mean(axis=0).round(1),
             np.abs(a[:, 0] - a[:, -1]).mean(), np.abs(a[0] - a[-1]).mean()))


if __name__ == "__main__":
    import urllib.parse
    main()
