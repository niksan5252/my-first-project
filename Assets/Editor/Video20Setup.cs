using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 20 "Halloween Night": seven Halloween worlds on FiestaEnvironment (FiestaEnvironment.Halloween.cs) - Pumpkin, Haunted, Witch, Zombie, Vampire, Swamp, Monster - the V5 trainer, tunnel x1.75 with a 75 deg lens. Tracks track20_1..7 are Desktop/треки/20 видео with the quiet intros cut and the short ones looped on the bar to about a minute; charts from deck20-1..7. Idempotent.
public static class Video20Setup
{
    private const string Themes = "Assets/Themes/";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Settings = "Assets/Settings/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_Video20.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    private class Skin
    {
        public FiestaSkin id;
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
            id = FiestaSkin.Pumpkin, name = "Pumpkin", source = "Rainbow", track = "track20_1", beatmap = "track20_1.json",
            primary = new Color(1f, 0.5f, 0.05f), secondary = new Color(0.6f, 0.2f, 1f),
            floor = new Color(0.04f, 0.025f, 0.02f), grid = new Color(1f, 0.5f, 0.1f), lane = new Color(0.65f, 0.3f, 1f),
            fog = new Color(0.1f, 0.05f, 0.1f), light = new Color(0.9f, 0.85f, 1f), filter = new Color(1f, 0.97f, 0.97f),
            lightIntensity = 0.45f, exposure = 0.35f, saturation = 20f, bloomScale = 1.15f, dropBloom = 0.55f,
            rate = 30f, lifetime = new Vector2(1.5f, 3.5f), speed = new Vector2(1f, 3f), size = new Vector2(0.02f, 0.05f),
        },
        new Skin
        {
            id = FiestaSkin.Haunted, name = "Haunted", source = "BluePink", track = "track20_2", beatmap = "track20_2.json",
            primary = new Color(0.55f, 1f, 0.95f), secondary = new Color(0.6f, 0.3f, 1f),
            floor = new Color(0.02f, 0.03f, 0.04f), grid = new Color(0.5f, 1f, 0.9f), lane = new Color(0.65f, 0.35f, 1f),
            fog = new Color(0.04f, 0.07f, 0.08f), light = new Color(0.9f, 0.85f, 1f), filter = new Color(1f, 0.97f, 0.97f),
            lightIntensity = 0.4f, exposure = 0.4f, saturation = 16f, bloomScale = 1.15f, dropBloom = 0.55f,
            rate = 30f, lifetime = new Vector2(1.5f, 3.5f), speed = new Vector2(1f, 3f), size = new Vector2(0.02f, 0.05f),
        },
        new Skin
        {
            id = FiestaSkin.Witch, name = "Witch", source = "CyberGreen", track = "track20_3", beatmap = "track20_3.json",
            primary = new Color(0.3f, 1f, 0.3f), secondary = new Color(0.7f, 0.3f, 1f),
            floor = new Color(0.02f, 0.02f, 0.04f), grid = new Color(0.35f, 1f, 0.35f), lane = new Color(0.75f, 0.35f, 1f),
            fog = new Color(0.05f, 0.03f, 0.09f), light = new Color(0.9f, 0.85f, 1f), filter = new Color(1f, 0.97f, 0.97f),
            lightIntensity = 0.4f, exposure = 0.4f, saturation = 18f, bloomScale = 1.15f, dropBloom = 0.55f,
            rate = 30f, lifetime = new Vector2(1.5f, 3.5f), speed = new Vector2(1f, 3f), size = new Vector2(0.02f, 0.05f),
        },
        new Skin
        {
            id = FiestaSkin.Zombie, name = "Zombie", source = "CyberGreen", track = "track20_4", beatmap = "track20_4.json",
            primary = new Color(1f, 0.6f, 0.15f), secondary = new Color(0.5f, 1f, 0.2f),
            floor = new Color(0.03f, 0.03f, 0.03f), grid = new Color(1f, 0.6f, 0.15f), lane = new Color(0.55f, 1f, 0.25f),
            fog = new Color(0.07f, 0.08f, 0.04f), light = new Color(0.9f, 0.85f, 1f), filter = new Color(1f, 0.97f, 0.97f),
            lightIntensity = 0.4f, exposure = 0.4f, saturation = 16f, bloomScale = 1.15f, dropBloom = 0.55f,
            rate = 30f, lifetime = new Vector2(1.5f, 3.5f), speed = new Vector2(1f, 3f), size = new Vector2(0.02f, 0.05f),
        },
        new Skin
        {
            id = FiestaSkin.Vampire, name = "Vampire", source = "BluePink", track = "track20_5", beatmap = "track20_5.json",
            primary = new Color(1f, 0.05f, 0.15f), secondary = new Color(0.55f, 0.2f, 1f),
            floor = new Color(0.04f, 0.015f, 0.02f), grid = new Color(1f, 0.1f, 0.2f), lane = new Color(0.6f, 0.25f, 1f),
            fog = new Color(0.1f, 0.02f, 0.05f), light = new Color(0.9f, 0.85f, 1f), filter = new Color(1f, 0.97f, 0.97f),
            lightIntensity = 0.4f, exposure = 0.35f, saturation = 18f, bloomScale = 1.15f, dropBloom = 0.55f,
            rate = 30f, lifetime = new Vector2(1.5f, 3.5f), speed = new Vector2(1f, 3f), size = new Vector2(0.02f, 0.05f),
        },
        new Skin
        {
            id = FiestaSkin.Swamp, name = "Swamp", source = "CyberGreen", track = "track20_6", beatmap = "track20_6.json",
            primary = new Color(0.4f, 1f, 0.7f), secondary = new Color(0.7f, 1f, 0.2f),
            floor = new Color(0.02f, 0.03f, 0.02f), grid = new Color(0.4f, 1f, 0.7f), lane = new Color(0.75f, 1f, 0.25f),
            fog = new Color(0.03f, 0.07f, 0.05f), light = new Color(0.9f, 0.85f, 1f), filter = new Color(1f, 0.97f, 0.97f),
            lightIntensity = 0.4f, exposure = 0.4f, saturation = 16f, bloomScale = 1.15f, dropBloom = 0.55f,
            rate = 30f, lifetime = new Vector2(1.5f, 3.5f), speed = new Vector2(1f, 3f), size = new Vector2(0.02f, 0.05f),
        },
        new Skin
        {
            id = FiestaSkin.Monster, name = "Monster", source = "Rainbow", track = "track20_7", beatmap = "track20_7.json",
            primary = new Color(1f, 0.5f, 0.05f), secondary = new Color(0.65f, 0.25f, 1f),
            floor = new Color(0.04f, 0.02f, 0.05f), grid = new Color(1f, 0.5f, 0.1f), lane = new Color(0.7f, 0.3f, 1f),
            fog = new Color(0.09f, 0.04f, 0.12f), light = new Color(0.9f, 0.85f, 1f), filter = new Color(1f, 0.97f, 0.97f),
            lightIntensity = 0.5f, exposure = 0.35f, saturation = 22f, bloomScale = 1.15f, dropBloom = 0.55f,
            rate = 30f, lifetime = new Vector2(1.5f, 3.5f), speed = new Vector2(1f, 3f), size = new Vector2(0.02f, 0.05f),
        },
    };

    [MenuItem("Tunnel/Playlist/Build Video 20 (Halloween)", priority = 99)]
    public static void Build()
    {
        var skins = Skins();
        var themes = new TunnelTheme[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            AssetDatabase.ImportAsset("Assets/Audio/Tracks/" + skins[i].track + ".wav");
            CopyTrackImport("Assets/Audio/Tracks/" + skins[i].track + ".wav");
            themes[i] = BuildTheme(skins[i]);
        }
        AssetDatabase.SaveAssets();
        var tracks = new AudioClip[skins.Length];
        for (int i = 0; i < skins.Length; i++) tracks[i] = Profile(skins[i], themes[i]).track;

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        var v10 = AssetDatabase.LoadAssetAtPath<RunPlaylist>(Video10Setup.PlaylistPath);   // the wrapper's flags
        p.title = "Video20";
        p.preRoll = true;
        p.noteMarginBeats = 1f;
        p.wallsInTransition = 0;
        p.finalFadeSeconds = 0f;
        p.thunderFlash = 0.8f;
        // The trainer from our Gemini clips (the user's call): the filmed video silhouette, solid
        // black and backlit, a move per note on baked frames (commit 0c0635a) - the scene's
        // videoSet; no Mixamo set, no mocap.
        // ...and then (the user's call, after trying it): the very first trainer of video 1,
        // SilhouetteTrainerV5 - Jennifer's cyan contour on Trainer v5.
        p.trainerMotionSet = null;
        p.trainerFilmed = false;
        p.trainerMocap3D = false;
        p.trainerClassic = false;
        p.trainerV5 = true;
        p.scrollSpeedScale = 1.75f;           // the notes fly ~3.9 s (x2 was a touch fast - the user's call)
        p.cameraFov = 75f;                    // a wider lens: the notes rush in at the end
        p.hideTrainer = false;                // video 20 goes out with the trained V5 trainer (the user's call)
        Video12Setup.EnsureTrainerV5();
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
        p.welcomeText = "HAPPY HALLOWEEN";

        p.segments = new List<PlaylistSegment>();
        for (int i = 0; i < skins.Length; i++)
            p.segments.Add(new PlaylistSegment
            {
                label = "Level " + (i + 1) + " " + skins[i].name, role = PlaylistSegmentRole.Level,
                track = tracks[i], beatmapFileName = skins[i].beatmap, theme = themes[i],
                clipStart = 0f, clipEnd = 0f, transitionIn = PlaylistTransition.Interlude, transitionSeconds = 3.5f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        // Copy Video 10's playlist flags that this script does not name (spoiler/intro off etc.).
        if (v10 != null) CopyUnsetFlags(v10, p);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[Video20] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    // Every serialized bool of Video 10's playlist that Build() did not set itself (e.g. spoiler,
    // welcome cards) is taken over, so Video 11 has exactly Video 10's wrapper.
    private static readonly HashSet<string> SetHere = new HashSet<string>
    {
        "preRoll", "trainerFilmed", "trainerClassic", "trainerV5", "trainerMocap3D", "hideTrainer", "trainerTextured", "trainerPanelInThemeColors",
        "retention", "recordFromContent",
    };

    private static void CopyUnsetFlags(RunPlaylist from, RunPlaylist to)
    {
        var src = new SerializedObject(from);
        var dst = new SerializedObject(to);
        var it = src.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.propertyType != SerializedPropertyType.Boolean || SetHere.Contains(it.name)) continue;
            var d = dst.FindProperty(it.propertyPath);
            if (d != null) d.boolValue = it.boolValue;
        }
        dst.ApplyModifiedPropertiesWithoutUndo();
    }

    private static TunnelTheme BuildTheme(Skin s)
    {
        var theme = CopyAsset<TunnelTheme>(Themes + "Theme_Video9_" + s.source + ".asset", Themes + "Theme_Halloween_" + s.name + ".asset");
        theme.primaryColor = s.primary; theme.secondaryColor = s.secondary;
        theme.accentEmissionIntensity = 1.35f;
        theme.cueColorsByHand = true; theme.leftHandColor = LeftHand; theme.rightHandColor = RightHand;
        theme.floorColor = s.floor; theme.floorMetallic = 0.15f; theme.floorSmoothness = 0.7f;   // wet asphalt
        theme.floorGridLineColor = s.grid; theme.floorGridGlintIntensity = 0.45f;
        theme.laneLineColor = s.lane; theme.laneLineEmissionIntensity = 0.7f;
        theme.fogColor = s.fog; theme.fogStartRatio = 0.45f; theme.fogEndRatio = 1f;
        theme.ambientOverride = new Color(s.fog.r * 1.4f + 0.04f, s.fog.g * 1.4f + 0.04f, s.fog.b * 1.4f + 0.05f, 1f);
        theme.lightIntensity = s.lightIntensity;
        theme.lightColor = s.light;
        theme.cameraBackgroundColor = s.fog;
        theme.skyboxMaterial = null;           // the horizon glow is FiestaEnvironment's own, beyond the fog
        theme.hudComboTextColor = s.primary; theme.hudBannerBorderColor = s.secondary;

        theme.ringArchPrefab = Prefab("Ring_Halloween_" + s.name, typeof(RingArchFiesta), s.id, SetKit);
        theme.environmentPrefab = Prefab("Environment_Halloween_" + s.name, typeof(FiestaEnvironment), s.id,
            so => { so.FindProperty("dropBloom").floatValue = s.dropBloom; SetKit(so); });
        theme.gatePrefab = null;

        var dust = CopyAsset<ParticlePreset>(Themes + "Presets/ParticlePreset_Video9_" + s.source + ".asset", Themes + "Presets/ParticlePreset_Halloween_" + s.name + ".asset");
        dust.rate = s.rate; dust.lifetime = s.lifetime; dust.speed = s.speed; dust.size = s.size; dust.emissionIntensity = 1.3f;
        EditorUtility.SetDirty(dust);
        theme.airParticles = dust;

        theme.postProcessingProfile = PostProfile(s);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    private static GameObject Prefab(string name, System.Type script, FiestaSkin skin, System.Action<SerializedObject> more)
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

    // Every Kenney model under Assets/ThirdParty/Kenney, for FiestaKit to find by name.
    private static void SetKit(SerializedObject so)
    {
        var list = so.FindProperty("kit");
        var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Kenney" });
        list.arraySize = guids.Length;
        for (int i = 0; i < guids.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
    }

    private static VolumeProfile PostProfile(Skin s)
    {
        var source = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Settings + "TunnelScene_PostProcessing_Video9_" + s.source + ".asset");
        string path = Settings + "TunnelScene_PostProcessing_Halloween_" + s.name + ".asset";
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
        string path = Themes + "Profiles/RunProfile_Halloween_" + s.name + ".asset";
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


    [MenuItem("Tunnel/Playlist/Use: Video 20", priority = 100)] private static void UseVideo20() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
