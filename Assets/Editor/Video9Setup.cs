using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 9, after the competitor's format (Docs: Анализ_конкурента_DanceMode.md): three levels on
/// the NeonPrism theme in three BRIGHT, saturated skins (light scenes, not dark ones), the filmed
/// trainer (Jennifer, solid black, mocap only), and the retention wrapper - a spoiler of four 3 s
/// clips from levels 2-3, the opening cards, cards between the levels over the frozen last frame,
/// the combo rating ladder, the outro and the end screen. The Video9 playlist is assigned in the
/// scene; record it with Playlist -> Record (the recording starts on the music). Idempotent.
public static class Video9Setup
{
    private const string Themes = "Assets/Themes/";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Settings = "Assets/Settings/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_Video9.asset";
    private const string MotionSetSource = "Assets/SharedAssets/Mixamo/JenniferMotionSet.asset";
    private const string MotionSetPath = "Assets/SharedAssets/Mixamo/JenniferFilmedMotionSet.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    private class Skin
    {
        public PrismSkin id;
        public string name, source, track, beatmap;
        public float clipEnd;
        public Color primary, secondary, floor, grid, lane, fog, light, filter;
        public float exposure, saturation, bloomScale, dropBloom, bloomPeak = 5f;
        public float rate; public Vector2 lifetime, speed, size;
    }

    // Footprints and fists by hand, as the competitor's: left cyan, right magenta, every level.
    private static readonly Color LeftHand = new Color(0f, 0.9f, 1f);
    private static readonly Color RightHand = new Color(1f, 0.1f, 0.72f);

    private static Skin[] Skins() => new[]
    {
        // 1: blue and pink neon chevrons - a deep electric blue room, pink and blue frames.
        new Skin
        {
            id = PrismSkin.CyanPulse, name = "BluePink", source = "CyanPulse", track = "track9_1", beatmap = "track9_1.json",
            primary = new Color(1f, 0.22f, 0.72f), secondary = new Color(0.25f, 0.6f, 1f),
            floor = new Color(0.03f, 0.04f, 0.14f), grid = new Color(0.35f, 0.55f, 1f), lane = new Color(1f, 0.4f, 0.8f),
            fog = new Color(0.1f, 0.16f, 0.5f), light = new Color(0.7f, 0.75f, 1f), filter = new Color(1f, 0.97f, 1f),
            exposure = 0.45f, saturation = 18f, bloomScale = 1.05f, dropBloom = 0.35f,
            rate = 45f, lifetime = new Vector2(1f, 3f), speed = new Vector2(8f, 16f), size = new Vector2(0.02f, 0.05f),
        },
        // 2: the cyber city - teal and lime, lasers on the strong beats.
        new Skin
        {
            id = PrismSkin.RedLaser, name = "CyberGreen", source = "RedLaser", track = "track9_2", beatmap = "track9_2.json",
            primary = new Color(0f, 1f, 0.85f), secondary = new Color(0.55f, 1f, 0.15f),
            floor = new Color(0.01f, 0.08f, 0.08f), grid = new Color(0.1f, 0.9f, 0.7f), lane = new Color(0.6f, 1f, 0.3f),
            fog = new Color(0.03f, 0.3f, 0.32f), light = new Color(0.65f, 1f, 0.9f), filter = new Color(0.97f, 1f, 0.98f),
            exposure = 0.4f, saturation = 15f, bloomScale = 1.05f, dropBloom = 0.4f,
            rate = 40f, lifetime = new Vector2(1f, 2.5f), speed = new Vector2(6f, 14f), size = new Vector2(0.02f, 0.05f),
        },
        // 3: every colour - a violet sky, the hue wandering phrase by phrase, the brightest drops.
        new Skin
        {
            id = PrismSkin.Spectrum, name = "Rainbow", source = "Spectrum", track = "track9_3", beatmap = "track9_3.json", clipEnd = 208.685f,
            primary = new Color(1f, 0.45f, 0.1f), secondary = new Color(0.75f, 0.25f, 1f),
            floor = new Color(0.07f, 0.03f, 0.12f), grid = new Color(0.9f, 0.6f, 1f), lane = new Color(1f, 0.85f, 0.3f),
            fog = new Color(0.3f, 0.1f, 0.5f), light = new Color(1f, 0.85f, 1f), filter = Color.white,
            exposure = 0.45f, saturation = 22f, bloomScale = 1.1f, dropBloom = 0.6f, bloomPeak = 5.5f,
            rate = 60f, lifetime = new Vector2(1f, 3f), speed = new Vector2(6f, 14f), size = new Vector2(0.03f, 0.07f),
        },
    };

