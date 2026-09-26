using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Builds (or refreshes) the X Bot's SilhouetteMotionSet from the Mixamo folder: the relaxed
/// stance, the jump and the crouch, with each clip's impact frame measured by
/// SilhouetteClipAnalysis. Punches, kicks, holds and dodges are posed procedurally by the trainer
/// (SilhouettePoseJob), so their clips are not in the set - the files stay in the project.
///
/// Left out on purpose: Rifle Jump (the pose holds a rifle), the fighting idles and "Idle" (a low
/// boxing stance), the dances, and
/// the MMA/boxing takes - the trainer is a relaxed warm-up instructor, not a fighter.
public static class SilhouetteMotionSetBuilder
{
    public const string AssetPath = SilhouetteMixamoImport.Root + "/XBotMotionSet.asset";

    private static readonly string[] Curb = { "Jump", "Jumping Up" };
    // The relaxed stance is a still pose: the last frame of the take that ends standing easy. The
    // "Idle" take itself is a low boxing stance, and so are the fight idles.
    private const string Stance = "Fight Idle To Standing Idle";

    [MenuItem("Tunnel/Silhouette/Build Motion Set", priority = 42)]
    public static void Build()
    {
        var results = SilhouetteClipAnalysis.AnalyzeAll(out string table);
        Debug.Log(table);
        var byName = new Dictionary<string, SilhouetteClipAnalysis.Result>();
        foreach (var r in results) byName[r.clip.name] = r;

        var set = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(AssetPath);
        bool created = set == null;
        if (created) set = ScriptableObject.CreateInstance<SilhouetteMotionSet>();

        set.model = AssetDatabase.LoadAssetAtPath<GameObject>(SilhouetteMixamoImport.SkinFile);
        set.avatar = AssetDatabase.LoadAllAssetsAtPath(SilhouetteMixamoImport.SkinFile).OfType<Avatar>().FirstOrDefault();

        List<SilhouetteMotion> List(string[] names) => names.Select(n => Motion(byName, n)).Where(m => m != null).ToList();
        set.idle = List(new[] { Stance });
        foreach (var m in set.idle) m.holdFrame = m.Length;
        set.curb = List(Curb);
        set.duckDown = Motion(byName, "Standing To Crouched") ?? new SilhouetteMotion();
        set.duckUp = Motion(byName, "Crouched To Standing") ?? new SilhouetteMotion();

        // A take that stops at the top of its jump lands by playing its take-off backwards.
        foreach (var m in set.curb)
            if (m.impact >= m.Length - 0.1f) m.reverseAfterImpact = true;

        if (created) AssetDatabase.CreateAsset(set, AssetPath);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log("[Silhouette] motion set written to " + AssetPath);
    }

    private static SilhouetteMotion Motion(Dictionary<string, SilhouetteClipAnalysis.Result> byName, string name)
    {
        if (!byName.TryGetValue(name, out var r))
        {
            Debug.LogWarning("[Silhouette] clip not found: " + name);
            return null;
        }
        return new SilhouetteMotion { clip = r.clip, impact = r.impact, side = r.side };
    }
}
