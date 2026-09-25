using System.Collections.Generic;

/// Walks a time-sorted note list and hands out each note at the moment it has to be SPAWNED,
/// which is one flight time before it is due at the hit zone.
///
/// A cursor rather than a queue that gets drained: the list is immutable and reusable, so
/// restarting a track is just Rewind() instead of re-reading and re-validating the file. The
/// cursor also makes a dropped frame harmless — a stall that spans several notes releases all of
/// them on the next call instead of losing the ones it slept through.
public sealed class BeatmapSequencer
{
    private readonly List<BeatmapCue> cues;
    private int cursor;

    public BeatmapSequencer(List<BeatmapCue> cues)
    {
        this.cues = cues ?? new List<BeatmapCue>();
    }

    public int Count => cues.Count;
    /// Read-only access to the loaded chart, for presentation that plans ahead (CenterWords).
    public BeatmapCue this[int index] => cues[index];
    public int Remaining => cues.Count - cursor;
    public bool Finished => cursor >= cues.Count;

    /// The last note's arrival time, so a caller can tell when the chart is played out.
    public float LastCueTime => cues.Count == 0 ? 0f : cues[cues.Count - 1].time;

    public void Rewind() => cursor = 0;

    /// Releases the next note whose spawn moment has arrived, i.e. whose arrival time minus the
    /// flight time has been reached. Call repeatedly until it returns false: several notes can
    /// come due in the same frame, and a chord across lanes always does.
    public bool TryDequeue(float songTime, float flightTime, out BeatmapCue cue)
    {
        if (cursor < cues.Count && cues[cursor].time - flightTime <= songTime)
        {
            cue = cues[cursor];
            cursor++;
            return true;
        }

        cue = default;
        return false;
    }
}
