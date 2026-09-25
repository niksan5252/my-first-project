using TMPro;
using UnityEngine;

/// Which family of effects a look draws. The look asset holds the numbers; HudSkin holds the
/// drawing for each family.
public enum HudLookKind
{
    SynthChrome,   // A: italic chrome gradient, neon edge, progress bar to the next milestone
    CyberGlitch,   // B: chamfered plate, monospace, RGB-split glitch on +1, typed-out banners
    ArcadeRetro,   // C: pixel font in a pixel frame, hard shadow, banners burst with pixel stars
}

/// How the HUD LOOKS - fonts, sizes, placement, glow and the lengths of its effects. What the HUD
/// says and when stays in HudPresentation; colours stay in the TunnelTheme. Several of these sit
/// on HudSkin and one is active, so a look can be swapped - even during Play - without touching
/// either of the other two.
///
/// Sizes and positions are in the HUD canvas's reference pixels (1920x1080, scaled with the
/// screen), measured from the top-right corner. Effect lengths are in beats, like every other HUD
/// duration.
[CreateAssetMenu(menuName = "Tunnel/HUD Look", fileName = "HudLook")]
public class HudLook : ScriptableObject
{
    public HudLookKind kind;
    public string displayName = "Look";

    [Header("Combo")]
    public TMP_FontAsset comboFont;
    public float comboSize = 180f;
    public float comboSpacing;
    public bool comboItalic;
    // string.Format pattern for the count: "{0}" plain, "{0:000}" zero-padded.
    public string comboFormat = "{0}";
    public Vector2 comboPosition = new Vector2(-60f, -40f);   // top-right corner of the group
    public Vector2 comboBox = new Vector2(540f, 250f);
    public float comboGlow = 0.35f;

    [Header("Label")]
    public TMP_FontAsset labelFont;
    public string labelText = "COMBO";
    public float labelSize = 26f;
    public float labelSpacing = 30f;

    [Header("Banner")]
    public TMP_FontAsset bannerFont;
    public float bannerSize = 60f;
    public bool bannerItalic;
    public Vector2 bannerPosition = new Vector2(-60f, -320f);
    public Vector2 bannerBox = new Vector2(520f, 90f);
    public float bannerGlow = 0.45f;

    [Header("Effects (beats)")]
    public float glitchBeats = 0.25f;        // B: RGB split on every +1
    public float typeBeatsPerLetter = 0.125f; // B: banner typed out, one letter per eighth
    public float starBeats = 1f;             // C: pixel stars flying out of a banner
    public int starCount = 12;
    public float progressEaseBeats = 0.25f;  // A: how fast the bar catches up with the count
}
