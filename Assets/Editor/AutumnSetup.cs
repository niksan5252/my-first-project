using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// Video 4, "Autumn vibe": three themes and the playlist that plays them through one tunnel,
/// built from code like the Halloween ones. Segments are added as their themes exist.
public static class AutumnSetup
{
    public const string PlaylistPath = "Assets/Themes/Playlists/Playlist_Autumn.asset";
    public const string Nature = "Assets/SharedAssets/Kits3D/kenney_nature-kit/Models/";
    public const string Particles = "Assets/SharedAssets/VFX/kenney_particlePack/PNG/";
    public const string Masks = "Assets/SharedAssets/VFX/kenney_light-masks-1.0/Transparent/";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";
    public static readonly string[] Tracks =
    {
        "Assets/Audio/Tracks/navara_fallen_angel.wav", "Assets/Audio/Tracks/hester_brighter_days.wav", "Assets/Audio/Tracks/papa_khan_suffocate.wav",
    };
    public const string ForestTheme = "Assets/Themes/Theme_AutumnForest.asset";
    public const string HarvestTheme = "Assets/Themes/Theme_AutumnHarvest.asset";
    public const string RainCityTheme = "Assets/Themes/Theme_AutumnRainCity.asset";

    // --- Segment 1: the golden forest ---
    // Turquoise and raspberry cubes: the two colours furthest from gold, amber and burgundy.
    public static readonly Color Turquoise = new Color(0.1f, 0.92f, 0.85f);
    public static readonly Color Raspberry = new Color(1f, 0.18f, 0.5f);
    public static readonly Color Amber = new Color(1f, 0.6f, 0.15f);
    public static readonly Color ForestHaze = new Color(0.2f, 0.13f, 0.07f);

    [MenuItem("Tunnel/Themes/Build Autumn Forest Theme", priority = 74)]
    public static void BuildForest()
    {
        var sky = Sky("Assets/Themes/Presets/ForestSky_Mat.mat");
        sky.SetColor("_TopColor", new Color(0.12f, 0.2f, 0.38f));
        sky.SetColor("_HorizonColor", new Color(0.62f, 0.38f, 0.18f));
        sky.SetColor("_MoonColor", new Color(1f, 0.85f, 0.55f));
        sky.SetColor("_MoonHalo", new Color(1f, 0.55f, 0.2f));
        sky.SetFloat("_MoonSize", 0.1f);
        sky.SetVector("_MoonDir", new Vector4(0.2f, 0.13f, 1f, 0f));
        sky.SetColor("_CloudColor", new Color(0.55f, 0.35f, 0.25f));
        sky.SetFloat("_Stars", 0f);
        EditorUtility.SetDirty(sky);

        var ringGo = new GameObject("Ring_Branches");
        var ring = ringGo.AddComponent<RingArchBranches>();
        var rso = new SerializedObject(ring);
        rso.FindProperty("leafClump").objectReferenceValue = Model(Nature + "plant_bushLarge.fbx");
        rso.FindProperty("glowMask").objectReferenceValue = Tex(Particles + "light_01.png");
        rso.FindProperty("glowShader").objectReferenceValue = Shader.Find("Tunnel/ClubGlow");
        rso.ApplyModifiedPropertiesWithoutUndo();
        var ringPrefab = PrefabUtility.SaveAsPrefabAsset(ringGo, "Assets/Prefabs/Ring_Branches.prefab");
        Object.DestroyImmediate(ringGo);

        var envGo = new GameObject("Environment_Forest");
        var deco = envGo.AddComponent<ForestEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        CommonEnv(so);
        Col(so, "groundColor", new Color(0.16f, 0.07f, 0.03f));
        Col(so, "moonBlue", new Color(0.9f, 0.7f, 0.4f));
        Col(so, "flameOrange", Amber);
        Col(so, "stoneTint", new Color(1f, 0.92f, 0.85f));
        Int(so, "batCountOverride", 0);
        Models(so, "treeModels", "tree_oak_fall", "tree_default_fall", "tree_detailed_fall", "tree_fat_fall", "tree_cone_fall", "tree_blocks_fall");
        Models(so, "bushes", "plant_bushLarge", "plant_bushDetailed", "plant_bush", "plant_bushTriangle");
        Models(so, "groundProps", "stump_old", "stump_oldTall", "stump_roundDetailed", "log_large", "log_stack", "rock_largeA", "rock_largeC", "rock_smallB");
        Models(so, "mushrooms", "mushroom_redGroup", "mushroom_tanGroup", "mushroom_redTall");
        var rays = so.FindProperty("rayMasks");
        string[] rayNames = { "streaks_composed_a.png", "streaks_composed_c.png", "streaks_composed_e.png" };
        rays.arraySize = rayNames.Length;
        for (int i = 0; i < rayNames.Length; i++) rays.GetArrayElementAtIndex(i).objectReferenceValue = Tex(Masks + rayNames[i]);
        so.ApplyModifiedPropertiesWithoutUndo();
        var env = PrefabUtility.SaveAsPrefabAsset(envGo, "Assets/Prefabs/Environment_Forest.prefab");
        Object.DestroyImmediate(envGo);

        var theme = Theme(ForestTheme);
        theme.primaryColor = Turquoise;
        theme.secondaryColor = Raspberry;
        theme.accentEmissionIntensity = 1.2f;
        theme.ringArchPrefab = ringPrefab;
        theme.floorMaterialOverride = null;
        theme.floorColor = new Color(0.07f, 0.045f, 0.03f);
        theme.floorMetallic = 0.1f;
        theme.floorSmoothness = 0.25f;
        theme.floorGridLineColor = Amber * 0.3f;
        theme.floorGridGlintIntensity = 0.3f;
        theme.laneLineColor = Amber;
        theme.laneLineEmissionIntensity = 0.5f;
        theme.fogColor = ForestHaze;
        theme.fogStartRatio = 0.15f;
        theme.fogEndRatio = 0.9f;
        // Late-afternoon sun: the one scene in the project lit by daylight, so the directional light
        // is the sun itself - still modest, the glows and shafts carry the rest.
        theme.lightIntensity = 0.6f;
        theme.lightColor = new Color(1f, 0.8f, 0.55f);
        theme.skyboxMaterial = sky;
        theme.cameraBackgroundColor = ForestHaze;
        // Daylight: the shaded side of a trunk is warm brown, not black.
        theme.ambientOverride = new Color(0.34f, 0.26f, 0.2f, 1f);
        theme.environmentPrefab = env;
        theme.postProcessingProfile = Volume("Assets/Settings/TunnelScene_PostProcessing_Forest.asset");
        theme.hudComboTextColor = Turquoise;
        theme.hudBannerBorderColor = Raspberry;
        Save(theme, ForestTheme);
        Debug.Log("[AutumnSetup] built " + ForestTheme);
    }

