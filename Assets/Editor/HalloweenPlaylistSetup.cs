using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// The Halloween video (video 3) as a RunPlaylist - three tracks through one tunnel - and a short
/// transition test playlist, built from code like the theme setups. Also the menu that points the
/// scene's RunProfileSelector at a playlist (or back at its run profile) and picks a solo segment.
///
/// Segments are added here as their themes exist; only segment 1 is in the playlist so far.
public static class HalloweenPlaylistSetup
{
    private const string Folder = "Assets/Themes/Playlists";
    public const string PlaylistPath = Folder + "/Playlist_Halloween.asset";
    public const string TestPath = Folder + "/Playlist_TransitionTest.asset";
    private const string ImportTemplate = "Assets/Audio/Tracks/irokz_goodbye_my_love.wav";
    private const string SkeletonSet = "Assets/SharedAssets/Mixamo/CartoonSkeletonMotionSet.asset";

    private const string HauntedHouse = "Assets/Audio/Tracks/neoni_haunted_house.wav";
    public static readonly string[] Tracks =
    {
        HauntedHouse, "Assets/Audio/Tracks/axollo_burn_it.wav", "Assets/Audio/Tracks/rxm_look_at_me.wav",
    };
    private const string GraveyardTheme = "Assets/Themes/Theme_HalloweenGraveyard.asset";
    private const string PhonkTheme = "Assets/Themes/Theme_Phonk.asset";
    private const string PumpkinTheme = "Assets/Themes/Theme_HalloweenPumpkin.asset";
    private const string HauntedTheme = "Assets/Themes/Theme_HalloweenHaunted.asset";
    private const string OceanTheme = "Assets/Themes/Theme_Ocean.asset";

    [MenuItem("Tunnel/Playlist/Build Halloween Playlist", priority = 80)]
    public static void Build()
    {
        CopyTrackImports();
        Directory.CreateDirectory(Folder);
        var p = LoadOrCreate(PlaylistPath);
        p.title = "Halloween";
        p.preRoll = true;
        // Nothing on the road through a transition: the look changing IS the event, and a long
        // wall there hides it.
        p.wallsInTransition = 0;
        p.trainerMotionSet = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(SkeletonSet);
        p.trainerTextured = true;
        p.segments = new List<PlaylistSegment>
        {
            new PlaylistSegment
            {
                label = "Graveyard", track = Clip(HauntedHouse), beatmapFileName = "neoni_haunted_house.json",
                theme = Theme(GraveyardTheme) ?? Theme(OceanTheme),
                // The music is over by 176 s (the file runs on in silence to 181.9): end there, so
                // the crossfade overlaps music with music instead of fading silence.
                clipEnd = 176f,
            },
            // Burn It: its drumless intro is 29.5 s; the segment starts 16 bars in (14.8 s), so the
            // crossfade lands in the intro's second half and the drums come in 15 s later.
            new PlaylistSegment
            {
                label = "PumpkinFestival", track = Clip(Tracks[1]), beatmapFileName = "axollo_burn_it.json",
                theme = Theme(PumpkinTheme), clipStart = 14.816f,
                // Burn It falls silent after 181 s.
                clipEnd = 181.2f,
                transitionIn = PlaylistTransition.Crossfade, transitionSeconds = 3.5f,
            },
            // LOOK AT ME!: in on a thunderclap on its first bar line after the silent start
            // (2.279 s), its first note a second after the clap; out 2.8 s after its last punch
            // (175.29 s) - CONGRATULATIONS, then the finale's fade.
            new PlaylistSegment
            {
                label = "HauntedHouse", track = Clip(Tracks[2]), beatmapFileName = "rxm_look_at_me.json",
                theme = Theme(HauntedTheme), clipStart = 2.279f, clipEnd = 178.1f,
                transitionIn = PlaylistTransition.Thunder, transitionSeconds = 3.5f,
            },
        };
        p.finalFadeSeconds = 2.5f;
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        Use(PlaylistPath);
        Debug.Log("[Playlist] built " + PlaylistPath + " (" + p.segments.Count + " segments)");
    }

