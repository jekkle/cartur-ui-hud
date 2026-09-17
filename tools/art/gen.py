"""Generate one UI art piece with a local ComfyUI server, repeatably.

    python gen.py <piece> [--seed N] [--count K] [--dry-run]

piece is a key in pieces.json["pieces"]; its values override
pieces.json["defaults"]. --dry-run writes only the guide PNG and the workflow
JSON (no HTTP calls). --count K runs seeds seed..seed+K-1 sequentially.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
REFS = os.path.join(HERE, "refs")
SERVER = "http://127.0.0.1:8188"
CKPT = "RealVisXL_V5.0_fp16.safetensors"
CONTROLNET = "controlnet-canny-sdxl-1.0.fp16.safetensors"
REF_NAMES = ["ref_knot", "ref_rail", "ref_point", "ref_diamond"]
START_CMD = r'D:\Ai\ComfyUI\venv\Scripts\python.exe main.py --listen 127.0.0.1 --port 8188'
sys.path.insert(0, HERE)
import check_piece  # noqa: E402


def load_spec(piece):
    data = json.load(open(os.path.join(HERE, "pieces.json")))
    if piece not in data["pieces"]:
        sys.exit(f"unknown piece {piece!r}; choices: {list(data['pieces'])}")
    spec = dict(data["defaults"])
    spec.update(data["pieces"][piece])
    return spec


def render_size(spec):
    """SDXL falls apart under ~768px, so every piece renders with its long side at 1024
    (multiple of 64) and is downscaled after. Returns (W, H, scale)."""
    w, h = spec["size"]
    scale = 1024 / max(w, h)
    return int(round(w * scale / 64)) * 64, int(round(h * scale / 64)) * 64, scale


def draw_guide(spec, path):
    """Canny-style edge map at render size: outer rect, 9-slice band, bevel, corner bosses."""
    w, h, scale = render_size(spec)
    # a wide piece's band can be taller than half its height; cap it or the rect inverts
    border = int(min(spec["border"] * scale, min(w, h) * 0.4))
    im = Image.new("RGB", (w, h), "black")
    d = ImageDraw.Draw(im)
    if spec["guide"] == "strip":
        d.line([0, 4, w, 4], fill="white", width=3)
        d.line([0, h - 5, w, h - 5], fill="white", width=3)
        im.save(path); print(f"guide -> {path}"); return
    if spec["guide"] == "disc":
        r = int(min(w, h) * 0.42)
        d.ellipse([w // 2 - r, h // 2 - r, w // 2 + r, h // 2 + r], outline="white", width=4)
        im.save(path); print(f"guide -> {path}"); return
    # no outer bevel lines: they read as a wooden picture frame around the bronze
    d.rectangle([border, border, w - border - 1, h - border - 1], outline="white", width=3)
    d.rectangle([2, 2, w - 3, h - 3], outline="white", width=3)
    r = int(border * 0.28)
    for cx, cy in [(border, border), (w - border, border), (border, h - border), (w - border, h - border)]:
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline="white", width=3)
    im.save(path)
    print(f"guide -> {path}")


def upload_image(path):
    with open(path, "rb") as f:
        data = f.read()
    boundary = uuid.uuid4().hex
    name = os.path.basename(path)
    parts = []
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="image"; filename="{name}"\r\nContent-Type: image/png\r\n\r\n'.encode())
    parts.append(data)
    parts.append(b"\r\n")
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="overwrite"\r\n\r\ntrue\r\n--{boundary}--\r\n'.encode())
    body = b"".join(parts)
    req = urllib.request.Request(
        f"{SERVER}/upload/image", data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    with urllib.request.urlopen(req) as resp:
        return json.load(resp)["name"]


def build_workflow(piece, spec, seed, guide_name, ref_names, w, h):
    r0, r1, r2, r3 = ref_names
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CKPT}},
        "2": {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["1", 0], "preset": "PLUS (high strength)"}},
        "10": {"class_type": "LoadImage", "inputs": {"image": r0}},
        "11": {"class_type": "LoadImage", "inputs": {"image": r1}},
        "12": {"class_type": "LoadImage", "inputs": {"image": r2}},
        "13": {"class_type": "LoadImage", "inputs": {"image": r3}},
        "14": {"class_type": "ImageBatch", "inputs": {"image1": ["10", 0], "image2": ["11", 0]}},
        "15": {"class_type": "ImageBatch", "inputs": {"image1": ["14", 0], "image2": ["12", 0]}},
        "16": {"class_type": "ImageBatch", "inputs": {"image1": ["15", 0], "image2": ["13", 0]}},
        "3": {"class_type": "IPAdapterAdvanced", "inputs": {
            "model": ["2", 0], "ipadapter": ["2", 1], "image": ["16", 0],
            "weight": spec["ip_weight"], "weight_type": "style transfer",
            "combine_embeds": "average", "start_at": 0.0, "end_at": 1.0,
            "embeds_scaling": "V only",
        }},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": spec["positive"].format(desc=spec["desc"]), "clip": ["1", 1]}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"text": spec["negative"], "clip": ["1", 1]}},
        "6": {"class_type": "ControlNetLoader", "inputs": {"control_net_name": CONTROLNET}},
        "7": {"class_type": "LoadImage", "inputs": {"image": guide_name}},
        "8": {"class_type": "ControlNetApplyAdvanced", "inputs": {
            "positive": ["4", 0], "negative": ["5", 0], "control_net": ["6", 0], "image": ["7", 0],
            "strength": spec["cn_strength"], "start_percent": 0.0, "end_percent": spec["cn_end"], "vae": ["1", 2],
        }},
        "9": {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
        "17": {"class_type": "KSampler", "inputs": {
            "model": ["3", 0], "seed": seed, "steps": spec["steps"], "cfg": spec["cfg"],
            "sampler_name": "dpmpp_2m", "scheduler": "karras",
            "positive": ["8", 0], "negative": ["8", 1], "latent_image": ["9", 0], "denoise": 1.0,
        }},
        "18": {"class_type": "VAEDecode", "inputs": {"samples": ["17", 0], "vae": ["1", 2]}},
        "19": {"class_type": "SaveImage", "inputs": {"images": ["18", 0], "filename_prefix": f"cartur/{piece}_{seed}"}},
    }


def server_up():
    try:
        urllib.request.urlopen(f"{SERVER}/history", timeout=3)
        return True
    except (urllib.error.URLError, OSError):
        return False


def queue_and_wait(workflow):
    body = json.dumps({"prompt": workflow, "client_id": "cartur-art"}).encode()
    req = urllib.request.Request(f"{SERVER}/prompt", data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req) as resp:
        prompt_id = json.load(resp)["prompt_id"]
    start = time.time()
    while time.time() - start < 600:
        with urllib.request.urlopen(f"{SERVER}/history/{prompt_id}") as resp:
            hist = json.load(resp)
        if prompt_id in hist:
            elapsed = time.time() - start
            print(f"render done in {elapsed:.1f}s")
            return hist[prompt_id]["outputs"]["19"]["images"][0]
        time.sleep(2)
    sys.exit(f"timed out waiting on prompt {prompt_id} after 600s")


def fetch_image(info, dest):
    q = urllib.parse.urlencode({"filename": info["filename"], "subfolder": info["subfolder"], "type": info["type"]})
    with urllib.request.urlopen(f"{SERVER}/view?{q}") as resp:
        data = resp.read()
    with open(dest, "wb") as f:
        f.write(data)
    print(f"fetched -> {dest}")


def postprocess(raw_path, spec, dest):
    im = Image.open(raw_path).convert("RGBA")
    im = im.resize(tuple(spec["size"]), Image.LANCZOS)
    kind = spec.get("kind", "frame")
    if kind == "strip":
        # mirror the left half onto the right: the seam at both ends is then the same
        # pixel column, which is what makes the band tile end to end
        w, h = spec["size"]
        half = im.crop((0, 0, w // 2, h))
        im.paste(half.transpose(Image.FLIP_LEFT_RIGHT), (w // 2, 0))
        im.save(dest); print(f"post-processed -> {dest}"); return
    if kind == "disc":
        w, h = spec["size"]
        r = int(min(w, h) * 0.42)
        m = Image.new("L", (w, h), 0)
        ImageDraw.Draw(m).ellipse([w // 2 - r, h // 2 - r, w // 2 + r, h // 2 + r], fill=255)
        im.putalpha(m)
        im.save(dest); print(f"post-processed -> {dest}"); return
    # The 9-slice centre is stretched to any size, so whatever the model painted there
    # would smear. Replace it with the interior's own median tone plus faint grain,
    # feathered into the band so the rim keeps its bevel.
    w, h = spec["size"]
    b = spec["border"]
    px = np.asarray(im).astype(np.float32)
    inner = px[b:h - b, b:w - b, :3]
    tone = np.median(inner.reshape(-1, 3), axis=0)
    rng = np.random.default_rng(0)
    grain = rng.normal(0, 3, (h, w, 1)).astype(np.float32)
    flat = np.broadcast_to(tone, (h, w, 3)) + grain
    feather = max(4, b // 4)
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rectangle([b, b, w - b - 1, h - b - 1], fill=255)
    mask = np.asarray(mask.filter(ImageFilter.GaussianBlur(feather))).astype(np.float32)[..., None] / 255
    px[..., :3] = px[..., :3] * (1 - mask) + flat * mask
    im = Image.fromarray(np.clip(px, 0, 255).astype(np.uint8), "RGBA")
    # Diffusion never mirrors exactly, and a 9-slice shows every mismatch as a seam
    # where the frame meets the stretched middle. The top-left quadrant becomes the
    # whole piece: mirrored right, then down. Symmetry is then exact by construction.
    w, h = spec["size"]
    q = im.crop((0, 0, w // 2, h // 2))
    top = Image.new("RGBA", (w, h // 2))
    top.paste(q, (0, 0))
    top.paste(q.transpose(Image.FLIP_LEFT_RIGHT), (w // 2, 0))
    im = Image.new("RGBA", (w, h))
    im.paste(top, (0, 0))
    im.paste(top.transpose(Image.FLIP_TOP_BOTTOM), (0, h // 2))
    if spec.get("alpha") == "rounded":
        w, h = spec["size"]
        r = int(spec["border"] * 0.35)
        # opaque everywhere except outside the rounded rect corners; hard edge is fine
        corner = Image.new("L", (w, h), 0)
        cd = ImageDraw.Draw(corner)
        cd.rectangle([r, 0, w - r - 1, h - 1], fill=255)
        cd.rectangle([0, r, w - 1, h - r - 1], fill=255)
        for cx, cy in [(r, r), (w - r - 1, r), (r, h - r - 1), (w - r - 1, h - r - 1)]:
            cd.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
        alpha = np.asarray(im.split()[3]).astype(np.uint8)
        keep = np.asarray(corner)
        alpha = np.where(keep > 0, alpha, 0)
        im.putalpha(Image.fromarray(alpha, "L"))
    im.save(dest)
    print(f"post-processed -> {dest}")


def run_one(piece, spec, seed, dry_run):
    os.makedirs(OUT, exist_ok=True)
    w, h, _ = render_size(spec)
    guide_path = os.path.join(OUT, f"{piece}_guide.png")
    draw_guide(spec, guide_path)

    ref_names_local = [f"{n}.png" for n in REF_NAMES]
    workflow = build_workflow(piece, spec, seed, os.path.basename(guide_path), ref_names_local, w, h)
    wf_path = os.path.join(OUT, f"{piece}_{seed}.workflow.json")
    json.dump(workflow, open(wf_path, "w"), indent=2)
    print(f"workflow -> {wf_path}")
    if dry_run:
        return

    if not server_up():
        print(f"ComfyUI not reachable at {SERVER}. Start it with:\n  {START_CMD}\n  (cwd D:\\Ai\\ComfyUI)")
        sys.exit(1)

    guide_name = upload_image(guide_path)
    ref_names = [upload_image(os.path.join(REFS, f"{n}.png")) for n in REF_NAMES]
    workflow = build_workflow(piece, spec, seed, guide_name, ref_names, w, h)

    info = queue_and_wait(workflow)
    raw_path = os.path.join(OUT, f"{piece}_{seed}_raw.png")
    fetch_image(info, raw_path)

    final_seeded = os.path.join(OUT, f"{piece}_{seed}.png")
    postprocess(raw_path, spec, final_seeded)
    final_path = os.path.join(OUT, f"{piece}.png")
    Image.open(final_seeded).save(final_path)

    fails = check_piece.check(final_path)
    if fails:
        for f in fails:
            print(f"FAIL {final_path}: {f}")
    else:
        print(f"PASS {final_path}")


def main():
    args = sys.argv[1:]
    dry_run = "--dry-run" in args
    args = [a for a in args if a != "--dry-run"]
    if not args:
        sys.exit(__doc__)
    piece = args[0]
    spec = load_spec(piece)
    seed = spec.get("seed", 1000)
    count = 1
    i = 1
    while i < len(args):
        if args[i] == "--seed":
            seed = int(args[i + 1]); i += 2
        elif args[i] == "--count":
            count = int(args[i + 1]); i += 2
        else:
            sys.exit(f"unknown arg {args[i]}")
    for s in range(seed, seed + count):
        run_one(piece, spec, s, dry_run)


if __name__ == "__main__":
    main()
