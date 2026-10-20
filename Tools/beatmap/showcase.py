"""The button showcase: every cue type and every set combination the generator knows, one after
another at 120 BPM on silence, a gap between them - for picking the trainer's movement per button.

Writes Assets/StreamingAssets/Beatmaps/showcase.json, Assets/Audio/Tracks/showcase_silent.wav and
a schedule (song time -> what is on screen) to Desktop/витрина_кнопок.txt.
    .venv\\Scripts\\python showcase.py
"""
import json
import os
import random
import wave

import generate_beatmap as g

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
BPM = 120.0
BEAT = 60.0 / BPM
LANES = 4
START = 1.0          # song seconds before the first item (was 4: the user waited too long)
GAP = 2              # beats of nothing after each item

# (label, kind, payload). raw: [(beat, type, lane, duration_beats, extra)], set: (exercise, beats, mirror)
ITEMS = [
    ("Kick - inner lanes", "raw", [(b, g.KICK, 1 + b % 2, 0, None) for b in range(4)]),
    ("Kick - outer lanes", "raw", [(b, g.KICK, 0 if b % 2 == 0 else 3, 0, None) for b in range(4)]),
    ("Jump", "raw", [(0, g.JUMP, 1, 0, None), (2, g.JUMP, 2, 0, None)]),
    ("KickHold - left, then right", "raw", [(0, "KickHold", 1, 2, None), (3, "KickHold", 2, 2, None)]),
    ("KickHold - outer (leg out wide)", "raw", [(0, "KickHold", 0, 2, None), (3, "KickHold", 3, 2, None)]),
    ("Dodge - wall left, then right", "raw", [(0, g.DODGE, 0, 1, None), (4, g.DODGE, 3, 1, None)]),
    ("Duck - bar to duck under", "raw", [(0, g.DUCK, 1, 0, None)]),
    ("Curb - kerb to jump", "raw", [(0, g.CURB, 1, 0, None)]),
    ("CurbGap - one leg (free lane 3)", "raw", [(0, g.CURB_GAP, 3, 4, None)]),
    ("CurbSplit - wide stance", "raw", [(0, g.CURB_SPLIT, 1, 4, None)]),
    ("Rails - stand narrow", "raw", [(0, "Rails", 1, 4, None)]),
    ("SquatHold - squat held", "raw", [(0, "SquatHold", 1, 6, None)]),
    # every set combination
    ("HighLow - two heights", "set", (g.HIGH_LOW, 8, False)),
    ("PunchPair - double punch", "set", (g.PUNCH_PAIR, 8, False)),
    ("Mirror - both hands every beat", "set", (g.MIRROR, 8, False)),
    ("Fan - punch fan", "set", (g.FAN, 8, False)),
    ("SpeedPunch - eighths", "set", (g.SPEED_PUNCH, 8, False)),
    ("JabCross - half time", "set", (g.JAB_CROSS, 8, False)),
    ("JabKick - 1-2-1-kick", "set", (g.JAB_KICK, 8, False)),
    ("JabKick mirrored", "set", (g.JAB_KICK, 8, True)),
    ("StepTap - step + jab", "set", (g.STEP_TAP, 8, False)),
    ("StepTouch - half time steps", "set", (g.STEP_TOUCH, 8, False)),
    ("StepReach - step + reach up", "set", (g.STEP_REACH, 8, False)),
    ("StepPair - together/step", "set", (g.STEP_PAIR, 8, False)),
    ("SpeedStep - fast feet", "set", (g.SPEED_STEP, 8, False)),
    ("Chord - both feet (+punch)", "set", (g.CHORD, 8, False)),
    ("SideJump", "set", (g.SIDE_JUMP, 8, False)),
    ("Jack - jumping jacks", "set", (g.JACK, 8, False)),
    ("Shift - pair sliding", "set", (g.SHIFT, 8, False)),
    ("PowerJump - jump, fists up", "set", (g.POWER_JUMP, 8, False)),
    ("PunchKick - punch + opposite foot", "set", (g.PUNCH_KICK, 8, False)),
    ("Cross - the star (rays)", "set", (g.CROSS, 4, False)),
    ("Arc - circles both hands, then steps", "set", (g.ARC, 8, False)),
    ("ArcPunch - circle + punches", "set", (g.ARC_PUNCH, 8, False)),
    ("HoldJab - hold one fist, punch other", "set", (g.HOLD_JAB, 8, False)),
    ("LegHoldPunch - leg held, punches", "set", (g.LEG_HOLD_PUNCH, 8, False)),
    ("PressHold - both fists held, steps", "set", (g.PRESS_HOLD, 8, False)),
    ("RailsHigh - rails + high punches", "set", (g.RAILS_HIGH, 8, False)),
    ("RailsSet - rails + punches", "set", (g.RAILS, 8, False)),
    ("Lunge - strip under one foot", "set", (g.LUNGE, 8, False)),
    ("Wide - feet apart / together", "set", (g.WIDE, 8, False)),
    ("Squat - squat held set", "set", (g.SQUAT, 8, False)),
    ("Balance - one leg / wide kerb", "set", (g.BALANCE, 8, False)),
    ("WallRun - long wall + steps", "set", (g.WALL_RUN, 8, False)),
    # arms last - already tuned
    ("Punch - inner lanes, left/right", "raw", [(b, g.PUNCH, 1 + b % 2, 0, None) for b in range(4)]),
    ("Punch - outer lanes", "raw", [(b, g.PUNCH, 0 if b % 2 == 0 else 3, 0, None) for b in range(4)]),
    ("PunchHigh", "raw", [(b, g.PUNCH_HIGH, 1 + b % 2, 0, None) for b in range(4)]),
    ("PunchHigh - outer lanes", "raw", [(b, g.PUNCH_HIGH, 0 if b % 2 == 0 else 3, 0, None) for b in range(4)]),
    ("PunchHold - left, then right", "raw", [(0, "PunchHold", 1, 2, None), (3, "PunchHold", 2, 2, None)]),
    ("PunchHold - both hands", "raw", [(0, "PunchHold", 1, 3, None), (0, "PunchHold", 2, 3, None)]),
    ("ArcHold - one hand circle", "raw", [(0, g.ARC_HOLD, 1, 4, (-360.0, 0.75))]),
    ("ArcHold - both hands", "raw", [(0, g.ARC_HOLD, 1, 6, (-450.0, 0.8)), (0, g.ARC_HOLD, 2, 6, (450.0, 0.8))]),
]


