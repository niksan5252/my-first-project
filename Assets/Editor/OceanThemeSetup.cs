using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Builds the "Ocean" underwater theme and the Ocean run profile from code, like
/// SynthwaveThemeSetup. Existing assets are updated in place; the other themes and profiles are
/// not touched, except that the scene's selector is pointed at Ocean.
public static class OceanThemeSetup
{
    private const string ThemePath = "Assets/Themes/Theme_Ocean.asset";
    private const string VolumePath = "Assets/Settings/TunnelScene_PostProcessing_Ocean.asset";
    private const string VolumeSource = "Assets/Settings/TunnelScene_PostProcessing_Cyberpunk.asset";
    private const string RingPrefabPath = "Assets/Prefabs/Ring_OceanWave.prefab";
    private const string EnvPrefabPath = "Assets/Prefabs/Environment_Ocean.prefab";
    private const string SkyPath = "Assets/Themes/Presets/OceanSky_Mat.mat";
    private const string RoadPath = "Assets/Themes/Presets/OceanCaustics_Road_Mat.mat";
    private const string SeabedPath = "Assets/Themes/Presets/OceanCaustics_Seabed_Mat.mat";
    private const string ProfilePath = "Assets/Themes/Profiles/RunProfile_Ocean.asset";
    private const string Track = "Assets/Audio/Tracks/seanyy_ocean.wav";
    private const string IrokzTrack = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";
    private const string Masks = "Assets/SharedAssets/VFX/kenney_light-masks-1.0/Transparent/";

    // The theme's two colours are what CueSpawner paints the cues with (one per hand), the walls
    // and the pads - so both are warm: coral pink and a peach orange, reading on the blue and away
    // from the cyan trainer. The sea's own aqua lives in the environment and the rings.
    public static readonly Color Coral = new Color(1f, 0.38f, 0.45f);
    public static readonly Color Peach = new Color(1f, 0.6f, 0.3f);
    public static readonly Color Aqua = new Color(0.15f, 0.85f, 0.8f);
    // The water at eye level: fog and the sky's horizon colour are the same.
    public static readonly Color Water = new Color(0.01f, 0.06f, 0.12f);
    public static readonly Color Sand = new Color(0.02f, 0.045f, 0.07f);