    // --- Segment 2: sunset over the harvest ---
    // Royal blue and lime: the two that stand out of orange, pink and gold.
    public static readonly Color RoyalBlue = new Color(0.25f, 0.5f, 1f);
    public static readonly Color Lime = new Color(0.62f, 1f, 0.2f);
    public static readonly Color SunsetOrange = new Color(1f, 0.5f, 0.2f);
    public static readonly Color SunsetHaze = new Color(0.34f, 0.15f, 0.14f);

    [MenuItem("Tunnel/Themes/Build Autumn Harvest Theme", priority = 75)]
    public static void BuildHarvest()
    {
        var sky = Sky("Assets/Themes/Presets/HarvestSky_Mat.mat");
        sky.SetColor("_TopColor", new Color(0.2f, 0.12f, 0.32f));
        sky.SetColor("_HorizonColor", new Color(0.95f, 0.45f, 0.32f));
        // The huge sun sitting on the horizon (GraveyardSky's disc).
        sky.SetColor("_MoonColor", new Color(1f, 0.6f, 0.28f));
        sky.SetColor("_MoonHalo", new Color(1f, 0.38f, 0.3f));
        sky.SetFloat("_MoonSize", 0.3f);
        sky.SetVector("_MoonDir", new Vector4(0f, 0.08f, 1f, 0f));
        sky.SetColor("_CloudColor", new Color(0.75f, 0.35f, 0.4f));
        sky.SetFloat("_Stars", 0f);
        EditorUtility.SetDirty(sky);

        var ringGo = new GameObject("Ring_HarvestGate");
        var ring = ringGo.AddComponent<RingArchPumpkinGate>();
        var rso = new SerializedObject(ring);
        rso.FindProperty("pumpkinModel").objectReferenceValue = Model(Nature + "crop_pumpkin.fbx");
        rso.FindProperty("garlandModel").objectReferenceValue = Model(GraveyardThemeSetup.Poly + "Lanterns_On_String_5m.prefab");
        rso.FindProperty("glowMask").objectReferenceValue = Tex(Particles + "light_01.png");
        rso.FindProperty("glowShader").objectReferenceValue = Shader.Find("Tunnel/ClubGlow");
        rso.FindProperty("wood").colorValue = new Color(0.38f, 0.24f, 0.13f);
        rso.FindProperty("pumpkinGlow").colorValue = new Color(1f, 0.55f, 0.3f);
        rso.ApplyModifiedPropertiesWithoutUndo();
        var ringPrefab = PrefabUtility.SaveAsPrefabAsset(ringGo, "Assets/Prefabs/Ring_HarvestGate.prefab");
        Object.DestroyImmediate(ringGo);

        var envGo = new GameObject("Environment_Harvest");
        var deco = envGo.AddComponent<HarvestEnvironmentDecorator>();
        var so = new SerializedObject(deco);
        CommonEnv(so);
        Col(so, "groundColor", new Color(0.22f, 0.12f, 0.05f));
        Col(so, "moonBlue", new Color(1f, 0.6f, 0.45f));
        Col(so, "flameOrange", SunsetOrange);
        Col(so, "stoneTint", new Color(1f, 0.9f, 0.85f));
        Int(so, "batCountOverride", 0);
        Models(so, "wheat", "crops_wheatStageB", "crops_wheatStageA");
        Models(so, "corn", "crops_cornStageD", "crops_cornStageC");
        Models(so, "pumpkinCrops", "crop_pumpkin", GraveyardThemeSetup.Poly + "Pumpkin_Carved_Lit_A.prefab", GraveyardThemeSetup.Poly + "Pumpkin_A.prefab");
        Models(so, "pumpkins", "crop_pumpkin");
        Models(so, "haystacks", GraveyardThemeSetup.Kenney + "hay-bale.fbx", GraveyardThemeSetup.Kenney + "hay-bale-bundled.fbx");
        Obj(so, "fence", Model(Nature + "fence_simple.fbx"));
        so.ApplyModifiedPropertiesWithoutUndo();
        var env = PrefabUtility.SaveAsPrefabAsset(envGo, "Assets/Prefabs/Environment_Harvest.prefab");
        Object.DestroyImmediate(envGo);

        var theme = Theme(HarvestTheme);
        theme.primaryColor = RoyalBlue;
        theme.secondaryColor = Lime;
        theme.accentEmissionIntensity = 1.25f;
        theme.ringArchPrefab = ringPrefab;
        theme.floorMaterialOverride = null;
        theme.floorColor = new Color(0.08f, 0.045f, 0.035f);
        theme.floorMetallic = 0.1f;
        theme.floorSmoothness = 0.3f;
        theme.floorGridLineColor = SunsetOrange * 0.3f;
        theme.floorGridGlintIntensity = 0.3f;
        theme.laneLineColor = new Color(1f, 0.55f, 0.6f);
        theme.laneLineEmissionIntensity = 0.5f;
        theme.fogColor = SunsetHaze;
        theme.fogStartRatio = 0.15f;
        theme.fogEndRatio = 0.9f;
        theme.lightIntensity = 0.55f;
        theme.lightColor = new Color(1f, 0.62f, 0.4f);
        theme.skyboxMaterial = sky;
        theme.cameraBackgroundColor = SunsetHaze;
        theme.ambientOverride = new Color(0.36f, 0.22f, 0.22f, 1f);
        theme.environmentPrefab = env;
        theme.postProcessingProfile = Volume("Assets/Settings/TunnelScene_PostProcessing_Harvest.asset");
        theme.hudComboTextColor = Lime;
        theme.hudBannerBorderColor = RoyalBlue;
        Save(theme, HarvestTheme);
        Debug.Log("[AutumnSetup] built " + HarvestTheme);
    }

