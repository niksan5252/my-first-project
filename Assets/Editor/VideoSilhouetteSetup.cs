using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// The video trainer (VideoSilhouette). Bake Frames turns every clip in Assets/Silhouette/Video
/// (a .webm with alpha; green-screen takes go through Tools/silhouette/key_green.py first) into one
/// loop of its movement - a flipbook PNG in Assets/Silhouette/Baked, imported as a Texture2DArray - with
/// Tools/silhouette/bake_frames.py, which finds the loop and the strike; the clip's row in
/// VideoSilhouetteSet is added or refreshed (its hand-set loop kept when manualCycle is on). A new
/// clip is the file plus a row in the set's note table. Setup bakes and turns the video trainer on
/// in the tunnel scene in place of the 3D one.
public static class VideoSilhouetteSetup
{
    private const string VideoFolder = "Assets/Silhouette/Video";
    private const string BakedFolder = "Assets/Silhouette/Baked";
    public const string SetPath = "Assets/Silhouette/VideoSilhouetteSet.asset";
    private const string ScenePath = "Assets/Scenes/TunnelScene.unity";
    private const string Script = "Tools/silhouette/bake_frames.py";

    [System.Serializable]
    private class BakeResult
    {
        public float fps, cycleStart, cycleEnd, peakTime, amplitude, seam;
        public int frames, columns, rows, width, height, peakFrame, strikes, sourceFrames;
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

    [MenuItem("Tunnel/Video Silhouette/Reset Note Table")]
    public static void ResetTable()
    {
        var set = LoadSet();
        set.bindings = VideoSilhouetteSet.DefaultBindings();
        set.excluded = VideoSilhouetteSet.DefaultExcluded();
        set.fallbackClip = "28_step_touch";
        set.restClip = "30_idle";
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tunnel/Video Silhouette/Bake Frames")]
    public static VideoSilhouetteSet Bake()
    {
        var set = LoadSet();
        set.frameShader = Shader.Find("Tunnel/SilhouetteFrames");
        if (!AssetDatabase.IsValidFolder(BakedFolder)) AssetDatabase.CreateFolder("Assets/Silhouette", "Baked");
        int height = set.frameHeight / 4 * 4;
        int width = Mathf.Max(4, Mathf.RoundToInt(height * set.panelAspect / 4f) * 4);

        var files = new List<string>(Directory.GetFiles(VideoFolder, "*.webm"));
        files.Sort(System.StringComparer.Ordinal);
        var names = new HashSet<string>();
        var report = new StringBuilder("[VideoSilhouette] baked (name: frames, loop s, strike frame, strikes, amplitude, seam):\n");
        try
        {
            for (int fi = 0; fi < files.Count; fi++)
            {
                string name = Path.GetFileNameWithoutExtension(files[fi]);
                if (set.excluded.Contains(name)) continue;
                names.Add(name);
                EditorUtility.DisplayProgressBar("Bake Frames", name, fi / (float)files.Count);
                var row = set.clips.Find(c => c.name == name);
                if (row == null) set.clips.Add(row = new VideoSilhouetteSet.Clip { name = name });

                var args = new StringBuilder();
                string path = BakedFolder + "/" + name + ".png";
                args.Append(Quote(Path.GetFullPath(Script))).Append(' ').Append(Quote(Path.GetFullPath(files[fi]))).Append(' ').Append(Quote(Path.GetFullPath(path)));
                args.Append(Inv(" --width {0} --height {1} --zoom {2} --cx {3} --cy {4}", width, height, set.zoom, set.centerX, set.centerY));
                if (row.manualCycle)
                    args.Append(Inv(" --cycle {0} {1} --peak {2} --strikes {3}", row.cycleStart, row.cycleEnd, row.peakTime, Mathf.Max(1, row.strikes)));
                if (row.mirror) args.Append(" --mirror");
                var result = Run(args.ToString());
                if (result == null) continue;

                var arr = ImportFlipbook(path, result);
                if (arr == null) continue;

                row.frames = arr;
                row.frameCount = result.frames;
                row.fps = result.fps;
                row.peakFrame = result.peakFrame;
                if (!row.manualCycle)
                {
                    row.cycleStart = result.cycleStart;
                    row.cycleEnd = result.cycleEnd;
                    row.peakTime = result.peakTime;
                    row.strikes = result.strikes;
                }
                report.AppendLine(Inv("  {0}: {1} fr, {2:F2}-{3:F2}, strike {4}, x{5}, amp {6:F3}, seam {7:F3}",
                    name, result.frames, result.cycleStart, result.cycleEnd, result.peakFrame, row.strikes, result.amplitude, result.seam));
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        // Rows of clips left out or gone.
        set.clips.RemoveAll(c => !names.Contains(c.name));
        set.clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
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

    private static VideoSilhouetteSet LoadSet()
    {
        var set = AssetDatabase.LoadAssetAtPath<VideoSilhouetteSet>(SetPath);
        if (set != null) return set;
        set = ScriptableObject.CreateInstance<VideoSilhouetteSet>();
        set.bindings = VideoSilhouetteSet.DefaultBindings();
        set.excluded = VideoSilhouetteSet.DefaultExcluded();
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
