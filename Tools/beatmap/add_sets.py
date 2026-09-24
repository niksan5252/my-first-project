"""Add the `sets` block to a beatmap that was generated before the field existed.

Why this exists rather than "just regenerate the chart": the set plan is laid out by
generate_beatmap.py on the bar grid, and re-running it would need the exact CLI arguments the
original chart was made with. Anything else - a different --pattern, a different --set-bars, even
a newer librosa - produces a different chart. The notes in a finished map are not worth risking
for a presentation field.

It is safe to do without the audio because in workout mode every note inside a set is the same
exercise (see the "Workout structure" section of generate_beatmap.py). A run of consecutive
events with the same `type` therefore IS a set, and its boundaries can be read straight off the
file. On techno_circuit_flow.json this recovers 40 sets of ~4.5 s work + ~1 s rest.

    python add_sets.py ../../Assets/StreamingAssets/Beatmaps/techno_circuit_flow.json
    python add_sets.py <file> --dry-run        # print what it would write, change nothing

`events` is never touched: the script only adds (or replaces) the top-level "sets" key.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import tempfile
from pathlib import Path
from typing import Dict, List


# A run shorter than this is not a set - it is the tail of the previous one, left behind when the
# generator's bar grid and the track's last few onsets disagree. Merging it back is what stops a
# one-note "set" from firing an end-of-set banner a second after the previous one.
MIN_SET_NOTES = 3


def derive_sets(events: List[dict], min_notes: int) -> List[dict]:
    """Group the note list into runs of one exercise, in time order."""
    if not events:
        return []

    ordered = sorted(events, key=lambda e: float(e.get("time", 0.0)))
    runs: List[Dict] = []

    for event in ordered:
        kind = event.get("type", "")
        # A hold's block ends when its sustain ends, not when its head arrives.
        finish = float(event.get("time", 0.0)) + float(event.get("duration", 0.0) or 0.0)

        if runs and runs[-1]["exercise"] == kind:
            runs[-1]["end"] = max(runs[-1]["end"], finish)
            runs[-1]["notes"] += 1
            continue

        runs.append({
            "exercise": kind,
            "start": float(event.get("time", 0.0)),
            "end": finish,
            "notes": 1,
        })

    merged: List[Dict] = []
    for run in runs:
        if merged and run["notes"] < min_notes:
            # Absorbed by the block before it: the stray note keeps its time, but it stops being
            # a boundary of its own.
            merged[-1]["end"] = max(merged[-1]["end"], run["end"])
            merged[-1]["notes"] += run["notes"]
            continue
        merged.append(run)

    # The rest is the gap to the next set. The last set has no next one, so it is given the
    # median rest of the others - a guess, but only ever used to time a banner.
    gaps = [merged[i + 1]["start"] - merged[i]["end"] for i in range(len(merged) - 1)]
    gaps = [g for g in gaps if g > 0]
    tail_rest = sorted(gaps)[len(gaps) // 2] if gaps else 0.0

    result: List[dict] = []
    for i, run in enumerate(merged):
        rest_end = merged[i + 1]["start"] if i + 1 < len(merged) else run["end"] + tail_rest
        result.append({
            "index": i,
            "exercise": run["exercise"],
            "start": round(run["start"], 3),
            "end": round(run["end"], 3),
            "restEnd": round(rest_end, 3),
            "notes": run["notes"],
        })
    return result


def write_atomic(document: dict, destination: Path) -> None:
    """Same rule as the generator: the file is either the old map or the new one, never half."""
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


def main(argv: List[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("beatmap", type=Path, nargs="+", help="beatmap JSON file(s) to update")
    parser.add_argument("--min-notes", type=int, default=MIN_SET_NOTES,
                        help=f"runs shorter than this merge into the previous set (default {MIN_SET_NOTES})")
    parser.add_argument("--dry-run", action="store_true", help="report only, write nothing")
    args = parser.parse_args(argv)

    for path in args.beatmap:
        if not path.exists():
            print(f"{path}: not found", file=sys.stderr)
            return 1

        document = json.loads(path.read_text(encoding="utf-8"))
        events = document.get("events") or []
        sets = derive_sets(events, args.min_notes)

        if not sets:
            print(f"{path.name}: no events, nothing to do")
            continue

        work = [s["end"] - s["start"] for s in sets]
        rests = [s["restEnd"] - s["end"] for s in sets[:-1]]
        print(f"{path.name}: {len(events)} events -> {len(sets)} sets, "
              f"work {min(work):.1f}-{max(work):.1f}s, "
              f"rest {min(rests):.1f}-{max(rests):.1f}s" if rests else
              f"{path.name}: {len(events)} events -> {len(sets)} sets")

        if args.dry_run:
            for s in sets:
                print(f"  {s['index']:>3}  {s['exercise']:<10} {s['start']:>7.2f} - {s['end']:>7.2f}"
                      f"  rest to {s['restEnd']:>7.2f}  ({s['notes']} notes)")
            continue

        document["sets"] = sets
        write_atomic(document, path)
        print(f"  written: {path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
