"""Turns a folder of Suno tracks into retention-cut level tracks (video 28).

What the analytics said (90% of views on TV, 96% from "Up next", ~2 min watched whatever the length):
the viewer arrives on autoplay and leaves at the first dull moment. The level tracks with a quiet
breakdown in their first 30 s lost a third more viewers by 0:30 (track18_1: 47% vs track17_1: 71%).
So every level here:

  * starts ON the drop (the quiet intro before the first loud bar is cut);
  * has no long breakdown: a run of quiet bars longer than --max-quiet-bars keeps only its last
    --keep-buildup bars (the riser into the next drop), the rest is cut;
  * ends on its last loud bar plus --tail-bars.

All cuts sit on bar lines with a short crossfade, so the beat grid runs on unbroken and the beatmap
generator fits it as usual. The first --split tracks are cut in two at a phrase line near
--first-level seconds, so the world changes early (level 1 ~65 s, the second world before 1:15 of
the video) while the video keeps its length.

usage (from Tools/beatmap, the beatmap venv):
  .venv\\Scripts\\python prepare_levels.py "C:/Users/PC/Desktop/<5 tracks>" track28 --split 2
writes Assets/Audio/Tracks/track28_1.wav ... track28_7.wav and prints what was cut.
"""
import argparse, glob, os, sys
import numpy as np
import soundfile as sf
import librosa

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Audio", "Tracks"))


def analyse(y_mono, sr):
    """Beat grid, the bar lines (phase chosen where the kick lands), and each bar's bass level 0..1."""
    tempo, beats = librosa.beat.beat_track(y=y_mono, sr=sr, units="time")
    bpm = float(np.atleast_1d(tempo)[0])
    beat = 60.0 / bpm
    # an even grid from the tracker's beats (median phase), so bars have one exact length
    phase = float(np.median(np.mod(beats, beat))) if len(beats) else 0.0
    S = np.abs(librosa.stft(y_mono, n_fft=2048, hop_length=512))
    fr = librosa.fft_frequencies(sr=sr, n_fft=2048)
    low = S[fr < 150].sum(0)
    times = librosa.frames_to_time(np.arange(len(low)), sr=sr, hop_length=512)
    dur = len(y_mono) / sr
    # bar phase: the beat (0..3) whose positions carry the most low-end onset
    onset = np.maximum(0, np.diff(low, prepend=low[0]))
    best, best_k = -1, 0
    for k in range(4):
        ts = np.arange(phase + k * beat, dur, 4 * beat)
        v = sum(onset[min(len(onset) - 1, int(t * sr / 512))] for t in ts)
        if v > best: best, best_k = v, k
    start = phase + best_k * beat
    while start - 4 * beat >= 0: start -= 4 * beat
    bar_len = 4 * beat
    lines = np.arange(start, dur, bar_len)
    level = []
    for b in lines:
        m = (times >= b) & (times < b + bar_len)
        level.append(float(low[m].mean()) if m.any() else 0.0)
    level = np.array(level)
    ref = np.percentile(level, 90) if len(level) else 1.0
    return bpm, bar_len, lines, level / max(ref, 1e-9)


def plan_bars(level, loud, max_quiet, keep_buildup, tail):
    """Indices of the bars kept: from the first loud bar, long quiet runs shortened to their build-up."""
    is_loud = level >= loud
    if not is_loud.any(): return list(range(len(level)))
    first, last = int(np.argmax(is_loud)), int(len(level) - 1 - np.argmax(is_loud[::-1]))
    keep, i = [], first
    while i <= last:
        if is_loud[i]: keep.append(i); i += 1; continue
        j = i
        while j <= last and not is_loud[j]: j += 1
        run = list(range(i, j))
        keep += run if len(run) <= max_quiet else run[-keep_buildup:]
        i = j
    keep += [b for b in range(last + 1, min(len(level), last + 1 + tail))]
    return keep


