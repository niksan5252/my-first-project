// Theme_Ocean's sky, as a skybox: under water there is no sky, only the water column - near-black
// navy below, the fog's deep blue at eye level, brightening towards the surface overhead, where a
// wide soft window of daylight shimmers with a moving caustic pattern (the Kenney caustics mask,
// seen from below). The eye-level colour is the fog colour, set by OceanEnvironmentDecorator, so
// the fogged far end of the tunnel melts into the water.
//
// _Shimmer (phase of the surface pattern) and _SurfaceGlow come from C# on the song clock.
Shader "Tunnel/OceanSky"
{
    Properties
    {
        _DeepColor ("Below", Color) = (0.0, 0.01, 0.03, 1)
        _HorizonColor ("Eye level (= fog)", Color) = (0.01, 0.06, 0.12, 1)
        _UpColor ("Towards the surface", Color) = (0.03, 0.2, 0.32, 1)
        _SurfaceColor ("Surface light", Color) = (0.35, 0.75, 0.85, 1)
        _SurfaceGlow ("Surface glow", Range(0, 2)) = 0.6
        _Caustics ("Caustics mask", 2D) = "black" {}
        _Shimmer ("Shimmer phase", Float) = 0
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
                half4 _DeepColor, _HorizonColor, _UpColor, _SurfaceColor;
                half _SurfaceGlow;
                float4 _Caustics_ST;
                float _Shimmer;
            CBUFFER_END
            TEXTURE2D(_Caustics);
            SAMPLER(sampler_linear_repeat);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 c;
                if (d.y < 0.0) c = lerp(_HorizonColor.rgb, _DeepColor.rgb, saturate(-d.y / 0.35));
                else
                {
                    c = lerp(_HorizonColor.rgb, _UpColor.rgb, smoothstep(0.0, 0.6, d.y));
                    // The surface seen from below: a plane overhead, projected along the view ray.
                    float2 uv = d.xz / max(d.y, 0.08) * 0.35;
                    float a = SAMPLE_TEXTURE2D(_Caustics, sampler_linear_repeat, uv + float2(_Shimmer * 0.11, _Shimmer * 0.07)).a;
                    float b = SAMPLE_TEXTURE2D(_Caustics, sampler_linear_repeat, uv * 0.8 - float2(_Shimmer * 0.05, _Shimmer * 0.12)).a;
                    float window = smoothstep(0.25, 0.95, d.y);
                    float web = a * 0.5 + b * 0.5;
                    c += _SurfaceColor.rgb * window * _SurfaceGlow * (0.4 + 0.6 * web * web);
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
