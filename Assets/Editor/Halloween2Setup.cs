using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 8, "Halloween 2": three new Halloween themes - WitchSwamp, MadLab, VampireCastle - made
/// from COPIES of the first Halloween playlist's themes (Graveyard, Pumpkin, Haunted; the originals
/// are not touched) and redesigned: their own palette, floor, fog, sky (a recoloured copy of the
/// original's GraveyardSky material), post profile and air particles, over the new ring
/// (RingArchHalloween2) and scenery (Halloween2Environment). A RunProfile for each and the
/// Halloween2 playlist that plays them through one tunnel without the trainer. Idempotent: running
/// it again re-applies the settings to the same assets.
public static class Halloween2Setup
{
    private const string Themes = "Assets/Themes/";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Settings = "Assets/Settings/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_Halloween2.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";
    private const string GlowMask = "Assets/SharedAssets/VFX/kenney_light-masks-1.0/Default/circle_a.png";

    private class Look
    {
        public Halloween2Skin id;
        public string name, source, sky, air, track, beatmap;
        public Color primary, secondary, third, floor, grid, lane, fog, light, filter;
        public float metallic, smoothness, bloomScale, dropBloom, bloomPeak = 5f;
        public Color skyTop, skyHorizon, moon, halo, clouds;
        public Vector4 moonDir; public float moonSize, stars;
        public float rate; public Vector2 lifetime, speed, size; public float particleEmission;
    }

    private static Look[] Looks() => new[]
    {
        new Look
        {
            id = Halloween2Skin.WitchSwamp, name = "WitchSwamp", source = "HalloweenGraveyard", sky = "GraveyardSky_Mat",
            air = "ParticlePreset_PhonkEmbers", track = "track8_1", beatmap = "track8_1.json",
            primary = new Color(0.62f, 0.15f, 1f), secondary = new Color(0.45f, 1f, 0.1f), third = new Color(0.7f, 1f, 0.78f),
            floor = new Color(0.012f, 0.006f, 0.02f), grid = new Color(0.32f, 0.1f, 0.5f), lane = new Color(0.45f, 1f, 0.15f),
            fog = new Color(0.05f, 0.028f, 0.08f), light = new Color(0.75f, 0.6f, 1f), filter = new Color(0.97f, 0.93f, 1f),
            metallic = 0.3f, smoothness = 0.75f, bloomScale = 1.15f, dropBloom = 0.4f,
            skyTop = new Color(0.012f, 0.004f, 0.03f), skyHorizon = new Color(0.06f, 0.035f, 0.09f, 1.4f),
            moon = new Color(0.78f, 1f, 0.62f), halo = new Color(0.3f, 0.7f, 0.2f, 0.6f), clouds = new Color(0.05f, 0.025f, 0.07f),
            moonDir = new Vector4(-0.25f, 0.22f, 1f, 0f), moonSize = 0.2f, stars = 0.8f,
            // Spores drifting over the bog: slow, small.
            rate = 30f, lifetime = new Vector2(3f, 5f), speed = new Vector2(0.2f, 0.8f), size = new Vector2(0.03f, 0.07f), particleEmission = 1.1f,
        },
        new Look
        {
            id = Halloween2Skin.MadLab, name = "MadLab", source = "HalloweenPumpkin", sky = "PumpkinSky_Mat",
            air = "ParticlePreset_ToxicSparks", track = "track8_2", beatmap = "track8_2.json",
            primary = new Color(0.2f, 1f, 0.35f), secondary = new Color(0.15f, 0.55f, 1f), third = new Color(0.85f, 0.75f, 1f),
            floor = new Color(0.004f, 0.012f, 0.02f), grid = new Color(0.1f, 0.4f, 0.8f), lane = new Color(0.25f, 1f, 0.4f),
            fog = new Color(0.01f, 0.04f, 0.06f), light = new Color(0.6f, 0.9f, 1f), filter = new Color(0.92f, 1f, 1f),
            metallic = 0.7f, smoothness = 0.85f, bloomScale = 1.2f, dropBloom = 0.5f, bloomPeak = 6f,
            skyTop = new Color(0.003f, 0.01f, 0.02f), skyHorizon = new Color(0.02f, 0.06f, 0.09f, 1.4f),
            moon = new Color(0.7f, 0.9f, 1f), halo = new Color(0.15f, 0.4f, 0.8f, 0.5f), clouds = new Color(0.02f, 0.05f, 0.08f),
            moonDir = new Vector4(0.3f, 0.3f, 1f, 0f), moonSize = 0.09f, stars = 0.6f,
            // Sparks in the air of the lab: quick and bright.
            rate = 40f, lifetime = new Vector2(0.8f, 1.8f), speed = new Vector2(4f, 10f), size = new Vector2(0.02f, 0.05f), particleEmission = 1.4f,
        },
        new Look
        {
            id = Halloween2Skin.VampireCastle, name = "VampireCastle", source = "HalloweenHaunted", sky = "HauntedSky_Mat",
            air = "ParticlePreset_PhonkEmbers", track = "track8_3", beatmap = "track8_3.json",
            primary = new Color(1f, 0.05f, 0.12f), secondary = new Color(1f, 0.72f, 0.2f), third = new Color(0.55f, 0.1f, 0.9f),
            floor = new Color(0.012f, 0.002f, 0.004f), grid = new Color(0.5f, 0.05f, 0.08f), lane = new Color(1f, 0.7f, 0.2f),
            fog = new Color(0.06f, 0.006f, 0.016f), light = new Color(1f, 0.55f, 0.5f), filter = new Color(1f, 0.93f, 0.92f),
            metallic = 0.5f, smoothness = 0.9f, bloomScale = 1.2f, dropBloom = 0.5f,
            skyTop = new Color(0.01f, 0f, 0.005f), skyHorizon = new Color(0.08f, 0.008f, 0.02f, 1.4f),
            moon = new Color(1f, 0.14f, 0.08f), halo = new Color(0.65f, 0.03f, 0.05f, 0.85f), clouds = new Color(0.05f, 0.004f, 0.012f),
            // The huge red moon, straight ahead over the road.
            moonDir = new Vector4(0f, 0.2f, 1f, 0f), moonSize = 0.36f, stars = 0.7f,
            // Embers off the candles.
            rate = 30f, lifetime = new Vector2(2f, 4f), speed = new Vector2(0.4f, 1.6f), size = new Vector2(0.03f, 0.06f), particleEmission = 1.3f,
        },
    };

