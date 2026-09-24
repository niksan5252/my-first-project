// The sustained-note (hold) counterpart to Custom/CueVolumetric. It keeps that shader's whole
// look — the fixed-direction half-Lambert fill and the Fresnel rim that fake a lit bevel in a
// scene with no Light components — and adds the two things a hold needs and an opaque shader
// fundamentally cannot give it:
//
//   1. A fade that runs ALONG the strip. _HoldProgress is how much of the note the hit zone has
//      already swept through, 0 at the near end and 1 at the far one, and everything behind it
//      is faded OUT rather than darkened. This is why the strip can be one single unbroken mesh:
//      the played part is tracked by a parameter, so the geometry never has to be chopped into
//      per-cell pieces that show their seams.
//   2. Fading that actually removes pixels. The previous approach tinted spent geometry toward
//      the frame ink, which on an opaque surface reads as solid black blocks sliding past the
//      camera. Here a spent pixel's alpha reaches zero and clip() discards it, so there is no
//      stage at which a dark opaque remnant can exist.
//
// Alpha blending with ZWrite ON plus clip() rather than plain transparency: a fully spent pixel
// is discarded and writes no depth, while a surviving one is effectively opaque and sorts
// against the rest of the scene normally. That keeps the head plate and its beam ordering
// correctly against each other without the draw-order lottery that a pure transparent queue
// brings, which matters when several holds in different lanes overlap on screen.
Shader "Custom/CueHoldStrip"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.1, 0.1, 0.1, 1)
        _EmissionColor ("Emission Color", Color) = (1, 0, 0.8, 1)
        _RimColor ("Rim Color", Color) = (1, 0, 0.8, 1)
        _FakeLightDir ("Fake Light Direction", Vector) = (0.4, 0.8, -0.4, 0)
        _FakeLightIntensity ("Fake Light Intensity", Range(0, 2)) = 0.6
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 1.3

        // How far the hit zone has swept along this strip, 0..1 from its near end.
        _HoldProgress ("Hold Progress", Range(0, 1)) = 0
        // Width of the fade band, in the same 0..1 units, so the boundary is a soft wipe and
        // never a hard edge chasing the note down the lane.
        _HoldSoftness ("Hold Fade Softness", Range(0.001, 0.5)) = 0.12
        // 1 for a strip that has not been struck yet; raised while the note is being held so the
        // whole length lights up as feedback that it is live.
        _HoldBoost ("Hold Brightness Boost", Range(0.5, 4)) = 1
        // Global fade, used for the dissolve at the end of the note. Multiplies the gradient.
        _HoldAlpha ("Hold Alpha", Range(0, 1)) = 1
        // 1 for the long strip itself; 0 for compact parts (the head plate, its border) that sit
        // at one point along the note and so fade as a whole via _HoldAlpha instead.
        _HoldGradient ("Use Length Gradient", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float holdT : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float4 _RimColor;
                float4 _FakeLightDir;
                float _FakeLightIntensity;
                float _RimPower;
                float _RimIntensity;
                float _HoldProgress;
                float _HoldSoftness;
                float _HoldBoost;
                float _HoldAlpha;
                float _HoldGradient;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // The strip is authored as a UNIT cube and stretched to length by the transform,
                // so object space always spans -0.5..0.5 along Z no matter how long the note is.
                // Reading the gradient here rather than from world space is what keeps a 2-cell
                // and a 6-cell note fading at the same relative rate.
                OUT.holdT = IN.positionOS.z + 0.5;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float3 L = normalize(_FakeLightDir.xyz);

                float ndotl = saturate(dot(N, L) * 0.5 + 0.5);
                float3 lit = _BaseColor.rgb * ndotl * _FakeLightIntensity;

                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimIntensity;
                float3 color = (_EmissionColor.rgb + _RimColor.rgb * rim + lit) * _HoldBoost;

                // Ahead of the sweep: untouched. Behind it: gone. The band between is the wipe.
                float swept = smoothstep(_HoldProgress - _HoldSoftness, _HoldProgress + _HoldSoftness, IN.holdT);
                float gradient = lerp(1.0, swept, _HoldGradient);
                float alpha = gradient * _HoldAlpha;

                // Discard before anything spent can be drawn at all, so no darkened remnant of a
                // played note can ever appear on screen.
                clip(alpha - 0.004);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
