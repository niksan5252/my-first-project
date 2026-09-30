using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Import rules for the Mixamo trainer clips under Assets/SharedAssets/Mixamo.
///
/// One file carries the X Bot's skin (side step/Long Left Side Step.fbx); its Avatar is the one
/// every other file copies, so all of them retarget onto the same skeleton without each growing an
/// avatar of its own. Everything is Humanoid.
///
/// Root motion is BAKED INTO THE POSE on every clip - rotation, height and XZ, all on the original
/// root - so the body does exactly what Mixamo authored relative to the model's root and the root
/// itself never moves (the Animator runs with root motion off). That is what keeps a jump a jump
/// and a crouch a crouch, and what lets SilhouetteTrainer soft-clamp the few clips that do travel
/// (the long side steps, some dances) instead of losing their movement entirely.
///
/// Cycles are the exception: several of the step loops are not really "In Place" (Walking covers
/// 1.7 m per cycle, Catwalk Walk 5 m), and neither are some dances, so on a loop the XZ travel is
/// left OUT of the pose. It becomes root motion, which the Animator discards, and the figure
/// steps on the spot. The step loops leave their height out as well (Running Up Stairs climbs).
///
/// `mirrorOneShots` adds a mirrored twin (" (Mirror)") of every punch and kick, for a character
/// whose takes all lead with one side. The X Bot set does not need it: its punches already use
/// both hands and its kicks both legs, see SilhouetteClipAnalysis.
///
/// fitness/ holds the takes every exercise is built on (Jumping Jacks, Step Touch, the jabs, the
/// side steps, Air Squat...), downloaded In Place. Every one of them gets a mirrored twin, because
/// a take leads with one hand or one foot and the trainer has to show the note on either side.
public class SilhouetteMixamoImport : AssetPostprocessor
{
    public const string Root = "Assets/SharedAssets/Mixamo";
    public const string SkinFile = Root + "/side step/Long Left Side Step.fbx";

    // Folders whose clips run as cycles rather than one-shots.
    private static readonly string[] LoopFolders = { "dance", "fight idle" };
    private static readonly string[] LoopNames =
    {
        "Speedbag", "Stomping", "Stepping Left", "Walking", "Catwalk Walk", "Running Up Stairs",
        "Jumping Jacks", "Running Man", "Step Touch Dance", "Boxing Warm Up", "Kick Step",
    };
    public const string FitnessFolder = "fitness";
    // One-shots that get a mirrored copy, when mirrorOneShots is on.
    private static readonly string[] MirrorFolders = { "punch", "kick (step)" };
    private const bool mirrorOneShots = false;
    // Cycles that must stay level as well as on the spot.
    private static readonly string[] StepLoops = { "Stomping", "Stepping Left", "Walking", "Catwalk Walk", "Running Up Stairs" };

    private static bool IsMixamo(string path) => path.Replace('\\', '/').StartsWith(Root + "/") && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

    public static string ClipName(string path)
    {
        string n = Path.GetFileNameWithoutExtension(path);
        int at = n.IndexOf('@');
        return at >= 0 ? n.Substring(at + 1) : n;
    }

    private static string Folder(string path) => Path.GetFileName(Path.GetDirectoryName(path)).ToLowerInvariant();

    public static bool IsLoop(string path)
    {
        string name = ClipName(path);
        return LoopFolders.Contains(Folder(path)) && name != "Fight Idle To Standing Idle" || LoopNames.Contains(name);
    }

    public const string CharactersFolder = Root + "/Characters/";
    public static bool IsCharacter(string path) => path.Replace('\\', '/').StartsWith(CharactersFolder);

    private static bool WantsMirror(string path) =>
        Folder(path) == FitnessFolder || mirrorOneShots && MirrorFolders.Contains(Folder(path)) && !IsLoop(path);

    private void OnPreprocessModel()
    {
        if (!IsMixamo(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        bool skin = assetPath.Replace('\\', '/') == SkinFile;

        // A character (Characters/): a rigged T-pose model that is only ever drawn as a contour.
        // Its own avatar, and neither materials nor animation - the clips come from the other
        // files, retargeted through the humanoid.
        if (IsCharacter(assetPath))
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            return;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.importAnimation = true;
        importer.materialImportMode = skin ? ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.resampleCurves = true;
        importer.animationCompression = ModelImporterAnimationCompression.Optimal;

        // The fitness takes come on another export of the skeleton (bones "mixamorig6:"), which
        // the X Bot's avatar does not map: each builds its own, and the humanoid retargets.
        if (skin || Folder(assetPath) == FitnessFolder) importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        else
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(SkinFile).OfType<Avatar>().FirstOrDefault();
            // The skin file imports first (see Configure); if it is somehow not there yet the file
            // falls back to its own avatar rather than to none, and Configure imports it again.
            if (avatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
            }
            else importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }
    }

    // Clip settings need the takes, which only exist once the file has been read once - so they
    // are written by Configure against defaultClipAnimations rather than in the preprocessor.
    public static void ApplyClipSettings(ModelImporter importer, string path)
    {
        var takes = importer.defaultClipAnimations;
        if (takes == null || takes.Length == 0) return;
        var src = takes[0];
        string name = ClipName(path);
        bool loop = IsLoop(path);

        bool step = StepLoops.Contains(name);
        var clips = new List<ModelImporterClipAnimation> { Make(src, name, loop, false, !loop, !step) };
        if (WantsMirror(path)) clips.Add(Make(src, name + " (Mirror)", loop, true, !loop, !step));
        importer.clipAnimations = clips.ToArray();
    }

    private static ModelImporterClipAnimation Make(ModelImporterClipAnimation src, string name, bool loop, bool mirror, bool bakeXZ, bool bakeY)
    {
        return new ModelImporterClipAnimation
        {
            name = name,
            takeName = src.takeName,
            firstFrame = src.firstFrame,
            lastFrame = src.lastFrame,
            loopTime = loop,
            loopPose = false,
            mirror = mirror,
            lockRootRotation = true,
            keepOriginalOrientation = true,
            lockRootHeightY = bakeY,
            keepOriginalPositionY = true,
            lockRootPositionXZ = bakeXZ,
            keepOriginalPositionXZ = true,
            heightFromFeet = false,
        };
    }

    [MenuItem("Tunnel/Silhouette/Configure Mixamo Import", priority = 40)]
    public static void Configure()
    {
        EnsureLayer(SilhouetteLayerName);
        var files = AssetDatabase.FindAssets("t:Model", new[] { Root })
            .Select(AssetDatabase.GUIDToAssetPath).Where(IsMixamo).ToList();
        // Skin first: every other file copies its avatar.
        files.Sort((a, b) => (a == SkinFile ? 0 : 1).CompareTo(b == SkinFile ? 0 : 1));

        foreach (var path in files)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); // runs OnPreprocessModel
            if (IsCharacter(path)) continue;
            ApplyClipSettings(importer, path);
            importer.SaveAndReimport();
        }
        Debug.Log("[Silhouette] configured " + files.Count + " Mixamo files");
    }

    public const string SilhouetteLayerName = "Silhouette";

    public static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(p.stringValue)) continue;
            p.stringValue = name;
            tagManager.ApplyModifiedProperties();
            return i;
        }
        Debug.LogError("[Silhouette] no free user layer for '" + name + "'");
        return -1;
    }
}
