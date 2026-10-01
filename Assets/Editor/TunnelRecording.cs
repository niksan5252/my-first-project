using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Records the tunnel to an MP4 with the track's audio: Game View (so the Screen Space Overlay HUD
/// is in it), a constant 60 fps, 1440p or 1080p. Tunnel > Record Full Track presses Play, starts
/// the recorder on the first frame and stops it - and Play - once the track has finished.
///
/// The settings are built here in code rather than kept in the Recorder window, whose state lives
/// in Library/ and is neither versioned nor shared.
///
/// The recording runs on a fixed time step, so how long a frame takes to render does not matter,
/// and the image is rendered well above what live play can afford - see RecordingQuality, which
/// raises it for the recording only and puts everything back afterwards.
///
/// Next to every video goes a <name>_sync.csv: for each recorded frame, the video time and the
/// SongTime the game used on it. In the file, video time t is also audio time t (the recorder
/// renders exactly one frame's worth of audio per frame), so the two columns must advance
/// together; a constant difference between them is where the music starts in the recording.
[InitializeOnLoad]
public static class TunnelRecording
{
    /// One way to record. The whole quality policy is this table: output size, bitrate, and how
    /// far the renderer is pushed above that size.
    private struct Profile
    {
        public string label;
        public int width, height;
        public float megabits;     // H.264 High profile target; <= 0 keeps the Recorder's "High" preset
        public float renderScale;  // URP render scale while recording; the image is downsampled to width x height
        public int msaa;           // MSAA samples while recording (1 = off)
        public bool smaa;          // camera SMAA (High) while recording
    }

    // 1440p is the default: YouTube serves 1440p and up with VP9, which looks visibly better than
    // its 1080p stream even on a 1080p screen. Both profiles render internally at 3840x2160.
    private static readonly Profile P1440 = new Profile
        { label = "1440p", width = 2560, height = 1440, megabits = 80f, renderScale = 1.5f, msaa = 4, smaa = true };
    private static readonly Profile P1080 = new Profile
        { label = "1080p", width = 1920, height = 1080, megabits = 50f, renderScale = 2f, msaa = 4, smaa = true };
    // 4K renders at 5760x3240 - the same 1.5x supersampling as 1440p. P4KNative is the fallback
    // for a GPU that cannot hold that, and the baseline to compare the supersampled one against.
    private static readonly Profile P4K = new Profile
        { label = "4K", width = 3840, height = 2160, megabits = 140f, renderScale = 1.5f, msaa = 4, smaa = true };
    private static readonly Profile P4KNative = new Profile
        { label = "4Knative", width = 3840, height = 2160, megabits = 140f, renderScale = 1f, msaa = 4, smaa = true };
    // What recordings were made with before: kept for comparisons.
    private static readonly Profile PLegacy = new Profile
        { label = "legacy1080p", width = 1920, height = 1080, megabits = 0f, renderScale = 1f, msaa = 1, smaa = false };

    private const float Fps = 60f;
    // Recorded past the last sample, so the video does not cut on the final note's tail.
    private const float TailSeconds = 1f;
    // Long enough to include the first punch notes (from ~19 s) after the intro walls.
    private const float TestSeconds = 33f;

    private const string PendingKey = "TunnelRecording.Pending";   // "seconds|profile|png"

    private static RecorderController controller;
    private static float stopAfter;          // 0: when the track ends; else seconds of video
    private static int firstFrame;
    private static string outputBase;
    private static StringBuilder sync;
    private static int lastLoggedFrame;
    private static bool restoreMute, audioReported;
    private static readonly System.Diagnostics.Stopwatch wallClock = new System.Diagnostics.Stopwatch();

