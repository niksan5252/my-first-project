using System.Text;
using UnityEditor;
using UnityEngine;

/// Logs where a mocap clip puts Jennifer's joints (model space) - a check of the retarget without rendering.
public static class MocapDebug
{
    [MenuItem("Tunnel/Mocap/Debug Punch Reach", priority = 62)]
    public static void PunchReach()
    {
        var set = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>("Assets/SharedAssets/Mixamo/JenniferFilmedMotionSet.asset");
        var go = Object.Instantiate(set.model);
        go.hideFlags = HideFlags.HideAndDontSave;
        var a = go.GetComponent<Animator>();
        var sb = new StringBuilder();
        foreach (var m in set.punches)
        {
            m.clip.SampleAnimation(go, m.impact);
            var hand = a.GetBoneTransform(m.limb == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
            var sh = a.GetBoneTransform(m.limb == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm).position;
            var d = hand - sh;
            sb.AppendLine($"{m.clip.name} limb {m.limb}: side {d.x:F2} up {d.y:F2} fwd {d.z:F2}");
        }
        Object.DestroyImmediate(go);
        System.IO.File.WriteAllText("Screenshots/punch_reach.txt", sb.ToString());
    }

    [MenuItem("Tunnel/Mocap/Debug Pose Dump", priority = 61)]
    public static void Dump()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(SilhouetteMotionSetBuilder.JenniferModel);
        var go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        var a = go.GetComponent<Animator>();
        var sb = new StringBuilder();
        string P(HumanBodyBones b) { var p = a.GetBoneTransform(b).position; return $"({p.x:F2},{p.y:F2},{p.z:F2})"; }
        void Line(string tag) => sb.AppendLine($"{tag}: hips{P(HumanBodyBones.Hips)} head{P(HumanBodyBones.Head)} lKnee{P(HumanBodyBones.LeftLowerLeg)} lFoot{P(HumanBodyBones.LeftFoot)} rFoot{P(HumanBodyBones.RightFoot)} lHand{P(HumanBodyBones.LeftHand)} rHand{P(HumanBodyBones.RightHand)}");
        Line("rest");
        foreach (var (name, t) in new[] { ("Star", 1.08f), ("Kick", 1.0f), ("Punch#2", 0.53f), ("StepGroove", 2f) })
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Trainer/Mocap/" + name + ".anim");
            if (clip == null) { sb.AppendLine(name + " missing"); continue; }
            clip.SampleAnimation(go, t);
            Line(name);
        }
        sb.AppendLine("humanScale " + a.humanScale);
        var tpl = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>("Assets/SharedAssets/Mixamo/JenniferFilmedMotionSet.asset");
        foreach (var c in new[] { tpl.punches[0].clip, tpl.idle[0].clip, AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Trainer/Mocap/Star.anim"), AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Trainer/Mocap/Punch#2.anim") })
        {
            float y = 0f, x = 0f;
            foreach (var b in AnimationUtility.GetCurveBindings(c))
            {
                if (b.propertyName == "RootT.y") y = AnimationUtility.GetEditorCurve(c, b).Evaluate(0.3f);
                if (b.propertyName == "RootT.x") x = AnimationUtility.GetEditorCurve(c, b).Evaluate(0.3f);
            }
            sb.AppendLine(c.name + " RootT.y " + y + " x " + x + " humanMotion " + c.humanMotion);
        }
        Object.DestroyImmediate(go);
        System.IO.File.WriteAllText("Screenshots/mocap_debug.txt", sb.ToString());
    }
}
