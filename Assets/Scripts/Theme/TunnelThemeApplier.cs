using UnityEngine;
using UnityEngine.Rendering;

/// Single entry point that reads the assigned TunnelTheme and applies it to every themeable
/// system in the scene: NeonTunnelGenerator, CueSpawner, the Global Volume's post-processing
/// profile, the camera's background, and the environment prefab. Gameplay logic (cue pooling,
/// hit-detection, combo scoring, silhouette triggers, scrolling) never reads TunnelTheme —
/// this is the only place that does, and it never touches that logic itself.
///
/// Apply(theme) is public and safe to call again at runtime, so themes can be swapped live for
/// A/B comparison without reloading the scene.
public class TunnelThemeApplier : MonoBehaviour
{
    [SerializeField] private TunnelTheme theme;
    [SerializeField] private NeonTunnelGenerator tunnelGenerator;
    [SerializeField] private CueSpawner cueSpawner;
    [SerializeField] private Volume globalVolume;
    [SerializeField] private Camera targetCamera;
    // Where the environment prefab gets instantiated. Defaults to this GameObject's own
    // transform if left unassigned.
    [SerializeField] private Transform environmentAnchor;
    // Optional: only themes that need ordinary Lit/textured assets set lightIntensity above 0.
    // Disabled outright (not just intensity 0) when a theme doesn't need it, so a pure-emission
    // theme's render is byte-for-byte what it would be with no Light in the scene at all.
    [SerializeField] private Light directionalLight;
    // Optional. The HUD reads only the theme's two reserved hud* colours and its accents, so a
    // scene without a HUD is unaffected and a theme swap recolours the counter and banner along
    // with everything else.
    [SerializeField] private HudController hud;

    private GameObject spawnedEnvironment;

    private void Awake()
    {
        if (environmentAnchor == null) environmentAnchor = transform;
        Apply(theme);
    }

    public void Apply(TunnelTheme newTheme)
    {
        if (newTheme == null) return;
        theme = newTheme;

        ApplyPostProcessing();
        ApplyCameraBackground();
        ApplyLight();

        if (tunnelGenerator != null)
        {
            tunnelGenerator.Configure(theme);
            tunnelGenerator.Build();
        }

        if (cueSpawner != null)
        {
            cueSpawner.Configure(theme);
            cueSpawner.Build();
        }

        if (hud != null) hud.ApplyTheme(theme);

        ApplyEnvironment();
    }

    private void ApplyPostProcessing()
    {
        if (globalVolume == null || theme.postProcessingProfile == null) return;
        // sharedProfile (not .profile): repoints the Volume at the theme's own persisted asset
        // directly, rather than creating a throwaway runtime copy. See NeonTunnelGenerator's old
        // ConfigureBloom/ConfigureColorGrading (removed) — the theme now owns the whole profile.
        globalVolume.sharedProfile = theme.postProcessingProfile;
    }

    private void ApplyCameraBackground()
    {
        if (targetCamera == null) return;

        if (theme.skyboxMaterial != null)
        {
            RenderSettings.skybox = theme.skyboxMaterial;
            targetCamera.clearFlags = CameraClearFlags.Skybox;
        }
        else
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = theme.cameraBackgroundColor;
        }
    }

    private void ApplyLight()
    {
        if (directionalLight == null) return;
        directionalLight.enabled = theme.lightIntensity > 0.0001f;
        directionalLight.intensity = theme.lightIntensity;
        directionalLight.color = theme.lightColor;
    }

    private void ApplyEnvironment()
    {
        if (spawnedEnvironment != null)
        {
            Destroy(spawnedEnvironment);
            spawnedEnvironment = null;
        }

        if (theme.environmentPrefab == null) return;

        spawnedEnvironment = Instantiate(theme.environmentPrefab, environmentAnchor);

        if (spawnedEnvironment.TryGetComponent<IThemePalette>(out var palette))
            palette.ApplyPalette(theme.primaryColor, theme.secondaryColor, theme.accentEmissionIntensity);

        if (spawnedEnvironment.TryGetComponent<IEnvironmentDecorator>(out var decorator))
        {
            float tunnelLength = tunnelGenerator != null ? tunnelGenerator.TunnelLength : 0f;
            // The tunnel's scroll speed goes with it: an environment that runs alongside the road
            // (the cyberpunk street) has to travel at exactly the tunnel's speed or it reads as
            // sliding, and it can only know that speed if it is handed over here.
            float scrollSpeed = tunnelGenerator != null ? tunnelGenerator.ScrollSpeed : 0f;
            decorator.Initialize(tunnelLength, scrollSpeed);
        }
    }
}
