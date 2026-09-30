using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// Finds, in each fitness take, the moments the trainer puts on notes - by sampling it on the
/// X Bot and watching the bones, the way SilhouetteClipAnalysis does for the older takes:
///   Punch    - the frame the striking hand is furthest from its shoulder (the hand that gains most)
///   Uppercut - the frame the striking hand is highest
///   Steps    - every tap, step or knee of a dance loop: a peak of how far a foot is out from the
///              hips plus how high it is, with the foot that does it
///   Jack     - every landing (the hips at their lowest), open or closed by how far apart the feet are
///   Hop      - the landing (impact), take-off and apex of a jump in place
///   Jump     - the apex (the kerb's hop is timed on it), take-off and landing
///   Squat    - the bottom (impact) and the moment it is up again
///   Side step, dodge - where the hips have 95% of their sideways travel, and the travel's path
/// A mirrored twin (" (Mirror)") is measured on its original - SampleAnimation ignores the mirror
/// flag - with the limbs swapped and the travel reversed.
public static class SilhouetteFitnessAnalysis
{
    public enum Role { Punch, Uppercut, Steps, Jack, Hop, Jump, Squat, SideStep, Dodge, Idle }

    private const float Fps = 60f;

    private struct Pose
    {
        public Vector3 hips, head, lHand, rHand, lShoulder, rShoulder, lFoot, rFoot;
    }

