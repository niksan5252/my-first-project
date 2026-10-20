using TMPro;
using UnityEngine;

/// The display fonts for the cards and the NEW MOVES intro (the user: "the default TextMesh Pro font
/// is boring"), built by Tunnel/Fonts/Build Display Fonts into Resources/Fonts. Null when missing:
/// the text then keeps TextMesh Pro's default.
public static class DisplayFonts
{
    private static TMP_FontAsset bungee, bungeeShade, luckiest, monoton, russo;

    public static TMP_FontAsset Bungee => bungee ??= Load("Bungee-Regular");             // headlines
    // Bungee Shade's layered glyphs come out as the shade alone in an SDF atlas: kept, not used.
    public static TMP_FontAsset BungeeShade => bungeeShade ??= Load("BungeeShade-Regular");
    public static TMP_FontAsset Luckiest => luckiest ??= Load("LuckiestGuy-Regular");     // the small lines: LET'S GO!
    public static TMP_FontAsset Monoton => monoton ??= Load("Monoton-Regular");           // neon tubes: the worlds' names
    public static TMP_FontAsset Russo => russo ??= Load("RussoOne-Regular");              // the moves' names

    private static TMP_FontAsset Load(string name) => Resources.Load<TMP_FontAsset>("Fonts/" + name + " SDF");

    /// Puts `font` on `t` (if it exists) and the outline back on, which a font swap resets.
    public static void Use(TMP_Text t, TMP_FontAsset font, float outline = 0.2f)
    {
        if (t == null || font == null) return;
        if (t.font != font) t.font = font;
        t.fontStyle &= ~FontStyles.Bold;     // the display faces are heavy already; faux bold smears them
        t.outlineWidth = outline;
        t.outlineColor = new Color32(0, 0, 0, 255);
    }
}
