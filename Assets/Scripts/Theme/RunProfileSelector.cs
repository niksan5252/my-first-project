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
    [SerializeField] private CueSpawner cueSpawner;
    [SerializeField] private TunnelThemeApplier themeApplier;

    public RunProfile Profile => profile;

    private void Awake()
    {
        if (profile == null) return;
        if (cueSpawner != null) cueSpawner.SetTrack(profile.track, profile.beatmapFileName);
        if (themeApplier != null && profile.theme != null) themeApplier.SetTheme(profile.theme);
        Debug.Log("[RunProfile] " + profile.name + ": track " + (profile.track != null ? profile.track.name : "-")
                + ", chart " + profile.beatmapFileName + ", theme " + (profile.theme != null ? profile.theme.name : "-"), this);
    }
}
