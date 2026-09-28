using UnityEngine;

namespace TunnelRender
{
    /// <summary>
    /// What the offline silhouette render records. To render another move: import its FBX
    /// (Tools/Silhouette Render/Import Animation FBX...), put its clip here, change the output folder, Render.
    /// Editor-only: this lives in an Editor folder and never ships in a build.
    /// </summary>
    [CreateAssetMenu(menuName = "Tunnel/Silhouette Render Settings")]
    public class SilhouetteRenderSettings : ScriptableObject
    {
        [Tooltip("Humanoid clip to play on the character.")]
        public AnimationClip clip;

        [Tooltip("Optional clips played right after 'clip', e.g. crouch down, then stand up.")]
        public AnimationClip[] then = new AnimationClip[0];

        [Tooltip("Output folder, relative to the project root. Existing PNGs in it are replaced.")]
        public string outputFolder = "Recordings/Silhouette_Wave";

        [Min(1)] public int loops = 1;
        [Min(1)] public int fps = 30;
        [Min(16)] public int width = 1920;
        [Min(16)] public int height = 1080;

        [Tooltip("Outline thickness in pixels at the output resolution.")]
        [Range(1f, 20f)] public float outlinePx = 6f;

        [Tooltip("Outline colour. Default is the trainer's cyan from the tunnel (0, 0.85, 1).")]
        public Color outlineColor = new Color(0f, 0.85f, 1f, 1f);

        [Tooltip("How far the neon glow spreads outside the outline, in pixels at the output resolution. 0 = no glow.")]
        [Range(0f, 60f)] public float glowRadiusPx = 16f;

        [Tooltip("Glow strength.")]
        [Range(0f, 3f)] public float glowIntensity = 1f;

        [Tooltip("Empty space above the head and below the feet, as a fraction of the frame height.")]
        [Range(0f, 0.3f)] public float verticalMargin = 0.06f;

        [Tooltip("Render at this multiple of the output size, then downsample: smooth edges.")]
        [Range(1, 4)] public int supersample = 2;
    }
}
