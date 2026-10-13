#!/usr/bin/env python3
"""Generate a Unity beatmap JSON from an audio file using librosa onset analysis.

The output format is the one `Assets/Scripts/Beatmap/Beatmap.cs` declares and
`BeatmapReader.TryParse` validates. That reader is strict on purpose, so this generator
has to respect four invariants or the map is rejected at load with a message naming the
offending event index:

  * `version` must equal `BeatmapReader.SupportedVersion` (1).
  * `type` is matched case-sensitively against the C# `CueType` enum:
    Kick | Punch | KickHold | PunchHold | ArcHold | Dodge | Curb | Duck. (`Jump` is retired: the
    reader still accepts the name and loads it as `Curb`, but nothing here writes one.)
  * a hold type must carry `duration > 0`, and a one-shot type must carry exactly `0`.
    "Absent" is not an option - JsonUtility fills a missing field with 0, which is right
    for one-shots and fatal for holds, so holds are always written explicitly.
  * `lane` must sit in `0 .. laneCount-1`, where laneCount comes from
    NeonTunnelGenerator (4 at the time of writing). `--lanes` must match it.

Run `--help` for the full flag list; README.md next to this file has the recipes.
"""

from __future__ import annotations

import argparse
import json
import os
import random
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Dict, List, Optional, Sequence, Tuple

import numpy as np
from scipy.signal import butter, sosfiltfilt

try:
    import librosa
except ImportError:  # pragma: no cover - environment problem, not a code path
    sys.exit(
        "librosa is not installed. From Tools/beatmap:\n"
        "    python -m venv .venv\n"
        "    .venv\\Scripts\\python -m pip install -r requirements.txt"
    )


# --------------------------------------------------------------------------------------
# Format constants - these mirror the C# side and must not drift from it.
# --------------------------------------------------------------------------------------

BEATMAP_VERSION = 1                    # BeatmapReader.SupportedVersion
GENERATOR = "Tools/beatmap/generate_beatmap.py (librosa)"

KICK, PUNCH, JUMP = "Kick", "Punch", "Jump"

HOLD_TYPE_OF = {KICK: "KickHold", PUNCH: "PunchHold"}   # Jump is never sustained
# The sweep: a sustained punch whose tail runs from the vanishing point up and out to one side,
# and the player's raised fist travels out along it. A hold in every sense the format cares
# about (it carries a duration), but NOT the sustained form of any one-shot, so it is outside
# HOLD_TYPE_OF - a sweep trimmed to nothing degrades to a plain Punch.
SWEEP = "PunchSweep"
HOLD_TYPES = set(HOLD_TYPE_OF.values()) | {SWEEP}

# A chord is a SET exercise, never a JSON type: every note of a Chord set is written out as two
# Kicks on the inner lanes at the same moment (a jump landing on both feet), and every
# --chord-punch-every'th of them also gets a Punch on top (the fist thrown on the landing).
# `expand_chords` does the translation after lanes are settled, so nothing above it needs to know.
CHORD = "Chord"

# Video 3, pass 2: the variety exercises. Every one of these is a SET exercise that
# `expand_compound` writes out as ordinary notes; three are also JSON types of their own.
PUNCH_HIGH = "PunchHigh"     # JSON type: a punch at overhead height
CURB_GAP = "CurbGap"         # JSON type, obstacle with a duration: a kerb over every lane but `lane`
CURB_SPLIT = "CurbSplit"     # JSON type, obstacle with a duration: a kerb over the two inner lanes
CROSS = "Cross"              # two high PunchHolds + two KickHolds on the outer lanes: the star
WALL_RUN = "WallRun"         # a long Dodge wall down one side, notes on the free side
SIDE_JUMP = "SideJump"       # both feet: lanes 0+1, then 2+3, alternating on the beats
HIGH_LOW = "HighLow"         # punches on two levels, alternating hands, sometimes both at once
JACK = "Jack"                # both feet: outer 0+3, inner 1+2, alternating (jumping jacks)
SHIFT = "Shift"              # both feet: a pair sliding one lane per beat, 0+1 -> 1+2 -> 2+3 -> back
BALANCE = "Balance"          # a CurbGap or CurbSplit held for the set: one leg, or a wide stance
# Video 7 ("Neon Carnival"):
ARC_HOLD = "ArcHold"         # JSON type: a PunchHold bent into a circle (or a spiral of several turns).
                             # Carries `arc` (degrees, signed, + clockwise as the player sees it) and
                             # `radius` (metres, 0 = the game's default); the lane's half is the hand.
ARC = "Arc"                  # set: one mirrored pair of ArcHolds (both hands) on the set's first beat,
                             # then light steps for the rest of the set - a breather at a phrase change
FAN = "Fan"                  # set: the punch fan - mirrored Punch/PunchHigh pairs thrown out like rays
                             # on the even beats, a step (Kick) left/right on the odd ones: a dance
STEP_TAP = "StepTap"         # set: step left, jab right, step right, jab left - one limb per beat (light)
MIRROR = "Mirror"            # set: both hands at once on every beat, low and high in turn (a dance pair)
PUNCH_KICK = "PunchKick"     # set: a punch and the opposite foot together on every beat (dense combo)
# Video 8 (Halloween 2). Each has a LEAD side - the left as written; a plan entry marked "R" mirrors
# the whole set onto the other side (mirror_sets), so a motif can be repeated to the right.
STEP_TOUCH = "StepTouch"     # set: step-touch, one foot every other beat, L/R (half time - the warm-up)
JAB_CROSS = "JabCross"       # set: one punch every other beat, L/R (half time)
JAB_KICK = "JabKick"         # set: jab (lead), cross (rear), jab (lead), kick with the rear foot - the 1-2-1-kick
STEP_REACH = "StepReach"     # set: step lead foot + reach up with the rear hand, then the other side (contralateral)
ARC_PUNCH = "ArcPunch"       # set: the lead hand circles (one ArcHold) while the rear hand punches every beat
POWER_JUMP = "PowerJump"     # set: a two-foot jump with both fists overhead, every other beat
# Video 17 - new moves (the user: "three or four videos with about the same buttons"):
STAR_JUMP = "StarJump"       # set: star (feet 0+3, both fists up 0+3), then tuck (feet 1+2), in turn
SKATER = "Skater"            # set: skater bounds - one foot to an outer lane, the opposite fist across
WINDMILL = "Windmill"        # set: a high punch to an outer lane, the opposite foot steps in, side to side
LADDER = "Ladder"            # set: punch ladder - low, low, high, high, both high
HOPSCOTCH = "Hopscotch"      # set: both feet in the middle, one out to the side, both, the other side
KNEE_CLINCH = "KneeClinch"   # set: both fists up (the clinch) and a knee held a beat, sides in turn
CROSS_JACK = "CrossJack"     # set: a side jump with the far fist reaching up the other way
# Video 18 - Latin dance-fitness moves (the music went guaracha / latin house):
CUMBIA = "Cumbia"            # set: step out wide with the opposite fist high, step back in, the other side
MERENGUE = "Merengue"        # set: marching feet on every beat, a double punch on the fourth
SALSA = "Salsa"              # set: salsa basic - three steps (L R L) and a high reach on four, then mirrored
SAMBA = "Samba"              # set: a two-foot bounce, then both arms open wide (outer fists), in turn
MARACAS = "Maracas"          # set: shake the maracas - high and low fists on the diagonals, every beat
TORERO = "Torero"            # set: a long lunge out to an outer lane (held) with the far fist reaching high - ole
# Video 9 (the competitor's vocabulary, --competitor-style). Two new JSON types, both held poses
# with a duration: Rails (rails over the two outer lanes - stand narrow) and SquatHold (floor
# lasers and a bar over the road every beat - a wide squat, pulsing).
RAILS_TYPE = "Rails"
SQUAT_HOLD_TYPE = "SquatHold"
STEP_PAIR = "StepPair"       # set: both feet together on the inner lanes every other beat (step together / small hop)
PUNCH_PAIR = "PunchPair"     # set: the double punch - both fists together every other beat, low and high in turn
SPEED_PUNCH = "SpeedPunch"   # set: punches on every beat AND the eighth after it, hands alternating
SPEED_STEP = "SpeedStep"     # set: fast feet - a step on every beat and every eighth, feet alternating
SQUAT = "Squat"              # set: one SquatHold for the whole set (4-8 s): a wide squat held, pulsing on the beat
RAILS = "RailsSet"           # set: Rails for the set, a punch every other beat above them
LUNGE = "Lunge"              # set: a long strip under the left foot held out (KickHold, lane 0), the right foot tapping every other beat
WIDE = "Wide"                # set: two strips apart (feet wide, 1.5 beats), then both feet together - a slow jack of the legs
# Video 10 (reworked): sets built around a held pose with work on top - holds that are trained, not waited out.
HOLD_JAB = "HoldJab"         # set: one fist held out (PunchHold) for 4 beats while the other punches low/high, then sides swap
LEG_HOLD_PUNCH = "LegHoldPunch"  # set: one leg out on a strip (KickHold) for 4 beats, both fists punching on top, then the other leg
SQUAT_PUNCH = "SquatPunch"   # set: the squat held for the set (SquatHold) with a punch on every beat, hands alternating
RAILS_HIGH = "RailsHigh"     # set: rails (stand narrow) held for the set, uppercut-height punches on every beat
PRESS_HOLD = "PressHold"     # set: both fists pressed out and held 2 beats, then two steps - push, hold, step, step
HOLD_SETS_V10 = {HOLD_JAB, LEG_HOLD_PUNCH, SQUAT_PUNCH, RAILS_HIGH, PRESS_HOLD}
COMPOUND = {HOLD_JAB, LEG_HOLD_PUNCH, SQUAT_PUNCH, RAILS_HIGH, PRESS_HOLD, CROSS, WALL_RUN, SIDE_JUMP, HIGH_LOW, JACK, SHIFT, BALANCE, ARC, FAN, STEP_TAP, MIRROR, PUNCH_KICK,
            STEP_TOUCH, JAB_CROSS, JAB_KICK, STEP_REACH, ARC_PUNCH, POWER_JUMP,
            STAR_JUMP, SKATER, WINDMILL, LADDER, HOPSCOTCH, KNEE_CLINCH, CROSS_JACK,
            CUMBIA, MERENGUE, SALSA, SAMBA, MARACAS, TORERO,
            STEP_PAIR, PUNCH_PAIR, SPEED_PUNCH, SPEED_STEP, SQUAT, RAILS, LUNGE, WIDE}
# Types that may carry a duration in the JSON: the holds, a long Dodge wall, the balance kerbs.
DURATION_TYPES = {"KickHold", "PunchHold", "PunchSweep", "Dodge", CURB_GAP, CURB_SPLIT, ARC_HOLD, "Rails", "SquatHold"}

# The three obstacles. Unlike the note types these are not something to strike at the hit zone -
# they are pieces of road furniture to get around, over or under, and in the game the camera
# itself performs the movement. Two consequences for this file:
#
#   * they are never sustained, so they never take a duration (the reader rejects one that does);
#   * for a Dodge the `lane` is not a position but a SIDE. Lanes in the left half of the road put
#     the wall on the left and send the player right; the right half is the mirror. That is why
#     Dodge's default lane pair is the outer pair and why `resolve_hold_overlaps` must not nudge
#     an obstacle sideways the way it may nudge a note - a nudged Dodge would point the other way.
DODGE, CURB, DUCK = "Dodge", "Curb", "Duck"
OBSTACLE_TYPES = {DODGE, CURB, DUCK}

# JUMP is RETIRED as an exercise and survives only as an ACOUSTIC label. The classifier still
# calls a full-spectrum accent a Jump, because that is what it is in the audio and it is what
# decides which band the timing refinement looks in - but no note is ever written out as one.
# `Curb` replaced it on the game side: a kerb across the whole road asking for one honest jump,
# in place of a hurdle bar standing in a single lane. `retire_jump` below does the translation
# once, at the end, so the analysis code above it needs no special case.
EXERCISE_FOR_ACOUSTIC_JUMP = CURB


# --------------------------------------------------------------------------------------
# Analysis result
# --------------------------------------------------------------------------------------

@dataclass
class Event:
    """One candidate note. Carries analysis-only fields that never reach the JSON."""

    time: float
    type: str
    intensity: float
    lane: int = 0
    duration: float = 0.0
    frame: int = 0                                     # onset frame in the mel spectrogram
    bands: Tuple[float, float, float] = (0.0, 0.0, 0.0)

    # What the AUDIO says this onset is, as opposed to `type`, which in workout mode is what
    # the current set says the player should do. The two are deliberately separate: the
    # exercise is chosen by the workout structure, but the timing refinement still has to look
    # for the attack in the band the sound actually occupies. Refining a hi-hat in the low band
    # because the set happens to be a Kick set would put the note tens of milliseconds off.
    acoustic: str = PUNCH
    set_index: int = -1
    arc: float = 0.0       # ArcHold only: signed degrees, + clockwise as the player sees it
    radius: float = 0.0    # ArcHold only: metres, 0 = the game's default

    @property
    def is_hold(self) -> bool:
        return self.duration > 0.0


# --------------------------------------------------------------------------------------
# Spectral setup
# --------------------------------------------------------------------------------------

def mel_band_channels(sr: int, n_mels: int, kick_hz: float, punch_hz: float) -> List[int]:
    """Split the mel spectrogram into [low, mid, high] channel ranges by frequency.

    The boundaries are indices into MEL bins, not FFT bins: `onset_strength_multi` builds
    a mel spectrogram by default, and feeding it FFT-bin indices silently analyses the
    wrong frequencies (mel bins are packed towards the low end, so an FFT index meaning
    "150 Hz" lands somewhere in the low mids). The boundaries are clamped so the three
    ranges stay strictly increasing even at unusual sample rates or small `n_mels`.
    """
    mel_f = librosa.mel_frequencies(n_mels=n_mels, fmin=0.0, fmax=sr / 2.0)
    low = int(np.searchsorted(mel_f, kick_hz))
    mid = int(np.searchsorted(mel_f, punch_hz))
    low = max(1, min(low, n_mels - 2))
    mid = max(low + 1, min(mid, n_mels - 1))
    return [0, low, mid, n_mels]


def estimate_tempo(onset_env: np.ndarray, sr: int, hop_length: int) -> float:
    """BPM for the whole track.

    `librosa.beat.tempo` moved to `librosa.feature.rhythm.tempo` in librosa 0.10, was
    deprecated there, and is GONE in librosa 1.0 — so the new location is tried first and
    the old name only exists as a fallback for 0.9.

    The new location is reached through `librosa.feature.tempo`, its re-export, rather than
    through `librosa.feature.rhythm`: `librosa.feature` is a lazy_loader module, so the
    `rhythm` submodule is not an attribute of it until something imports it explicitly, and
    `getattr(librosa.feature, "rhythm", None)` quietly returns None on a fresh interpreter.
    The re-export is a plain function object and is always there.

    Either way the result is an array (one estimate per aggregation window); with the
    default aggregation it holds a single value.
    """
    tempo_fn = getattr(librosa.feature, "tempo", None) or getattr(librosa.beat, "tempo", None)
    if tempo_fn is None:  # pragma: no cover - only on an unexpected librosa layout
        raise SystemExit(
            "this librosa exposes tempo estimation at neither librosa.feature.tempo nor "
            "librosa.beat.tempo — check the installed version against requirements.txt"
        )
    value = tempo_fn(onset_envelope=onset_env, sr=sr, hop_length=hop_length)
    return round(float(np.atleast_1d(value)[0]), 2)


def tempo_from_beats(beat_times: np.ndarray) -> Optional[float]:
    """A first BPM estimate from tracked beat positions, used to seed the comb fit.

    Fitting a straight line through (beat index, beat time) uses the whole track as the
    baseline, which is already far better than the tempogram - but not good enough to use on its
    own. The tracker's beat list contains the occasional inserted or missed beat, and the fit
    inherits them: on this project's techno track it landed on 139.56 BPM where the truth is
    139.99, a 0.3% error that walks the grid 0.64 s over three minutes. `fit_beat_comb` takes it
    from here; this only has to be close enough to keep that search narrow.

    Beat indices are recovered by rounding against the median interval first, which keeps one
    missed beat from shifting every index after it.
    """
    if len(beat_times) < 8:
        return None

    spacing = float(np.median(np.diff(beat_times)))
    if not (0.05 < spacing < 4.0):
        return None

    index = np.round((beat_times - beat_times[0]) / spacing)
    design = np.vstack([index, np.ones_like(index)]).T
    (period, _), *_ = np.linalg.lstsq(design, beat_times, rcond=None)
    if not (0.05 < period < 4.0):
        return None
    return round(60.0 / float(period), 3)


def fit_beat_comb(env: np.ndarray, rate: float, period_guess: float, duration: float,
                  span: float = 0.04, steps: int = 800) -> Tuple[float, float, float]:
    """Fit a regular beat comb to the onset envelope over the WHOLE track.

    Returns (period, phase, score). This is what finally pins the tempo down, and it is worth
    understanding why the obvious alternatives do not:

      * `librosa.feature.tempo` reads a tempogram and is quantised to its bins. On this
        project's techno track it answered 143.55 BPM in all 19 overlapping windows - perfectly
        consistent, and wrong by 2.5%.
      * The beat tracker follows the audio well but its output contains the occasional inserted
        or missed beat, so a straight fit through its beat times inherits those and landed on
        139.56 where the truth is 139.99. Over 207 s that 0.3% walks the grid 0.64 s - a beat
        and a half - which is exactly the drift a workout cannot have.

    A comb spanning the whole track has the track's full length as its baseline, so a period
    wrong by a tenth of a percent displaces the last tooth by 200 ms and the score collapses.
    On the techno track this scored 0.9932 at the true period against 0.1516 at the tempogram's
    answer. The tracker is still used - to seed `period_guess`, which is what keeps the search
    narrow enough to stay cheap.
    """
    best = (period_guess, 0.0, -1.0)
    limit = len(env) - 1
    for period in np.linspace(period_guess * (1 - span), period_guess * (1 + span), steps):
        teeth = int(duration / period)
        if teeth < 8:
            continue
        offsets = np.arange(teeth) * period
        phases = np.arange(0.0, period, 1.0 / rate)
        index = np.rint((phases[:, None] + offsets[None, :]) * rate).astype(int)
        np.clip(index, 0, limit, out=index)
        scores = env[index].mean(axis=1)
        top = int(np.argmax(scores))
        if scores[top] > best[2]:
            best = (float(period), float(phases[top]), float(scores[top]))
    return best


def robust_reference(x: np.ndarray, percentile: float) -> float:
    """A normalisation reference that a single freak transient cannot dominate.

    Dividing by the maximum would push an entire quiet track towards zero intensity the
    moment one clipped sample or one cymbal crash sets the ceiling, so a high percentile
    is used instead; anything above it simply clips to 1.
    """
    ref = float(np.percentile(x, percentile)) if x.size else 0.0
    if ref <= 1e-9:
        ref = float(np.max(x)) if x.size else 0.0
    return ref if ref > 1e-9 else 1.0


# --------------------------------------------------------------------------------------
# Classification
# --------------------------------------------------------------------------------------

def classify(lo: float, mid: float, hi: float, strength: float, cfg) -> str:
    """Pick Kick / Punch / Jump from the per-band onset strengths at one onset.

    The bands carry onset STRENGTH (spectral flux), not raw energy. That distinction
    matters: a track with a continuous bass drone has huge low-band energy in every frame,
    so an energy-based rule would label every hi-hat a Kick. Flux only rises when
    something in that band actually starts.

    Order matters - Jump is tested first because a full-spectrum accent is also, by
    definition, bass-heavy, and the Kick rule would otherwise swallow it.

    A Kick is recognised two ways, and the second one is not redundant:

      * RELATIVE - the low band dominates the others. Right on sparse or well-separated
        material, where a kick really is the only thing sounding at that instant.
      * ABSOLUTE - the low band is lit in its own terms (`--kick-level`), whatever else is
        playing. This is the rule that makes dense productions work. In techno the kick
        lands on a hi-hat or a clap almost every time, so mid and high are loud at exactly
        the moments the kick is; measured on the 143.55 BPM techno track in this project the
        relative rule alone found 44 kicks where the low band's own onsets say there are
        roughly 430, and the chart came out as a near-unbroken row of Punches on a
        four-on-the-floor record.
    """
    if (lo >= cfg.jump_band and mid >= cfg.jump_band and hi >= cfg.jump_band
            and strength >= cfg.jump_strength):
        return JUMP
    if lo >= cfg.kick_level or lo >= cfg.kick_dominance * max(mid, hi):
        return KICK
    return PUNCH


# --------------------------------------------------------------------------------------
# Detection: which moments get a note at all
# --------------------------------------------------------------------------------------
#
# Two detectors, chosen by --detector. `drums` (the default) puts notes on the kick and on the
# clap/snare and nothing else. `all` is the original: every onset in the loudest band, which on a
# dance track means the hats and the shakers too, and a chart whose rhythm cannot be read.

def drum_envelopes(y: np.ndarray, sr: int, cfg) -> Tuple[np.ndarray, np.ndarray, float]:
    """(kick envelope, clap envelope, envelope rate). This is the whole rule for what counts as a
    kick and what counts as a clap, and the function to change if either picks the wrong things.

    Both are AMPLITUDE envelopes of band-limited copies of the signal, each normalised so its 99th
    percentile is 1 - not the spectral flux the `all` detector uses. The difference is what makes
    the kick findable under a bass line. Flux is computed on decibels, which inflate small changes:
    a bass line wobbling between 20% and 50% of the low band's level reads as a stream of onsets
    as strong as the kick's. In amplitude the kick is a jump to the top of the band (~1.0 from
    ~0.2) and the bass stays underneath it, so a height threshold separates them.

      * kick: the band below --kick-hz.
      * clap / snare: the geometric mean of the mid band and the band above --punch-hz. A clap
        has a body in the mids and a noise burst in the highs, so both jump at once; a hi-hat
        lives in the highs and a melody in the mids, and the geometric mean of a large and a small
        number is small, which a sum or a max would not be.

    The windows are the ones the timing refinement was calibrated with, so the attack read off
    these envelopes is already as precise as that pass would make it.
    """
    hop = max(1, int(round(cfg.refine_hop * sr)))

    def band(low: float, high: float, window: float) -> np.ndarray:
        env = rms_envelope(band_filter(y, sr, low, high), max(2, int(round(window * sr))), hop)
        return env / robust_reference(env, cfg.norm_percentile)

    kick = band(0.0, cfg.kick_hz, cfg.refine_low_window)
    clap = np.sqrt(band(cfg.kick_hz, cfg.punch_hz, cfg.refine_high_window)
                   * band(cfg.punch_hz, sr / 2.0, cfg.refine_high_window))
    clap = clap / robust_reference(clap, cfg.norm_percentile)
    return kick, clap, sr / hop


def drums_play_in_bar(kicks_in_bar: int, cfg) -> bool:
    """Whether a bar counts as one where the drums play, and so gets a note on every beat.

    The one rule the grid detector's shape hangs on, kept apart so it can be changed alone. A
    kick COUNT rather than "a kick on every beat", because parts of a track can carry a
    syncopated pattern (1, 2, 3, the "a" of 3, the "and" of 4) that is plainly a drum bar while
    leaving a beat or two without a kick of its own; and a count of at least two, so one stray
    low hit in a breakdown does not light up a whole bar.
    """
    return kicks_in_bar >= cfg.grid_min_kicks


def detect_grid(y: np.ndarray, sr: int, mel_hop: int, low_onsets: np.ndarray,
                beat_period: float, beat_phase: float, cfg) -> Tuple[List[Event], List[str]]:
    """Notes on every beat of the fitted grid, in the bars where the drums play, and nowhere else.

    The grid decides WHEN - which is what makes the chart regular enough to follow as a workout.
    The kick decides WHERE: a bar gets its beats only if `drums_play_in_bar` says so, so
    breakdowns and quiet stretches come out as rests. Returns the events and a one-character map
    of the bars ('#' drums, '.' none) for the report.

    The notes sit on the grid shifted by the MEDIAN offset between the detected kicks and their
    nearest beats. The comb that fits the grid reads spectral flux, which peaks a little before
    the kick's attack; measured per track rather than assumed, so it is a property of the audio
    and not a constant someone has to maintain.
    """
    duration = len(y) / sr
    kicks = [ev for ev in detect_drums(y, sr, mel_hop, cfg, claps=False)]
    kick_times = np.array([ev.time for ev in kicks])
    kick_level = {round(ev.time, 4): ev.intensity for ev in kicks}

    grid = np.arange(beat_phase, duration, beat_period)
    grid_frames = np.rint(grid * sr / mel_hop).astype(int)
    bar_starts = find_bar_lines(grid, low_onsets, grid_frames, cfg.beats_per_bar)

    offset = 0.0
    if kick_times.size:
        nearest = grid[np.clip(np.searchsorted(grid, kick_times), 1, len(grid) - 1) - 1]
        delta = kick_times - nearest
        delta = np.where(delta > beat_period / 2, delta - beat_period, delta)
        close = np.abs(delta) <= beat_period / 4
        if close.any():
            offset = float(np.median(delta[close]))

    events: List[Event] = []
    bar_map: List[str] = []
    for index, start in enumerate(bar_starts):
        end = bar_starts[index + 1] if index + 1 < len(bar_starts) else start + cfg.beats_per_bar * beat_period
        inside = kick_times[(kick_times >= start + offset - beat_period / 4) & (kick_times < end + offset - beat_period / 4)]
        active = drums_play_in_bar(len(inside), cfg)
        quiet_every = getattr(cfg, "grid_quiet_every", 0.0)
        bar_map.append("#" if active else (":" if quiet_every > 0 else "."))
        if not active and quiet_every <= 0:
            continue
        beats_here = grid[(grid >= start - 1e-6) & (grid < end - 1e-6)]
        if not active:
            # --grid-quiet-every: a breakdown still carries a note every N beats, at a lower
            # intensity, so a "maximum load" chart does not stop for the whole of it.
            step = max(1, int(round(quiet_every)))
            for n, beat in enumerate(beats_here):
                if n % step: continue
                time = float(beat + offset)
                if 0.0 <= time < duration:
                    events.append(Event(time=time, type=KICK, acoustic=KICK, intensity=0.45,
                                        frame=int(round(time * sr / mel_hop))))
            continue
        for beat in beats_here:
            time = float(beat + offset)
            if time < 0.0 or time >= duration:
                continue
            # Intensity from the kick on this beat when there is one; a beat the pattern leaves
            # empty borrows the bar's median so it reads as part of the same bar, not as quiet.
            near = inside[np.abs(inside - time) <= beat_period / 4]
            level = (kick_level.get(round(float(near[0]), 4), 1.0) if near.size
                     else float(np.median([kick_level.get(round(float(k), 4), 1.0) for k in inside])))
            events.append(Event(time=time, type=KICK, acoustic=KICK, intensity=level,
                                frame=int(round(time * sr / mel_hop))))
    return events, bar_map


