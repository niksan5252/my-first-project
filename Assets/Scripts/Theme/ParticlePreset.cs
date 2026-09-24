using UnityEngine;

/// Reusable air-particle tuning for a theme (sparks, stardust, embers, debris...). A theme
/// references one of these assets instead of embedding the fields directly, so the same preset
/// can be shared across multiple themes and tuned independently of everything else in a theme.
[CreateAssetMenu(menuName = "Tunnel/Particle Preset", fileName = "NewParticlePreset")]
public class ParticlePreset : ScriptableObject
{
    public bool enabled = true;
    // Null = a plain white material tinted by each particle's own start color (the existing
    // default look, capped through the theme's emission intensity). Set this to swap the
    // particle's texture/silhouette entirely (e.g. a debris-chunk sprite instead of a soft dot).
    public Material particleMaterial;
    public float rate = 45f;
    public Vector2 lifetime = new Vector2(1f, 3f);
    public Vector2 speed = new Vector2(5f, 15f);
    public Vector2 size = new Vector2(0.01f, 0.05f);
    [Range(0f, 2.5f)] public float emissionIntensity = 1.1f;
    public ParticleSystemShapeType shapeType = ParticleSystemShapeType.Box;
}