    // --- The playlist ---

    [MenuItem("Tunnel/Playlist/Build Autumn Playlist", priority = 82)]
    public static void BuildPlaylist()
    {
        CopyTrackImports();
        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        p.title = "AutumnVibe";
        p.preRoll = true;
        p.wallsInTransition = 0;
        p.finalFadeSeconds = 2.5f;
        p.thunderFlash = 0.8f;
        // Jennifer, the scene's own trainer, with her cyan contour; her panel takes the palette.
        p.trainerMotionSet = null;
        p.trainerTextured = false;
        p.trainerPanelInThemeColors = true;
        p.segments = new List<PlaylistSegment>();
        if (File.Exists(ForestTheme))
            p.segments.Add(new PlaylistSegment
            {
                label = "GoldenForest", track = Clip(Tracks[0]), beatmapFileName = "navara_fallen_angel.json",
                theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(ForestTheme), clipEnd = 285f,
            });
        if (File.Exists(HarvestTheme))
            p.segments.Add(new PlaylistSegment
            {
                label = "HarvestSunset", track = Clip(Tracks[1]), beatmapFileName = "hester_brighter_days.json",
                theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(HarvestTheme), clipEnd = 301f,
                transitionIn = PlaylistTransition.Crossfade, transitionSeconds = 3.5f, transitionLook = PlaylistTransitionLook.Gust,
            });
        if (File.Exists(RainCityTheme))
            p.segments.Add(new PlaylistSegment
            {
                label = "RainyCity", track = Clip(Tracks[2]), beatmapFileName = "papa_khan_suffocate.json",
                theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(RainCityTheme), clipStart = 0.5f, clipEnd = 194.2f,
                transitionIn = PlaylistTransition.Crossfade, transitionSeconds = 4f, transitionLook = PlaylistTransitionLook.Rain,
            });
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[AutumnSetup] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    [MenuItem("Tunnel/Playlist/Use: Autumn", priority = 93)] private static void UseAutumn() => HalloweenPlaylistSetup.Use(PlaylistPath);

    // --- helpers ---

    public static void CommonEnv(SerializedObject so)
    {
        Obj(so, "glowShader", Shader.Find("Tunnel/ClubGlow"));
        Obj(so, "flareMask", Tex(Particles + "light_01.png"));
        Obj(so, "flameTexture", Tex(Particles + "flame_01.png"));
        Obj(so, "smokeTexture", Tex(Particles + "smoke_07.png"));
    }

    public static Material Sky(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Tunnel/GraveyardSky")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Tunnel/GraveyardSky");
        return m;
    }

