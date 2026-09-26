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
    ///
    /// Read once per frame and then held, so everything in a frame sees the same moment, and
    /// SMOOTHED - see Smooth - because dspTime itself is a staircase.
    public float SongTime
    {
        get
        {
            if (offline) return offlineTime - offset - deviceOffset;
            float device = Capturing ? 0f : deviceOffset;
            if (!running) return -offset - device;
            if (smoothFrame != Time.frameCount)
            {
                smoothFrame = Time.frameCount;
                smoothTime = Smooth((AudioSettings.dspTime - startDsp) - BufferLatency - device - offset);
            }
            return smoothTime;
        }
    }

    // --- Smoothing ---
    //
    // dspTime only moves when the mixer finishes a block: in 10.7 ms steps (512 samples at 48 kHz),
    // sometimes two at once. Measured in the Editor at ~220 fps, song time stood still on 55% of
    // frames and then jumped, so the camera and every cue moved in a stutter - still, still, jump.
    // In a recording each 16.7 ms frame got 10.7 or 21.3 ms of song time, i.e. everything ran at
    // 0.64x and 1.28x speed on alternate frames.
    //
    // The fix is the usual one: advance song time with the frame clock, which is smooth, and use
    // the audio clock only to keep it honest. `smoothBias` is song time minus frame-clock time.
    //   * Live, it follows the dsp reading through a one-second low-pass: the steps average out,
    //     real drift between the two clocks (parts per million) is still followed, and the average
    //     song time equals the raw one - so beatmapUserOffset, calibrated against it, still holds.
    //   * While the Recorder captures, the frame clock advances by exactly one frame of audio per
    //     frame, and dspTime is that position rounded DOWN to a block. So the true bias is the
    //     largest one seen: taking the running maximum lands on it within a few frames.
    // A reading more than SnapSeconds away (a hitch, a pause, a seek) is taken as it is.
    private const double SnapSeconds = 0.1;
    private const double TrackSeconds = 1.0;
    private double smoothBias;
    private bool smoothValid;
    private bool smoothCapturing;
    private int smoothFrame = -1;
    private float smoothTime;

    private float Smooth(double raw)
    {
        bool capturing = Capturing;
        double local = capturing ? Time.timeAsDouble : Time.unscaledTimeAsDouble;
        double error = raw - local;
        if (!smoothValid || capturing != smoothCapturing || System.Math.Abs(error - smoothBias) > SnapSeconds)
        {
            smoothBias = error;
            smoothValid = true;
            smoothCapturing = capturing;
        }
        else if (capturing) smoothBias = System.Math.Max(smoothBias, error);
        else smoothBias += (error - smoothBias) * (1.0 - System.Math.Exp(-Time.unscaledDeltaTime / TrackSeconds));
        return (float)(local + smoothBias);
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
    public void Begin(AudioSource audioSource, float leadIn, float audioOffset, float deviceLatency) =>
        Begin(audioSource, leadIn, audioOffset, deviceLatency, 0f);

    /// As above, but starting `startAt` seconds into the track: the audio plays from there and the
    /// clock reads startAt as it starts. For a test recording of a track whose notes begin late.
    public void Begin(AudioSource audioSource, float leadIn, float audioOffset, float deviceLatency, float startAt)
    {
        source = audioSource;
        offset = audioOffset;
        deviceOffset = deviceLatency;
        offline = false;
        running = true;
        smoothValid = false;
        smoothFrame = -1;
        bufferLatency = DspBufferLatency();

        double now = AudioSettings.dspTime;
        double playDsp = now + Mathf.Max(0f, leadIn);
        startAt = Mathf.Max(0f, startAt);
        startDsp = playDsp - startAt;

        if (source != null && source.clip != null)
        {
            source.Stop();
            source.time = Mathf.Min(startAt, Mathf.Max(0f, source.clip.length - 0.01f));
            source.PlayScheduled(playDsp);
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