def detect_drums(y: np.ndarray, sr: int, mel_hop: int, cfg, claps: bool = True) -> List[Event]:
    """Notes on the kick and the clap/snare only, each found in its own band against its own threshold.

    A hit is an envelope peak at least --kick-threshold / --clap-threshold high that also stands
    --drum-prominence above the valleys around it, no closer than --min-interval to the next. Its
    time is the attack in front of the peak, read with the same rule as the refinement pass.

    A kick and a clap landing together come out as two events a few ms apart; `filter_min_interval`
    downstream keeps the stronger. `acoustic` records which drum it was - the note's TYPE is still
    decided by the workout set, as for every other note.
    """
    from scipy.signal import find_peaks

    kick_env, clap_env, rate = drum_envelopes(y, sr, cfg)
    sources = [(KICK, kick_env, cfg.kick_threshold, cfg.refine_low_rise)]
    if claps and not cfg.no_claps:
        sources.append((PUNCH, clap_env, cfg.clap_threshold, cfg.refine_rise))

    events: List[Event] = []
    for source, env, threshold, rise in sources:
        peaks, props = find_peaks(env, height=threshold, prominence=cfg.drum_prominence,
                                  distance=max(1, int(round(cfg.min_interval * rate))))
        for peak, height in zip(peaks, props["peak_heights"]):
            # A short search window: the peak is already known, this only walks back to its attack.
            attack = refine_onset(env, rate, peak / rate, 0.02, rise)
            time = attack if attack is not None else peak / rate
            events.append(Event(
                time=float(time), type=source, acoustic=source,
                intensity=float(np.clip(height, 0.0, 1.0)),
                frame=int(round(time * sr / mel_hop)),
                bands=(float(kick_env[peak]), float(clap_env[peak]), 0.0),
            ))
    events.sort(key=lambda e: e.time)
    return events


def detect_all_onsets(detect_env: np.ndarray, norm_bands: np.ndarray, sr: int, hop: int,
                      wait_frames: int, last_frame: int, cfg) -> List[Event]:
    """The original detector: every onset of the loudest band, typed by `classify`."""
    onset_frames = librosa.onset.onset_detect(
        onset_envelope=detect_env, sr=sr, hop_length=hop,
        units="frames", backtrack=False,
        delta=cfg.onset_delta, wait=wait_frames,
    )
    if len(onset_frames) == 0:
        raise SystemExit(
            "no onsets detected - try lowering --onset-delta, or check the file is not silence"
        )
    onset_times = librosa.frames_to_time(onset_frames, sr=sr, hop_length=hop)

    # Intensity is read off the SAME loudest-band envelope that detection uses, not off the
    # sum of the three bands. Summing looks like the natural "how big is this hit" measure
    # and quietly breaks the chart: a hi-hat lights all three bands and scores ~3x its
    # per-band value, a kick lights one and scores ~1x, so on any track with hats the kicks
    # come out as the weakest events in the map. Referenced against the onsets themselves
    # rather than every frame, so the silence between notes does not push the chart to 1.
    strength_ref = robust_reference(detect_env[onset_frames], cfg.norm_percentile)

    events: List[Event] = []
    for frame, time in zip(onset_frames, onset_times):
        # Peak over a couple of frames: the flux peak and the onset frame do not always
        # coincide, and a single-frame reading makes classification jittery.
        window = slice(int(frame), min(last_frame, int(frame) + cfg.peak_frames) + 1)
        lo = float(np.max(norm_bands[0][window]))
        mid = float(np.max(norm_bands[1][window]))
        hi = float(np.max(norm_bands[2][window]))
        strength = float(np.max(detect_env[window])) / strength_ref

        sound = classify(lo, mid, hi, strength, cfg)
        events.append(Event(
            time=float(time),
            type=sound,             # provisional: workout mode overwrites this from the set
            acoustic=sound,         # what the audio is, kept for the refinement band choice
            intensity=float(np.clip(strength, 0.0, 1.0)),
            frame=int(frame),
            bands=(lo, mid, hi),
        ))
    return events


# --------------------------------------------------------------------------------------
# Per-band timing refinement
# --------------------------------------------------------------------------------------
#
# Detection finds WHICH moments matter; this finds WHEN they actually are. The two need
# different tools, and using one tool for both is what put a 32 ms systematic gap between
# Kick and Punch in the first charts.
#
# The cause: onset detection reads a mel spectrogram, whose window was 2048 samples - 93 ms
# at 22050 Hz. A window that long has to be long, because resolving a 60 Hz kick in the
# frequency domain requires it. But the window smears the attack across itself, and it does
# so DIFFERENTLY for a low-frequency body than for a broadband transient, so the flux peak
# sits a few frames later for one than for the other. Measured against the waveform, kicks
# came out 5 ms early and everything else 27 ms late. No single offset can fix a difference
# that depends on the note's type, which is exactly why it was heard as wandering rather
# than as lag: on a four-on-the-floor track the types alternate constantly, so the error
# swung back and forth by ~32 ms from note to note.
#
# The fix is to stop asking the spectrogram for timing. Timing needs AMPLITUDE resolution,
# not frequency resolution: band-limit the signal, take its envelope, and read the attack
# off that. Each band gets its own envelope window, because the requirements differ - a
# 60 Hz waveform needs a window spanning at least one cycle (~17 ms) to have a meaningful
# amplitude at all, while a hi-hat transient is over in 4 ms and a long window would blur it.
#
# The filters are applied with `sosfiltfilt`, which runs forwards and backwards. That is not
# a detail: an ordinary IIR filter delays the signal by its own group delay, and a delay is
# precisely what is being measured here, so a one-pass filter would inject the very error
# this pass exists to remove.


def band_filter(signal: np.ndarray, sr: int, low: float, high: float, order: int = 4) -> np.ndarray:
    """Zero-phase band-limited copy of the signal."""
    nyq = sr / 2.0
    high = min(high, nyq * 0.99)
    if low <= 0.0:
        sos = butter(order, high / nyq, btype="low", output="sos")
    elif high >= nyq * 0.98:
        sos = butter(order, low / nyq, btype="high", output="sos")
    else:
        sos = butter(order, [low / nyq, high / nyq], btype="band", output="sos")
    return sosfiltfilt(sos, signal)


def rms_envelope(signal: np.ndarray, window: int, hop: int) -> np.ndarray:
    """Moving-RMS envelope, computed from a cumulative sum so window length costs nothing."""
    window = max(2, window)
    squared = signal * signal
    running = np.concatenate([[0.0], np.cumsum(squared)])
    count = (len(signal) - window) // hop
    if count <= 0:
        return np.zeros(0)
    index = np.arange(count) * hop
    return np.sqrt(np.maximum(running[index + window] - running[index], 0.0) / window)


def refine_onset(env: np.ndarray, env_rate: float, t: float, search: float,
                 rise: float) -> Optional[float]:
    """The true attack time near `t`, or None when the envelope says nothing happened.

    Finds the envelope peak inside the search window, walks back to the valley that precedes
    it, and returns the moment the rise from valley to peak passes `rise` of its height,
    interpolated between envelope samples so the answer is not quantised to the hop.

    A fraction of the rise, rather than the peak or the steepest slope: the peak is late by
    the whole attack time and varies with how hard the note was struck, while the steepest
    slope drifts with the shape of the envelope. The point where the note first becomes
    clearly audible above what preceded it is both stable and close to what a listener calls
    the hit.
    """
    lo = max(0, int((t - search) * env_rate))
    hi = min(len(env) - 1, int((t + search) * env_rate))
    if hi - lo < 4:
        return None

    segment = env[lo:hi + 1]
    peak_index = int(np.argmax(segment))
    if peak_index == 0:
        return None

    valley_index = peak_index
    while valley_index > 0 and segment[valley_index - 1] <= segment[valley_index]:
        valley_index -= 1

    valley, peak = float(segment[valley_index]), float(segment[peak_index])
    if peak <= valley:
        return None

    threshold = valley + rise * (peak - valley)
    cross = valley_index
    while cross < peak_index and segment[cross] < threshold:
        cross += 1

    position = float(cross)
    if cross > valley_index and segment[cross] > segment[cross - 1]:
        step = (threshold - segment[cross - 1]) / (segment[cross] - segment[cross - 1])
        position = (cross - 1) + step

    return (lo + position) / env_rate


# --------------------------------------------------------------------------------------
# Hold detection
# --------------------------------------------------------------------------------------

def measure_hold(energy: np.ndarray, start_frame: int, limit_frame: int, cfg,
                 sr: int, hop_length: int) -> float:
    """How long the note sustains, in seconds, or 0.0 if it is a one-shot.

    The criterion - the part most open to being changed, see README:

      A note is a hold when the energy IN ITS OWN BAND stays above `--hold-decay` of that
      note's attack peak for at least `--hold-min` seconds, with no later event inside
      that span.

    Reading it band-locally is what makes it survive a real mix. Measured broadband, a
    sustained bass note looks like it stops the instant the hi-hats do, and a held pad
    looks sustained straight through the kick pattern underneath it. The peak is taken
    over a short attack window rather than at the onset frame itself, because the onset
    frame is where flux rises, which is a frame or two before amplitude actually peaks.

    `limit_frame` is the next event OF THE SAME FAMILY (the next Kick-ish event for a kick,
    the next Punch-ish event for a punch; a Jump bounds both, being a full-spectrum accent).
    Bounding by the next event of any kind is the obvious rule and it makes holds
    essentially unreachable: on any track with a hi-hat pattern there is another onset
    every 100-150 ms, so every sustain gets cut long before `--hold-min`. A held bass note
    is not ended by the hats playing over it, and the chart should not pretend it is.
    """
    attack_end = min(len(energy) - 1, start_frame + cfg.attack_frames)
    if attack_end <= start_frame:
        return 0.0

    peak = float(np.max(energy[start_frame:attack_end + 1]))
    if peak <= 1e-8:
        return 0.0

    # The note has to actually LIFT the band above what was already playing there.
    #
    # Without this, a dense mix reports holds almost everywhere: the mid band of a busy
    # arrangement never falls below 45% of anything, so every snare that happens to land
    # over sustained strings "sustains" for as long as the strings do, and the chart fills
    # with holds that belong to a different instrument. Comparing the attack against the
    # median of the band just before the onset asks the only question that matters - is
    # this sustain this note's doing, or was the band already loud?
    window_start = max(0, start_frame - cfg.attack_frames - 1)
    if start_frame > window_start:
        before = float(np.median(energy[window_start:start_frame]))
        if before > 1e-8 and peak < cfg.hold_lift * before:
            return 0.0

    threshold = peak * cfg.hold_decay
    ceiling = min(len(energy) - 1, start_frame + cfg.hold_max_frames, limit_frame)

    frame = attack_end
    while frame < ceiling and energy[frame + 1] >= threshold:
        frame += 1

    duration = float(librosa.frames_to_time(frame - start_frame, sr=sr, hop_length=hop_length))
    return duration if duration >= cfg.hold_min else 0.0


# --------------------------------------------------------------------------------------
# Workout structure: sets, rests, and which exercise each set is
# --------------------------------------------------------------------------------------
#
# Onsets decide WHEN a note happens. In workout mode they no longer decide WHAT it is.
#
# Typing each note by the frequency content of its own onset is correct as music analysis and
# wrong as exercise design: a bar of techno puts a kick, a hat and a clap inside 400 ms, so the
# chart asks for a leg, then an arm, then a jump, and nobody can follow that. A fitness routine
# holds one movement for long enough to get into it, then changes. So the track is cut into
# sets on the bar grid, each set is one exercise, and every note inside it is that exercise.
#
# Set boundaries are placed on BARS, not on round numbers of seconds, because a change of
# movement that lands mid-bar reads as a mistake even to someone who cannot name what is wrong.

ALL_EXERCISES = (PUNCH, KICK, "PunchHold", "KickHold", SWEEP, CHORD, DODGE, CURB, DUCK,
                 CROSS, WALL_RUN, SIDE_JUMP, HIGH_LOW, JACK, SHIFT, BALANCE, ARC, FAN,
                 STEP_TAP, MIRROR, PUNCH_KICK, STEP_TOUCH, JAB_CROSS, JAB_KICK, STEP_REACH, ARC_PUNCH,
                 POWER_JUMP, STEP_PAIR, PUNCH_PAIR, SPEED_PUNCH, SPEED_STEP, SQUAT, RAILS, LUNGE, WIDE,
                 HOLD_JAB, LEG_HOLD_PUNCH, SQUAT_PUNCH, RAILS_HIGH, PRESS_HOLD,
                 STAR_JUMP, SKATER, WINDMILL, LADDER, HOPSCOTCH, KNEE_CLINCH, CROSS_JACK,
                 CUMBIA, MERENGUE, SALSA, SAMBA, MARACAS, TORERO)

# Named set orders. A pattern is a list of (exercise, how many sets in a row), cycled until the
# track runs out. This is the one place the shape of the workout lives: changing the routine
# means editing this table or passing --pattern, and nothing else in the file needs to know.
SET_PATTERNS: Dict[str, List[Tuple[str, int]]] = {
    # The straight version: one long block per exercise, in the order given, repeating.
    "classic": [(PUNCH, 6), (KICK, 5), (CURB, 4), ("PunchHold", 2)],
    # Default. Same spine as classic for the first half, then shorter and re-ordered blocks, so
    # a three-minute track does not play the identical cycle twice and the second half feels
    # like it is going somewhere.
    "workout": [(PUNCH, 6), (KICK, 5), (CURB, 4), ("PunchHold", 2),
                (PUNCH, 4), (KICK, 4), ("KickHold", 2), (CURB, 3), (PUNCH, 3), (KICK, 3)],
    # Default. A circuit: every set is a different exercise, so the movement changes on a fixed
    # cadence - one set - and the only knob for how often that happens is --set-bars.
    #
    # Every block is a single set on purpose. Doubling any of them would make that exercise run
    # twice as long as all the others, and the point of a circuit is that the cadence is
    # predictable: the person learns how long a block lasts and stops having to watch for the
    # change. Holds are spaced through it as the breathers - they are the one exercise that is a
    # rest from the others rather than an addition to them.
    #
    # Balanced at 4 Punch, 4 Kick, 3 Jump, 2 PunchHold, 1 KickHold over 14 sets.
    "circuit": [(PUNCH, 1), (KICK, 1), (CURB, 1), (PUNCH, 1), ("KickHold", 1),
                (KICK, 1), (PUNCH, 1), (CURB, 1), ("PunchHold", 1),
                (KICK, 1), (PUNCH, 1), (CURB, 1), (KICK, 1), ("PunchHold", 1)],
    # Short blocks, arms and legs traded back and forth - a harder, more switch-heavy routine.
    "intervals": [(PUNCH, 2), (KICK, 2), (CURB, 1), (PUNCH, 2), (KICK, 2), ("PunchHold", 1)],
    "arms": [(PUNCH, 8), ("PunchHold", 2)],
    "legs": [(KICK, 8), ("KickHold", 2), (CURB, 3)],
    # Obstacle sets alternate with ordinary striking sets rather than running back to back. Two
    # reasons, and the second is the one that actually forced it: an obstacle is a whole-body
    # movement and stacking them is exhausting in a way punches are not; and the camera performs
    # the movement, so a routine that is nothing but obstacles is a routine in which the camera
    # never sits still, which is unpleasant to watch and much worse to record.
    "obstacles": [(DODGE, 1), (PUNCH, 1), (CURB, 1), (KICK, 1), (DUCK, 1), (PUNCH, 1)],
    # The default circuit with the obstacles folded in - same cadence, same balance of arms and
    # legs, with one obstacle set roughly every third block.
    "circuit_obstacles": [(PUNCH, 1), (KICK, 1), (DODGE, 1), (PUNCH, 1), ("KickHold", 1),
                          (KICK, 1), (CURB, 1), (PUNCH, 1), ("PunchHold", 1),
                          (KICK, 1), (DUCK, 1), (PUNCH, 1), (CURB, 1), (KICK, 1), ("PunchHold", 1)],
    # Cardio for the Halloween playlist, run with --set-bars 2 on the grid detector: short blocks
    # that change every two bars, a note on every beat, the two-foot jump chords and the arm
    # sweeps folded in, one barrier to duck under about every sixteen sets and a wall set about
    # as often. Arms and legs still alternate block by block, so every change is a switch of limb
    # rather than of everything at once.
    "intense": [(PUNCH, 1), (KICK, 1), (CHORD, 1), (PUNCH, 1), (SWEEP, 1), (KICK, 1), (DUCK, 1),
                (PUNCH, 1), (CHORD, 1), (KICK, 1), ("PunchHold", 1), (PUNCH, 1), (SWEEP, 1),
                (KICK, 1), (CHORD, 1), (DODGE, 1), (PUNCH, 1), ("KickHold", 1)],
}

# How close together notes of each exercise may be, in BEATS rather than seconds, so a routine
# keeps its shape at any tempo. These are floors, not grids: a Punch set still follows the
# music, it just never asks for two punches inside half a beat.
#
# The holds and Jump matter most. Left to the onset density of a techno track, a PunchHold set
# would ask for a new sustained note five times a second, which is not a hold in any sense a
# person could act on, and a Jump set would ask for four jumps a second.
DEFAULT_SPACING_BEATS: Dict[str, float] = {
    PUNCH: 0.5, KICK: 0.5, "PunchHold": 2.0, "KickHold": 2.0,
    # A jump every other beat is the most a person keeps up for a whole block; a sweep needs the
    # arm back at the centre before the next one, so it too takes every other beat.
    CHORD: 2.0, SWEEP: 2.0,
    # The variety set: jumps and punches on every beat, the star once a bar, the long wall's and
    # the balance kerb's single note at the set's start (spacing = the whole set).
    SIDE_JUMP: 1.0, JACK: 1.0, SHIFT: 1.0, HIGH_LOW: 1.0, CROSS: 4.0, WALL_RUN: 1.0, BALANCE: 64.0,
    # Video 7: the fan moves on every beat (punch pair, step, punch pair, step); an Arc set's notes
    # are written from its own bar lines by expand_compound, so its spacing only thins the grid.
    FAN: 1.0, ARC: 1.0, STEP_TAP: 1.0, MIRROR: 1.0, PUNCH_KICK: 1.0,
    # Video 8: the half-time warm-up moves and the power jump take every other beat.
    STEP_TOUCH: 2.0, JAB_CROSS: 2.0, JAB_KICK: 1.0, STEP_REACH: 1.0, ARC_PUNCH: 1.0, POWER_JUMP: 2.0,
    # Video 9: pairs every other beat; the held poses take their notes from the set's own beats.
    STEP_PAIR: 1.0, PUNCH_PAIR: 1.0, SPEED_PUNCH: 1.0, SPEED_STEP: 1.0, SQUAT: 1.0, RAILS: 1.0, LUNGE: 1.0, WIDE: 1.0,
    # Video 10: the held-pose sets read every beat; the hold itself is written from the set's beats.
    HOLD_JAB: 1.0, LEG_HOLD_PUNCH: 1.0, SQUAT_PUNCH: 1.0, RAILS_HIGH: 1.0, PRESS_HOLD: 1.0,
    # Video 17: the new moves read every beat (the knee clinch every other).
    STAR_JUMP: 1.0, SKATER: 1.0, WINDMILL: 1.0, LADDER: 1.0, HOPSCOTCH: 1.0, KNEE_CLINCH: 2.0, CROSS_JACK: 1.0,
    CUMBIA: 1.0, MERENGUE: 1.0, SALSA: 1.0, SAMBA: 1.0, MARACAS: 1.0, TORERO: 2.0,
    # Obstacles are spaced far wider than any note, and the number is not a taste judgement: the
    # camera performs the movement and has to be back at centre before the next one starts. The
    # longest reaction is the dodge, whose lead-in, hold and return together run 1.59 s - so at
    # 8 beats (3.43 s at 140 BPM) the camera sits still at centre for 1.84 s before the next
    # obstacle asks it to move. 4 beats (1.71 s) is the floor if a denser routine is wanted; it
    # still clears, but with only 0.1 s of rest. See also --obstacle-gap, which enforces the same
    # floor ACROSS sets, where per-exercise spacing cannot reach.
    DODGE: 8.0, CURB: 8.0, DUCK: 8.0,
}

# Which two lanes an exercise alternates between. Adjacent and symmetric about the centre, so
# the alternation reads as left limb / right limb rather than as a position on the road.
DEFAULT_LANE_PAIRS: Dict[str, Tuple[int, int]] = {
    PUNCH: (1, 2), KICK: (1, 2), "PunchHold": (1, 2), "KickHold": (1, 2),
    # A sweep's lane is the side its tail runs out to, so it takes the outer pair: left arm out
    # to the left, then right arm out to the right. A chord's lane is rewritten by expand_chords;
    # the pair here is where its optional Punch goes.
    SWEEP: (0, 3), CHORD: (1, 2),
    # Dodge reads its lane as a side, so it takes the OUTER pair: the walls then alternate left
    # and right within a set and the player is moved both ways rather than leaning one way all
    # block. Curb and Duck span the whole road and ignore their lane entirely; they keep the
    # centre pair only so the field carries something the reader will accept.
    DODGE: (0, 3), CURB: (1, 2), DUCK: (1, 2),
}


@dataclass
class WorkoutSet:
    """One block of the routine: a run of bars doing one exercise, then a rest."""

    index: int
    exercise: str
    start: float          # first beat of the set
    end: float            # where the active part stops and the rest begins
    rest_end: float       # where the next set starts
    bar: int              # index of the first bar, for reporting
    notes: int = 0
    mirror: bool = False  # lead with the right: every lane flipped, every circle turned the other way


def find_bar_lines(beat_times: np.ndarray, low_onsets: np.ndarray, beat_frames: np.ndarray,
                   beats_per_bar: int) -> np.ndarray:
    """Beat times that start a bar.

    librosa tracks beats but not downbeats, so the phase - which of every four beats is beat
    one - has to be chosen. It is chosen by the low band: across almost all dance music the
    downbeat carries the heaviest low-frequency accent, so the phase whose beats sum to the most
    low-band onset strength is the one that starts the bars. It is a heuristic, and on material
    with no strong bass accent it will pick an arbitrary phase; the sets are still bar-aligned
    to each other in that case, they may simply all start on beat three.
    """
    if len(beat_times) < beats_per_bar * 2:
        return beat_times[:1] if len(beat_times) else np.zeros(0)

    best_phase, best_score = 0, -1.0
    for phase in range(beats_per_bar):
        frames = beat_frames[phase::beats_per_bar]
        frames = frames[frames < len(low_onsets)]
        score = float(low_onsets[frames].sum()) if frames.size else 0.0
        if score > best_score:
            best_score, best_phase = score, phase

    return beat_times[best_phase::beats_per_bar]


def parse_pattern(spec: str) -> List[Tuple[str, int]]:
    """A named pattern, or an inline one like 'Punch:6,Kick:5,Jump:4,PunchHold:2'."""
    if spec == "intense-v2" or spec in CHARACTER_PLANS:
        return [(KICK, 1)]      # planned by plan_sets_v2 / plan_sets_character, not cycled
    if spec in SET_PATTERNS:
        return SET_PATTERNS[spec]

    parsed: List[Tuple[str, int]] = []
    for chunk in spec.split(","):
        chunk = chunk.strip()
        if not chunk:
            continue
        name, _, count = chunk.partition(":")
        name = name.strip()
        if name not in ALL_EXERCISES:
            raise SystemExit(
                f"--pattern: '{name}' is not an exercise. Use one of {', '.join(ALL_EXERCISES)}, "
                f"or a preset name: {', '.join(sorted(SET_PATTERNS))}"
            )
        try:
            repeats = int(count) if count.strip() else 1
        except ValueError:
            raise SystemExit(f"--pattern: '{chunk}' - the part after ':' must be a whole number")
        if repeats < 1:
            raise SystemExit(f"--pattern: '{chunk}' - a block needs at least one set")
        parsed.append((name, repeats))

    if not parsed:
        raise SystemExit("--pattern is empty")
    return parsed


def _positive_float(raw: str, name: str, chunk: str) -> float:
    try:
        value = float(raw)
    except ValueError:
        raise SystemExit(f"{name}: '{chunk}' - the part after ':' must be a number")
    if value <= 0:
        raise SystemExit(f"{name}: '{chunk}' - spacing must be greater than zero")
    return value


def _positive_int(raw: str, name: str, chunk: str) -> int:
    try:
        value = int(raw)
    except ValueError:
        raise SystemExit(f"{name}: '{chunk}' - the part after ':' must be a whole number of bars")
    if value <= 0:
        raise SystemExit(f"{name}: '{chunk}' - a set must be at least one bar long")
    return value


def _lane_pair(raw: str, name: str, chunk: str) -> Tuple[int, int]:
    parts = raw.split("-")
    if len(parts) != 2:
        raise SystemExit(f"{name}: '{chunk}' - write a lane pair as two numbers, e.g. Punch:1-2")
    try:
        left, right = int(parts[0]), int(parts[1])
    except ValueError:
        raise SystemExit(f"{name}: '{chunk}' - lane numbers must be whole numbers")
    return left, right


def parse_mapping(spec: Optional[str], defaults: dict, name: str, convert) -> dict:
    """Parse 'Punch:x,Kick:y' over a copy of `defaults`, leaving unmentioned keys alone."""
    values = dict(defaults)
    if not spec:
        return values
    for chunk in spec.split(","):
        chunk = chunk.strip()
        if not chunk:
            continue
        key, _, raw = chunk.partition(":")
        key = key.strip()
        if key not in ALL_EXERCISES:
            raise SystemExit(f"{name}: '{key}' is not an exercise")
        values[key] = convert(raw.strip(), name, chunk)
    return values


