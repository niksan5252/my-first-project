// Theme_Aurora's sky, as a skybox: a cold dark-blue night over an ice field - near-black navy at
// the zenith, the fog's blue at the horizon - with stars and the aurora (Aurora.hlsl) hanging over
// the far end of the road. The horizon colour is the fog colour, set by AuroraEnvironmentDecorator.
Shader "Tunnel/AuroraSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.005, 0.01, 0.035, 1)
        _HorizonColor ("Horizon (= fog)", Color) = (0.02, 0.04, 0.09, 1)
        _GroundColor ("Below horizon", Color) = (0.01, 0.02, 0.05, 1)
        _StarDensity ("Star density", Range(0, 0.02)) = 0.005
        _StarIntensity ("Star intensity", Range(0, 3)) = 1
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
            #include "Aurora.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GroundColor;
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

            half3 Stars(float3 d)
            {
                float3 p = d * 170.0;
                float3 cell = floor(p);
                float h = AuroraHash31(cell);
                if (h > _StarDensity) return 0;
                float3 centre = cell + 0.25 + 0.5 * float3(AuroraHash31(cell + 11.1), AuroraHash31(cell + 23.7), AuroraHash31(cell + 47.3));
                float core = saturate(1.0 - length(p - centre) / (0.08 + 0.12 * AuroraHash31(cell + 5.5)));
                float twinkle = 0.7 + 0.3 * sin(_StarTime * (1.0 + 2.0 * AuroraHash31(cell + 3.3)) + h * 6283.0);
                return core * core * twinkle * _StarIntensity * smoothstep(0.02, 0.25, d.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 c;
                if (d.y < 0.0) c = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-d.y / 0.15));
                else
                {
                    c = lerp(_HorizonColor.rgb, _ZenithColor.rgb, smoothstep(0.0, 0.55, d.y));
                    c += Stars(d);
                    c += AuroraLight(d);
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
