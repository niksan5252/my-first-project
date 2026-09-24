/// What CueSpawner hands out when a note is scored at the hit zone. A struct, and passed by
/// value, so a subscriber can keep it without holding on to any of the spawner's pooled state.
///
/// "Scored" means the note is done: a one-shot cue at the moment it crosses the hit zone, a hold
/// at the moment its far end has gone through. A hold therefore raises this exactly once, at its
/// end, which is what makes a combo count one sustained note as one.
public readonly struct CueHitInfo
{
    public readonly CueType type;
    public readonly int lane;
    public readonly float intensity;  // 0..1 from the chart; the HUD scales its pulse with it
    public readonly float songTime;   // the clock the note was scored on, not Time.time
    public readonly bool wasHold;

    public CueHitInfo(CueType type, int lane, float intensity, float songTime, bool wasHold)
    {
        this.type = type;
        this.lane = lane;
        this.intensity = intensity;
        this.songTime = songTime;
        this.wasHold = wasHold;
    }
}