def plan_sets(bar_lines: np.ndarray, pattern: List[Tuple[str, int]], set_bars: int,
              rest_beats: float, beat: float, track_end: float,
              set_bars_for: Optional[Dict[str, int]] = None) -> List[WorkoutSet]:
    """Lay sets out along the bar grid until the track runs out.

    Sets step by a WHOLE number of bars, and the rest is carved out of the end of each set
    rather than added between them. That is what lets the rest be shorter than a bar without
    losing the bar alignment: a rest of, say, one beat inserted *between* sets would push every
    subsequent set a beat further off the grid, and by the fourth set the routine would be
    changing movement in the middle of a bar. Here the block always runs bar line to bar line
    and simply stops accepting notes `rest_beats` before the end.

    A set that cannot fit its full length before the end of the track is not emitted at all,
    rather than emitted short: half a block of an exercise at the end of a routine reads as the
    file being truncated.

    `set_bars_for` gives some exercises a length of their own (--set-bars-for); everything else
    is `set_bars` long. Every length is still a whole number of bars, so the grid holds.
    """
    order: List[str] = []
    for exercise, repeats in pattern:
        order.extend([exercise] * repeats)

    sets: List[WorkoutSet] = []
    bar = 0
    while True:
        exercise = order[len(sets) % len(order)]
        bars = max(1, int((set_bars_for or {}).get(exercise, set_bars)))
        if bar + bars >= len(bar_lines):
            break
        start = float(bar_lines[bar])
        boundary = float(bar_lines[bar + bars])
        if start >= track_end:
            break
        end = max(start, boundary - rest_beats * beat)
        sets.append(WorkoutSet(index=len(sets), exercise=exercise,
                               start=start, end=min(end, track_end),
                               rest_end=min(boundary, track_end), bar=bar))
        bar += bars

    return sets


def apply_workout(events: List[Event], sets: List[WorkoutSet], beat: float,
                  spacing: Dict[str, float], cfg) -> List[Event]:
    """Retype every note by the set it falls in, drop the rests, and thin each set.

    Returns the surviving notes. Anything landing in a rest is discarded outright - the rest is
    the point, it is the window in which the player reads what is coming and changes stance.
    """
    kept: List[Event] = []
    for block in sets:
        inside = [ev for ev in events if block.start <= ev.time < block.end]
        if not inside:
            continue

        # --spacing-tolerance forgives a floor missed by rounding: on the grid detector two
        # notes exactly N beats apart can come out a hair under N beats of the fitted period.
        floor = (spacing.get(block.exercise, 0.5) - cfg.spacing_tolerance) * beat
        last = -1e9
        chosen: List[Event] = []
        for ev in inside:
            if ev.time - last < floor:
                continue
            ev.type = block.exercise
            ev.set_index = block.index
            chosen.append(ev)
            last = ev.time

        block.notes = len(chosen)
        kept.extend(chosen)

    return kept


def size_workout_holds(events: List[Event], sets: List[WorkoutSet], cfg) -> None:
    """Give every hold in a hold set a length, and demote any that cannot have one.

    In workout mode a hold's length is not measured from the audio - the set decided this is a
    sustained movement, so the question is only how long to sustain it. It runs to the next note
    of the same set, or to the end of the set, less `--hold-gap`, and is capped by `--hold-max`.
    """
    by_set: Dict[int, List[Event]] = {}
    for ev in events:
        by_set.setdefault(ev.set_index, []).append(ev)

    for block in sets:
        if block.exercise not in HOLD_TYPES:
            continue
        members = sorted(by_set.get(block.index, []), key=lambda e: e.time)
        for position, ev in enumerate(members):
            last_end = block.rest_end if block.exercise == SWEEP else block.end
            limit = members[position + 1].time if position + 1 < len(members) else last_end
            # A sweep leaves its arm out at the side, and the next one starts from the centre:
            # it stops --sweep-gap-beats early so there is time to bring the arm back.
            gap = cfg.sweep_gap_beats * cfg.beat if block.exercise == SWEEP else cfg.hold_gap
            ev.duration = min(cfg.hold_max, max(0.0, limit - ev.time - gap))
            if round(ev.duration, 3) <= 0.0:
                ev.duration = 0.0
                ev.type = KICK if block.exercise == HOLD_TYPE_OF[KICK] else PUNCH


def assign_workout_lanes(events: List[Event], sets: List[WorkoutSet],
                         pairs: Dict[str, Tuple[int, int]], lane_count: int) -> None:
    """Alternate left/right within each set: left hand, right hand, left foot, right foot.

    The counter restarts at every set, so each block opens on the same side and the routine is
    symmetric across repetitions rather than depending on how many notes the previous set had.
    """
    by_set: Dict[int, List[Event]] = {}
    for ev in events:
        by_set.setdefault(ev.set_index, []).append(ev)

    for block in sets:
        pair = pairs.get(block.exercise, (1, 2))
        members = sorted(by_set.get(block.index, []), key=lambda e: e.time)
        for position, ev in enumerate(members):
            ev.lane = min(lane_count - 1, max(0, pair[position % 2]))


def expand_chords(events: List[Event], sets: List[WorkoutSet], punch_every: int,
                  punch_lanes: Tuple[int, int], lane_count: int) -> List[Event]:
    """Write every Chord note out as two Kicks on the inner lanes, plus a Punch on some.

    The whole chord rule lives here: which lanes the feet land in (the two inner ones - a jump
    onto both feet, straight ahead), and how often a fist goes with it (every `punch_every`'th
    chord of a set, 0 = never), alternating between the two lanes of `punch_lanes` so the arm
    changes too. Returns a new, time-sorted list; set note counts are updated to match.
    """
    inner = (max(0, lane_count // 2 - 1), min(lane_count - 1, lane_count // 2))
    out: List[Event] = []
    per_set: Dict[int, int] = {}
    for ev in events:
        if ev.type != CHORD:
            out.append(ev)
            continue
        n = per_set.get(ev.set_index, 0)
        per_set[ev.set_index] = n + 1
        for lane in inner:
            out.append(Event(time=ev.time, type=KICK, intensity=ev.intensity, lane=lane,
                             frame=ev.frame, bands=ev.bands, acoustic=ev.acoustic,
                             set_index=ev.set_index))
        if punch_every > 0 and n % punch_every == punch_every - 1:
            lane = punch_lanes[(n // punch_every) % 2]
            out.append(Event(time=ev.time, type=PUNCH, intensity=ev.intensity, lane=lane,
                             frame=ev.frame, bands=ev.bands, acoustic=ev.acoustic,
                             set_index=ev.set_index))
    counts: Dict[int, int] = {}
    for ev in out:
        counts[ev.set_index] = counts.get(ev.set_index, 0) + 1
    for block in sets:
        if block.exercise == CHORD:
            block.notes = counts.get(block.index, 0)
    out.sort(key=lambda e: e.time)
    return out


# The "intense-v2" routine: which exercise each set gets. This is the whole policy - the two
# rotations, how long each exercise's set is, and what counts as the stretch after a drop - and it
# is the one function to change for a different routine (plan_sets_v2 only walks the bars).
INTENSE_V2_NORMAL = [KICK, HIGH_LOW, SIDE_JUMP, PUNCH, WALL_RUN, SHIFT, CHORD, "KickHold",
                     HIGH_LOW, JACK, DUCK, KICK, CROSS, PUNCH, BALANCE, SIDE_JUMP, "PunchHold",
                     SHIFT, DODGE, CHORD]
INTENSE_V2_HEAVY = [CROSS, JACK, SIDE_JUMP, CHORD]
# --v2-rotation arms: the same routine with the arms given more of the work (video 4 asked for
# about 60/40 legs/arms, where the default comes out near 75/25): more Punch, HighLow and PunchHold
# sets, fewer Kick-only ones, and HighLow in the heavy rotation after drops.
INTENSE_V2_ROTATIONS = {
    "default": (INTENSE_V2_NORMAL, INTENSE_V2_HEAVY),
    "arms": ([PUNCH, KICK, HIGH_LOW, SIDE_JUMP, PUNCH, WALL_RUN, HIGH_LOW, SHIFT, "PunchHold",
              CHORD, PUNCH, JACK, DUCK, HIGH_LOW, CROSS, PUNCH, BALANCE, "KickHold", HIGH_LOW,
              SIDE_JUMP, "PunchHold", DODGE],
             [CROSS, HIGH_LOW, JACK, PUNCH, SIDE_JUMP, CHORD]),
}
INTENSE_V2_BARS = {WALL_RUN: 4, CROSS: 2, BALANCE: 2, DUCK: 2, DODGE: 2}
INTENSE_V2_HEAVY_BARS = 8        # bars after a drop that take the heavy rotation
OBSTACLE_SETS = {WALL_RUN, BALANCE, DUCK, DODGE, CURB, SQUAT}


def drops_from_bar_map(bar_map: List[str]) -> List[int]:
    """Bars where the drums come back after at least four bars without them (or first start)."""
    drops, quiet = [], 0   # a track that opens on its drums does not open on a "drop"
    for i, c in enumerate(bar_map):
        if c == "#":
            if quiet >= 4: drops.append(i)
            quiet = 0
        else:
            quiet += 1
    return drops


def choose_set_exercise(bar: int, drops: List[int], history: List[str], cursor: dict,
                        rotation_name: str = "default") -> str:
    """The next set's exercise. Heavy ones (the star, jacks, side jumps, chords) for the first
    bars after a drop, the normal rotation elsewhere; never the same twice running, and never two
    obstacle sets back to back - the camera has to come home between them."""
    heavy = any(0 <= bar - d < INTENSE_V2_HEAVY_BARS for d in drops)
    normal_list, heavy_list = INTENSE_V2_ROTATIONS[rotation_name]
    rotation, key = (heavy_list, "heavy") if heavy else (normal_list, "normal")
    for _ in range(len(rotation)):
        ex = rotation[cursor[key] % len(rotation)]
        cursor[key] += 1
        if history and ex == history[-1]: continue
        if history and ex in OBSTACLE_SETS and history[-1] in OBSTACLE_SETS: continue
        return ex
    return KICK


def plan_sets_v2(bar_lines: np.ndarray, bar_map: List[str], rest_beats: float, beat: float,
                 track_end: float, rotation_name: str = "default") -> List[WorkoutSet]:
    """Sets for the intense-v2 routine, laid bar line to bar line like plan_sets."""
    drops = drops_from_bar_map(bar_map)
    sets: List[WorkoutSet] = []
    history: List[str] = []
    cursor = {"heavy": 0, "normal": 0}
    bar = 0
    while True:
        ex = choose_set_exercise(bar, drops, history, cursor, rotation_name)
        bars = INTENSE_V2_BARS.get(ex, 2)
        if bar + bars >= len(bar_lines):
            break
        start, boundary = float(bar_lines[bar]), float(bar_lines[bar + bars])
        if start >= track_end:
            break
        end = max(start, boundary - rest_beats * beat)
        sets.append(WorkoutSet(index=len(sets), exercise=ex, start=start, end=min(end, track_end),
                               rest_end=min(boundary, track_end), bar=bar))
        history.append(ex)
        bar += bars
    return sets


# --------------------------------------------------------------------------------------
# Video 6 ("Neon Prism"): one character of movement per track
# --------------------------------------------------------------------------------------
# --pattern intense-v2-arms / -legs / -combo / -finale. Same machinery as intense-v2 (sets on bar
# lines, notes on the grid, the exercises written out by expand_compound), but the routine is
# composed in PHRASES - 4 or 8 bars - rather than cycled exercise by exercise:
#   * a phrase is a short list of (exercise, bars) with a motif that comes back (A B A C);
#   * the normal phrases take turns, one after another;
#   * after a drop the "drop" phrases play for drop_bars (the brightest links of the track);
#   * a phrase that starts in a stretch without drums is a "calm" one (holds - a breather).
# This table is the whole policy: to change what a track asks for, edit it, nothing else.
CHARACTER_PLANS: Dict[str, dict] = {
    # Arms: punches on the beats, two levels, the star; the legs only come in for a bar-pair now and then.
    "intense-v2-arms": {
        "normal": [
            [(PUNCH, 2), (HIGH_LOW, 2), (PUNCH, 2), (HIGH_LOW, 2)],
            [(PUNCH, 2), ("PunchHold", 2), (PUNCH, 2), (CROSS, 2)],
            [(HIGH_LOW, 2), (PUNCH, 2), (HIGH_LOW, 2), ("PunchHold", 2)],
            [(PUNCH, 2), (HIGH_LOW, 2), (PUNCH, 2), (KICK, 2)],
        ],
        "drop": [[(CROSS, 2), (HIGH_LOW, 2), (CROSS, 2), (PUNCH, 2)]],
        "calm": [[("PunchHold", 2), (PUNCH, 2)]],
        "fill": PUNCH, "drop_bars": 8,
    },
    # Legs and trunk: kicks, side jumps, steps (Shift), ducks, kerbs, one-leg balance.
    "intense-v2-legs": {
        "normal": [
            [(KICK, 2), (SIDE_JUMP, 2), (KICK, 2), (SIDE_JUMP, 2)],
            [(KICK, 2), (SHIFT, 2), (KICK, 2), (DUCK, 2)],
            [("KickHold", 2), (KICK, 2), (SIDE_JUMP, 2), (BALANCE, 2)],
            [(SHIFT, 2), (KICK, 2), (CURB, 2), (KICK, 2)],
        ],
        "drop": [[(JACK, 2), (SIDE_JUMP, 2), (SHIFT, 2), (KICK, 2)]],
        "calm": [[("KickHold", 2), (KICK, 2)], [("KickHold", 2), (DUCK, 2)],
                 [(KICK, 2), (BALANCE, 2)], [("KickHold", 2), (CURB, 2)]],
        "fill": KICK, "drop_bars": 8,
    },
    # Combinations: two-foot chords with a punch, jacks, shifts under punches, changing every 4/8 bars.
    "intense-v2-combo": {
        "normal": [
            [(CHORD, 2), (HIGH_LOW, 2), (CHORD, 2), (HIGH_LOW, 2)],
            [(JACK, 2), (PUNCH, 2), (JACK, 2), (KICK, 2)],
            [(SHIFT, 2), (HIGH_LOW, 2), (SIDE_JUMP, 2), (PUNCH, 2)],
            [(CROSS, 2), (JACK, 2)],
        ],
        "drop": [[(CHORD, 2), (JACK, 2), (CROSS, 2), (HIGH_LOW, 2)]],
        "calm": [[("PunchHold", 2), ("KickHold", 2)]],
        "fill": PUNCH, "drop_bars": 8,
    },
    # Finale: a mix of everything, one-bar changes in the first phrase, two drop phrases in a row.
    "intense-v2-finale": {
        "normal": [
            [(PUNCH, 1), (KICK, 1), (HIGH_LOW, 1), (SIDE_JUMP, 1), (PUNCH, 1), (KICK, 1), (JACK, 1), (CHORD, 1)],
            [(HIGH_LOW, 2), (SHIFT, 2), (CHORD, 2), (CROSS, 2)],
            [(JACK, 2), (PUNCH, 2), (SIDE_JUMP, 2), (HIGH_LOW, 2)],
            [(KICK, 2), (DUCK, 2), (HIGH_LOW, 2), (CURB, 2)],
        ],
        "drop": [[(CROSS, 2), (CHORD, 2), (JACK, 2), (HIGH_LOW, 2)],
                 [(CHORD, 2), (CROSS, 2), (SIDE_JUMP, 2), (HIGH_LOW, 2)]],
        "calm": [[("PunchHold", 2), (KICK, 2)]],
        "fill": PUNCH, "drop_bars": 16,
    },

    # Video 7 ("Neon Carnival"). Three levels that must feel like three different levels - no
    # phrase is shared between them. Each has its own `arcs`: the ArcHold circles it uses, in turn,
    # as (degrees, beats, radius m); and with `lead_in` the bars before a drop become an Arc set,
    # so the circles land on breaks, melodic stretches and run-ups to the drops. Two Arc sets are
    # never back to back (plan_sets_character).
    #
    # 1, RioNight - the light level: steps and single punches, a fan only on the drops, medium circles.
    "carnival-rio": {
        "normal": [
            [(KICK, 2), (STEP_TAP, 2), (PUNCH, 2), (ARC, 2)],
            [(SHIFT, 2), (PUNCH, 2), (KICK, 2), (ARC, 2)],
            [(STEP_TAP, 2), (KICK, 2), (PUNCH, 2), (ARC, 2)],
        ],
        "drop": [[(STEP_TAP, 2), (FAN, 2), (KICK, 2), (PUNCH, 2)]],
        "calm": [[(ARC, 2), (KICK, 2)]],
        "fill": KICK, "drop_bars": 8, "lead_in": True, "calm_first": True,
        "arcs": [(270.0, 4, 0.7), (360.0, 6, 0.75), (270.0, 4, 0.7), (450.0, 6, 0.8)],
    },
    # 2, Carnaval - the dance level: fans and mirrored pairs everywhere, the longest circles.
    "carnival-arcs": {
        "normal": [
            [(FAN, 2), (MIRROR, 2), (ARC, 4)],
            [(FAN, 2), (HIGH_LOW, 2), (FAN, 2), (ARC, 2)],
            [(MIRROR, 2), (FAN, 2), (ARC, 4)],
            [(FAN, 2), (MIRROR, 2), (HIGH_LOW, 2), (ARC, 2)],
        ],
        "drop": [[(FAN, 2), (MIRROR, 2), (FAN, 2), (CROSS, 2)], [(FAN, 2), (MIRROR, 2), (ARC, 4)]],
        "calm": [[(ARC, 4), (MIRROR, 2), (FAN, 2)]],
        "fill": MIRROR, "drop_bars": 8, "lead_in": True, "calm_first": True,
        "arcs": [(450.0, 8, 0.8), (540.0, 8, 0.85), (360.0, 6, 0.75), (540.0, 8, 0.85)],
    },
    # 3, Copacabana - the finale: dense arms + legs, chords, side jumps, ducks; circles only on
    # the breaks and the run-ups.
    "carnival-copa": {
        "normal": [
            [(CHORD, 2), (SIDE_JUMP, 2), (PUNCH_KICK, 2), (JACK, 2)],
            [(PUNCH_KICK, 2), (DUCK, 2), (SIDE_JUMP, 2), (CHORD, 2)],
            [(JACK, 2), (HIGH_LOW, 2), (PUNCH_KICK, 2), (CROSS, 2)],
        ],
        "drop": [[(CHORD, 2), (PUNCH_KICK, 2), (JACK, 2), (SIDE_JUMP, 2)],
                 [(PUNCH_KICK, 2), (CHORD, 2), (SIDE_JUMP, 2), (CROSS, 2)]],
        "calm": [[(ARC, 2), (KICK, 2), (PUNCH_KICK, 2), (KICK, 2), (PUNCH_KICK, 2), (KICK, 2)]],
        "fill": PUNCH_KICK, "drop_bars": 16, "lead_in": False, "calm_first": True,
        "arcs": [(360.0, 6, 0.75), (270.0, 4, 0.7), (450.0, 6, 0.8)],
    },
}

CHARACTER_PLANS.update({
    # Video 8 ("Halloween 2"): a cardio warm-up for a person in front of the screen, in three levels
    # that each bring new moves rather than more of the same. Every phrase is 8 bars: a motif, the
    # motif again on the other side (an "R" entry - mirror_sets), and a change of limb to close it.
    # Sets end on a beat of rest, so every move starts from the centre.
    #
    # 1, WitchSwamp - the easy level: steps and single punches, half-time warm-up moves (one limb
    # every other beat), a simple wall, a short hold; two-bar blocks that come back again and again;
    # circles only now and then on the quiet stretches, and short.
    "halloween2-swamp": {
        "normal": [
            [(STEP_TOUCH, 2), (JAB_CROSS, 2), (KICK, 2), (PUNCH, 2)],           # half time -> full time
            [(STEP_TAP, 2), (PUNCH, 2), (STEP_TAP, 2, "R"), (DODGE, 2)],        # motif, mirrored, a wall
            [(KICK, 2), (JAB_CROSS, 2), (KICK, 2, "R"), ("PunchHold", 2)],
            [(STEP_TAP, 2, "R"), (PUNCH, 2, "R"), (STEP_TOUCH, 2), (DODGE, 2)],
        ],
        "drop": [[(PUNCH, 2), (STEP_TAP, 2), (PUNCH, 2, "R"), (STEP_TAP, 2, "R")]],
        "calm": [[(STEP_TOUCH, 2), (JAB_CROSS, 2)], [(STEP_TOUCH, 2), (ARC, 2)],
                 [("PunchHold", 2), (STEP_TOUCH, 2)], [(JAB_CROSS, 2), (STEP_TOUCH, 2)]],
        "fill": STEP_TOUCH, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "arcs": [(270.0, 4, 0.7), (360.0, 4, 0.7)],
    },
    # 2, MadLab - the hard level: the 1-2-1-kick to both sides, step-and-reach, high/low punches, the
    # punch fan, mirrored pairs, side jumps, the star, ducks and kerbs, the long circles before drops.
    "halloween2-lab": {
        "normal": [
            [(JAB_KICK, 2), (JAB_KICK, 2, "R"), (HIGH_LOW, 2), (DUCK, 2)],
            [(FAN, 2), (SIDE_JUMP, 2), (FAN, 2), (CURB, 2)],
            [(STEP_REACH, 2), (STEP_REACH, 2, "R"), ("KickHold", 2), (CROSS, 2)],
            [(MIRROR, 2), (JAB_KICK, 2, "R"), (SIDE_JUMP, 2), (ARC, 2)],
        ],
        "drop": [[(FAN, 2), (JAB_KICK, 2), (FAN, 2), (CROSS, 2)],
                 [(HIGH_LOW, 2), (SIDE_JUMP, 2), (JAB_KICK, 2, "R"), (CROSS, 2)]],
        "calm": [[(ARC, 4), (STEP_REACH, 2), ("KickHold", 2)], [("KickHold", 2), (DUCK, 2)],
                 [(STEP_REACH, 2, "R"), (ARC, 2)]],
        "fill": MIRROR, "drop_bars": 8, "lead_in": True, "calm_first": True,
        "arcs": [(450.0, 6, 0.8), (540.0, 8, 0.85), (360.0, 6, 0.75), (720.0, 8, 0.85)],
    },
    # 3, VampireCastle - the very hard level: power jumps and two-foot chords with punches, jacks,
    # shifts, punch+kick, one-bar 1-2-1 combos switching sides, a circle with punches from the other
    # hand, the long wall, one-leg / wide-stance kerbs; 16 bars of drop phrases, short breathers.
    "halloween2-castle": {
        "normal": [
            [(WALL_RUN, 4), (JAB_KICK, 1), (JAB_KICK, 1, "R"), (POWER_JUMP, 2)],
            [(SHIFT, 2), (ARC_PUNCH, 2), (SHIFT, 2), (ARC_PUNCH, 2, "R")],
            [(CHORD, 2), (BALANCE, 2), (PUNCH_KICK, 2, "R"), (JACK, 2)],
            [(POWER_JUMP, 2), (JAB_KICK, 1), (JAB_KICK, 1, "R"), (JACK, 2), (PUNCH_KICK, 2)],
        ],
        "drop": [[(CHORD, 2), (PUNCH_KICK, 2), (JACK, 2), (SHIFT, 2)],
                 [(JAB_KICK, 1), (JAB_KICK, 1, "R"), (POWER_JUMP, 2), (ARC_PUNCH, 2), (JACK, 2)],
                 [(PUNCH_KICK, 2, "R"), (CHORD, 2), (SHIFT, 2), (ARC_PUNCH, 2, "R")]],
        "calm": [[(ARC, 2), (STEP_TOUCH, 2)], [(BALANCE, 2), (KICK, 2)]],
        "fill": PUNCH_KICK, "drop_bars": 16, "lead_in": False, "calm_first": True,
        "arcs": [(360.0, 4, 0.75), (450.0, 6, 0.8)],
    },
})

CHARACTER_PLANS.update({
    # Video 9: the competitor's system (Immersive Dance Mode), adapted. A small vocabulary:
    # footprints (steps, single and paired), fists (alternating, paired, two heights), the RAYS
    # (Cross: both fists high and out with two strips apart - the star) on the first bar of every
    # drop and at the end of the level, the held squat (SquatHold) on build-ups and breaks, walls,
    # rails (stand narrow), long strips (a lunge, both feet wide). Phrases of 8 bars: steps ->
    # punches -> a big move on the phrase's edge; quiet stretches (no kick) get the big slow moves,
    # drops the quick steps and punches. Each level adds moves to the one before; the ArcHold - our
    # own signature - is limited to one or two per level.
    "competitor-easy": {            # ~1.3 actions/s
        "normal": [
            [(STEP_TOUCH, 2), (PUNCH, 2), (KICK, 2), (DODGE, 2)],
            [(KICK, 2), (JAB_CROSS, 2), (KICK, 2, "R"), (PUNCH, 2, "R")],
            [(STEP_PAIR, 2), (PUNCH, 2), (STEP_TOUCH, 2), (DODGE, 2, "R")],
            [(KICK, 2), (JAB_CROSS, 2), (STEP_PAIR, 2), (PUNCH, 2)],
        ],
        "drop": [[(CROSS, 1), (KICK, 2), (JAB_CROSS, 2), (STEP_PAIR, 2), (PUNCH, 1)]],
        "calm": [[(SQUAT, 4), (STEP_TOUCH, 2), (JAB_CROSS, 2)],
                 [(STEP_TOUCH, 2), (ARC, 2), (SQUAT, 4)],
                 [(DODGE, 2), (STEP_TOUCH, 2), (JAB_CROSS, 2), (STEP_TOUCH, 2)]],
        "fill": KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75)],
    },
    "competitor-medium": {          # ~1.8 actions/s: + two heights, double punches, strips, rails, ducks
        "normal": [
            [(KICK, 2, "R"), (PUNCH_PAIR, 2), (KICK, 2), (DODGE, 2)],
            [(KICK, 2), (HIGH_LOW, 2), (STEP_PAIR, 2), (DUCK, 2)],
            [(KICK, 2), (MIRROR, 2), (LUNGE, 2), (LUNGE, 2, "R")],
            [(STEP_PAIR, 2), (HIGH_LOW, 2), (RAILS, 2), (DODGE, 2, "R")],
        ],
        "drop": [[(CROSS, 1), (KICK, 2), (MIRROR, 2), (HIGH_LOW, 2), (PUNCH, 1)],
                 [(CROSS, 1), (STEP_PAIR, 2), (PUNCH, 2), (KICK, 2), (HIGH_LOW, 1)]],
        "calm": [[(SQUAT, 4), (WIDE, 2), (KICK, 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (ARC, 2), (KICK, 2)],
                 [(RAILS, 2), (STEP_TOUCH, 2), (SQUAT, 4)]],
        "fill": KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(360.0, 6, 0.75), (450.0, 6, 0.8)],
    },
    "competitor-hard": {            # ~2.3 actions/s: + speed punches, jumps over kerbs, jacks, punch+kick, the long wall
        "normal": [
            [(WALL_RUN, 4), (SPEED_PUNCH, 2), (DUCK, 2)],
            [(KICK, 2), (SPEED_PUNCH, 2), (JACK, 2), (CURB, 2)],
            [(JACK, 2), (SPEED_PUNCH, 2), (PUNCH_KICK, 2), (DODGE, 2)],
            [(PUNCH_KICK, 2), (SPEED_PUNCH, 2), (LUNGE, 2), (LUNGE, 2, "R")],
        ],
        "drop": [[(CROSS, 1), (SPEED_STEP, 2), (SPEED_PUNCH, 2), (JACK, 2), (SPEED_PUNCH, 1)],
                 [(CROSS, 1), (SPEED_STEP, 2), (SPEED_PUNCH, 2), (PUNCH_KICK, 2), (SPEED_STEP, 1)]],
        "calm": [[(SQUAT, 4), (RAILS, 2), (SPEED_PUNCH, 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (ARC, 2), (SPEED_PUNCH, 2)],
                 [(WIDE, 2), (PUNCH_PAIR, 2), (SQUAT, 4)]],
        "fill": KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75), (450.0, 6, 0.8)],
    },
})

CHARACTER_PLANS.update({
    # Video 10 ("Cosmic Rush"): levels 1 and 2 as dense as competitor-hard (~2.3 actions/s) but each
    # its own workout, so no two levels - and no two videos - feel alike. Many phrases, each used
    # once before the cycle repeats, mirrored halves ("R") so both sides work; level 3 stays
    # competitor-hard.
    #
    # 1, OrbitStation - the boxer's station: arms and trunk. 1-2-1-kick combos, the punch fan, two
    # heights, double punches, a circle with the lead hand while the rear one punches, mirrored
    # pairs, step-and-reach, ducks under bars and walls between the combos.
    "cosmic-boxer": {
        "normal": [
            [(JAB_KICK, 2), (JAB_KICK, 2, "R"), (HIGH_LOW, 2), (DUCK, 2)],
            [(FAN, 2), (PUNCH_PAIR, 2), (FAN, 2), (DODGE, 2)],
            [(STEP_REACH, 2), (STEP_REACH, 2, "R"), (MIRROR, 2), (DUCK, 2)],
            [(ARC_PUNCH, 2), (HIGH_LOW, 2), (ARC_PUNCH, 2, "R"), (DODGE, 2, "R")],
            [(JAB_KICK, 1), (JAB_KICK, 1, "R"), (FAN, 2), (PUNCH_PAIR, 2), (WALL_RUN, 4)],
            [(MIRROR, 2), (STEP_TAP, 2), (HIGH_LOW, 2, "R"), (DUCK, 2)],
            [(PUNCH_PAIR, 2), (JAB_KICK, 2, "R"), (STEP_REACH, 2), (DODGE, 2)],
        ],
        "drop": [[(CROSS, 1), (FAN, 2), (JAB_KICK, 2), (HIGH_LOW, 2), (PUNCH_PAIR, 1)],
                 [(CROSS, 1), (MIRROR, 2), (JAB_KICK, 2, "R"), (FAN, 2), (HIGH_LOW, 1)],
                 [(CROSS, 1), (HIGH_LOW, 2), (STEP_REACH, 2), (MIRROR, 2), (FAN, 1)]],
        "calm": [[(ARC, 2), (STEP_REACH, 2), ("PunchHold", 2), (JAB_CROSS, 2)],
                 [(RAILS, 2), (PUNCH_PAIR, 2), (ARC_PUNCH, 2), (STEP_TOUCH, 2)],
                 [("PunchHold", 2), (MIRROR, 2), (ARC, 2), (STEP_REACH, 2, "R")]],
        "fill": HIGH_LOW, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2, WALL_RUN: 2}, "finale": CROSS,
        "arcs": [(450.0, 6, 0.8), (360.0, 4, 0.75)],
    },
    # 2, AsteroidStorm - plyometrics: legs and jumps. Jacks, side jumps, shifts, power jumps with
    # the fists up, two-foot chords with a punch, kerbs to jump, one-leg balance, the wide stance,
    # step pairs, the held squat on the breaks; punches only on top of the jumps.
    "cosmic-plyo": {
        "normal": [
            [(JACK, 2), (SIDE_JUMP, 2), (JACK, 2), (CURB, 2)],
            [(SHIFT, 2), (POWER_JUMP, 2), (SHIFT, 2, "R"), (BALANCE, 2)],
            [(CHORD, 2), (STEP_PAIR, 2), (SIDE_JUMP, 2, "R"), (CURB, 2)],
            [(POWER_JUMP, 2), (KICK, 2), (WIDE, 2), (BALANCE, 2)],
            [(SIDE_JUMP, 2), (CHORD, 2), (JACK, 2), ("KickHold", 2)],
            [(STEP_PAIR, 2), (SHIFT, 2), (POWER_JUMP, 2), (CURB, 2)],
            [(KICK, 2, "R"), (JACK, 2), (CHORD, 2), (BALANCE, 2)],
        ],
        "drop": [[(CROSS, 1), (POWER_JUMP, 2), (JACK, 2), (SIDE_JUMP, 2), (CHORD, 1)],
                 [(CROSS, 1), (CHORD, 2), (SHIFT, 2), (JACK, 2), (POWER_JUMP, 1)],
                 [(CROSS, 1), (SIDE_JUMP, 2), (POWER_JUMP, 2), (STEP_PAIR, 2), (JACK, 1)]],
        "calm": [[(SQUAT, 4), (WIDE, 2), ("KickHold", 2)],
                 [(BALANCE, 2), (KICK, 2), (SQUAT, 4)],
                 [(WIDE, 2), (STEP_PAIR, 2), ("KickHold", 2), (KICK, 2, "R")]],
        "fill": KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {SQUAT: 3}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75)],
    },
})

