"""One trainer clip (.webm with alpha) -> the frames of one loop of its movement, for the Unity
menu Tunnel/Video Silhouette/Bake Frames (which runs this and builds a Texture2DArray).

Finds the loop (a take of 2 s or less is used whole): of every stretch of 0.4-3 s that starts and ends on the same pose, the one that
moves the most (mean distance of its frames from its first) for the least seam; when both sides
of the body move in the clip, the loop must hold both (a march is left AND right). The strike /
peak frame is the one farthest from the clip's first pose (the person standing). Seam: the last
3 frames are blended into the frames just before the loop's start, so the wrap is seamless.

    python bake_frames.py CLIP.webm OUT.png [--cycle A B] [--peak T] [--mirror] [--strikes N]

Writes OUT.png, a flipbook of the loop's frames (left to right, top to bottom, `columns` across;
Unity imports it as a Texture2DArray), and prints one JSON line: fps, frames, columns, rows,
cycleStart/cycleEnd/peakTime (source seconds), peakFrame, strikes, amplitude, seam.
"""
import argparse
import json

import numpy as np
from PIL import Image

from silhouette_common import decode, probe

MIN_CYCLE, MAX_CYCLE = 0.4, 3.0     # seconds
SEAM_WEIGHT = 2.0
SEAM_FRAMES = 3
WHOLE_CLIP = 2.0      # seconds: a take this short is used whole


def distances(a, b):
    """Mean |a_i - b_j| for every pair: [len(a), len(b)]."""
    fa, fb = a.reshape(len(a), -1), b.reshape(len(b), -1)
    return np.stack([np.abs(fb - x).mean(axis=1) for x in fa])


def find_cycle(alpha, fps):
    """(start, end) frame indices of the loop, end exclusive, and its score terms."""
    t = len(alpha)
    half = alpha.shape[2] // 2
    d = distances(alpha, alpha)
    dl = distances(alpha[..., :half], alpha[..., :half])
    dr = distances(alpha[..., half:], alpha[..., half:])
    glob_l, glob_r = dl[:, 0].max(), dr[:, 0].max()
    both = min(glob_l, glob_r) > 0.5 * max(glob_l, glob_r)
    pmin, pmax = max(2, round(MIN_CYCLE * fps)), round(MAX_CYCLE * fps)
    best, best_score = (0, t), -np.inf
    for s in range(t):
        for e in range(s + pmin, min(t - 1, s + pmax) + 1):
            if both:
                if dl[s:e, s].max() < 0.5 * glob_l or dr[s:e, s].max() < 0.5 * glob_r:
                    continue
            score = d[s:e, s].mean() - SEAM_WEIGHT * d[s, e]
            if score > best_score:
                best_score, best = score, (s, e)
    s, e = best
    return s, e, float(d[s:e, s].mean()), float(d[s, e]), dl, dr


