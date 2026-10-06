using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// The video trainer (VideoSilhouette).
///   Reset Clips And Table - the clips in Assets/Silhouette/Clips (green-screen takes keyed by
///     Tools/silhouette/key_green.py), the moves cut out of each (clip seconds, measured on the
///     takes) and the note -> move table, after the brief.
///   Bake Frames - each clip's used stretch to a flipbook PNG in Assets/Silhouette/Baked (by
///     Tools/silhouette/bake_frames.py), imported as a Texture2DArray.
///   Report Video9 - which move every kind of note in the Video 9 charts gets.
///   Setup - bakes and turns the video trainer on in the tunnel scene.
public static class VideoSilhouetteSetup
{
    private const string ClipFolder = "Assets/Silhouette/Clips";
    private const string BakedFolder = "Assets/Silhouette/Baked";
    public const string SetPath = "Assets/Silhouette/VideoSilhouetteSet.asset";
    private const string ScenePath = "Assets/Scenes/TunnelScene.unity";
    private const string PlaylistPath = "Assets/Themes/Playlists/Playlist_Video9.asset";
    private const string Script = "Tools/silhouette/bake_frames.py";
    private const int LaneCount = 4;

    [System.Serializable]
    private class BakeResult
    {
        public float fps, rangeStart, rangeEnd;
        public int frames, columns, rows, width, height, sourceFrames;
    }

    [MenuItem("Tunnel/Video Silhouette/Setup")]
    public static void Setup()
    {
        var set = Bake();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
        foreach (var trainer in Object.FindObjectsByType<SilhouetteTrainer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(trainer);
            so.FindProperty("useVideoSilhouette").boolValue = true;
            so.FindProperty("videoSet").objectReferenceValue = set;
            so.ApplyModifiedProperties();
        }
        EditorSceneManager.SaveScene(scene);
    }

    // --- the clips and their moves (clip seconds) ---

    private static VideoSilhouetteSet.Move M(string group, VideoSilhouetteSet.Side side, float start, float peak, float end,
                                             float loopStart = 0f, float loopEnd = 0f, bool holdLoops = false) =>
        new VideoSilhouetteSet.Move { group = group, side = side, start = start, peak = peak, end = end, loopStart = loopStart, loopEnd = loopEnd, holdLoops = holdLoops };

    private const VideoSilhouetteSet.Side L = VideoSilhouetteSet.Side.Left, R = VideoSilhouetteSet.Side.Right, N = VideoSilhouetteSet.Side.None;