CHARACTER_PLANS.update({
    # Video 10, reworked after a real workout ("boring, the same over and over, no holds at all"):
    # Second pass: no punches inside a held squat (SquatPunch out - the squat is held alone), more
    # kerbs to jump, walls and squats/ducks through every level.
    # all three levels train everything - arms, legs, jumps, trunk - each phrase has at least one
    # held pose with work on top (HoldJab, LegHoldPunch, SquatPunch, RailsHigh, PressHold, Lunge,
    # Wide, ArcPunch, the long wall, the star), eight phrases a level so one comes back rarely, and
    # the three levels share no phrase - they differ by their combinations, not by body part.
    "cosmic-1": {
        "normal": [
            [(JAB_KICK, 2), (HOLD_JAB, 2), (JACK, 2), (DUCK, 2)],
            [(LEG_HOLD_PUNCH, 2), (CURB, 2), (FAN, 2), (DUCK, 2)],
            [(STEP_REACH, 2), (PRESS_HOLD, 2), (SHIFT, 2), (CURB, 2)],
            [(HIGH_LOW, 2), (LUNGE, 2), (LUNGE, 2, "R"), (DODGE, 2)],
            [(CURB, 2), (HOLD_JAB, 2, "R"), (PUNCH_KICK, 2), (BALANCE, 2)],
            [(MIRROR, 2), (SQUAT, 2), (STEP_PAIR, 2), (DODGE, 2, "R")],
            [(JAB_KICK, 2, "R"), (WIDE, 2), (SPEED_PUNCH, 2), (DUCK, 2)],
            [(CHORD, 2), (LEG_HOLD_PUNCH, 2, "R"), (PUNCH_PAIR, 2), (WALL_RUN, 2)],
        ],
        "drop": [[(CROSS, 1), (FAN, 2), (CURB, 2), (HOLD_JAB, 2), (JAB_KICK, 1)],
                 [(CROSS, 1), (POWER_JUMP, 2), (DUCK, 2), (LEG_HOLD_PUNCH, 2), (SHIFT, 1)],
                 [(CROSS, 1), (PUNCH_KICK, 2), (PRESS_HOLD, 2), (DODGE, 2), (MIRROR, 1)]],
        "calm": [[(SQUAT, 4), (ARC, 2), ("KickHold", 2)],
                 [(RAILS_HIGH, 2), ("PunchHold", 2), (LUNGE, 2), (CURB, 2)],
                 [(ARC_PUNCH, 2), (BALANCE, 2), (SQUAT, 4)]],
        "fill": HIGH_LOW, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(450.0, 6, 0.8), (360.0, 4, 0.75)],
    },
    "cosmic-2": {
        "normal": [
            [(SIDE_JUMP, 2), (LEG_HOLD_PUNCH, 2), (STEP_REACH, 2, "R"), (CURB, 2)],
            [(DUCK, 2), (RAILS_HIGH, 2), (JACK, 2), (DODGE, 2, "R")],
            [(SPEED_STEP, 2), (HOLD_JAB, 2), (MIRROR, 2), (BALANCE, 2)],
            [(SHIFT, 2, "R"), (PRESS_HOLD, 2), (JAB_KICK, 2), (DUCK, 2)],
            [(FAN, 2), (LUNGE, 2, "R"), (LUNGE, 2), (CURB, 2)],
            [(STEP_TAP, 2), (SQUAT, 2), (HIGH_LOW, 2, "R"), (WALL_RUN, 2)],
            [(CHORD, 2), (WIDE, 2), (PUNCH_KICK, 2, "R"), (WALL_RUN, 2)],
            [(JACK, 2), (HOLD_JAB, 2, "R"), (SPEED_PUNCH, 2), (CURB, 2)],
        ],
        "drop": [[(CROSS, 1), (CURB, 2), (PUNCH_KICK, 2), (RAILS_HIGH, 2), (SIDE_JUMP, 1)],
                 [(CROSS, 1), (SPEED_PUNCH, 2), (LEG_HOLD_PUNCH, 2, "R"), (DUCK, 2), (FAN, 1)],
                 [(CROSS, 1), (SHIFT, 2), (HOLD_JAB, 2), (DODGE, 2, "R"), (HIGH_LOW, 1)]],
        "calm": [[(LUNGE, 2), (LUNGE, 2, "R"), (SQUAT, 4)],
                 [("PunchHold", 2), (ARC, 2), (DODGE, 2), ("KickHold", 2)],
                 [(BALANCE, 2), (RAILS_HIGH, 2), (PRESS_HOLD, 2), (ARC_PUNCH, 2)]],
        "fill": KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(360.0, 6, 0.75), (540.0, 8, 0.85)],
    },
    "cosmic-3": {
        "normal": [
            [(WALL_RUN, 4), (HOLD_JAB, 2), (POWER_JUMP, 2)],
            [(PUNCH_KICK, 2), (LEG_HOLD_PUNCH, 2), (SPEED_STEP, 2), (DUCK, 2)],
            [(JAB_KICK, 1), (JAB_KICK, 1, "R"), (SQUAT, 2), (JACK, 2), (CURB, 2)],
            [(CURB, 2), (PRESS_HOLD, 2, "R"), (SIDE_JUMP, 2), (DUCK, 2)],
            [(MIRROR, 2), (RAILS_HIGH, 2), (CHORD, 2), (DODGE, 2)],
            [(SPEED_PUNCH, 2), (LUNGE, 2), (SHIFT, 2), (SQUAT, 2)],
            [(DODGE, 2), (WIDE, 2), (STEP_REACH, 2), (CURB, 2)],
            [(POWER_JUMP, 2), (LEG_HOLD_PUNCH, 2, "R"), (PUNCH_PAIR, 2), (DUCK, 2)],
        ],
        "drop": [[(CROSS, 1), (SPEED_STEP, 2), (HOLD_JAB, 2), (DUCK, 2), (SPEED_PUNCH, 1)],
                 [(CROSS, 1), (CHORD, 2), (LEG_HOLD_PUNCH, 2), (CURB, 2), (POWER_JUMP, 1)],
                 [(CROSS, 1), (JAB_KICK, 2), (RAILS_HIGH, 2), (WALL_RUN, 2), (FAN, 1)]],
        "calm": [[(SQUAT, 4), (LUNGE, 2, "R"), (ARC, 2)],
                 [(PRESS_HOLD, 2), ("KickHold", 2), (BALANCE, 2), (CURB, 2)],
                 [(ARC_PUNCH, 2), ("PunchHold", 2), (SQUAT, 4)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(450.0, 6, 0.8), (720.0, 8, 0.85)],
    },
})

CHARACTER_PLANS.update({
    # Video 13 ("Five Elements"): the user's call - "from level 1 something interesting, not just
    # arms: walls, jumps, dodges, squats, legs and arms, all combined". Every phrase mixes an
    # obstacle (wall, kerb, duck, dodge, squat, balance) with a leg set and an arm set, and the very
    # first phrase of each level is a showpiece. Five plans, no phrase shared, so the levels differ
    # by their combinations; the density climbs from 1 to 5.
    "mix-1": {
        "normal": [
            [(WALL_RUN, 2), (JAB_KICK, 2), (CURB, 2), (LEG_HOLD_PUNCH, 2)],
            [(SIDE_JUMP, 2), (DUCK, 2), (HIGH_LOW, 2), (SQUAT, 2)],
            [(STEP_REACH, 2), (DODGE, 2), (JACK, 2, ), (CURB, 2)],
            [(PUNCH_KICK, 2), (BALANCE, 2), (FAN, 2), (DUCK, 2)],
            [(SHIFT, 2), (DODGE, 2, "R"), (HOLD_JAB, 2), (WALL_RUN, 2)],
            [(LUNGE, 2), (LUNGE, 2, "R"), (POWER_JUMP, 2), (DUCK, 2)],
        ],
        "drop": [[(CROSS, 1), (POWER_JUMP, 2), (CURB, 2), (JAB_KICK, 2), (SIDE_JUMP, 1)],
                 [(CROSS, 1), (JACK, 2), (DUCK, 2), (MIRROR, 2), (SHIFT, 1)]],
        "calm": [[(BALANCE, 2), (ARC_PUNCH, 2), (DODGE, 2), ("KickHold", 2)],
                 [(SQUAT, 4), (STEP_REACH, 2), (CURB, 2)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75)],
    },
    "mix-2": {
        "normal": [
            [(CURB, 2), (SPEED_STEP, 2), (WALL_RUN, 2), (PUNCH_PAIR, 2)],
            [(JACK, 2), (SQUAT, 2), (MIRROR, 2), (DODGE, 2)],
            [(LEG_HOLD_PUNCH, 2, "R"), (DUCK, 2), (SIDE_JUMP, 2, "R"), (CURB, 2)],
            [(RAILS_HIGH, 2), (POWER_JUMP, 2), (BALANCE, 2), (JAB_KICK, 2, "R")],
            [(WIDE, 2), (DODGE, 2, "R"), (SPEED_PUNCH, 2), (DUCK, 2)],
            [(CHORD, 2), (WALL_RUN, 2), (STEP_TAP, 2), (CURB, 2)],
        ],
        "drop": [[(CROSS, 1), (SIDE_JUMP, 2), (DUCK, 2), (PUNCH_KICK, 2), (JACK, 1)],
                 [(CROSS, 1), (CURB, 2), (FAN, 2), (DODGE, 2), (POWER_JUMP, 1)]],
        "calm": [[(LUNGE, 2), (LUNGE, 2, "R"), (SQUAT, 4)],
                 [(PRESS_HOLD, 2), (BALANCE, 2), ("PunchHold", 2), (CURB, 2)]],
        "fill": KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75)],
    },
    "mix-3": {
        "normal": [
            [(DUCK, 2), (POWER_JUMP, 2), (DODGE, 2), (HOLD_JAB, 2, "R")],
            [(SHIFT, 2, "R"), (CURB, 2), (LEG_HOLD_PUNCH, 2), (WALL_RUN, 2)],
            [(JAB_KICK, 2), (SQUAT, 2), (SPEED_STEP, 2), (DUCK, 2)],
            [(SIDE_JUMP, 2), (BALANCE, 2), (HIGH_LOW, 2, "R"), (CURB, 2)],
            [(PUNCH_KICK, 2, "R"), (DODGE, 2), (JACK, 2), (SQUAT, 2)],
            [(STEP_REACH, 2, "R"), (WALL_RUN, 2), (CHORD, 2), (DUCK, 2)],
        ],
        "drop": [[(CROSS, 1), (SPEED_STEP, 2), (CURB, 2), (SPEED_PUNCH, 2), (POWER_JUMP, 1)],
                 [(CROSS, 1), (SHIFT, 2), (DUCK, 2), (LEG_HOLD_PUNCH, 2, "R"), (JACK, 1)]],
        "calm": [[(RAILS_HIGH, 2), ("KickHold", 2), (BALANCE, 2), (PRESS_HOLD, 2)],
                 [(SQUAT, 4), (ARC, 2), (DODGE, 2)]],
        "fill": HIGH_LOW, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(450.0, 6, 0.8)],
    },
    "mix-4": {
        "normal": [
            [(WALL_RUN, 2), (SIDE_JUMP, 2), (DUCK, 2), (SPEED_PUNCH, 2)],
            [(CURB, 2), (MIRROR, 2), (SQUAT, 2), (JACK, 2)],
            [(DODGE, 2), (PUNCH_KICK, 2), (BALANCE, 2), (POWER_JUMP, 2)],
            [(LUNGE, 2, "R"), (LUNGE, 2), (CURB, 2), (FAN, 2)],
            [(SPEED_STEP, 2), (DUCK, 2), (HOLD_JAB, 2), (DODGE, 2, "R")],
            [(CHORD, 2), (SQUAT, 2), (JAB_KICK, 2), (WALL_RUN, 2)],
        ],
        "drop": [[(CROSS, 1), (JACK, 2), (CURB, 2), (SPEED_PUNCH, 2), (SIDE_JUMP, 1)],
                 [(CROSS, 1), (POWER_JUMP, 2), (DODGE, 2), (PUNCH_KICK, 2), (SHIFT, 1)]],
        "calm": [[(WIDE, 2), (PRESS_HOLD, 2), (SQUAT, 4)],
                 [(ARC_PUNCH, 2), (CURB, 2), ("PunchHold", 2), (BALANCE, 2)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(540.0, 8, 0.85)],
    },
    "mix-5": {
        "normal": [
            [(POWER_JUMP, 2), (WALL_RUN, 2), (SPEED_STEP, 2), (CURB, 2)],
            [(JAB_KICK, 1), (JAB_KICK, 1, "R"), (DUCK, 2), (JACK, 2), (SQUAT, 2)],
            [(SPEED_PUNCH, 2), (BALANCE, 2), (SIDE_JUMP, 2), (DODGE, 2)],
            [(LEG_HOLD_PUNCH, 2), (CURB, 2), (SHIFT, 2), (DUCK, 2)],
            [(PUNCH_KICK, 2), (DODGE, 2, "R"), (CHORD, 2), (WALL_RUN, 2)],
            [(HOLD_JAB, 2), (SQUAT, 2), (POWER_JUMP, 2), (CURB, 2)],
        ],
        "drop": [[(CROSS, 1), (SPEED_STEP, 2), (DUCK, 2), (SPEED_PUNCH, 2), (JACK, 1)],
                 [(CROSS, 1), (SIDE_JUMP, 2), (CURB, 2), (PUNCH_KICK, 2), (POWER_JUMP, 1)],
                 [(CROSS, 1), (SHIFT, 2), (WALL_RUN, 2), (FAN, 2), (CHORD, 1)]],
        "calm": [[(RAILS_HIGH, 2), (BALANCE, 2), (ARC, 2), (CURB, 2)],
                 [(SQUAT, 4), (LUNGE, 2), (LUNGE, 2, "R")]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(720.0, 8, 0.85)],
    },
})

