using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Builds the "Aurora" frozen-night theme and the FeelingsFade run profile from code, like
/// OceanThemeSetup. Existing assets are updated in place; the other themes and profiles are not
/// touched, except that the scene's selector is pointed at FeelingsFade.
public static class AuroraThemeSetup
{
    private const string ThemePath = "Assets/Themes/Theme_Aurora.asset";
    private const string VolumePath = "Assets/Settings/TunnelScene_PostProcessing_Aurora.asset";
    private const string VolumeSource = "Assets/Settings/TunnelScene_PostProcessing_Cyberpunk.asset";
    private const string RingPrefabPath = "Assets/Prefabs/Ring_AuroraIce.prefab";
    private const string EnvPrefabPath = "Assets/Prefabs/Environment_Aurora.prefab";
    private const string SkyPath = "Assets/Themes/Presets/AuroraSky_Mat.mat";
    private const string IcePath = "Assets/Themes/Presets/AuroraIce_Road_Mat.mat";
    private const string SnowPath = "Assets/Themes/Presets/AuroraIce_Snow_Mat.mat";
    private const string ProfilePath = "Assets/Themes/Profiles/RunProfile_FeelingsFade.asset";
    private const string Track = "Assets/Audio/Tracks/feelings_fade.wav";
    private const string IrokzTrack = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    // The theme's two colours paint the cues (one per hand), walls and pads: a rose pink and a
    // spring green - taken from the aurora's own colours, but warm/yellow enough to stay clear of
    // the cyan trainer and to read on the dark-blue night.
    public static readonly Color Rose = new Color(1f, 0.42f, 0.7f);
    public static readonly Color Spring = new Color(0.45f, 1f, 0.4f);
    // The night at the horizon: fog and the sky's horizon colour are the same.
    public static readonly Color Night = new Color(0.02f, 0.04f, 0.09f);

    [MenuItem("Tunnel/Themes/Build Aurora Theme + FeelingsFade Profile", priority = 69)]
    public static void Build()
    {
        CopyTrackImport();
        if (!File.Exists(VolumePath)) AssetDatabase.CopyAsset(VolumeSource, VolumePath);

        var sky = LoadOrCreate(SkyPath, "Tunnel/AuroraSky");
        sky.SetColor("_HorizonColor", Night);
        EditorUtility.SetDirty(sky);

        var ice = LoadOrCreate(IcePath, "Tunnel/AuroraIce");
        ice.SetColor("_BaseColor", new Color(0.01f, 0.022f, 0.045f));
        ice.SetColor("_CrackColor", new Color(0.35f, 0.55f, 0.7f));
        ice.SetFloat("_CrackIntensity", 0.3f);
        ice.SetFloat("_CrackScale", 2.4f);
        ice.SetFloat("_Reflection", 0.7f);
        ice.SetFloat("_FresnelPower", 2.5f);
        ice.SetFloat("_Ripple", 0.03f);
        ice.SetColor("_FadeColor", Night);
        ice.SetFloat("_FadeStart", 18f);
        ice.SetFloat("_FadeEnd", 130f);
        EditorUtility.SetDirty(ice);

        var snow = LoadOrCreate(SnowPath, "Tunnel/AuroraIce");
        snow.SetColor("_BaseColor", new Color(0.05f, 0.07f, 0.11f));
        snow.SetColor("_CrackColor", new Color(0.2f, 0.25f, 0.32f));
        snow.SetFloat("_CrackIntensity", 0.08f);
        snow.SetFloat("_CrackScale", 6f);
        snow.SetFloat("_Reflection", 0.2f);
        snow.SetFloat("_FresnelPower", 4f);
        snow.SetFloat("_Ripple", 0.15f);
        snow.SetColor("_FadeColor", Night);
        snow.SetFloat("_FadeStart", 18f);
        snow.SetFloat("_FadeEnd", 180f);
        EditorUtility.SetDirty(snow);

        var ringGo = new GameObject("Ring_AuroraIce");
        ringGo.AddComponent<RingArchAuroraIce>();
        PrefabUtility.SaveAsPrefabAsset(ringGo, RingPrefabPath);
        Object.DestroyImmediate(ringGo);

        var envGo = new GameObject("Environment_Aurora");
        var deco = envGo.AddComponent<AuroraEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        so.FindProperty("snowMaterial").objectReferenceValue = snow;
        so.FindProperty("crystalShader").objectReferenceValue = Shader.Find("Tunnel/AuroraCrystal");
        so.FindProperty("snowflakeTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SharedAssets/VFX/kenney_particlePack/PNG/circle_05.png");
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(envGo, EnvPrefabPath);
        Object.DestroyImmediate(envGo);

        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ThemePath);
        bool created = theme == null;
        if (created) theme = ScriptableObject.CreateInstance<TunnelTheme>();
        theme.primaryColor = Rose;
        theme.secondaryColor = Spring;
        theme.accentEmissionIntensity = 1.15f;
        theme.ringArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath);
        theme.floorMaterialOverride = ice;
        theme.floorColor = new Color(0.01f, 0.022f, 0.045f);
        theme.floorMetallic = 0f;
        theme.floorSmoothness = 0.9f;
        // The generator's static floor rungs would sit on cracks that travel: glow = ice.
        theme.floorGridLineColor = new Color(0.01f, 0.022f, 0.045f);
        theme.floorGridGlintIntensity = 1f;
        theme.laneLineColor = new Color(0.55f, 0.75f, 0.9f);
        theme.laneLineEmissionIntensity = 0.35f;
        theme.fogColor = Night;
        theme.fogStartRatio = 0.2f;
        theme.fogEndRatio = 0.9f;
        theme.lightIntensity = 0f;
        theme.lightColor = Color.white;
        theme.skyboxMaterial = sky;
        theme.cameraBackgroundColor = Night;
        theme.environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvPrefabPath);
        theme.airParticles = null;
        theme.postProcessingProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumePath);
        theme.hudComboTextColor = Rose;
        theme.hudBannerBorderColor = Spring;
        if (created) AssetDatabase.CreateAsset(theme, ThemePath);
        EditorUtility.SetDirty(theme);

        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(ProfilePath);
        bool newProfile = p == null;
        if (newProfile) p = ScriptableObject.CreateInstance<RunProfile>();
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>(Track);
        p.beatmapFileName = "feelings_fade.json";
        p.theme = theme;
        // Notes from the start of the track, like Ocean: no intro bars, the first set kept whole,
        // and a pre-roll so the first notes have time to fly in.
        p.overrideIntro = true;
        p.introBars = 0f;
        p.introSkipsPartialSet = false;
        p.preRoll = true;
        if (newProfile) AssetDatabase.CreateAsset(p, ProfilePath);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();

        SelectFeelingsFade();
        Debug.Log("[AuroraTheme] built " + ThemePath + " and " + ProfilePath + ", active: FeelingsFade");
    }

    [MenuItem("Tunnel/Themes/Run Profile: FeelingsFade", priority = 70)]
    public static void SelectFeelingsFade()
    {
        var sel = Object.FindAnyObjectByType<RunProfileSelector>();
        if (sel == null) { Debug.LogError("[AuroraTheme] no RunProfileSelector in the scene"); return; }
        var sso = new SerializedObject(sel);
        sso.FindProperty("profile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RunProfile>(ProfilePath);
        sso.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(sel.gameObject.scene);
        EditorSceneManager.SaveScene(sel.gameObject.scene);
    }

    private static Material LoadOrCreate(string path, string shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = Shader.Find(shader);
        return m;
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
