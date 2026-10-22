using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Video 26 (phonk): the worlds of videos 24-25 recoloured, the tracks as they are, charts deck26-1..5
/// (long sets: a warm-up, then the dance), the level cards, no spoiler. Idempotent.
public static class Video26Setup
{
    private const string Themes = "Assets/Themes/";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Settings = "Assets/Settings/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_Video26.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    private class Skin
    {
        public FiestaSkin id;
        public string name, source, track;
        public Color primary, secondary, floor, fog, filter;
        public float exposure, saturation;
    }

    private static readonly Color LeftHand = new Color(0f, 0.9f, 1f);
    private static readonly Color RightHand = new Color(1f, 0.1f, 0.72f);

    private static Skin S(FiestaSkin id, string name, string source, int track, Color a, Color b, Color fog, float exposure = 0.35f) => new Skin
    {
        id = id, name = name, source = source, track = "track26_" + track, primary = a, secondary = b,
        floor = new Color(0.03f, 0.03f, 0.045f), fog = fog, filter = new Color(1f, 0.98f, 0.98f), exposure = exposure, saturation = 18f,
    };

    // Phonk (the user's call): the worlds already built, recoloured purple, red and acid neon.
    private static Skin[] Skins() => new[]
    {
        S(FiestaSkin.CrystalCave, "Crystal Cave", "BluePink", 1, new Color(0.7f, 0.2f, 1f), new Color(1f, 0.1f, 0.3f), new Color(0.05f, 0.02f, 0.08f)),
        S(FiestaSkin.CyberFactory, "Cyber Factory", "Rainbow", 2, new Color(1f, 0.15f, 0.25f), new Color(0.2f, 0.9f, 1f), new Color(0.07f, 0.02f, 0.03f)),
        S(FiestaSkin.SakuraNight, "Sakura Night", "BluePink", 3, new Color(1f, 0.2f, 0.6f), new Color(0.6f, 0.3f, 1f), new Color(0.06f, 0.02f, 0.07f)),
        S(FiestaSkin.PlanetRing, "Planet Ring", "BluePink", 4, new Color(0.4f, 1f, 0.3f), new Color(0.75f, 0.25f, 1f), new Color(0.02f, 0.02f, 0.06f)),
        S(FiestaSkin.ElDorado, "El Dorado", "Rainbow", 5, new Color(1f, 0.7f, 0.1f), new Color(1f, 0.1f, 0.2f), new Color(0.09f, 0.04f, 0.02f)),
    };