CHARACTER_PLANS.update({
    # Video 14 - "the best workout of your life": a coach's routine per level. Every phrase is a
    # little story - an opener, an obstacle, a power move, a finisher - and both sides work
    # ("R"). Holds always carry work on top, a jump is followed by something for the arms, and the
    # sets after an obstacle are filled (--fill-obstacles), so there is no standing about.
    # 1 Steampunk (117 BPM) - power: heavy combos, holds, big jumps.
    "pro-1": {
        "normal": [
            [(JAB_KICK, 2), (CURB, 2), (HOLD_JAB, 2), (SIDE_JUMP, 2)],
            [(STEP_REACH, 2), (DUCK, 2), (PUNCH_KICK, 2), (JACK, 2)],
            [(LEG_HOLD_PUNCH, 2), (DODGE, 2), (MIRROR, 2), (POWER_JUMP, 2)],
            [(SHIFT, 2), (SQUAT, 2), (FAN, 2), (CURB, 2)],
            [(LUNGE, 2), (LUNGE, 2, "R"), (SPEED_PUNCH, 2), (DUCK, 2)],
            [(CHORD, 2), (WALL_RUN, 2), (HIGH_LOW, 2), (STEP_PAIR, 2)],
            [(JAB_KICK, 2, "R"), (BALANCE, 2), (PRESS_HOLD, 2), (SIDE_JUMP, 2, "R")],
        ],
        "drop": [[(CROSS, 1), (POWER_JUMP, 2), (SPEED_PUNCH, 2), (JACK, 2), (FAN, 1)],
                 [(CROSS, 1), (SIDE_JUMP, 2), (CURB, 2), (PUNCH_KICK, 2), (SHIFT, 1)],
                 [(CROSS, 1), (CHORD, 2), (DUCK, 2), (MIRROR, 2), (POWER_JUMP, 1)]],
        "calm": [[(RAILS_HIGH, 2), (STEP_REACH, 2), (ARC, 2), (CURB, 2)],
                 [(BALANCE, 2), (HOLD_JAB, 2), (SQUAT, 4)],
                 [(WIDE, 2), (ARC_PUNCH, 2), (DODGE, 2), ("KickHold", 2)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75), (450.0, 6, 0.8)],
    },
    # 2 Chinese festival (136 BPM) - flow: the dragon dance - side jumps, fans, circles, shifts.
    "pro-2": {
        "normal": [
            [(SIDE_JUMP, 2), (FAN, 2), (DODGE, 2), (JAB_KICK, 2)],
            [(SHIFT, 2), (HOLD_JAB, 2, "R"), (CURB, 2), (MIRROR, 2)],
            [(STEP_TAP, 2), (DUCK, 2), (LEG_HOLD_PUNCH, 2), (SIDE_JUMP, 2, "R")],
            [(PUNCH_KICK, 2), (WALL_RUN, 2), (FAN, 2), (JACK, 2)],
            [(STEP_REACH, 2, "R"), (BALANCE, 2), (SPEED_PUNCH, 2), (CURB, 2)],
            [(CHORD, 2), (DODGE, 2, "R"), (HIGH_LOW, 2), (POWER_JUMP, 2)],
            [(LUNGE, 2, "R"), (LUNGE, 2), (MIRROR, 2), (DUCK, 2)],
        ],
        "drop": [[(CROSS, 1), (SIDE_JUMP, 2), (FAN, 2), (SHIFT, 2), (SPEED_PUNCH, 1)],
                 [(CROSS, 1), (JACK, 2), (DODGE, 2), (PUNCH_KICK, 2), (SIDE_JUMP, 1)],
                 [(CROSS, 1), (POWER_JUMP, 2), (MIRROR, 2), (CURB, 2), (FAN, 1)]],
        "calm": [[(ARC_PUNCH, 2), (STEP_TOUCH, 2), (ARC, 2), (DODGE, 2)],
                 [(PRESS_HOLD, 2), (BALANCE, 2), ("PunchHold", 2), (CURB, 2)],
                 [(RAILS_HIGH, 2), (WIDE, 2), (SQUAT, 4)]],
        "fill": HIGH_LOW, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(540.0, 8, 0.85), (360.0, 4, 0.75)],
    },
    # 3 Crystal cave (144 BPM, the longest) - agility: quick feet, ducks under stalactites, dodges.
    "pro-3": {
        "normal": [
            [(SPEED_STEP, 2), (DUCK, 2), (JAB_KICK, 2), (SHIFT, 2)],
            [(STEP_PAIR, 2), (DODGE, 2), (SPEED_PUNCH, 2), (CURB, 2)],
            [(HIGH_LOW, 2), (SQUAT, 2), (SIDE_JUMP, 2), (DUCK, 2)],
            [(PUNCH_KICK, 2, "R"), (WALL_RUN, 2), (STEP_TAP, 2), (JACK, 2)],
            [(LEG_HOLD_PUNCH, 2, "R"), (CURB, 2), (MIRROR, 2), (DODGE, 2, "R")],
            [(SPEED_STEP, 2), (BALANCE, 2), (FAN, 2), (POWER_JUMP, 2)],
            [(CHORD, 2), (DUCK, 2), (HOLD_JAB, 2), (SHIFT, 2)],
        ],
        "drop": [[(CROSS, 1), (SPEED_STEP, 2), (DUCK, 2), (SPEED_PUNCH, 2), (JACK, 1)],
                 [(CROSS, 1), (SHIFT, 2), (CURB, 2), (JAB_KICK, 2), (SIDE_JUMP, 1)],
                 [(CROSS, 1), (STEP_PAIR, 2), (DODGE, 2), (PUNCH_KICK, 2), (POWER_JUMP, 1)]],
        "calm": [[(BALANCE, 2), (ARC_PUNCH, 2), (DUCK, 2), ("KickHold", 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (SQUAT, 4)],
                 [(RAILS_HIGH, 2), (PRESS_HOLD, 2), (ARC, 2), (CURB, 2)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(450.0, 6, 0.8)],
    },
    # 4 Night funfair (117 BPM) - fun: jacks, jumps, mirrors and punch fans like a ride.
    "pro-4": {
        "normal": [
            [(JACK, 2), (MIRROR, 2), (CURB, 2), (STEP_REACH, 2)],
            [(POWER_JUMP, 2), (DODGE, 2), (FAN, 2), (STEP_PAIR, 2)],
            [(SIDE_JUMP, 2), (DUCK, 2), (HOLD_JAB, 2), (JACK, 2)],
            [(LUNGE, 2), (LUNGE, 2, "R"), (PUNCH_KICK, 2), (CURB, 2)],
            [(SHIFT, 2, "R"), (SQUAT, 2), (HIGH_LOW, 2, "R"), (WALL_RUN, 2)],
            [(JAB_KICK, 2), (BALANCE, 2), (MIRROR, 2), (POWER_JUMP, 2)],
            [(CHORD, 2), (DUCK, 2), (LEG_HOLD_PUNCH, 2), (SIDE_JUMP, 2)],
        ],
        "drop": [[(CROSS, 1), (JACK, 2), (MIRROR, 2), (POWER_JUMP, 2), (FAN, 1)],
                 [(CROSS, 1), (SIDE_JUMP, 2), (DUCK, 2), (SPEED_PUNCH, 2), (JACK, 1)],
                 [(CROSS, 1), (STEP_PAIR, 2), (CURB, 2), (FAN, 2), (CHORD, 1)]],
        "calm": [[(WIDE, 2), (ARC, 2), (DODGE, 2), (PRESS_HOLD, 2)],
                 [(RAILS_HIGH, 2), ("PunchHold", 2), (BALANCE, 2), (CURB, 2)],
                 [(ARC_PUNCH, 2), (STEP_TOUCH, 2), (SQUAT, 4)]],
        "fill": KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75), (720.0, 8, 0.85)],
    },
    # 5 Olympus (129 BPM, the finale) - everything at once, the densest: the gods' trial.
    "pro-5": {
        "normal": [
            [(POWER_JUMP, 2), (WALL_RUN, 2), (SPEED_PUNCH, 2), (CURB, 2)],
            [(JAB_KICK, 1), (JAB_KICK, 1, "R"), (DUCK, 2), (JACK, 2), (LEG_HOLD_PUNCH, 2)],
            [(SPEED_STEP, 2), (DODGE, 2), (FAN, 2), (SIDE_JUMP, 2)],
            [(HOLD_JAB, 2), (SQUAT, 2), (PUNCH_KICK, 2), (SHIFT, 2)],
            [(CHORD, 2), (CURB, 2), (MIRROR, 2), (DUCK, 2)],
            [(STEP_REACH, 2), (BALANCE, 2), (SPEED_PUNCH, 2), (POWER_JUMP, 2)],
            [(LEG_HOLD_PUNCH, 2, "R"), (DODGE, 2, "R"), (HIGH_LOW, 2), (JACK, 2)],
        ],
        "drop": [[(CROSS, 1), (POWER_JUMP, 2), (DUCK, 2), (SPEED_PUNCH, 2), (SIDE_JUMP, 1)],
                 [(CROSS, 1), (SPEED_STEP, 2), (CURB, 2), (PUNCH_KICK, 2), (JACK, 1)],
                 [(CROSS, 1), (SHIFT, 2), (WALL_RUN, 2), (FAN, 2), (POWER_JUMP, 1)]],
        "calm": [[(RAILS_HIGH, 2), (BALANCE, 2), (ARC, 2), (CURB, 2)],
                 [(PRESS_HOLD, 2), (DODGE, 2), (ARC_PUNCH, 2), ("KickHold", 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (SQUAT, 4)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(720.0, 8, 0.85), (450.0, 6, 0.8)],
    },
})

CHARACTER_PLANS.update({
    # Video 15 - fast tracks (135+ BPM) and a twice-as-fast tunnel: routines built on speed and
    # rhythm - quick feet and fast hands between the obstacles, jumps on the drops, and the big
    # holds only on the breaks. Same rules as pro-*: every phrase mixes an obstacle, legs, arms and
    # a jump; both sides work; obstacle sets are filled (--fill-obstacles).
    # 1 Race track - sprint: speed steps, jabs, dodges like overtaking.
    "turbo-1": {
        "normal": [
            [(SPEED_STEP, 2), (DODGE, 2), (JAB_KICK, 2), (SIDE_JUMP, 2)],
            [(SPEED_PUNCH, 2), (CURB, 2), (STEP_PAIR, 2), (DODGE, 2, "R")],
            [(PUNCH_KICK, 2), (DUCK, 2), (SHIFT, 2), (HIGH_LOW, 2)],
            [(JACK, 2), (WALL_RUN, 2), (MIRROR, 2), (POWER_JUMP, 2)],
            [(STEP_TAP, 2), (CURB, 2), (HOLD_JAB, 2), (SIDE_JUMP, 2, "R")],
            [(SPEED_STEP, 2), (DODGE, 2), (FAN, 2), (CHORD, 2)],
        ],
        "drop": [[(CROSS, 1), (SPEED_STEP, 2), (DODGE, 2), (SPEED_PUNCH, 2), (JACK, 1)],
                 [(CROSS, 1), (SIDE_JUMP, 2), (CURB, 2), (PUNCH_KICK, 2), (POWER_JUMP, 1)]],
        "calm": [[(RAILS_HIGH, 2), (STEP_REACH, 2), (ARC_PUNCH, 2), (CURB, 2)],
                 [(BALANCE, 2), (PRESS_HOLD, 2), (SQUAT, 4)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75)],
    },
    # 2 Alien planet - weightless: jumps, jacks, circles, holds floating on top.
    "turbo-2": {
        "normal": [
            [(JACK, 2), (MIRROR, 2), (DUCK, 2), (STEP_REACH, 2)],
            [(POWER_JUMP, 2), (HOLD_JAB, 2), (DODGE, 2), (SHIFT, 2)],
            [(SIDE_JUMP, 2), (FAN, 2), (CURB, 2), (LEG_HOLD_PUNCH, 2)],
            [(CHORD, 2), (SQUAT, 2), (SPEED_PUNCH, 2), (JACK, 2)],
            [(LUNGE, 2), (LUNGE, 2, "R"), (WALL_RUN, 2), (MIRROR, 2)],
            [(STEP_PAIR, 2), (DUCK, 2), (HIGH_LOW, 2, "R"), (POWER_JUMP, 2)],
        ],
        "drop": [[(CROSS, 1), (JACK, 2), (MIRROR, 2), (POWER_JUMP, 2), (FAN, 1)],
                 [(CROSS, 1), (SHIFT, 2), (DUCK, 2), (SPEED_PUNCH, 2), (SIDE_JUMP, 1)]],
        "calm": [[(ARC, 2), (BALANCE, 2), ("PunchHold", 2), (DODGE, 2)],
                 [(WIDE, 2), (ARC_PUNCH, 2), (SQUAT, 4)]],
        "fill": HIGH_LOW, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(540.0, 8, 0.85), (360.0, 4, 0.75)],
    },
    # 3 Wild West - the gunfight: quick-draw punches, ducks behind cover, kerb jumps like a horse.
    "turbo-3": {
        "normal": [
            [(JAB_KICK, 2), (DUCK, 2), (SPEED_PUNCH, 2), (CURB, 2)],
            [(STEP_TAP, 2), (DODGE, 2), (PUNCH_KICK, 2, "R"), (SIDE_JUMP, 2)],
            [(HIGH_LOW, 2), (CURB, 2), (STEP_PAIR, 2), (DUCK, 2)],
            [(SHIFT, 2), (WALL_RUN, 2), (FAN, 2), (JACK, 2)],
            [(LEG_HOLD_PUNCH, 2, "R"), (DODGE, 2, "R"), (SPEED_STEP, 2), (POWER_JUMP, 2)],
            [(JAB_KICK, 2, "R"), (SQUAT, 2), (MIRROR, 2), (CURB, 2)],
        ],
        "drop": [[(CROSS, 1), (SPEED_PUNCH, 2), (DUCK, 2), (SPEED_STEP, 2), (SIDE_JUMP, 1)],
                 [(CROSS, 1), (JACK, 2), (CURB, 2), (FAN, 2), (POWER_JUMP, 1)]],
        "calm": [[(RAILS_HIGH, 2), (STEP_REACH, 2), (ARC, 2), (DUCK, 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (SQUAT, 4)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(450.0, 6, 0.8)],
    },
    # 4 Pirate cove - on deck: rolling side jumps, rope climbs (high punches), cannon ducks.
    "turbo-4": {
        "normal": [
            [(SIDE_JUMP, 2), (HIGH_LOW, 2), (DUCK, 2), (STEP_REACH, 2, "R")],
            [(SHIFT, 2), (CURB, 2), (HOLD_JAB, 2, "R"), (JACK, 2)],
            [(PUNCH_KICK, 2), (DODGE, 2), (FAN, 2), (STEP_PAIR, 2)],
            [(POWER_JUMP, 2), (WALL_RUN, 2), (SPEED_PUNCH, 2), (SIDE_JUMP, 2, "R")],
            [(LEG_HOLD_PUNCH, 2), (SQUAT, 2), (MIRROR, 2), (CURB, 2)],
            [(SPEED_STEP, 2), (DUCK, 2), (JAB_KICK, 2), (CHORD, 2)],
        ],
        "drop": [[(CROSS, 1), (SIDE_JUMP, 2), (DUCK, 2), (MIRROR, 2), (JACK, 1)],
                 [(CROSS, 1), (POWER_JUMP, 2), (DODGE, 2), (SPEED_PUNCH, 2), (SHIFT, 1)]],
        "calm": [[(PRESS_HOLD, 2), (BALANCE, 2), (ARC_PUNCH, 2), (CURB, 2)],
                 [(WIDE, 2), ("KickHold", 2), (SQUAT, 4)]],
        "fill": KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75)],
    },
    # 5 Aztec city - the finale: the temple trial, everything, the densest.
    "turbo-5": {
        "normal": [
            [(POWER_JUMP, 2), (DUCK, 2), (SPEED_PUNCH, 2), (SIDE_JUMP, 2)],
            [(JAB_KICK, 1), (JAB_KICK, 1, "R"), (CURB, 2), (JACK, 2), (HOLD_JAB, 2)],
            [(SPEED_STEP, 2), (WALL_RUN, 2), (FAN, 2), (SHIFT, 2)],
            [(LEG_HOLD_PUNCH, 2), (DODGE, 2), (MIRROR, 2), (POWER_JUMP, 2)],
            [(CHORD, 2), (SQUAT, 2), (PUNCH_KICK, 2, "R"), (CURB, 2)],
            [(STEP_PAIR, 2), (DUCK, 2), (HIGH_LOW, 2), (JACK, 2)],
        ],
        "drop": [[(CROSS, 1), (POWER_JUMP, 2), (CURB, 2), (SPEED_PUNCH, 2), (SIDE_JUMP, 1)],
                 [(CROSS, 1), (SPEED_STEP, 2), (DUCK, 2), (FAN, 2), (JACK, 1)],
                 [(CROSS, 1), (SHIFT, 2), (DODGE, 2), (PUNCH_KICK, 2), (POWER_JUMP, 1)]],
        "calm": [[(RAILS_HIGH, 2), (BALANCE, 2), (ARC, 2), (CURB, 2)],
                 [(PRESS_HOLD, 2), (DUCK, 2), (ARC_PUNCH, 2), ("KickHold", 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (SQUAT, 4)]],
        "fill": PUNCH_KICK, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(720.0, 8, 0.85), (450.0, 6, 0.8)],
    },
})

CHARACTER_PLANS.update({
    # Video 17 ("Legends") - the user: "three or four videos with about the same buttons". Built
    # around the seven new moves (StarJump, Skater, Windmill, Ladder, Hopscotch, KneeClinch,
    # CrossJack); the familiar ones only as links between them. Each level leans on its own pair.
    # 1 Jurassic - big animals: star jumps and skater bounds.
    # Video 18 "Fiesta": the Latin moves (Cumbia, Merengue, Salsa, Samba, Maracas, Torero) as the
    # spine of every level, mixed with the obstacles and a few moves not used since video 14.
    "fiesta-1": {
        "normal": [
            [(CUMBIA, 2), (DUCK, 2), (MARACAS, 2), (SAMBA, 2)],
            [(SALSA, 2), (CURB, 2), (MERENGUE, 2), (TORERO, 2)],
            [(SAMBA, 2), (DODGE, 2), (CUMBIA, 2, "R"), (MARACAS, 2)],
            [(MERENGUE, 2), (SQUAT, 2), (SALSA, 2), (POWER_JUMP, 2)],
            [(TORERO, 2), (WALL_RUN, 2), (MARACAS, 2, "R"), (CUMBIA, 2)],
        ],
        "drop": [[(CROSS, 1), (SAMBA, 2), (CUMBIA, 2), (MARACAS, 2), (MERENGUE, 1)],
                 [(CROSS, 1), (MARACAS, 2), (DUCK, 2), (SAMBA, 2), (SALSA, 1)]],
        "calm": [[(SALSA, 2), (ARC_PUNCH, 2), (TORERO, 2), (MERENGUE, 2)],
                 [(CUMBIA, 2), (BALANCE, 2), (MARACAS, 2), (DODGE, 2)]],
        "fill": MARACAS, "drop_bars": 8, "lead_in": False, "calm_first": True,
    },
    "fiesta-2": {
        "normal": [
            [(SAMBA, 2), (CURB, 2), (SALSA, 2), (MARACAS, 2)],
            [(MERENGUE, 2), (DUCK, 2), (TORERO, 2), (CUMBIA, 2)],
            [(MARACAS, 2), (DODGE, 2, "R"), (SAMBA, 2), (SPEED_STEP, 2)],
            [(CUMBIA, 2), (SQUAT, 2), (MERENGUE, 2), (SALSA, 2, "R")],
            [(TORERO, 2), (CURB, 2), (SAMBA, 2), (HIGH_LOW, 2)],
        ],
        "drop": [[(CROSS, 1), (MERENGUE, 2), (SAMBA, 2), (CUMBIA, 2), (MARACAS, 1)],
                 [(CROSS, 1), (SAMBA, 2), (WALL_RUN, 2), (MARACAS, 2), (CUMBIA, 1)]],
        "calm": [[(TORERO, 2), (FAN, 2), (SALSA, 2), (MERENGUE, 2)],
                 [(MARACAS, 2), (LUNGE, 2), (LUNGE, 2, "R"), (DUCK, 2)]],
        "fill": SAMBA, "drop_bars": 8, "lead_in": False, "calm_first": True,
    },
    "fiesta-3": {
        "normal": [
            [(SALSA, 2), (DODGE, 2), (SAMBA, 2), (MARACAS, 2)],
            [(CUMBIA, 2), (DUCK, 2), (MERENGUE, 2), (TORERO, 2)],
            [(MARACAS, 2), (CURB, 2), (SALSA, 2, "R"), (SAMBA, 2)],
            [(MERENGUE, 2), (WALL_RUN, 2), (CUMBIA, 2), (POWER_JUMP, 2)],
        ],
        "drop": [[(CROSS, 1), (SAMBA, 2), (MARACAS, 2), (CUMBIA, 2), (SALSA, 1)],
                 [(CROSS, 1), (CUMBIA, 2), (DUCK, 2), (SAMBA, 2), (MERENGUE, 1)]],
        "calm": [[(SALSA, 2), (TORERO, 2), (MARACAS, 2), (DODGE, 2)]],
        "fill": CUMBIA, "drop_bars": 8, "lead_in": False, "calm_first": True,
    },
    "fiesta-4": {
        "normal": [
            [(MARACAS, 2), (SQUAT, 2), (CUMBIA, 2), (SAMBA, 2)],
            [(TORERO, 2), (DODGE, 2), (MERENGUE, 2), (SALSA, 2)],
            [(SAMBA, 2), (CURB, 2), (MARACAS, 2, "R"), (SPEED_STEP, 2)],
            [(SALSA, 2), (DUCK, 2), (TORERO, 2, "R"), (CUMBIA, 2)],
            [(MERENGUE, 2), (WALL_RUN, 2), (SAMBA, 2), (HIGH_LOW, 2)],
            [(CUMBIA, 2), (DUCK, 2), (MARACAS, 2), (POWER_JUMP, 2)],
        ],
        "drop": [[(CROSS, 1), (CUMBIA, 2), (SAMBA, 2), (MERENGUE, 2), (MARACAS, 1)],
                 [(CROSS, 1), (MARACAS, 2), (CURB, 2), (SAMBA, 2), (CUMBIA, 1)]],
        "calm": [[(TORERO, 2), (ARC_PUNCH, 2), (SALSA, 2), (MERENGUE, 2)],
                 [(SALSA, 2), (BALANCE, 2), (CUMBIA, 2), (DODGE, 2)]],
        "fill": MERENGUE, "drop_bars": 8, "lead_in": False, "calm_first": True,
    },
    "fiesta-5": {
        "normal": [
            [(TORERO, 2), (CURB, 2), (MARACAS, 2), (SALSA, 2)],
            [(SAMBA, 2), (DUCK, 2), (CUMBIA, 2), (MERENGUE, 2)],
            [(SALSA, 2), (DODGE, 2), (TORERO, 2, "R"), (SAMBA, 2)],
            [(MARACAS, 2), (SQUAT, 2), (MERENGUE, 2, "R"), (POWER_JUMP, 2)],
            [(CUMBIA, 2), (WALL_RUN, 2), (SAMBA, 2), (MARACAS, 2)],
        ],
        "drop": [[(CROSS, 1), (SAMBA, 2), (CUMBIA, 2), (TORERO, 2), (MARACAS, 1)],
                 [(CROSS, 1), (MERENGUE, 2), (DUCK, 2), (SAMBA, 2), (SALSA, 1)],
                 [(CROSS, 1), (MARACAS, 2), (CURB, 2), (CUMBIA, 2), (SAMBA, 1)]],
        "calm": [[(SALSA, 2), (FAN, 2), (TORERO, 2), (MERENGUE, 2)],
                 [(CUMBIA, 2), (LUNGE, 2), (LUNGE, 2, "R"), (DODGE, 2)]],
        "fill": TORERO, "drop_bars": 8, "lead_in": False, "calm_first": True,
    },
    "legend-1": {
        "normal": [
            [(STAR_JUMP, 2), (CURB, 2), (WINDMILL, 2), (SKATER, 2)],
            [(LADDER, 2), (DUCK, 2), (SKATER, 2, "R"), (HOPSCOTCH, 2)],
            [(KNEE_CLINCH, 2), (DODGE, 2), (STAR_JUMP, 2), (JAB_KICK, 2)],
            [(CROSS_JACK, 2), (SQUAT, 2), (LADDER, 2), (SKATER, 2)],
            [(HOPSCOTCH, 2), (WALL_RUN, 2), (WINDMILL, 2, "R"), (STAR_JUMP, 2)],
        ],
        "drop": [[(CROSS, 1), (STAR_JUMP, 2), (SKATER, 2), (CROSS_JACK, 2), (LADDER, 1)],
                 [(CROSS, 1), (SKATER, 2), (CURB, 2), (WINDMILL, 2), (STAR_JUMP, 1)]],
        "calm": [[(KNEE_CLINCH, 2), (ARC_PUNCH, 2), (BALANCE, 2), (LADDER, 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (WINDMILL, 2), (DODGE, 2)]],
        "fill": WINDMILL, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75)],
    },
    # 2 Magic - spells: windmills, the punch ladder, circles.
    "legend-2": {
        "normal": [
            [(WINDMILL, 2), (DUCK, 2), (LADDER, 2), (HOPSCOTCH, 2)],
            [(CROSS_JACK, 2), (CURB, 2), (KNEE_CLINCH, 2), (WINDMILL, 2, "R")],
            [(LADDER, 2), (DODGE, 2, "R"), (STAR_JUMP, 2), (SKATER, 2)],
            [(HOPSCOTCH, 2), (SQUAT, 2), (WINDMILL, 2), (CROSS_JACK, 2, "R")],
            [(SKATER, 2), (WALL_RUN, 2), (LADDER, 2), (KNEE_CLINCH, 2)],
        ],
        "drop": [[(CROSS, 1), (WINDMILL, 2), (CROSS_JACK, 2), (LADDER, 2), (STAR_JUMP, 1)],
                 [(CROSS, 1), (LADDER, 2), (DUCK, 2), (SKATER, 2), (WINDMILL, 1)]],
        "calm": [[(ARC, 2), (KNEE_CLINCH, 2), (BALANCE, 2), (WINDMILL, 2)],
                 [(PRESS_HOLD, 2), (HOPSCOTCH, 2), (ARC_PUNCH, 2), (CURB, 2)]],
        "fill": LADDER, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 2}, "finale": CROSS,
        "arcs": [(540.0, 8, 0.85), (360.0, 4, 0.75)],
    },
    # 3 Starship - zero-g: hopscotch and cross jacks, quick and light.
    "legend-3": {
        "normal": [
            [(HOPSCOTCH, 2), (DODGE, 2), (CROSS_JACK, 2), (LADDER, 2)],
            [(SKATER, 2, "R"), (DUCK, 2), (HOPSCOTCH, 2), (WINDMILL, 2)],
            [(CROSS_JACK, 2), (CURB, 2), (KNEE_CLINCH, 2), (STAR_JUMP, 2)],
            [(LADDER, 2), (WALL_RUN, 2), (HOPSCOTCH, 2), (SKATER, 2)],
            [(WINDMILL, 2), (SQUAT, 2), (CROSS_JACK, 2, "R"), (HOPSCOTCH, 2)],
        ],
        "drop": [[(CROSS, 1), (HOPSCOTCH, 2), (DODGE, 2), (CROSS_JACK, 2), (SKATER, 1)],
                 [(CROSS, 1), (CROSS_JACK, 2), (STAR_JUMP, 2), (LADDER, 2), (HOPSCOTCH, 1)]],
        "calm": [[(RAILS_HIGH, 2), (WINDMILL, 2), (BALANCE, 2), (KNEE_CLINCH, 2)],
                 [(ARC_PUNCH, 2), (LADDER, 2), (DUCK, 2), ("KickHold", 2)]],
        "fill": HOPSCOTCH, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(450.0, 6, 0.8)],
    },
    # 4 Rome - the gladiator: knee clinches, the ladder, skater bounds.
    "legend-4": {
        "normal": [
            [(KNEE_CLINCH, 2), (CURB, 2), (LADDER, 2), (SKATER, 2)],
            [(WINDMILL, 2), (DUCK, 2), (KNEE_CLINCH, 2, "R"), (STAR_JUMP, 2)],
            [(SKATER, 2), (DODGE, 2), (LADDER, 2), (CROSS_JACK, 2)],
            [(HOPSCOTCH, 2), (SQUAT, 2), (KNEE_CLINCH, 2), (WINDMILL, 2)],
        ],
        "drop": [[(CROSS, 1), (KNEE_CLINCH, 2), (SKATER, 2), (LADDER, 2), (STAR_JUMP, 1)],
                 [(CROSS, 1), (STAR_JUMP, 2), (CURB, 2), (WINDMILL, 2), (SKATER, 1)]],
        "calm": [[(PRESS_HOLD, 2), (LADDER, 2), (BALANCE, 2), (DUCK, 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (KNEE_CLINCH, 2), (CURB, 2)]],
        "fill": SKATER, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(360.0, 4, 0.75)],
    },
    # 5 Viking - the finale: every new move, the densest mix.
    "legend-5": {
        "normal": [
            [(STAR_JUMP, 2), (WALL_RUN, 2), (SKATER, 2), (LADDER, 2)],
            [(CROSS_JACK, 2), (DUCK, 2), (WINDMILL, 2), (HOPSCOTCH, 2)],
            [(KNEE_CLINCH, 2), (CURB, 2), (STAR_JUMP, 2), (SKATER, 2, "R")],
            [(LADDER, 2), (DODGE, 2), (CROSS_JACK, 2, "R"), (WINDMILL, 2)],
            [(HOPSCOTCH, 2), (SQUAT, 2), (KNEE_CLINCH, 2), (STAR_JUMP, 2)],
        ],
        "drop": [[(CROSS, 1), (STAR_JUMP, 2), (CROSS_JACK, 2), (SKATER, 2), (WINDMILL, 1)],
                 [(CROSS, 1), (WINDMILL, 2), (DUCK, 2), (LADDER, 2), (STAR_JUMP, 1)],
                 [(CROSS, 1), (HOPSCOTCH, 2), (CURB, 2), (KNEE_CLINCH, 2), (CROSS_JACK, 1)]],
        "calm": [[(RAILS_HIGH, 2), (BALANCE, 2), (ARC, 2), (LADDER, 2)],
                 [(LUNGE, 2), (LUNGE, 2, "R"), (WINDMILL, 2), (CURB, 2)]],
        "fill": STAR_JUMP, "drop_bars": 8, "lead_in": False, "calm_first": True,
        "limits": {ARC: 1}, "finale": CROSS,
        "arcs": [(720.0, 8, 0.85)],
    },
})

# ArcHold circles for a plan without its own `arcs`: (degrees, beats, radius m).
ARC_SHAPES = [(360.0, 6, 0.75), (270.0, 4, 0.7), (450.0, 6, 0.8), (540.0, 8, 0.85)]
# An ArcHold is never shorter than this many beats; an Arc set too short for it gets punches.
ARC_MIN_BEATS = 4

# The fan's rays, in turn, one per punch pair: (type, left lane, right lane). Inner lanes punch
# forward, outer lanes throw the arms out wide; low and high take turns, so the four pairs of a
# two-bar set draw a fan opening and closing.
FAN_RAYS = [(PUNCH, 1, 2), (PUNCH_HIGH, 0, 3), (PUNCH_HIGH, 1, 2), (PUNCH, 0, 3)]


