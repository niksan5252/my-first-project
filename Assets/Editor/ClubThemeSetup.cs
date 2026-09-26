using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Builds the "Festival / Club" theme and the two run profiles (irokz on the cyberpunk tunnel,
/// Adikop in the club) from code, so the assets can be regenerated rather than hand-assembled.
/// Existing assets are updated in place, keeping their GUIDs; the other themes are not touched.
public static class ClubThemeSetup
{
    private const string ThemePath = "Assets/Themes/Theme_Club.asset";
    private const string VolumePath = "Assets/Settings/TunnelScene_PostProcessing_Club.asset";
    private const string VolumeSource = "Assets/Settings/TunnelScene_PostProcessing_Cyberpunk.asset";
    private const string RingPrefabPath = "Assets/Prefabs/Ring_ClubHalo.prefab";
    private const string EnvPrefabPath = "Assets/Prefabs/Environment_Club.prefab";
    private const string ProfilesFolder = "Assets/Themes/Profiles";
    private const string IrokzProfilePath = ProfilesFolder + "/RunProfile_Irokz.asset";
    private const string AdikopProfilePath = ProfilesFolder + "/RunProfile_Adikop.asset";
    private const string AdikopTrack = "Assets/Audio/Tracks/adikop_bring_me_back.wav";
    private const string IrokzTrack = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";
    private const string Masks = "Assets/SharedAssets/VFX/kenney_light-masks-1.0/Default/";
    private const string Patterns = "Assets/SharedAssets/Textures/kenney_pattern-pack-extra/PNG/Default/";

    // Warm amber and violet: away from the magenta/cyan tunnel, and apart from both the cyan
    // trainer and the yellow centre words.
    public static readonly Color Amber = new Color(1f, 0.42f, 0.04f);
    public static readonly Color Violet = new Color(0.58f, 0.18f, 1f);

    [MenuItem("Tunnel/Themes/Build Club Theme + Run Profiles", priority = 60)]
    public static void Build()
    {
        ConfigureTrackImport();

        if (!File.Exists(VolumePath)) AssetDatabase.CopyAsset(VolumeSource, VolumePath);

        // Ring prefab
        var ringGo = new GameObject("Ring_ClubHalo");
        ringGo.AddComponent<RingArchClubHalo>();
        PrefabUtility.SaveAsPrefabAsset(ringGo, RingPrefabPath);
        Object.DestroyImmediate(ringGo);

        // Environment prefab
        var envGo = new GameObject("Environment_Club");
        var deco = envGo.AddComponent<ClubEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        so.FindProperty("ledShader").objectReferenceValue = Shader.Find("Tunnel/ClubLedWall");
        so.FindProperty("glowShader").objectReferenceValue = Shader.Find("Tunnel/ClubGlow");
        so.FindProperty("coneMask").objectReferenceValue = Tex(Masks + "cone_a_blur.png");
        so.FindProperty("flareMask").objectReferenceValue = Tex(Masks + "circle_a.png");
        so.FindProperty("speakerMask").objectReferenceValue = Tex(Masks + "circle_rings_b.png");
        var pats = so.FindProperty("ledPatterns");
        string[] names = { "pattern_012.png", "pattern_024.png", "pattern_036.png", "pattern_048.png", "pattern_060.png" };
        pats.arraySize = 0;
        foreach (var n in names)
        {
            var t = Tex(Patterns + n);
            if (t == null) continue;
            pats.arraySize++;
            pats.GetArrayElementAtIndex(pats.arraySize - 1).objectReferenceValue = t;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(envGo, EnvPrefabPath);
        Object.DestroyImmediate(envGo);

        // Theme
        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ThemePath);
        bool created = theme == null;
        if (created) theme = ScriptableObject.CreateInstance<TunnelTheme>();
        theme.primaryColor = Amber;
        theme.secondaryColor = Violet;
        theme.accentEmissionIntensity = 1.4f;
        theme.ringArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath);
        theme.floorMaterialOverride = null;
        theme.floorColor = new Color(0.03f, 0.02f, 0.045f);
        theme.floorMetallic = 0.35f;
        theme.floorSmoothness = 0.6f;
        theme.floorGridLineColor = Violet;
        theme.floorGridGlintIntensity = 0.4f;
        theme.laneLineColor = Amber;
        theme.laneLineEmissionIntensity = 0.55f;
        theme.fogColor = new Color(0.03f, 0.012f, 0.05f);
        theme.fogStartRatio = 0.45f;
        theme.fogEndRatio = 1f;
        theme.lightIntensity = 0.25f;
        theme.lightColor = new Color(1f, 0.82f, 0.75f);
        theme.skyboxMaterial = null;
        theme.cameraBackgroundColor = new Color(0.015f, 0.008f, 0.03f);
        theme.environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvPrefabPath);
        theme.airParticles = AssetDatabase.LoadAssetAtPath<ParticlePreset>("Assets/Themes/Presets/ParticlePreset_SciFi.asset");
        theme.postProcessingProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumePath);
        theme.hudComboTextColor = Amber;
        theme.hudBannerBorderColor = Violet;
        if (created) AssetDatabase.CreateAsset(theme, ThemePath);
        EditorUtility.SetDirty(theme);

        // Run profiles
        if (!AssetDatabase.IsValidFolder(ProfilesFolder)) AssetDatabase.CreateFolder("Assets/Themes", "Profiles");
        var irokz = Profile(IrokzProfilePath, IrokzTrack, "irokz_goodbye_my_love.json", AssetDatabase.LoadAssetAtPath<TunnelTheme>("Assets/Themes/Theme_Cyberpunk.asset"));
        var adikop = Profile(AdikopProfilePath, AdikopTrack, "adikop_bring_me_back.json", theme);
        AssetDatabase.SaveAssets();

        // Scene: one selector, pointing at Adikop.
        var spawner = Object.FindAnyObjectByType<CueSpawner>();
        var applier = Object.FindAnyObjectByType<TunnelThemeApplier>();
        var sel = Object.FindAnyObjectByType<RunProfileSelector>();
        if (sel == null) sel = new GameObject("RunProfile").AddComponent<RunProfileSelector>();
        var sso = new SerializedObject(sel);
        sso.FindProperty("profile").objectReferenceValue = adikop;
        sso.FindProperty("cueSpawner").objectReferenceValue = spawner;
        sso.FindProperty("themeApplier").objectReferenceValue = applier;
        sso.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(sel.gameObject.scene);
        EditorSceneManager.SaveScene(sel.gameObject.scene);
        Debug.Log("[ClubTheme] built " + ThemePath + ", profiles " + irokz.name + " / " + adikop.name + ", active: " + adikop.name);
    }

    /// Points the scene's selector at a profile by name (Irokz / Adikop).
    public static void Select(string which)
    {
        var sel = Object.FindAnyObjectByType<RunProfileSelector>();
        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(which == "Irokz" ? IrokzProfilePath : AdikopProfilePath);
        var sso = new SerializedObject(sel);
        sso.FindProperty("profile").objectReferenceValue = p;
        sso.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(sel.gameObject.scene);
    }

    [MenuItem("Tunnel/Themes/Run Profile: Irokz", priority = 61)] private static void SelectIrokz() => Select("Irokz");
    [MenuItem("Tunnel/Themes/Run Profile: Adikop", priority = 62)] private static void SelectAdikop() => Select("Adikop");

    private static RunProfile Profile(string path, string track, string chart, TunnelTheme theme)
    {
        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(path);
        bool created = p == null;
        if (created) p = ScriptableObject.CreateInstance<RunProfile>();
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>(track);
        p.beatmapFileName = chart;
        p.theme = theme;
        if (created) AssetDatabase.CreateAsset(p, path);
        EditorUtility.SetDirty(p);
        return p;
    }

    /// The new track imports exactly like irokz: Vorbis, normalised, not preloaded (CueSpawner
    /// loads the data itself before the clock starts).
    private static void ConfigureTrackImport()
    {
        var src = (AudioImporter)AssetImporter.GetAtPath(IrokzTrack);
        var dst = (AudioImporter)AssetImporter.GetAtPath(AdikopTrack);
        if (src == null || dst == null) return;
        dst.defaultSampleSettings = src.defaultSampleSettings;
        dst.forceToMono = src.forceToMono;
        dst.loadInBackground = src.loadInBackground;
        dst.ambisonic = src.ambisonic;
        var sso = new SerializedObject(dst);
        var ssrc = new SerializedObject(src);
        var nd = sso.FindProperty("m_Normalize") ?? sso.FindProperty("normalize");
        var ns = ssrc.FindProperty("m_Normalize") ?? ssrc.FindProperty("normalize");
        if (nd != null && ns != null) nd.boolValue = ns.boolValue;
        sso.ApplyModifiedPropertiesWithoutUndo();
        dst.SaveAndReimport();
    }

    private static Texture2D Tex(string path)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (t == null) Debug.LogWarning("[ClubTheme] missing texture " + path);
        return t;
    }
}
