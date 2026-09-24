using UnityEngine;

/// Turns the chart's tempo into the unit the HUD actually animates in: the beat.
///
/// Why the HUD cares. Every duration it used to hold was a number of seconds picked against one
/// track at 140 BPM, so on a slower track the combo pulse ran ahead of the music and the banner
/// left before the phrase did. Expressed in beats, the same numbers mean the same MUSICAL length
/// at any tempo: a banner that holds for four beats holds for one bar whether the track is 100
/// or 170 BPM.
///
/// It reads SongClock and never writes to it - the clock stays the one authority on where the
/// track is, and this only divides by the tempo.
public sealed class HudBeatClock
{
    private const float MinBpm = 20f;
    private const float MaxBpm = 400f;

    private float bpm;
    private float fallbackBpm = 120f;
    private SongClock song;

    // Song time of the first note that was scored. The beat grid is anchored there rather than at
    // song time zero: the generator fits its beat comb to the whole track, so beat one does not
    // have to land on t=0, but every note in the chart DOES sit on that grid. Anchoring on a real
    // note is therefore the one way to make a flicker that pulses with the music rather than near
    // it - without reaching into the beatmap code to ask where the grid starts.
    private float anchor;
    private bool anchored;

    public float Bpm => bpm > 0f ? bpm : fallbackBpm;
    public float SecondsPerBeat => 60f / Mathf.Clamp(Bpm, MinBpm, MaxBpm);
    public bool HasChartTempo => bpm > 0f;

    public void Bind(SongClock clock) => song = clock;

    /// Takes the tempo the chart declares. A nonsense value (a missing field parses as 0) leaves
    /// the fallback in place instead of producing a beat of infinite length.
    public void SetTempo(float chartBpm, float fallback)
    {
        fallbackBpm = Mathf.Clamp(fallback > 0f ? fallback : 120f, MinBpm, MaxBpm);
        bpm = (chartBpm >= MinBpm && chartBpm <= MaxBpm) ? chartBpm : 0f;
    }

    public void AnchorTo(float songTime)
    {
        if (anchored) return;
        anchor = songTime;
        anchored = true;
    }

    public void Reset() => anchored = false;

    /// Beats to seconds. Everything the HUD animates goes through here.
    public float Seconds(float beats) => Mathf.Max(0f, beats) * SecondsPerBeat;

    /// Where we are inside the current flicker cycle, 0..1, with 0 exactly on a beat.
    /// `cyclesPerBeat` of 1 pulses once per beat, 2 pulses on every eighth.
    public float Phase(float cyclesPerBeat)
    {
        float now = (song != null && song.IsRunning) ? song.SongTime : Time.time;
        float beats = (now - (anchored ? anchor : 0f)) / SecondsPerBeat;
        float cycles = beats * Mathf.Max(0.01f, cyclesPerBeat);
        return cycles - Mathf.Floor(cycles);
    }
}