def plan_sets_character(bar_lines: np.ndarray, bar_map: List[str], rest_beats: float, beat: float,
                        track_end: float, plan_name: str) -> List[WorkoutSet]:
    """Sets for a CHARACTER_PLANS routine, laid bar line to bar line like plan_sets."""
    plan = CHARACTER_PLANS[plan_name]
    drops = drops_from_bar_map(bar_map)
    cursor = {"normal": 0, "drop": 0, "calm": 0}
    sets: List[WorkoutSet] = []
    history: List[str] = []
    limits = dict(plan.get("limits", {}))     # exercise -> how many sets of it the track may have

    def emit(ex: str, bar: int, bars: int, mirror: bool = False) -> bool:
        if bar + bars >= len(bar_lines):
            return False
        start, boundary = float(bar_lines[bar]), float(bar_lines[bar + bars])
        if start >= track_end:
            return False
        if ex in OBSTACLE_SETS and history and history[-1] in OBSTACLE_SETS:
            ex = plan["fill"]          # the camera has to come home between two obstacle sets
        if ex == ARC and history and history[-1] == ARC:
            ex = plan["fill"]          # a rest between two circles: never one straight after another
        if ex in limits:
            if limits[ex] <= 0: ex, mirror = plan["fill"], False
            else: limits[ex] -= 1
        end = max(start, boundary - rest_beats * beat)
        sets.append(WorkoutSet(index=len(sets), exercise=ex, start=start, end=min(end, track_end),
                               rest_end=min(boundary, track_end), bar=bar, mirror=mirror))
        history.append(ex)
        return True

    bar = _START_BAR[0]
    while bar < len(bar_lines):
        in_drop = any(0 <= bar - d < plan["drop_bars"] for d in drops)
        quiet = bar < len(bar_map) and bar_map[bar] != "#"
        # `calm_first`: a bar without drums is a break even inside a drop's stretch.
        if quiet and plan.get("calm_first"):
            kind = "calm"
        elif in_drop:
            kind = "drop"
        elif quiet:
            kind = "calm"
        else:
            kind = "normal"
        phrases = plan[kind]
        phrase = phrases[cursor[kind] % len(phrases)]
        cursor[kind] += 1
        # The next drop starts a phrase of its own: this one stops at the last set that ends on or
        # before it, and one-bar sets of the plan's filler exercise cover whatever is left.
        ahead = [d for d in drops if bar < d < bar + sum(item[1] for item in phrase)]
        limit = ahead[0] if (ahead and kind != "drop") else None
        for item in phrase:
            ex, bars = item[0], item[1]
            mirror = len(item) > 2 and item[2] == "R"
            if limit is not None and bar + bars > limit:
                # The run-up to the drop: fillers, then (with `lead_in`) an Arc set of up to four
                # bars that ends on the drop.
                arc_bars = min(4, limit - bar) if plan.get("lead_in") and limit - bar >= 2 else 0
                while bar < limit - arc_bars:
                    if not emit(plan["fill"], bar, 1): return sets
                    bar += 1
                if arc_bars and bar < limit:
                    if not emit(ARC, bar, limit - bar): return sets
                    bar = limit
                break
            if not emit(ex, bar, bars, mirror): return finale(sets)
            bar += bars
    return finale(sets)


def finale(sets: List[WorkoutSet]) -> List[WorkoutSet]:
    """The competitor's last move of a level is the star; plans with a `finale` exercise end on it
    (applied by plan_sets_character's caller through _FINALE)."""
    if _FINALE[0] and sets:
        sets[-1].exercise = _FINALE[0]
        sets[-1].mirror = False
    return sets


_FINALE: List[Optional[str]] = [None]
# The bar a competitor-style plan starts on: the --intro-rest line, so a held pose there is not cut.
_START_BAR: List[int] = [0]


def mirror_sets(events: List[Event], sets: List[WorkoutSet], lane_count: int) -> None:
    """The sets planned with "R" lead with the right: lane l becomes lane_count-1-l and every
    ArcHold turns the other way, so the motif is the same movement on the other side."""
    mirrored = {b.index for b in sets if b.mirror}
    for ev in events:
        if ev.set_index in mirrored:
            ev.lane = lane_count - 1 - ev.lane
            ev.arc = -ev.arc


