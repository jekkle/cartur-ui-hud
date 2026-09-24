"""Renders the base wood, keyed to the food and power diamonds.

    python tools/art/wood.py [--count N] [--seed S]

Cartur's call: the wood is whatever the food and power diamond art is made of, and it has
to be real rendered wood rather than a generated pattern. So the material comes from
ref_diamond through the IP-Adapter - his own art driving it, not an adjective in a prompt
- and every render is colour-keyed afterwards to food_frame.png's measured wood, mean
51.9,45.5,40.4, so no seed can drift the family.

Nothing is installed. This writes a contact sheet next to the real frame art for Cartur
to pick from, which is the standing rule for anything generated.
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(HERE, "out", "wood")
sys.path.insert(0, HERE)
import gen  # noqa: E402

SIZE = 1024
TARGET = np.array([51.9, 45.5, 40.4])       # food_frame.png, opaque mean

POSITIVE = ("photograph of an aged oak board, fine straight grain running across, "
            "worn matte surface, faint tool marks, viking hall timber, "
            "flat even top-down light, no shadows, no perspective, full frame texture")
NEGATIVE = ("shine, gloss, varnish, reflection, vignette, perspective, angle, edges, border, "
            "frame, plank gaps, seams, text, watermark, logo, carving, ornament, metal, people")


def workflow(seed, ref_name):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": gen.CKPT}},
        "2": {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["1", 0], "preset": "PLUS (high strength)"}},
        "10": {"class_type": "LoadImage", "inputs": {"image": ref_name}},
        "3": {"class_type": "IPAdapterAdvanced", "inputs": {
            "model": ["2", 0], "ipadapter": ["2", 1], "image": ["10", 0],
            "weight": 0.55, "weight_type": "style transfer", "combine_embeds": "average",
            "start_at": 0.0, "end_at": 1.0, "embeds_scaling": "V only"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": POSITIVE, "clip": ["1", 1]}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["1", 1]}},
        "9": {"class_type": "EmptyLatentImage", "inputs": {"width": SIZE, "height": SIZE, "batch_size": 1}},
        "17": {"class_type": "KSampler", "inputs": {
            "model": ["3", 0], "seed": seed, "steps": 32, "cfg": 5.5,
            "sampler_name": "dpmpp_2m", "scheduler": "karras",
            "positive": ["4", 0], "negative": ["5", 0], "latent_image": ["9", 0], "denoise": 1.0}},
        "18": {"class_type": "VAEDecode", "inputs": {"samples": ["17", 0], "vae": ["1", 2]}},
        "19": {"class_type": "SaveImage", "inputs": {"images": ["18", 0], "filename_prefix": f"cartur/wood_{seed}"}},
    }


def key_to_diamond(img):
    """Move a render onto food_frame's wood without flattening its grain."""
    a = np.asarray(img.convert("RGB")).astype(np.float64)
    lum = a.mean(axis=2)
    lo, hi = np.percentile(lum, 2), np.percentile(lum, 98)
    v = np.clip((lum - lo) / max(hi - lo, 1e-6), 0, 1)

    ref = np.asarray(Image.open(os.path.join(ASSETS, "food_frame.png")).convert("RGBA")).astype(np.float64)
    op = ref[..., 3] >= 250
    rlum = ref[..., :3].mean(axis=2)[op]
    rlo, rhi = np.percentile(rlum, 2), np.percentile(rlum, 98)

    out = (rlo + v * (rhi - rlo))[..., None] * (TARGET / TARGET.mean())[None, None, :]
    out *= TARGET / out.reshape(-1, 3).mean(axis=0)
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB")


def main():
    count = int(sys.argv[sys.argv.index("--count") + 1]) if "--count" in sys.argv else 3
    seed0 = int(sys.argv[sys.argv.index("--seed") + 1]) if "--seed" in sys.argv else 1000
    if not gen.server_up():
        sys.exit("ComfyUI is not answering on " + gen.SERVER)
    os.makedirs(OUT, exist_ok=True)
    ref_name = gen.upload_image(os.path.join(HERE, "refs", "ref_diamond.png"))
    for i in range(count):
        seed = seed0 + i
        info = gen.queue_and_wait(workflow(seed, ref_name))
        raw = os.path.join(OUT, f"raw_{seed}.png")
        gen.fetch_image(info, raw)
        keyed = key_to_diamond(Image.open(raw))
        keyed.save(os.path.join(OUT, f"wood_{seed}.png"))
        a = np.asarray(keyed).astype(float)
        print("seed %d keyed mean %.1f %.1f %.1f (target %.1f %.1f %.1f)"
              % (seed, a[..., 0].mean(), a[..., 1].mean(), a[..., 2].mean(), *TARGET))


if __name__ == "__main__":
    main()
