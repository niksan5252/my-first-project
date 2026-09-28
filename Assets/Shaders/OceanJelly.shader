// Theme_Ocean's jellyfish: additive, translucent light. The bell glows mostly at its silhouette
// (rim term from the view angle) with a faint core, which is what makes a dome read as a
// see-through jelly; the tentacles use a high _Core and no rim. Colour and brightness per object
// from C# (OceanEnvironmentDecorator, DOTween on the song clock). Fades into the fog (towards
// black, as additive light should).
Shader "Tunnel/OceanJelly"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.45, 0.6, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1
        _Core ("Core (flat part)", Range(0, 1)) = 0.15
        _Rim ("Rim strength", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity, _Core, _Rim;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(pw);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(pw);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float nv = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float rim = pow(1.0 - nv, 2.0) * _Rim;
                half3 c = _Color.rgb * _Intensity * (_Core + rim);
                c = MixFogColor(c, half3(0, 0, 0), i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
