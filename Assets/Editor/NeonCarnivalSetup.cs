using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 7, "Neon Carnival": one new neon theme in three looks - RioNight, Carnaval, Copacabana -
/// a RunProfile for each, and the NeonCarnival playlist that plays them through one tunnel without
/// the trainer panel. A look is its own TunnelTheme asset (palette, notes coloured by hand, floor,
/// fog, air particles, post profile) over the shared fan-arch ring and carnival environment, whose
/// prefabs carry the look and its third colour. Theme copies come from Theme_SciFi; the originals
/// are not touched. Idempotent: running it again re-applies the settings to the same assets.
public static class NeonCarnivalSetup
{
    private const string Themes = "Assets/Themes/";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Settings = "Assets/Settings/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_NeonCarnival.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    // The fan of punches reads as two hands: left cyan, right magenta, in every look.
    private static readonly Color LeftHand = new Color(0f, 0.85f, 1f);
    private static readonly Color RightHand = new Color(1f, 0.05f, 0.65f);

    private class Look
    {
        public CarnivalSkin id;
        public string name, track, beatmap;
        public Color primary, secondary, third, floor, grid, lane, fog, light, background, filter;
        public float bloomScale, dropBloom, bloomPeak = 5f;
        public float rate; public Vector2 lifetime, speed, size; public float particleEmission;
    }

    private static Look[] Looks() => new[]
    {
        new Look
        {
            id = CarnivalSkin.RioNight, name = "RioNight", track = "track7_1", beatmap = "track7_1.json",
            primary = new Color(0.1f, 1f, 0.35f), secondary = new Color(1f, 0.85f, 0.05f), third = new Color(1f, 0.95f, 0.7f),
            floor = new Color(0.004f, 0.015f, 0.006f), grid = new Color(0.08f, 0.6f, 0.2f), lane = new Color(1f, 0.85f, 0.2f),
            fog = new Color(0.01f, 0.05f, 0.03f), light = new Color(0.7f, 1f, 0.6f), background = new Color(0.003f, 0.012f, 0.01f),
            filter = new Color(0.95f, 1f, 0.9f), bloomScale = 1.15f, dropBloom = 0.4f,
            // Warm specks drifting in the night air.
            rate = 35f, lifetime = new Vector2(2f, 4f), speed = new Vector2(0.5f, 2f), size = new Vector2(0.03f, 0.07f), particleEmission = 1.2f,
        },
        new Look
        {
            id = CarnivalSkin.Carnaval, name = "Carnaval", track = "track7_2", beatmap = "track7_2.json",
            primary = new Color(1f, 0.15f, 0.6f), secondary = new Color(1f, 0.45f, 0.05f), third = new Color(0.6f, 0.15f, 1f),
            floor = new Color(0.015f, 0.004f, 0.012f), grid = new Color(0.7f, 0.1f, 0.5f), lane = new Color(1f, 0.5f, 0.1f),
            fog = new Color(0.07f, 0.01f, 0.06f), light = new Color(1f, 0.6f, 0.8f), background = new Color(0.02f, 0.003f, 0.02f),
            filter = new Color(1f, 0.94f, 0.97f), bloomScale = 1.25f, dropBloom = 0.6f, bloomPeak = 6f,
            rate = 45f, lifetime = new Vector2(1.5f, 3f), speed = new Vector2(3f, 9f), size = new Vector2(0.03f, 0.07f), particleEmission = 1.4f,
        },
        new Look
        {
            id = CarnivalSkin.Copacabana, name = "Copacabana", track = "track7_3", beatmap = "track7_3.json",
            primary = new Color(0f, 0.9f, 1f), secondary = new Color(0.95f, 0.97f, 1f), third = new Color(0.95f, 0.05f, 0.3f),
            floor = new Color(0.002f, 0.01f, 0.018f), grid = new Color(0.05f, 0.45f, 0.6f), lane = new Color(0.95f, 0.2f, 0.4f),
            fog = new Color(0f, 0.04f, 0.07f), light = new Color(0.6f, 0.95f, 1f), background = new Color(0.001f, 0.008f, 0.02f),
            filter = new Color(0.9f, 1f, 1f), bloomScale = 1.2f, dropBloom = 0.5f,
            // Sea spray: quick, small, low.
            rate = 40f, lifetime = new Vector2(1f, 2.5f), speed = new Vector2(6f, 14f), size = new Vector2(0.02f, 0.05f), particleEmission = 1.1f,
        },
    };

