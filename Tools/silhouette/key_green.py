"""Green-screen clip -> trainer silhouette .webm in the look of Assets/Silhouette/Video.

The person is keyed out of the green, scaled and placed like the existing clips (standing height
STAND_HEIGHT px, feet at FEET_Y, centred), filled black with a cyan -> violet -> magenta neon
contour and a soft glow, and written as VP8 with alpha. The scale and place are measured on the
first frame (the person standing) and kept for the whole clip, so a jump still leaves the floor.

    python key_green.py SOURCE.mp4 Assets/Silhouette/Video/NAME.webm [--trim 0.25 4.2]
"""
import argparse

import numpy as np
from PIL import Image
from scipy import ndimage

from silhouette_common import decode, encode_webm_alpha, probe

OUT_W, OUT_H = 1280, 720
STAND_HEIGHT = 650      # px, head to sole, like the existing clips
FEET_Y = 694            # px, the soles
# Greenness (G - max(R, B)) of the backdrop is 25-45, of the person below 10.
KEY_LO, KEY_HI = 10.0, 24.0
NEON = np.array([[30, 220, 255], [140, 120, 255], [255, 60, 220]], np.float32)   # left, middle, right
NEON_HALF_WIDTH = 150   # px from the figure's centre to the pure end colours


def key(rgb):
    """Soft alpha of the person in one frame."""
    f = rgb.astype(np.float32)
    green = f[..., 1] - np.maximum(f[..., 0], f[..., 2])
    a = 1.0 - np.clip((green - KEY_LO) / (KEY_HI - KEY_LO), 0.0, 1.0)
    h, w = a.shape
    a[int(h * 0.88):, int(w * 0.84):] = 0.0          # the generator's watermark
    hard = a > 0.5
    lab, n = ndimage.label(hard)
    if n == 0:
        return np.zeros_like(a)
    areas = ndimage.sum(hard, lab, range(1, n + 1))
    main = int(np.argmax(areas)) + 1
    ys, xs = np.nonzero(lab == main)
    y0, y1, x0, x1 = ys.min() - 150, ys.max() + 150, xs.min() - 150, xs.max() + 150
    keep = np.zeros(n + 1, bool)
    for i, (sl, area) in enumerate(zip(ndimage.find_objects(lab), areas), start=1):
        cy, cx = (sl[0].start + sl[0].stop) / 2, (sl[1].start + sl[1].stop) / 2
        keep[i] = i == main or (area > 300 and y0 <= cy <= y1 and x0 <= cx <= x1)
    body = keep[lab]
    # Pin-holes inside the body (a glint on the clothes), not the real gaps (an arm on the hip).
    holes = ndimage.binary_fill_holes(body) & ~body
    hl, hn = ndimage.label(holes)
    if hn:
        small = np.concatenate([[False], ndimage.sum(holes, hl, range(1, hn + 1)) < 400])
        body |= small[hl]
    body = ndimage.binary_dilation(body, iterations=2)
    a = np.where(body, a, 0.0)
    a[ndimage.binary_erosion(body, iterations=3)] = 1.0
    return ndimage.gaussian_filter(a, 0.8)


def placement(alpha):
    """(scale, dx, dy) taking the standing figure to the clips' size and place."""
    rows = np.nonzero(alpha.max(axis=1) > 0.5)[0]
    cols = np.nonzero(alpha.max(axis=0) > 0.5)[0]
    top, bottom = rows.min(), rows.max()
    s = STAND_HEIGHT / max(1, bottom - top)
    cx = (alpha[rows.min():rows.max() + 1] > 0.5).nonzero()[1].mean() if cols.size else alpha.shape[1] / 2
    return s, OUT_W / 2 - cx * s, FEET_Y - bottom * s


def place(alpha, s, dx, dy):
    h, w = alpha.shape
    img = Image.fromarray(alpha.astype(np.float32), "F").resize((round(w * s), round(h * s)), Image.BILINEAR)
    big = np.asarray(img)
    out = np.zeros((OUT_H, OUT_W), np.float32)
    ox, oy = int(round(dx)), int(round(dy))
    sx0, sy0 = max(0, -ox), max(0, -oy)
    tx0, ty0 = max(0, ox), max(0, oy)
    cw = min(big.shape[1] - sx0, OUT_W - tx0)
    ch = min(big.shape[0] - sy0, OUT_H - ty0)
    if cw > 0 and ch > 0:
        out[ty0:ty0 + ch, tx0:tx0 + cw] = big[sy0:sy0 + ch, sx0:sx0 + cw]
    return np.clip(out, 0.0, 1.0)


def stylize(m):
    """Black fill, neon contour, glow -> straight-alpha RGBA uint8."""
    ero = ndimage.grey_erosion(m, size=5)
    dil = ndimage.grey_dilation(m, size=3)
    line = np.clip(dil - ero, 0.0, 1.0)
    glow = np.clip(ndimage.gaussian_filter(line, 6.0) * 1.6, 0.0, 0.85)
    t = np.clip((np.arange(OUT_W, dtype=np.float32) - OUT_W / 2) / NEON_HALF_WIDTH * 0.5 + 0.5, 0.0, 1.0)
    lo = np.clip(t * 2, 0, 1)[:, None]
    hi = np.clip(t * 2 - 1, 0, 1)[:, None]
    neon = np.where(t[:, None] < 0.5, NEON[0] + (NEON[1] - NEON[0]) * lo, NEON[1] + (NEON[2] - NEON[1]) * hi)
    neon = neon[None, :, :]
    # Premultiplied, bottom to top: glow, the black body, the contour.
    p = neon * glow[..., None]
    a = glow
    p = p * (1 - m[..., None])
    a = m + a * (1 - m)
    p = neon * line[..., None] + p * (1 - line[..., None])
    a = line + a * (1 - line)
    rgb = np.where(a[..., None] > 1e-4, p / np.maximum(a[..., None], 1e-4), 0.0)
    return np.concatenate([np.clip(rgb, 0, 255), a[..., None] * 255.0], axis=-1).round().astype(np.uint8)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("source")
    ap.add_argument("out")
    ap.add_argument("--trim", nargs=2, type=float, metavar=("START", "END"), help="seconds of the source kept")
    args = ap.parse_args()

    _, _, fps = probe(args.source)
    start, end = args.trim if args.trim else (None, None)
    frames = decode(args.source, start, end)
    alphas = [key(f) for f in frames]
    s, dx, dy = placement(alphas[0])
    out = np.stack([stylize(place(a, s, dx, dy)) for a in alphas])
    encode_webm_alpha(out, args.out, fps)
    print(f"{args.out}: {len(out)} frames at {fps:g} fps, scale {s:.3f}")


if __name__ == "__main__":
    main()