    public static SilhouetteMotion Analyze(GameObject go, Animator a, AnimationClip clip, AnimationClip measureOn, Role role, StringBuilder log)
    {
        bool mirror = measureOn != clip;
        int frames = Mathf.Max(1, Mathf.RoundToInt(measureOn.length * Fps));
        var p = new Pose[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            measureOn.SampleAnimation(go, Mathf.Min(measureOn.length, f / Fps));
            Vector3 P(HumanBodyBones b) => go.transform.InverseTransformPoint(a.GetBoneTransform(b).position);
            p[f] = new Pose
            {
                hips = P(HumanBodyBones.Hips), head = P(HumanBodyBones.Head),
                lHand = P(HumanBodyBones.LeftHand), rHand = P(HumanBodyBones.RightHand),
                lShoulder = P(HumanBodyBones.LeftUpperArm), rShoulder = P(HumanBodyBones.RightUpperArm),
                lFoot = P(HumanBodyBones.LeftFoot), rFoot = P(HumanBodyBones.RightFoot),
            };
        }
        float T(int f) => Mathf.Min(measureOn.length, f / Fps);
        var m = new SilhouetteMotion { clip = clip, impact = 0f, limb = -1 };
        var marks = new List<float>();
        var kinds = new List<MarkKind>();
        var limbs = new List<int>();
        float restFoot = Mathf.Min(Min(p, q => q.lFoot.y), Min(p, q => q.rFoot.y));

        switch (role)
        {
            case Role.Punch:
            case Role.Uppercut:
            {
                // The hand that gains the most is the one that strikes.
                float Metric(Pose q, int hand) => role == Role.Punch
                    ? Vector3.Distance(hand == 0 ? q.lHand : q.rHand, hand == 0 ? q.lShoulder : q.rShoulder)
                    : (hand == 0 ? q.lHand.y : q.rHand.y);
                int bestHand = 0, bestF = 0;
                float bestGain = float.NegativeInfinity;
                for (int h = 0; h < 2; h++)
                    for (int f = 0; f <= frames; f++)
                    {
                        float g = Metric(p[f], h) - Metric(p[0], h);
                        if (g > bestGain) { bestGain = g; bestHand = h; bestF = f; }
                    }
                m.limb = bestHand;
                m.impact = T(bestF);
                marks.Add(m.impact); kinds.Add(MarkKind.Strike); limbs.Add(bestHand);
                break;
            }
            case Role.Steps:
            {
                // How far out and how high each foot is; its peaks are the taps.
                for (int foot = 0; foot < 2; foot++)
                {
                    var e = new float[frames + 1];
                    for (int f = 0; f <= frames; f++)
                    {
                        var fp = foot == 0 ? p[f].lFoot : p[f].rFoot;
                        var d = fp - p[f].hips; d.y = 0f;
                        e[f] = d.magnitude + (fp.y - restFoot);
                    }
                    int win = Mathf.RoundToInt(0.14f * Fps);
                    int last = -1000;
                    for (int f = 0; f <= frames; f++)
                    {
                        if (e[f] < 0.24f) continue;
                        bool peak = true;
                        for (int k = Mathf.Max(0, f - win); k <= Mathf.Min(frames, f + win) && peak; k++)
                            if (e[k] > e[f] || (e[k] == e[f] && k < f)) peak = false;
                        if (!peak || f - last < win) continue;
                        last = f;
                        marks.Add(T(f)); kinds.Add(MarkKind.Step); limbs.Add(foot);
                    }
                }
                break;
            }
            case Role.Jack:
            {
                // A loop: the last frame is the first again, so frames 0..frames-1 wrap around.
                int win = Mathf.RoundToInt(0.15f * Fps);
                for (int f = 0; f < frames; f++)
                {
                    bool low = true;
                    for (int k = f - win; k <= f + win && low; k++)
                    {
                        int kk = ((k % frames) + frames) % frames;
                        if (p[kk].hips.y < p[f].hips.y || (p[kk].hips.y == p[f].hips.y && kk < f)) low = false;
                    }
                    if (!low) continue;
                    float apart = Mathf.Abs(p[f].lFoot.x - p[f].rFoot.x);
                    marks.Add(T(f)); kinds.Add(apart > 0.4f ? MarkKind.LandOpen : MarkKind.LandClosed); limbs.Add(-1);
                }
                break;
            }
            case Role.Hop:
            case Role.Jump:
            {
                int top = 0;
                for (int f = 0; f <= frames; f++) if (p[f].hips.y > p[top].hips.y) top = f;
                float ground = restFoot + 0.03f;
                float Low(Pose q) => Mathf.Min(q.lFoot.y, q.rFoot.y);
                int off = top, land = top;
                while (off > 0 && Low(p[off]) > ground) off--;
                while (land < frames && Low(p[land]) > ground) land++;
                m.takeoff = T(off); m.apex = T(top);
                float landT = T(land);
                m.impact = role == Role.Hop ? landT : m.apex;
                marks.Add(landT); kinds.Add(MarkKind.Land); limbs.Add(-1);
                break;
            }
            case Role.Squat:
            {
                int bottom = 0;
                for (int f = 0; f <= frames; f++) if (p[f].hips.y < p[bottom].hips.y) bottom = f;
                int up = bottom;
                while (up < frames && p[up].hips.y < p[0].hips.y - 0.02f) up++;
                m.impact = T(bottom);
                m.rise = T(up);
                break;
            }
            case Role.SideStep:
            case Role.Dodge:
            {
                // Side step: the hips' travel. Dodge: the head's, which is where the lean shows.
                float X(Pose q) => role == Role.Dodge ? q.head.x : q.hips.x;
                float max = 0f;
                for (int f = 0; f <= frames; f++) max = Mathf.Max(max, Mathf.Abs(X(p[f]) - X(p[0])));
                int at = 0;
                for (int f = 0; f <= frames; f++) if (Mathf.Abs(X(p[f]) - X(p[0])) >= max * 0.95f) { at = f; break; }
                m.impact = T(at);
                m.travel = p[frames].hips.x - p[0].hips.x;
                m.travelCurve = new AnimationCurve();
                for (int f = 0; f <= frames; f += 3) m.travelCurve.AddKey(T(f), p[f].hips.x - p[0].hips.x);
                m.limb = (role == Role.Dodge ? X(p[at]) - X(p[0]) : m.travel) > 0f ? 1 : 0;   // toward +X = the figure's right
                break;
            }
        }

        if (mirror)
        {
            if (m.limb >= 0) m.limb = 1 - m.limb;
            for (int i = 0; i < limbs.Count; i++) if (limbs[i] >= 0) limbs[i] = 1 - limbs[i];
            m.travel = -m.travel;
            if (m.travelCurve != null)
            {
                var keys = m.travelCurve.keys;
                for (int i = 0; i < keys.Length; i++) keys[i].value = -keys[i].value;
                m.travelCurve = new AnimationCurve(keys);
            }
        }
        // Marks in time order.
        var order = new List<int>();
        for (int i = 0; i < marks.Count; i++) order.Add(i);
        order.Sort((x, y) => marks[x].CompareTo(marks[y]));
        m.marks = new float[order.Count]; m.markKinds = new MarkKind[order.Count]; m.markLimbs = new int[order.Count];
        for (int i = 0; i < order.Count; i++) { m.marks[i] = marks[order[i]]; m.markKinds[i] = kinds[order[i]]; m.markLimbs[i] = limbs[order[i]]; }

        if (log != null)
        {
            var ci = CultureInfo.InvariantCulture;
            log.Append(clip.name).Append(" | ").Append(role).Append(" | ").Append(clip.length.ToString("F2", ci))
               .Append(clip.isLooping ? " loop" : "").Append(" | impact ").Append(m.impact.ToString("F3", ci))
               .Append(" limb ").Append(m.limb);
            if (m.takeoff >= 0f) log.Append(" takeoff ").Append(m.takeoff.ToString("F2", ci)).Append(" apex ").Append(m.apex.ToString("F2", ci));
            if (m.rise >= 0f) log.Append(" up ").Append(m.rise.ToString("F2", ci));
            if (role == Role.SideStep || role == Role.Dodge) log.Append(" travel ").Append(m.travel.ToString("+0.00;-0.00", ci));
            if (m.marks.Length > 1)
            {
                log.Append(" | marks");
                for (int i = 0; i < m.marks.Length; i++)
                    log.Append(' ').Append(m.marks[i].ToString("F2", ci)).Append(m.markKinds[i] == MarkKind.LandOpen ? "o" : m.markKinds[i] == MarkKind.LandClosed ? "c" : m.markLimbs[i] == 0 ? "L" : m.markLimbs[i] == 1 ? "R" : "");
            }
            log.Append('\n');
        }
        return m;
    }

    private static float Min(Pose[] p, System.Func<Pose, float> f)
    {
        float m = float.PositiveInfinity;
        foreach (var q in p) m = Mathf.Min(m, f(q));
        return m;
    }
}
