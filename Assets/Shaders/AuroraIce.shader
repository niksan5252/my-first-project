// Theme_Aurora's ground: the road as dark clear ice and the snowfield beside it. Both mirror the
// aurora (Aurora.hlsl, the same light as the sky, looked up along the reflected view ray), the ice
// strongly and sharply, the snow only faintly; the ice also shows a web of pale cracks frozen in,
// mapped in world X/Z and travelling with the road at the tunnel's speed on SONG time
// (_ClubSongTime from ClubBeat, _SynthScrollSpeed from the environment decorator).
// Fresnel: the reflection grows towards grazing angles, as on real ice. Fades to the fog colour by
// its own distance curve. Opaque, unlit.
Shader "Tunnel/AuroraIce"
{
    Properties
    {
        _BaseColor ("Base", Color) = (0.01, 0.025, 0.05, 1)
        _CrackColor ("Cracks", Color) = (0.35, 0.55, 0.7, 1)
        _CrackIntensity ("Crack intensity", Range(0, 2)) = 0.35
        _CrackScale ("Crack cell (m)", Float) = 2.4
        _Reflection ("Reflection", Range(0, 2)) = 1
        _FresnelPower ("Fresnel power", Range(0.5, 8)) = 3
        _Ripple ("Reflection ripple", Range(0, 0.2)) = 0.03
        _ScrollScale ("Scroll (x tunnel speed)", Float) = 1
        _FadeColor ("Fade colour (= fog)", Color) = (0.02, 0.04, 0.09, 1)
        _FadeStart ("Fade start", Float) = 20
        _FadeEnd ("Fade end", Float) = 140
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Ice"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Aurora.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _CrackColor;
                float _CrackIntensity, _CrackScale, _Reflection, _FresnelPower, _Ripple, _ScrollScale;
                half4 _FadeColor;
                float _FadeStart, _FadeEnd;
            CBUFFER_END
            float _ClubSongTime, _SynthScrollSpeed;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float2 Hash22(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.xx + q.yz) * q.zy);
            }

            // Distance to the nearest Voronoi edge: small along the cell borders (the cracks).
            float CrackEdge(float2 p)
            {
                float2 g = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float2 r = o + Hash22(g + o) - f;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return sqrt(d2) - sqrt(d1);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                float2 w = float2(p.x, p.z + _ClubSongTime * _SynthScrollSpeed * _ScrollScale);
                float3 view = normalize(p - _WorldSpaceCameraPos);

                float edge = CrackEdge(w / _CrackScale);
                float fw = fwidth(w.x / _CrackScale) + 1e-4;
                float crack = 1.0 - smoothstep(0.0, 0.04 + fw * 1.5, edge);
                float fine = 1.0 - smoothstep(0.0, 0.03 + fw * 2.0, CrackEdge(w / (_CrackScale * 0.37) + 7.3));

                // The mirror: the aurora along the reflected ray, nudged by the cracks' facets.
                float3 n = float3(0, 1, 0);
                // A gentle, smooth waviness (no per-cell steps, which read as tiles).
                float2 jitter = float2(sin(w.x * 1.7 + w.y * 0.6), sin(w.y * 1.3 - w.x * 0.8)) * _Ripple;
                n = normalize(n + float3(jitter.x, 0, jitter.y));
                float3 r = reflect(view, n);
                float fres = pow(1.0 - saturate(dot(-view, n)), _FresnelPower);
                half3 c = _BaseColor.rgb;
                c += AuroraLight(r) * _Reflection * (0.25 + 0.75 * fres);
                c += _CrackColor.rgb * (crack + 0.4 * fine) * _CrackIntensity;

                float dist = distance(p, _WorldSpaceCameraPos);
                float f = saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 1.0));
                c = lerp(c, _FadeColor.rgb, f * f * (3.0 - 2.0 * f));
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