    /// "степ ногой": a step out to the right, arms swinging. Left = mirrored.
    /// "марширует": knees R 1.83, L 2.375 on one 1.125 s repeat; held = frozen up.
    /// "узкий шаг и широкий шаг": out to a wide stance (2.1) and back - the wider of the two jack
    ///   takes (feet 71 px apart against 45 for "чередует прыжок, ноги врозь, узкие").
    /// "прыжок на месте": the top of the jump (2.33), apex to apex 1.5 s.
    /// "узкие шаги": steps L 3.33, R 4.45 on a 2.25 s repeat, looped through a hold.
    /// "удары прямо LR": straight punches R 0.71, L 1.33 (repeat 0.71-2.12); both: L then R.
    /// "удары вверх": up L 0.79, R 1.4 (repeat 0.79-2.25); both: L 2.25 then R 2.72.
    /// "рисует круги руками": the left arm round, then the right; looped through a hold.
    /// "присяд сумо": the second squat's bottom (2.38); held = pulsing bottom to half up.
    /// "узодит ввлево с вытянутой ркуой": the side step left, arm out (0.85). Right = mirrored.
    /// "степы приколные с подтанцовкой": the groove for pauses.
    private static List<VideoSilhouetteSet.Clip> DefaultClips() => new List<VideoSilhouetteSet.Clip>
    {
        C("step", M("step", R, 1.0f, 1.67f, 2.4f)),
        C("march",
            M("march", R, 1.5f, 1.83f, 2.08f, 1.5f, 2.625f), M("march", L, 2.04f, 2.375f, 2.62f, 1.5f, 2.625f),
            M("marchHold", R, 1.5f, 1.83f, 2.08f), M("marchHold", L, 2.04f, 2.375f, 2.62f)),
        C("step_narrow_wide", M("jack", N, 1.55f, 2.1f, 2.8f)),
        C("jump", M("jump", N, 1.55f, 2.33f, 3.25f, 0.83f, 2.33f)),
        C("narrow_steps", M("rails", N, 2.5f, 3.33f, 4.0f, 3.33f, 5.58f, true)),
        C("punch_lr",
            M("punch", R, 0.42f, 0.71f, 1.0f, 0.71f, 2.12f), M("punch", L, 1.0f, 1.33f, 1.67f, 0.71f, 2.12f),
            M("punch2", N, 1.0f, 1.72f, 2.45f)),
        C("punch_up",
            M("punchHigh", L, 0.45f, 0.79f, 1.1f, 0.79f, 2.25f), M("punchHigh", R, 1.1f, 1.4f, 1.75f, 0.79f, 2.25f),
            M("punchHigh2", N, 2.0f, 2.48f, 3.0f)),
        C("arm_circles", M("circles", N, 0.4f, 1.2f, 3.9f, 0.55f, 3.65f, true)),
        C("sumo",
            M("sumo", N, 1.8f, 2.38f, 3.3f),
            M("sumoHold", N, 1.8f, 2.38f, 3.3f, 1.27f, 2.38f, true)),
        C("dodge", M("dodge", L, 0.2f, 0.85f, 1.7f)),
        C("groove", M("groove", N, GrooveStart, GrooveStart, GrooveEnd, GrooveStart, GrooveEnd)),
    };

    private const float GrooveStart = 3.33f, GrooveEnd = 5.58f;

    private static VideoSilhouetteSet.Clip C(string name, params VideoSilhouetteSet.Move[] moves) =>
        new VideoSilhouetteSet.Clip { name = name, moves = new List<VideoSilhouetteSet.Move>(moves) };

    [MenuItem("Tunnel/Video Silhouette/Reset Clips And Table")]
    public static void ResetClips()
    {
        var set = LoadSet();
        set.clips = DefaultClips();
        set.bindings = VideoSilhouetteSet.DefaultBindings();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
    }

    // --- baking ---

