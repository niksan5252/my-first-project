# Beatmap generator

Turns an audio file into the beatmap JSON that `CueSpawner` loads from
`Assets/StreamingAssets/Beatmaps/`.

It lives in `Tools/` rather than under `Assets/` on purpose: Unity imports everything below
`Assets/`, and a `.py` file there becomes a tracked asset with a `.meta` file for no benefit.
`StreamingAssets` is the one folder Unity copies verbatim without importing, so a regenerated
map is picked up on the next Play without a reimport — you do not even need to refocus the
Editor.

## Setup (once)

Needs Python 3.9+. From this folder:

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
```

The `.venv` folder is throwaway; delete it and repeat to start clean.

## Running

```powershell
# Simplest form: writes Assets/StreamingAssets/Beatmaps/<name>.json
.venv\Scripts\python generate_beatmap.py path\to\track.wav

# Explicit output, plus a preview of the first 20 events in the terminal
.venv\Scripts\python generate_beatmap.py track.wav -o ..\..\Assets\StreamingAssets\Beatmaps\level1.json --preview 20

# Sparser chart for a busy track: no more than ~4 notes a second, only confident onsets
.venv\Scripts\python generate_beatmap.py track.wav --min-interval 0.25 --onset-delta 0.12

# Analyse and report without touching the file
.venv\Scripts\python generate_beatmap.py track.wav --dry-run --preview 30
```

`--help` lists every flag with its default. The output path defaults to
`Assets/StreamingAssets/Beatmaps/<input stem>.json`, resolved relative to *this script*, so it
works from any working directory.

Set `beatmapFileName` on the `CueSpawner` component to the file name you produced (just the
name, not the path — `BeatmapReader.ResolvePath` prepends `StreamingAssets/Beatmaps`).

## Which moments get a note — `--detector`

**`drums` (default): notes on the kick and the clap/snare only.** Hats, shakers, small percussion,
sustains and melody never reach the chart. Each drum is found in its own band, against its own
threshold, on an **amplitude** envelope (99th percentile = 1) — not on spectral flux:

* kick — the band below `--kick-hz`; a peak must reach `--kick-threshold` (0.7);
* clap / snare — the geometric mean of the mid band and the band above `--punch-hz`, i.e. a hit
  that is loud in both at once; a peak must reach `--clap-threshold` (0.6);
* both must stand `--drum-prominence` (0.3) above the valleys around them. `--no-claps` keeps the
  kick only.

Amplitude rather than flux is what makes the kick findable under a bass line: flux is computed on
decibels, which inflate small changes, so a bass wobbling between 20% and 50% of the low band
reads as onsets as strong as the kick's. In amplitude the kick jumps to ~1.0 and the bass stays
under ~0.5. The drum's type still does not decide the note's type — the workout set does; the drum
only decides *when*.

Measured on `irokz_goodbye_my_love.wav` (a phonk-house track, 128 BPM), share of chart notes
within 40 ms of a beat, after removing each detector's constant offset from the fitted grid:

| detector | notes | on a beat | off the grid | median error |
|---|---|---|---|---|
| `all` (the old behaviour) | 276 | 44% | 45% | 34 ms |
| `drums` | 277 | 27% | 69% | 55 ms |
| `drums --no-claps` | 174 | 74% | 24% | 8 ms |

That track has no clap backbeat — the "claps" it finds are its bell melody — so on it the clap
half makes things worse and `--no-claps` is the right call. Most of the remaining 24% is real:
parts of the track use a syncopated kick pattern, not four-on-the-floor. `--detector all` keeps
the original every-onset behaviour.

**`grid`: a note on every beat, but only where the drums play.** The fitted beat grid decides
*when*, the kick decides *where*: a bar gets a note on each of its beats only if the drums detector
finds at least `--grid-min-kicks` (2) kicks in it; every other bar — breakdowns, fills, quiet
stretches — is a rest. The rule lives in `drums_play_in_bar`. Notes sit on the grid shifted by the
median offset between the detected kicks and their nearest beats, measured per track (+25 ms on
the track below: the comb that fits the grid reads spectral flux, which peaks just before the
kick's attack). The run prints a map of the bars, `#` drums and `.` rest, 16 to a row.

This is what `irokz_goodbye_my_love.json` in the scene is made with — 69 of 106 bars carry notes,
every interval is a whole number of beats, and the rests line up with the breakdowns:

```powershell
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\irokz_goodbye_my_love.wav --detector grid --fill-gaps-dodge --set-bars-for Punch:5,Kick:5
```

`--set-bars-for` gives chosen exercises a set length of their own, in whole bars; everything else
keeps `--set-bars` (3). Punch and Kick at 5 bars double their active time on this track (4.0 s to
8.0 s, 9.3 to 16.3 notes a set) and shrink the average silence between sets from 1.73 s to 1.36 s;
the other sets are unchanged, there are just fewer of them in a longer cycle.

## Filling empty stretches with Dodge walls — `--fill-gaps-dodge`

Grid charts leave the intro, the breakdowns and the outro empty. `--fill-gaps-dodge` puts Dodge
walls there - measured, so walls are a break between exercise blocks rather than a replacement
for the music (a wall every bar through a 40 s intro was the problem this replaced):

