using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.Rendering;

/// Records the tunnel to an MP4 with the track's audio: Game View (so the Screen Space Overlay HUD
/// is in it), 1920x1080, a constant 60 fps. Tunnel > Record Full Track presses Play, starts the
/// recorder on the first frame and stops it - and Play - once the track has finished.
///
/// The settings are built here in code rather than kept in the Recorder window, whose state lives
/// in Library/ and is neither versioned nor shared.
///
/// Next to every video goes a <name>_sync.csv: for each recorded frame, the video time and the
/// SongTime the game used on it. In the file, video time t is also audio time t (the recorder
/// renders exactly one frame's worth of audio per frame), so the two columns must advance
/// together; a constant difference between them is where the music starts in the recording.
[InitializeOnLoad]
public static class TunnelRecording
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const float Fps = 60f;
    // Recorded past the last sample, so the video does not cut on the final note's tail.
    private const float TailSeconds = 1f;
    private const float TestSeconds = 15f;

    private const string PendingKey = "TunnelRecording.PendingSeconds";   // <0 none, 0 full track

    private static RecorderController controller;
    private static float stopAfter;          // 0: when the track ends; else seconds of video
    private static int firstFrame;
    private static string outputBase;
    private static StringBuilder sync;
    private static int lastLoggedFrame;
    private static bool restoreMute;

    public static string OutputFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));

    static TunnelRecording()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tunnel/Record Full Track", priority = 0)]
    private static void RecordFullTrack() => Launch(0f);

    [MenuItem("Tunnel/Record Test (15 s)", priority = 1)]
    private static void RecordTest() => Launch(TestSeconds);

    [MenuItem("Tunnel/Open Recordings Folder", priority = 20)]
    private static void OpenFolder()
    {
        Directory.CreateDirectory(OutputFolder);
        EditorUtility.RevealInFinder(OutputFolder);
    }

    [MenuItem("Tunnel/Record Full Track", true)]
    [MenuItem("Tunnel/Record Test (15 s)", true)]
    private static bool CanLaunch() => !EditorApplication.isPlayingOrWillChangePlaymode;

    private static void Launch(float seconds)
    {
        SessionState.SetFloat(PendingKey, seconds);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            float pending = SessionState.GetFloat(PendingKey, -1f);
            SessionState.EraseFloat(PendingKey);
            if (pending >= 0f) Begin(pending);
        }
        else if (change == PlayModeStateChange.ExitingPlayMode && controller != null)
        {
            Finish();
        }
    }

    private static void Begin(float seconds)
    {
        var spawner = UnityEngine.Object.FindFirstObjectByType<CueSpawner>();
        string track = spawner != null && spawner.Music != null ? spawner.Music.name : "tunnel";
        Directory.CreateDirectory(OutputFolder);
        outputBase = Path.Combine(OutputFolder, track + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                                               + (seconds > 0f ? "_test" : ""));

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Tunnel Movie";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
        };
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = Width, OutputHeight = Height };
        movie.AudioInputSettings.PreserveAudio = true;
        movie.OutputFile = outputBase;

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.FrameRate = Fps;
        // Uncapped would render as fast as the machine can, which is fine for the video but makes
        // the Editor unwatchable while it records; capped keeps it at real time or slower.
        settings.CapFrameRate = true;

        // The Game view's Mute Audio mutes the master output, and the recorder captures the
        // master output: left on, it produces a video with a silent audio track. Lifted for the
        // recording and put back afterwards.
        restoreMute = EditorUtility.audioMasterMute;
        EditorUtility.audioMasterMute = false;

        controller = new RecorderController(settings);
        controller.PrepareRecording();
        if (!controller.StartRecording())
        {
            Debug.LogError("[TunnelRecording] the recorder did not start - see the messages above");
            controller = null;
            return;
        }

        stopAfter = seconds;
        // The first rendered frame after this is not in the video yet: the recorder captures from
        // the one after it. Measured, not assumed - dspTime does not advance across that frame,
        // i.e. no audio is rendered for it, and the video has exactly one frame per row from here.
        firstFrame = Time.frameCount + 2;
        lastLoggedFrame = -1;
        sync = new StringBuilder("frame,videoTime,songTime,dspTime\n");
        RenderPipelineManager.endContextRendering += LogFrame;
        EditorApplication.update += Watch;
        Debug.Log("[TunnelRecording] recording " + (seconds > 0f ? seconds + " s" : "the full track")
                + " to " + outputBase + ".mp4");
    }

    /// One row per rendered frame: what the game believed the song time was on it.
    private static void LogFrame(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
    {
        if (sync == null || Time.frameCount == lastLoggedFrame) return;
        lastLoggedFrame = Time.frameCount;
        int frame = Time.frameCount - firstFrame;
        if (frame < 0) return;
        var spawner = UnityEngine.Object.FindFirstObjectByType<CueSpawner>();
        var clock = spawner != null ? spawner.Clock : null;
        string song = clock != null && clock.IsRunning ? clock.SongTime.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) : "";
        sync.Append(frame).Append(',')
            .Append((frame / Fps).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(song).Append(',')
            .Append(AudioSettings.dspTime.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
    }

    private static void Watch()
    {
        if (controller == null || !EditorApplication.isPlaying) return;
        float video = (Time.frameCount - firstFrame) / Fps;
        bool done;
        if (stopAfter > 0f) done = video >= stopAfter;
        else
        {
            var spawner = UnityEngine.Object.FindFirstObjectByType<CueSpawner>();
            var clip = spawner != null ? spawner.Music : null;
            var clock = spawner != null ? spawner.Clock : null;
            done = clip != null && clock != null && clock.IsRunning && clock.SongTime >= clip.length + TailSeconds;
        }
        if (done) EditorApplication.ExitPlaymode();   // Finish runs on the way out
    }

    private static void Finish()
    {
        EditorApplication.update -= Watch;
        RenderPipelineManager.endContextRendering -= LogFrame;
        controller.StopRecording();
        controller = null;
        EditorUtility.audioMasterMute = restoreMute;
        if (sync != null) File.WriteAllText(outputBase + "_sync.csv", sync.ToString());
        sync = null;
        Debug.Log("[TunnelRecording] saved " + outputBase + ".mp4");
    }
}
