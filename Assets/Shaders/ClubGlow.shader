// Additive light for the club theme: laser beams, spotlight cones, strobe flares, speaker cones.
// The shape is a greyscale mask (Kenney light masks, or a gradient made in code), coloured and
// scaled by _Color x _Intensity - both driven per object from C#, in time with ClubBeat. Adds only
// light, writes no depth, and fades into the theme's fog like everything else.
Shader "Tunnel/ClubGlow"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 0.45, 0.05, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
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
                float4 _MainTex_ST;
                half4 _Color;
                half _Intensity;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                half3 c = _Color.rgb * m * _Intensity;
                // Additive: fog fades the light out (towards black) rather than tinting it.
                c = MixFogColor(c, half3(0, 0, 0), i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
