// The aurora itself, shared by the sky (Tunnel/AuroraSky) and the ice that mirrors it
// (Tunnel/AuroraIce), so the reflection is the same light as the sky above it.
//
// Two curtains of light hung over the far end of the road, in angular coordinates (azimuth,
// elevation): each has a sharp lower edge that wanders and folds along the azimuth, fine vertical
// rays, and fades upward, its colour climbing from _AuroraLow (green, the lower edge) to
// _AuroraHigh (violet/pink, the top).
// Driven by globals, set every frame by AuroraEnvironmentDecorator from DOTween tweens on the song
// clock:
//   _AuroraPhase - how far the curtains have drifted and folded (song time plus a beat sway);
//   _AuroraWave  - how deeply they fold, swelling with the bar;
//   _AuroraGain  - brightness, breathing softly on the beats;
//   _AuroraLow, _AuroraHigh - the colours.
#ifndef TUNNEL_AURORA_INCLUDED
#define TUNNEL_AURORA_INCLUDED

float _AuroraPhase, _AuroraWave, _AuroraGain;
float4 _AuroraLow, _AuroraHigh;

float3 AuroraCurtain(float a, float e, float i, float ph, float wave, float centre, float width)
{
    // The curtain's lower edge wanders in elevation along the azimuth and folds with the wave.
    float edge = 0.2 + 0.09 * i + 0.055 * sin(a * 2.3 + ph * 0.35 + i * 1.7)
               + wave * 0.035 * sin(a * 5.3 - ph * 0.8 + i * 2.1);
    float u = e - edge;
    float bottom = smoothstep(-0.018, 0.006, u);
    float up = exp(-max(u, 0.0) / (0.11 + 0.04 * i));
    // Fine vertical rays, drifting.
    float rays = 0.45 + 0.55 * pow(0.5 + 0.5 * sin(a * 38.0 + 3.0 * sin(a * 6.0 + ph * 0.4) + ph * 0.25), 1.5);
    float span = exp(-pow((a - centre) / width, 2.0));
    float3 col = lerp(_AuroraLow.rgb, _AuroraHigh.rgb, saturate(u / 0.2));
    return col * bottom * up * rays * span;
}

float3 AuroraLight(float3 d)
{
    if (d.y <= 0.005) return 0;
    float a = atan2(d.x, d.z);
    float e = asin(saturate(d.y));
    float ph = _AuroraPhase;
    float3 c = AuroraCurtain(a, e, 0.0, ph, _AuroraWave, -0.35, 0.9)
             + 0.8 * AuroraCurtain(a, e, 1.0, ph * 0.8 + 3.0, _AuroraWave, 0.55, 0.8);
    return c * _AuroraGain * smoothstep(0.005, 0.08, d.y);
}

float AuroraHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

#endif