    /// Segment 1 cut short, then the same track again under Phonk through a THUNDER transition, then
    /// back to segment 1's look through a CROSSFADE: both kinds of transition in 95 seconds.
    [MenuItem("Tunnel/Playlist/Build Transition Test Playlist", priority = 81)]
    public static void BuildTest()
    {
        CopyTrackImports();
        Directory.CreateDirectory(Folder);
        var first = Theme(GraveyardTheme) ?? Theme(OceanTheme);
        var p = LoadOrCreate(TestPath);
        p.title = "TransitionTest";
        p.preRoll = true;
        p.trainerMotionSet = AssetDatabase.LoadAssetAtPath<SilhouetteMotionSet>(SkeletonSet);
        p.trainerTextured = true;
        p.segments = new List<PlaylistSegment>
        {
            new PlaylistSegment { label = "A", track = Clip(HauntedHouse), beatmapFileName = "neoni_haunted_house.json",
                                  theme = first, clipStart = 0f, clipEnd = 34.3f },
            new PlaylistSegment { label = "B-thunder", track = Clip(HauntedHouse), beatmapFileName = "neoni_haunted_house.json",
                                  theme = Theme(PhonkTheme), clipStart = 54.9f, clipEnd = 86.9f,
                                  transitionIn = PlaylistTransition.Thunder, transitionSeconds = 3.5f },
            new PlaylistSegment { label = "C-crossfade", track = Clip(HauntedHouse), beatmapFileName = "neoni_haunted_house.json",
                                  theme = first, clipStart = 114.3f, clipEnd = 142f,
                                  transitionIn = PlaylistTransition.Crossfade, transitionSeconds = 3.5f },
        };
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        Use(TestPath);
        Debug.Log("[Playlist] built " + TestPath);
    }

    [MenuItem("Tunnel/Playlist/Use: Halloween", priority = 90)] private static void UseHalloween() => Use(PlaylistPath);
    [MenuItem("Tunnel/Playlist/Use: Transition Test", priority = 91)] private static void UseTest() => Use(TestPath);
    [MenuItem("Tunnel/Playlist/Use: none (the run profile)", priority = 92)] private static void UseNone() => Use(null);
    [MenuItem("Tunnel/Playlist/Solo: whole playlist", priority = 100)] private static void SoloAll() => Solo(-1);
    [MenuItem("Tunnel/Playlist/Solo: segment 1", priority = 101)] private static void Solo1() => Solo(0);
    [MenuItem("Tunnel/Playlist/Solo: segment 2", priority = 102)] private static void Solo2() => Solo(1);
    [MenuItem("Tunnel/Playlist/Solo: segment 3", priority = 103)] private static void Solo3() => Solo(2);

    public static void Use(string path)
    {
        Edit(so => so.FindProperty("playlist").objectReferenceValue = path != null ? AssetDatabase.LoadAssetAtPath<RunPlaylist>(path) : null);
    }

    public static void Solo(int index) => Edit(so => so.FindProperty("playlistSolo").intValue = index);

    private static void Edit(System.Action<SerializedObject> change)
    {
        var sel = Object.FindAnyObjectByType<RunProfileSelector>();
        if (sel == null) { Debug.LogError("[Playlist] no RunProfileSelector in the scene"); return; }
        var so = new SerializedObject(sel);
        change(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(sel.gameObject.scene);
        EditorSceneManager.SaveScene(sel.gameObject.scene);
    }

    private static RunPlaylist LoadOrCreate(string path)
    {
        var p = AssetDatabase.LoadAssetAtPath<RunPlaylist>(path);
        if (p != null) return p;
        p = ScriptableObject.CreateInstance<RunPlaylist>();
        AssetDatabase.CreateAsset(p, path);
        return p;
    }

    private static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    private static TunnelTheme Theme(string path) => AssetDatabase.LoadAssetAtPath<TunnelTheme>(path);

    /// Same import as the other tracks: Vorbis, normalised, not preloaded.
    private static void CopyTrackImports()
    {
        var src = (AudioImporter)AssetImporter.GetAtPath(ImportTemplate);
        if (src == null) return;
        foreach (var t in Tracks)
        {
            var dst = (AudioImporter)AssetImporter.GetAtPath(t);
            if (dst == null) continue;
            var a = src.defaultSampleSettings; var b = dst.defaultSampleSettings;
            if (a.loadType == b.loadType && a.compressionFormat == b.compressionFormat && a.quality == b.quality
                && a.preloadAudioData == b.preloadAudioData && dst.forceToMono == src.forceToMono) continue;
            dst.defaultSampleSettings = a;
            dst.forceToMono = src.forceToMono;
            dst.loadInBackground = src.loadInBackground;
            dst.SaveAndReimport();
        }
    }
}
