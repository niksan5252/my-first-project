using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// Video 23 "Neon Fiesta": five levels in video 1's look (neon octagon rings in two colours, motion
/// blur, x1.4) over a world picked for each track's hook - Prism Hall (violin), Copacabana Night
/// (steel drums), Volcano (spanish guitar, the SPEED ROUND), Rio Carnival (festival), Arcade
/// ("la la la", the finale). The level order is the user's own file order (Desktop/треки/23 видео);
/// intros cut, short tracks looped; charts from deck23-1..5; the retention pack.
public static class Video23Setup
{
    private const string Themes = "Assets/Themes/";
    public const string PlaylistPath = Themes + "Playlists/Playlist_Video23.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";

    private class Level
    {
        public string name, source, track, sky;
        public Color primary, secondary;
    }

    private static Level L(string name, string source, int track, Color a, Color b) =>
        new Level { name = name, source = source, track = "track23_" + track, primary = a, secondary = b };

    private static Level WithSky(Level l, string sky) { l.sky = sky; return l; }

    // The strongest track first (5), the second strongest last (7) - by the energy of their first 30 s.
    private static Level[] Levels() => new[]
    {
        WithSky(L("Prism Hall", "Theme_NeonPrism_Spectrum", 1, new Color(0.85f, 0.95f, 1f), new Color(0.2f, 0.8f, 1f)), "Skybox_Cyberpunk_Night"),
        L("Copacabana Night", "Theme_NeonCarnival_Copacabana", 2, new Color(0.1f, 0.95f, 0.85f), new Color(1f, 0.25f, 0.65f)),
        L("Volcano", "Theme_Worlds_Volcano", 3, new Color(1f, 0.45f, 0.05f), new Color(1f, 0.1f, 0.1f)),
        L("Rio Carnival", "Theme_HyperCity_RioCarnival", 4, new Color(1f, 0.78f, 0.2f), new Color(1f, 0.15f, 0.6f)),
        L("Arcade", "Theme_Worlds_Arcade", 5, new Color(0.6f, 1f, 0.15f), new Color(0.7f, 0.3f, 1f)),
    };

    [MenuItem("Tunnel/Playlist/Build Video 23 (Neon Fiesta)", priority = 100)]
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
        p.title = "Video23";
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
        p.scrollSpeedScale = 1.4f;            // x1.75 strobed on the dense octagon rings (the user: jerky)
        p.motionBlur = 0.6f;
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
        // The spoiler: the loudest seconds of level 4 (Rio Carnival) and of the finale (Arcade).
        foreach (var (level, from, to) in new[] { (4, 56.3f, 59.8f), (5, 51.7f, 55.7f) })
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
        Debug.Log("[Video23] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    // The source theme as it is (its city, sky, particles, post), with the tunnel's own octagon
    // rings back (no arch prefab - video 1's look) in this level's two colours.
    private static TunnelTheme BuildTheme(Level l)
    {
        string path = Themes + "Theme_Neon23_" + l.name.Replace(" ", "") + ".asset";
        if (!File.Exists(path)) AssetDatabase.CopyAsset(Themes + l.source + ".asset", path);
        var theme = AssetDatabase.LoadAssetAtPath<TunnelTheme>(path);
        theme.primaryColor = l.primary;
        theme.secondaryColor = l.secondary;
        theme.ringArchPrefab = null;
        // A world without a sky of its own gets one (the user: "why is the background black").
        if (l.sky != null)
        {
            var sky = AssetDatabase.FindAssets(l.sky + " t:Material");
            if (sky.Length > 0)
            {
                // A copy of its own, brighter, so video 1's shared sky is left as it is.
                string copy = Themes + "Sky_Neon23_" + l.name.Replace(" ", "") + ".mat";
                if (!File.Exists(copy)) AssetDatabase.CopyAsset(AssetDatabase.GUIDToAssetPath(sky[0]), copy);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(copy);
                if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1.1f);
                EditorUtility.SetDirty(mat);
                theme.skyboxMaterial = mat;
            }
        }
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

    [MenuItem("Tunnel/Playlist/Use: Video 23", priority = 100)] private static void UseVideo23() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