    [MenuItem("Tunnel/Playlist/Build Halloween 2", priority = 86)]
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
        p.title = "Halloween2";
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
        Debug.Log("[Halloween2] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    private static TunnelTheme BuildTheme(Look s)
    {
        var theme = CopyAsset<TunnelTheme>(Themes + "Theme_" + s.source + ".asset", Themes + "Theme_Halloween2_" + s.name + ".asset");
        theme.primaryColor = s.primary; theme.secondaryColor = s.secondary;
        theme.accentEmissionIntensity = 1.3f;
        theme.floorMaterialOverride = null;
        theme.floorColor = s.floor; theme.floorMetallic = s.metallic; theme.floorSmoothness = s.smoothness;
        theme.floorGridLineColor = s.grid; theme.floorGridGlintIntensity = 0.3f;
        theme.laneLineColor = s.lane; theme.laneLineEmissionIntensity = 0.55f;
        theme.fogColor = s.fog; theme.fogStartRatio = 0.25f; theme.fogEndRatio = 0.9f;
        theme.lightColor = s.light;
        theme.cameraBackgroundColor = s.fog;
        theme.hudComboTextColor = s.primary; theme.hudBannerBorderColor = s.secondary;
        theme.gatePrefab = null;

        theme.ringArchPrefab = Prefab("Ring_Halloween2_" + s.name, typeof(RingArchHalloween2), s.id, so =>
        {
            so.FindProperty("color").colorValue = s.primary;
            so.FindProperty("secondColor").colorValue = s.secondary;
            so.FindProperty("thirdColor").colorValue = s.third;
        });
        theme.environmentPrefab = Prefab("Environment_Halloween2_" + s.name, typeof(Halloween2Environment), s.id, so =>
        {
            so.FindProperty("thirdColor").colorValue = s.third;
            so.FindProperty("dropBloom").floatValue = s.dropBloom;
            so.FindProperty("bloomPeak").floatValue = s.bloomPeak;
            so.FindProperty("glowTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(GlowMask);
        });

        var sky = CopyAsset<Material>(Themes + "Presets/" + s.sky + ".mat", Themes + "Presets/Halloween2_" + s.name + "Sky_Mat.mat");
        sky.SetColor("_TopColor", s.skyTop);
        sky.SetColor("_HorizonColor", s.skyHorizon);
        sky.SetColor("_MoonColor", s.moon);
        sky.SetColor("_MoonHalo", s.halo);
        sky.SetColor("_CloudColor", s.clouds);
        sky.SetVector("_MoonDir", s.moonDir);
        sky.SetFloat("_MoonSize", s.moonSize);
        sky.SetFloat("_Stars", s.stars);
        sky.SetFloat("_MoonPulse", 0f);
        sky.SetFloat("_Flash", 0f);
        sky.SetColor("_FlashColor", Color.Lerp(s.secondary, Color.white, 0.5f));
        EditorUtility.SetDirty(sky);
        theme.skyboxMaterial = sky;

        var air = CopyAsset<ParticlePreset>(Themes + "Presets/" + s.air + ".asset", Themes + "Presets/ParticlePreset_Halloween2_" + s.name + ".asset");
        air.rate = s.rate; air.lifetime = s.lifetime; air.speed = s.speed; air.size = s.size; air.emissionIntensity = s.particleEmission;
        EditorUtility.SetDirty(air);
        theme.airParticles = air;

        theme.postProcessingProfile = PostProfile(s);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    private static GameObject Prefab(string name, System.Type script, Halloween2Skin skin, System.Action<SerializedObject> more)
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

    // The ORIGINAL theme's post profile, copied, with a colour filter and the bloom scaled from the
    // original's value every time, so re-running does not compound it.
    private static VolumeProfile PostProfile(Look s)
    {
        var source = AssetDatabase.LoadAssetAtPath<TunnelTheme>(Themes + "Theme_" + s.source + ".asset").postProcessingProfile;
        string path = Settings + "TunnelScene_PostProcessing_Halloween2_" + s.name + ".asset";
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
        string path = Themes + "Profiles/RunProfile_Halloween2_" + s.name + ".asset";
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

    [MenuItem("Tunnel/Playlist/Use: Halloween 2", priority = 96)] private static void UseHalloween2() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