* **intro** — walls only in the last `--gap-intro-lead-bars` (4) before the first note, one every
  `--gap-intro-every` beats (4 = a bar), at most `--gap-intro-max` (4); before that the road is
  empty and the trainer stands in its stance. The first `--gap-intro-bars` (1) after the music
  starts always stay empty;
* **breakdowns** — one wall every `--gap-dodge-every` beats (8 = every other bar), only in a pause
  of at least `--gap-min-bars` (4) — a shorter pause keeps no wall — measured from the end of a
  hold, not its start;
* **outro** — at most `--gap-outro-max` (3) walls after the last note;
* every wall sits on the fitted bar grid, so walls follow the tempo even where no drum plays, and
  keeps `--gap-margin-bars` (1) clear of the nearest note on both sides, so the camera is back in
  the centre before the next exercise;
* sides mostly alternate, one time in three the same side repeats, never three in a row
  (`next_dodge_side`, seeded by `--seed`). The run prints the walls as `time L|R`, where L/R is
  the way the player moves.

The walls are added after the workout sets are planned, so no set counts them among its notes.
`CueSpawner` relies on that: its intro trim drops every note before the intro cutoff *except* a
Dodge outside any set with notes, which it keeps once it can be born `introWallBars` (1) after
the music starts. A note spawns one flight (≈6.9 s in TunnelScene) before it arrives, so the first
intro wall that actually appears arrives at ≈9.5 s; earlier ones stay in the chart for scenes with
a shorter flight.

The run prints the count per stretch (`gap walls by stretch: intro …, breakdowns …, outro …`).

**The old fill-everything behaviour** (a wall every bar in every gap of 2+ bars) is still there:
`--gap-min-bars 2 --gap-dodge-every 4 --gap-intro-lead-bars 0 --gap-intro-max 0 --gap-outro-max 0`
(0 = no limit). The committed `irokz_goodbye_my_love.json` and `adikop_bring_me_back.json` were
made that way, before the new defaults; regenerating them with these flags reproduces them.

On `irokz_goodbye_my_love.wav` (old behaviour): 25 walls — 9 in the intro (5 of them shown in the
scene), 1 at 33.9 s, 10 across the 65–92 s breakdowns, 5 in the outro.
On `adikop_bring_me_back.wav`: 43 walls with the old behaviour (intro 22, breakdowns 21), 13 with
the new defaults (intro 3, breakdowns 10, outro 0).
On `rameses_offline.wav` (new defaults, the committed chart): 12 walls — intro 3, breakdowns 8,
outro 1. It runs at 125 BPM: the kick is four-on-the-floor at ~480 ms, i.e. one note per beat is
2.08 notes/s; the 83 BPM that tempo estimators also offer is the triplet clap pattern, not the pulse.
On `second_wind.wav` (new defaults, the committed chart): 14 walls - intro 3, breakdowns 10,
outro 1. The comb fits 126.984 BPM and the kick is four-on-the-floor at ~470 ms (IOIs 0.97-1.02
beats), so one note per beat is 2.12 notes/s; the tempogram's 117 and beat_track's 129 are both off.
On `seanyy_ocean.wav`: 8 walls, all in breakdowns (the drums start with the track, so there is
no intro stretch). 127.986 BPM from the comb; beat_track and the tempogram both say 129.2 (their
bin), and the kick sits on the chart grid within 4 ms (median) in the recording.
On `feelings_fade.wav`: 10 walls - intro 3, breakdowns 6, outro 1; 129.996 BPM from the comb
(estimators: 129.2, their bin), kick four-on-the-floor (IOIs 0.975-1.013 of the median).

## What it does

1. **Onsets and tempo.** One mel spectrogram feeds the whole pipeline, so the onset envelope,
   the per-band classification and the sustain measurement are all indexed by the same frames.
   Onsets come from `librosa.onset.onset_detect`; tempo from `librosa.feature.tempo` (the
   post-0.10 home of `librosa.beat.tempo`, which librosa 1.0 removed).

   Detection does **not** run on `librosa.onset.onset_strength`, and that is deliberate: it
   averages spectral flux over all 128 mel bands, and a kick drum only lights the two or three
   below 150 Hz, so in the average it contributes a few percent while a hi-hat lights most of
   the spectrum. On a plain four-on-the-floor loop that costs you every single Kick — the hats
   get detected, the kicks never clear the threshold, and the chart comes out as an unbroken
   row of Punches. Instead the three per-band envelopes are each normalised against their own
   band and the detection envelope is their **maximum**: an event that is strong anywhere is an
   event. Intensity is read off that same envelope for the same reason.

