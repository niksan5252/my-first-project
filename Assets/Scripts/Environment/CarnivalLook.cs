using UnityEngine;

/// The three looks of Theme NeonCarnival (video 7), a neon Brazilian carnival. One set of
/// components - RingArchCarnival and NeonCarnivalEnvironment - serves all three; each prefab
/// carries its skin and the third colour the theme's two accents have no room for.
public enum CarnivalSkin
{
    RioNight,     // green and yellow; hills and the city in neon outline, garlands of bulbs pulsing on the beat
    Carnaval,     // pink, orange, violet; feather fans that open on the strong beats, confetti on the drops
    Copacabana,   // cyan, white, crimson; the wavy promenade floor, neon palms, waves of light down the floor
}

/// Shared timing rules of the carnival skins, all read from ClubBeat (the song's clock).
public static class CarnivalLook
{
    // Copacabana's floor wave: it leaves the camera on each bar line and reaches WaveSpan metres
    // down the road by the end of the bar.
    public const float WaveSpan = 80f;
    private const float WaveWidth = 6f;

    public static float Beats => ClubBeat.BeatIndex + ClubBeat.BeatPhase;

    /// 0..1 through the current bar.
    public static float BarPosition => (Mathf.Repeat(ClubBeat.BeatIndex, 4f) + ClubBeat.BeatPhase) / 4f;

    /// Beats one and three of the bar.
    public static bool StrongBeat => (ClubBeat.BeatIndex & 1) == 0;

    public static bool InDrop => ClubBeat.Energy > 1.15f;

    /// How much of the bar's wave is at world depth z, 0..1.
    public static float Wave(float z)
    {
        float d = (z - BarPosition * WaveSpan) / WaveWidth;
        return Mathf.Exp(-d * d);
    }

    /// Picks one of three colours by index.
    public static Color Of(int i, Color a, Color b, Color c)
    {
        switch (((i % 3) + 3) % 3) { case 0: return a; case 1: return b; default: return c; }
    }
}
