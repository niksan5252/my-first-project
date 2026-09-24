using UnityEngine;

/// The one authority on "where are we in the track". Everything that has to line up with the
/// music reads SongTime from here and nothing reads Time.time.
///
/// Built on AudioSettings.dspTime rather than audioSource.time on purpose. audioSource.time is
/// resampled from the playback head once per frame, so it quantises to the frame rate and jitters
/// with it; dspTime is the audio hardware's own clock, advances independently of rendering, and
/// is what PlayScheduled measures against. For anything that has to hit a beat, that difference
/// is the difference between "tight" and "nearly right".
///
/// Begin() can delay playback by a lead-in and reports NEGATIVE SongTime until the audio starts.
/// CueSpawner passes 0 - the music starts at once and the notes are what wait, see
/// CueSpawner.IntroCutoff - but the lead-in stays available for anything that needs silence first.
public sealed class SongClock
{
    private AudioSource source;
    private double startDsp;   // dspTime at which SongTime reads exactly 0
    private float offset;      // positive shifts the whole chart later, calibrating audio latency
    private bool running;

    // Offline mode: SongTime comes from a value pushed in by hand instead of the audio clock.
    // This is how the track can be rehearsed with no audio attached at all, and how the timing
    // self-check steps the sequencer deterministically rather than at the mercy of frame pacing.
    private bool offline;
    private float offlineTime;

    public bool IsRunning => running;
    public bool IsOffline => offline;

    /// Seconds into the track. Negative during the lead-in, 0 exactly as the audio begins.
    public float SongTime
    {
        get
        {
            if (offline) return offlineTime - offset;
            if (!running) return -offset;
            return (float)(AudioSettings.dspTime - startDsp) - offset;
        }
    }

    /// Starts the clock, scheduling audio to begin leadIn seconds from now. A null source, or one
    /// with no clip, is not an error: the clock still runs and the tunnel still plays the chart,
    /// which is exactly what is wanted when testing timing without a track.
    public void Begin(AudioSource audioSource, float leadIn, float audioOffset)
    {
        source = audioSource;
        offset = audioOffset;
        offline = false;
        running = true;

        double now = AudioSettings.dspTime;
        startDsp = now + Mathf.Max(0f, leadIn);

        if (source != null && source.clip != null)
        {
            source.Stop();
            source.PlayScheduled(startDsp);
        }
    }

    /// Drives the clock by hand. Used by the offline timing check and by any rehearsal that has
    /// to advance without real audio.
    public void BeginOffline(float startTime, float audioOffset)
    {
        source = null;
        offset = audioOffset;
        offline = true;
        running = true;
        offlineTime = startTime;
    }

    public void SetOfflineTime(float t)
    {
        if (!offline) return;
        offlineTime = t;
    }

    public void Stop()
    {
        running = false;
        offline = false;
        if (source != null) source.Stop();
    }
}
