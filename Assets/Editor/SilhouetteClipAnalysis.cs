using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Finds each trainer clip's "impact frame" by sampling it on the X Bot and watching the bones,
/// instead of reading numbers off by eye in the Animation window:
///   punch  - the frame a hand is furthest AHEAD of its own shoulder (the arm at full reach)
///   kick   - the frame a foot is furthest ahead of the hips plus highest above where it stood -
///            the leg at full extension; for a knee strike, the knee instead of the foot.
///            (Height alone picks the chamber of a thrust kick, not the kick.)
///   jump   - the apex of the hips
///   dodge  - the frame the hips have covered 90% of their sideways travel
///   crouch - the frame the hips reach the bottom (down) or the top (up)
/// Loops have no impact; for them the pass reports only the length and how far the hips wander.
///
/// Sampling is done on a throwaway instance of the skin model in the Editor, 60 samples a second
/// (the clips' own rate), so the result is the pose the Animator will actually produce.
public static class SilhouetteClipAnalysis
{
    public enum Kind { Punch, Kick, Jump, DodgeLeft, DodgeRight, CrouchDown, CrouchUp, Loop, OneShot }

    public struct Result
    {
        public AnimationClip clip;
        public Kind kind;
        public float impact;      // seconds into the clip
        public string side;       // which hand/foot, or the travel direction
        public float travel;      // hips displacement (m) at impact, or wander for a loop
        public float peak;        // the metric's value at impact
        public float restHipsY;
    }

    public const float Fps = 60f;

    public static Kind KindFor(string folder, string clipName)
    {
        switch (folder)
        {
            case "punch": return clipName.StartsWith("Speedbag") ? Kind.Loop : Kind.Punch;
            case "kick (step)": return clipName.StartsWith("Kick") ? Kind.Kick : Kind.Loop;
            case "jump": return Kind.Jump;
            case "side step": return clipName.Contains("Left") ? Kind.DodgeLeft : Kind.DodgeRight;
            case "crouch": return clipName.StartsWith("Standing To") ? Kind.CrouchDown : Kind.CrouchUp;
            case "fight idle": return clipName.Contains(" To ") ? Kind.OneShot : Kind.Loop;
            default: return Kind.Loop;
        }
    }