def render(y, sr, lines, bar_len, bars, xfade=0.03):
    """The kept bars glued back together; a short crossfade wherever bars were skipped."""
    n_x = int(xfade * sr)
    out, prev = None, None
    for b in bars:
        a, e = int(lines[b] * sr), int(min(len(y), (lines[b] + bar_len) * sr))
        piece = y[a:e]
        if out is None: out = piece.copy()
        elif prev is not None and b == prev + 1: out = np.concatenate([out, piece])
        else:
            f = np.linspace(0, 1, n_x)[:, None] if y.ndim > 1 else np.linspace(0, 1, n_x)
            out = np.concatenate([out[:-n_x], out[-n_x:] * (1 - f) + piece[:n_x] * f, piece[n_x:]])
        prev = b
    # a short fade at the very end
    n_f = min(len(out), int(1.5 * sr))
    f = np.linspace(1, 0, n_f)[:, None] if out.ndim > 1 else np.linspace(1, 0, n_f)
    out[-n_f:] = out[-n_f:] * f
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", help="folder with the tracks (wav/mp3), used in name order")
    ap.add_argument("prefix", help="output name prefix, e.g. track28")
    ap.add_argument("--split", type=int, default=2, help="the first N tracks are cut into two levels each")
    ap.add_argument("--first-level", type=float, default=65.0, help="target length of the first half of a split track, s")
    ap.add_argument("--min-part", type=float, default=40.0, help="shortest half of a split track, s")
    ap.add_argument("--loud", type=float, default=0.45, help="a bar is loud when its bass reaches this (0..1 of the track's loud bars)")
    ap.add_argument("--max-quiet-bars", type=int, default=4, help="quiet runs longer than this are shortened")
    ap.add_argument("--keep-buildup", type=int, default=4, help="bars kept at the end of a shortened quiet run (the riser)")
    ap.add_argument("--tail-bars", type=int, default=1, help="bars kept after the last loud bar")
    ap.add_argument("--max-tracks", type=int, default=5)
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(argv)

    files = sorted(f for f in glob.glob(os.path.join(a.src, "*")) if f.lower().endswith((".wav", ".mp3", ".flac", ".ogg")))[:a.max_tracks]
    if not files: print("no tracks in", a.src); return 1
    os.makedirs(a.out, exist_ok=True)
    level_no, total = 1, 0.0
    for ti, f in enumerate(files):
        y, sr = librosa.load(f, sr=None, mono=False)
        y = y.T if y.ndim > 1 else y                        # (n, ch)
        mono = librosa.to_mono(y.T) if y.ndim > 1 else y
        bpm, bar_len, lines, level = analyse(librosa.resample(mono, orig_sr=sr, target_sr=22050), 22050)
        bars = plan_bars(level, a.loud, a.max_quiet_bars, a.keep_buildup, a.tail_bars)
        src_len, kept_len = len(mono) / sr, len(bars) * bar_len
        cut = len(level) - len(bars)
        print(f"{os.path.basename(f)}: {bpm:.1f} BPM, {src_len:.0f}s -> {kept_len:.0f}s (cut {cut} bars: intro/breakdowns)")
        print("   bars  " + "".join("#" if l >= a.loud else "." for l in level))
        print("   kept  " + "".join("K" if i in set(bars) else " " for i in range(len(level))))
        parts = [bars]
        if ti < a.split:
            # cut at a phrase line (every 8 kept bars) nearest the target, both halves long enough
            best = None
            for k in range(8, len(bars), 8):
                t1, t2 = k * bar_len, (len(bars) - k) * bar_len
                if t1 < a.min_part or t2 < a.min_part: continue
                if best is None or abs(t1 - a.first_level) < abs(best * bar_len - a.first_level): best = k
            if best: parts = [bars[:best], bars[best:]]
            else: print("   (too short to split)")
        for p in parts:
            name = f"{a.prefix}_{level_no}.wav"
            dur = len(p) * bar_len
            total += dur
            print(f"   -> {name}: {dur:.0f}s, {len(p)} bars")
            if not a.dry_run:
                sf.write(os.path.join(a.out, name), render(y, sr, lines, bar_len, p), sr)
            level_no += 1
    print(f"{level_no - 1} levels, {total / 60:.1f} min of music")
    return 0


if __name__ == "__main__":
    sys.exit(main())
