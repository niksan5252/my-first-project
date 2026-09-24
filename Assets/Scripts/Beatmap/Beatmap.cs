using System;

/// Wire format of a beatmap file, matching one-for-one what the librosa generator writes. These
/// are deliberately dumb DTOs: they exist only to survive JsonUtility and are converted into the
/// validated BeatmapCue form before anything in the game touches them.
///
/// Two constraints of UnityEngine.JsonUtility shaped this format and are worth knowing before
/// editing it:
///   * it cannot deserialize an enum from a string, only from its underlying int — so `type` is
///     carried as text and parsed by hand in BeatmapReader. Storing the int instead would mean
///     the generator wrote magic numbers that silently re-map the day CueType gains a value.
///   * it cannot deserialize a top-level array, which is why events live inside a wrapper object
///     rather than the file being a bare list.
[Serializable]
public class BeatmapEvent
{
    public float time;      // seconds from the start of the track
    // CueType by name: Kick | Punch | Jump | KickHold | PunchHold | Dodge | Curb | Duck
    public string type;
    // 0 .. laneCount-1. For the note types this is where on the road the cue stands. The
    // obstacles read it differently, and that is deliberate rather than an overload of
    // convenience: Curb and Duck span the whole road and ignore it, and Dodge reads it as a
    // SIDE - the left half of the lanes puts the wall on the left, the right half on the right.
    // A separate `direction` field would be a second thing to keep in step across the generator,
    // this DTO, the reader and the spawner, and it would say nothing the lane cannot.
    public int lane;
    public float duration;  // seconds of sustain; 0 (or absent) for one-shot notes
    public float intensity; // 0..1 onset strength; scales the hit flash and camera shake
}

/// One workout set: a run of bars doing one exercise, followed by its rest. Written by the
/// generator (which lays the sets out on the bar grid in the first place) and, for charts made
/// before the field existed, by Tools/beatmap/add_sets.py.
///
/// Nothing in the spawn path reads this: notes are still driven entirely by `events`. It exists
/// so presentation - the HUD's end-of-set banner - can know where a block of work ends without
/// having to guess it from the notes.
[Serializable]
public class BeatmapSet
{
    public int index;
    public string exercise;  // CueType by name, same spelling as BeatmapEvent.type
    public float start;      // first note of the set
    public float end;        // last note of the active part; the rest runs from here
    public float restEnd;    // where the next set begins
    public int notes;
}

[Serializable]
public class Beatmap
{
    public int version;
    public string track;       // which audio file this was generated from; diagnostics only
    public float bpm;          // tempo, for anything that wants to pulse with it
    public float audioOffset;  // global shift in seconds, for calibrating the audio path's latency
    public string generator;   // what produced this file, for when results drift
    public BeatmapEvent[] events;
    // Optional. A chart without it plays identically; only the HUD's set banners go quiet.
    public BeatmapSet[] sets;
}