2. **Type by frequency band.** Three bands — low (below `--kick-hz`, default 150 Hz), mid
   (up to `--punch-hz`, default 2 kHz) and high. The classifier reads onset *strength*
   (spectral flux) per band, not raw energy: on a track with a continuous bass drone every
   frame has huge low-band energy, so an energy rule would call every hi-hat a Kick. Flux only
   rises when something in that band actually starts.

   * all three bands firing hard at once, on a strong onset → **Jump**
   * low band lit in its own terms (`--kick-level`) **or** dominant over the others
     (`--kick-dominance` × the strongest other band) → **Kick**
   * otherwise → **Punch**

   The absolute rule is not redundant with the relative one, and dense productions need it: in
   techno the kick lands on a hi-hat or a clap nearly every time, so mid and high are loud at
   exactly the moments the kick is. On the 143.55 BPM techno track in this project the relative
   rule alone found 44 kicks where the low band's own onsets say there are roughly 430 — a
   four-on-the-floor record charted as an almost unbroken row of Punches. Lower `--kick-level`
   to ~0.30 for that kind of material; the default 0.45 is the conservative setting.

   Jump is tested first because a full-spectrum accent is also bass-heavy and the Kick rule
   would otherwise swallow it. `--no-jumps` turns them off entirely.

3. **Timing refinement.** Detection says *which* moments matter; a second pass says *when* they
   are. Using one tool for both is what put a 32 ms systematic gap between Kick and Punch in the
   first charts: the mel spectrogram's window has to be long (93 ms at the old settings) to
   resolve a 60 Hz kick in frequency, and a window that long smears the attack across itself —
   differently for a low-frequency body than for a broadband transient. Measured against the
   waveform, kicks came out early and everything else late. No single offset fixes a difference
   that depends on the note's type, which is why it was heard as *wandering* rather than as lag:
   on a four-on-the-floor track the types alternate constantly, so the error swung back and
   forth by ~32 ms from note to note.

   So the refinement pass stops asking the spectrogram for timing. Timing needs amplitude
   resolution, not frequency resolution: band-limit the signal with a zero-phase filter, take an
   RMS envelope, and read the attack off that as the point where the rise from the preceding
   valley passes a fraction of its height, interpolated between envelope samples. Each band gets
   its own window, since the requirements differ. Zero-phase filtering (`sosfiltfilt`) is not a
   detail — an ordinary IIR filter delays the signal by its group delay, and a delay is exactly
   what is being measured.

   Calibrated against the synthetic fixture, where the true hit times are known rather than
   estimated: the combined error went from **+25.7 ± 9.6 ms to −1.5 ± 6.5 ms**, and the worst
   single note from 56 ms to 13 ms. Turn the pass off with `--no-refine` to see the raw grid.

4. **Holds.** See the criterion below.

5. **Intensity.** Normalised onset strength in the band the event actually owns, `0..1`. The
   reference is the 99th percentile of the detected onsets rather than the maximum over the
   whole file (`--norm-percentile`) so one clipped sample or one cymbal crash cannot push an
   entire track towards zero.

6. **Lanes.** `--lane-mode`, default `alternate`. See below.

7. **Thinning.** `--min-interval` (default 0.12 s) sets the floor on the gap between events,
   and `--min-intensity` drops the weakest onsets before that.

## Workout mode — what decides a note's type

**Onsets decide *when* a note happens. The workout structure decides *what* it is.** This is the
default; `--no-sets` restores typing each note by its own frequency content.

Typing every note acoustically is correct as music analysis and wrong as exercise design. A bar
of techno puts a kick, a hat and a clap inside 400 ms, so the chart asks for a leg, then an arm,
then a jump, and nobody can follow that. So the track is cut into **sets** on the bar grid, each
set is one exercise, and every note inside it is that exercise.

* `--set-bars` (3) — length of a set, in bars, measured bar line to bar line. At 140 BPM that is
  5.1 s, which is the cadence a person can actually follow: long enough to get into a movement,
  short enough that no exercise outstays its welcome. Drop to 2 for a faster circuit (3.4 s).
* `--rest-beats` (1.0) — beats of silence at the **end** of each set, not between sets. This is
  what lets the rest be shorter than a bar without losing bar alignment: a rest inserted
  *between* sets would push every later set further off the grid, and within four sets the
  routine would be changing movement mid-bar. Here the block always runs bar line to bar line
  and simply stops accepting notes early. Notes landing in the rest are dropped.

  The silence a person experiences is longer than this number, because the next set's first
  onset does not land on its own first frame either — at one beat the measured gaps run
  0.45–1.1 s. `--show-sets` prints the real gap per set, which is what to judge it by.
* `--beats-per-bar` (4).
* `--pattern` — the set order. Presets: `circuit` (default), `workout`, `classic`, `intervals`,
  `arms`, `legs`. Or an inline list: `--pattern "Punch:6,Kick:5,Jump:4,PunchHold:2"`, where each
  entry is an exercise and how many sets of it run back to back, cycled until the track ends.
  This table (`SET_PATTERNS` in the script) is the only place the shape of the routine lives.

  `circuit` makes every block a single set, so the movement changes on a fixed cadence and the
  person learns how long a block lasts instead of having to watch for the change. `workout` is
  the opposite extreme — six sets of one exercise in a row — and is what the first version did.
* `--spacing` — minimum gap between notes per exercise, **in beats**, so a routine keeps its
  shape at any tempo. Defaults: Punch 0.5, Kick 0.5, Jump 1, holds 2. The holds and Jump are the
  ones that matter: left to the onset density of a techno track, a hold set would ask for a new
  sustained note five times a second.
