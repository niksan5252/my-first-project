/// One validated, playable note. Deliberately a separate type from BeatmapEvent: the DTO carries
/// a raw string for its type and unchecked numbers, and none of that should ever reach the spawn
/// path. By the time something is a BeatmapCue it is known to name a real CueType, to sit in a
/// real lane, and to have a duration that agrees with whether it is a hold.
public readonly struct BeatmapCue
{
    public readonly float time;      // when this note must ARRIVE at the hit zone
    public readonly CueType type;
    public readonly int lane;
    public readonly float duration;  // seconds of sustain; 0 for one-shot notes
    public readonly float intensity; // 0..1

    public BeatmapCue(float time, CueType type, int lane, float duration, float intensity)
    {
        this.time = time;
        this.type = type;
        this.lane = lane;
        this.duration = duration;
        this.intensity = intensity;
    }

    public bool IsHold => duration > 0f;
}
