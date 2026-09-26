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
}
