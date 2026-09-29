using UnityEngine;

/// A thunderclap made from nothing but noise, so the project carries no one else's sound file:
///
///   * the CRACK - a burst of bright noise with a near-instant attack, falling away in a few
///     hundred milliseconds, with a second, smaller split a moment later, the way a close strike
///     tears in two;
///   * the CRACKLE - sparse random clicks riding on the crack's tail, like the discharge fizzing;
///   * the RUMBLE - brown noise low-passed to below ~120 Hz, swelling in after the crack and
///     rolling away over seconds, its level wandering as it rolls.
///
/// Deterministic from its seed, so every render of a video has the same clap. The crack's front
/// sits at CrackAt seconds into the clip, which is what the scheduler lines up with the beat.
public static class ThunderSynth
{
    public const float CrackAt = 0.02f;

    public static AudioClip Create(int sampleRate = 48000, float seconds = 4.5f, int seed = 1337)
    {
        int n = Mathf.CeilToInt(seconds * sampleRate);
        var left = new float[n];
        var right = new float[n];
        var rng = new System.Random(seed);
        float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        // One-pole filters, state per channel.
        float hpL = 0f, hpR = 0f, prevL = 0f, prevR = 0f;
        float brownL = 0f, brownR = 0f, lpL = 0f, lpR = 0f, lp2L = 0f, lp2R = 0f;
        float lowCut = Mathf.Exp(-2f * Mathf.PI * 110f / sampleRate);   // rumble low-pass
        float crackHp = Mathf.Exp(-2f * Mathf.PI * 900f / sampleRate);  // crack high-pass

        // The rumble's level wanders: a few slow random swells.
        var swell = new float[8];
        for (int i = 0; i < swell.Length; i++) swell[i] = 0.6f + 0.4f * (float)rng.NextDouble();

        int crackStart = Mathf.RoundToInt(CrackAt * sampleRate);
        int split = crackStart + Mathf.RoundToInt(0.11f * sampleRate);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sampleRate;
            float nl = Noise(), nr = Noise();

            // Crack: high-passed noise, attack of ~2 ms, decay ~0.25 s, and the split's echo.
            float crackEnv = 0f;
            if (i >= crackStart) crackEnv += Mathf.Min(1f, (i - crackStart) / (0.002f * sampleRate)) * Mathf.Exp(-(i - crackStart) / (0.09f * sampleRate));
            if (i >= split) crackEnv += 0.55f * Mathf.Min(1f, (i - split) / (0.002f * sampleRate)) * Mathf.Exp(-(i - split) / (0.16f * sampleRate));
            hpL = crackHp * (hpL + nl - prevL); prevL = nl;
            hpR = crackHp * (hpR + nr - prevR); prevR = nr;
            float crackL = hpL * crackEnv * 0.9f, crackR = hpR * crackEnv * 0.9f;

            // Crackle: sparse clicks, densest just after the crack, gone after ~1.2 s.
            float crackleRate = 900f * Mathf.Exp(-Mathf.Max(0f, t - CrackAt) / 0.35f);
            if (t > CrackAt && rng.NextDouble() < crackleRate / sampleRate)
            {
                float k = (float)(rng.NextDouble() * 0.6 + 0.2) * Mathf.Exp(-(t - CrackAt) / 0.5f);
                int len = Mathf.Min(n - i, 24);
                bool toLeft = rng.NextDouble() < 0.5;
                for (int j = 0; j < len; j++)
                {
                    float v = k * (1f - j / (float)len) * Noise();
                    if (toLeft) left[i + j] += v; else right[i + j] += v * 0.8f;
                }
            }

            // Rumble: integrated (brown) noise, low-passed twice, swelling in over ~0.25 s and
            // rolling away over ~3 s, its level wandering between the swell points.
            brownL = Mathf.Clamp(brownL * 0.998f + nl * 0.05f, -1f, 1f);
            brownR = Mathf.Clamp(brownR * 0.998f + nr * 0.05f, -1f, 1f);
            lpL = lowCut * lpL + (1f - lowCut) * brownL; lp2L = lowCut * lp2L + (1f - lowCut) * lpL;
            lpR = lowCut * lpR + (1f - lowCut) * brownR; lp2R = lowCut * lp2R + (1f - lowCut) * lpR;
            float since = Mathf.Max(0f, t - CrackAt);
            float rumbleEnv = (1f - Mathf.Exp(-since / 0.08f)) * Mathf.Exp(-since / 1.5f);
            float wander = Mathf.Lerp(swell[Mathf.Min(swell.Length - 1, (int)(t * 2f))],
                                      swell[Mathf.Min(swell.Length - 1, (int)(t * 2f) + 1)], (t * 2f) % 1f);
            float rumbleL = lp2L * rumbleEnv * wander * 9f, rumbleR = lp2R * rumbleEnv * wander * 9f;

            left[i] += crackL + rumbleL;
            right[i] += crackR + rumbleR;
        }

        // Normalise to a peak just under full scale, then fade the last 50 ms to silence.
        float peak = 1e-4f;
        for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(left[i]), Mathf.Abs(right[i]));
        float gain = 0.95f / peak;
        int fade = Mathf.RoundToInt(0.05f * sampleRate);
        var data = new float[n * 2];
        for (int i = 0; i < n; i++)
        {
            float f = i > n - fade ? (n - i) / (float)fade : 1f;
            data[i * 2] = left[i] * gain * f;
            data[i * 2 + 1] = right[i] * gain * f;
        }
        var clip = AudioClip.Create("Thunder (procedural)", n, 2, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
