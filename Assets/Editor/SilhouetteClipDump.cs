using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Writes every fitness take's bone paths, sampled on the X Bot at 60 fps, to
/// Temp/SilhouetteClipDump/<clip>.csv - hips, hands, feet, knees and shoulders in the model's own
/// space - for looking at a take's timing outside Unity (where its landings and punches are).
public static class SilhouetteClipDump
{
    [MenuItem("Tunnel/Silhouette/Dump Fitness Clips", priority = 45)]
    public static void Dump()
    {
        string dir = Path.Combine("Temp", "SilhouetteClipDump");
        Directory.CreateDirectory(dir);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(SilhouetteMixamoImport.SkinFile);
        var go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        var a = go.GetComponent<Animator>();
        a.applyRootMotion = false;
        var bones = new[]
        {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        };
        var ci = CultureInfo.InvariantCulture;
        try
        {
            var paths = AssetDatabase.FindAssets("t:Model", new[] { SilhouetteMixamoImport.Root + "/" + SilhouetteMixamoImport.FitnessFolder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p);
            foreach (var path in paths)
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    var sb = new StringBuilder("t");
                    foreach (var b in bones) sb.Append(',').Append(b).Append("_x,").Append(b).Append("_y,").Append(b).Append("_z");
                    sb.Append('\n');
                    int frames = Mathf.RoundToInt(clip.length * 60f);
                    for (int f = 0; f <= frames; f++)
                    {
                        float t = Mathf.Min(clip.length, f / 60f);
                        clip.SampleAnimation(go, t);
                        sb.Append(t.ToString("F4", ci));
                        foreach (var b in bones)
                        {
                            var p = go.transform.InverseTransformPoint(a.GetBoneTransform(b).position);
                            sb.Append(',').Append(p.x.ToString("F4", ci)).Append(',').Append(p.y.ToString("F4", ci)).Append(',').Append(p.z.ToString("F4", ci));
                        }
                        sb.Append('\n');
                    }
                    File.WriteAllText(Path.Combine(dir, clip.name + ".csv"), sb.ToString());
                    Debug.Log("[Silhouette] dump " + clip.name + " " + clip.length.ToString("F2", ci) + " s loop " + clip.isLooping);
                }
        }
        finally { Object.DestroyImmediate(go); }
    }
}
