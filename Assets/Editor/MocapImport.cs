using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Video-and-studio mocap -> the trainer (video 10). Tools/mocap/*.csv hold 33 MediaPipe-style
/// landmarks a frame (from our green-screen clips through MediaPipe, and from the CMU studio
/// captures through Desktop/силует/cmu/bvh_to_json.py; a "(Mirror)" twin of each). Every frame is
/// retargeted onto Jennifer by aiming her bones along the landmark segments (hips and chest from
/// the hip and shoulder lines, then upper arm -> forearm -> hand, thigh -> shin -> foot, neck/head),
/// read back as a HumanPose and written as muscle curves - a Humanoid clip like any Mixamo take.
/// The clips are then measured by SilhouetteFitnessAnalysis exactly as the Mixamo takes were, and
/// filed into a copy of Video 9's motion set (mocap only, groove, ~0.2 s crossfades), so the
/// trainer's note logic - a move per note, its strike on the note - is unchanged.
///
/// CSV: line 1 "fps,frames,hipHeight"; then a line a frame: root xyz + 33 x xyz, metres, hip-centred.
/// Axes X = viewer right, Y up, Z away from the camera; the performer faces the camera.
public static class MocapImport
{
    private const string Source = "Tools/mocap";
    private const string ClipFolder = "Assets/Trainer/Mocap";
    public const string SetPath = "Assets/Trainer/Mocap/MocapMotionSet.asset";
    private const string Template = "Assets/SharedAssets/Mixamo/JenniferFilmedMotionSet.asset";
    private const float OutFps = 30f;

    // name -> role. Each also gets its (Mirror). Chosen by eye on the previews (Desktop/силует/mocap/preview).
    private static readonly (string name, SilhouetteFitnessAnalysis.Role role)[] Takes =
    {
        ("Punch", SilhouetteFitnessAnalysis.Role.Punch),         // straight punches L/R, from our clip
        ("Box4", SilhouetteFitnessAnalysis.Role.Punch),          // CMU boxing, jab/cross upright, facing the viewer
        ("PunchUp", SilhouetteFitnessAnalysis.Role.Uppercut),    // CMU punches up
        ("PunchHigh", SilhouetteFitnessAnalysis.Role.Uppercut),  // our clip: punches up
        ("Kick", SilhouetteFitnessAnalysis.Role.Steps),          // step-tap
        ("March", SilhouetteFitnessAnalysis.Role.Steps),
        ("HighKnees", SilhouetteFitnessAnalysis.Role.Steps),     // CMU, knees up on the spot
        ("Star", SilhouetteFitnessAnalysis.Role.Jack),           // CMU jumping jacks - "the rays"
        ("Star2", SilhouetteFitnessAnalysis.Role.Jack),
        ("CurbSplit", SilhouetteFitnessAnalysis.Role.Jack),      // our clip: feet apart / together
        ("JumpCMU", SilhouetteFitnessAnalysis.Role.Hop),
        ("Jump", SilhouetteFitnessAnalysis.Role.Hop),
        ("NarrowWide", SilhouetteFitnessAnalysis.Role.Hop),
        ("JumpCMU", SilhouetteFitnessAnalysis.Role.Jump),        // the kerb: the straight jump
        ("SquatDeep", SilhouetteFitnessAnalysis.Role.Squat),
        ("SideStep", SilhouetteFitnessAnalysis.Role.SideStep),
        ("Dodge", SilhouetteFitnessAnalysis.Role.Dodge),
        ("StepGroove", SilhouetteFitnessAnalysis.Role.Idle),     // lively stance between sets
    };

    // MediaPipe landmark indices
    private const int Nose = 0, LEar = 7, REar = 8, LSh = 11, RSh = 12, LEl = 13, REl = 14, LWr = 15, RWr = 16,
        LPinky = 17, RPinky = 18, LIndex = 19, RIndex = 20, LHip = 23, RHip = 24, LKnee = 25, RKnee = 26,
        LAnk = 27, RAnk = 28, LHeel = 29, RHeel = 30, LToe = 31, RToe = 32;

    private class Take { public float fps, hipHeight; public List<Vector3> root = new List<Vector3>(); public List<Vector3[]> p = new List<Vector3[]>(); }

    [MenuItem("Tunnel/Mocap/Import All + Build Motion Set", priority = 60)]
    public static void ImportAll()
    {
        Directory.CreateDirectory(ClipFolder);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(SilhouetteMotionSetBuilder.JenniferModel);
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var name in Takes.SelectMany(t => Reps(t.name, t.role)).Distinct())
            foreach (var n in new[] { name, name + " (Mirror)" })
            {
                var take = Load(Path.Combine(Source, n + ".csv"));
                if (take == null) { Debug.LogWarning("[Mocap] missing " + n); continue; }
                clips[n] = Retarget(model, take, n);
            }
        AssetDatabase.SaveAssets();
        BuildSet(clips);
    }

    // A single-strike role (punch, jump, squat, dodge...) uses the take cut into single reps around
    // each peak ("Punch#1"...), as the Mixamo takes were one move each; steps, jacks and the groove
    // keep the whole take (their marks are every step/landing in it).
    private static IEnumerable<string> Reps(string name, SilhouetteFitnessAnalysis.Role role)
    {
        if (role == SilhouetteFitnessAnalysis.Role.Steps || role == SilhouetteFitnessAnalysis.Role.Jack || role == SilhouetteFitnessAnalysis.Role.Idle)
            return new[] { name };
        var reps = Enumerable.Range(1, 12).Select(i => name + "#" + i).Where(n => File.Exists(Path.Combine(Source, n + ".csv"))).ToList();
        return reps.Count > 0 ? reps : new List<string> { name };
    }

    private static Take Load(string path)
    {
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        var head = lines[0].Split(',');
        var t = new Take { fps = F(head[0]), hipHeight = F(head[2]) };
        for (int i = 1; i < lines.Length; i++)
        {
            var v = lines[i].Split(',');
            if (v.Length < 102) continue;
            // to model space: the performer faces the camera, the model faces +Z -> turn 180 about Y
            Vector3 M(int k) => new Vector3(-F(v[k]), F(v[k + 1]), -F(v[k + 2]));
            t.root.Add(M(0));
            var p = new Vector3[33];
            for (int j = 0; j < 33; j++) p[j] = M(3 + j * 3);
            t.p.Add(p);
        }
        return t;
    }

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static AnimationClip Retarget(GameObject model, Take take, string name)
    {
        var go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var anim = go.GetComponent<Animator>();
        try
        {
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var bones = (HumanBodyBones[])System.Enum.GetValues(typeof(HumanBodyBones));
            var rest = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (var b in bones)
                if (b != HumanBodyBones.LastBone && B(b) != null) rest[b] = B(b).rotation;
            Vector3 restHips = B(HumanBodyBones.Hips).position;
            float modelLeg = Vector3.Distance(B(HumanBodyBones.LeftUpperLeg).position, B(HumanBodyBones.LeftLowerLeg).position)
                           + Vector3.Distance(B(HumanBodyBones.LeftLowerLeg).position, B(HumanBodyBones.LeftFoot).position);
            var p0 = take.p[0];
            float dataLeg = Vector3.Distance(p0[LHip], p0[LKnee]) + Vector3.Distance(p0[LKnee], p0[LAnk]);
            float scale = dataLeg > 0.1f ? modelLeg / dataLeg : 1f;

            var handler = new HumanPoseHandler(anim.avatar, go.transform);
            var pose = new HumanPose();
            int n = take.p.Count;
            int outN = Mathf.Max(2, Mathf.RoundToInt((n - 1) / take.fps * OutFps) + 1);
            var muscles = new AnimationCurve[HumanTrait.MuscleCount];
            for (int i = 0; i < muscles.Length; i++) muscles[i] = new AnimationCurve();
            var rootT = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var rootQ = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

            for (int f = 0; f < outN; f++)
            {
                float time = f / OutFps;
                float src = Mathf.Min(n - 1, time * take.fps);
                int a = Mathf.FloorToInt(src), b2 = Mathf.Min(n - 1, a + 1);
                float w = src - a;
                var P = new Vector3[33];
                for (int k = 0; k < 33; k++) P[k] = Vector3.Lerp(take.p[a][k], take.p[b2][k], w);
                Vector3 root = Vector3.Lerp(take.root[a], take.root[b2], w);

                foreach (var kv in rest) B(kv.Key).rotation = kv.Value;
                // Hips: the hip line and the trunk's up.
                Vector3 hipMid = (P[LHip] + P[RHip]) * 0.5f, shMid = (P[LSh] + P[RSh]) * 0.5f;
                Vector3 right = (P[RHip] - P[LHip]).normalized;
                Vector3 up = (shMid - hipMid).normalized;
                Quaternion hipsRot = Quaternion.LookRotation(Vector3.Cross(right, up), up);
                Vector3 shRight = (P[RSh] - P[LSh]).normalized;
                Quaternion chestRot = Quaternion.LookRotation(Vector3.Cross(shRight, up), up);
                B(HumanBodyBones.Hips).rotation = hipsRot * rest[HumanBodyBones.Hips];
                B(HumanBodyBones.Hips).position = restHips + new Vector3(root.x, root.y, 0f) * scale;
                SetRot(B(HumanBodyBones.Spine), rest, HumanBodyBones.Spine, Quaternion.Slerp(hipsRot, chestRot, 0.5f));
                if (B(HumanBodyBones.Chest) != null) SetRot(B(HumanBodyBones.Chest), rest, HumanBodyBones.Chest, chestRot);
                if (B(HumanBodyBones.UpperChest) != null) SetRot(B(HumanBodyBones.UpperChest), rest, HumanBodyBones.UpperChest, chestRot);

                // Limbs, parent first: aim each bone at its child's landmark.
                Aim(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), P[LEl] - P[LSh]);
                Aim(B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), P[LWr] - P[LEl]);
                Aim(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), P[REl] - P[RSh]);
                Aim(B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), P[RWr] - P[REl]);
                // Hands follow the forearm: the finger landmarks are too noisy to aim a wrist by - they
                // made the fists flick from frame to frame.
                Aim(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), P[LKnee] - P[LHip]);
                Aim(B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), P[LAnk] - P[LKnee]);
                Aim(B(HumanBodyBones.LeftFoot), B(HumanBodyBones.LeftToes), P[LToe] - P[LAnk]);
                Aim(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), P[RKnee] - P[RHip]);
                Aim(B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), P[RAnk] - P[RKnee]);
                Aim(B(HumanBodyBones.RightFoot), B(HumanBodyBones.RightToes), P[RToe] - P[RAnk]);
                // Head: from the shoulders' middle toward the ears' middle, half on the neck.
                Vector3 earMid = (P[LEar] + P[REar]) * 0.5f;
                Vector3 headDir = earMid - shMid;
                if (B(HumanBodyBones.Neck) != null) AimPartial(B(HumanBodyBones.Neck), B(HumanBodyBones.Head), headDir, 0.5f);
                Vector3 face = P[Nose] - earMid; face.y *= 0.5f;
                Vector3 fwd = chestRot * Vector3.forward;
                if (face.sqrMagnitude > 1e-6f) fwd = Vector3.Slerp(fwd, face.normalized, 0.6f);
                Vector3 headUp = headDir.sqrMagnitude > 1e-6f ? headDir.normalized : up;
                B(HumanBodyBones.Head).rotation = Quaternion.LookRotation(fwd, headUp) * rest[HumanBodyBones.Head];

                handler.GetHumanPose(ref pose);
                for (int i = 0; i < muscles.Length; i++) muscles[i].AddKey(time, pose.muscles[i]);
                rootT[0].AddKey(time, pose.bodyPosition.x); rootT[1].AddKey(time, pose.bodyPosition.y); rootT[2].AddKey(time, pose.bodyPosition.z);
                rootQ[0].AddKey(time, pose.bodyRotation.x); rootQ[1].AddKey(time, pose.bodyRotation.y);
                rootQ[2].AddKey(time, pose.bodyRotation.z); rootQ[3].AddKey(time, pose.bodyRotation.w);
            }
            handler.Dispose();
            // The capture's frame-to-frame noise, smoothed out of every curve (a two-pass 5-tap
            // Gaussian, ~0.1 s): a strike's peak stays where it was, the shake between goes.
            foreach (var c in muscles) Smooth(c);
            foreach (var c in rootT) Smooth(c);
            foreach (var c in rootQ) Smooth(c);

            string path = ClipFolder + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves();
            clip.frameRate = OutFps;
            for (int i = 0; i < muscles.Length; i++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), MuscleAttr(HumanTrait.MuscleName[i])), muscles[i]);
            string[] t = { "RootT.x", "RootT.y", "RootT.z" }, q = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int i = 0; i < 3; i++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), t[i]), rootT[i]);
            for (int i = 0; i < 4; i++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), q[i]), rootQ[i]);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            // The rep loops (steps, marches, jacks, the groove) are walked mark by mark past their end -
            // they must loop, or the figure freezes on the last frame.
            settings.loopTime = !name.Contains("#");
            // Bake the root into the pose with the original height and place, as the Mixamo takes are
            // imported: the trainer plays without root motion, and an un-baked clip would hand its
            // height to the (unused) root and sink the figure.
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }
        finally { Object.DestroyImmediate(go); }
    }

    /// A step is ON the note when the foot comes down, not when the knee is highest (the analysis's
    /// mark): each step mark moves to that foot's next lowest point within 0.6 s - the landing.
    private static void LandMarks(GameObject go, Animator a, SilhouetteMotion m, System.Text.StringBuilder log)
    {
        const float fps = 60f;
        int frames = Mathf.Max(1, Mathf.RoundToInt(m.Length * fps));
        var footY = new float[2, frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            m.clip.SampleAnimation(go, Mathf.Min(m.Length, f / fps));
            footY[0, f] = go.transform.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.LeftFoot).position).y;
            footY[1, f] = go.transform.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.RightFoot).position).y;
        }
        for (int i = 0; i < m.marks.Length; i++)
        {
            int foot = i < m.markLimbs.Length ? Mathf.Clamp(m.markLimbs[i], 0, 1) : 0;
            int f0 = Mathf.RoundToInt(m.marks[i] * fps), best = f0;
            for (int f = f0; f <= Mathf.Min(frames, f0 + Mathf.RoundToInt(0.6f * fps)); f++)
                if (footY[foot, f] < footY[foot, best] - 1e-4f) best = f;
            m.marks[i] = best / fps;
        }
        log.AppendLine("  steps on the landing: " + string.Join(" ", m.marks.Select(t => t.ToString("F2"))));
    }

    private static void Smooth(AnimationCurve c)
    {
        var k = c.keys;
        if (k.Length < 5) return;
        for (int pass = 0; pass < 2; pass++)
        {
            var v = k.Select(x => x.value).ToArray();
            for (int i = 0; i < k.Length; i++)
            {
                float V(int j) => v[Mathf.Clamp(j, 0, v.Length - 1)];
                k[i].value = (V(i - 2) + 4f * V(i - 1) + 6f * V(i) + 4f * V(i + 1) + V(i + 2)) / 16f;
            }
        }
        c.keys = k;
    }

    // Muscle names in clips use dots for the finger muscles ("Left Thumb 1 Stretched" -> "LeftHand.Thumb.1 Stretched").
    private static string MuscleAttr(string m)
    {
        foreach (var side in new[] { "Left", "Right" })
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                string pre = side + " " + finger + " ";
                if (m.StartsWith(pre)) return side + "Hand." + finger + "." + m.Substring(pre.Length);
            }
        return m;
    }

    private static void SetRot(Transform t, Dictionary<HumanBodyBones, Quaternion> rest, HumanBodyBones b, Quaternion bodyRot)
    {
        if (t != null) t.rotation = bodyRot * rest[b];
    }

    private static void Aim(Transform bone, Transform child, Vector3 target) => AimPartial(bone, child, target, 1f);

    private static void AimPartial(Transform bone, Transform child, Vector3 target, float amount)
    {
        if (bone == null || child == null || target.sqrMagnitude < 1e-8f) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, target), amount) * bone.rotation;
    }

    private static void BuildSet(Dictionary<string, AnimationClip> clips)
    {
        var set = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(SetPath);
        bool created = set == null;
        if (created) set = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(Template));

        var xbot = AssetDatabase.LoadAssetAtPath<GameObject>(SilhouetteMixamoImport.SkinFile);
        var go = Object.Instantiate(xbot);
        go.hideFlags = HideFlags.HideAndDontSave;
        var a = go.GetComponent<Animator>();
        a.applyRootMotion = false;
        var log = new System.Text.StringBuilder("[Mocap] takes").AppendLine();
        foreach (var l in new[] { set.punches, set.uppercuts, set.steps, set.jacks, set.hops, set.sideSteps, set.dodges }) l.Clear();
        set.curb.Clear();
        var lively = set.idleLively.Where(m => m.clip != null && !m.clip.name.StartsWith("StepGroove")).ToList();
        try
        {
            set.analysisHipsHeight = a.humanScale;
            foreach (var (take, role) in Takes)
            foreach (var name in Reps(take, role))
                foreach (var n in new[] { name, name + " (Mirror)" })
                {
                    if (!clips.TryGetValue(n, out var clip)) continue;
                    var m = SilhouetteFitnessAnalysis.Analyze(go, a, clip, clip, role, log);
                    if (role == SilhouetteFitnessAnalysis.Role.Steps) LandMarks(go, a, m, log);
                    bool original = n == name;
                    // A rep whose strike sits on the cut's edge has no wind-up or recovery: drop it.
                    bool single = role != SilhouetteFitnessAnalysis.Role.Steps && role != SilhouetteFitnessAnalysis.Role.Jack && role != SilhouetteFitnessAnalysis.Role.Idle;
                    if (single && (m.impact < 0.2f || m.impact > m.Length - 0.15f)) { log.AppendLine("  dropped " + n); continue; }
                    switch (role)
                    {
                        case SilhouetteFitnessAnalysis.Role.Punch: set.punches.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Uppercut: set.uppercuts.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Steps: set.steps.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Jack: set.jacks.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Hop: set.hops.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Jump: if (original) set.curb.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Squat: if (original) set.squat = m; break;
                        case SilhouetteFitnessAnalysis.Role.SideStep: set.sideSteps.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Dodge: set.dodges.Add(m); break;
                        case SilhouetteFitnessAnalysis.Role.Idle: if (original) lively.Add(new SilhouetteMotion { clip = clip }); break;
                    }
                }
        }
        finally { Object.DestroyImmediate(go); }
        set.idleLively = lively;
        foreach (var m in set.curb)
            if (m.impact >= m.Length - 0.1f) m.reverseAfterImpact = true;
        set.mocapOnly = true;
        set.noFootIK = true;
        set.assistHand = 0f;
        set.assistFoot = 0f;
        if (created) AssetDatabase.CreateAsset(set, SetPath);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Screenshots");
        File.WriteAllText("Screenshots/trainer_mocap_takes.txt", log.ToString());
        Debug.Log("[Mocap] motion set written to " + SetPath + "\n" + log);
    }
}
