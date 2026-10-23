"""Video 28 in one go: cut the downloaded tracks into levels, then chart every level.
usage (from Tools/beatmap): .venv\\Scripts\\python make_video28.py "<folder with the 5 tracks>"
Then in Unity: Tunnel/Playlist/Build Video 28 (Retention)."""
import glob, os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
TRACKS = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Audio", "Tracks"))
src = sys.argv[1]
for old in glob.glob(os.path.join(TRACKS, "track28_*.wav")): os.remove(old)    # a re-run starts clean
subprocess.run([sys.executable, os.path.join(HERE, "prepare_levels.py"), src, "track28", "--split", "2"], check=True)
levels = sorted(glob.glob(os.path.join(TRACKS, "track28_*.wav")), key=lambda p: int(p.rsplit("_", 1)[1].split(".")[0]))
for n, wav in enumerate(levels, 1):
    r = subprocess.run([sys.executable, os.path.join(HERE, "generate_beatmap.py"), wav, "--detector", "grid", "--pattern", "deck28-%d" % n,
                        "--rest-beats", "1", "--spacing-tolerance", "0.05", "--grid-quiet-every", "2", "--fill-gaps-dodge"],
                       capture_output=True, text=True)
    print(os.path.basename(wav), "chart:", "ok" if r.returncode == 0 else "FAILED\n" + (r.stdout + r.stderr)[-800:])
    if r.returncode != 0: sys.exit(1)
print("done: %d levels. Unity: Tunnel/Playlist/Build Video 28 (Retention)" % len(levels))
