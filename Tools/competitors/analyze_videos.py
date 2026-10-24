"""Breaks competitor videos down for analysis (the cloud cannot open YouTube): frames, scene cuts, music.
usage (from the project folder, the beatmap venv has numpy/librosa):
  Tools\\beatmap\\.venv\\Scripts\\python Tools/competitors/analyze_videos.py "<folder with the downloaded videos>"
writes Docs/competitors/<video>/ (about 1-2 MB a video):
  sheet_full.jpg   - a frame every 10 s, 8 to a row, left to right, top to bottom
  sheet_intro.jpg  - the first 60 s, a frame every 2 s, 6 to a row
  summary.json     - duration, BPM, loudness and bass per second, scene cuts (big picture changes)
  sheet_cues_a.jpg / sheet_cues_b.jpg - 40 s of gameplay at 3 frames a second (level 1 from 0:40, and a
                     later level at 60% of the video): every cue, the combo counter and the trainer readable
then commit and push Docs/competitors."""
import json, os, re, subprocess, sys, tempfile
import numpy as np

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "Docs", "competitors")


def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")


def duration(v):
    r = run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", v])
    return float(r.stdout.strip() or 0)


def sheet(v, out, start, length, every, cols, width=320):
    n = max(1, int(length // every))
    rows = (n + cols - 1) // cols
    vf = f"fps=1/{every},scale={width}:-2,tile={cols}x{rows}"
    run(["ffmpeg", "-y", "-loglevel", "error", "-ss", str(start), "-t", str(length), "-i", v, "-vf", vf, "-frames:v", "1", "-q:v", "4", out])


def scene_cuts(v, threshold=0.3):
    r = run(["ffmpeg", "-i", v, "-vf", f"scale=320:-2,select='gt(scene,{threshold})',showinfo", "-an", "-f", "null", "-"])
    return [round(float(t), 1) for t in re.findall(r"pts_time:([\d.]+)", r.stderr)]


def audio(v):
    with tempfile.TemporaryDirectory() as d:
        wav = os.path.join(d, "a.wav")
        run(["ffmpeg", "-y", "-loglevel", "error", "-i", v, "-vn", "-ac", "1", "-ar", "22050", wav])
        import soundfile as sf
        y, sr = sf.read(wav)
    y = y.astype(np.float32)
    sec = sr
    loud, bass = [], []
    # crude low band: moving average (~150 Hz)
    k = max(1, sr // 150)
    lo = np.convolve(y, np.ones(k) / k, mode="same")
    for i in range(0, len(y) - sec + 1, sec):
        loud.append(round(float(20 * np.log10(np.sqrt(np.mean(y[i:i + sec] ** 2)) + 1e-9)), 1))
        bass.append(round(float(20 * np.log10(np.sqrt(np.mean(lo[i:i + sec] ** 2)) + 1e-9)), 1))
    bpm = None
    try:
        import librosa
        t, _ = librosa.beat.beat_track(y=y[: sr * 120], sr=sr)
        bpm = round(float(np.atleast_1d(t)[0]), 1)
    except Exception:
        pass
    return loud, bass, bpm


def main():
    src = sys.argv[1]
    vids = sorted(f for f in os.listdir(src) if f.lower().endswith((".mp4", ".mkv", ".webm", ".mov")))
    if not vids: print("no videos in", src); return 1
    for f in vids:
        v = os.path.join(src, f)
        name = re.sub(r"[^A-Za-z0-9#]+", "_", os.path.splitext(f)[0])[:60].strip("_")
        out = os.path.join(OUT, name)
        os.makedirs(out, exist_ok=True)
        dur = duration(v)
        sheet(v, os.path.join(out, "sheet_full.jpg"), 0, dur, 10, 8)
        sheet(v, os.path.join(out, "sheet_intro.jpg"), 0, min(60, dur), 2, 6, 400)
        # dense gameplay strips: the cue logic (what comes when, how often, which side)
        sheet(v, os.path.join(out, "sheet_cues_a.jpg"), 40, 40, 1 / 3, 10, 384)
        sheet(v, os.path.join(out, "sheet_cues_b.jpg"), round(dur * 0.6), 40, 1 / 3, 10, 384)
        cuts = scene_cuts(v)
        loud, bass, bpm = audio(v)
        json.dump({"file": f, "duration": round(dur, 1), "bpm": bpm, "scene_cuts": cuts,
                   "loudness_db_per_s": loud, "bass_db_per_s": bass}, open(os.path.join(out, "summary.json"), "w"))
        print(f"{name}: {dur / 60:.1f} min, BPM {bpm}, {len(cuts)} scene cuts")
    print("done -> Docs/competitors. Commit and push it.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