* `--lane-pairs` — the two lanes each exercise alternates between, `Punch:0-3` style. Default is
  `1-2` for everything: adjacent and symmetric about the centre, so the alternation reads as
  left limb / right limb rather than as a position on the road. Widen Punch to `0-3` if the
  centre-only look is too narrow.
* `--show-sets` — print the routine, one line per set.

Two details worth knowing. Bar boundaries are placed on **bars, not on round numbers of
seconds** — a change of movement landing mid-bar reads as a mistake even to someone who cannot
name what is wrong. And a hold's length in workout mode is not measured from the audio: the set
already decided this is a sustained movement, so the note simply runs to the next note of its
set, less `--hold-gap`, capped by `--hold-max`.

The acoustic classification does not disappear — it still chooses which frequency band the
timing refinement looks in. Refining a hi-hat in the low band because the set happens to be a
Kick set would put the note tens of milliseconds off.

## Obstacles — the three exercises the camera performs

`Dodge`, `Curb` and `Duck` are exercises like any other in a pattern, but they are not notes.
A note is something to strike at the hit zone and it is over there; an obstacle is a piece of the
road that keeps coming and has to be gone **around**, **over** or **under**, and in the game the
camera itself makes the movement — it leans out of the way of a wall, floats over a kerb, drops
under a barrier. So the person following the video is not being asked to hit something; they are
being shown a movement to copy.

| exercise | what is on the road | what the player does |
|---|---|---|
| `Dodge` | a wall taller than a person down one half of the road, 8 m long | step to the free half and stay there while it passes |
| `Curb` | a low kerb, half a metre, right across the road | jump |
| `Duck`  | a beam across the road at head height, with the passage under it left clear | crouch |

**`Dodge` reads its lane as a side.** Lanes in the left half of the road put the wall on the left
and send the player right; the right half is the mirror. That is why its default lane pair is
`0-3`: the walls then alternate left and right within a set instead of leaning one way for the
whole block. `Curb` and `Duck` span the full road and ignore their lane entirely.

The lane carries the direction rather than a `direction` field of its own, because a new field
would be a second thing to keep in step across this generator, the C# DTO, the reader and the
spawner, and it would say nothing the lane cannot. The cost is that `resolve_hold_overlaps` must
leave obstacles alone — nudging a `Dodge` into a free lane would silently point it the other way
— which it does.

### Spacing — the number that is not a taste judgement

The camera has to be back at centre before the next obstacle asks it to move again, so obstacles
are spaced far wider than any note. The longest reaction is the dodge: its lead-in, the hold
while the wall goes past, and the return together run **1.59 s**. The defaults put 8 beats
between obstacles — 3.43 s at 140 BPM — which leaves 1.84 s of rest at centre between two of
them. 4 beats (1.71 s) is the floor if you want a denser routine: it still clears, but with
almost no rest.

Two knobs, and you want both:

* `--spacing Dodge:8,Curb:8,Duck:8` (the default) is the floor **inside** one set;
* `--obstacle-gap` (default 8 beats) is the same floor **across set boundaries**, which
  per-exercise spacing cannot see. A set of walls ending on its last bar line followed by a set
  of barriers starting on the next one puts two obstacles within a beat of each other, and
  nothing inside `apply_workout` would notice.

An obstacle that falls foul of `--obstacle-gap` is **dropped, not moved**. Moving it would take
it off the onset it was placed on, and an obstacle arriving off the beat is worse than no
obstacle at all — the whole camera reaction is timed against the music.

### Patterns that include them

* `obstacles` — obstacle sets alternating with striking sets. Not back to back: an obstacle is a
  whole-body movement, and a routine that is nothing but obstacles is one where the camera never
  sits still, which is unpleasant to watch and much worse to record.
* `circuit_obstacles` — the default circuit with the obstacles folded in, one obstacle set about
  every third block. This is what `techno_obstacle_circuit.json` was made with:

```powershell
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\techno_circuit_flow.wav `
    -o ..\..\Assets\StreamingAssets\Beatmaps\techno_obstacle_circuit.json `
    --pattern circuit_obstacles --kick-level 0.30 --show-sets
```

A three-bar set at 140 BPM holds two obstacles at this spacing, which is what the shipped chart
has: 6 `Dodge`, 6 `Curb`, 4 `Duck` over 207 s, with a measured minimum gap of 3.44 s.

## Getting the tempo right — harder than it looks

Three methods were tried on this project's techno track, and only the last is trustworthy:

| method | answer | verdict |
|---|---|---|
| `librosa.feature.tempo` (tempogram) | 143.55 BPM in all 19 windows | wrong by 2.5% |
| straight fit through `beat_track`'s beats | 139.56 BPM | 0.3% out — 0.64 s of drift per track |
| comb fitted to the whole track | 139.99 BPM | correct; the track is a machine 140 |

The tempogram is quantised to its own bins and was *consistently* wrong, which is the dangerous
kind of wrong — repeating the measurement in 19 windows reproduced the same error and looked
like confirmation. The beat tracker follows the audio well but its output contains the odd
inserted or missed beat, which a straight fit inherits.

