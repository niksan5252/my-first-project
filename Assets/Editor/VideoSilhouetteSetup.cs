using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

/// The video trainer (VideoSilhouette): imports the filmed clips in Assets/Silhouette/Video with
/// their alpha (no transcode - VP8 keeps it as it is), fills VideoSilhouetteSet with every clip and
/// a first guess of its strike, and turns the video trainer on in the tunnel scene in place of the
/// 3D one. Idempotent: a clip already in the set keeps its hand-tuned times.
public static class VideoSilhouetteSetup
{
    private const string VideoFolder = "Assets/Silhouette/Video";
    public const string SetPath = "Assets/Silhouette/VideoSilhouetteSet.asset";
    private const string ScenePath = "Assets/Scenes/TunnelScene.unity";
    // The clips of several repeats (~10 s); the fragment taken is one repeat around the middle.
    private const float LongClip = 3f;
    private const float RepeatSeconds = 1.6f;

    [MenuItem("Tunnel/Video Silhouette/Setup")]
    public static void Setup()
    {
        foreach (var path in Directory.GetFiles(VideoFolder, "*.webm"))
        {
            var p = path.Replace('\\', '/');
            if (AssetImporter.GetAtPath(p) is VideoClipImporter imp)
            {
                var ts = imp.defaultTargetSettings;
                if (ts.enableTranscoding)
                {
                    ts.enableTranscoding = false;
                    imp.defaultTargetSettings = ts;
                    imp.SaveAndReimport();
                }
            }
        }

        var set = AssetDatabase.LoadAssetAtPath<VideoSilhouetteSet>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<VideoSilhouetteSet>();
            set.bindings = VideoSilhouetteSet.DefaultBindings();
            AssetDatabase.CreateAsset(set, SetPath);
        }
        foreach (var guid in AssetDatabase.FindAssets("t:VideoClip", new[] { VideoFolder }))
        {
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip == null || set.clips.Exists(c => c.name == clip.name)) continue;
            float len = (float)clip.length;
            float mid = len * 0.5f;
            bool longClip = len > LongClip;
            set.clips.Add(new VideoSilhouetteSet.Clip
            {
                name = clip.name,
                clip = clip,
                hitTime = mid,
                start = longClip ? Mathf.Max(0f, mid - RepeatSeconds * 0.5f) : 0f,
                end = longClip ? Mathf.Min(len, mid + RepeatSeconds * 0.5f) : len,
                // The short single repeats are fitted to the note; the long ones play at 1x.
                fitSpeed = !longClip,
            });
        }
        set.clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

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

        var names = new List<string>();
        foreach (var c in set.clips) names.Add(c.name + " " + c.hitTime.ToString("F2") + " [" + c.start.ToString("F2") + ".." + c.end.ToString("F2") + "]");
        Debug.Log("[VideoSilhouette] set: " + string.Join(", ", names));
    }
}