    // The spoiler: the four 3 s clips that score highest on actions, rays and big moves in levels 2
    // and 3 (picked from the charts by Tools/beatmap: actions in the window + 3 x rays + 2 x big
    // moves, 20 s apart, from 25 s in), each cut half a beat before a beat.
    // false: no spoiler and no opening cards (WELCOME...) - the video opens straight on level 1.
    // The clips below stay for turning it back on.
    private const bool SpoilerIntro = false;
    private static readonly (int level, float from, float to)[] Spoiler =
    {
        (2, 101.846f, 104.977f), (2, 126.448f, 129.580f), (3, 172.691f, 175.551f), (3, 193.668f, 196.528f),
    };

    [MenuItem("Tunnel/Playlist/Build Video 9", priority = 87)]
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
        var trainer = MotionSet();

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        p.title = "Video9";
        p.preRoll = true;
        p.noteMarginBeats = 1f;
        p.wallsInTransition = 0;
        p.finalFadeSeconds = 0f;              // the tracks stop on their last strong bar, as the competitor's
        p.thunderFlash = 0.8f;
        p.trainerMotionSet = trainer;
        p.trainerTextured = false;
        p.trainerPanelInThemeColors = false;
        p.trainerFilmed = true;
        p.hideTrainer = false;                // the trainer panel: the filmed clips (VideoSilhouette)
        p.retention = true;
        p.recordFromContent = true;
        p.interludeSeconds = 13f;
        p.introCardsSeconds = 9f;
        p.outroSeconds = 26f;
        p.endScreenSeconds = 20f;
        p.loopVolume = 0.22f;
        p.loopBars = 4;
        p.welcomeText = "WELCOME TO BEAT TUNNEL";