A comb spanning the whole track has the track's full length as its baseline: a period wrong by a
tenth of a percent displaces the last tooth by 200 ms and the score collapses. It scored 0.9932
at the true period against 0.1516 at the tempogram's answer. The beat tracker is still used — to
seed the search, which keeps it narrow and cheap.

The independent check that settles it: the synthetic fixture is written at exactly 120 BPM by
the script that generates it. The comb recovers 119.898; the tempogram said 117.45.

## The hold criterion — this is the part worth arguing about

You asked for a proposal here, so: **a note is a hold when it lifts its own frequency band
clearly above what was already playing there, and that band then stays above `--hold-decay`
(default 0.45) of the note's attack peak for at least `--hold-min` (default 0.35 s), with no
later note of the same instrument family inside that span.**

Four details, each of which was the difference between the feature working and the feature
being useless when I tested it:

* **Band-local, not broadband.** Kicks are measured in the low band, Punches in mid+high.
  Measured broadband, a sustained bass note appears to stop the moment the hi-hats do, and a
  held pad appears to sustain straight through the kick pattern underneath it.
* **Energy, not flux.** The classifier uses flux; hold length cannot. A held note produces no
  flux after its attack, so a flux reading calls everything a one-shot.
* **The peak comes from a short attack window** (`--hold-attack`, 60 ms), not from the onset
  frame: the onset frame is where flux rises, a frame or two before amplitude actually peaks.
* **The note has to lift the band** (`--hold-lift`, default 1.5×) above its level just before
  the onset. Without this a dense mix reports holds everywhere — the mid band of a busy
  arrangement never drops below 45% of anything, so every snare landing over sustained strings
  "sustains" for as long as the strings do. On the 65 s orchestral track in this project that
  one condition took holds from 21% of all events down to 5%.

A hold is bounded by the next event **of the same family** (next Kick-ish event for a kick,
next Punch-ish for a punch; a Jump bounds both). Bounding by the next event of any kind is the
obvious rule and it makes holds unreachable in practice: with a hi-hat pattern there is another
onset every 100–150 ms, so every sustain is cut long before `--hold-min` and you get zero holds
on a track full of them. A held bass note is not ended by the hats playing over it.

On the gameplay side, a hold keeps its lane to itself for its whole length: later notes that
would collide are **moved to a free lane**, not used to trim the hold. Only when every lane is
held does the blocking hold get shortened, and a hold trimmed to nothing degrades back to
`Kick`/`Punch` — a hold whose duration rounds to zero is exactly what `BeatmapReader` refuses
to load.

**Where I expect this to need tuning.** `--hold-decay` is the main dial: lower (0.3) is more
permissive and yields longer holds, higher (0.6) demands a genuinely flat sustain.
`--hold-lift` controls how much of a dense mix gets through at all — raise it to 2.0+ if you
still see holds that belong to some other instrument. `--hold-min` sets the shortest hold worth
having. Worth a listening pass before we settle the defaults.

An alternative I did not implement, in case you prefer it: derive holds from pitch stability
(`librosa.pyin`) instead of energy, so a hold means "one continuous note" rather than "sustained
loudness". Markedly better on melodic material, useless on drums, and slow.

## Lane assignment

Deliberately isolated: every strategy is a function `(events, lane_count, rng) -> None` that
writes `ev.lane` in place, registered in the `LANE_STRATEGIES` dict. Nothing else in the
pipeline reads or assumes anything about lane numbers, so replacing the logic is one function
plus one dict entry.

| `--lane-mode` | behaviour |
|---|---|
| `alternate` *(default)* | round-robin `0,1,2,3,0,…`; predictable and evenly spread |
| `frequency` | left to right by brightness — bass on the left, cymbals on the right |
| `random` | seeded random (`--seed`), never twice in a row |

Whatever a strategy proposes, `resolve_hold_overlaps` runs afterwards and moves any note that
would land in a lane a hold is still occupying. That is the one gameplay invariant a strategy
cannot be trusted to uphold, so it lives outside them rather than in each of them — which does
mean a nudged note can occasionally end up beside its neighbour.

`--lanes` must match `NeonTunnelGenerator.laneCount` (currently 4). `BeatmapReader` rejects the
whole file if any lane falls outside `0..laneCount-1`.

## Known limits

* **A hit in the first frame of the file is not detected.** Onset detection needs a rise, and
  there is nothing before sample 0 to rise from. If a track starts on a downbeat, either add a
  few hundred ms of silence at the head of the audio or hand-add the first event.
* **A loud sustain masks percussion in its own band.** While a bass note is held, kicks under
  it stop producing low-band flux and drop out of the chart. Audible on the test fixture as a
  missing kick in the middle of the sustained bass. Raising `--n-mels` narrows the affected
  band a little; properly fixing it needs source separation, which is out of scope here.
* **Tempo is a single global estimate.** A track that changes tempo gets one BPM for all of it.
  Nothing in the event timing depends on BPM, so this only affects anything that pulses with it.
  The estimate runs on its own envelope at a fixed time resolution rather than on the detection
  envelope, because sharing the detection hop makes the reported tempo a function of
  `--hop-length`: on the techno track here, moving the hop from 512 to 256 moved the estimate
  from 143.55 to 139.67 BPM on audio whose real tempo does not vary at all.
