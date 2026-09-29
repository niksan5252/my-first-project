using UnityEngine;

/// Applies a RunProfile before anything is built: hands the track and the chart to CueSpawner and
/// the theme to TunnelThemeApplier. Runs first (execution order -1000) because the applier builds
/// the tunnel, the cues and the environment in its own Awake, and the spawner loads its chart
/// when it is built - both must already see the profile's choices by then.
///
/// With no profile assigned it does nothing, and the scene plays with whatever the components
/// themselves are set to.
[DefaultExecutionOrder(-1000)]
public class RunProfileSelector : MonoBehaviour
{
    [SerializeField] private RunProfile profile;
    // A playlist - several tracks through one tunnel - takes the profile's place when set.
    [SerializeField] private RunPlaylist playlist;
    // -1 plays the whole playlist; 0, 1, 2... plays that one segment alone (to record it by itself).
    [SerializeField] private int playlistSolo = -1;
    [SerializeField] private CueSpawner cueSpawner;
    [SerializeField] private TunnelThemeApplier themeApplier;
    [SerializeField] private HudController hud;
    [SerializeField] private SilhouetteTrainer trainer;

    public RunProfile Profile => profile;
    public RunPlaylist Playlist => playlist;

    private void Awake()
    {
        if (playlist != null)
        {
            if (hud == null) hud = FindAnyObjectByType<HudController>();
            if (trainer == null) trainer = FindAnyObjectByType<SilhouetteTrainer>();
            var director = gameObject.AddComponent<PlaylistDirector>();
            if (director.Configure(playlist, playlistSolo, cueSpawner, themeApplier, hud, trainer)) return;
            Destroy(director);
        }
        if (profile == null) return;
        if (cueSpawner != null) cueSpawner.SetTrack(profile.track, profile.beatmapFileName);
        if (cueSpawner != null && profile.overrideIntro)
            cueSpawner.SetIntro(profile.introBars, profile.introSkipsPartialSet, profile.preRoll);
        if (themeApplier != null && profile.theme != null) themeApplier.SetTheme(profile.theme);
        Debug.Log("[RunProfile] " + profile.name + ": track " + (profile.track != null ? profile.track.name : "-")
                + ", chart " + profile.beatmapFileName + ", theme " + (profile.theme != null ? profile.theme.name : "-"), this);
    }
}