        p.segments = new List<PlaylistSegment>();
        for (int k = 0; SpoilerIntro && k < Spoiler.Length; k++)
        {
            var (level, from, to) = Spoiler[k];
            var s = skins[level - 1];
            p.segments.Add(new PlaylistSegment
            {
                label = "Spoiler " + (k + 1), role = PlaylistSegmentRole.Spoiler,
                track = profiles[level - 1].track, beatmapFileName = s.beatmap, theme = themes[level - 1],
                clipStart = from, clipEnd = to, transitionIn = PlaylistTransition.Cut, transitionSeconds = 2f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        }
        for (int i = 0; i < skins.Length; i++)
            p.segments.Add(new PlaylistSegment
            {
                label = "Level " + (i + 1) + " " + skins[i].name, role = PlaylistSegmentRole.Level,
                track = profiles[i].track, beatmapFileName = skins[i].beatmap, theme = themes[i],
                clipStart = 0f, clipEnd = skins[i].clipEnd, transitionIn = PlaylistTransition.Interlude, transitionSeconds = 3.5f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[Video9] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    /// Jennifer with every movement from the takes alone: no hand/foot IK, a step-touch groove
    /// between notes, crossfades of ~0.2 s, the held squat pulsing on the beat.
    private static SilhouetteMotionSet MotionSet()
    {
        var set = CopyAsset<SilhouetteMotionSet>(MotionSetSource, MotionSetPath);
        set.mocapOnly = true;
        set.assistHand = 0f;
        set.assistFoot = 0f;
        set.grooveAmount = 0.55f;
        set.grooveMinBeats = 2f;
        set.grooveMaxBars = 8f;
        set.squatPulse = 0.14f;
        set.takeFadeBeats = 0.42f;            // ~0.19 s at 130 bpm
        EditorUtility.SetDirty(set);
        return set;
    }

    private static TunnelTheme BuildTheme(Skin s)
    {
        var theme = CopyAsset<TunnelTheme>(Themes + "Theme_NeonPrism_" + s.source + ".asset", Themes + "Theme_Video9_" + s.name + ".asset");
        theme.primaryColor = s.primary; theme.secondaryColor = s.secondary;
        theme.accentEmissionIntensity = 1.35f;
        theme.cueColorsByHand = true; theme.leftHandColor = LeftHand; theme.rightHandColor = RightHand;
        theme.floorColor = s.floor; theme.floorMetallic = 0.4f; theme.floorSmoothness = 0.85f;
        theme.floorGridLineColor = s.grid; theme.floorGridGlintIntensity = 0.45f;
        theme.laneLineColor = s.lane; theme.laneLineEmissionIntensity = 0.6f;
        // A light, saturated room: the fog is a bright colour and the far tunnel melts into it.
        theme.fogColor = s.fog; theme.fogStartRatio = 0.35f; theme.fogEndRatio = 0.95f;
        theme.ambientOverride = new Color(s.fog.r * 1.2f, s.fog.g * 1.2f, s.fog.b * 1.2f, 1f);
        theme.lightColor = s.light;
        theme.cameraBackgroundColor = s.fog;
        theme.skyboxMaterial = null;
        theme.hudComboTextColor = s.primary; theme.hudBannerBorderColor = s.secondary;

        theme.ringArchPrefab = Prefab("Ring_Video9_" + s.name, typeof(RingArchPrism), s.id, null);
        theme.environmentPrefab = Prefab("Environment_Video9_" + s.name, typeof(NeonPrismEnvironment), s.id,
            so => { so.FindProperty("dropBloom").floatValue = s.dropBloom; so.FindProperty("bloomPeak").floatValue = s.bloomPeak; });

        var dust = CopyAsset<ParticlePreset>(Themes + "Presets/ParticlePreset_NeonPrism_" + s.source + ".asset", Themes + "Presets/ParticlePreset_Video9_" + s.name + ".asset");
        dust.rate = s.rate; dust.lifetime = s.lifetime; dust.speed = s.speed; dust.size = s.size; dust.emissionIntensity = 1.3f;
        EditorUtility.SetDirty(dust);
        theme.airParticles = dust;

        theme.postProcessingProfile = PostProfile(s);
        EditorUtility.SetDirty(theme);
        return theme;
    }

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

    // The source skin's profile, brighter: exposure up, more saturation, the bloom scaled from the
    // ORIGINAL's value every time so a re-run does not compound it.
    private static VolumeProfile PostProfile(Skin s)
    {
        var source = AssetDatabase.LoadAssetAtPath<TunnelTheme>(Themes + "Theme_NeonPrism_" + s.source + ".asset").postProcessingProfile;
        string path = Settings + "TunnelScene_PostProcessing_Video9_" + s.name + ".asset";
        var copy = CopyAsset<VolumeProfile>(AssetDatabase.GetAssetPath(source), path);
        if (!copy.TryGet(out ColorAdjustments ca)) ca = copy.Add<ColorAdjustments>(true);
        ca.colorFilter.overrideState = true; ca.colorFilter.value = s.filter;
        ca.postExposure.overrideState = true; ca.postExposure.value = s.exposure;
        ca.saturation.overrideState = true; ca.saturation.value = s.saturation;
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
        string path = Themes + "Profiles/RunProfile_Video9_" + s.name + ".asset";
        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(path);
        if (p == null) { p = ScriptableObject.CreateInstance<RunProfile>(); AssetDatabase.CreateAsset(p, path); }
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Tracks/" + s.track + ".wav");
        p.beatmapFileName = s.beatmap;
        p.theme = theme;
        p.preRoll = true;
        p.hideTrainer = false;                // the trainer panel: the filmed clips (VideoSilhouette)
        EditorUtility.SetDirty(p);
        return p;
    }

    private static T CopyAsset<T>(string from, string to) where T : Object
    {
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

    [MenuItem("Tunnel/Playlist/Use: Video 9", priority = 97)] private static void UseVideo9() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
