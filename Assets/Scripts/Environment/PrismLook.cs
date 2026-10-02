using UnityEngine;

/// The four looks of Theme NeonPrism (video 6). One set of components - RingArchPrism and
/// NeonPrismEnvironment - serves all four; each prefab just carries a different skin.
public enum PrismSkin
{
    RedLaser,       // red and white; a fan of laser beams on the strong beats
    Ultraviolet,    // deep violet, blacklight; glowing dots and dust
    CyanPulse,      // teal and blue; a wave of light runs down the frames every bar
    Spectrum,       // every colour, the hue drifting phrase by phrase; maximum bloom on the drops
}

/// Shared timing and colour rules of the prism skins, all read from ClubBeat (the song's clock).
public static class PrismLook
{
    // CyanPulse's wave: it leaves the camera at the start of each bar and reaches WaveSpan metres
    // down the tunnel by the end of it.
    public const float WaveSpan = 90f;
    private const float WaveWidth = 7f;

    private static float BeatsNow => ClubBeat.BeatIndex + ClubBeat.BeatPhase;

    /// 0..1 through the current bar.
    public static float BarPosition => (Mathf.Repeat(ClubBeat.BeatIndex, 4f) + ClubBeat.BeatPhase) / 4f;

    /// Beats one and three of the bar - the strong beats.
    public static bool StrongBeat => (ClubBeat.BeatIndex & 1) == 0;

    /// How much of the bar's wave is at world depth z, 0..1.
    public static float Wave(float z)
    {
        float d = (z - BarPosition * WaveSpan) / WaveWidth;
        return Mathf.Exp(-d * d);
    }

    /// Spectrum's base hue, 0..1: one step round the wheel per 4-bar phrase, taken over the phrase's last
    /// bar so the colour glides to the next one on the phrase boundary instead of jumping.
    public static float PhraseHue
    {
        get
        {
            float phrase = BeatsNow / 16f;
            float whole = Mathf.Floor(phrase);
            float glide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((phrase - whole - 0.75f) / 0.25f));
            return Mathf.Repeat((whole + glide) * 0.125f, 1f);
        }
    }

    public static Color Spectrum(float hueOffset, float value = 1f) =>
        Color.HSVToRGB(Mathf.Repeat(PhraseHue + hueOffset, 1f), 0.92f, value);
}
