using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Builds the "Synthwave / Second Wind" theme and the SecondWind run profile from code, like
/// PhonkThemeSetup. Existing assets are updated in place; the other themes and profiles are not
/// touched, except that the scene's selector is pointed at SecondWind.
public static class SynthwaveThemeSetup
{
    private const string ThemePath = "Assets/Themes/Theme_Synthwave.asset";
    private const string VolumePath = "Assets/Settings/TunnelScene_PostProcessing_Synthwave.asset";
    private const string VolumeSource = "Assets/Settings/TunnelScene_PostProcessing_Cyberpunk.asset";
    private const string RingPrefabPath = "Assets/Prefabs/Ring_SynthPortal.prefab";
    private const string EnvPrefabPath = "Assets/Prefabs/Environment_Synthwave.prefab";
    private const string SkyPath = "Assets/Themes/Presets/SynthwaveSky_Mat.mat";
    private const string RoadPath = "Assets/Themes/Presets/SynthGrid_Road_Mat.mat";
    private const string GroundPath = "Assets/Themes/Presets/SynthGrid_Ground_Mat.mat";
    private const string MountainPath = "Assets/Themes/Presets/SynthGrid_Mountains_Mat.mat";
    private const string ProfilePath = "Assets/Themes/Profiles/RunProfile_SecondWind.asset";
    private const string Track = "Assets/Audio/Tracks/second_wind.wav";
    private const string IrokzTrack = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    // Sunset coral and a clear violet: away from the club's amber, the cyberpunk magenta/cyan and
    // the phonk crimson/white, and against the cyan trainer and the yellow words. Coral is the
    // first colour (rings, lanes), violet the second (obstacle bodies need a colour that reads
    // against the warm sunset behind them).
    public static readonly Color Coral = new Color(1f, 0.42f, 0.22f);
    public static readonly Color Violet = new Color(0.55f, 0.25f, 1f);
    // The horizon: fog and the sky's horizon colour are the same, so the road melts into it.
    public static readonly Color Horizon = new Color(0.24f, 0.05f, 0.22f);

    [MenuItem("Tunnel/Themes/Build Synthwave Theme + SecondWind Profile", priority = 65)]
    public static void Build()
    {
        CopyTrackImport();
        if (!File.Exists(VolumePath)) AssetDatabase.CopyAsset(VolumeSource, VolumePath);

        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Tunnel/SynthwaveSky"));
            AssetDatabase.CreateAsset(sky, SkyPath);
        }
        sky.shader = Shader.Find("Tunnel/SynthwaveSky");
        sky.SetColor("_HorizonColor", Horizon);
        // Centre just under the horizon: a big setting sun whose top stays below the centre words.
        sky.SetFloat("_SunRadius", 0.17f);
        sky.SetFloat("_SunElevation", -0.01f);
        EditorUtility.SetDirty(sky);

        // The road: lanes 1.5 m wide, so the grid's long lines fall on the lane lines; 3 m rungs.
        var road = GridMaterial(RoadPath, new Vector2(1.5f, 3f), 0.05f, 0.85f);
        road.SetColor("_FillColor", new Color(0.018f, 0.006f, 0.04f));
        road.SetFloat("_FadeStart", 20f);
        road.SetFloat("_FadeEnd", 150f);
        // The plain either side: bigger cells, fading out towards the horizon.
        var ground = GridMaterial(GroundPath, new Vector2(4f, 4f), 0.07f, 0.95f);
        ground.SetFloat("_FadeStart", 25f);
        ground.SetFloat("_FadeEnd", 420f);

        // The mountains: static (no scroll), a coarser mesh of lines, violet into pink with
        // distance, only partly faded so they stand against the horizon glow.
        var mountains = GridMaterial(MountainPath, new Vector2(7f, 7f), 0.35f, 0.9f);
        mountains.SetFloat("_Channel", 1f);
        mountains.SetFloat("_ScrollScale", 0f);
        mountains.SetVector("_WaveAxis", new Vector4(0f, 1f, 0f, 0f));
        mountains.SetColor("_FillColor", new Color(0.03f, 0.008f, 0.07f));
        mountains.SetColor("_LineColor", new Color(0.7f, 0.3f, 1f));
        mountains.SetColor("_LineColorFar", new Color(1f, 0.35f, 0.62f));
        mountains.SetFloat("_ColorSplit", 300f);
        mountains.SetFloat("_FadeStart", 140f);
        mountains.SetFloat("_FadeEnd", 520f);
        mountains.SetFloat("_FadeAmount", 0.55f);

