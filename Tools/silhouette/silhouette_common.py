"""Shared helpers for the trainer-silhouette tools: ffmpeg decode/encode of RGBA frames."""
import subprocess

import imageio_ffmpeg
import numpy as np

FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()


def probe(path):
    """(width, height, fps) of the first video stream."""
    reader = imageio_ffmpeg.read_frames(path)
    meta = next(reader)
    reader.close()
    w, h = meta["size"]
    return w, h, float(meta["fps"])


def decode(path, start=None, end=None, vf=None, size=None, alpha=False):
    """All frames of `path` (between start and end seconds) as uint8 [n, h, w, 4 or 3].

    alpha: the source is VP8/VP9 with an alpha channel - decoded with libvpx, which keeps it.
    vf: an ffmpeg filter chain applied before the frames are read; `size` is its output (w, h).
    """
    cmd = [FFMPEG, "-v", "error"]
    if alpha:
        cmd += ["-c:v", "libvpx"]
    if start is not None:
        cmd += ["-ss", f"{start:.4f}"]
    cmd += ["-i", path]
    if end is not None:
        cmd += ["-t", f"{end - (start or 0.0):.4f}"]
    chain = [vf] if vf else []
    chain.append("format=rgba" if alpha else "format=rgb24")
    cmd += ["-vf", ",".join(chain), "-an", "-f", "rawvideo", "-"]
    out = subprocess.run(cmd, capture_output=True, check=True).stdout
    if size is None:
        w, h, _ = probe(path)
    else:
        w, h = size
    ch = 4 if alpha else 3
    return np.frombuffer(out, np.uint8).reshape(-1, h, w, ch)


def encode_webm_alpha(frames, path, fps):
    """RGBA frames -> VP8 .webm with its alpha (what Unity and the bake both read)."""
    n, h, w, _ = frames.shape
    cmd = [FFMPEG, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", f"{w}x{h}",
           "-r", str(fps), "-i", "-", "-c:v", "libvpx", "-pix_fmt", "yuva420p", "-auto-alt-ref", "0",
           "-b:v", "4M", "-metadata:s:v:0", "alpha_mode=1", path]
    subprocess.run(cmd, input=np.ascontiguousarray(frames).tobytes(), check=True)
