using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 6, "Neon Prism": one new neon theme in four skins - RedLaser, Ultraviolet, CyanPulse,
/// Spectrum - a RunProfile for each, and the NeonPrism playlist that plays them through one tunnel
/// without the trainer panel. A skin is its own TunnelTheme asset (palette, floor, fog, air
/// particles, post profile) over the shared prism ring and environment components, whose prefabs
/// carry the skin's accent. The theme copies come from Theme_SciFi; the originals are not touched.
/// Idempotent: running it again re-applies the settings to the same assets.
public static class NeonPrismSetup
{
    private const string Themes = "Assets/Themes/";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Settings = "Assets/Settings/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_NeonPrism.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    private class Skin
    {
        public PrismSkin id;
        public string name, track, beatmap;
        public Color primary, secondary, floor, grid, lane, fog, light, background, filter;
        public float bloomScale, dropBloom, bloomPeak = 5f;
        public float rate; public Vector2 lifetime, speed, size; public float particleEmission;
    }

    private static Skin[] Skins() => new[]
    {
        new Skin
        {
            id = PrismSkin.RedLaser, name = "RedLaser", track = "prism_club_energy_1", beatmap = "prism_club_energy_1.json",
            primary = new Color(1f, 0.04f, 0.07f), secondary = new Color(1f, 0.94f, 0.94f),
            floor = new Color(0.012f, 0.004f, 0.004f), grid = new Color(0.9f, 0.05f, 0.07f), lane = new Color(1f, 0.9f, 0.9f),
            fog = new Color(0.09f, 0f, 0.012f), light = new Color(1f, 0.55f, 0.55f), background = new Color(0.01f, 0f, 0.002f),
            filter = new Color(1f, 0.92f, 0.92f), bloomScale = 1.15f, dropBloom = 0.5f,
            rate = 30f, lifetime = new Vector2(1f, 2.5f), speed = new Vector2(6f, 14f), size = new Vector2(0.02f, 0.05f), particleEmission = 1.2f,
        },
        new Skin
        {
            id = PrismSkin.Ultraviolet, name = "Ultraviolet", track = "prism_club_energy_2", beatmap = "prism_club_energy_2.json",
            primary = new Color(0.42f, 0.04f, 1f), secondary = new Color(0.88f, 0.3f, 1f),
            floor = new Color(0.01f, 0.005f, 0.025f), grid = new Color(0.3f, 0.05f, 0.8f), lane = new Color(0.6f, 0.3f, 1f),
            fog = new Color(0.04f, 0f, 0.1f), light = new Color(0.55f, 0.35f, 1f), background = new Color(0.01f, 0f, 0.03f),
            filter = new Color(0.92f, 0.86f, 1f), bloomScale = 1.2f, dropBloom = 0.4f,
            // The dust: slow, small, long-lived dots drifting through the room.
            rate = 70f, lifetime = new Vector2(3f, 6f), speed = new Vector2(0.4f, 1.6f), size = new Vector2(0.03f, 0.09f), particleEmission = 1.5f,
        },
        new Skin
        {
            id = PrismSkin.CyanPulse, name = "CyanPulse", track = "prism_club_energy_3", beatmap = "prism_club_energy_3.json",
            primary = new Color(0f, 1f, 0.82f), secondary = new Color(0.1f, 0.38f, 1f),
            floor = new Color(0f, 0.015f, 0.02f), grid = new Color(0f, 0.7f, 0.8f), lane = new Color(0.2f, 0.6f, 1f),
            fog = new Color(0f, 0.05f, 0.08f), light = new Color(0.5f, 0.95f, 1f), background = new Color(0f, 0.012f, 0.02f),
            filter = new Color(0.86f, 1f, 1f), bloomScale = 1.15f, dropBloom = 0.4f,
            rate = 40f, lifetime = new Vector2(1f, 3f), speed = new Vector2(8f, 18f), size = new Vector2(0.02f, 0.05f), particleEmission = 1.1f,
        },
        new Skin
        {
            id = PrismSkin.Spectrum, name = "Spectrum", track = "prism_f_minor_drop", beatmap = "prism_f_minor_drop.json",
            primary = new Color(1f, 0.15f, 0.75f), secondary = new Color(0.1f, 0.9f, 1f),
            floor = new Color(0.01f, 0.01f, 0.015f), grid = new Color(0.55f, 0.55f, 0.85f), lane = new Color(0.9f, 0.9f, 1f),
            fog = new Color(0.03f, 0.03f, 0.06f), light = new Color(0.9f, 0.9f, 1f), background = new Color(0.005f, 0.005f, 0.012f),
            filter = Color.white, bloomScale = 1.3f, dropBloom = 1f, bloomPeak = 6f,
            rate = 60f, lifetime = new Vector2(1f, 3f), speed = new Vector2(6f, 14f), size = new Vector2(0.03f, 0.07f), particleEmission = 1.5f,
        },
    };

