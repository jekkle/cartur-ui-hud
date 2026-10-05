"""4x-UltraSharp through ComfyUI (venv on :8188), then down to --scale of the source with Lanczos.

    python tools/art/upscale.py --scale 2 in.png out.png [in2.png out2.png ...]
"""
import argparse, json, time, urllib.request, uuid
from io import BytesIO
from PIL import Image

URL = "http://127.0.0.1:8188"

def post(path, data, ctype):
    req = urllib.request.Request(URL + path, data=data, headers={"Content-Type": ctype})
    return json.load(urllib.request.urlopen(req))

def upload(path):
    b = uuid.uuid4().hex
    body = (f"--{b}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"up_{b}.png\"\r\n"
            "Content-Type: image/png\r\n\r\n").encode() + open(path, "rb").read() + f"\r\n--{b}--\r\n".encode()
    return post("/upload/image", body, f"multipart/form-data; boundary={b}")["name"]

def upscale(src, dst, scale):
    name = upload(src)
    wf = {"1": {"class_type": "LoadImage", "inputs": {"image": name}},
          "2": {"class_type": "UpscaleModelLoader", "inputs": {"model_name": "4x-UltraSharp.pth"}},
          "3": {"class_type": "ImageUpscaleWithModel", "inputs": {"upscale_model": ["2", 0], "image": ["1", 0]}},
          "4": {"class_type": "SaveImage", "inputs": {"images": ["3", 0], "filename_prefix": "upscale"}}}
    pid = post("/prompt", json.dumps({"prompt": wf}).encode(), "application/json")["prompt_id"]
    while True:
        h = json.load(urllib.request.urlopen(f"{URL}/history/{pid}"))
        if pid in h and h[pid].get("outputs"):
            img = h[pid]["outputs"]["4"]["images"][0]
            break
        time.sleep(1)
    raw = urllib.request.urlopen(f"{URL}/view?filename={img['filename']}&subfolder={img['subfolder']}&type=output").read()
    w, h0 = Image.open(src).size
    Image.open(BytesIO(raw)).convert("RGB").resize((w * scale, h0 * scale), Image.LANCZOS).save(dst)
    print(dst, (w * scale, h0 * scale))

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--scale", type=int, default=2)
    ap.add_argument("files", nargs="+")
    a = ap.parse_args()
    for s, d in zip(a.files[::2], a.files[1::2]):
        upscale(s, d, a.scale)
