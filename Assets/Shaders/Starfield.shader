// Renders the background starfield as one additive mesh of camera-facing quads, each carrying
// its own brightness, tint and twinkle phase in its vertex data — so 200+ stars stay a single
// draw call while still looking individually varied.
//
// Additive rather than alpha-blended: stars sit on near-black sky, and additive is what lets a
// bright one bloom while a faint one stays a dim speck, instead of every star compositing to the
// same flat opacity. Nothing here writes depth, and scene fog is deliberately NOT applied — the
// sky is meant to read as far beyond the tunnel's fog volume, so distance is expressed by the
// per-star brightness the mesh already bakes in (see SciFiEnvironmentDecorator.BuildStarfield)
// rather than by fogging stars out to nothing.
Shader "Custom/Starfield"
{
    Properties
    {
        _BaseMap ("Star Sprite", 2D) = "white" {}
        _Intensity ("Intensity", Range(0, 4)) = 1.2
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.35
        _TwinkleSpeed ("Twinkle Speed", Range(0, 8)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Name "StarfieldAdditive"
            Tags { "LightMode"="UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 phase : TEXCOORD1; // x = twinkle phase offset, y = twinkle rate scale
                float4 color : COLOR;     // rgb = tint, a = brightness
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Intensity;
                float _TwinkleAmount;
                float _TwinkleSpeed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                // Each star gets its own phase and rate, so the field shimmers instead of
                // pulsing in unison — a single shared sine reads as the whole sky blinking.
                float twinkle = sin(_Time.y * _TwinkleSpeed * IN.phase.y + IN.phase.x * 6.2831853);
                float brightness = IN.color.a * (1.0 - _TwinkleAmount + _TwinkleAmount * (twinkle * 0.5 + 0.5));

                OUT.color = float4(IN.color.rgb, brightness);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float3 color = sprite.a * IN.color.rgb * IN.color.a * _Intensity;
                return half4(color, 1.0); // alpha is unused: the blend is One One
            }
            ENDHLSL
        }
    }
}