    public static List<Result> AnalyzeAll(out string table)
    {
        var results = new List<Result>();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(SilhouetteMixamoImport.SkinFile);
        var go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        var animator = go.GetComponent<Animator>();
        animator.applyRootMotion = false;

        var paths = AssetDatabase.FindAssets("t:Model", new[] { SilhouetteMixamoImport.Root })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
        var sb = new StringBuilder("[Silhouette] clip analysis\nclip | kind | len s | impact s (frame) | side | travel m | peak\n");
        try
        {
            foreach (var path in paths)
            {
                string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)).ToLowerInvariant();
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    var r = Analyze(go, animator, clip, KindFor(folder, clip.name));
                    results.Add(r);
                    sb.Append(clip.name).Append(" | ").Append(r.kind).Append(" | ").Append(clip.length.ToString("F2"))
                      .Append(" | ").Append(r.impact.ToString("F3")).Append(" (").Append(Mathf.RoundToInt(r.impact * Fps)).Append(")")
                      .Append(" | ").Append(r.side).Append(" | ").Append(r.travel.ToString("F2"))
                      .Append(" | ").Append(r.peak.ToString("F2")).Append('\n');
                }
            }
        }
        finally { Object.DestroyImmediate(go); }
        table = sb.ToString();
        return results;
    }

    private struct Pose
    {
        public Vector3 hips, lHand, rHand, lShoulder, rShoulder, lFoot, rFoot, lKnee, rKnee;
    }

    private static Pose Sample(GameObject go, Animator a, AnimationClip clip, float t)
    {
        clip.SampleAnimation(go, t);
        Vector3 P(HumanBodyBones b) => go.transform.InverseTransformPoint(a.GetBoneTransform(b).position);
        return new Pose
        {
            hips = P(HumanBodyBones.Hips),
            lHand = P(HumanBodyBones.LeftHand), rHand = P(HumanBodyBones.RightHand),
            lShoulder = P(HumanBodyBones.LeftUpperArm), rShoulder = P(HumanBodyBones.RightUpperArm),
            lFoot = P(HumanBodyBones.LeftFoot), rFoot = P(HumanBodyBones.RightFoot),
            lKnee = P(HumanBodyBones.LeftLowerLeg), rKnee = P(HumanBodyBones.RightLowerLeg),
        };
    }

    public static Result Analyze(GameObject go, Animator a, AnimationClip clip, Kind kind)
    {
        int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * Fps));
        var poses = new Pose[frames + 1];
        for (int f = 0; f <= frames; f++) poses[f] = Sample(go, a, clip, Mathf.Min(clip.length, f / Fps));
        var rest = poses[0];
        var r = new Result { clip = clip, kind = kind, side = "", restHipsY = rest.hips.y };

        int best = 0;
        float bestV = float.NegativeInfinity;
        void Consider(int f, float v, string side)
        {
            if (v > bestV) { bestV = v; best = f; r.side = side; }
        }

        switch (kind)
        {
            case Kind.Punch:
                for (int f = 0; f <= frames; f++)
                {
                    Consider(f, poses[f].lHand.z - poses[f].lShoulder.z, "L hand");
                    Consider(f, poses[f].rHand.z - poses[f].rShoulder.z, "R hand");
                }
                break;
            case Kind.Kick:
                bool knee = clip.name.Contains("Knee");
                for (int f = 0; f <= frames; f++)
                {
                    var p = poses[f];
                    if (knee)
                    {
                        Consider(f, (p.lKnee.z - p.hips.z) + (p.lKnee.y - rest.lKnee.y), "L leg");
                        Consider(f, (p.rKnee.z - p.hips.z) + (p.rKnee.y - rest.rKnee.y), "R leg");
                    }
                    else
                    {
                        Consider(f, (p.lFoot.z - p.hips.z) + (p.lFoot.y - rest.lFoot.y), "L leg");
                        Consider(f, (p.rFoot.z - p.hips.z) + (p.rFoot.y - rest.rFoot.y), "R leg");
                    }
                }
                break;
            case Kind.Jump:
                for (int f = 0; f <= frames; f++) Consider(f, poses[f].hips.y, "apex");
                break;
            case Kind.CrouchDown:
            case Kind.CrouchUp:
            {
                // First frame within 2% of the extreme, so a clip that settles slowly still times
                // the moment the body actually gets there.
                float target = kind == Kind.CrouchDown ? poses.Min(p => p.hips.y) : poses.Max(p => p.hips.y);
                float span = Mathf.Abs(poses.Max(p => p.hips.y) - poses.Min(p => p.hips.y));
                for (int f = 0; f <= frames; f++)
                    if (Mathf.Abs(poses[f].hips.y - target) <= span * 0.02f) { best = f; bestV = poses[f].hips.y; break; }
                r.side = kind == Kind.CrouchDown ? "bottom" : "top";
                r.travel = span;
                break;
            }
            case Kind.DodgeLeft:
            case Kind.DodgeRight:
            {
                float maxX = 0f;
                for (int f = 0; f <= frames; f++) maxX = Mathf.Max(maxX, Mathf.Abs(poses[f].hips.x - rest.hips.x));
                for (int f = 0; f <= frames; f++)
                    if (Mathf.Abs(poses[f].hips.x - rest.hips.x) >= maxX * 0.9f) { best = f; break; }
                float dx = poses[best].hips.x - rest.hips.x;
                r.side = dx < 0 ? "-X" : "+X";
                r.travel = dx;
                bestV = maxX;
                break;
            }
            default:
            {
                float wander = 0f;
                for (int f = 0; f <= frames; f++)
                {
                    var d = poses[f].hips - rest.hips; d.y = 0f;
                    wander = Mathf.Max(wander, d.magnitude);
                }
                r.travel = wander;
                bestV = 0f;
                best = 0;
                break;
            }
        }

        r.impact = best / Fps;
        r.peak = bestV;
        return r;
    }

    [MenuItem("Tunnel/Silhouette/Analyze Clips", priority = 41)]
    public static void LogAnalysis()
    {
        AnalyzeAll(out string table);
        Debug.Log(table);
    }
}
