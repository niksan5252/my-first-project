using UnityEngine;

/// The shape an ArcHold's fist follows (video 24: "not only circles - other patterns with the arms").
/// One function for the cue's tube (CueSpawner.RebuildArc) and for the trainer's hand (Trainer V5's
/// Arc gesture), so the hand rides exactly the path drawn on screen. The offset is in units of the
/// hold's radius, about its centre; `s` runs 0..1 over the hold. For the repeating shapes the
/// chart's `arc` (degrees) sets how many times the pattern goes round (360 = once), its sign the
/// direction it starts in.
public static class ArmPath
{
    public const int Circle = 0, Eight = 1, Wave = 2, Zigzag = 3, UpDown = 4, SideSide = 5;
    public const int Count = 6;

    public static Vector2 Offset(int shape, float sweepDeg, float startDeg, float s)
    {
        float d = sweepDeg < 0f ? -1f : 1f;
        float c = Mathf.Max(1f, Mathf.Abs(sweepDeg) / 360f);
        float tau = Mathf.PI * 2f;
        switch (shape)
        {
            case Eight:     // a figure of eight lying on its side: the infinity sign
                return new Vector2(d * Mathf.Sin(tau * s * c), 0.55f * Mathf.Sin(2f * tau * s * c));
            case Wave:      // across the body, rising and falling
                return new Vector2(d * (2f * s - 1f), 0.45f * Mathf.Sin(2f * tau * s * c));
            case Zigzag:    // across the body in sharp up-down strokes
            {
                float p = s * 2f * c;
                p -= Mathf.Floor(p);
                return new Vector2(d * (2f * s - 1f), 0.5f * (1f - 4f * Mathf.Abs(p - 0.5f)));
            }
            case UpDown:    // straight up and down, from the top
                return new Vector2(0f, Mathf.Cos(tau * s * c));
            case SideSide:  // straight side to side, from the middle
                return new Vector2(d * Mathf.Sin(tau * s * c), 0f);
            default:        // the circle, from startDeg, as it always was
            {
                float a = (startDeg - sweepDeg * s) * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
        }
    }
}