# Video 24: the new moves on their own, to train the trainer on them first.
NEW_MOVES = [
    ("Knee - high knees, left/right every half beat", "raw", [(k * 0.5, g.KNEE, 1 + k % 2, 0, None) for k in range(8)]),
    ("SquatJump - squat, then jump (x3)", "raw", [(k * 2, g.SQUAT_JUMP_TYPE, 1, 0, None) for k in range(3)]),
    ("Crunch - knee to opposite elbow, left/right", "raw", [(k, g.CRUNCH, 1 + k % 2, 0, None) for k in range(6)]),
    ("HighKnees - set", "set", (g.HIGH_KNEES, 8, False)),
    ("SquatJumps - set", "set", (g.SQUAT_JUMPS, 8, False)),
    ("CrossCrunch - set", "set", (g.CROSS_CRUNCH, 8, False)),
    # Video 24, part 2: arms out through the walls' slots; the one-leg kerb on every lane; arm patterns.
    ("ArmGate - arms out through the slots, held", "set", (g.ARM_GATE, 6, False)),
    ("Balance - one leg / wide, lanes in turn (1)", "set", (g.BALANCE, 4, False)),
    ("Balance - (2)", "set", (g.BALANCE, 4, False)),
    ("Balance - (3)", "set", (g.BALANCE, 4, False)),
    ("Balance - (4)", "set", (g.BALANCE, 4, False)),
    ("ArmFlow - wave (right hand)", "set", (g.ARM_FLOW, 8, False)),
    ("ArmFlow - zigzag (left hand)", "set", (g.ARM_FLOW, 8, False)),
    ("ArmFlow - up-down (right hand)", "set", (g.ARM_FLOW, 8, False)),
    ("ArmFlow - side-side (left hand)", "set", (g.ARM_FLOW, 8, False)),
    ("ArmFlow - figure eight (right hand)", "set", (g.ARM_FLOW, 8, False)),
    ("ArmFlow - deep wave, squat to reach (left hand)", "set", (g.ARM_FLOW, 8, False)),
]