* **Low-frequency attacks have no single right answer.** A 60 Hz transient is physically smeared
  over ~15 ms, so where exactly the hit "is" depends on the convention used to read it. Two
  reasonable envelope estimators disagreed by 22 ms on the same kicks. The refinement pass is
  calibrated so both bands land on the *same* convention — which is what the offset knob needs —
  but the absolute zero point is a choice, not a measurement.

## Format contract

The reader in `Assets/Scripts/Beatmap/BeatmapReader.cs` validates every event and refuses the
whole file on the first problem, naming the event index. The generator upholds all of it:

* `version` is `1` (`BeatmapReader.SupportedVersion`);
* `type` matches the C# `CueType` enum **case-sensitively**: `Kick`, `Punch`, `Jump`,
  `KickHold`, `PunchHold`, `Dodge`, `Curb`, `Duck`;
* holds carry `duration > 0`, one-shots carry exactly `0.0` — never a missing field, and the
  three obstacles are one-shots;
* `lane` is within `0..lanes-1`, `time` is non-negative, `intensity` is `0..1`.

If you change `CueType` on the C# side, `HOLD_TYPE_OF`, `OBSTACLE_TYPES` and the
`KICK`/`PUNCH`/`JUMP`/`DODGE`/`CURB`/`DUCK` constants at the top of `generate_beatmap.py` are the
matching edit.

Output is written to a temp file and `os.replace`d over the target, so a player reading
`StreamingAssets` mid-regeneration sees either the old map or the new one, never half of one.

## Workout sets in the chart (`sets`)

In workout mode the generator lays the routine out on the bar grid: a set is a run of bars doing
one exercise, followed by a rest. Since v1 of this field it writes that plan into the chart as an
optional top-level `sets` array, one entry per block:

```json
{ "index": 0, "exercise": "Punch", "start": 1.341, "end": 5.81, "restEnd": 6.668, "notes": 14 }
```

Nothing in the spawn path reads it — notes still come entirely from `events` — so a chart without
the field plays identically. It exists for presentation: the HUD shows its end-of-set banner at
`end`.

### Charts made before the field existed

`add_sets.py` adds the block to an existing chart **without re-running the analysis**:

```sh
python add_sets.py ../../Assets/StreamingAssets/Beatmaps/techno_circuit_flow.json --dry-run
python add_sets.py ../../Assets/StreamingAssets/Beatmaps/techno_circuit_flow.json
```

It needs no audio, because in workout mode every note inside a set is the same exercise — so a
run of consecutive events sharing a `type` *is* a set. Runs shorter than `--min-notes` (3) are
merged into the block before them; those are grid artefacts, not sets, and left alone they would
fire an end-of-set banner a second after the previous one. `events` is never touched.

Re-generating instead is the wrong move for a finished chart: the set plan depends on the exact
`--pattern` and `--set-bars` the map was made with, and any difference silently rewrites every
note in it.

## Intense charts - chords and sweeps (the Halloween playlist)

Two exercises that are not JSON types of their own - or not only:

* **`Chord`** (set exercise only). Every note of a Chord set is written as **two `Kick`s on the
  inner lanes at the same moment** - a jump landing on both feet - and every
  `--chord-punch-every`'th one (default 2) also gets a **`Punch`** on top, alternating between the
  pair's lanes (`--lane-pairs Chord:1-2`). Spacing 2 beats: a jump every other beat. `expand_chords`
  is the whole rule. The trainer recognises two kicks on one moment and plays a two-foot hop that
  lands on the note (the punch is thrown on the landing).
* **`PunchSweep`** - a new JSON type, a hold: a raised fist carried out to the side along a tail
  that runs from the vanishing point up and out (`CueSpawner`, lane 0 = left, lane 3 = right;
  default pair `0-3`). It stops `--sweep-gap-beats` (0.5) before the next note of its set so the arm
  can come back to the middle; the last one of a set may run into the set's rest.

`--pattern intense` folds both in with short blocks, one Duck set about every eighteen sets and a
Dodge set as often. `--spacing-tolerance 0.05` lets grid notes exactly N beats apart pass their
N-beat floor (without it rounding of the fitted period drops every other one). The committed
`neoni_haunted_house.json`:

```powershell
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\neoni_haunted_house.wav `
    --detector grid --pattern intense --set-bars 2 --spacing-tolerance 0.05 --fill-gaps-dodge --show-sets
