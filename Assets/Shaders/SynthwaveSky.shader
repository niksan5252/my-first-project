// Theme_Synthwave's sky, as a skybox: a deep indigo zenith falling to a violet and then a sunset
// band at the horizon, stars, and a huge sun sitting on the horizon straight down the road - a
// disc graded yellow at the top through orange to pink at the bottom, cut by horizontal slits
// that thicken towards its base and drift down.
//
// In the sky rather than on geometry so the sun is always on the horizon, never enters the
// tunnel's fog and never needs placing against the camera. The horizon colour is set to the
// fog colour by SynthwaveEnvironmentDecorator, so the fogged far end of the road melts into it.
//
// Animated only through properties set from C# (SynthwaveEnvironmentDecorator, DOTween tweens
// positioned on the song clock): _SunGlow (the pulse), _SunScroll (slit drift), _StarTime.
Shader "Tunnel/SynthwaveSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.03, 0.01, 0.09, 1)
        _MidColor ("Mid sky", Color) = (0.16, 0.04, 0.3, 1)
        _HorizonColor ("Horizon (= fog)", Color) = (0.42, 0.1, 0.34, 1)
        _GlowColor ("Horizon glow", Color) = (1, 0.36, 0.3, 1)
        _GroundColor ("Below horizon", Color) = (0.05, 0.015, 0.08, 1)
        _HorizonGlow ("Horizon glow strength", Range(0, 2)) = 0.55

        _SunTop ("Sun top", Color) = (1, 0.86, 0.3, 1)
        _SunMid ("Sun middle", Color) = (1, 0.45, 0.16, 1)
        _SunBottom ("Sun bottom", Color) = (1, 0.18, 0.5, 1)
        _SunElevation ("Sun centre elevation (rad)", Range(-0.3, 0.5)) = 0.07
        _SunRadius ("Sun radius (rad)", Range(0.02, 0.5)) = 0.2
        _SunIntensity ("Sun intensity", Range(0, 3)) = 0.95
        _SunGlow ("Sun glow (pulse)", Range(0, 3)) = 0.5
        _SunScroll ("Slit drift (0..1)", Float) = 0
        _SlitCount ("Slits", Range(2, 20)) = 9

        _StarDensity ("Star density", Range(0, 0.02)) = 0.004
        _StarIntensity ("Star intensity", Range(0, 3)) = 1.1
        _StarTime ("Star time", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _MidColor, _HorizonColor, _GlowColor, _GroundColor;
                half _HorizonGlow;
                half4 _SunTop, _SunMid, _SunBottom;
                float _SunElevation, _SunRadius, _SunIntensity, _SunGlow, _SunScroll, _SlitCount;
                float _StarDensity, _StarIntensity, _StarTime;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            half3 Stars(float3 dir, float elevation)
            {
                // A star per lit cell of a grid wrapped on the view direction; each sits at a random
                // point of its cell and twinkles on its own phase.
                const float scale = 160.0;
                float3 p = dir * scale;
                float3 cell = floor(p);
                float h = Hash31(cell);
                if (h > _StarDensity) return 0;
                float3 centre = cell + 0.25 + 0.5 * float3(Hash31(cell + 11.1), Hash31(cell + 23.7), Hash31(cell + 47.3));
                float d = length(p - centre);
                float size = 0.09 + 0.12 * Hash31(cell + 5.5);
                float core = saturate(1.0 - d / size);
                float twinkle = 0.65 + 0.35 * sin(_StarTime * (1.5 + 2.5 * Hash31(cell + 3.3)) + h * 6283.0);
                // Fewer and fainter towards the horizon glow.
                float fade = smoothstep(0.03, 0.3, elevation);
                half3 tint = lerp(half3(1.0, 0.85, 1.0), half3(0.75, 0.8, 1.0), Hash31(cell + 9.9));
                return tint * core * core * twinkle * fade * _StarIntensity;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float elevation = asin(clamp(dir.y, -1.0, 1.0));
                float azimuth = atan2(dir.x, dir.z);

                // Sky gradient: horizon -> mid violet -> zenith indigo; flat below the horizon.
                half3 sky;
                if (elevation >= 0.0)
                {
                    float a = saturate(elevation / 0.35);
                    float b = saturate((elevation - 0.25) / 0.9);
                    sky = lerp(_HorizonColor.rgb, _MidColor.rgb, smoothstep(0.0, 1.0, a));
                    sky = lerp(sky, _ZenithColor.rgb, smoothstep(0.0, 1.0, b));
                    // A warm band sitting on the horizon, brightest behind the sun.
                    float band = exp(-elevation / 0.06);
                    float behind = exp(-azimuth * azimuth / 0.35);
                    sky += _GlowColor.rgb * band * _HorizonGlow * (0.45 + 0.55 * behind);
                    sky += Stars(dir, elevation);
                }
                else
                {
                    sky = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-elevation / 0.12));
                }

                // The sun: a disc on an azimuth/elevation plane, graded and slit.
                float2 sp = float2(azimuth, elevation - _SunElevation) / _SunRadius;
                float r = length(sp);
                float y = sp.y;                                   // -1 bottom .. 1 top
                half3 sun = y > 0.1 ? lerp(_SunMid.rgb, _SunTop.rgb, saturate((y - 0.1) / 0.8))
                                    : lerp(_SunBottom.rgb, _SunMid.rgb, saturate((y + 0.9) / 1.0));
                // Slits: none in the top part, growing thicker towards the bottom, drifting down.
                float slitZone = saturate((0.35 - y) / 1.35);
                float stripe = frac(y * _SlitCount * 0.5 + _SunScroll);
                float gap = slitZone * 0.62;
                float aa = fwidth(y * _SlitCount * 0.5) * 1.2;
                float solid = smoothstep(gap - aa, gap + aa, stripe);
                float disc = saturate((1.0 - r) / max(fwidth(r) * 1.5, 1e-4));
                float body = disc * solid * step(0.0, elevation);   // the horizon cuts it
                sky = lerp(sky, sun * _SunIntensity, body);
                // Glow around the sun and through its slits, which pulses.
                float halo = exp(-max(r - 1.0, 0.0) * 3.2) * (1.0 - body * 0.6);
                sky += lerp(_SunBottom.rgb, _SunMid.rgb, 0.5) * halo * _SunGlow * 0.35 * step(0.0, elevation + 0.01);

                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
