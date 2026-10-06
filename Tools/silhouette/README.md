# Trainer silhouette tools

The video trainer (`VideoSilhouette`, Video 9 on) shows filmed clips as a black figure with a neon
contour. Its clips live in `Assets/Silhouette/Video` (.webm, VP8 with alpha); the game draws them
from frames baked ahead of time, one loop per clip, looked up by song time (no video playback).

```
pip install -r requirements.txt
```

## A green-screen take -> a clip

```
python key_green.py "SOURCE.mp4" ../../Assets/Silhouette/Video/31_name.webm [--trim 0.9 2.9]
```

Keys out the green, scales the person (measured on the first frame, standing) to the size and
place of the other clips, and draws it black with the cyan -> magenta contour and glow. `--trim`
keeps only the active seconds (generated takes often move for a few seconds, then stand).

## Clips -> frames

In Unity: **Tunnel/Video Silhouette/Bake Frames**. It runs `bake_frames.py` on every .webm in the
folder (except the set's `excluded`), writes `Assets/Silhouette/Baked/<name>.png` - a flipbook of
one loop, imported as a Texture2DArray (DXT5) - and adds or refreshes the clip's row in
`Assets/Silhouette/VideoSilhouetteSet.asset`. Then add a row to the set's note table (`bindings`).

`bake_frames.py` finds the loop by itself (a take of 2 s or less is used whole; longer ones: the
stretch that moves most for the smallest seam, holding both sides when both move) and the strike
(the frame farthest from standing). When it gets it wrong, set the row's **manualCycle** with
`cycleStart`/`cycleEnd`/`peakTime` (source seconds) and `strikes`, and bake again. **mirror**
appends the loop's mirror image: a one-sided move becomes left-then-right.

To look at what it found, run it by hand:

```
python bake_frames.py ../../Assets/Silhouette/Video/22_march.webm out.png --cycle 1.5 2.625
```