    [MenuItem("Tunnel/Themes/Build Ocean Theme + Ocean Profile", priority = 67)]
    public static void Build()
    {
        CopyTrackImport();
        if (!File.Exists(VolumePath)) AssetDatabase.CopyAsset(VolumeSource, VolumePath);
        var caustics = AssetDatabase.LoadAssetAtPath<Texture2D>(Masks + "water_caustics_a.png");

        var sky = LoadOrCreate(SkyPath, "Tunnel/OceanSky");
        sky.SetColor("_HorizonColor", Water);
        sky.SetTexture("_Caustics", caustics);
        EditorUtility.SetDirty(sky);

        var road = LoadOrCreate(RoadPath, "Tunnel/OceanCaustics");
        road.SetTexture("_Caustics", caustics);
        road.SetColor("_SandColor", Sand);
        road.SetColor("_LightColor", new Color(0.3f, 0.7f, 0.8f));
        road.SetFloat("_Scale", 3.5f);
        road.SetFloat("_Intensity", 0.55f);
        road.SetColor("_FadeColor", Water);
        road.SetFloat("_FadeStart", 12f);
        road.SetFloat("_FadeEnd", 110f);
        EditorUtility.SetDirty(road);

        var seabed = LoadOrCreate(SeabedPath, "Tunnel/OceanCaustics");
        seabed.SetTexture("_Caustics", caustics);
        seabed.SetColor("_SandColor", Sand * 0.8f);
        seabed.SetColor("_LightColor", new Color(0.3f, 0.75f, 0.85f));
        seabed.SetFloat("_Scale", 5f);
        seabed.SetFloat("_Intensity", 0.45f);
        seabed.SetColor("_FadeColor", Water);
        seabed.SetFloat("_FadeStart", 12f);
        seabed.SetFloat("_FadeEnd", 110f);
        EditorUtility.SetDirty(seabed);

        var ringGo = new GameObject("Ring_OceanWave");
        ringGo.AddComponent<RingArchOceanWave>();
        PrefabUtility.SaveAsPrefabAsset(ringGo, RingPrefabPath);
        Object.DestroyImmediate(ringGo);

        var envGo = new GameObject("Environment_Ocean");
        var deco = envGo.AddComponent<OceanEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        so.FindProperty("seabedMaterial").objectReferenceValue = seabed;
        so.FindProperty("glowShader").objectReferenceValue = Shader.Find("Tunnel/ClubGlow");
        so.FindProperty("jellyShader").objectReferenceValue = Shader.Find("Tunnel/OceanJelly");
        so.FindProperty("bubbleTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SharedAssets/VFX/kenney_particlePack/PNG/circle_01.png");
        so.FindProperty("tipMask").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Masks + "circle_a.png");
        var rays = so.FindProperty("rayMasks");
        string[] rayNames = { "streaks_composed_a.png", "streaks_composed_c.png", "streaks_composed_e.png", "streaks_composed_g.png" };
        rays.arraySize = rayNames.Length;
        for (int i = 0; i < rayNames.Length; i++)
            rays.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Masks + rayNames[i]);
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(envGo, EnvPrefabPath);
        Object.DestroyImmediate(envGo);

        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ThemePath);
        bool created = theme == null;
        if (created) theme = ScriptableObject.CreateInstance<TunnelTheme>();
        theme.primaryColor = Coral;
        theme.secondaryColor = Peach;
        theme.accentEmissionIntensity = 1.2f;
        theme.ringArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath);
        theme.floorMaterialOverride = road;
        theme.floorColor = Sand;
        theme.floorMetallic = 0f;
        theme.floorSmoothness = 0.3f;
        // The generator's static floor rungs would fight caustics that travel: glow = sand.
        theme.floorGridLineColor = Sand;
        theme.floorGridGlintIntensity = 1f;
        theme.laneLineColor = Aqua * 0.8f;
        theme.laneLineEmissionIntensity = 0.45f;
        theme.fogColor = Water;
        theme.fogStartRatio = 0.12f;
        theme.fogEndRatio = 0.8f;
        theme.lightIntensity = 0f;
        theme.lightColor = Color.white;
        theme.skyboxMaterial = sky;
        theme.cameraBackgroundColor = Water;
        theme.environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvPrefabPath);
        theme.airParticles = null;
        theme.postProcessingProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumePath);
        theme.hudComboTextColor = Coral;
        theme.hudBannerBorderColor = Peach;
        if (created) AssetDatabase.CreateAsset(theme, ThemePath);
        EditorUtility.SetDirty(theme);

        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(ProfilePath);
        bool newProfile = p == null;
        if (newProfile) p = ScriptableObject.CreateInstance<RunProfile>();
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>(Track);
        p.beatmapFileName = "seanyy_ocean.json";
        p.theme = theme;
        // The drums start with the track: notes from the first bar, the first set kept whole, and a
        // pre-roll so the first notes (from 1.5 s) have time to fly in.
        p.overrideIntro = true;
        p.introBars = 0f;
        p.introSkipsPartialSet = false;
        p.preRoll = true;
        if (newProfile) AssetDatabase.CreateAsset(p, ProfilePath);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();

        SelectOcean();
        Debug.Log("[OceanTheme] built " + ThemePath + " and " + ProfilePath + ", active: Ocean");
    }

    [MenuItem("Tunnel/Themes/Run Profile: Ocean", priority = 68)]
    public static void SelectOcean()
    {
        var sel = Object.FindAnyObjectByType<RunProfileSelector>();
        if (sel == null) { Debug.LogError("[OceanTheme] no RunProfileSelector in the scene"); return; }
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
