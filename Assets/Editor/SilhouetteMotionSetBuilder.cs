using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Builds (or refreshes) the X Bot's SilhouetteMotionSet from the Mixamo folder: which clip goes
/// to which kind of note, with each clip's impact frame measured by SilhouetteClipAnalysis. Run
/// after the clips change; the asset is ordinary data afterwards and can be edited by hand.
///
/// Left out on purpose: Rifle Jump (the pose holds a rifle), and the standing Idle and Fight Idle
/// To Standing Idle - the trainer stays in its fighting stance between notes.
public static class SilhouetteMotionSetBuilder
{
    public const string AssetPath = SilhouetteMixamoImport.Root + "/XBotMotionSet.asset";

    private static readonly string[] Punch = { "Jab Cross", "Jab Cross (1)", "Lead Jab", "Lead Jab (1)", "Punching" };
    private static readonly string[] Kick = { "Kick Front Snap Lead", "Kick Mma Low", "Kick Mma Medium", "Kick Thrust Lead", "Kick Knee Lead" };
    private static readonly string[] PunchHold = { "Speedbag" };
    private static readonly string[] KickHold = { "Stomping", "Stepping Left", "Walking", "Catwalk Walk", "Running Up Stairs" };
    private static readonly string[] Curb = { "Jump", "Jumping Up" };
    private static readonly string[] Idle = { "Fight Idle", "Fighting Idle" };
    private static readonly string[] Dance = { "Booty Hip Hop Dance", "Chicken Dance", "House Dancing", "Step Hip Hop Dance", "Swing Dancing" };

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
        set.punch = List(Punch);
        set.kick = List(Kick);
        set.punchHold = List(PunchHold);
        set.kickHold = List(KickHold);
        set.curb = List(Curb);
        set.dodgeLeft = List(new[] { "Long Left Side Step" });
        set.dodgeRight = List(new[] { "Long Right Side Step" });
        set.duckDown = Motion(byName, "Standing To Crouched") ?? new SilhouetteMotion();
        set.duckUp = Motion(byName, "Crouched To Standing") ?? new SilhouetteMotion();
        set.idle = List(Idle);
        set.dance = List(Dance);

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
