"""Blanks Cartur's concept board: empty slots, empty alcove, no text.

    python tools/art/blank.py

Two inpaint passes because the two jobs want different prompts - a slot wants an empty
carved recess, an alcove wants empty stone and mist. Masks are rectangles on measured
coordinates, verified by cropping those regions and looking at them first, and each is
inset so the carved bezel around every slot survives and only the contents are replaced.
"""
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(HERE, "out", "blank")
SRC = r"C:\Users\Tool1\.claude\uploads\603ce7e7-6dff-4749-8f0d-4aa2295ccee8\c71450b5-image.png"
sys.path.insert(0, HERE)
import gen  # noqa: E402

W, H = 1536, 1024
INSET = 11

SLOT_PROMPT = ("empty slot, dark hollow recess in carved oak, plain shadowed interior, "
               "nothing inside, no object, no icon, no text, no number")
ALCOVE_PROMPT = ("empty stone alcove, dark interior, faint mist, distant dim rock wall, "
                 "no person, no figure, no character, no armour, no weapon")
NEG = "person, figure, character, armour, weapon, item, icon, tool, text, letters, numbers, runes, logo, watermark"


def cells(x0, x1, y0, y1, n, vertical):
    out = []
    for i in range(n):
        if vertical:
            a = y0 + (y1 - y0) * i / n
            b = y0 + (y1 - y0) * (i + 1) / n
            out.append((x0 + INSET, a + INSET, x1 - INSET, b - INSET))
        else:
            a = x0 + (x1 - x0) * i / n
            b = x0 + (x1 - x0) * (i + 1) / n
            out.append((a + INSET, y0 + INSET, b - INSET, y1 - INSET))
    return out


def mask_slots():
    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)
    for r in cells(186, 822, 248, 332, 8, False):        # hotbar row
        d.rectangle(r, fill=255)
    for r in cells(862, 962, 262, 738, 4, True):         # equipment, left column
        d.rectangle(r, fill=255)
    for r in cells(1302, 1402, 262, 792, 4, True):       # equipment, right column
        d.rectangle(r, fill=255)
    for r in cells(862, 1392, 872, 932, 4, False):       # stat strip
        d.rectangle(r, fill=255)
    d.rectangle((128, 706, 238, 800), fill=255)          # the plaque's lettering
    return m


def mask_alcove():
    m = Image.new("L", (W, H), 0)
    ImageDraw.Draw(m).rectangle((972, 262, 1292, 866), fill=255)
    return m


def workflow(image_name, mask_name, positive, seed):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": gen.CKPT}},
        "2": {"class_type": "LoadImage", "inputs": {"image": image_name}},
        "3": {"class_type": "LoadImageMask", "inputs": {"image": mask_name, "channel": "red"}},
        "4": {"class_type": "VAEEncodeForInpaint", "inputs": {
            "pixels": ["2", 0], "vae": ["1", 2], "mask": ["3", 0], "grow_mask_by": 8}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"text": positive, "clip": ["1", 1]}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": NEG, "clip": ["1", 1]}},
        "7": {"class_type": "KSampler", "inputs": {
            "model": ["1", 0], "seed": seed, "steps": 30, "cfg": 6.0,
            "sampler_name": "dpmpp_2m", "scheduler": "karras",
            "positive": ["5", 0], "negative": ["6", 0], "latent_image": ["4", 0], "denoise": 1.0}},
        "18": {"class_type": "VAEDecode", "inputs": {"samples": ["7", 0], "vae": ["1", 2]}},
        "19": {"class_type": "SaveImage", "inputs": {"images": ["18", 0], "filename_prefix": f"cartur/blank_{seed}"}},
    }


def main():
    if not gen.server_up():
        sys.exit("ComfyUI is not answering on " + gen.SERVER)
    os.makedirs(OUT, exist_ok=True)

    board = Image.open(SRC).convert("RGB").resize((W, H), Image.LANCZOS)
    step0 = os.path.join(OUT, "step0.png")
    board.save(step0)

    for i, (mk, prompt, tag) in enumerate(((mask_slots(), SLOT_PROMPT, "slots"),
                                           (mask_alcove(), ALCOVE_PROMPT, "alcove"))):
        mp = os.path.join(OUT, f"mask_{tag}.png")
        mk.convert("RGB").save(mp)
        img_name = gen.upload_image(step0)
        mask_name = gen.upload_image(mp)
        info = gen.queue_and_wait(workflow(img_name, mask_name, prompt, 3000 + i))
        step0 = os.path.join(OUT, f"step{i + 1}_{tag}.png")
        gen.fetch_image(info, step0)
        print("pass %s -> %s" % (tag, step0))

    Image.open(step0).save(os.path.join(ROOT, "..", "..", "concept_blank.png"))
    print("wrote concept_blank.png")


if __name__ == "__main__":
    main()
