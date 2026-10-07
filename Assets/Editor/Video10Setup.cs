using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 10 "Cosmic Rush": three levels in space on one procedural theme (CosmicEnvironment +
/// RingArchCosmic) in three skins - OrbitStation (maximum-energy, easy), AsteroidStorm (final-drop,
/// medium), NebulaHyperdrive (big-finish, hard) - in Video 9's format: retention cards between the
/// levels, rating ladder, outro and end screen, no spoiler/WELCOME intro. Built on copies of the
/// Video 9 assets (theme, dust, post profile) so the HUD/notes stay as tuned there. Idempotent.
public static class Video10Setup
{
    private const string Themes = "Assets/Themes/";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Settings = "Assets/Settings/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_Video10.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    private class Skin
    {
        public CosmicSkin id;
        public string name, source, track, beatmap;
        public Color primary, secondary, floor, grid, lane, fog, light, filter;
        public float lightIntensity, exposure, saturation, bloomScale, dropBloom;
        public float rate; public Vector2 lifetime, speed, size;
    }

    private static readonly Color LeftHand = new Color(0f, 0.9f, 1f);
    private static readonly Color RightHand = new Color(1f, 0.1f, 0.72f);

    private static Skin[] Skins() => new[]
    {
        new Skin
        {
            id = CosmicSkin.OrbitStation, name = "OrbitStation", source = "BluePink", track = "track10_1", beatmap = "track10_1.json",
            primary = new Color(0f, 0.85f, 1f), secondary = new Color(0.62f, 0.25f, 1f),
            floor = new Color(0.01f, 0.015f, 0.06f), grid = new Color(0.2f, 0.7f, 1f), lane = new Color(0.7f, 0.4f, 1f),
            fog = new Color(0.02f, 0.035f, 0.13f), light = new Color(0.7f, 0.8f, 1f), filter = new Color(0.97f, 0.98f, 1f),
            lightIntensity = 0.15f, exposure = 0.25f, saturation = 15f, bloomScale = 1.05f, dropBloom = 0.35f,
            rate = 35f, lifetime = new Vector2(1f, 3f), speed = new Vector2(8f, 16f), size = new Vector2(0.02f, 0.05f),
        },
        new Skin
        {
            id = CosmicSkin.AsteroidStorm, name = "AsteroidStorm", source = "CyberGreen", track = "track10_2", beatmap = "track10_2.json",
            primary = new Color(1f, 0.42f, 0.05f), secondary = new Color(1f, 0.12f, 0.2f),
            floor = new Color(0.06f, 0.015f, 0.01f), grid = new Color(1f, 0.45f, 0.1f), lane = new Color(1f, 0.25f, 0.2f),
            fog = new Color(0.14f, 0.035f, 0.02f), light = new Color(1f, 0.6f, 0.35f), filter = new Color(1f, 0.97f, 0.94f),
            lightIntensity = 0.55f, exposure = 0.3f, saturation = 18f, bloomScale = 1.1f, dropBloom = 0.45f,
            rate = 55f, lifetime = new Vector2(0.8f, 2f), speed = new Vector2(10f, 20f), size = new Vector2(0.02f, 0.06f),
        },
        new Skin
        {
            id = CosmicSkin.NebulaHyperdrive, name = "NebulaHyperdrive", source = "Rainbow", track = "track10_3", beatmap = "track10_3.json",
            primary = new Color(1f, 0.3f, 0.75f), secondary = new Color(1f, 0.78f, 0.3f),
            floor = new Color(0.05f, 0.02f, 0.08f), grid = new Color(1f, 0.5f, 0.85f), lane = new Color(1f, 0.82f, 0.4f),
            fog = new Color(0.2f, 0.05f, 0.24f), light = new Color(1f, 0.85f, 1f), filter = Color.white,
            lightIntensity = 0.2f, exposure = 0.4f, saturation = 22f, bloomScale = 1.15f, dropBloom = 0.6f,
            rate = 70f, lifetime = new Vector2(1f, 3f), speed = new Vector2(10f, 22f), size = new Vector2(0.03f, 0.07f),
        },
    };

    [MenuItem("Tunnel/Playlist/Build Video 10 (Cosmic Rush)", priority = 88)]
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
        var tracks = new AudioClip[skins.Length];
        for (int i = 0; i < skins.Length; i++) tracks[i] = Profile(skins[i], themes[i]).track;

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        var v9 = AssetDatabase.LoadAssetAtPath<RunPlaylist>(Video9Setup.PlaylistPath);
        p.title = "Video10";
        p.preRoll = true;
        p.noteMarginBeats = 1f;
        p.wallsInTransition = 0;
        p.finalFadeSeconds = 0f;
        p.thunderFlash = 0.8f;
        // The trainer: Jennifer, solid black, on the new mocap (MocapImport - our clips + CMU studio takes).
        var mocap = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(MocapImport.SetPath);
        p.trainerMotionSet = mocap != null ? mocap : v9 != null ? v9.trainerMotionSet : null;
        p.trainerFilmed = false;
        p.trainerMocap3D = true;
        p.hideTrainer = true;                 // no trainer panel in this video (the user's call)
        p.trainerTextured = false;
        p.trainerPanelInThemeColors = false;
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
        for (int i = 0; i < skins.Length; i++)
            p.segments.Add(new PlaylistSegment
            {
                label = "Level " + (i + 1) + " " + skins[i].name, role = PlaylistSegmentRole.Level,
                track = tracks[i], beatmapFileName = skins[i].beatmap, theme = themes[i],
                // One continuous run: the tracks crossfade inside the tunnel while the look changes under the veil.
                clipStart = 0f, clipEnd = 0f, transitionIn = PlaylistTransition.Crossfade, transitionSeconds = 4f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[Video10] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    private static TunnelTheme BuildTheme(Skin s)
    {
        var theme = CopyAsset<TunnelTheme>(Themes + "Theme_Video9_" + s.source + ".asset", Themes + "Theme_Cosmic_" + s.name + ".asset");
        theme.primaryColor = s.primary; theme.secondaryColor = s.secondary;
        theme.accentEmissionIntensity = 1.35f;
        theme.cueColorsByHand = true; theme.leftHandColor = LeftHand; theme.rightHandColor = RightHand;
        theme.floorColor = s.floor; theme.floorMetallic = 0.1f; theme.floorSmoothness = 0.55f;     // dark glass, not a mirror of the bright default sky
        theme.floorGridLineColor = s.grid; theme.floorGridGlintIntensity = 0.4f;
        theme.laneLineColor = s.lane; theme.laneLineEmissionIntensity = 0.6f;
        theme.fogColor = s.fog; theme.fogStartRatio = 0.4f; theme.fogEndRatio = 1f;
        theme.ambientOverride = new Color(s.fog.r * 1.5f + 0.03f, s.fog.g * 1.5f + 0.03f, s.fog.b * 1.5f + 0.04f, 1f);
        theme.lightIntensity = s.lightIntensity;
        theme.lightColor = s.light;
        theme.cameraBackgroundColor = s.fog;
        theme.skyboxMaterial = null;           // the star dome and planets are CosmicEnvironment's own, beyond the fog
        theme.hudComboTextColor = s.primary; theme.hudBannerBorderColor = s.secondary;

        theme.ringArchPrefab = Prefab("Ring_Cosmic_" + s.name, typeof(RingArchCosmic), s.id, null);
        theme.environmentPrefab = Prefab("Environment_Cosmic_" + s.name, typeof(CosmicEnvironment), s.id,
            so => so.FindProperty("dropBloom").floatValue = s.dropBloom);
        theme.gatePrefab = null;

        var dust = CopyAsset<ParticlePreset>(Themes + "Presets/ParticlePreset_Video9_" + s.source + ".asset", Themes + "Presets/ParticlePreset_Cosmic_" + s.name + ".asset");
        dust.rate = s.rate; dust.lifetime = s.lifetime; dust.speed = s.speed; dust.size = s.size; dust.emissionIntensity = 1.3f;
        EditorUtility.SetDirty(dust);
        theme.airParticles = dust;

        theme.postProcessingProfile = PostProfile(s);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    private static GameObject Prefab(string name, System.Type script, CosmicSkin skin, System.Action<SerializedObject> more)
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

    private static VolumeProfile PostProfile(Skin s)
    {
        var source = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Settings + "TunnelScene_PostProcessing_Video9_" + s.source + ".asset");
        string path = Settings + "TunnelScene_PostProcessing_Cosmic_" + s.name + ".asset";
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
        string path = Themes + "Profiles/RunProfile_Cosmic_" + s.name + ".asset";
        var p = AssetDatabase.LoadAssetAtPath<RunProfile>(path);
        if (p == null) { p = ScriptableObject.CreateInstance<RunProfile>(); AssetDatabase.CreateAsset(p, path); }
        p.track = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Tracks/" + s.track + ".wav");
        p.beatmapFileName = s.beatmap;
        p.theme = theme;
        p.preRoll = true;
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
        dst.defaultSampleSettings = src.defaultSampleSettings;
        dst.forceToMono = src.forceToMono;
        dst.loadInBackground = src.loadInBackground;
        dst.SaveAndReimport();
    }

    /// Every button and combination on silence (Tools/beatmap/showcase.py), the mocap trainer on,
    /// to pick the trainer's movement for each cue. Schedule: Desktop/витрина_кнопок.txt.
    public const string ShowcasePath = Themes + "Playlists/Playlist_Showcase.asset";

    [MenuItem("Tunnel/Playlist/Build Button Showcase", priority = 89)]
    public static void BuildShowcase()
    {
        const string track = "Assets/Audio/Tracks/showcase_silent.wav";
        AssetDatabase.ImportAsset(track);
        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(ShowcasePath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, ShowcasePath); }
        p.title = "Showcase";
        p.preRoll = true;
        p.retention = false;
        p.finalFadeSeconds = 0f;
        // The Mixamo takes on Jennifer (Video 9's set) - smooth; the video mocap set read as jerky.
        p.trainerMotionSet = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>("Assets/SharedAssets/Mixamo/JenniferFilmedMotionSet.asset");
        if (p.trainerMotionSet != null)
        {
            // Outer lanes (1 and 4): the punch always goes straight out to the side.
            p.trainerMotionSet.outerPunchSide = true;
            p.trainerMotionSet.highPunchUp = true;      // high notes: the fist straight up
            p.trainerMotionSet.outerKickWide = true;    // outer-lane steps: the foot out wide
            p.trainerMotionSet.kickOutOuter = 0.65f;
            p.trainerMotionSet.kickBodyFollowOuter = 0.35f;
            p.trainerMotionSet.punchOutOuter = 0.55f;
            // Punches: the straight jab only (and its mirror for the other hand) - the cross and the
            // hook wind up oddly.
            p.trainerMotionSet.punches.RemoveAll(m => m.clip == null || !m.clip.name.StartsWith("Lead Jab"));
            // High punches and held fists too: the jab, not the uppercuts' wind-up.
            p.trainerMotionSet.uppercuts = new List<SilhouetteMotion>(p.trainerMotionSet.punches);
            // Steps: the plain marching step from our Gemini clip ("марширует", mocap - clean alternate marks; "степ ногой" froze), not the Mixamo
            // dance takes. Those clips carry no foot IK goals, so foot IK is off for the set.
            var mocapSet = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(MocapImport.SetPath);
            if (mocapSet != null)
            {
                var steps = mocapSet.steps.Where(m => m.clip != null && m.clip.name.StartsWith("March")).ToList();
                if (steps.Count > 0) { p.trainerMotionSet.steps = steps; p.trainerMotionSet.noFootIK = true; }
            }
            EditorUtility.SetDirty(p.trainerMotionSet);
        }
        p.trainerTextured = false;
        p.trainerFilmed = false;
        p.trainerMocap3D = true;
        p.hideTrainer = false;
        p.segments = new List<PlaylistSegment>
        {
            new PlaylistSegment
            {
                label = "Showcase", role = PlaylistSegmentRole.Level,
                track = AssetDatabase.LoadAssetAtPath<AudioClip>(track), beatmapFileName = "showcase.json",
                theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(Themes + "Theme_Cosmic_OrbitStation.asset"),
                transitionIn = PlaylistTransition.Cut,
            },
        };
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(ShowcasePath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[Showcase] built " + ShowcasePath);
    }

    [MenuItem("Tunnel/Playlist/Use: Video 10", priority = 98)] private static void UseVideo10() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
