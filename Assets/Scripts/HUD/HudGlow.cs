using TMPro;
using UnityEngine;

/// The one place that knows how a HUD label is made to glow, so the counter and the banner cannot
/// drift apart. Everything here goes through the text's own material instance (`fontMaterial`,
/// which TMP clones per object on first access), never the shared font material - writing to the
/// shared one would recolour every label using that font, including any added later.
///
/// Why glow and outline rather than simply a brighter colour: the scene's bloom has a threshold
/// of 0.8, so a label bright enough to bloom is already a label whose glyph core is clipping to
/// white. Glow puts the bloom-feeding brightness OUTSIDE the glyph and leaves the core at the
/// theme's own colour, which is what keeps a magenta "124" magenta instead of a white slab.
public static class HudGlow
{
    /// Colours a label and gives it a neon edge. Returns the resting glow power, which callers
    /// keep so a pulse or a flicker has a value to swing around.
    public static float Apply(TMP_Text text, Color color, float power)
    {
        if (text == null) return power;

        Material mat = text.fontMaterial;
        mat.EnableKeyword(ShaderUtilities.Keyword_Glow);
        mat.SetColor(ShaderUtilities.ID_GlowColor, color);
        mat.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
        mat.SetFloat(ShaderUtilities.ID_GlowInner, 0.05f);
        mat.SetFloat(ShaderUtilities.ID_GlowOuter, 0.35f);
        mat.SetFloat(ShaderUtilities.ID_GlowPower, power);

        // A dark outline between the glyph and its glow. Without it the glow eats into the
        // letterform on a bright background and the text loses its edge.
        mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.02f, 0.02f, 0.06f, 1f));
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.12f);

        return power;
    }

    public static void SetPower(TMP_Text text, float power)
    {
        if (text == null) return;
        text.fontMaterial.SetFloat(ShaderUtilities.ID_GlowPower, Mathf.Max(0f, power));
    }
}
