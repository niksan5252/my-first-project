// The trainer figure: a flat, near-white fill whose edges turn toward the theme colour, like a neon
// tube seen side-on. Unlit and LDR on purpose - it is drawn into a RenderTexture that the UI shows,
// never through the scene's bloom, and every value stays below 1 so nothing clips into a white
// core. Alpha is always 1: the panel's glow is made from this alpha (SilhouetteGlow.shader).
Shader "Tunnel/SilhouetteBody"
{
    Properties
    {
        _FillColor ("Fill", Color) = (0.78, 0.8, 0.85, 1)
        _RimColor ("Rim (theme)", Color) = (0, 0.85, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FillColor;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                half fres = pow(1.0h - saturate(abs(dot(n, v))), _RimPower);
                half3 c = lerp(_FillColor.rgb, _RimColor.rgb, fres * _RimStrength);
                return half4(saturate(c), 1.0h);
            }
            ENDHLSL
        }
    }
}