    [MenuItem("Tunnel/Playlist/Build Neon Carnival", priority = 85)]
    public static void Build()
    {
        var looks = Looks();
        var themes = new TunnelTheme[looks.Length];
        for (int i = 0; i < looks.Length; i++)
        {
            CopyTrackImport("Assets/Audio/Tracks/" + looks[i].track + ".wav");
            themes[i] = BuildTheme(looks[i]);
        }
        AssetDatabase.SaveAssets();

        var profiles = new RunProfile[looks.Length];
        for (int i = 0; i < looks.Length; i++) profiles[i] = Profile(looks[i], themes[i]);

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        p.title = "NeonCarnival";
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
            Segment(looks[0], profiles[0], PlaylistTransition.Crossfade, 3.5f),
            Segment(looks[1], profiles[1], PlaylistTransition.Crossfade, 3.5f),
            Segment(looks[2], profiles[2], PlaylistTransition.Thunder, 3.5f),
        };
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[NeonCarnival] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    private static TunnelTheme BuildTheme(Look s)
    {
        var theme = CopyAsset<TunnelTheme>(Themes + "Theme_SciFi.asset", Themes + "Theme_NeonCarnival_" + s.name + ".asset");
        theme.primaryColor = s.primary; theme.secondaryColor = s.secondary;
        theme.accentEmissionIntensity = 1.3f;
        theme.cueColorsByHand = true; theme.leftHandColor = LeftHand; theme.rightHandColor = RightHand;
        theme.floorColor = s.floor; theme.floorMetallic = 0.5f; theme.floorSmoothness = 0.9f;
        theme.floorGridLineColor = s.grid; theme.floorGridGlintIntensity = s.id == CarnivalSkin.Copacabana ? 0.15f : 0.3f;
        theme.laneLineColor = s.lane; theme.laneLineEmissionIntensity = 0.5f;
        theme.fogColor = s.fog; theme.fogStartRatio = 0.3f; theme.fogEndRatio = 0.9f;
        theme.lightColor = s.light;
        theme.cameraBackgroundColor = s.background;
        theme.skyboxMaterial = null;
        theme.hudComboTextColor = s.primary; theme.hudBannerBorderColor = s.secondary;

        theme.ringArchPrefab = Prefab("Ring_NeonCarnival_" + s.name, typeof(RingArchCarnival), s.id, so =>
        {
            so.FindProperty("color").colorValue = s.primary;
            so.FindProperty("secondColor").colorValue = s.secondary;
            so.FindProperty("thirdColor").colorValue = s.third;
        });
        theme.environmentPrefab = Prefab("Environment_NeonCarnival_" + s.name, typeof(NeonCarnivalEnvironment), s.id, so =>
        {
            so.FindProperty("thirdColor").colorValue = s.third;
            so.FindProperty("dropBloom").floatValue = s.dropBloom;
            so.FindProperty("bloomPeak").floatValue = s.bloomPeak;
        });

        var air = CopyAsset<ParticlePreset>(Themes + "Presets/ParticlePreset_PhonkEmbers.asset", Themes + "Presets/ParticlePreset_NeonCarnival_" + s.name + ".asset");
        air.rate = s.rate; air.lifetime = s.lifetime; air.speed = s.speed; air.size = s.size; air.emissionIntensity = s.particleEmission;
        EditorUtility.SetDirty(air);
        theme.airParticles = air;

        theme.postProcessingProfile = PostProfile(s);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    // A ring or environment prefab carrying one look, rebuilt from scratch each run (the asset keeps its guid).
    private static GameObject Prefab(string name, System.Type script, CarnivalSkin skin, System.Action<SerializedObject> more)
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
    private static VolumeProfile PostProfile(Look s)
    {
        var source = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Settings + "TunnelScene_PostProcessing_SciFi.asset");
        if (source == null) source = AssetDatabase.LoadAssetAtPath<TunnelTheme>(Themes + "Theme_SciFi.asset").postProcessingProfile;
        string path = Settings + "TunnelScene_PostProcessing_NeonCarnival_" + s.name + ".asset";
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

    private static RunProfile Profile(Look s, TunnelTheme theme)
    {
        string path = Themes + "Profiles/RunProfile_NeonCarnival_" + s.name + ".asset";
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

    private static PlaylistSegment Segment(Look s, RunProfile profile, PlaylistTransition t, float seconds) =>
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

    [MenuItem("Tunnel/Playlist/Use: Neon Carnival", priority = 95)] private static void UseCarnival() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
