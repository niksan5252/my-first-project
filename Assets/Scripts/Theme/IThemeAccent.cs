using UnityEngine;

/// Implemented by a theme's ring-arch prefab root (TunnelTheme.ringArchPrefab) if it wants to be
/// recolored per-ring — NeonTunnelGenerator calls this once per instantiated ring, alternating
/// the theme's two accent colors the same way the procedural octagon rings already do. A prefab
/// that doesn't implement this (e.g. one with its own pre-authored materials) is left as-is.
public interface IThemeAccent
{
    void ApplyAccent(Color color, float emissionIntensity);
}
