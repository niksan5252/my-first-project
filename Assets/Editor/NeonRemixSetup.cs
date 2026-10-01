using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 5, "Neon Remix": three new themes copied from the old neon ones (the originals are not
/// touched) - ToxicNeon from Phonk, IceCity from Cyberpunk, GoldDisco from Club - a RunProfile for
/// each, and the NeonRemix playlist that plays them through one tunnel. Idempotent: running it again
/// re-applies the settings to the copies.
public static class NeonRemixSetup
{
    private const string Themes = "Assets/Themes/";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Settings = "Assets/Settings/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_NeonRemix.asset";
    private static readonly string[] Tracks =
    {
        "Assets/Audio/Tracks/remix_toxic_neon.wav", "Assets/Audio/Tracks/remix_ice_city.wav", "Assets/Audio/Tracks/remix_gold_disco.wav",
    };
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    [MenuItem("Tunnel/Playlist/Build Neon Remix", priority = 83)]
    public static void Build()
    {
        CopyTrackImports();
        var toxic = BuildToxicNeon();
        var ice = BuildIceCity();
        var gold = BuildGoldDisco();
        AssetDatabase.SaveAssets();

        var profiles = new[]
        {
            Profile("RunProfile_ToxicNeon", Tracks[0], "remix_toxic_neon.json", toxic),
            Profile("RunProfile_IceCity", Tracks[1], "remix_ice_city.json", ice),
            Profile("RunProfile_GoldDisco", Tracks[2], "remix_gold_disco.json", gold),
        };

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        p.title = "NeonRemix";
        p.preRoll = true;
        p.wallsInTransition = 0;
        p.finalFadeSeconds = 2.5f;
        p.thunderFlash = 0.8f;
        p.trainerMotionSet = null;
        p.trainerTextured = false;
        p.trainerPanelInThemeColors = true;
        p.segments = new List<PlaylistSegment>
        {
            Segment("ToxicNeon", profiles[0], PlaylistTransition.Crossfade, 3.5f, PlaylistTransitionLook.Veil),
            Segment("IceCity", profiles[1], PlaylistTransition.Crossfade, 3.5f, PlaylistTransitionLook.Veil),
            Segment("GoldDisco", profiles[2], PlaylistTransition.Thunder, 3.5f, PlaylistTransitionLook.Veil),
        };
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[NeonRemix] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    // --- ToxicNeon: acid green and black, violet accent, sharp teeth, green fog, flying sparks ---
    private static TunnelTheme BuildToxicNeon()
    {
        var theme = CopyTheme("Theme_Phonk", "Theme_ToxicNeon");
        var green = new Color(0.224f, 1f, 0.078f);          // #39FF14
        var violet = new Color(0.62f, 0.1f, 1f);
        theme.primaryColor = green; theme.secondaryColor = violet;
        theme.accentEmissionIntensity = 1.35f;
        theme.floorColor = new Color(0.008f, 0.014f, 0.01f);
        theme.floorGridLineColor = new Color(0.1f, 0.5f, 0.04f);
        theme.laneLineColor = green;
        theme.fogColor = new Color(0.01f, 0.09f, 0.02f);
        theme.fogStartRatio = 0.25f; theme.fogEndRatio = 0.85f;
        theme.lightColor = new Color(0.5f, 1f, 0.4f);
        theme.cameraBackgroundColor = new Color(0.003f, 0.012f, 0.005f);
        theme.hudComboTextColor = green; theme.hudBannerBorderColor = violet;

        theme.ringArchPrefab = RingPrefab("Ring_ToxicSpike", typeof(RingArchToxicSpike));
        theme.environmentPrefab = CopyPrefab("Environment_Phonk", "Environment_ToxicNeon");

        // Sparks: more, faster, bigger than the phonk embers.
        var sparks = CopyAsset<ParticlePreset>(Themes + "Presets/ParticlePreset_PhonkEmbers.asset", Themes + "Presets/ParticlePreset_ToxicSparks.asset");
        sparks.rate = 85f; sparks.speed = new Vector2(9f, 20f); sparks.size = new Vector2(0.03f, 0.09f); sparks.emissionIntensity = 1.6f;
        EditorUtility.SetDirty(sparks);
        theme.airParticles = sparks;

        theme.postProcessingProfile = CopyProfile(theme.postProcessingProfile, "ToxicNeon", new Color(0.82f, 1f, 0.8f), 1.25f);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    // --- IceCity: white, ice blue and a touch of violet; hexagon rings, wet road, no moon ---
    private static TunnelTheme BuildIceCity()
    {
        var theme = CopyTheme("Theme_Cyberpunk", "Theme_IceCity");
        var white = new Color(0.82f, 0.95f, 1f);
        var blue = new Color(0.22f, 0.68f, 1f);
        var violet = new Color(0.55f, 0.42f, 1f);
        theme.primaryColor = white; theme.secondaryColor = blue;
        theme.floorColor = new Color(0.015f, 0.025f, 0.05f);
        theme.floorMetallic = 0.9f; theme.floorSmoothness = 0.97f;       // wet road: mirrors the lights
        theme.floorGridLineColor = violet; theme.floorGridGlintIntensity = 0.7f;
        theme.laneLineColor = violet;
        theme.fogColor = new Color(0.035f, 0.06f, 0.12f);
        theme.lightColor = new Color(0.75f, 0.88f, 1f);
        theme.cameraBackgroundColor = new Color(0.02f, 0.035f, 0.07f);
        theme.hudComboTextColor = white; theme.hudBannerBorderColor = blue;

        theme.ringArchPrefab = RingPrefab("Ring_IceHex", typeof(RingArchIceHex));
        var env = CopyPrefab("Environment_Cyberpunk", "Environment_IceCity");
        var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(env));
        try
        {
            var sky = root.GetComponentInChildren<CityNightSkyDecorator>(true);
            if (sky != null) Set(sky, so => so.FindProperty("buildMoon").boolValue = false);   // no moon
            var city = root.GetComponent<CityEnvironmentDecorator>();
            if (city != null) Set(city, so =>
            {
                SetColor(so, "lampWarmColor", new Color(0.85f, 0.93f, 1f)); SetColor(so, "lampCoolColor", new Color(0.45f, 0.75f, 1f));
                SetColor(so, "windowWarmColor", new Color(0.8f, 0.9f, 1f)); SetColor(so, "windowCoolColor", new Color(0.55f, 0.6f, 1f));
                SetColor(so, "hazeColor", new Color(0.35f, 0.55f, 0.95f));
                SetColor(so, "groundColor", new Color(0.03f, 0.05f, 0.1f));
                so.FindProperty("groundSmoothness").floatValue = 0.92f;
            });
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);   // the rain component is gone: no rain in this look
            PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GetAssetPath(env));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        theme.environmentPrefab = env;

        theme.postProcessingProfile = CopyProfile(theme.postProcessingProfile, "IceCity", new Color(0.85f, 0.94f, 1f), 1.1f);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    // --- GoldDisco: gold, black, white; the club's halo rings in gold, rays, a mirror ball ---
    private static TunnelTheme BuildGoldDisco()
    {
        var theme = CopyTheme("Theme_Club", "Theme_GoldDisco");
        var gold = new Color(1f, 0.72f, 0.16f);
        var white = new Color(1f, 0.97f, 0.88f);
        theme.primaryColor = gold; theme.secondaryColor = white;
        theme.floorColor = new Color(0.012f, 0.01f, 0.006f);
        theme.floorGridLineColor = gold;
        theme.laneLineColor = gold;
        theme.fogColor = new Color(0.05f, 0.035f, 0.01f);
        theme.lightColor = new Color(1f, 0.88f, 0.6f);
        theme.cameraBackgroundColor = new Color(0.01f, 0.007f, 0.003f);
        theme.hudComboTextColor = gold; theme.hudBannerBorderColor = white;

        // The ring stays the club halo (ClubHalo takes the theme's colours through ApplyAccent).
        var env = CopyPrefab("Environment_Club", "Environment_GoldDisco");
        var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(env));
        try
        {
            if (root.GetComponent<DiscoMirrorBall>() == null) root.AddComponent<DiscoMirrorBall>();
            PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GetAssetPath(env));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        theme.environmentPrefab = env;

        theme.postProcessingProfile = CopyProfile(theme.postProcessingProfile, "GoldDisco", new Color(1f, 0.93f, 0.8f), 1.2f);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    // --- helpers ---

    private static TunnelTheme CopyTheme(string from, string to) =>
        CopyAsset<TunnelTheme>(Themes + from + ".asset", Themes + to + ".asset");

    private static T CopyAsset<T>(string from, string to) where T : Object
    {
        // An existing copy is kept and re-applied to, so the guid other assets point at stays.
        if (!File.Exists(to)) AssetDatabase.CopyAsset(from, to);
        return AssetDatabase.LoadAssetAtPath<T>(to);
    }

    private static GameObject CopyPrefab(string from, string to) =>
        CopyAsset<GameObject>(Prefabs + from + ".prefab", Prefabs + to + ".prefab");

    private static GameObject RingPrefab(string name, System.Type script)
    {
        string path = Prefabs + name + ".prefab";
        if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var go = new GameObject(name);
        go.AddComponent(script);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // A copy of the source theme's post profile with a colour filter and a stronger bloom.
    private static VolumeProfile CopyProfile(VolumeProfile source, string name, Color filter, float bloomIntensityScale)
    {
        string path = Settings + "TunnelScene_PostProcessing_" + name + ".asset";
        var copy = CopyAsset<VolumeProfile>(AssetDatabase.GetAssetPath(source), path);
        if (copy.TryGet(out ColorAdjustments ca)) { ca.colorFilter.overrideState = true; ca.colorFilter.value = filter; }
        if (copy.TryGet(out Bloom bloom) && bloom.intensity.overrideState && !AlreadyScaled(path))
            bloom.intensity.value *= bloomIntensityScale;
        EditorUtility.SetDirty(copy);
        return copy;
    }

    // Re-running must not compound the bloom scale: mark the copy once.
    private static bool AlreadyScaled(string path)
    {
        string key = "NeonRemix.bloom." + path;
        if (SessionState.GetBool(key, false)) return true;
        SessionState.SetBool(key, true);
        return false;
    }

    private static RunProfile Profile(string name, string track, string beatmap, TunnelTheme theme)
    {
        string path = Themes + "Profiles/" + name + ".asset";
        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(path);
        if (p == null) { p = ScriptableObject.CreateInstance<RunProfile>(); AssetDatabase.CreateAsset(p, path); }
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>(track);
        p.beatmapFileName = beatmap;
        p.theme = theme;
        p.preRoll = true;
        EditorUtility.SetDirty(p);
        return p;
    }

    private static PlaylistSegment Segment(string label, RunProfile profile, PlaylistTransition t, float seconds, PlaylistTransitionLook look) =>
        new PlaylistSegment
        {
            label = label, track = profile.track, beatmapFileName = profile.beatmapFileName, theme = profile.theme,
            transitionIn = t, transitionSeconds = seconds, transitionLook = look,
        };

    private static void Set(Object target, System.Action<SerializedObject> change)
    {
        var so = new SerializedObject(target);
        change(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetColor(SerializedObject so, string field, Color c)
    {
        var prop = so.FindProperty(field);
        if (prop != null) prop.colorValue = c;
        else Debug.LogWarning("[NeonRemix] no field " + field);
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