```

105.000 BPM from the comb (kick IOIs peak at 0.56-0.58 s; beat_track and the tempogram say 103.4,
their bin). Drums play from the first bar, so notes start at 0.07 s; the playlist's pre-roll lets
them fly in. 193 events: Kick 85 (36 of them in 18 two-foot chords), Punch 72 (9 on chords),
PunchSweep 14, KickHold 8, PunchHold 4, Duck 2 (27.5 s, 112.1 s), Dodge 8 (2 in Dodge sets, 6 gap walls).

## intense-v2 - the variety routine (video 3, pass 2)

`--pattern intense-v2` is planned by `plan_sets_v2` / `choose_set_exercise`, not cycled: a normal
rotation of short sets (2 bars, the long wall 4) that never repeats an exercise back to back and
never puts two obstacle sets together, and a heavy rotation - Cross, Jack, SideJump, Chord - for the
eight bars after every drop (the drums returning after 4+ quiet bars). Rest: `--rest-beats 1`.
`--grid-quiet-every 2` keeps a note every other beat through breakdowns and intros. PunchSweep is
off unless `--sweeps` is given (a sweep set becomes a PunchHold set).

`expand_compound` writes the variety exercises out as ordinary notes:

| set exercise | notes |
|---|---|
| Cross | PunchHold + KickHold on lanes 0 and 3 together, once a bar, held to a beat before the next (the star) |
| WallRun | one Dodge with a duration along the set (a long wall), Kicks alternating on the free lanes, a Punch every other beat |
| SideJump | Kick pairs 0+1 / 2+3 on alternate beats |
| HighLow | Punch and the new **PunchHigh** (overhead) on alternating hands, every fourth beat both at once |
| Jack | Kick pairs 0+3 / 1+2 alternating |
| Shift | a Kick pair walking one lane per beat: 0+1, 1+2, 2+3, 1+2, ... |
| Balance | one **CurbGap** (a kerb over every lane but `lane`: stand on one leg) or **CurbSplit** (over the two inner lanes: wide stance), in turn, held for the set |

New JSON: `PunchHigh` (one-shot), `CurbGap` / `CurbSplit` (obstacles, duration required), and
`Dodge` may now carry a duration (a long wall). The committed charts:

```powershell
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\neoni_haunted_house.wav --detector grid --pattern intense-v2 --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\axollo_burn_it.wav --detector grid --pattern intense-v2 --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge
```

`axollo_burn_it.wav`: 130.001 BPM from the comb (kick IOIs 0.46 s; beat_track and the tempogram
give their 129.2 bin); a 29.5 s drumless intro, filled at every other beat.

`rxm_look_at_me.wav` (phonk): **120 BPM** (comb 119.992). The comb converges to the same period
seeded from 60, 120 or 240, so the octave is decided by what the chart asks of a person: 120 puts
one note a beat at 2.0 notes/s (under the ~2.3 limit), 240 would ask for 4/s, 60 for a sluggish
1/s. The phase checks out: 36% of the detected kicks land on its beats against 6% on the
off-beat eighths; the claps at 0.25 s are its eighths. beat_track and the tempogram say 117.45
(their bin). 321 events.

## Per-track character: `--pattern intense-v2-arms | -legs | -combo | -finale`

Video 6 (Neon Prism) wants every chart to feel different. These patterns use the intense-v2
machinery but compose the routine in 4/8-bar **phrases** (a motif that comes back: A B A C) from
the `CHARACTER_PLANS` table in `generate_beatmap.py` - that table is the whole policy. Drops get
their own "drop" phrases, stretches without drums get "calm" ones (holds, balance, ducks).
`arms`: Punch/HighLow/PunchHold/Cross, a little Kick. `legs`: Kick/SideJump/Shift/Jack, Duck/Curb/
Balance in the calm stretches. `combo`: Chord/Jack/Shift under HighLow, Cross. `finale`: everything,
one-bar changes in the first phrase, 16 bars of "drop" phrases after a drop.

```powershell
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\prism_club_energy_1.wav --detector grid --pattern intense-v2-arms --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge
```

## Video 7 (Neon Carnival): `ArcHold`, the carnival sets, `--intro-melody-onsets`

**`ArcHold`** is a JSON type: a PunchHold bent into a circle. The hand is the lane's half of the
road (lanes 0-1 left, 2-3 right); `arc` is the degrees the fist travels, signed (positive =
clockwise as the player sees it; 270-540 in the charts, the reader takes up to 720), and `radius`
is in metres (0 = the game's default, 0.75). The fist starts at the top of the circle.

Set exercises written out by `expand_compound`:

* `Arc` - a mirrored pair of ArcHolds (both hands) on the set's first beat, a WHOLE number of beats
  long (4-8, so it starts and ends on a beat), then steps. The circles come from the plan's `arcs`
  list (degrees, beats, radius); directions alternate pair by pair, outward then inward. Never two
  Arc sets back to back. With the plan's `lead_in`, the bars before a drop become an Arc set.
* `Fan` - mirrored Punch/PunchHigh pairs thrown out like rays (`FAN_RAYS`), a step between pairs.
* `StepTap` - step L, jab R, step R, jab L (light). `Mirror` - both hands every beat, low/high in
  turn. `PunchKick` - a punch and the opposite foot together every beat (dense).

The three plans differ on purpose - no phrase is shared:
`carnival-rio` (light: steps, single punches, a fan only on drops, medium circles),
`carnival-arcs` (dance: fans and mirrored pairs, the longest circles, 6-8 beats, up to 540 deg),
`carnival-copa` (finale: chords, side jumps, jacks, ducks, punch+kick; circles only on breaks).
`calm_first` makes a bar without drums a break even inside a drop's stretch.

**`--intro-melody-onsets`**: before the drums first come in (the first `#` bar of the grid map),
notes follow the melody instead of the grid - HPSS harmonic part, onset detection
(`--intro-melody-delta`, `--intro-melody-min`), no closer than an eighth note (the stronger of two
wins), the stronger half Punches, the rest steps, alternating hands/feet. No chords, no holds.
Each onset is then moved to its attack in the full mix (`refine_onset`, the drums' rule; search
`--intro-melody-search`, 60 ms): the onset-strength peak trails the attack by ~32 ms median on
track7_1. Measured against backtracked mix onsets, notes within 15 ms went from 1/42 to 41/46
(track7_1) and 0/44 to 26/38 (track7_2). "Drums in" is the first of two drum bars in a row
(`first_drum_bar`) - a lone drum bar in an intro is a fill.

