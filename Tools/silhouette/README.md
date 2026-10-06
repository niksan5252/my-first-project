# Trainer silhouette tools

The video trainer (`VideoSilhouette`, Video 9 on) shows filmed clips as a black figure with a neon
contour. Its clips live in `Assets/Silhouette/Video` (.webm, VP8 with alpha); the game draws them
from frames baked ahead of time, one loop per clip, looked up by song time (no video playback).

```
pip install -r requirements.txt
```

## A green-screen take -> a clip

```
python key_green.py "SOURCE.mp4" ../../Assets/Silhouette/Clips/name.webm [--trim 0.9 2.9]
```

Keys out the green, scales the person (measured on the first frame, standing) to the size and
place of the other clips, and draws it black with the cyan -> magenta contour and glow. `--trim`
keeps only the active seconds (generated takes often move for a few seconds, then stand).

## Clips -> moves -> frames

The clips live in `Assets/Silhouette/Clips` (keyed whole, e.g.
`python key_green.py "степ ногой.mp4" ../../Assets/Silhouette/Clips/step.webm`). Each is cut into
moves - a step, the punch of each hand, a squat - with the strike that lands on the note
(`VideoSilhouetteSetup.DefaultClips`, clip seconds measured on the takes; tune them in
`Assets/Silhouette/VideoSilhouetteSet.asset`). In Unity:

- **Tunnel/Video Silhouette/Reset Clips And Table** - the clips, their moves and the note -> move table.
- **Tunnel/Video Silhouette/Bake Frames** - runs `bake_frames.py --range` on the stretch of each
  clip its moves use and writes `Assets/Silhouette/Baked/<name>.png`, a flipbook imported as a
  Texture2DArray (DXT5).
- **Tunnel/Video Silhouette/Report Video9** - which move every kind of note in the Video 9 charts
  gets, notes left without one, the dodge take's share (also `Logs/VideoSilhouetteReport.txt`).

`bake_frames.py` without `--range` still finds a loop and its strike by itself, which helps when
measuring a new take:

```
python bake_frames.py ../../Assets/Silhouette/Clips/march.webm out.png
```