    [MenuItem("Tunnel/Playlist/Build Video 26 (Phonk)", priority = 100)]
    public static void Build()
    {
        var skins = Skins();
        var themes = new TunnelTheme[skins.Length];
        var tracks = new AudioClip[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            string path = "Assets/Audio/Tracks/" + skins[i].track + ".wav";
            AssetDatabase.ImportAsset(path);
            CopyTrackImport(path);
            themes[i] = BuildTheme(skins[i]);
            tracks[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        AssetDatabase.SaveAssets();

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        var v10 = AssetDatabase.LoadAssetAtPath<RunPlaylist>(Video10Setup.PlaylistPath);   // the wrapper's flags
        p.title = "Video26";
        p.preRoll = true;
        p.noteMarginBeats = 1f;
        p.wallsInTransition = 0;
        p.finalFadeSeconds = 0f;
        p.thunderFlash = 0.8f;
        p.trainerMotionSet = null;
        p.trainerFilmed = false;
        p.trainerMocap3D = false;
        p.trainerClassic = false;
        p.trainerV5 = true;
        p.scrollSpeedScale = 1.4f;
        p.motionBlur = 0.6f;
        p.cameraFov = 75f;
        p.hideTrainer = false;
        Video12Setup.EnsureTrainerV5();
        p.trainerTextured = false;
        p.trainerPanelInThemeColors = false;
        p.retention = true;
        p.recordFromContent = true;
        p.interludeSeconds = 6f;              // the level card (LevelCards), 6 s - the user's call              // the short break naming the next world (RetentionOverlay)
        p.introCardsSeconds = 3f;
        p.endScreenSeconds = 20f;
        p.loopVolume = 0.22f;
        p.loopBars = 4;
        p.welcomeText = "WELCOME TO BEAT TUNNEL";
        p.progressHud = true;
        p.kcalPerMinute = 9f;
        p.coachVoice = true;
        p.favoriteQuestion = true;
        p.spoilerSubtitle = "";
        p.welcomeVoice = "";
        p.outroSeconds = 36f;

        // No spoiler, no intro: LEVEL 1's card opens the video, a card before every level after it.
        p.segments = new List<PlaylistSegment>();
        p.introMoves = new string[0];
        p.levelCards = true;
        for (int i = 0; i < skins.Length; i++)
            p.segments.Add(new PlaylistSegment
            {
                label = "Level " + (i + 1) + " " + skins[i].name, role = PlaylistSegmentRole.Level,
                track = tracks[i], beatmapFileName = skins[i].track + ".json", theme = themes[i],
                clipStart = 0f, clipEnd = 0f, transitionIn = PlaylistTransition.Interlude, transitionSeconds = 3.5f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        if (v10 != null) CopyUnsetFlags(v10, p);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[Video26] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    // Video 9's neon base, the world's own scenery (FiestaEnvironment), and video 1's octagon rings
    // (no arch prefab) in the world's two colours.
    private static TunnelTheme BuildTheme(Skin s)
    {
        string key = s.name.Replace(" ", "");
        var theme = CopyAsset<TunnelTheme>(Themes + "Theme_Video9_" + s.source + ".asset", Themes + "Theme_Neon26_" + key + ".asset");
        theme.primaryColor = s.primary; theme.secondaryColor = s.secondary;
        theme.accentEmissionIntensity = 1.35f;
        theme.cueColorsByHand = true; theme.leftHandColor = LeftHand; theme.rightHandColor = RightHand;
        theme.floorColor = s.floor; theme.floorMetallic = 0.15f; theme.floorSmoothness = 0.7f;
        theme.floorGridLineColor = s.primary; theme.floorGridGlintIntensity = 0.45f;
        theme.laneLineColor = s.secondary; theme.laneLineEmissionIntensity = 0.7f;
        theme.fogColor = s.fog; theme.fogStartRatio = 0.45f; theme.fogEndRatio = 1f;
        theme.ambientOverride = new Color(s.fog.r * 1.4f + 0.05f, s.fog.g * 1.4f + 0.05f, s.fog.b * 1.4f + 0.06f, 1f);
        theme.lightIntensity = 0.5f;
        theme.lightColor = new Color(0.9f, 0.88f, 1f);
        theme.cameraBackgroundColor = s.fog;
        theme.skyboxMaterial = null;           // the sky pieces are FiestaEnvironment's own, beyond the fog
        theme.hudComboTextColor = s.primary; theme.hudBannerBorderColor = s.secondary;
        theme.ringArchPrefab = Prefab("Ring_Neon26_" + key, s.id, typeof(RingArchFiesta));   // the world's own arch (RingArchFiesta.Neon25.cs)
        theme.environmentPrefab = Prefab("Environment_Neon26_" + key, s.id, typeof(FiestaEnvironment));
        theme.gatePrefab = null;
        theme.postProcessingProfile = PostProfile(s, key);
        EditorUtility.SetDirty(theme);
        return theme;
    }

    private static GameObject Prefab(string name, FiestaSkin skin, System.Type script)
    {
        string path = Prefabs + name + ".prefab";
        var go = new GameObject(name);
        var comp = go.AddComponent(script);
        var so = new SerializedObject(comp);
        so.FindProperty("skin").enumValueIndex = (int)skin;
        var bloom = so.FindProperty("dropBloom");
        if (bloom != null) bloom.floatValue = 0.5f;
        // The Kenney models: the project's set plus the factory and the full nature kit.
        var list = so.FindProperty("kit");
        var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Kenney", "Assets/SharedAssets/Kits3D/kenney_factory-kit_3.0", "Assets/SharedAssets/Kits3D/kenney_nature-kit" });
        list.arraySize = guids.Length;
        for (int i = 0; i < guids.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
        so.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static VolumeProfile PostProfile(Skin s, string key)
    {
        var source = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Settings + "TunnelScene_PostProcessing_Video9_" + s.source + ".asset");
        var copy = CopyAsset<VolumeProfile>(AssetDatabase.GetAssetPath(source), Settings + "TunnelScene_PostProcessing_Neon26_" + key + ".asset");
        if (!copy.TryGet(out ColorAdjustments ca)) ca = copy.Add<ColorAdjustments>(true);
        ca.colorFilter.overrideState = true; ca.colorFilter.value = s.filter;
        ca.postExposure.overrideState = true; ca.postExposure.value = s.exposure;
        ca.saturation.overrideState = true; ca.saturation.value = s.saturation;
        EditorUtility.SetDirty(copy);
        return copy;
    }

    private static T CopyAsset<T>(string from, string to) where T : Object
    {
        if (!File.Exists(to)) AssetDatabase.CopyAsset(from, to);
        return AssetDatabase.LoadAssetAtPath<T>(to);
    }

    private static readonly HashSet<string> SetHere = new HashSet<string>
    {
        "preRoll", "trainerFilmed", "trainerClassic", "trainerV5", "trainerMocap3D", "hideTrainer", "trainerTextured", "trainerPanelInThemeColors",
        "retention", "recordFromContent", "progressHud", "coachVoice", "favoriteQuestion", "levelCards",
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

    [MenuItem("Tunnel/Playlist/Use: Video 26", priority = 100)] private static void UseVideo26() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
