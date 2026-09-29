// Theme_HalloweenGraveyard's sky: a clear, cold night over a cemetery. Deep navy overhead fading to
// the fog's moonlit blue-grey at the horizon, a scatter of stars, slow ragged clouds drifting across,
// and a HUGE full moon hanging over the far end of the road - its disc mottled with maria, a soft
// halo around it and a wide ring of haze. The moon breathes with the music through _MoonPulse
// (driven from C# on the song clock), and a lightning flash lifts the whole sky through _Flash.
//
// Every value stays near or below the bloom threshold except the moon's core, which is allowed a
// little over it so the bloom makes a glow rather than a white blob.
Shader "Tunnel/GraveyardSky"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.01, 0.015, 0.05, 1)
        _HorizonColor ("Horizon (= fog)", Color) = (0.07, 0.1, 0.17, 1)
        _MoonColor ("Moon", Color) = (0.85, 0.92, 1.0, 1)
        _MoonHalo ("Moon halo", Color) = (0.35, 0.55, 0.9, 1)
        _MoonDir ("Moon direction", Vector) = (0.18, 0.2, 1, 0)
        _MoonSize ("Moon angular radius (rad)", Range(0.02, 0.4)) = 0.17
        _MoonPulse ("Moon pulse (0..1, from C#)", Range(0, 1)) = 0
        _CloudColor ("Clouds", Color) = (0.03, 0.04, 0.07, 1)
        _CloudSpeed ("Cloud drift", Float) = 0.004
        _Stars ("Stars", Range(0, 2)) = 1
        _Flash ("Lightning flash (from C#)", Range(0, 1)) = 0
        _FlashColor ("Flash colour", Color) = (0.6, 0.75, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _HorizonColor, _MoonColor, _MoonHalo, _CloudColor, _FlashColor;
                float4 _MoonDir;
                float _MoonSize, _MoonPulse, _CloudSpeed, _Stars, _Flash;
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

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x), lerp(hash21(i + float2(0, 1)), hash21(i + 1), u.x), u.y);
            }
            float fbm(float2 p) { float a = 0.5, s = 0; for (int k = 0; k < 5; k++) { s += a * noise(p); p *= 2.03; a *= 0.5; } return s; }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = saturate(d.y);
                half3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(up, 0.55));
                // Below the horizon: the fog colour, a little darker - never visible past the scenery.
                if (d.y < 0) col = _HorizonColor.rgb * 0.8;

                // Stars: a cell grid on the sky's own coordinates, a few per cell lit, twinkling slowly.
                float2 sp = float2(atan2(d.x, d.z), asin(clamp(d.y, -1, 1))) * 90.0;
                float2 cell = floor(sp);
                float star = hash21(cell);
                float2 at = frac(sp) - (0.25 + 0.5 * float2(hash21(cell + 7.1), hash21(cell + 3.3)));
                float tw = 0.6 + 0.4 * sin(_Time.y * (1.5 + star * 3) + star * 40);
                col += step(0.955, star) * saturate(1 - length(at) * 7) * tw * _Stars * 0.55 * smoothstep(0.02, 0.25, up);

                // The moon.
                float3 md = normalize(_MoonDir.xyz);
                float ang = acos(clamp(dot(d, md), -1, 1));
                float r = ang / _MoonSize;
                float3 side = normalize(cross(md, float3(0, 1, 0)));
                float3 upv = cross(side, md);
                float2 muv = float2(dot(d, side), dot(d, upv)) / _MoonSize;
                float maria = fbm(muv * 2.2 + 3.1) * 0.6 + fbm(muv * 7.0) * 0.4;
                float disc = smoothstep(1.0, 0.985, r);
                half3 moon = _MoonColor.rgb * (0.78 + 0.22 * (1 - smoothstep(0.35, 0.75, maria)));
                moon *= 1 - 0.18 * r * r;                       // a little limb darkening
                float pulse = _MoonPulse;
                half3 halo = _MoonHalo.rgb * (exp(-(r - 1) * 2.2) * 0.55 + exp(-(r - 1) * 0.45) * 0.22) * (1 + 0.8 * pulse);
                col = lerp(col + (r > 1 ? halo : 0), moon * (1.05 + 0.25 * pulse), disc);

                // Clouds: ragged fbm bands drifting sideways, thin over the moon so it shows through
                // them, lit on their moon side.
                float2 cp = float2(atan2(d.x, d.z) * 2.2 + _Time.y * _CloudSpeed * 10, d.y * 7.0);
                float c = smoothstep(0.52, 0.8, fbm(cp * float2(1.4, 2.5)));
                c *= smoothstep(0.0, 0.08, d.y) * (1 - smoothstep(0.35, 0.7, d.y));
                half3 cloud = _CloudColor.rgb + _MoonHalo.rgb * 0.25 * exp(-max(0, r - 1) * 0.6);
                col = lerp(col, cloud, c * (disc > 0.5 ? 0.55 : 0.85));

                col += _FlashColor.rgb * _Flash * (0.35 + 0.65 * up);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