        var ringGo = new GameObject("Ring_SynthPortal");
        ringGo.AddComponent<RingArchSynthPortal>();
        PrefabUtility.SaveAsPrefabAsset(ringGo, RingPrefabPath);
        Object.DestroyImmediate(ringGo);

        var envGo = new GameObject("Environment_Synthwave");
        var deco = envGo.AddComponent<SynthwaveEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        so.FindProperty("groundMaterial").objectReferenceValue = ground;
        so.FindProperty("mountainMaterial").objectReferenceValue = mountains;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(envGo, EnvPrefabPath);
        Object.DestroyImmediate(envGo);

        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ThemePath);
        bool created = theme == null;
        if (created) theme = ScriptableObject.CreateInstance<TunnelTheme>();
        theme.primaryColor = Coral;
        theme.secondaryColor = Violet;
        theme.accentEmissionIntensity = 1.3f;
        theme.ringArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath);
        theme.floorMaterialOverride = road;
        theme.floorColor = new Color(0.02f, 0.008f, 0.04f);
        theme.floorMetallic = 0.2f;
        theme.floorSmoothness = 0.6f;
        // The generator's own floor rungs stand still, which fights a grid that runs. Their glow
        // is set to the road's fill so they disappear into it.
        theme.floorGridLineColor = new Color(0.018f, 0.006f, 0.04f);
        theme.floorGridGlintIntensity = 1f;
        theme.laneLineColor = Coral;
        theme.laneLineEmissionIntensity = 0.5f;
        theme.fogColor = Horizon;
        theme.fogStartRatio = 0.3f;
        theme.fogEndRatio = 0.95f;
        theme.lightIntensity = 0f;
        theme.lightColor = Color.white;
        theme.skyboxMaterial = sky;
        theme.cameraBackgroundColor = new Color(0.03f, 0.01f, 0.08f);
        theme.environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvPrefabPath);
        theme.airParticles = null;
        theme.postProcessingProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumePath);
        theme.hudComboTextColor = Coral;
        theme.hudBannerBorderColor = Violet;
        if (created) AssetDatabase.CreateAsset(theme, ThemePath);
        EditorUtility.SetDirty(theme);

        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(ProfilePath);
        bool newProfile = p == null;
        if (newProfile) p = ScriptableObject.CreateInstance<RunProfile>();
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>(Track);
        p.beatmapFileName = "second_wind.json";
        p.theme = theme;
        if (newProfile) AssetDatabase.CreateAsset(p, ProfilePath);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();

        SelectSecondWind();
        Debug.Log("[SynthwaveTheme] built " + ThemePath + " and " + ProfilePath + ", active: SecondWind");
    }

    [MenuItem("Tunnel/Themes/Run Profile: SecondWind", priority = 66)]
    public static void SelectSecondWind()
    {
        var sel = Object.FindAnyObjectByType<RunProfileSelector>();
        if (sel == null) { Debug.LogError("[SynthwaveTheme] no RunProfileSelector in the scene"); return; }
        var sso = new SerializedObject(sel);
        sso.FindProperty("profile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RunProfile>(ProfilePath);
        sso.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(sel.gameObject.scene);
        EditorSceneManager.SaveScene(sel.gameObject.scene);
    }

    private static Material GridMaterial(string path, Vector2 cell, float lineWidth, float intensity)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Tunnel/SynthGrid"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = Shader.Find("Tunnel/SynthGrid");
        m.SetColor("_FillColor", new Color(0.022f, 0.008f, 0.05f));
        m.SetColor("_LineColor", new Color(1f, 0.32f, 0.5f));
        m.SetColor("_LineColorFar", Violet);
        m.SetFloat("_ColorSplit", 70f);
        m.SetVector("_Cell", new Vector4(cell.x, cell.y, 0f, 0f));
        m.SetFloat("_LineWidth", lineWidth);
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_Channel", 0f);
        m.SetFloat("_ScrollScale", 1f);
        m.SetVector("_WaveAxis", new Vector4(0f, 0f, 1f, 0f));
        m.SetColor("_HorizonColor", Horizon);
        m.SetFloat("_FadeAmount", 1f);
        EditorUtility.SetDirty(m);
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
