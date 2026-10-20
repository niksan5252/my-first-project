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
    // ArcHold only: degrees of arc the fist travels, signed (+ clockwise as the player sees it), and
    // the circle's radius in metres (0 = CueSpawner's default). 0 for every other type.
    public readonly float arc;
    public readonly float radius;
    public readonly int shape;       // ArcHold only: ArmPath's pattern

    public BeatmapCue(float time, CueType type, int lane, float duration, float intensity, float arc = 0f, float radius = 0f, int shape = 0)
    {
        this.time = time;
        this.type = type;
        this.lane = lane;
        this.duration = duration;
        this.intensity = intensity;
        this.arc = arc;
        this.radius = radius;
        this.shape = shape;
    }

    public bool IsHold => duration > 0f;
}
