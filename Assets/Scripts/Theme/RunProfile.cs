using UnityEngine;

/// One video's worth of choices: the track, the chart made from it, and the tunnel's look. A
/// RunProfileSelector in the scene points at one of these, so switching the whole video - music,
/// notes and theme together - is one reference, and a new video is a new asset rather than an
/// edit to a scene that the previous video depends on.
[CreateAssetMenu(menuName = "Tunnel/Run Profile", fileName = "RunProfile")]
public class RunProfile : ScriptableObject
{
    public AudioClip track;
    // File name under StreamingAssets/Beatmaps, as CueSpawner expects it.
    public string beatmapFileName;
    public TunnelTheme theme;

    [Header("Intro (optional - off keeps CueSpawner's own settings)")]
    // For a track whose drums start with the music: let the notes come from the very beginning.
    public bool overrideIntro;
    // Bars of music before the first note may arrive (CueSpawner's introBars).
    [Min(0f)] public float introBars = 0f;
    // Skip the rest of a workout set the intro ends inside (CueSpawner's introSkipsPartialSet).
    public bool introSkipsPartialSet = false;
    // Start the tunnel before the music - just long enough for the chart's first note to fly in
    // from the spawn point - instead of dropping every note that would have to be born before the
    // music starts. The music then starts so that the first note lands on its beat.
    public bool preRoll = true;
}
