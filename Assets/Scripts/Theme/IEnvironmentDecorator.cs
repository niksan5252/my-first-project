/// Implemented by a theme's environment prefab root (TunnelTheme.environmentPrefab) if it needs
/// an explicit build step after instantiation — e.g. procedurally generating its own content, the
/// way SciFiEnvironmentDecorator does. TunnelThemeApplier calls this once, right after
/// ApplyPalette (if the prefab also implements IThemePalette). A prefab with fully static,
/// pre-authored content doesn't need to implement this at all.
///
/// scrollSpeed is the tunnel's own scroll speed, handed over so an environment that has to move
/// WITH the tunnel (a street running alongside it, as opposed to a fixed sky backdrop) can do its
/// own scrolling and recycling without reaching back into NeonTunnelGenerator. A decorator that
/// is a static backdrop simply ignores it.
public interface IEnvironmentDecorator
{
    void Initialize(float tunnelLength, float scrollSpeed);
}