    public static string OutputFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));

    static TunnelRecording()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        // A recording cut short by a crash or a domain reload never reached Finish; the render
        // pipeline asset would otherwise keep its recording settings.
        RecordingQuality.RestoreLeftovers();
    }

    [MenuItem("Tunnel/Record Full Track (1440p)", priority = 0)]
    private static void RecordFull1440() => Launch(0f, "1440p", false);

    [MenuItem("Tunnel/Record Full Track (1080p)", priority = 1)]
    private static void RecordFull1080() => Launch(0f, "1080p", false);

    [MenuItem("Tunnel/Record Full Track (4K)", priority = 2)]
    private static void RecordFull4K() => Launch(0f, "4K", false);

    [MenuItem("Tunnel/Record Test (33 s, 1440p)", priority = 3)]
    private static void RecordTest() => Launch(TestSeconds, "1440p", false);

    [MenuItem("Tunnel/Record Test (33 s, 4K)", priority = 4)]
    private static void RecordTest4K() => Launch(TestSeconds, "4K", false);

    // For a track with a long intro: the test starts LeadBeforeNotes seconds before the first
    // note, so the 33 s show the last intro walls and the first cubes instead of empty road.
    private const float LeadBeforeNotes = 8f;

    [MenuItem("Tunnel/Record Test (33 s from the first notes, 1440p)", priority = 5)]
    private static void RecordTestFromNotes() => Launch(TestSeconds, "1440p", false, true);

    // For a playlist: the test starts this long before its first transition, so the 33 s show the
    // end of one segment, the change of look and the start of the next.
    private const float LeadBeforeTransition = 12f;

    [MenuItem("Tunnel/Record Test (33 s across the playlist's first transition, 1440p)", priority = 6)]
    private static void RecordTestTransition() => Launch(TestSeconds, "1440p", false, false, true);

    [MenuItem("Tunnel/Record Test (33 s across the playlist's last transition, 1440p)", priority = 7)]
    private static void RecordTestLastTransition() => Launch(TestSeconds, "1440p", false, false, true, true);

    [MenuItem("Tunnel/Open Recordings Folder", priority = 20)]
    private static void OpenFolder()
    {
        Directory.CreateDirectory(OutputFolder);
        EditorUtility.RevealInFinder(OutputFolder);
    }

    [MenuItem("Tunnel/Record Full Track (1440p)", true)]
    [MenuItem("Tunnel/Record Full Track (1080p)", true)]
    [MenuItem("Tunnel/Record Full Track (4K)", true)]
    [MenuItem("Tunnel/Record Test (33 s, 1440p)", true)]
    [MenuItem("Tunnel/Record Test (33 s, 4K)", true)]
    [MenuItem("Tunnel/Record Test (33 s from the first notes, 1440p)", true)]
    [MenuItem("Tunnel/Record Test (33 s across the playlist's first transition, 1440p)", true)]
    [MenuItem("Tunnel/Record Test (33 s across the playlist's last transition, 1440p)", true)]
    private static bool CanLaunch() => !EditorApplication.isPlayingOrWillChangePlaymode;

    /// For quality comparisons: any profile ("1440p", "1080p", "4K", "4Knative", "legacy1080p"), optionally with a
    /// lossless PNG of every frame next to the video to measure what the encoder loses.
    public static void LaunchDiagnostic(float seconds, string profile, bool png) => Launch(seconds, profile, png);

    private static void Launch(float seconds, string profile, bool png, bool fromNotes = false, bool atTransition = false, bool last = false)
    {
        SessionState.SetString(PendingKey, seconds.ToString(CultureInfo.InvariantCulture) + "|" + profile + "|" + (png ? "1" : "0")
                                           + "|" + (fromNotes ? "1" : atTransition ? (last ? "3" : "2") : "0"));
        EditorApplication.EnterPlaymode();
    }

    private static void StartNearFirstNotes()
    {
        var spawner = UnityEngine.Object.FindAnyObjectByType<CueSpawner>();
        if (spawner == null) return;
        float first = spawner.FirstNoteTime;
        if (first > LeadBeforeNotes) spawner.SetStartTime(first - LeadBeforeNotes);
    }

    private static void StartBeforeTransition(bool last)
    {
        var spawner = UnityEngine.Object.FindAnyObjectByType<CueSpawner>();
        var timeline = PlaylistDirector.Active != null ? PlaylistDirector.Active.Timeline : null;
        if (spawner == null || timeline == null || timeline.spans.Count < 2)
        {
            Debug.LogWarning("[TunnelRecording] no playlist with a transition - recording from the start");
            return;
        }
        var span = timeline.spans[last ? timeline.spans.Count - 1 : 1];
        spawner.SetStartTime(Mathf.Max(0f, span.inStart - LeadBeforeTransition));
    }

    private static Profile Find(string label) =>
        label == P1080.label ? P1080 : label == P4K.label ? P4K : label == P4KNative.label ? P4KNative
      : label == PLegacy.label ? PLegacy : P1440;

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            string pending = SessionState.GetString(PendingKey, "");
            SessionState.EraseString(PendingKey);
            if (pending.Length == 0) return;
            var parts = pending.Split('|');
            if (parts.Length > 3 && parts[3] == "1") StartNearFirstNotes();
            if (parts.Length > 3 && parts[3] == "2") StartBeforeTransition(false);
            if (parts.Length > 3 && parts[3] == "3") StartBeforeTransition(true);
            Begin(float.Parse(parts[0], CultureInfo.InvariantCulture), Find(parts[1]), parts[2] == "1");
        }
        else if (change == PlayModeStateChange.ExitingPlayMode && controller != null)
        {
            Finish();
        }
    }

    private static void Begin(float seconds, Profile profile, bool png)
    {
        var spawner = UnityEngine.Object.FindFirstObjectByType<CueSpawner>();
        string track = spawner != null && !string.IsNullOrEmpty(spawner.TrackName) ? spawner.TrackName : "tunnel";
        Directory.CreateDirectory(OutputFolder);
        outputBase = Path.Combine(OutputFolder, track + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                                               + "_" + profile.label + (seconds > 0f ? "_test" : ""));

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.AddRecorderSettings(MovieSettings(profile));
        if (png) settings.AddRecorderSettings(PngSettings(profile));
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
        RecordingQuality.Apply(profile.renderScale, profile.msaa, profile.smaa);

        controller = new RecorderController(settings);
        controller.PrepareRecording();
        if (!controller.StartRecording())
        {
            Debug.LogError("[TunnelRecording] the recorder did not start - see the messages above");
            controller = null;
            RecordingQuality.Restore();
            EditorUtility.audioMasterMute = restoreMute;
            return;
        }

        stopAfter = seconds;
        // The first rendered frame after this is not in the video yet: the recorder captures from
        // the one after it. Measured, not assumed - dspTime does not advance across that frame,
        // i.e. no audio is rendered for it, and the video has exactly one frame per row from here.
        firstFrame = Time.frameCount + 2;
        lastLoggedFrame = -1;
        audioReported = false;
        sync = new StringBuilder("frame,videoTime,songTime,dspTime\n");
        RenderPipelineManager.endContextRendering += LogFrame;
        EditorApplication.update += Watch;
        wallClock.Restart();
        Debug.Log("[TunnelRecording] recording " + (seconds > 0f ? seconds + " s" : "the full track")
                + " at " + profile.width + "x" + profile.height + " to " + outputBase + ".mp4");
    }

    private static MovieRecorderSettings MovieSettings(Profile profile)
    {
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Tunnel Movie";
        movie.Enabled = true;
        // The Recorder's quality presets ("High" included) take Unity's legacy encoder path, which
        // writes H.264 Baseline at ~8 Mbit/s at 1080p60 - the soft, blocky look of the first
        // recordings. Custom takes the path with a profile, B-frames and a bitrate of our choosing.
        movie.EncoderSettings = profile.megabits > 0f
            ? new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom,
                EncodingProfile = CoreEncoderSettings.H264EncodingProfile.High,
                TargetBitRate = profile.megabits,
            }
            : new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = profile.width, OutputHeight = profile.height };
        movie.AudioInputSettings.PreserveAudio = true;
        movie.OutputFile = outputBase;
        return movie;
    }

    private static ImageRecorderSettings PngSettings(Profile profile)
    {
        var png = ScriptableObject.CreateInstance<ImageRecorderSettings>();
        png.name = "Tunnel Frames";
        png.Enabled = true;
        png.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.PNG;
        png.imageInputSettings = new GameViewInputSettings { OutputWidth = profile.width, OutputHeight = profile.height };
        png.OutputFile = outputBase + "_png/frame_" + DefaultWildcard.Frame;
        return png;
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
        string song = clock != null && clock.IsRunning ? clock.SongTime.ToString("F4", CultureInfo.InvariantCulture) : "";
        sync.Append(frame).Append(',')
            .Append((frame / Fps).ToString("F4", CultureInfo.InvariantCulture)).Append(',')
            .Append(song).Append(',')
            .Append(AudioSettings.dspTime.ToString("F4", CultureInfo.InvariantCulture)).Append('\n');
    }

    private static void Watch()
    {
        if (controller == null || !EditorApplication.isPlaying) return;
        // Begin lifts the Game view's Mute Audio once, but the Editor can apply it again later (the
        // Game view regaining focus, a layout change) and the rest of a 9-minute recording would be
        // silent without a word. So it is kept lifted for as long as the recorder runs.
        if (EditorUtility.audioMasterMute) { EditorUtility.audioMasterMute = false; Debug.LogWarning("[TunnelRecording] the Editor muted audio again - lifted"); }
        if (AudioListener.pause) AudioListener.pause = false;
        float video = (Time.frameCount - firstFrame) / Fps;
        if (!audioReported && video >= 3f) { audioReported = true; ReportAudio(); }
        bool done;
        if (stopAfter > 0f) done = video >= stopAfter;
        else
        {
            // The track's length - or, for a playlist, the whole run's, every segment end to end.
            var spawner = UnityEngine.Object.FindFirstObjectByType<CueSpawner>();
            float length = spawner != null ? spawner.TrackLength : 0f;
            var clock = spawner != null ? spawner.Clock : null;
            done = length > 0f && clock != null && clock.IsRunning && clock.SongTime >= length + TailSeconds;
        }
        if (done) EditorApplication.ExitPlaymode();   // Finish runs on the way out
    }

    /// A line in the log, 3 s into the video, saying whether anything is playing: a silent
    /// recording is found out here, not after the last frame.
    private static void ReportAudio()
    {
        var sb = new StringBuilder("[TunnelRecording] audio at 3 s: masterMute=" + EditorUtility.audioMasterMute
            + " listenerVolume=" + AudioListener.volume + " listeners=" + UnityEngine.Object.FindObjectsByType<AudioListener>().Length);
        int playing = 0;
        foreach (var src in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
            if (src.isPlaying) { playing++; sb.Append(" | ").Append(src.name).Append(" clip=").Append(src.clip != null ? src.clip.name : "null").Append(" volume=").Append(src.volume).Append(" mute=").Append(src.mute); }
        if (playing == 0) Debug.LogError(sb.Append(" | NOTHING IS PLAYING - this recording will be silent").ToString());
        else Debug.Log(sb.ToString());
    }

    private static void Finish()
    {
        EditorApplication.update -= Watch;
        RenderPipelineManager.endContextRendering -= LogFrame;
        controller.StopRecording();
        controller = null;
        RecordingQuality.Restore();
        EditorUtility.audioMasterMute = restoreMute;
        if (sync != null) File.WriteAllText(outputBase + "_sync.csv", sync.ToString());
        sync = null;
        Debug.Log("[TunnelRecording] saved " + outputBase + ".mp4 - "
                + ((Time.frameCount - firstFrame) / Fps).ToString("F1") + " s of video in "
                + wallClock.Elapsed.TotalSeconds.ToString("F1") + " s");
    }

    /// Render settings raised for a recording and put back afterwards. The render pipeline asset
    /// is a project asset, so a change to it outlives Play mode: the original values are kept in
    /// SessionState too, and put back on the next load if the Editor never got to Restore.
    private static class RecordingQuality
    {
        private const string SavedKey = "TunnelRecording.SavedQuality";   // "asset path|renderScale|msaa"

        public static void Apply(float renderScale, int msaa, bool smaa)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                if (SessionState.GetString(SavedKey, "").Length == 0)
                    SessionState.SetString(SavedKey, AssetDatabase.GetAssetPath(urp) + "|"
                        + urp.renderScale.ToString(CultureInfo.InvariantCulture) + "|" + urp.msaaSampleCount);
                urp.renderScale = renderScale;
                urp.msaaSampleCount = msaa;
            }
            // The camera is a scene object, so this one reverts with Play mode by itself.
            var camera = Camera.main;
            var data = camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;
            if (data != null && smaa)
            {
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
        }

        public static void Restore() => RestoreLeftovers();

        public static void RestoreLeftovers()
        {
            string saved = SessionState.GetString(SavedKey, "");
            if (saved.Length == 0) return;
            SessionState.EraseString(SavedKey);
            var parts = saved.Split('|');
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(parts[0]);
            if (urp == null) return;
            urp.renderScale = float.Parse(parts[1], CultureInfo.InvariantCulture);
            urp.msaaSampleCount = int.Parse(parts[2], CultureInfo.InvariantCulture);
        }
    }
}
