using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Builds the "Night Drift / Phonk" theme and the Rameses run profile from code, like
/// ClubThemeSetup. Existing assets are updated in place; the other themes and profiles are not
/// touched, except that the scene's selector is pointed at Rameses.
public static class PhonkThemeSetup
{
    private const string ThemePath = "Assets/Themes/Theme_Phonk.asset";
    private const string VolumePath = "Assets/Settings/TunnelScene_PostProcessing_Phonk.asset";
    private const string VolumeSource = "Assets/Settings/TunnelScene_PostProcessing_Cyberpunk.asset";
    private const string RingPrefabPath = "Assets/Prefabs/Ring_PhonkGate.prefab";
    private const string EnvPrefabPath = "Assets/Prefabs/Environment_Phonk.prefab";
    private const string EmbersPath = "Assets/Themes/Presets/ParticlePreset_PhonkEmbers.asset";
    private const string ProfilePath = "Assets/Themes/Profiles/RunProfile_Rameses.asset";
    private const string Track = "Assets/Audio/Tracks/rameses_offline.wav";
    private const string IrokzTrack = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";
    private const string Kit = "Assets/SharedAssets/Kits3D/kenney_city-kit-roads/Models/FBX format/";
    private const string Masks = "Assets/SharedAssets/VFX/kenney_light-masks-1.0/Default/";

    // Deep crimson and a cold white: away from the club's amber/violet and the cyberpunk
    // magenta/cyan, against the cyan trainer and the yellow words. The white is kept dim: at full
    // strength an obstacle's glowing body (the theme's SECOND colour) burnt out to a white slab,
    // so crimson is the second colour and the white the first, at ~70%.
    public static readonly Color Crimson = new Color(0.92f, 0.04f, 0.1f);
    public static readonly Color ColdWhite = new Color(0.6f, 0.66f, 0.82f);

    [MenuItem("Tunnel/Themes/Build Phonk Theme + Rameses Profile", priority = 63)]
    public static void Build()
    {
        CopyTrackImport();
        if (!File.Exists(VolumePath)) AssetDatabase.CopyAsset(VolumeSource, VolumePath);

        var ringGo = new GameObject("Ring_PhonkGate");
        ringGo.AddComponent<RingArchPhonkGate>();
        PrefabUtility.SaveAsPrefabAsset(ringGo, RingPrefabPath);
        Object.DestroyImmediate(ringGo);

        var envGo = new GameObject("Environment_Phonk");
        var deco = envGo.AddComponent<PhonkEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        so.FindProperty("glowShader").objectReferenceValue = Shader.Find("Tunnel/ClubGlow");
        so.FindProperty("flareMask").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Masks + "circle_a.png");
        so.FindProperty("coneMask").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Masks + "cone_a_blur.png");
        so.FindProperty("smokeTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Themes/Presets/SmokePuff_Brackeys.png");
        so.FindProperty("lampModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "light-curved.fbx");
        so.FindProperty("pillarModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "bridge-pillar-wide.fbx");
        so.FindProperty("barrierModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "construction-barrier.fbx");
        so.FindProperty("warningLightModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "construction-light.fbx");
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(envGo, EnvPrefabPath);
        Object.DestroyImmediate(envGo);

        // Embers drifting through the air, in the theme's two colours.
        var embers = AssetDatabase.LoadAssetAtPath<ParticlePreset>(EmbersPath);
        bool newEmbers = embers == null;
        if (newEmbers) embers = ScriptableObject.CreateInstance<ParticlePreset>();
        embers.enabled = true;
        embers.particleMaterial = null;
        embers.rate = 28f;
        embers.lifetime = new Vector2(1.2f, 2.8f);
        embers.speed = new Vector2(3f, 9f);
        embers.size = new Vector2(0.02f, 0.05f);
        embers.emissionIntensity = 1.1f;
        embers.shapeType = ParticleSystemShapeType.Box;
        if (newEmbers) AssetDatabase.CreateAsset(embers, EmbersPath);
        EditorUtility.SetDirty(embers);

        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ThemePath);
        bool created = theme == null;
        if (created) theme = ScriptableObject.CreateInstance<TunnelTheme>();
        theme.primaryColor = ColdWhite;
        theme.secondaryColor = Crimson;
        theme.accentEmissionIntensity = 1.25f;
        theme.ringArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath);
        theme.floorMaterialOverride = null;
        // Wet asphalt: near black, smooth.
        theme.floorColor = new Color(0.015f, 0.015f, 0.02f);
        theme.floorMetallic = 0.25f;
        theme.floorSmoothness = 0.92f;
        theme.floorGridLineColor = new Color(0.45f, 0.03f, 0.06f);
        theme.floorGridGlintIntensity = 0.3f;
        theme.laneLineColor = ColdWhite;
        theme.laneLineEmissionIntensity = 0.4f;
        theme.fogColor = new Color(0.045f, 0.008f, 0.014f);
        theme.fogStartRatio = 0.3f;
        theme.fogEndRatio = 0.95f;
        theme.lightIntensity = 0.3f;
        theme.lightColor = new Color(0.75f, 0.8f, 1f);
        theme.skyboxMaterial = null;
        theme.cameraBackgroundColor = new Color(0.01f, 0.008f, 0.012f);
        theme.environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvPrefabPath);
        theme.airParticles = embers;
        theme.postProcessingProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumePath);
        theme.hudComboTextColor = Crimson;
        theme.hudBannerBorderColor = ColdWhite;
        theme.floorSmoothness = 0.8f;
        if (created) AssetDatabase.CreateAsset(theme, ThemePath);
        EditorUtility.SetDirty(theme);

        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(ProfilePath);
        bool newProfile = p == null;
        if (newProfile) p = ScriptableObject.CreateInstance<RunProfile>();
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>(Track);
        p.beatmapFileName = "rameses_offline.json";
        p.theme = theme;
        if (newProfile) AssetDatabase.CreateAsset(p, ProfilePath);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();

        SelectRameses();
        Debug.Log("[PhonkTheme] built " + ThemePath + " and " + ProfilePath + ", active: Rameses");
    }

    [MenuItem("Tunnel/Themes/Run Profile: Rameses", priority = 64)]
    public static void SelectRameses()
    {
        var sel = Object.FindAnyObjectByType<RunProfileSelector>();
        if (sel == null) { Debug.LogError("[PhonkTheme] no RunProfileSelector in the scene"); return; }
        var sso = new SerializedObject(sel);
        sso.FindProperty("profile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RunProfile>(ProfilePath);
        sso.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(sel.gameObject.scene);
        EditorSceneManager.SaveScene(sel.gameObject.scene);
    }

    /// Same import as the other tracks: Vorbis, normalised, not preloaded.
    private static void CopyTrackImport()
    {
        var src = (AudioImporter)AssetImporter.GetAtPath(IrokzTrack);
        var dst = (AudioImporter)AssetImporter.GetAtPath(Track);
        if (src == null || dst == null) return;
        dst.defaultSampleSettings = src.defaultSampleSettings;
        dst.forceToMono = src.forceToMono;
        dst.loadInBackground = src.loadInBackground;
        dst.SaveAndReimport();
    }
}
