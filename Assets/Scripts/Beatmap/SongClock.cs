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
/// CueSpawner uses only a few DSP buffers of it, see CueSpawner.StartSongClock.
///
/// dspTime counts samples the mixer has PRODUCED, and those reach the speakers later: they wait in
/// Unity's own DSP buffers and then in the OS audio stack. Read raw, SongTime runs ahead of what is
/// heard by that whole delay. The DSP-buffer part is known exactly - bufferLength x numBuffers /
/// outputSampleRate - and is subtracted here, so it follows the Audio project setting on its own.
/// Measured against a WASAPI loopback of the output, going from 1024x4 to 512x4 moved the delay
/// by 42.8 ms against the 42.7 ms this formula predicts. What remains belongs to the device and
/// OS, is not visible to Unity, and is CueSpawner.beatmapUserOffset's job.
///
/// Neither of those two exists while the Recorder captures a video. It runs the game on a fixed
/// time step (Time.captureDeltaTime > 0) and renders the mix itself, one frame's worth of samples
/// per frame, straight into the file: nothing is buffered for a device and nothing reaches a
/// monitor. Subtracting them there put the notes in the video ~73 ms behind the music, so while
/// time is captured SongTime is the mixer's position with only the chart's own offset applied.
public sealed class SongClock
{
    private AudioSource source;
    private double startDsp;   // dspTime at which the audio starts, i.e. SongTime 0 at the mixer
    private float offset;      // the chart's own audioOffset; positive shifts the whole chart later
    private float deviceOffset; // latency calibration of the speakers/monitor, live playback only
    private float bufferLatency;
    private bool running;

    // Offline mode: SongTime comes from a value pushed in by hand instead of the audio clock.
    // This is how the track can be rehearsed with no audio attached at all, and how the timing
    // self-check steps the sequencer deterministically rather than at the mercy of frame pacing.
    private bool offline;
    private float offlineTime;

    public bool IsRunning => running;
    public bool IsOffline => offline;

    /// Seconds of Unity's DSP buffering subtracted from SongTime. 0 offline, where nothing plays.
    public float BufferLatency => offline || Capturing ? 0f : bufferLatency;

    /// True while the game runs on a captured, fixed time step - i.e. while the Recorder writes a
    /// video. Audio then goes to the file, not through a device, so no output latency applies.
    public static bool Capturing => Time.captureDeltaTime > 0f;

    /// Seconds into the track as it is being heard. Negative during the lead-in and the buffer
    /// latency; 0 as the first sample leaves Unity's buffers.
    public float SongTime
    {
        get
        {
            if (offline) return offlineTime - offset - deviceOffset;
            float device = Capturing ? 0f : deviceOffset;
            if (!running) return -offset - device;
            return (float)(AudioSettings.dspTime - startDsp) - BufferLatency - device - offset;
        }
    }

    /// Unity's own output buffering, in seconds, from the live audio configuration.
    public static float DspBufferLatency()
    {
        AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
        return bufferLength * numBuffers / (float)Mathf.Max(1, AudioSettings.outputSampleRate);
    }

    /// Starts the clock, scheduling audio to begin leadIn seconds from now. A null source, or one
    /// with no clip, is not an error: the clock still runs and the tunnel still plays the chart,
    /// which is exactly what is wanted when testing timing without a track.
    public void Begin(AudioSource audioSource, float leadIn, float audioOffset, float deviceLatency)
    {
        source = audioSource;
        offset = audioOffset;
        deviceOffset = deviceLatency;
        offline = false;
        running = true;
        bufferLatency = DspBufferLatency();

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
    public void BeginOffline(float startTime, float audioOffset, float deviceLatency)
    {
        source = null;
        offset = audioOffset;
        deviceOffset = deviceLatency;
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