def alternating(dl, dr, s, e):
    """Left and right take turns (a march, punches L/R) rather than moving together."""
    al, ar = dl[s:e, s], dr[s:e, s]
    if al.std() < 1e-6 or ar.std() < 1e-6:
        return False
    return np.corrcoef(al, ar)[0, 1] < 0.3


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("clip")
    ap.add_argument("out")
    ap.add_argument("--width", type=int, default=320)
    ap.add_argument("--height", type=int, default=512)
    ap.add_argument("--zoom", type=float, default=1.0, help="1 = the video's full height")
    ap.add_argument("--cx", type=float, default=0.5)
    ap.add_argument("--cy", type=float, default=0.5)
    ap.add_argument("--cycle", nargs=2, type=float, metavar=("A", "B"), help="the loop, source seconds")
    ap.add_argument("--peak", type=float, help="the strike, source seconds")
    ap.add_argument("--mirror", action="store_true", help="loop + its mirror image: one side, then the other")
    ap.add_argument("--strikes", type=int, help="strikes (beats) in one loop")
    args = ap.parse_args()

    vw, vh, fps = probe(args.clip)
    # The figure, at the analysis size: the whole frame (arms out of the panel still count).
    small = decode(args.clip, vf="scale=128:72", size=(128, 72), alpha=True)[..., 3].astype(np.float32) / 255.0
    n = len(small)
    if not args.cycle and n <= WHOLE_CLIP * fps:
        args.cycle = (0.0, n / fps)      # a short take is one repeat already: all of it
    if args.cycle:
        s = min(n - 1, max(0, round(args.cycle[0] * fps)))
        e = min(n, max(s + 1, round(args.cycle[1] * fps)))
        d = distances(small, small)
        half = small.shape[2] // 2
        dl = distances(small[..., :half], small[..., :half])
        dr = distances(small[..., half:], small[..., half:])
        amp, seam = float(d[s:e, s].mean()), float(d[s, min(e, n - 1)])
    else:
        s, e, amp, seam, dl, dr = find_cycle(small, fps)

    rest = small[0]
    if args.peak is not None:
        peak = min(e - 1, max(s, round(args.peak * fps)))
    else:
        dev = np.abs(small[s:e] - rest).reshape(e - s, -1).mean(axis=1)
        peak = s + int(np.argmax(dev))

    # The panel's crop of the frames, at the bake size.
    zoom = max(1.0, args.zoom)
    ch = vh / zoom
    cw = min(vw, ch * args.width / args.height)
    x = min(max(0.0, args.cx * vw - cw / 2), vw - cw)
    y = min(max(0.0, (1 - args.cy) * vh - ch / 2), vh - ch)
    vf = f"crop={cw:.0f}:{ch:.0f}:{x:.0f}:{y:.0f},scale={args.width}:{args.height}:flags=area"
    full = decode(args.clip, vf=vf, size=(args.width, args.height), alpha=True).astype(np.float32)

    def loop(flip):
        f = full[s:e].copy()
        pre = full[max(0, s - SEAM_FRAMES):s]
        if flip:
            f = f[:, :, ::-1]
        # The end of this half runs into what comes next: the unflipped start (or, for the first
        # half of a mirrored pair, the flipped one) - reached through the frames before it.
        nxt = pre[:, :, ::-1] if (args.mirror and not flip) else pre
        k = len(nxt)
        for j in range(k):
            w = (j + 1) / (k + 1)
            i = len(f) - k + j
            if i >= 0:
                f[i] = premul_lerp(f[i], nxt[j], w)
        return f

    frames = np.concatenate([loop(False), loop(True)]) if args.mirror else loop(False)
    strikes = args.strikes or (2 if args.mirror or alternating(dl, dr, s, e) else 1)
    frames[..., :3] = np.where(frames[..., 3:] > 0, frames[..., :3], 0)
    out = frames.round().clip(0, 255).astype(np.uint8)
    count = len(out)
    cols = int(np.ceil(np.sqrt(count * args.height / args.width)))
    rows = int(np.ceil(count / cols))
    sheet = np.zeros((rows * args.height, cols * args.width, 4), np.uint8)
    for i, f in enumerate(out):
        r, c = divmod(i, cols)
        sheet[r * args.height:(r + 1) * args.height, c * args.width:(c + 1) * args.width] = f
    Image.fromarray(sheet, "RGBA").save(args.out, optimize=True)
    print(json.dumps({
        "fps": fps, "frames": count, "columns": cols, "rows": rows, "width": args.width, "height": args.height,
        "cycleStart": s / fps, "cycleEnd": e / fps, "peakTime": peak / fps, "peakFrame": peak - s,
        "strikes": strikes, "amplitude": round(amp, 4), "seam": round(seam, 4), "sourceFrames": n,
    }))


def premul_lerp(a, b, w):
    """Blend two straight-alpha RGBA frames without dark fringes."""
    pa = a[..., :3] * a[..., 3:] / 255.0
    pb = b[..., :3] * b[..., 3:] / 255.0
    al = a[..., 3:] * (1 - w) + b[..., 3:] * w
    p = pa * (1 - w) + pb * w
    rgb = np.where(al > 0, p * 255.0 / np.maximum(al, 1e-3), 0)
    return np.concatenate([rgb, al], axis=-1)


if __name__ == "__main__":
    main()
