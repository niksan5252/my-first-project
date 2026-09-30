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
    // A boxer's bounce on the spot, from fitness/: one more lively stance between the sets.
    private static readonly string[] LivelyFitness = { "Boxing Warm Up" };

    // Every fitness take (Assets/SharedAssets/Mixamo/fitness) and the part it plays - the whole
    // exercise-to-take mapping. Each is used together with its mirrored twin.
    //   punches: Punch notes;  uppercuts: PunchHigh and PunchHold;  steps: runs of kicks;
    //   jacks: jack runs, Cross (held on the open landing);  hops: side jumps, pair shifts, chords;
    //   sideSteps/dodges: walls and CurbGap;  squat: Duck;  curb: Curb.
    private static readonly (string clip, SilhouetteFitnessAnalysis.Role role)[] Fitness =
    {
        ("Lead Jab Short Head", SilhouetteFitnessAnalysis.Role.Punch),
        ("Cross Punch", SilhouetteFitnessAnalysis.Role.Punch),
        ("Hook Punch", SilhouetteFitnessAnalysis.Role.Punch),
        ("Uppercut Left", SilhouetteFitnessAnalysis.Role.Uppercut),
        ("Uppercut Lead Hand", SilhouetteFitnessAnalysis.Role.Uppercut),
        ("Step Touch Dance", SilhouetteFitnessAnalysis.Role.Steps),
        ("Kick Step", SilhouetteFitnessAnalysis.Role.Steps),
        ("Running Man", SilhouetteFitnessAnalysis.Role.Steps),
        ("Jumping Jacks", SilhouetteFitnessAnalysis.Role.Jack),
        ("Jump In Place A", SilhouetteFitnessAnalysis.Role.Hop),
        ("Jump In Place B", SilhouetteFitnessAnalysis.Role.Hop),
        ("Standing Jump", SilhouetteFitnessAnalysis.Role.Jump),
        ("Air Squat", SilhouetteFitnessAnalysis.Role.Squat),
        ("Side Step Short Right", SilhouetteFitnessAnalysis.Role.SideStep),
        ("Side Step Short Left", SilhouetteFitnessAnalysis.Role.SideStep),
        ("Dodging Right", SilhouetteFitnessAnalysis.Role.Dodge),
        ("Boxing Warm Up", SilhouetteFitnessAnalysis.Role.Idle),
    };

    // Third preset: PolyOne's Cartoon Skeleton, for the Halloween playlist. Its FBX imports as a
    // valid Humanoid (26 bones mapped automatically), so the same Mixamo takes and IK gestures
    // retarget onto it; the prefab is used for its textured material.
    public const string SkeletonModel = "Assets/PolyOne/Cartoon Skeleton/Prefabs/Free Pack - Cartoon Skeleton (Rigged).prefab";
    public const string SkeletonAssetPath = SilhouetteMixamoImport.Root + "/CartoonSkeletonMotionSet.asset";

    [MenuItem("Tunnel/Silhouette/Build Motion Set (Cartoon Skeleton)", priority = 44)]
    public static void BuildSkeleton() => Build(SkeletonModel, SkeletonAssetPath, JenniferAssetPath);

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
        if (set.avatar == null && set.model != null && set.model.TryGetComponent<Animator>(out var animator)) set.avatar = animator.avatar;

        List<SilhouetteMotion> List(string[] names) => names.Select(n => Motion(byName, n)).Where(m => m != null).ToList();
        set.idle = List(Calm);
        set.idleLively = List(Lively);
        set.curb = List(Curb);
        set.duckDown = Motion(byName, "Standing To Crouched") ?? new SilhouetteMotion();
        set.duckUp = Motion(byName, "Crouched To Standing") ?? new SilhouetteMotion();
        BuildExercises(set);

        // A take that stops at the top of its jump lands by playing its take-off backwards.
        foreach (var m in set.curb)
            if (m.impact >= m.Length - 0.1f) m.reverseAfterImpact = true;

        if (created) AssetDatabase.CreateAsset(set, assetPath);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log("[Silhouette] motion set written to " + assetPath);
    }

    /// The fitness takes, each measured (SilhouetteFitnessAnalysis) together with its mirror.
    private static void BuildExercises(SilhouetteMotionSet set)
    {
        string dir = SilhouetteMixamoImport.Root + "/" + SilhouetteMixamoImport.FitnessFolder + "/";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(SilhouetteMixamoImport.SkinFile);
        var go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        var a = go.GetComponent<Animator>();
        a.applyRootMotion = false;
        var log = new System.Text.StringBuilder("[Silhouette] fitness takes").AppendLine();
        foreach (var l in new[] { set.punches, set.uppercuts, set.steps, set.jacks, set.hops, set.sideSteps, set.dodges }) l.Clear();
        set.curb.Clear();
        try
        {
            set.analysisHipsHeight = a.humanScale;
            foreach (var (name, role) in Fitness)
            {
                var clips = AssetDatabase.LoadAllAssetsAtPath(dir + "X Bot@" + name + ".fbx").OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).ToList();
                var original = clips.FirstOrDefault(c => c.name == name);
                if (original == null) { Debug.LogWarning("[Silhouette] fitness take not found: " + name); continue; }
                foreach (var clip in clips.OrderBy(c => c.name))
                {
                    var m = SilhouetteFitnessAnalysis.Analyze(go, a, clip, original, role, log);
                    switch (role)
                    {
                        case SilhouetteFitnessAnalysis.Role.Punch: set.punches.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Uppercut: set.uppercuts.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Steps: set.steps.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Jack: set.jacks.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Hop: set.hops.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Jump: if (clip == original) set.curb.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Squat: if (clip == original) set.squat = m; break;
                        case SilhouetteFitnessAnalysis.Role.SideStep: set.sideSteps.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Dodge: set.dodges.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Idle:
                            if (clip == original && LivelyFitness.Contains(name)) set.idleLively.Add(new SilhouetteMotion { clip = clip });
                            break;
                    }
                }
            }
        }
        finally { Object.DestroyImmediate(go); }
        Debug.Log(log.ToString());
        System.IO.Directory.CreateDirectory("Screenshots");
        System.IO.File.WriteAllText("Screenshots/trainer_v6_takes.txt", log.ToString());
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