**`--intro-arcs`**: instead, the intro before the drums is a chain of ArcHolds, one per 4-bar
phrase (the remainder goes to the last), each to one beat before the next phrase, one turn a bar;
hands L, R, L...; the last phrase both hands mirrored. **`--intro-rest S`**: no notes before the
phrase (else bar) line nearest S seconds, within +-15%.

```powershell
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track7_1.wav --detector grid --pattern carnival-rio --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge --intro-melody-onsets --intro-rest 7
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track7_2.wav --detector grid --pattern carnival-arcs --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge --intro-melody-onsets
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track7_3.wav --detector grid --pattern carnival-copa --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge --intro-arcs
```

## Video 8 (Halloween 2): `halloween2-swamp | -lab | -castle`, mirrored sets, arcs + melody intro

Three plans in `CHARACTER_PLANS`, composed as 8-bar phrases: a motif, the motif again on the
other side, a change of limb. A plan entry may carry a third element `"R"` - the set is mirrored
(`mirror_sets`: every lane flipped, every ArcHold turned the other way), so a combo that leads with
the left is repeated leading with the right. New set exercises (`expand_compound`), each written
for the left lead:

| set | notes |
|---|---|
| StepTouch / JabCross | a step / a punch every other beat, L/R (half time - the warm-up) |
| JabKick | jab L, cross R, jab L, kick with the rear (right) foot - the 1-2-1-kick |
| StepReach | step L + PunchHigh R, step R + PunchHigh L, one limb a beat |
| ArcPunch | the left hand circles (one ArcHold, a turn a bar, up to 8 beats) while the right punches every beat, low/high |
| PowerJump | two Kicks + two PunchHighs together every other beat: a jump with both fists up |

Levels: `swamp` (easy - steps, single punches, half-time sets, simple walls, two short circles),
`lab` (hard - JabKick both sides, StepReach, HighLow, Fan, Mirror, SideJump, Cross, Duck, Curb,
long circles before drops), `castle` (very hard - PowerJump, Chord, Jack, Shift, PunchKick,
one-bar JabKick side switches, ArcPunch, WallRun, Balance; 16 bars of drop phrases).

With both `--intro-arcs` and `--intro-melody-onsets`, the intro opens with ArcHold phrases and its
last `--intro-melody-bars` (8) follow the melody into the drums.

```powershell
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track8_1.wav --detector grid --pattern halloween2-swamp --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge --intro-melody-onsets --intro-rest 7
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track8_2.wav --detector grid --pattern halloween2-lab --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge --intro-melody-onsets --intro-arcs
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track8_3.wav --detector grid --pattern halloween2-castle --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --fill-gaps-dodge --intro-melody-onsets --intro-arcs
```

## Video 9: `--competitor-style --difficulty easy|medium|hard`

The competitor's system (Immersive Dance Mode, see Documents/Анализ_конкурента_DanceMode.md) as three
plans, `competitor-easy|medium|hard`, chosen by the two flags. Footprints (steps, single and in
pairs), fists (alternating, in pairs, two heights), the rays (Cross: both fists high and out over two
strips apart - the star) on the first bar of every drop and as the level's last move (`finale`), the
held squat on breaks, walls, rails, long strips. Phrases of 8 bars, steps -> punches -> a big move;
quiet stretches get the big slow moves. `limits` caps ArcHold at 1-2 a level. The plan starts on the
`--intro-rest` bar, so the first action comes at ~5 s and a held pose there is not cut.

New JSON types (held poses, duration required): `Rails` (rails over the two outer lanes - stand
narrow) and `SquatHold` (floor lasers and a bar over the road on every beat - a wide squat, pulsing).
New sets: StepPair, PunchPair, SpeedPunch / SpeedStep (on the beat and the eighth after it), Squat,
RailsSet, Lunge (a strip held under one foot, the other pulsing every beat), Wide (strips apart,
then feet together).

Measured (distinct moments a second, a squat's bars counted once a beat): track9_1 easy 1.35,
track9_2 medium 1.74, track9_3 hard 2.39; every note on the eighth grid.

```powershell
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track9_1.wav --detector grid --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 2 --competitor-style --difficulty easy --intro-rest 5
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track9_2.wav --detector grid --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 1 --competitor-style --difficulty medium --intro-rest 5
.venv\Scripts\python generate_beatmap.py ..\..\Assets\Audio\Tracks\track9_3.wav --detector grid --rest-beats 1 --spacing-tolerance 0.05 --grid-quiet-every 1 --competitor-style --difficulty hard --intro-rest 5
```