    [MenuItem("Tunnel/Playlist/Build Neon Prism", priority = 84)]
    public static void Build()
    {
        var skins = Skins();
        var themes = new TunnelTheme[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            CopyTrackImport("Assets/Audio/Tracks/" + skins[i].track + ".wav");
            themes[i] = BuildTheme(skins[i]);
        }
        AssetDatabase.SaveAssets();

        var profiles = new RunProfile[skins.Length];
        for (int i = 0; i < skins.Length; i++) profiles[i] = Profile(skins[i], themes[i]);

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        p.title = "NeonPrism";
        p.preRoll = true;
        p.wallsInTransition = 0;
        p.finalFadeSeconds = 2.5f;
        p.thunderFlash = 0.8f;
        p.trainerMotionSet = null;
        p.trainerTextured = false;
        p.trainerPanelInThemeColors = false;
        p.hideTrainer = true;
        p.segments = new List<PlaylistSegment>
        {
            Segment(skins[0], profiles[0], PlaylistTransition.Crossfade, 3.5f),
            Segment(skins[1], profiles[1], PlaylistTransition.Crossfade, 3.5f),
            Segment(skins[2], profiles[2], PlaylistTransition.Thunder, 3.5f),
            Segment(skins[3], profiles[3], PlaylistTransition.Crossfade, 4f),
        };
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[NeonPrism] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    private static TunnelTheme BuildTheme(Skin s)
    {
        var theme = CopyAsset<TunnelTheme>(Themes + "Theme_SciFi.asset", Themes + "Theme_NeonPrism_" + s.name + ".asset");
        theme.primaryColor = s.primary; theme.secondaryColor = s.secondary;
        theme.accentEmissionIntensity = 1.3f;
        theme.floorColor = s.floor; theme.floorMetallic = 0.5f; theme.floorSmoothness = 0.9f;
        theme.floorGridLineColor = s.grid; theme.floorGridGlintIntensity = 0.3f;
        theme.laneLineColor = s.lane; theme.laneLineEmissionIntensity = 0.5f;
        theme.fogColor = s.fog; theme.fogStartRatio = 0.3f; theme.fogEndRatio = 0.9f;
        theme.lightColor = s.light;
        theme.cameraBackgroundColor = s.background;
        theme.skyboxMaterial = null;
        theme.hudComboTextColor = s.primary; theme.hudBannerBorderColor = s.secondary;

        theme.ringArchPrefab = Prefab("Ring_NeonPrism_" + s.name, typeof(RingArchPrism), s.id, null);
        theme.environmentPrefab = Prefab("Environment_NeonPrism_" + s.name, typeof(NeonPrismEnvironment), s.id,
            so => { so.FindProperty("dropBloom").floatValue = s.dropBloom; so.FindProperty("bloomPeak").floatValue = s.bloomPeak; });

        var dust = CopyAsset<ParticlePreset>(Themes + "Presets/ParticlePreset_PhonkEmbers.asset", Themes + "Presets/ParticlePreset_NeonPrism_" + s.name + ".asset");
        dust.rate = s.rate; dust.lifetime = s.lifetime; dust.speed = s.speed; dust.size = s.size; dust.emissionIntensity = s.particleEmission;
        EditorUtility.SetDirty(dust);
        theme.airParticles = dust;

        theme.postProcessingProfile = PostProfile(s);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    // A ring or environment prefab carrying one skin, rebuilt from scratch each run (the asset keeps its guid).
    private static GameObject Prefab(string name, System.Type script, PrismSkin skin, System.Action<SerializedObject> more)
    {
        string path = Prefabs + name + ".prefab";
        var go = new GameObject(name);
        var comp = go.AddComponent(script);
        var so = new SerializedObject(comp);
        so.FindProperty("skin").enumValueIndex = (int)skin;
        more?.Invoke(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // The SciFi profile with a colour filter and a stronger bloom; the scale is applied to the
    // ORIGINAL's value every time, so re-running does not compound it.
    private static VolumeProfile PostProfile(Skin s)
    {
        var source = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Settings + "TunnelScene_PostProcessing_SciFi.asset");
        if (source == null) source = AssetDatabase.LoadAssetAtPath<TunnelTheme>(Themes + "Theme_SciFi.asset").postProcessingProfile;
        string path = Settings + "TunnelScene_PostProcessing_NeonPrism_" + s.name + ".asset";
        var copy = CopyAsset<VolumeProfile>(AssetDatabase.GetAssetPath(source), path);
        if (copy.TryGet(out ColorAdjustments ca)) { ca.colorFilter.overrideState = true; ca.colorFilter.value = s.filter; }
        if (copy.TryGet(out Bloom bloom) && source.TryGet(out Bloom original) && original.intensity.overrideState)
        {
            bloom.intensity.overrideState = true;
            bloom.intensity.value = original.intensity.value * s.bloomScale;
        }
        EditorUtility.SetDirty(copy);
        return copy;
    }

    private static RunProfile Profile(Skin s, TunnelTheme theme)
    {
        string path = Themes + "Profiles/RunProfile_NeonPrism_" + s.name + ".asset";
        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(path);
        if (p == null) { p = ScriptableObject.CreateInstance<RunProfile>(); AssetDatabase.CreateAsset(p, path); }
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Tracks/" + s.track + ".wav");
        p.beatmapFileName = s.beatmap;
        p.theme = theme;
        p.preRoll = true;
        p.hideTrainer = true;
        EditorUtility.SetDirty(p);
        return p;
    }

    private static PlaylistSegment Segment(Skin s, RunProfile profile, PlaylistTransition t, float seconds) =>
        new PlaylistSegment
        {
            label = s.name, track = profile.track, beatmapFileName = profile.beatmapFileName, theme = profile.theme,
            transitionIn = t, transitionSeconds = seconds, transitionLook = PlaylistTransitionLook.Veil,
        };

    private static T CopyAsset<T>(string from, string to) where T : Object
    {
        // An existing copy is kept and re-applied to, so the guid other assets point at stays.
        if (!File.Exists(to)) AssetDatabase.CopyAsset(from, to);
        return AssetDatabase.LoadAssetAtPath<T>(to);
    }

    // The new tracks load the way the other tracks do.
    private static void CopyTrackImport(string track)
    {
        var src = (AudioImporter)AssetImporter.GetAtPath(ImportTemplate);
        var dst = (AudioImporter)AssetImporter.GetAtPath(track);
        if (src == null || dst == null) return;
        var a = src.defaultSampleSettings; var b = dst.defaultSampleSettings;
        if (a.loadType == b.loadType && a.compressionFormat == b.compressionFormat && a.preloadAudioData == b.preloadAudioData) return;
        dst.defaultSampleSettings = a;
        dst.forceToMono = src.forceToMono;
        dst.loadInBackground = src.loadInBackground;
        dst.SaveAndReimport();
    }

    [MenuItem("Tunnel/Playlist/Use: Neon Prism", priority = 94)] private static void UsePrism() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
