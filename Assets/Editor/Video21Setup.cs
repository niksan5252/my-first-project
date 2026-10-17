using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// Video 21 "Neon Nights": nine levels in the look of video 1 (the user: "everyone likes the first
/// tunnel") - the tunnel's own neon octagon rings in two alternating colours, a night city, a dark
/// sky - each level with its own colour pair and, now and then, its own city (Neo Tokyo, Synthwave,
/// Toxic District, Ice City, Gold City). Tracks track21_1..9 from Desktop/треки/21 видео (quiet
/// intros cut, short ones looped on the bar); the strongest opens, the second strongest closes;
/// charts from deck21-1..9 (no move shared by two neighbouring levels), a SPEED ROUND in level 5;
/// the video 20 retention pack (spoiler, progress strip, coach voice, kcal, favourite world).
/// Video 12's wrapper, the V5 trainer, tunnel x1.75 with a 75 deg lens. Idempotent.
public static class Video21Setup
{
    private const string Themes = "Assets/Themes/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_Video21.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    private class Level
    {
        public string name, source, track;
        public Color primary, secondary;
    }

    private static Level L(string name, string source, int track, Color a, Color b) =>
        new Level { name = name, source = source, track = "track21_" + track, primary = a, secondary = b };

    // The strongest track first (5), the second strongest last (7) - by the energy of their first 30 s.
    private static Level[] Levels() => new[]
    {
        L("Neon City", "Theme_Cyberpunk", 5, new Color(1f, 0.05f, 0.45f), new Color(0f, 0.85f, 1f)),           // exactly video 1
        L("Ultraviolet", "Theme_Cyberpunk", 9, new Color(0.65f, 0.2f, 1f), new Color(0.5f, 1f, 0.2f)),
        L("Neo Tokyo", "Theme_Worlds_NeoTokyo", 1, new Color(1f, 0.1f, 0.25f), new Color(0.6f, 0.95f, 1f)),
        L("Sunset Drive", "Theme_Cyberpunk", 3, new Color(1f, 0.45f, 0.05f), new Color(0.1f, 0.5f, 1f)),
        L("Synthwave", "Theme_Synthwave", 8, new Color(1f, 0.1f, 0.8f), new Color(1f, 0.85f, 0.1f)),
        L("Toxic District", "Theme_HyperCity_ToxicGreen", 6, new Color(0.3f, 1f, 0.3f), new Color(0.6f, 0.25f, 1f)),
        L("Blood Moon", "Theme_Cyberpunk", 4, new Color(1f, 0.05f, 0.1f), new Color(0f, 0.9f, 1f)),
        L("Ice City", "Theme_IceCity", 2, new Color(0.4f, 0.85f, 1f), new Color(1f, 0.4f, 0.8f)),
        L("Gold City", "Theme_HyperCity_GoldFinal", 7, new Color(1f, 0.75f, 0.15f), new Color(1f, 0.1f, 0.6f)),
    };

    [MenuItem("Tunnel/Playlist/Build Video 21 (Neon Nights)", priority = 100)]
    public static void Build()
    {
        var levels = Levels();
        var themes = new TunnelTheme[levels.Length];
        var tracks = new AudioClip[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            string path = "Assets/Audio/Tracks/" + levels[i].track + ".wav";
            AssetDatabase.ImportAsset(path);
            CopyTrackImport(path);
            themes[i] = BuildTheme(levels[i]);
            tracks[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        AssetDatabase.SaveAssets();

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        var v10 = AssetDatabase.LoadAssetAtPath<RunPlaylist>(Video10Setup.PlaylistPath);   // the wrapper's flags
        p.title = "Video21";
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
        p.scrollSpeedScale = 1.75f;
        p.cameraFov = 75f;
        p.hideTrainer = false;
        Video12Setup.EnsureTrainerV5();
        p.trainerTextured = false;
        p.trainerPanelInThemeColors = false;
        p.retention = true;
        p.recordFromContent = true;
        p.interludeSeconds = 13f;
        p.introCardsSeconds = 9f;
        p.endScreenSeconds = 20f;
        p.loopVolume = 0.22f;
        p.loopBars = 4;
        p.welcomeText = "WELCOME TO BEAT TUNNEL";
        // The retention pack (video 20).
        p.progressHud = true;
        p.kcalPerMinute = 9f;
        p.coachVoice = true;
        p.favoriteQuestion = true;
        p.spoilerSubtitle = "WAIT FOR THE FINAL LEVEL!";
        p.welcomeVoice = "WELCOME";
        p.outroSeconds = 36f;

        p.segments = new List<PlaylistSegment>();
        // The spoiler: the loudest seconds of level 8 (Ice City) and of the finale (Gold City).
        foreach (var (level, from, to) in new[] { (8, 77.1f, 80.6f), (9, 54.9f, 58.9f) })
            p.segments.Add(new PlaylistSegment
            {
                label = "Spoiler " + levels[level - 1].name, role = PlaylistSegmentRole.Spoiler,
                track = tracks[level - 1], beatmapFileName = levels[level - 1].track + ".json", theme = themes[level - 1],
                clipStart = from, clipEnd = to, transitionIn = PlaylistTransition.Cut, transitionSeconds = 2f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        for (int i = 0; i < levels.Length; i++)
            p.segments.Add(new PlaylistSegment
            {
                label = "Level " + (i + 1) + " " + levels[i].name, role = PlaylistSegmentRole.Level,
                track = tracks[i], beatmapFileName = levels[i].track + ".json", theme = themes[i],
                clipStart = 0f, clipEnd = 0f, transitionIn = PlaylistTransition.Interlude, transitionSeconds = 3.5f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        if (v10 != null) CopyUnsetFlags(v10, p);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[Video21] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    // The source theme as it is (its city, sky, particles, post), with the tunnel's own octagon
    // rings back (no arch prefab - video 1's look) in this level's two colours.
    private static TunnelTheme BuildTheme(Level l)
    {
        string path = Themes + "Theme_Neon21_" + l.name.Replace(" ", "") + ".asset";
        if (!File.Exists(path)) AssetDatabase.CopyAsset(Themes + l.source + ".asset", path);
        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(path);
        theme.primaryColor = l.primary;
        theme.secondaryColor = l.secondary;
        theme.ringArchPrefab = null;
        theme.gatePrefab = null;
        theme.laneLineColor = l.secondary;
        theme.floorGridLineColor = l.primary;
        theme.hudComboTextColor = l.primary;
        theme.hudBannerBorderColor = l.secondary;
        EditorUtility.SetDirty(theme);
        return theme;
    }

    private static readonly HashSet<string> SetHere = new HashSet<string>
    {
        "preRoll", "trainerFilmed", "trainerClassic", "trainerV5", "trainerMocap3D", "hideTrainer", "trainerTextured", "trainerPanelInThemeColors",
        "retention", "recordFromContent", "progressHud", "coachVoice", "favoriteQuestion",
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

    [MenuItem("Tunnel/Playlist/Use: Video 21", priority = 100)] private static void UseVideo21() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
