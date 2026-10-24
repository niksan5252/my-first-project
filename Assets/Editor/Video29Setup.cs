using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// Video 29: BEST OF BEAT TUNNEL - a compilation of the ten best levels so far, each with its own track,
/// chart and world (taken from its own video's playlist). Picked by the cue rules the analytics gave
/// (1.3-1.5 notes a second, 8-13 move types, the long levels of videos 13/14/10 that kept viewers best),
/// cut to about 2:15 each (a bar line), ~21 min of music. Every level is re-charted by those rules
/// (Tools/beatmap: deck29-N, 4-bar sets of one move, 8-12 moves a level): StreamingAssets/Beatmaps/best29_N.json.
/// The opening is the competitor's: a spoiler of the loudest moments of five levels, the secret one last,
/// then BEST OF BEAT TUNNEL -> THIS VIDEO HAS 10 LEVELS -> LEVEL 10 IS A SECRET! The hook: the last level
/// is the SECRET LEVEL, every level card counts down to it. Idempotent.
public static class Video29Setup
{
    public const string PlaylistPath = "Assets/Themes/Playlists/Playlist_Video29.asset";

    // (source video, track, world name, clip end (0 = the whole track)), in the order they play: an easy,
    // bright level first, the strongest in the middle and at the end, the gold one as the secret level.
    private static readonly (int video, string track, string world, float end)[] Levels =
    {
        (11, "track11_3", "Rio Carnival", 0f),
        (10, "track10_1", "Orbit Station", 134.7f),
        (13, "track13_3", "Desert", 133.77f),
        (22, "track22_3", "Neon Ocean", 0f),
        (14, "track14_4", "Funfair", 133.69f),
        (12, "track12_3", "Neo Tokyo", 134.6f),
        (9, "track9_2", "Cyber Grid", 134.28f),
        (24, "track24_5", "Firefly Jungle", 134.63f),
        (13, "track13_4", "Matrix", 133.28f),
        (27, "track27_5", "El Dorado", 0f),
    };

    // The spoiler: 3 s of the densest, loudest bars (index into Levels, from, to); the secret world last.
    private static readonly (int level, float from, float to)[] Spoilers =
    {
        (4, 89.88f, 92.88f), (8, 43.31f, 46.31f), (5, 80.17f, 83.17f), (7, 67.84f, 70.84f), (9, 48.6f, 52.1f),
    };

    [MenuItem("Tunnel/Playlist/Build Video 29 (Best Of)", priority = 100)]
    public static void Build()
    {
        var found = new PlaylistSegment[Levels.Length];
        for (int i = 0; i < Levels.Length; i++)
        {
            var (video, track, _, _) = Levels[i];
            var src = AssetDatabase.LoadAssetAtPath<RunPlaylist>("Assets/Themes/Playlists/Playlist_Video" + video + ".asset");
            if (src != null) found[i] = src.segments.Find(s => s != null && s.role == PlaylistSegmentRole.Level && s.beatmapFileName == track + ".json");
            if (found[i] == null || found[i].track == null || found[i].theme == null)
            { Debug.LogError("[Video29] no level " + track + " (track and theme) in Playlist_Video" + video); return; }
            if (!System.IO.File.Exists("Assets/StreamingAssets/Beatmaps/best29_" + (i + 1) + ".json"))
            { Debug.LogError("[Video29] no chart best29_" + (i + 1) + ".json"); return; }
        }

        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(PlaylistPath);
        if (p == null) { p = ScriptableObject.CreateInstance<RunPlaylist>(); AssetDatabase.CreateAsset(p, PlaylistPath); }
        var v10 = AssetDatabase.LoadAssetAtPath<RunPlaylist>(Video10Setup.PlaylistPath);   // the wrapper's flags
        p.title = "Video29";
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
        p.interludeSeconds = 6f;              // the level card (LevelCards), 6 s - the user's call
        p.introCardsSeconds = 9f;             // the three opening cards after the spoiler
        p.endScreenSeconds = 20f;
        p.loopVolume = 0.22f;
        p.loopBars = 4;
        p.progressHud = true;
        p.kcalPerMinute = 9f;
        p.coachVoice = true;
        p.favoriteQuestion = true;
        p.outroSeconds = 36f;
        p.levelCards = true;
        p.introMoves = new string[0];

        // The hook.
        p.spoilerSubtitle = "WAIT FOR THE SECRET LEVEL 10!";
        p.welcomeText = "BEST OF BEAT TUNNEL";
        p.welcomeSubtitle = "THE 10 BEST LEVELS IN ONE VIDEO!";
        p.welcomeVoice = "BEST_OF";
        p.welcomeHook = "LEVEL 10 IS A SECRET!";
        p.welcomeHookSubtitle = "can you make it to the end?";
        p.welcomeHookVoice = "SECRET_HOOK";
        p.secretFinal = true;

        p.segments = new List<PlaylistSegment>();
        foreach (var (level, from, to) in Spoilers)
            p.segments.Add(new PlaylistSegment
            {
                label = "Spoiler " + Levels[level].world, role = PlaylistSegmentRole.Spoiler,
                track = found[level].track, beatmapFileName = "best29_" + (level + 1) + ".json", theme = found[level].theme,
                clipStart = from, clipEnd = to, transitionIn = PlaylistTransition.Cut, transitionSeconds = 2f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        for (int i = 0; i < Levels.Length; i++)
            p.segments.Add(new PlaylistSegment
            {
                label = "Level " + (i + 1) + " " + Levels[i].world, role = PlaylistSegmentRole.Level,
                track = found[i].track, beatmapFileName = "best29_" + (i + 1) + ".json", theme = found[i].theme,
                clipStart = 0f, clipEnd = Levels[i].end, transitionIn = PlaylistTransition.Interlude, transitionSeconds = 3.5f,
                transitionLook = PlaylistTransitionLook.Veil,
            });
        if (v10 != null) CopyUnsetFlags(v10, p);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        HalloweenPlaylistSetup.Use(PlaylistPath);
        HalloweenPlaylistSetup.Solo(-1);
        Debug.Log("[Video29] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    private static readonly HashSet<string> SetHere = new HashSet<string>
    {
        "preRoll", "trainerFilmed", "trainerClassic", "trainerV5", "trainerMocap3D", "hideTrainer", "trainerTextured", "trainerPanelInThemeColors",
        "retention", "recordFromContent", "progressHud", "coachVoice", "favoriteQuestion", "levelCards", "secretFinal",
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

    [MenuItem("Tunnel/Playlist/Use: Video 29", priority = 100)] private static void UseVideo29() => HalloweenPlaylistSetup.Use(PlaylistPath);
}
