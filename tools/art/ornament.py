"""Renders the carved pieces: a repeating edge run and a corner boss.

    python tools/art/ornament.py [--seed S]

Driven by ref_knot and ref_diamond through the IP-Adapter, so the carving language and the
material both come from Cartur's own art rather than from adjectives. Everything is
colour-keyed afterwards to food_frame.png's wood, mean 51.9,45.5,40.4, so the carvings land
in the same family as the bars and the diamonds.

The edge is rendered wide and shallow because that is how it is used: a 9-slice's edge
slice is drawn Image.Type.Tiled, so the motif repeats at native size along a panel of any
width instead of stretching. The corner is rendered square because a 9-slice corner is never
stretched at all, which is why the heavy carving belongs there.
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Assets")
OUT = os.path.join(HERE, "out", "ornament")
sys.path.insert(0, HERE)
import gen  # noqa: E402
import wood  # noqa: E402

PIECES = {
    "edge": {
        "w": 1024, "h": 256, "ip": 0.7,
        "positive": ("carved oak rail, one continuous band of norse knotwork relief running left to "
                     "right, deep chisel cuts, interlaced strands, aged timber, flat even top-down "
                     "light, straight on, full width, repeating pattern"),
    },
    "corner": {
        "w": 768, "h": 768, "ip": 0.7,
        "positive": ("carved oak corner boss, norse knotwork relief turning a right angle, deep "
                     "chisel cuts, interlaced strands, aged timber, flat even top-down light, "
                     "straight on"),
    },
}
NEGATIVE = ("gloss, varnish, shine, reflection, vignette, perspective, tilt, photo frame, picture, "
            "text, watermark, logo, metal, gold, paint, colour, people, background, scene")


def workflow(spec, seed, refs):
    knot, diamond = refs
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": gen.CKPT}},
        "2": {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["1", 0], "preset": "PLUS (high strength)"}},
        "10": {"class_type": "LoadImage", "inputs": {"image": knot}},
        "11": {"class_type": "LoadImage", "inputs": {"image": diamond}},
        "12": {"class_type": "ImageBatch", "inputs": {"image1": ["10", 0], "image2": ["11", 0]}},
        "3": {"class_type": "IPAdapterAdvanced", "inputs": {
            "model": ["2", 0], "ipadapter": ["2", 1], "image": ["12", 0],
            "weight": spec["ip"], "weight_type": "style transfer", "combine_embeds": "average",
            "start_at": 0.0, "end_at": 1.0, "embeds_scaling": "V only"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": spec["positive"], "clip": ["1", 1]}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["1", 1]}},
        "9": {"class_type": "EmptyLatentImage", "inputs": {"width": spec["w"], "height": spec["h"], "batch_size": 1}},
        "17": {"class_type": "KSampler", "inputs": {
            "model": ["3", 0], "seed": seed, "steps": 34, "cfg": 5.5,
            "sampler_name": "dpmpp_2m", "scheduler": "karras",
            "positive": ["4", 0], "negative": ["5", 0], "latent_image": ["9", 0], "denoise": 1.0}},
        "18": {"class_type": "VAEDecode", "inputs": {"samples": ["17", 0], "vae": ["1", 2]}},
        "19": {"class_type": "SaveImage", "inputs": {"images": ["18", 0], "filename_prefix": f"cartur/orn_{seed}"}},
    }


def main():
    seed = int(sys.argv[sys.argv.index("--seed") + 1]) if "--seed" in sys.argv else 2000
    if not gen.server_up():
        sys.exit("ComfyUI is not answering on " + gen.SERVER)
    os.makedirs(OUT, exist_ok=True)
    knot = gen.upload_image(os.path.join(HERE, "refs", "ref_knot.png"))
    diamond = gen.upload_image(os.path.join(HERE, "refs", "ref_diamond.png"))
    for name, spec in PIECES.items():
        for i in range(2):
            s = seed + i
            info = gen.queue_and_wait(workflow(spec, s, (knot, diamond)))
            raw = os.path.join(OUT, f"{name}_{s}_raw.png")
            gen.fetch_image(info, raw)
            keyed = wood.key_to_diamond(Image.open(raw))
            keyed.save(os.path.join(OUT, f"{name}_{s}.png"))
            a = np.asarray(keyed).astype(float)
            print("%s seed %d -> mean %.1f %.1f %.1f" % (name, s, *[a[..., k].mean() for k in range(3)]))
        seed += 10


if __name__ == "__main__":
    main()
