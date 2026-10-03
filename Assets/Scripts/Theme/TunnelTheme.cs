using UnityEngine;
using UnityEngine.Rendering;

/// The entire swappable visual layer of the tunnel — everything TunnelThemeApplier pushes into
/// NeonTunnelGenerator, CueSpawner, the Global Volume, the camera and the environment anchor at
/// load time. One asset = one look (e.g. the cyan/magenta sci-fi tunnel, or a desert/western
/// variant). Gameplay logic (cue pooling/hit-detection, combo scoring, silhouette triggers,
/// scrolling/recycling) never reads from this asset and never changes when a theme does.
[CreateAssetMenu(menuName = "Tunnel/Theme", fileName = "NewTunnelTheme")]
public class TunnelTheme : ScriptableObject
{
    [Header("Palette (exactly 2 accent colors)")]
    public Color primaryColor = new Color(1f, 0f, 0.8f);
    public Color secondaryColor = new Color(0f, 0.898f, 1f);
    [Range(0f, 2.5f)] public float accentEmissionIntensity = 1.5f;

    [Header("Notes by Hand (optional)")]
    // Off = notes take the two accents at random (the original look). On = every note is coloured
    // by its hand: the left half of the road leftHandColor, the right half rightHandColor, so a
    // mirrored pair of punches reads as two hands at a glance.
    public bool cueColorsByHand;
    public Color leftHandColor = new Color(0f, 0.85f, 1f);
    public Color rightHandColor = new Color(1f, 0.05f, 0.65f);

    [Header("Ring / Arch Style")]
    // Null = the tunnel's own procedural octagon-beam generator (default look). Set this to a
    // prefab to swap the ring shape/mesh entirely; the prefab root should implement IThemeAccent
    // if it wants to be recolored per-ring the way the procedural rings already are.
    public GameObject ringArchPrefab;

    [Header("Floor")]
    // Null = generate a Lit material at runtime from the fields below. Set this only for a theme
    // that needs a genuinely different floor shader/texture, not just a recolor.
    public Material floorMaterialOverride;
    public Color floorColor = new Color(0.025f, 0.02f, 0.04f);
    [Range(0f, 1f)] public float floorMetallic = 0.6f;
    [Range(0f, 1f)] public float floorSmoothness = 0.85f;
    public Color floorGridLineColor = new Color(0f, 0.898f, 1f);
    [Range(0f, 2.5f)] public float floorGridGlintIntensity = 0.5f;
    public Color laneLineColor = new Color(1f, 0f, 0.8f);
    [Range(0f, 2.5f)] public float laneLineEmissionIntensity = 0.55f;

    [Header("Fog")]
    public Color fogColor = new Color(0.05f, 0f, 0.1f);
    [Range(0.05f, 0.9f)] public float fogStartRatio = 0.35f;
    [Range(0.1f, 1f)] public float fogEndRatio = 0.95f;
    // The flat ambient light. Alpha 0 (the default) keeps the night rule - the fog's colour x 0.3,
    // near black; a daylight theme sets its own so shaded sides of props are not black.
    public Color ambientOverride = new Color(0f, 0f, 0f, 0f);
    public Color AmbientColor => ambientOverride.a > 0f ? (Color)(Vector4)ambientOverride * 1f : fogColor * 0.3f;

    [Header("Directional Light")]
    // Scene has no Light by default (a deliberate, load-bearing choice for the pure-emission
    // sci-fi look — every neon material here relies on it). A theme that wants to use ordinary
    // Lit/textured assets (packs built for standard lighting, not emission) needs SOME light for
    // their _BaseMap to become visible at all — but it must stay low enough not to wash out the
    // existing bloom/emission look for themes that don't need it. Intensity 0 (the default,
    // matching Theme_SciFi) keeps a theme exactly as dark/emission-only as before this existed.
    [Range(0f, 1f)] public float lightIntensity = 0f;
    public Color lightColor = Color.white;

    [Header("Sky / Camera Background")]
    // Null = a plain solid-color camera clear (cameraBackgroundColor below). Set this only for a
    // theme that genuinely needs a rendered sky instead of a flat color.
    public Material skyboxMaterial;
    public Color cameraBackgroundColor = new Color(0.06f, 0f, 0.12f);

    [Header("Environment")]
    // Side/background set-dressing (buildings, cacti, planets, mountains...), instantiated once
    // under the Applier's environment anchor when the theme loads. Null = no environment.
    public GameObject environmentPrefab;
    // Optional: what a playlist flies through on its way INTO this theme - a gate at the border
    // between the previous look and this one. Null = this theme's own arch, made large and bright.
    public GameObject gatePrefab;

    [Header("Particles")]
    public ParticlePreset airParticles;

    [Header("Post Processing")]
    // The theme owns the ENTIRE Volume profile (Bloom/Tonemapping/ColorAdjustments/etc), not
    // just a few individual knobs — switching themes just repoints the scene's Global Volume at
    // a different profile asset.
    public VolumeProfile postProcessingProfile;

    [Header("HUD Accents (reserved — no combo/banner HUD exists yet)")]
    public Color hudComboTextColor = new Color(1f, 0f, 0.8f);
    public Color hudBannerBorderColor = new Color(0f, 0.898f, 1f);
}