    [MenuItem("Tunnel/Video Silhouette/Bake Frames")]
    public static VideoSilhouetteSet Bake()
    {
        var set = LoadSet();
        set.frameShader = Shader.Find("Tunnel/SilhouetteFrames");
        if (!AssetDatabase.IsValidFolder(BakedFolder)) AssetDatabase.CreateFolder("Assets/Silhouette", "Baked");
        int height = set.frameHeight / 4 * 4;
        int width = Mathf.Max(4, Mathf.RoundToInt(height * set.panelAspect / 4f) * 4);
        var report = new StringBuilder("[VideoSilhouette] baked (clip: frames, seconds):\n");
        try
        {
            for (int ci = 0; ci < set.clips.Count; ci++)
            {
                var clip = set.clips[ci];
                string file = ClipFolder + "/" + clip.name + ".webm";
                if (!File.Exists(file)) { Debug.LogError("[VideoSilhouette] no clip file " + file); continue; }
                EditorUtility.DisplayProgressBar("Bake Frames", clip.name, ci / (float)set.clips.Count);
                var used = clip.UsedRange();
                float a = Mathf.Max(0f, used.x - 0.05f), b = used.y + 0.05f;
                string path = BakedFolder + "/" + clip.name + ".png";
                var args = new StringBuilder();
                args.Append(Quote(Path.GetFullPath(Script))).Append(' ').Append(Quote(Path.GetFullPath(file))).Append(' ').Append(Quote(Path.GetFullPath(path)));
                args.Append(Inv(" --width {0} --height {1} --zoom {2} --cx {3} --cy {4} --range {5} {6}", width, height, set.zoom, set.centerX, set.centerY, a, b));
                var result = Run(args.ToString());
                if (result == null) continue;
                var arr = ImportFlipbook(path, result);
                if (arr == null) continue;
                clip.frames = arr;
                clip.frameCount = result.frames;
                clip.fps = result.fps;
                clip.bakeStart = result.rangeStart;
                report.AppendLine(Inv("  {0}: {1} fr, {2:F2}-{3:F2} s", clip.name, result.frames, result.rangeStart, result.rangeEnd));
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log(report.ToString());
        return set;
    }

    /// The flipbook as a Texture2DArray: one slice a cell, compressed with its alpha, no mips.
    private static Texture2DArray ImportFlipbook(string path, BakeResult result)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { Debug.LogError("[VideoSilhouette] not a texture: " + path); return null; }
        imp.textureShape = TextureImporterShape.Texture2DArray;
        var ts = new TextureImporterSettings();
        imp.ReadTextureSettings(ts);
        ts.flipbookColumns = result.columns;
        ts.flipbookRows = result.rows;
        imp.SetTextureSettings(ts);
        imp.mipmapEnabled = false;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.alphaIsTransparency = true;
        imp.sRGBTexture = true;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        imp.maxTextureSize = 16384;
        imp.textureCompression = TextureImporterCompression.Compressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
    }

    // --- the check ---

    /// For every kind of note in the Video 9 charts: how many, and the move (clip, group, side
    /// handling) it gets; notes left without a move of their own; the dodge take's share against
    /// the dodge notes' share. Written to the console and Logs/VideoSilhouetteReport.txt.
    [MenuItem("Tunnel/Video Silhouette/Report Video9")]
    public static string Report()
    {
        var set = LoadSet();
        var playlist = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PlaylistPath);
        var files = new List<string>();
        if (playlist != null)
        {
            var segs = new SerializedObject(playlist).FindProperty("segments");
            for (int i = 0; i < segs.arraySize; i++)
            {
                var f = segs.GetArrayElementAtIndex(i).FindPropertyRelative("beatmapFileName").stringValue;
                if (!string.IsNullOrEmpty(f)) files.Add(f);
            }
        }
        var sb = new StringBuilder("[VideoSilhouette] Video9 report\n");
        var byNote = new SortedDictionary<string, int>();
        var moveOf = new Dictionary<string, string>();
        int total = 0, missing = 0, dodgeNotes = 0, dodgeTake = 0, connected = 0, clear = 0, cut = 0, shortBlend = 0;
        float segTime = 0f, dodgeTime = 0f, grooveTime = 0f, stanceTime = 0f;
        foreach (var file in files)
        {
            if (!BeatmapReader.TryLoad(file, LaneCount, out var meta, out var cues, out var error)) { sb.AppendLine("  " + file + ": " + error); continue; }
            var chart = new BeatmapSequencer(cues);
            float first = float.PositiveInfinity, last = float.NegativeInfinity;
            foreach (var c in cues) { first = Mathf.Min(first, c.time); last = Mathf.Max(last, c.time + c.duration); }
            var grids = new List<VideoSilhouettePlanner.Grid>
            {
                new VideoSilhouettePlanner.Grid { from = float.NegativeInfinity, to = float.PositiveInfinity, anchor = first, secondsPerBeat = 60f / (meta.bpm >= 20f ? meta.bpm : 120f) },
            };
            var segments = VideoSilhouettePlanner.Plan(set, chart, grids, LaneCount, out var actions);
            sb.AppendLine(Inv("  {0}: {1} notes, {2} moves, {3} segments, bpm {4:F1}", file, cues.Count, actions.Count, segments.Count, meta.bpm));
            foreach (var a in actions)
            {
                string key = a.note + (a.side != VideoSilhouetteSet.Side.None ? " " + a.side : "");
                byNote.TryGetValue(key, out int n);
                byNote[key] = n + 1;
                moveOf[key] = a.missing ? "NO MOVE (fallback " + a.clip.name + ")" : a.clip.name + " / " + a.move.group + " " + a.move.side + (a.mirror ? " mirrored" : "");
                total++;
                if (a.missing) missing++;
                if (a.connected) connected++;
                if (a.connected || a.t - a.take >= Mathf.Min(set.crossfadeSeconds, 0.06f) - 1e-3f) clear++;
                if (!a.connected && a.t - a.take < 0.15f - 1e-3f) shortBlend++;
                if (!a.connected && a.t - a.take < (a.move.peak - a.move.start) / set.speedMax - 1e-3f) cut++;
                if (a.note == VideoSilhouetteSet.Note.Dodge) dodgeNotes++;
                if (a.clip.name == "dodge") dodgeTake++;
            }
            // Time each segment is on screen, from its take-over to the next one's, within the chart.
            for (int i = 0; i < segments.Count; i++)
            {
                float a0 = Mathf.Max(first, segments[i].from), a1 = Mathf.Min(last, i + 1 < segments.Count ? segments[i + 1].from : last);
                if (a1 <= a0) continue;
                segTime += a1 - a0;
                if (segments[i].clip.name == "dodge") dodgeTime += a1 - a0;
                if (segments[i].label == "groove") grooveTime += a1 - a0;
                if (segments[i].label == "stance") stanceTime += a1 - a0;
            }
        }
        sb.AppendLine("  note (side): count -> clip / move");
        foreach (var kv in byNote) sb.AppendLine(Inv("    {0}: {1} -> {2}", kv.Key, kv.Value, moveOf[kv.Key]));
        sb.AppendLine(Inv("  notes without a move of their own: {0} of {1}; played on through a repeat: {2}", missing, total, connected));
        sb.AppendLine(Inv("  strike fully on screen at its note (blend over): {0:P1}; blend under 0.15 s (notes that close): {1:P1}; wind-up cut short (no room even at speedMax): {2:P1}",
            clear / Mathf.Max(1f, total), shortBlend / Mathf.Max(1f, total), cut / Mathf.Max(1f, total)));
        sb.AppendLine(Inv("  dodge take: {0} moves = {1:P1}; dodge notes: {2} = {3:P1}; dodge take on screen {4:P1} of the time",
            dodgeTake, total > 0 ? dodgeTake / (float)total : 0f, dodgeNotes, total > 0 ? dodgeNotes / (float)total : 0f, segTime > 0f ? dodgeTime / segTime : 0f));
        sb.AppendLine(Inv("  on screen: groove {0:P1}, stance {1:P1}, moves {2:P1}", grooveTime / Mathf.Max(1e-3f, segTime), stanceTime / Mathf.Max(1e-3f, segTime),
            1f - (grooveTime + stanceTime) / Mathf.Max(1e-3f, segTime)));
        string text = sb.ToString();
        File.WriteAllText("Logs/VideoSilhouetteReport.txt", text);
        Debug.Log(text);
        return text;
    }

    // --- helpers ---

    private static VideoSilhouetteSet LoadSet()
    {
        var set = AssetDatabase.LoadAssetAtPath<VideoSilhouetteSet>(SetPath);
        if (set != null) return set;
        set = ScriptableObject.CreateInstance<VideoSilhouetteSet>();
        set.clips = DefaultClips();
        set.bindings = VideoSilhouetteSet.DefaultBindings();
        AssetDatabase.CreateAsset(set, SetPath);
        return set;
    }

    private static BakeResult Run(string args)
    {
        var psi = new ProcessStartInfo("python", args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetFullPath("Tools/silhouette"),
        };
        using (var p = Process.Start(psi))
        {
            var errTask = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                Debug.LogError("[VideoSilhouette] bake failed: python " + args + "\n" + errTask.Result);
                return null;
            }
            var lines = output.Trim().Split('\n');
            return JsonUtility.FromJson<BakeResult>(lines[lines.Length - 1]);
        }
    }

    private static string Quote(string s) => "\"" + s + "\"";
    private static string Inv(string format, params object[] args) => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
}