# Items already signed off by the user, left out of the showcase so it starts on the next one to fix.
DONE = {"Kick - inner lanes", "Kick - outer lanes", "Jump", "KickHold - left, then right",
        "KickHold - outer (leg out wide)", "Dodge - wall left, then right", "Duck - bar to duck under",
        "Curb - kerb to jump", "CurbGap - one leg (free lane 3)", "CurbSplit - wide stance"}
ITEMS = [it for it in ITEMS if it[0] not in DONE]
ITEMS = NEW_MOVES


def main():
    events, sets, schedule = [], [], []
    t = START
    for i, (label, kind, payload) in enumerate(ITEMS):
        if kind == "raw":
            beats = max(b + max(d, 1) for b, _, _, d, _ in payload)
            for b, typ, lane, dur, extra in payload:
                ev = g.Event(time=t + b * BEAT, type=typ, intensity=0.8, lane=lane, duration=dur * BEAT, set_index=i)
                if extra: ev.arc, ev.radius = extra
                events.append(ev)
            sets.append(g.WorkoutSet(index=i, exercise=label.split(" ")[0], start=t, end=t + beats * BEAT,
                                     rest_end=t + (beats + GAP) * BEAT, bar=0))
        else:
            ex, beats, mirror = payload
            ws = g.WorkoutSet(index=i, exercise=ex, start=t, end=t + beats * BEAT, rest_end=t + (beats + 1) * BEAT,
                              bar=0, mirror=mirror)
            sets.append(ws)
            block = [g.Event(time=t + k * BEAT, type=ex, intensity=0.8, set_index=i) for k in range(beats)]
            block = g.expand_chords(block, [ws], 2, (1, 2), LANES)
            block = g.expand_compound(block, [ws], BEAT, LANES, random.Random(i))
            g.mirror_sets(block, [ws], LANES)
            events.extend(block)
        schedule.append((t, label))
        t += (beats + GAP) * BEAT
    length = t + 2.0

    events.sort(key=lambda e: (e.time, e.lane))
    out = []
    for e in events:
        d = {"time": round(e.time, 3), "type": e.type, "lane": int(e.lane), "duration": round(e.duration, 3), "intensity": 0.8}
        if e.type == g.ARC_HOLD: d["arc"], d["radius"] = e.arc, e.radius
        if e.type == g.ARC_HOLD and getattr(e, "shape", 0): d["shape"] = int(e.shape)
        out.append(d)
    data = {"version": 1, "track": "showcase_silent.wav", "bpm": BPM, "audioOffset": 0.0,
            "generator": "Tools/beatmap/showcase.py", "events": out,
            "sets": [{"index": s.index, "exercise": s.exercise, "start": round(s.start, 3), "end": round(s.end, 3),
                      "restEnd": round(s.rest_end, 3), "notes": sum(1 for e in events if e.set_index == s.index)} for s in sets]}
    with open(os.path.join(ROOT, "Assets", "StreamingAssets", "Beatmaps", "showcase.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)

    with wave.open(os.path.join(ROOT, "Assets", "Audio", "Tracks", "showcase_silent.wav"), "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(44100)
        w.writeframes(b"\x00\x00\x00\x00" * int(44100 * length))

    lines = [f"Витрина кнопок: {len(ITEMS)} пунктов, {len(out)} нот, {length:.0f} с. Время = время песни (на экране после пред-ролла).", ""]
    lines += [f"{int(s // 60):02d}:{s % 60:05.2f}  {i + 1:2d}. {label}" for i, (s, label) in enumerate(schedule)]
    with open(os.path.join(os.path.expanduser("~"), "Desktop", "витрина_кнопок.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print(f"{len(ITEMS)} items, {len(out)} notes, {length:.0f} s")


if __name__ == "__main__":
    main()