    public static UnityEngine.Rendering.VolumeProfile Volume(string path)
    {
        if (!File.Exists(path)) AssetDatabase.CopyAsset("Assets/Settings/TunnelScene_PostProcessing_Graveyard.asset", path);
        return AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(path);
    }

    public static TunnelTheme Theme(string path)
    {
        var t = AssetDatabase.LoadAssetAtPath<TunnelTheme>(path);
        return t != null ? t : ScriptableObject.CreateInstance<TunnelTheme>();
    }

    public static void Save(TunnelTheme t, string path)
    {
        if (!AssetDatabase.Contains(t)) AssetDatabase.CreateAsset(t, path);
        EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();
    }

    public static GameObject Model(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
    public static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    private static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);

    public static void Obj(SerializedObject so, string f, Object o) { var p = so.FindProperty(f); if (p != null) p.objectReferenceValue = o; else Debug.LogWarning("[AutumnSetup] no field " + f); }
    public static void Col(SerializedObject so, string f, Color c) { var p = so.FindProperty(f); if (p != null) p.colorValue = c; else Debug.LogWarning("[AutumnSetup] no field " + f); }
    public static void Int(SerializedObject so, string f, int v) { var p = so.FindProperty(f); if (p != null) p.intValue = v; else Debug.LogWarning("[AutumnSetup] no field " + f); }
    public static void Float(SerializedObject so, string f, float v) { var p = so.FindProperty(f); if (p != null) p.floatValue = v; else Debug.LogWarning("[AutumnSetup] no field " + f); }

    /// Kenney Nature Kit models by name, or full asset paths, into an array field.
    public static void Models(SerializedObject so, string field, params string[] names)
    {
        var p = so.FindProperty(field);
        p.arraySize = 0;
        foreach (var n in names)
        {
            var path = n.StartsWith("Assets/") ? n : Nature + n + ".fbx";
            var o = Model(path);
            if (o == null) { Debug.LogWarning("[AutumnSetup] missing " + path); continue; }
            p.arraySize++;
            p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = o;
        }
    }

    private static void CopyTrackImports()
    {
        var src = (AudioImporter)AssetImporter.GetAtPath(ImportTemplate);
        if (src == null) return;
        foreach (var t in Tracks)
        {
            var dst = (AudioImporter)AssetImporter.GetAtPath(t);
            if (dst == null) continue;
            var a = src.defaultSampleSettings; var b = dst.defaultSampleSettings;
            if (a.loadType == b.loadType && a.compressionFormat == b.compressionFormat && a.preloadAudioData == b.preloadAudioData) continue;
            dst.defaultSampleSettings = a;
            dst.forceToMono = src.forceToMono;
            dst.loadInBackground = src.loadInBackground;
            dst.SaveAndReimport();
        }
    }
}
