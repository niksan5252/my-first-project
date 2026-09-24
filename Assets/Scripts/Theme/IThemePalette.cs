using UnityEngine;

/// Implemented by a theme's environment prefab root (TunnelTheme.environmentPrefab) if it wants
/// to be recolored from the theme's two-color palette — e.g. the sci-fi environment's
/// planet/nebula/starfield swap which accent hue plays ocean vs. land. TunnelThemeApplier calls
/// this once, right before the decorator builds its own content. A prefab that doesn't implement
/// this (e.g. one with fully pre-authored, theme-specific art) is left as-is.
public interface IThemePalette
{
    void ApplyPalette(Color primary, Color secondary, float emissionIntensity);
}