def expand_compound(events: List[Event], sets: List[WorkoutSet], beat: float, lane_count: int,
                    rng: random.Random, arc_shapes: Optional[List[Tuple[float, int, float]]] = None) -> List[Event]:
    """Writes the variety exercises out as notes. The whole shape of each one lives here:

      * SideJump - Kick pairs 0+1 / 2+3 alternating;  Jack - 0+3 / 1+2;  Shift - a pair walking
        one lane per beat and back (0+1, 1+2, 2+3, 1+2, ...);
      * HighLow - alternate hands, low Punch and PunchHigh in turn, every fourth beat both at once;
      * Cross - two PunchHolds and two KickHolds on the outer lanes, held to a beat before the next;
      * WallRun - one Dodge along the set (its duration), Kicks alternating on the two free lanes and
        a Punch on every other beat above them;
      * Balance - one CurbGap (a random free lane) or CurbSplit held for the set, nothing else.
    """
    by_set: Dict[int, List[Event]] = {}
    out: List[Event] = []
    for ev in events:
        if ev.type in COMPOUND: by_set.setdefault(ev.set_index, []).append(ev)
        else: out.append(ev)

    def note(src: Event, type_: str, lane: int, duration: float = 0.0) -> Event:
        return Event(time=src.time, type=type_, intensity=src.intensity, lane=lane, duration=duration,
                     frame=src.frame, bands=src.bands, acoustic=src.acoustic, set_index=src.set_index)

    shift_path = [(0, 1), (1, 2), (2, 3), (1, 2)]
    wall_side = 0
    balance_count = 0
    arc_count = 0
    for block in sets:
        members = sorted(by_set.get(block.index, []), key=lambda e: e.time)
        ex = block.exercise
        if not members and ex not in (ARC, SQUAT, RAILS, SQUAT_PUNCH, RAILS_HIGH): continue
        made: List[Event] = []
        if ex == ARC:
            # One mirrored pair on the set's first beat (its bar line - on the beat even where the
            # drums are silent), then steps on the beats after it ends, one per beat, left/right.
            first = members[0] if members and members[0].time - block.start < 0.25 * beat else None
            src = first or Event(time=block.start, type=ARC, intensity=0.6, set_index=block.index)
            src.time = first.time if first else block.start
            shapes = arc_shapes or ARC_SHAPES
            degrees, beats, radius = shapes[arc_count % len(shapes)]
            outward = arc_count % 2 == 0
            arc_count += 1
            # Starts on the set's first beat and ends on a beat: a whole number of beats, no more
            # than the set holds before its rest. Cut short, the circle keeps its speed (degrees
            # shrink with the beats, in quarter turns, never under three quarters).
            fit = int(np.floor((block.end - src.time) / beat + 1e-3))
            use = min(int(beats), fit)
            if use >= ARC_MIN_BEATS:
                if use < beats: degrees = max(270.0, round(degrees * use / beats / 90.0) * 90.0)
                length = use * beat
                left_sweep = -degrees if outward else degrees      # + = clockwise as the player sees it
                for lane, sweep in ((1, left_sweep), (2, -left_sweep)):
                    ev = note(src, ARC_HOLD, lane, length)
                    ev.arc, ev.radius = sweep, radius
                    made.append(ev)
                resume = src.time + length + 0.75 * beat
                for n, ev in enumerate(m for m in members if m.time >= resume):
                    made.append(note(ev, KICK, 1 + n % 2))
            else:
                for n, ev in enumerate(members):
                    made.append(note(ev, PUNCH, 1 + n % 2))
        elif ex == ARC_PUNCH:
            # The lead (left) hand circles - one ArcHold from the set's first beat, whole beats, a
            # turn a bar - while the rear hand punches on every beat after the first, low and high
            # in turn; once the circle ends, steps.
            first = members[0] if members and members[0].time - block.start < 0.25 * beat else None
            src = first or Event(time=block.start, type=ARC_PUNCH, intensity=0.6, set_index=block.index)
            src.time = first.time if first else block.start
            use = min(8, int(np.floor((block.end - src.time) / beat + 1e-3)))
            if use >= ARC_MIN_BEATS:
                length = use * beat
                arc = note(src, ARC_HOLD, 1, length)
                arc.arc, arc.radius = -360.0 * use / 4.0, 0.7          # outward: left anticlockwise
                made.append(arc)
                n = 0
                for ev in members:
                    if ev.time < src.time + 0.5 * beat: continue
                    if ev.time <= src.time + length - 0.25 * beat:
                        made.append(note(ev, PUNCH if n % 2 == 0 else PUNCH_HIGH, 2))
                    elif ev.time >= src.time + length + 0.75 * beat:
                        made.append(note(ev, KICK, 1 + n % 2))
                    n += 1
            else:
                for n, ev in enumerate(members):
                    made.append(note(ev, PUNCH, 1 + n % 2))
        elif ex == STEP_PAIR:
            # Together, step, together, step: both feet on the beat, then one foot, in turn.
            for n, ev in enumerate(members):
                if n % 2 == 0:
                    made.append(note(ev, KICK, 1))
                    made.append(note(ev, KICK, 2))
                else:
                    made.append(note(ev, KICK, 1 + (n // 2) % 2))
        elif ex == PUNCH_PAIR:
            # The double punch, then a single: low pair, jab, high pair, jab.
            for n, ev in enumerate(members):
                if n % 2 == 0:
                    kind = PUNCH if n % 4 == 0 else PUNCH_HIGH
                    made.append(note(ev, kind, 1))
                    made.append(note(ev, kind, 2))
                else:
                    made.append(note(ev, PUNCH, 1 + (n // 2) % 2))
        elif ex in (SPEED_PUNCH, SPEED_STEP):
            # On the beat and the eighth after it, hands (feet) alternating: 1-2, 1-2...
            kind = PUNCH if ex == SPEED_PUNCH else KICK
            hand = 0
            for ev in members:
                made.append(note(ev, kind, 1 + hand % 2)); hand += 1
                if ev.time + 0.5 * beat < block.end - 0.05 * beat:
                    off = note(ev, kind, 1 + hand % 2); off.time = ev.time + 0.5 * beat
                    made.append(off); hand += 1
        elif ex in (HOLD_JAB, LEG_HOLD_PUNCH, PRESS_HOLD):
            # Four-beat chunks from the set's beats. HoldJab: one fist held, the other punching on
            # the other three beats; LegHoldPunch: one leg held out, both fists on top in turn;
            # PressHold: both fists held two beats, then a step each side. Sides swap each chunk.
            for c in range(0, len(members), 4):
                chunk = members[c:c + 4]
                side = 1 + (c // 4) % 2
                first = chunk[0]
                beats_held = min(4, int(np.floor((block.end - first.time) / beat + 1e-3)))
                if ex == PRESS_HOLD:
                    if beats_held >= 2:
                        made.append(note(first, "PunchHold", 1, 1.75 * beat))
                        made.append(note(first, "PunchHold", 2, 1.75 * beat))
                    for k, ev in enumerate(chunk[2:]): made.append(note(ev, KICK, 1 + k % 2))
                    continue
                if beats_held < 2:
                    for k, ev in enumerate(chunk): made.append(note(ev, PUNCH, 1 + k % 2))
                    continue
                hold_type = "PunchHold" if ex == HOLD_JAB else "KickHold"
                made.append(note(first, hold_type, side, (beats_held - 0.5) * beat))
                for k, ev in enumerate(chunk[1:beats_held]):
                    kind = PUNCH if k % 2 == 0 else PUNCH_HIGH
                    lane = 3 - side if ex == HOLD_JAB else 1 + k % 2
                    made.append(note(ev, kind, lane))
        elif ex in (SQUAT_PUNCH, RAILS_HIGH):
            # The held pose for the set, a punch on every beat on top of it, hands alternating.
            t0 = members[0].time if members and members[0].time - block.start < 0.25 * beat else block.start
            src = members[0] if members else Event(time=t0, type=ex, intensity=0.6, set_index=block.index)
            beats_held = int(np.floor((block.end - t0) / beat + 1e-3))
            if beats_held >= 2:
                pose = note(src, SQUAT_HOLD_TYPE if ex == SQUAT_PUNCH else RAILS_TYPE, 1, beats_held * beat)
                pose.time = t0
                made.append(pose)
                for n, ev in enumerate(m for m in members if m.time >= t0 + 0.75 * beat):
                    made.append(note(ev, PUNCH if ex == SQUAT_PUNCH else PUNCH_HIGH, 1 + n % 2))
        elif ex in (SQUAT, RAILS):
            # One held pose for the set, from its first beat to the end of its active part, whole beats.
            t0 = members[0].time if members and members[0].time - block.start < 0.25 * beat else block.start
            src = members[0] if members else Event(time=t0, type=ex, intensity=0.6, set_index=block.index)
            beats_held = int(np.floor((block.end - t0) / beat + 1e-3))
            if beats_held >= 2:
                pose = note(src, SQUAT_HOLD_TYPE if ex == SQUAT else RAILS_TYPE, 1, beats_held * beat)
                pose.time = t0
                made.append(pose)
                if ex == RAILS:
                    for n, ev in enumerate(m for m in members if m.time >= t0 + 0.75 * beat):
                        if n % 2 == 0: made.append(note(ev, PUNCH, 1 + (n // 2) % 2))
        elif ex == LUNGE:
            # The left foot out on its strip for the set; the right foot taps every other beat.
            first = members[0]
            beats_held = int(np.floor((block.end - first.time) / beat + 1e-3))
            if beats_held >= 2:
                made.append(note(first, "KickHold", 0, beats_held * beat - 0.25 * beat))
                for ev in members[1:]:
                    made.append(note(ev, KICK, 2))      # a lunge pulse on every beat
            else:
                for n, ev in enumerate(members): made.append(note(ev, KICK, 1 + n % 2))
        elif ex == WIDE:
            # Out (two strips apart, held a beat), in (both feet together), every two beats.
            for n, ev in enumerate(members):
                if n % 2 == 0:
                    made.append(note(ev, "KickHold", 0, 0.75 * beat))
                    made.append(note(ev, "KickHold", lane_count - 1, 0.75 * beat))
                else:
                    made.append(note(ev, KICK, 1))
                    made.append(note(ev, KICK, 2))
        elif ex in (STEP_TOUCH, JAB_CROSS):
            for n, ev in enumerate(members):
                made.append(note(ev, KICK if ex == STEP_TOUCH else PUNCH, 1 + n % 2))
        elif ex == JAB_KICK:
            for n, ev in enumerate(members):
                k = n % 4           # jab L, cross R, jab L, kick R - the rear foot after the rear hand
                if k == 3: made.append(note(ev, KICK, 2))
                else: made.append(note(ev, PUNCH, 2 if k == 1 else 1))
        elif ex == STEP_REACH:
            for n, ev in enumerate(members):
                k = n % 4           # step L + reach R, step R + reach L: opposite hand and foot, one at a time
                if k == 0: made.append(note(ev, KICK, 1))
                elif k == 1: made.append(note(ev, PUNCH_HIGH, 2))
                elif k == 2: made.append(note(ev, KICK, 2))
                else: made.append(note(ev, PUNCH_HIGH, 1))
        elif ex == POWER_JUMP:
            for ev in members:
                for lane in (1, 2):
                    made.append(note(ev, KICK, lane))
                    made.append(note(ev, PUNCH_HIGH, lane))
        elif ex == STAR_JUMP:
            for n, ev in enumerate(members):
                if n % 2 == 0:
                    for lane in (0, lane_count - 1):
                        made.append(note(ev, KICK, lane))
                        made.append(note(ev, PUNCH_HIGH, lane))
                else:
                    made.append(note(ev, KICK, 1)); made.append(note(ev, KICK, lane_count - 2))
        elif ex == SKATER:
            for n, ev in enumerate(members):
                out_lane = 0 if n % 2 == 0 else lane_count - 1
                made.append(note(ev, KICK, out_lane))
                made.append(note(ev, PUNCH, lane_count - 2 if out_lane == 0 else 1))
        elif ex == WINDMILL:
            for n, ev in enumerate(members):
                high = 0 if n % 2 == 0 else lane_count - 1
                made.append(note(ev, PUNCH_HIGH, high))
                made.append(note(ev, KICK, lane_count - 2 if high == 0 else 1))
        elif ex == LADDER:
            for n, ev in enumerate(members):
                k = n % 5
                if k < 2: made.append(note(ev, PUNCH, 1 + k))
                elif k < 4: made.append(note(ev, PUNCH_HIGH, 1 + (k - 2)))
                else: made.append(note(ev, PUNCH_HIGH, 1)); made.append(note(ev, PUNCH_HIGH, 2))
        elif ex == HOPSCOTCH:
            for n, ev in enumerate(members):
                k = n % 4
                if k % 2 == 0: made.append(note(ev, KICK, 1)); made.append(note(ev, KICK, lane_count - 2))
                else: made.append(note(ev, KICK, 0 if k == 1 else lane_count - 1))
        elif ex == KNEE_CLINCH:
            for n, ev in enumerate(members):
                made.append(note(ev, "KickHold", 1 if n % 2 == 0 else lane_count - 2, beat))
                made.append(note(ev, PUNCH_HIGH, 1)); made.append(note(ev, PUNCH_HIGH, lane_count - 2))
        elif ex == CROSS_JACK:
            for n, ev in enumerate(members):
                left = n % 2 == 0
                made.append(note(ev, KICK, 0 if left else 2)); made.append(note(ev, KICK, 1 if left else 3))
                made.append(note(ev, PUNCH_HIGH, lane_count - 1 if left else 0))
        elif ex == CUMBIA:
            for n, ev in enumerate(members):
                k = n % 4           # out L + right fist high, in L, out R + left fist high, in R
                if k == 0: made.append(note(ev, KICK, 0)); made.append(note(ev, PUNCH_HIGH, lane_count - 1))
                elif k == 1: made.append(note(ev, KICK, 1))
                elif k == 2: made.append(note(ev, KICK, lane_count - 1)); made.append(note(ev, PUNCH_HIGH, 0))
                else: made.append(note(ev, KICK, lane_count - 2))
        elif ex == MERENGUE:
            for n, ev in enumerate(members):
                made.append(note(ev, KICK, 1 + n % 2))
                if n % 4 == 3: made.append(note(ev, PUNCH, 1)); made.append(note(ev, PUNCH, 2))
        elif ex == SALSA:
            for n, ev in enumerate(members):
                k = n % 8           # L R L reach-R-high | R L R reach-L-high
                if k == 3: made.append(note(ev, PUNCH_HIGH, lane_count - 1))
                elif k == 7: made.append(note(ev, PUNCH_HIGH, 0))
                else: made.append(note(ev, KICK, 1 if k in (0, 2, 5) else 2))
        elif ex == SAMBA:
            for n, ev in enumerate(members):
                if n % 2 == 0: made.append(note(ev, KICK, 1)); made.append(note(ev, KICK, lane_count - 2))
                else: made.append(note(ev, PUNCH, 0)); made.append(note(ev, PUNCH, lane_count - 1))
        elif ex == MARACAS:
            for n, ev in enumerate(members):
                k = n % 4           # high L + low R, then low L + high R: the diagonals in turn
                made.append(note(ev, PUNCH_HIGH if k in (0, 3) else PUNCH, 1))
                made.append(note(ev, PUNCH if k in (0, 3) else PUNCH_HIGH, 2))
        elif ex == TORERO:
            for n, ev in enumerate(members):
                out_lane = 0 if n % 2 == 0 else lane_count - 1
                made.append(note(ev, "KickHold", out_lane, 1.5 * beat))
                made.append(note(ev, PUNCH_HIGH, lane_count - 1 - out_lane))
        elif ex == STEP_TAP:
            for n, ev in enumerate(members):
                # step L, jab R, step R, jab L: one limb a beat, crossing the body.
                k = n % 4
                if k == 0: made.append(note(ev, KICK, 1))
                elif k == 1: made.append(note(ev, PUNCH, 2))
                elif k == 2: made.append(note(ev, KICK, 2))
                else: made.append(note(ev, PUNCH, 1))
        elif ex == MIRROR:
            for n, ev in enumerate(members):
                kind = PUNCH_HIGH if n % 2 else PUNCH
                made.append(note(ev, kind, 1))
                made.append(note(ev, kind, 2))
        elif ex == PUNCH_KICK:
            for n, ev in enumerate(members):
                hand = 1 + n % 2
                made.append(note(ev, PUNCH_HIGH if n % 4 == 3 else PUNCH, hand))
                made.append(note(ev, KICK, 3 - hand))     # the opposite foot
        elif ex == FAN:
            for n, ev in enumerate(members):
                if n % 2 == 0:
                    kind, left, right = FAN_RAYS[(n // 2) % len(FAN_RAYS)]
                    made.append(note(ev, kind, left))
                    made.append(note(ev, kind, right))
                else:
                    made.append(note(ev, KICK, 1 if (n // 2) % 2 == 0 else 2))
        elif ex == WALL_RUN:
            first = members[0]
            wall_left = wall_side % 2 == 0
            wall_side += 1
            length = max(beat, block.rest_end - first.time - beat)
            made.append(note(first, DODGE, 0 if wall_left else lane_count - 1, length))
            free = (2, 3) if wall_left else (0, 1)
            for n, ev in enumerate(members[1:]):
                if ev.time > first.time + length - 0.5 * beat: break
                made.append(note(ev, KICK, free[n % 2]))
                if n % 2 == 1: made.append(note(ev, PUNCH, free[(n + 1) % 2]))
        elif ex == BALANCE:
            first = members[0]
            length = max(beat, block.end - first.time)
            # One leg and the wide stance take turns; the free lane of a one-leg kerb is random.
            if balance_count % 2 == 0: made.append(note(first, CURB_GAP, rng.randrange(lane_count), length))
            else: made.append(note(first, CURB_SPLIT, 1, length))
            balance_count += 1
        elif ex == CROSS:
            for n, ev in enumerate(members):
                limit = members[n + 1].time if n + 1 < len(members) else block.end
                length = max(0.5 * beat, limit - ev.time - beat)
                for lane in (0, lane_count - 1):
                    made.append(note(ev, "PunchHold", lane, length))
                    made.append(note(ev, "KickHold", lane, length))
        else:
            for n, ev in enumerate(members):
                if ex == SIDE_JUMP: pair = (0, 1) if n % 2 == 0 else (2, 3)
                elif ex == JACK: pair = (0, lane_count - 1) if n % 2 == 0 else (1, 2)
                elif ex == SHIFT: pair = shift_path[n % len(shift_path)]
                else: pair = None
                if pair is not None:
                    made.append(note(ev, KICK, pair[0]))
                    made.append(note(ev, KICK, pair[1]))
                elif ex == HIGH_LOW:
                    hand = 1 + n % 2
                    if n % 4 == 3:
                        made.append(note(ev, PUNCH, 1))
                        made.append(note(ev, PUNCH_HIGH, 2))
                    else:
                        made.append(note(ev, PUNCH_HIGH if (n // 2) % 2 else PUNCH, hand))
        block.notes = len(made)
        out.extend(made)
    out.sort(key=lambda e: e.time)
    return out


def fill_obstacle_sets(events: List[Event], sets: List[WorkoutSet], beat: float, lane_count: int) -> List[Event]:
    """Video 14 (the user: "you just get going and - a pause, standing doing nothing"): a Curb,
    Duck or Dodge set holds ONE obstacle (space_obstacles keeps one per set) and left the rest of
    its two bars empty. Now the obstacle stays on its beat and two beats after it a combo runs to
    the set's end, one note a beat, picked so it follows from the obstacle:
      * after a Duck (coming up from the crouch): uppercuts, then a punch+kick combo;
      * after a Curb (just landed): alternating steps with a double punch, or knee taps;
      * after a Dodge (back in the middle): jab-cross with a step on every other beat.
    The variants take turns per obstacle type, so no two obstacle sets end the same way."""
    turn = {DUCK: 0, CURB: 0, DODGE: 0}
    added: List[Event] = []
    inner = (1, lane_count - 2)
    for block in sets:
        if block.exercise not in (DUCK, CURB, DODGE): continue
        obst = [e for e in events if e.set_index == block.index and e.type in OBSTACLE_TYPES]
        src = obst[0] if obst else None
        t0 = src.time if src else block.start - 2 * beat
        kind = block.exercise
        v = turn[kind]
        turn[kind] += 1
        n = 0
        t = t0 + 2 * beat
        while t < block.end - 0.25 * beat:
            base = Event(time=t, type=PUNCH, intensity=src.intensity if src else 0.8, lane=inner[n % 2],
                         acoustic=PUNCH, set_index=block.index)
            def put(type_: str, lane: int) -> None:
                added.append(Event(time=t, type=type_, intensity=base.intensity, lane=lane, acoustic=base.acoustic, set_index=block.index))
            side = inner[n % 2]
            other = inner[(n + 1) % 2]
            if kind == DUCK:
                if v % 2 == 0: put(PUNCH_HIGH, side)                       # uppercuts out of the crouch
                else: put(PUNCH, side); put(KICK, other) if n % 2 == 1 else None   # punch, punch+kick
            elif kind == CURB:
                if v % 2 == 0:
                    put(KICK, side)                                         # steps...
                    if n % 2 == 1: put(PUNCH, inner[0]); put(PUNCH, inner[1])   # ...with a double punch
                else: put(KICK, 0 if n % 2 == 0 else lane_count - 1)        # wide knee taps
            else:
                put(PUNCH, side)                                            # jab-cross...
                if n % 2 == 1: put(KICK, other)                             # ...a step every other beat
            n += 1
            t += beat
        block.notes += n
    return sorted(events + added, key=lambda e: e.time)


def melody_onset_events(y: np.ndarray, sr: int, hop: int, start: float, end: float, beat: float,
                        cfg) -> List[Event]:
    """Notes on the MELODY for a stretch with no drums in it (--intro-melody-onsets).

    The grid has nothing to follow before the kicks come in, so a chart there sits on beats the
    listener cannot hear. Here the harmonic part of the audio (HPSS) is run through an onset
    detector instead, and its onsets become light notes: no closer than an eighth note at the
    track's tempo (of two too close, the stronger stays), the stronger half Punches and the rest
    steps (Kick), each alternating left/right. No chords, no holds.

    The detector says WHICH note, not exactly WHEN: the onset-strength peak trails the attack (on
    track7_1 by a median 32 ms, up to 60 ms, measured against the mix's RMS rise). So every onset
    is moved to its attack in the full mix with refine_onset - the same envelope rule, windows
    and rise fraction the drum notes are timed with - and one the envelope cannot place is kept
    where the detector put it.
    """
    if end - start < beat:
        return []
    a, b = max(0, int(start * sr)), min(len(y), int(end * sr))
    harmonic = librosa.effects.hpss(y[a:b])[0]
    env = librosa.onset.onset_strength(y=harmonic, sr=sr, hop_length=hop)
    if env.size == 0 or env.max() <= 0:
        return []
    env = env / env.max()
    frames = librosa.onset.onset_detect(onset_envelope=env, sr=sr, hop_length=hop, units="frames",
                                        delta=cfg.intro_melody_delta, wait=1, normalize=False)
    refine_hop = max(1, int(round(cfg.refine_hop * sr)))
    attack_env = rms_envelope(y[a:b], max(2, int(round(cfg.refine_high_window * sr))), refine_hop)
    attack_rate = sr / refine_hop
    gap = beat / 2.0
    picked: List[Tuple[float, float]] = []
    for f in frames:
        t, strength = start + float(f) * hop / sr, float(env[f])
        refined = refine_onset(attack_env, attack_rate, t - start, cfg.intro_melody_search, cfg.refine_rise)
        if refined is not None:
            t = start + float(np.clip(refined, t - start - cfg.intro_melody_search, t - start + cfg.intro_melody_search))
        if strength < cfg.intro_melody_min:
            continue
        if picked and t - picked[-1][0] < gap:
            if strength > picked[-1][1]: picked[-1] = (t, strength)
            continue
        picked.append((t, strength))
    if not picked:
        return []
    median = float(np.median([p[1] for p in picked]))
    out: List[Event] = []
    punches = steps = 0
    for t, strength in picked:
        if strength >= median:
            type_, lane = PUNCH, 1 + punches % 2
            punches += 1
        else:
            type_, lane = KICK, 1 + steps % 2
            steps += 1
        out.append(Event(time=t, type=type_, intensity=float(np.clip(0.3 + 0.5 * strength, 0.0, 1.0)), lane=lane,
                         frame=int(round(t * sr / hop)), acoustic=type_))
    return out


def first_drum_bar(bar_map: List[str]) -> Optional[int]:
    """Where the drums really come in: the first of two drum bars in a row. A single drum bar on
    its own in an intro is a fill or a riser, not the beat starting - track7_1 has one at bar 8,
    four bars before its kicks, and taking it as the start put grid notes on the melody."""
    for i in range(len(bar_map) - 1):
        if bar_map[i] == "#" and bar_map[i + 1] == "#":
            return i
    return bar_map.index("#") if "#" in bar_map else None


def intro_arc_events(bar_lines: np.ndarray, drums_bar: int, beat: float) -> List[Event]:
    """--intro-arcs: the intro before the drums as a chain of long ArcHolds, one per phrase.

    Phrases are four bars from the first bar line; what does not divide goes to the last phrase
    (up to seven bars). Each ArcHold starts on its phrase's bar line and runs to one beat before
    the next phrase - whole beats, so it ends on a beat and leaves a one-beat breath - turning at
    one revolution a bar (360 degrees x its length in bars). Hands alternate L, R, L...; the
    last phrase before the drums is both hands at once, mirrored (each turning outward).
    """
    bars = drums_bar
    if bars < 2:
        return []
    sizes = [4] * (bars // 4)
    rest = bars % 4
    if not sizes: sizes = [bars]
    elif rest: sizes[-1] += rest
    out: List[Event] = []
    bar = 0
    for k, size in enumerate(sizes):
        start = float(bar_lines[bar])
        end = float(bar_lines[bar + size]) if bar + size < len(bar_lines) else start + size * 4 * beat
        beats = int(round((end - start) / beat)) - 1
        bar += size
        if beats < 2: continue
        length = beats * beat
        degrees = 360.0 * beats / 4.0
        last = k == len(sizes) - 1
        hands = (1, 2) if last else ((1,) if k % 2 == 0 else (2,))
        for lane in hands:
            ev = Event(time=start, type=ARC_HOLD, intensity=0.6, lane=lane, duration=length, acoustic=PUNCH)
            ev.arc = -degrees if lane == 1 else degrees      # outward: left anticlockwise, right clockwise
            ev.radius = 0.0
            out.append(ev)
    return out


def retire_jump(events: List[Event]) -> None:
    """Turn the acoustic Jump label into the exercise that replaced it.

    Runs last, after every decision that cares which band a note came from has been made, so
    the classifier and the timing refinement above keep working in the vocabulary of the audio
    while nothing downstream of here ever sees a type the game no longer spawns.
    """
    for ev in events:
        if ev.type == JUMP:
            ev.type = EXERCISE_FOR_ACOUSTIC_JUMP
            ev.duration = 0.0


def space_obstacles(events: List[Event], min_gap: float) -> List[Event]:
    """Drop any obstacle that lands too soon after the previous one, wherever it came from.

    Per-exercise `--spacing` can only see inside one set, and the gap that matters here spans
    set boundaries: a set of walls ending on its last bar line followed by a set of barriers
    starting on the next one puts two obstacles within a beat of each other, and the camera is
    still returning from the first when the second asks it to move again.

    Obstacles are DROPPED rather than moved. Moving one would take it off the onset it was
    placed on, and an obstacle that arrives off the beat is worse than no obstacle at all - the
    whole reaction is timed against the music.
    """
    kept: List[Event] = []
    last = -1e9
    for ev in events:
        if ev.type in OBSTACLE_TYPES:
            if ev.time - last < min_gap:
                continue
            last = ev.time
        kept.append(ev)
    return kept


# --------------------------------------------------------------------------------------
# Lane assignment - swap the strategy here, nothing else needs to know
# --------------------------------------------------------------------------------------
#
# Every strategy takes the final, time-sorted event list and writes `ev.lane` in place.
# Adding one means writing a function of this shape and registering it in LANE_STRATEGIES;
# no other part of the pipeline reads or assumes anything about lane numbers.

def lanes_alternate(events: List[Event], lane_count: int, rng: random.Random) -> None:
    """Plain round-robin: 0, 1, 2, 3, 0, ... Predictable and evenly spread.

    It does not repeat a lane back to back, though `resolve_hold_overlaps` can nudge a note
    out of a lane a hold is occupying, and a nudge occasionally lands next to its neighbour.
    """
    for index, ev in enumerate(events):
        ev.lane = index % lane_count


def lanes_frequency(events: List[Event], lane_count: int, rng: random.Random) -> None:
    """Left to right by brightness: bass on the outer left lane, cymbals on the right.

    Readable on a mix with clearly separated parts; on a dense track it tends to pile most
    notes into one or two lanes, which is exactly why it is not the default.
    """
    for ev in events:
        lo, mid, hi = ev.bands
        total = lo + mid + hi
        brightness = 0.0 if total <= 1e-9 else (0.5 * mid + hi) / total
        ev.lane = min(lane_count - 1, max(0, int(brightness * lane_count)))


def lanes_random(events: List[Event], lane_count: int, rng: random.Random) -> None:
    """Seeded random, never twice in a row. Reproducible for a given --seed."""
    previous = -1
    for ev in events:
        choices = [lane for lane in range(lane_count) if lane != previous]
        if not choices:
            choices = list(range(lane_count))
        ev.lane = rng.choice(choices)
        previous = ev.lane


LANE_STRATEGIES: Dict[str, Callable[[List[Event], int, random.Random], None]] = {
    "alternate": lanes_alternate,
    "frequency": lanes_frequency,
    "random": lanes_random,
}


# --------------------------------------------------------------------------------------
# Filtering
# --------------------------------------------------------------------------------------

def filter_min_interval(events: List[Event], min_interval: float) -> List[Event]:
    """Thin out clusters so a dense track cannot spawn 20 notes a second.

    Within a cluster the STRONGEST onset wins rather than the first. Keeping the first is
    the cheaper rule and it is subtly wrong on real drums: a snare's ghost note routinely
    precedes the hit by a few tens of milliseconds, so first-wins quantises the chart onto
    the ghosts and the whole map drifts early of the beat.

    A swap is refused when it would put the replacement too close to the event BEFORE the
    one being replaced - otherwise closing one gap opens a shorter one behind it.
    """
    kept: List[Event] = []
    for ev in events:
        if kept and ev.time - kept[-1].time < min_interval:
            stronger = ev.intensity > kept[-1].intensity
            room_behind = len(kept) < 2 or ev.time - kept[-2].time >= min_interval
            if stronger and room_behind:
                kept[-1] = ev
            continue
        kept.append(ev)
    return kept


def resolve_hold_overlaps(events: List[Event], lane_count: int, gap: float) -> None:
    """Keep each hold's lane clear for its whole sustain, moving later notes aside.

    Two notes in one lane at the same time is nonsense on screen, but the fix is to move
    the newcomer to a free lane rather than to shorten the hold: a hold exists precisely
    because the music sustains, and trimming it back to the next unrelated note is how the
    feature ends up never firing.

    Only when EVERY lane is occupied by a hold does a hold get trimmed, and then it is the
    one actually in the way. A hold trimmed to nothing degrades back to its one-shot type,
    which is not cosmetic: a hold whose `duration` rounds to 0.0 is exactly the
    inconsistency `BeatmapReader` refuses to load the file over.

    This runs after lane assignment on purpose. Lane strategies stay free to propose
    whatever they like, and the one gameplay invariant they must not violate is enforced
    here, once, instead of being reimplemented in each of them.
    """
    active: List[Tuple[int, float, int]] = []          # (lane, end time, event index)

    for index, ev in enumerate(events):
        active = [hold for hold in active if hold[1] + gap > ev.time]
        busy = {hold[0] for hold in active}

        # An obstacle does not stand in a lane - it spans the road, or, for a Dodge, its lane
        # names which SIDE the wall is on. Nudging one sideways to clear a hold would silently
        # turn a dodge-left into a dodge-right, so obstacles neither move nor block.
        if ev.type in OBSTACLE_TYPES:
            continue

        if ev.lane in busy:
            free = [lane for lane in range(lane_count) if lane not in busy]
            if free:
                # Nearest free lane, so a strategy's left-to-right intent survives a nudge,
                # and avoiding the previous note's lane where there is a choice, so a nudge
                # does not produce two cues in a row in the same place.
                previous_lane = events[index - 1].lane if index else -1
                candidates = [lane for lane in free if lane != previous_lane] or free
                ev.lane = min(candidates, key=lambda lane: (abs(lane - ev.lane), lane))
            else:
                blocking = next(hold for hold in active if hold[0] == ev.lane)
                victim = events[blocking[2]]
                victim.duration = max(0.0, ev.time - gap - victim.time)
                active = [hold for hold in active if hold[2] != blocking[2]]

        if ev.is_hold:
            active.append((ev.lane, ev.time + ev.duration, index))

    for ev in events:
        if ev.type in HOLD_TYPES and round(ev.duration, 3) <= 0.0:
            ev.duration = 0.0
            ev.type = KICK if ev.type == HOLD_TYPE_OF[KICK] else PUNCH


# --------------------------------------------------------------------------------------
# Pipeline
# --------------------------------------------------------------------------------------

def analyse(path: Path, cfg) -> Tuple[List[Event], float, float, int, List[WorkoutSet], np.ndarray]:
    """Run the whole analysis.

    Returns (events, bpm, duration_seconds, sample_rate, sets, bar_lines). The last two are
    empty under --no-sets, where notes are typed by their own frequency content instead.
    """
    y, sr = librosa.load(str(path), sr=(cfg.sr or None), mono=True)
    if y.size == 0:
        raise SystemExit(f"{path} decoded to zero samples - is the file empty or corrupt?")

    hop = cfg.hop_length
    n_mels = cfg.n_mels

    # One mel spectrogram feeds everything below, so the onset envelope, the per-band
    # classification and the sustain measurement are all indexed by the same frames.
    mel_power = librosa.feature.melspectrogram(
        y=y, sr=sr, n_fft=cfg.n_fft, hop_length=hop, n_mels=n_mels, fmax=sr / 2.0
    )
    mel_db = librosa.power_to_db(mel_power, ref=np.max)

    channels = mel_band_channels(sr, n_mels, cfg.kick_hz, cfg.punch_hz)
    onset_bands = librosa.onset.onset_strength_multi(
        S=mel_db, sr=sr, hop_length=hop, channels=channels
    )

    # Each band is normalised against its OWN distribution. A track can have a quiet hi-hat
    # and a loud kick without every onset being classified by whichever band is loudest in
    # absolute terms; what matters is how strong a hit is relative to the other hits in
    # that same band.
    band_refs = [robust_reference(onset_bands[b], cfg.norm_percentile) for b in range(3)]
    norm_bands = np.stack([onset_bands[b] / band_refs[b] for b in range(3)])

    # Detection runs on the LOUDEST BAND, not on a whole-spectrum envelope.
    #
    # This is the difference between finding the kick drum and not finding it at all.
    # `librosa.onset.onset_strength` averages spectral flux over every mel band, and a kick
    # only lights up the two or three bands below 150 Hz out of 128 — so in the average it
    # contributes a few percent, while a hi-hat or a snare lights up most of the spectrum
    # and dominates. On a plain four-on-the-floor loop that silently costs you every single
    # Kick: the hats get detected, the kicks never clear the threshold, and the chart comes
    # out as an unbroken row of Punches.
    #
    # Taking the max over the three per-band envelopes, each already normalised against its
    # own band, means an event that is strong ANYWHERE is an event. It also keeps detection
    # and classification reading the same numbers.
    detect_env = norm_bands.max(axis=0)

    # Beats first, tempo from the beats. The order matters: the beat tracker follows the audio,
    # and a straight-line fit through the beats it finds is a far sharper tempo estimate than the
    # tempogram, which is quantised to its own bins and was wrong by 2.5% on this project's
    # techno track. Everything downstream - the bar grid, the set boundaries - is then laid on
    # the same beats the number describes, so the reported BPM and the structure cannot disagree.
    # --bpm-hint: a fast track (170+) can lock onto 2/3 of its tempo (video 15: 172 read as 115);
    # the tracker then starts its search at the hint.
    hint = {"start_bpm": cfg.bpm_hint} if getattr(cfg, "bpm_hint", None) else {}
    beat_frames = np.asarray(librosa.beat.beat_track(
        onset_envelope=detect_env, sr=sr, hop_length=hop, units="frames", **hint
    )[1])
    beat_times = librosa.frames_to_time(beat_frames, sr=sr, hop_length=hop)

    seed = tempo_from_beats(beat_times)
    if seed is None:
        # Too few beats to fit - fall back to the tempogram rather than report nothing.
        beat_period = beat_phase = None
        bpm = estimate_tempo(detect_env, sr, hop)
    else:
        beat_period, beat_phase, _ = fit_beat_comb(
            detect_env, sr / hop, 60.0 / seed, float(len(y)) / sr
        )
        bpm = round(60.0 / beat_period, 3)

    wait_frames = max(0, int(round(cfg.onset_wait * sr / hop)))

    # Band energy (amplitude, not flux) drives sustain measurement: a held note produces no
    # flux after its attack, so a flux-based reading would call every note a one-shot.
    band_energy = [
        np.sqrt(np.mean(mel_power[channels[b]:channels[b + 1]], axis=0))
        for b in range(3)
    ]
    kick_energy = band_energy[0]
    punch_energy = np.sqrt(band_energy[1] ** 2 + band_energy[2] ** 2)
    last_frame = onset_bands.shape[1] - 1

    cfg.bar_map = []
    if cfg.detector == "grid":
        if beat_period is None:
            raise SystemExit("--detector grid needs a fitted beat grid, and too few beats were "
                             "tracked for one - use --detector drums")
        events, cfg.bar_map = detect_grid(y, sr, hop, norm_bands[0], beat_period, beat_phase, cfg)
        if not events:
            raise SystemExit("no bar had enough kicks - lower --grid-min-kicks or --kick-threshold")
    elif cfg.detector == "drums":
        events = detect_drums(y, sr, hop, cfg)
        if not events:
            raise SystemExit("no kick or clap passed its threshold - lower --kick-threshold / "
                             "--clap-threshold, or use --detector all")
    else:
        events = detect_all_onsets(detect_env, norm_bands, sr, hop, wait_frames, last_frame, cfg)

    events = [ev for ev in events if ev.intensity >= cfg.min_intensity]
    if not events:
        raise SystemExit(f"every onset fell below --min-intensity {cfg.min_intensity}")

    events = filter_min_interval(events, cfg.min_interval)

    # The drums detector reads its attacks off amplitude envelopes with the refinement's own
    # windows and rise fractions, so running the refinement again would only re-derive them.
    if not cfg.no_refine and cfg.detector == "all":
        # Two envelopes, each with the window its own band needs. The low band is read over a
        # window long enough to hold a cycle of the frequencies in it; the upper band over a
        # short one, so a fast transient is not blurred. Both share a hop, which sets the
        # precision of the answer - 1.5 ms by default, against the 23 ms the detection grid
        # used to impose.
        refine_hop = max(1, int(round(cfg.refine_hop * sr)))
        low_window = max(2, int(round(cfg.refine_low_window * sr)))
        high_window = max(2, int(round(cfg.refine_high_window * sr)))
        env_rate = sr / refine_hop

        low_band = rms_envelope(band_filter(y, sr, 0.0, cfg.kick_hz), low_window, refine_hop)
        high_band = rms_envelope(band_filter(y, sr, cfg.kick_hz, sr / 2.0), high_window, refine_hop)

        for ev in events:
            is_low = ev.acoustic == KICK
            env = low_band if is_low else high_band
            rise = cfg.refine_low_rise if is_low else cfg.refine_rise
            refined = refine_onset(env, env_rate, ev.time, cfg.refine_window, rise)
            if refined is None:
                continue
            # Clamped to the search window so a refinement can never relocate a note onto a
            # different hit, and the frame is moved with it because hold measurement below
            # reads the frame, not the time.
            ev.time = float(np.clip(refined, ev.time - cfg.refine_window, ev.time + cfg.refine_window))
            ev.frame = int(round(ev.time * sr / hop))

        # Refinement moves notes by up to the search window, so neither the ordering nor the
        # spacing contract survives it automatically. Both are re-established rather than
        # assumed: the sequencer walks a cursor forward and would skip anything out of order.
        events.sort(key=lambda e: e.time)
        events = filter_min_interval(events, cfg.min_interval)

    track_end = float(librosa.get_duration(y=y, sr=sr))

    if not cfg.no_sets:
        # Workout mode. Bars are laid on the fitted comb rather than on the tracker's raw beat
        # list. Both are "from the audio", but the comb's period and phase were fitted against
        # the whole track at once, so the grid is regular and does not inherit the tracker's
        # occasional inserted or missed beat - and on machine-sequenced material a regular grid
        # is the more faithful of the two, because the music really is regular.
        if beat_period is not None:
            grid = np.arange(beat_phase, track_end, beat_period)
            grid_frames = np.rint(grid * sr / hop).astype(int)
        else:
            grid, grid_frames = beat_times, beat_frames
        bar_lines = find_bar_lines(grid, norm_bands[0], grid_frames, cfg.beats_per_bar)
        if len(bar_lines) < 2:
            raise SystemExit(
                "could not find a bar grid - too few beats detected. Use --no-sets to fall back "
                "to typing every note by its own frequency content."
            )

        beat = beat_period if beat_period is not None else 60.0 / bpm
        pattern = parse_pattern(cfg.pattern)
        spacing = parse_mapping(cfg.spacing, DEFAULT_SPACING_BEATS, "--spacing",
                                lambda raw, name, chunk: _positive_float(raw, name, chunk))
        pairs = parse_mapping(cfg.lane_pairs, DEFAULT_LANE_PAIRS, "--lane-pairs",
                              lambda raw, name, chunk: _lane_pair(raw, name, chunk))

        set_bars_for = parse_mapping(cfg.set_bars_for, {}, "--set-bars-for",
                                     lambda raw, name, chunk: _positive_int(raw, name, chunk))
        if cfg.pattern in CHARACTER_PLANS:
            _FINALE[0] = CHARACTER_PLANS[cfg.pattern].get("finale")
            if cfg.competitor_style and cfg.intro_rest > 0:
                lines = [float(t) for t in bar_lines]
                lo, hi = 0.85 * cfg.intro_rest, 1.15 * cfg.intro_rest
                inside = [i for i in range(0, len(lines), 4) if lo <= lines[i] <= hi] or \
                         [i for i in range(len(lines)) if lo <= lines[i] <= hi]
                _START_BAR[0] = min(inside or range(len(lines)), key=lambda i: abs(lines[i] - cfg.intro_rest))
            sets = plan_sets_character(bar_lines, cfg.bar_map, cfg.rest_beats, beat, track_end, cfg.pattern)
        elif cfg.pattern == "intense-v2":
            sets = plan_sets_v2(bar_lines, cfg.bar_map, cfg.rest_beats, beat, track_end, cfg.v2_rotation)
        else:
            sets = plan_sets(bar_lines, pattern, cfg.set_bars, cfg.rest_beats, beat, track_end, set_bars_for)
        if not sets:
            raise SystemExit(
                f"the track is too short for even one {cfg.set_bars}-bar set - lower --set-bars"
            )

        cfg.beat = beat
        if not cfg.sweeps:
            # PunchSweep is off unless asked for: a sweep set becomes a PunchHold set.
            for block in sets:
                if block.exercise == SWEEP: block.exercise = "PunchHold"
        events = apply_workout(events, sets, beat, spacing, cfg)
        retire_jump(events)
        events = space_obstacles(events, cfg.obstacle_gap * beat)
        size_workout_holds(events, sets, cfg)
        assign_workout_lanes(events, sets, pairs, cfg.lanes)
        resolve_hold_overlaps(events, cfg.lanes, cfg.hold_gap)
        events = expand_chords(events, sets, cfg.chord_punch_every, pairs[CHORD], cfg.lanes)
        arc_shapes = CHARACTER_PLANS[cfg.pattern].get("arcs") if cfg.pattern in CHARACTER_PLANS else None
        events = expand_compound(events, sets, beat, cfg.lanes, random.Random(cfg.seed), arc_shapes)
        if cfg.fill_obstacles:
            events = fill_obstacle_sets(events, sets, beat, cfg.lanes)
        mirror_sets(events, sets, cfg.lanes)
        if cfg.fill_gaps_dodge:
            cfg.gap_walls = fill_gaps_with_dodges(events, bar_lines, beat, track_end, cfg)
            events = sorted(events + cfg.gap_walls, key=lambda e: e.time)
        first = first_drum_bar(cfg.bar_map)
        if (cfg.intro_melody_onsets or cfg.intro_arcs) and first is not None and 0 < first < len(bar_lines):
            # Before the drums come in: everything the grid put there goes, and the intro is
            # either the melody's onsets or one long ArcHold per phrase.
            drums_at = float(bar_lines[first]) - 0.25 * beat
            events = [e for e in events if e.time >= drums_at]
            sets[:] = [b for b in sets if b.start >= drums_at]
            if cfg.intro_arcs and cfg.intro_melody_onsets:
                # Both: the long intro opens with circles, whole phrases, and its last
                # --intro-melody-bars follow the melody into the drums.
                arc_bars = max(0, first - cfg.intro_melody_bars) // 4 * 4
                intro = intro_arc_events(bar_lines, arc_bars, beat) if arc_bars >= 4 else []
                tune_from = float(bar_lines[arc_bars]) - 0.1 * beat if arc_bars >= 4 else 0.0
                intro += melody_onset_events(y, sr, hop, max(0.0, tune_from), drums_at - 0.25 * beat, beat, cfg)
                intro.sort(key=lambda e: e.time)
            elif cfg.intro_arcs:
                intro = intro_arc_events(bar_lines, first, beat)
            else:
                intro = melody_onset_events(y, sr, hop, 0.0, drums_at - 0.25 * beat, beat, cfg)
            cfg.intro_notes = len(intro)
            events = sorted(intro + events, key=lambda e: e.time)
        if cfg.intro_rest > 0:
            # A clean opening: no note before the bar line nearest --intro-rest seconds (one inside
            # 6..8 s when there is one), so the intro plays out on its own.
            # A phrase line (every fourth bar) is preferred to a bar line when one is in range.
            lines = [float(t) for t in bar_lines]
            lo, hi = 0.85 * cfg.intro_rest, 1.15 * cfg.intro_rest
            phrases = [t for t in lines[::4] if lo <= t <= hi]
            inside = phrases or [t for t in lines if lo <= t <= hi]
            cut = min(inside or lines, key=lambda t: abs(t - cfg.intro_rest))
            cfg.intro_rest_at = cut
            events = [e for e in events if e.time >= cut - 0.25 * beat]
        return events, bpm, track_end, int(sr), sets, bar_lines

    if not cfg.no_holds:
        # For each event, the frame of the next event that would actually interrupt it.
        # Walked backwards so this stays linear rather than rescanning the tail per note.
        next_kick = [last_frame] * len(events)
        next_punch = [last_frame] * len(events)
        upcoming_kick = upcoming_punch = last_frame
        for index in range(len(events) - 1, -1, -1):
            next_kick[index] = upcoming_kick
            next_punch[index] = upcoming_punch
            if events[index].type in (KICK, JUMP):
                upcoming_kick = events[index].frame
            if events[index].type in (PUNCH, JUMP):
                upcoming_punch = events[index].frame

        for index, ev in enumerate(events):
            if ev.type == JUMP:
                continue
            is_kick = ev.type == KICK
            energy = kick_energy if is_kick else punch_energy
            limit = next_kick[index] if is_kick else next_punch[index]
            duration = measure_hold(energy, ev.frame, limit, cfg, sr, hop)
            if duration > 0.0:
                ev.type = HOLD_TYPE_OF[ev.type]
                ev.duration = duration

    rng = random.Random(cfg.seed)
    retire_jump(events)
    LANE_STRATEGIES[cfg.lane_mode](events, cfg.lanes, rng)
    resolve_hold_overlaps(events, cfg.lanes, cfg.hold_gap)

    return events, bpm, track_end, int(sr), [], np.zeros(0)


# --------------------------------------------------------------------------------------
# Gap filling: Dodge walls where the chart would otherwise be empty
# --------------------------------------------------------------------------------------

def next_dodge_side(history: List[int], rng: random.Random) -> int:
    """-1 (wall on the left, player goes right) or +1 (wall on the right, player goes left).

    The whole direction rule, kept apart so it can be changed alone: mostly alternating, but one
    time in three the same side again - never three in a row - so the walls do not become a
    metronome the player stops watching.
    """
    if not history:
        return rng.choice((-1, 1))
    if len(history) >= 2 and history[-1] == history[-2]:
        return -history[-1]
    return history[-1] if rng.random() < 1.0 / 3.0 else -history[-1]


def fill_gaps_with_dodges(events: List[Event], bar_lines: np.ndarray, beat: float,
                          track_end: float, cfg) -> List[Event]:
    """Dodge walls in the stretches without notes - measured, not wall-to-wall. Returns the walls.

    Walls are a break from the exercises, not a replacement for the music: a long intro filled
    with a wall every bar was 40 s of the same move before the first note. So each kind of gap
    has its own rule (defaults, all flags):

      * intro - walls only in the last --gap-intro-lead-bars (4) before the first note, one every
        --gap-intro-every beats (4 = a bar), at most --gap-intro-max (4); before that the road is
        empty and the trainer just stands in its stance;
      * breakdowns - one every --gap-dodge-every beats (8 = every other bar), and only in a gap
        of at least --gap-min-bars (4); a shorter pause keeps no wall at all;
      * outro - at most --gap-outro-max (3) walls after the last note.

    Every wall sits on the bar grid, so it follows the tempo even where no drum plays, and keeps
    --gap-margin-bars clear of the notes on both sides, so the camera is back in the centre before
    the next real exercise. The intro additionally starts --gap-intro-bars after the music.

    The old fill-everything behaviour is --gap-min-bars 2 --gap-dodge-every 4
    --gap-intro-lead-bars 0 --gap-intro-max 0 --gap-outro-max 0 (0 = no limit).

    The scene still decides which intro walls can actually be shown: a note spawns one flight
    before it arrives, so CueSpawner keeps an intro wall only when it would be born after its own
    intro allowance. The chart carries every wall the music has room for.
    """
    bar = cfg.beats_per_bar * beat
    margin = cfg.gap_margin_bars * bar
    # Candidate wall times on a grid of `every` beats, counted from the first bar line and
    # extended backwards from it to the start of the track.
    first_bar = float(bar_lines[0]) if len(bar_lines) else 0.0

    def slots_for(every_beats: float) -> np.ndarray:
        step = every_beats * beat
        origin = first_bar - np.floor(first_bar / step) * step
        return np.arange(origin, track_end, step)

    # Each gap as (kind, start, end, earliest wall, latest wall). A note occupies the road from
    # its arrival to the end of its hold; the track's start and end are edges with no note to clear.
    notes = sorted(events, key=lambda e: e.time)
    gaps: List[Tuple[str, float, float, float, float]] = []
    previous = None
    for ev in notes:
        if previous is None:
            lo = cfg.gap_intro_bars * bar
            if cfg.gap_intro_lead_bars > 0:
                lo = max(lo, ev.time - cfg.gap_intro_lead_bars * bar)
            gaps.append(("intro", 0.0, ev.time, lo, ev.time - margin))
        else:
            gaps.append(("break", previous, ev.time, previous + margin, ev.time - margin))
        previous = max(previous or 0.0, ev.time + ev.duration)
    if previous is None:
        gaps.append(("intro", 0.0, track_end, cfg.gap_intro_bars * bar, track_end - margin))
    else:
        gaps.append(("outro", previous, track_end, previous + margin, track_end - margin))

    intro_slots = slots_for(cfg.gap_intro_every)
    break_slots = slots_for(cfg.gap_dodge_every)
    rng = random.Random(cfg.seed)
    history: List[int] = []
    walls: List[Event] = []
    cfg.gap_wall_counts = {"intro": 0, "break": 0, "outro": 0}
    for kind, start, end, lo, hi in gaps:
        # The minimum length is a rule for pauses between notes; the intro and outro are edges of
        # the track, and have their own limits instead.
        if kind == "break" and end - start < cfg.gap_min_bars * bar - 1e-6:
            continue
        slots = intro_slots if kind == "intro" else break_slots
        chosen = slots[(slots >= lo - 1e-6) & (slots <= hi + 1e-6)]
        if kind == "intro" and cfg.gap_intro_max > 0:
            chosen = chosen[-cfg.gap_intro_max:]          # the ones nearest the first note
        if kind == "outro" and cfg.gap_outro_max > 0:
            chosen = chosen[:cfg.gap_outro_max]           # the ones nearest the last note
        cfg.gap_wall_counts[kind] += len(chosen)
        for time in chosen:
            side = next_dodge_side(history, rng)
            history.append(side)
            walls.append(Event(time=float(time), type=DODGE, acoustic=DODGE, intensity=0.7,
                               lane=0 if side < 0 else cfg.lanes - 1,
                               frame=0))
    return walls


def to_document(events: List[Event], bpm: float, track: str, audio_offset: float,
                sets: Optional[List[WorkoutSet]] = None) -> dict:
    """Build the JSON document, rounded to what Unity's float parsing will actually use.

    `duration` is forced to exactly 0.0 for one-shots and kept strictly positive for
    holds - BeatmapReader treats either mismatch as a hard error.
    """
    payload = []
    for ev in events:
        hold = ev.type in HOLD_TYPES or (ev.type in DURATION_TYPES and ev.duration > 0.0)
        payload.append({
            "time": round(ev.time, 3),
            "type": ev.type,
            "lane": int(ev.lane),
            "duration": round(ev.duration, 3) if hold else 0.0,
            "intensity": round(float(np.clip(ev.intensity, 0.0, 1.0)), 3),
        })
        if ev.type == ARC_HOLD:
            payload[-1]["arc"] = round(float(ev.arc), 1)
            payload[-1]["radius"] = round(float(ev.radius), 3)
    document = {
        "version": BEATMAP_VERSION,
        "track": track,
        "bpm": bpm,
        "audioOffset": round(float(audio_offset), 3),
        "generator": GENERATOR,
        "events": payload,
    }

    # The set plan, for anything that wants to know where a block of work ends - the HUD's
    # end-of-set banner does. Written only in workout mode, where sets exist at all; a chart
    # without the field plays identically, so older maps stay valid.
    if sets:
        document["sets"] = [{
            "index": int(s.index),
            "exercise": s.exercise,
            "start": round(float(s.start), 3),
            "end": round(float(s.end), 3),
            "restEnd": round(float(s.rest_end), 3),
            "notes": int(s.notes),
        } for s in sets]

    return document


def write_atomic(document: dict, destination: Path) -> None:
    """Write via a temp file in the same directory, then rename over the target.

    StreamingAssets is read with plain file IO at runtime, and a player that happens to
    read a half-written file gets a parse error rather than a retry. `os.replace` is atomic
    on the same volume, so the destination is either the old map or the new one, never a
    prefix of the new one.
    """
    destination.parent.mkdir(parents=True, exist_ok=True)
    handle, temp_path = tempfile.mkstemp(dir=str(destination.parent), suffix=".tmp")
    try:
        with os.fdopen(handle, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(document, stream, indent=2, ensure_ascii=False)
            stream.write("\n")
        os.replace(temp_path, destination)
    except BaseException:
        if os.path.exists(temp_path):
            os.unlink(temp_path)
        raise


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------

def default_output(audio: Path) -> Path:
    """Land in StreamingAssets/Beatmaps, located relative to this script, not the cwd."""
    project_root = Path(__file__).resolve().parents[2]
    return project_root / "Assets" / "StreamingAssets" / "Beatmaps" / f"{audio.stem}.json"


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description="Generate a Unity beatmap JSON from a WAV (or anything librosa can read).",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    p.add_argument("audio", type=Path, help="input audio file")
    p.add_argument("-o", "--output", type=Path, default=None,
                   help="output JSON (default: Assets/StreamingAssets/Beatmaps/<name>.json)")
    p.add_argument("--track", default=None,
                   help="value for the 'track' field (default: the input file name)")
    p.add_argument("--audio-offset", type=float, default=0.0,
                   help="global timing shift written into the map, for latency calibration")

    g = p.add_argument_group("filtering")
    g.add_argument("--min-interval", type=float, default=0.12,
                   help="minimum seconds between events; the strongest onset in a cluster wins")
    g.add_argument("--min-intensity", type=float, default=0.08,
                   help="drop onsets weaker than this (0..1) before spacing is applied")
    g.add_argument("--onset-delta", type=float, default=0.07,
                   help="librosa onset picking threshold; raise for fewer, more confident onsets")
    g.add_argument("--onset-wait", type=float, default=0.03,
                   help="seconds librosa waits after an onset before accepting the next")

    g = p.add_argument_group("detection")
    g.add_argument("--detector", choices=("drums", "grid", "all"), default="drums",
                   help="drums: notes on the kick and the clap/snare only, each found in its own "
                        "band; grid: a note on every beat of the fitted grid, but only in bars "
                        "where the kick plays; all: every onset, hats and percussion included "
                        "(the old behaviour)")
    g.add_argument("--grid-min-kicks", type=int, default=2,
                   help="grid detector: kicks a bar needs to count as a drum bar")
    g.add_argument("--kick-threshold", type=float, default=0.7,
                   help="drums detector: peak height of the low-band amplitude envelope (99th "
                        "percentile = 1) a kick must reach; a bass line sits below ~0.5")
    g.add_argument("--clap-threshold", type=float, default=0.6,
                   help="drums detector: peak height of the broadband mid x high envelope a "
                        "clap/snare must reach")
    g.add_argument("--drum-prominence", type=float, default=0.3,
                   help="drums detector: how far a peak must rise above the valleys around it")
    g.add_argument("--no-claps", action="store_true",
                   help="drums detector: kick only, for tracks with no clap or snare backbeat")

    g = p.add_argument_group("classification (--detector all)")
    g.add_argument("--kick-hz", type=float, default=150.0,
                   help="upper edge of the low (Kick) band")
    g.add_argument("--punch-hz", type=float, default=2000.0,
                   help="boundary between the mid and high bands")
    g.add_argument("--kick-dominance", type=float, default=1.30,
                   help="low band must exceed this multiple of the strongest other band for Kick")
    g.add_argument("--kick-level", type=float, default=0.45,
                   help="normalised low-band strength that is a Kick on its own; lower (~0.30) "
                        "for dense four-on-the-floor material")
    g.add_argument("--jump-band", type=float, default=0.55,
                   help="every band must reach this normalised strength for a Jump")
    g.add_argument("--jump-strength", type=float, default=0.80,
                   help="overall normalised onset strength required for a Jump")
    g.add_argument("--no-jumps", action="store_true", help="never emit Jump")

    g = p.add_argument_group("holds")
    g.add_argument("--no-holds", action="store_true", help="emit one-shots only")
    g.add_argument("--hold-min", type=float, default=0.35,
                   help="shortest sustain that counts as a hold, in seconds")
    g.add_argument("--hold-max", type=float, default=4.0, help="longest hold, in seconds")
    g.add_argument("--hold-decay", type=float, default=0.45,
                   help="fraction of the attack peak the band must stay above to still sustain")
    g.add_argument("--hold-attack", type=float, default=0.06,
                   help="seconds after the onset used to find the attack peak")
    g.add_argument("--hold-lift", type=float, default=1.5,
                   help="attack must exceed the band's pre-onset level by this factor to hold")
    g.add_argument("--hold-gap", type=float, default=0.08,
                   help="seconds a hold must end before the next event starts")

    g = p.add_argument_group("workout structure")
    g.add_argument("--no-sets", action="store_true",
                   help="type every note by its own frequency content instead of by the set it "
                        "falls in (the behaviour before workout mode)")
    g.add_argument("--set-bars", type=int, default=3,
                   help="length of one set, in bars, measured bar line to bar line")
    g.add_argument("--set-bars-for", default=None,
                   help="per-exercise set length in bars, e.g. 'Punch:5,Kick:5'; exercises not "
                        "named use --set-bars")
    g.add_argument("--rest-beats", type=float, default=1.0,
                   help="beats of silence at the END of each set, for changing stance; taken "
                        "out of the set rather than added between sets, so every set still "
                        "starts on a bar line however short the rest is")
    g.add_argument("--beats-per-bar", type=int, default=4, help="time signature numerator")
    g.add_argument("--pattern", default="circuit",
                   help="set order: a preset (" + ", ".join(sorted(SET_PATTERNS)) +
                        ") or an inline list like 'Punch:6,Kick:5,Jump:4,PunchHold:2'")
    g.add_argument("--spacing", default=None,
                   help="minimum gap between notes per exercise, IN BEATS, e.g. "
                        "'Jump:1.5,PunchHold:4'; unmentioned exercises keep their default")
    g.add_argument("--lane-pairs", default=None,
                   help="the two lanes each exercise alternates between, e.g. 'Punch:0-3'; "
                        "default is 1-2 for everything, and 0-3 for Dodge, whose lane is a side")
    g.add_argument("--v2-rotation", choices=sorted(INTENSE_V2_ROTATIONS), default="default",
                   help="intense-v2: which set rotation (arms = more punching, ~60/40 legs/arms)")
    g.add_argument("--sweeps", action="store_true",
                   help="keep PunchSweep sets (off by default: a sweep set becomes a PunchHold set)")
    g.add_argument("--grid-quiet-every", type=float, default=0.0,
                   help="grid detector: bars without drums still get a note every N beats (0 = rest)")
    g.add_argument("--spacing-tolerance", type=float, default=0.0,
                   help="beats by which a note may fall short of its --spacing floor and still be "
                        "kept; 0.05 lets grid notes exactly N beats apart through")
    g.add_argument("--chord-punch-every", type=int, default=2,
                   help="in a Chord set, every Nth two-foot jump also gets a Punch on top "
                        "(0 = never)")
    g.add_argument("--sweep-gap-beats", type=float, default=0.5,
                   help="a PunchSweep ends this many beats before the next note of its set, so "
                        "the arm is back at the centre for the next sweep")
    g.add_argument("--obstacle-gap", type=float, default=8.0,
                   help="minimum gap between ANY two obstacles, IN BEATS, across set boundaries "
                        "as well as inside a set. The camera performs the movement and has to be "
                        "back at centre before the next one starts; the longest reaction (the "
                        "dodge) runs 1.59 s. Default 8 beats = 3.43 s at 140 BPM.")
    g.add_argument("--bpm-hint", type=float, default=None,
                   help="the tempo to start the beat search at, for a track the tracker gets wrong")
    g.add_argument("--fill-obstacles", action="store_true",
                   help="after the obstacle of a Curb/Duck/Dodge set, a combo to the set's end "
                        "instead of two empty bars (video 14)")
    g.add_argument("--fill-gaps-dodge", action="store_true",
                   help="put Dodge walls, alternating sides, into every stretch without notes: "
                        "the intro, breakdowns, the outro")
    g.add_argument("--gap-min-bars", type=float, default=4.0,
                   help="--fill-gaps-dodge: a pause between notes shorter than this, in bars, "
                        "keeps no wall")
    g.add_argument("--gap-dodge-every", type=float, default=8.0,
                   help="--fill-gaps-dodge: beats between walls in a breakdown (8 = every other "
                        "bar, 4 = every bar). The dodge "
                        "reaction runs 1.59 s, so keep this above that")
    g.add_argument("--gap-margin-bars", type=float, default=1.0,
                   help="--fill-gaps-dodge: bars kept clear between a wall and the nearest note")
    g.add_argument("--gap-intro-bars", type=float, default=1.0,
                   help="--fill-gaps-dodge: bars after the music starts before the first wall")
    g.add_argument("--gap-intro-lead-bars", type=float, default=4.0,
                   help="--fill-gaps-dodge: intro walls only in this many bars before the first "
                        "note (0 = the whole intro)")
    g.add_argument("--gap-intro-every", type=float, default=4.0,
                   help="--fill-gaps-dodge: beats between intro walls (4 = one per bar)")
    g.add_argument("--gap-intro-max", type=int, default=4,
                   help="--fill-gaps-dodge: most walls in the intro (0 = no limit)")
    g.add_argument("--gap-outro-max", type=int, default=3,
                   help="--fill-gaps-dodge: most walls after the last note (0 = no limit)")
    g.add_argument("--intro-melody-onsets", action="store_true",
                   help="before the drums first come in, put the notes on the melody's onsets (HPSS harmonic + "
                        "onset detection) instead of the beat grid; light notes, at most one per eighth note")
    g.add_argument("--intro-melody-delta", type=float, default=0.08,
                   help="peak-picking threshold for --intro-melody-onsets (onset envelope normalised to 1)")
    g.add_argument("--intro-melody-min", type=float, default=0.12,
                   help="weakest melody onset kept by --intro-melody-onsets (0..1 of the strongest)")
    g.add_argument("--intro-melody-search", type=float, default=0.06,
                   help="seconds either side of a melody onset in which its attack is looked for")
    g.add_argument("--intro-arcs", action="store_true",
                   help="before the drums first come in, a chain of long ArcHolds instead of notes: one per "
                        "4-bar phrase, one turn a bar, hands alternating, the last phrase both hands")
    g.add_argument("--competitor-style", action="store_true",
                   help="video 9: the competitor's vocabulary and phrase system; picks the plan competitor-<difficulty>")
    g.add_argument("--difficulty", choices=("easy", "medium", "hard"), default="medium",
                   help="with --competitor-style: easy ~1.3, medium ~1.8, hard ~2.3 actions a second")
    g.add_argument("--intro-melody-bars", type=int, default=8,
                   help="with both --intro-arcs and --intro-melody-onsets: the last N bars before the drums "
                        "follow the melody, the whole phrases before them are circles (default 8)")
    g.add_argument("--intro-rest", type=float, default=0.0,
                   help="seconds of music at the start with no notes at all, rounded to the nearest bar line")
    g.add_argument("--show-sets", action="store_true",
                   help="print the planned routine: one line per set with its timecode")

    g = p.add_argument_group("timing refinement")
    g.add_argument("--no-refine", action="store_true",
                   help="skip per-band timing refinement and keep the raw detection grid")
    g.add_argument("--refine-window", type=float, default=0.05,
                   help="seconds either side of a detected onset to search for its true attack")
    g.add_argument("--refine-hop", type=float, default=0.0015,
                   help="envelope hop in seconds; this is the precision of the refined times")
    # The four numbers below were calibrated against Tools/beatmap's synthetic fixture, whose
    # hits sit on exact times, so the error there is known rather than estimated. Two results
    # from that sweep are worth keeping in mind before changing them:
    #   * filter order is almost irrelevant (orders 2, 3 and 4 differ by 0.1 ms), so the
    #     zero-phase filter's ringing is NOT what limits accuracy - the RMS window is;
    #   * window length dominates. On the low band a 20 ms window scattered kicks with a
    #     standard deviation of 18.6 ms while 10 ms gave 2.9 ms, and on the upper band 4 ms
    #     gave +5.0 +- 14.6 ms where 8 ms gave -1.3 +- 7.0 ms.
    g.add_argument("--refine-low-window", type=float, default=0.010,
                   help="RMS window for the low band, in seconds")
    g.add_argument("--refine-high-window", type=float, default=0.008,
                   help="RMS window for the mid/high band, in seconds")
    g.add_argument("--refine-low-rise", type=float, default=0.50,
                   help="attack threshold for the low band, as a fraction of the rise; higher "
                        "than the upper band because a low-frequency envelope rises more slowly, "
                        "so the same fraction would land earlier in absolute time")
    g.add_argument("--refine-rise", type=float, default=0.35,
                   help="attack threshold for the mid/high band, as a fraction of the rise")

    g = p.add_argument_group("lanes")
    g.add_argument("--lanes", type=int, default=4,
                   help="lane count; must match NeonTunnelGenerator.laneCount")
    g.add_argument("--lane-mode", choices=sorted(LANE_STRATEGIES), default="alternate",
                   help="lane assignment strategy")
    g.add_argument("--seed", type=int, default=0, help="seed for --lane-mode random")

    g = p.add_argument_group("analysis")
    g.add_argument("--sr", type=int, default=22050,
                   help="resample rate for analysis; 0 keeps the file's native rate")
    g.add_argument("--hop-length", type=int, default=256,
                   help="STFT hop in samples; sets the detection grid (256 at 22050 Hz = 11.6 ms)")
    g.add_argument("--n-fft", type=int, default=2048, help="STFT window in samples")
    g.add_argument("--n-mels", type=int, default=128, help="mel bands")
    g.add_argument("--norm-percentile", type=float, default=99.0,
                   help="percentile used as the normalisation reference for bands and intensity")

    g = p.add_argument_group("output")
    g.add_argument("--preview", type=int, default=0, metavar="N",
                   help="print the first N events as a table after writing")
    g.add_argument("--dry-run", action="store_true", help="analyse and report, write nothing")
    return p


def main(argv: Optional[Sequence[str]] = None) -> int:
    cfg = build_parser().parse_args(argv)
    if cfg.competitor_style and cfg.pattern not in CHARACTER_PLANS:
        cfg.pattern = "competitor-" + cfg.difficulty

    if not cfg.audio.is_file():
        sys.exit(f"input not found: {cfg.audio}")
    if cfg.lanes < 1:
        sys.exit("--lanes must be at least 1")
    if cfg.lane_mode == "frequency" and cfg.lanes < 2:
        sys.exit("--lane-mode frequency needs at least 2 lanes")

    # Frame-domain versions of the second-domain flags, computed once. The nominal rate is
    # only used for a first guess; analyse() works in whatever rate librosa actually gave.
    nominal_sr = cfg.sr or 22050
    # Expressed in time, not in frames: the band-peak window has to mean the same thing
    # whatever --hop-length is, or halving the hop silently halves how far past an onset the
    # classifier looks and changes every type it assigns.
    cfg.peak_frames = max(1, int(round(0.045 * nominal_sr / cfg.hop_length)))
    cfg.attack_frames = max(1, int(round(cfg.hold_attack * nominal_sr / cfg.hop_length)))
    cfg.hold_max_frames = max(1, int(round(cfg.hold_max * nominal_sr / cfg.hop_length)))

    events, bpm, seconds, sr, sets, bar_lines = analyse(cfg.audio, cfg)

    if cfg.no_jumps:
        for ev in events:
            if ev.type == JUMP:
                ev.type = PUNCH

    document = to_document(events, bpm, cfg.track or cfg.audio.name, cfg.audio_offset, sets)
    output = cfg.output or default_output(cfg.audio)

    counts: Dict[str, int] = {}
    for item in document["events"]:
        counts[item["type"]] = counts.get(item["type"], 0) + 1

    print(f"{cfg.audio.name}: {seconds:.1f}s @ {sr} Hz, bpm {bpm}")
    print(f"  events: {len(document['events'])}  "
          f"({', '.join(f'{k} {v}' for k, v in sorted(counts.items()))})")
    if sets:
        beat_s = 60.0 / bpm
        rest = cfg.rest_beats * beat_s
        cycle = float(np.mean([s.rest_end - s.start for s in sets]))
        lengths = ", ".join(f"{name} {np.mean([s.rest_end - s.start for s in sets if s.exercise == name]):.2f}s"
                            for name in dict.fromkeys(s.exercise for s in sets))
        print(f"  routine: {len(sets)} sets, one every {cycle:.2f}s on average ({lengths}; "
              f"each ends in {rest:.2f}s of rest), pattern '{cfg.pattern}'")
        longest, run, previous, span = 0.0, 0.0, None, 0.0
        for block in sets:
            length = block.rest_end - block.start
            run = run + length if block.exercise == previous else length
            longest = max(longest, run)
            previous = block.exercise
        print(f"  longest run of one exercise: {longest:.1f}s")
        print(f"  density: {len(document['events']) / max(seconds, 1e-6):.2f} events/s overall, "
              f"{len(document['events']) / max(sum(s.end - s.start for s in sets), 1e-6):.2f} "
              f"inside sets")
    else:
        print(f"  density: {len(document['events']) / max(seconds, 1e-6):.2f} events/s, "
              f"lane mode {cfg.lane_mode}")

    if getattr(cfg, "bar_map", None):
        # One character per bar, 16 to a row: '#' drums play, '.' a rest. Row labels are the
        # time of the row's first bar, so a stretch can be checked against the audio by ear.
        drum_bars = cfg.bar_map.count("#")
        bar_s = cfg.beats_per_bar * 60.0 / bpm
        print(f"  grid: notes on every beat of {drum_bars} of {len(cfg.bar_map)} bars "
              f"(# = drums, . = rest)")
        for row in range(0, len(cfg.bar_map), 16):
            print(f"    {row * bar_s:6.1f}s  {''.join(cfg.bar_map[row:row + 16])}")

    walls = getattr(cfg, "gap_walls", None)
    if cfg.fill_gaps_dodge:
        # Direction is the way the PLAYER goes: a wall on the right sends them left.
        listed = ", ".join(f"{w.time:.2f} {'L' if w.lane >= cfg.lanes / 2 else 'R'}"
                           for w in (walls or [])[:12])
        counts = getattr(cfg, "gap_wall_counts", None)
        if counts:
            print(f"  gap walls by stretch: intro {counts['intro']}, breakdowns {counts['break']}, outro {counts['outro']}")
        print(f"  gap walls: {len(walls or [])} Dodge (time + dodge direction): {listed}"
              + (" ..." if walls and len(walls) > 12 else ""))

    if cfg.show_sets and sets:
        print()
        # The gap column is the silence a person actually experiences: last note of this set to
        # first note of the next. It is always longer than --rest-beats, because the next set's
        # first onset does not land on its very first frame either.
        print(f"  {'set':>4}  {'bar':>4}  {'start':>8}  {'end':>8}  {'exercise':<10} "
              f"{'notes':>6}  {'gap':>6}  lanes")
        members_of = {block.index: sorted([e for e in events if e.set_index == block.index],
                                          key=lambda e: e.time) for block in sets}
        for position, block in enumerate(sets):
            members = members_of[block.index]
            lanes = sorted({e.lane for e in members})
            gap = ""
            if members and position + 1 < len(sets):
                following = members_of[sets[position + 1].index]
                if following:
                    gap = f"{following[0].time - members[-1].time:.2f}"
            print(f"  {block.index:>4}  {block.bar:>4}  {block.start:>8.3f}  {block.end:>8.3f}  "
                  f"{block.exercise:<10} {block.notes:>6}  {gap:>6}  "
                  f"{'/'.join(str(x) for x in lanes) if lanes else '-'}")

    if cfg.dry_run:
        print("  dry run - nothing written")
    else:
        write_atomic(document, output)
        print(f"  wrote {output}")

    if cfg.preview > 0:
        # `gap` is the interval from the previous note. `beat` is where the note sits in the bar
        # (1..beats-per-bar, fraction for off-beats) and `off` how far it is from the nearest
        # half-beat of the fitted grid - what to read to see whether the chart follows the rhythm.
        beat_s = 60.0 / bpm if bpm > 0 else 0.0
        origin = float(bar_lines[0]) if len(bar_lines) else None
        source_name = {KICK: "kick", PUNCH: "clap", JUMP: "full"}
        print()
        print(f"{'#':>4}  {'time':>8}  {'gap ms':>7}  {'src':<5} {'type':<10} {'lane':>4}  "
              f"{'dur':>6}  {'int':>5}" + (f"  {'beat':>5}  {'off ms':>6}" if origin is not None else ""))
        previous = None
        for index, (item, ev) in enumerate(zip(document["events"][:cfg.preview], events)):
            gap = "" if previous is None else f"{(item['time'] - previous) * 1000:7.0f}"
            previous = item["time"]
            grid = ""
            if origin is not None and beat_s > 0:
                position = (item["time"] - origin) / beat_s
                half = round(position * 2) / 2
                grid = (f"  {half % cfg.beats_per_bar + 1:>5.1f}  "
                        f"{(position - half) * beat_s * 1000:>+6.0f}")
            print(f"{index:>4}  {item['time']:>8.3f}  {gap:>7}  {source_name.get(ev.acoustic, '?'):<5} "
                  f"{item['type']:<10} {item['lane']:>4}  {item['duration']:>6.3f}  "
                  f"{item['intensity']:>5.3f}{grid}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
