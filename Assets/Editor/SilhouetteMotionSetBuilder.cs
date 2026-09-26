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
    // Second preset: the same clips and gestures on Jennifer, retargeted through the humanoid.
    // Created as a copy of the X Bot set, so gesture tuning carries over; the model differs.
    public const string JenniferModel = SilhouetteMixamoImport.CharactersFolder + "Jennifer.fbx";
    public const string JenniferAssetPath = SilhouetteMixamoImport.Root + "/JenniferMotionSet.asset";

    // The straight jump up, facing the viewer, landing on its own. "Jumping Up" is left out: it
    // turns half sideways in its crouch and takes off spread-eagled, which reads as flying away
    // rather than hopping a kerb (checked by eye in the panel, not by the names).
    private static readonly string[] Curb = { "Jump" };
    // The stance is motion capture: calm idles for the short rests, lively ones for the intro,
    // breakdowns and finale. ("Idle" and the fight idles are low boxing stances, left out.)
    private static readonly string[] Calm = { "Fitness Idle With Breathing", "Breathing Idle", "Weight Shift Idle" };
    private static readonly string[] Lively = { "Happy Idle 1", "Happy Idle 2" };

    [MenuItem("Tunnel/Silhouette/Build Motion Set (X Bot)", priority = 42)]
    public static void Build() => Build(SilhouetteMixamoImport.SkinFile, AssetPath, null);

    [MenuItem("Tunnel/Silhouette/Build Motion Set (Jennifer)", priority = 43)]
    public static void BuildJennifer() => Build(JenniferModel, JenniferAssetPath, AssetPath);

    /// Writes the set at `assetPath` for the model at `modelPath`. A new set starts as a copy of
    /// `templatePath` when there is one, so its gesture and timing settings match.
    public static void Build(string modelPath, string assetPath, string templatePath)
    {
        var results = SilhouetteClipAnalysis.AnalyzeAll(out string table);
        Debug.Log(table);
        var byName = new Dictionary<string, SilhouetteClipAnalysis.Result>();
        foreach (var r in results) byName[r.clip.name] = r;

        var set = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(assetPath);
        bool created = set == null;
        if (created)
        {
            var template = templatePath != null ? AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(templatePath) : null;
            set = template != null ? Object.Instantiate(template) : ScriptableObject.CreateInstance<SilhouetteMotionSet>();
        }

        set.model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        set.avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();

        List<SilhouetteMotion> List(string[] names) => names.Select(n => Motion(byName, n)).Where(m => m != null).ToList();
        set.idle = List(Calm);
        set.idleLively = List(Lively);
        set.curb = List(Curb);
        set.duckDown = Motion(byName, "Standing To Crouched") ?? new SilhouetteMotion();
        set.duckUp = Motion(byName, "Crouched To Standing") ?? new SilhouetteMotion();

        // A take that stops at the top of its jump lands by playing its take-off backwards.
        foreach (var m in set.curb)
            if (m.impact >= m.Length - 0.1f) m.reverseAfterImpact = true;

        if (created) AssetDatabase.CreateAsset(set, assetPath);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log("[Silhouette] motion set written to " + assetPath);
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
