// The trainer as a real character: its own texture, lit by a fixed soft key light from the front
// and above (the figure's stage has no lights of its own, and must not depend on the tunnel's), with
// a rim of the segment's colour around its edges - the panel's back light. LDR like SilhouetteBody:
// it is drawn into the panel's RenderTexture, never through the scene's bloom. Alpha is 1, so the
// panel's halo (SilhouetteGlow) can be made from it.
Shader "Tunnel/SilhouetteTextured"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _KeyDir ("Key light direction (towards the light)", Vector) = (0.3, 0.6, 1, 0)
        _Key ("Key", Range(0, 2)) = 0.85
        _Ambient ("Ambient", Range(0, 1)) = 0.45
        _RimColor ("Rim (segment colour)", Color) = (0, 0.85, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.6
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.9
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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _KeyDir;
                half _Key, _Ambient;
                half4 _RimColor;
                half _RimPower, _RimStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                // Wrapped diffuse, so the key light never leaves a hard black terminator on a toon model.
                half wrap = saturate((dot(n, normalize(_KeyDir.xyz)) + 0.35h) / 1.35h);
                half3 lit = albedo * (_Ambient + _Key * wrap);
                half fres = pow(1.0h - saturate(abs(dot(n, v))), _RimPower);
                lit += _RimColor.rgb * fres * _RimStrength;
                return half4(saturate(lit), 1.0h);
            }
            ENDHLSL
        }
    }
}
