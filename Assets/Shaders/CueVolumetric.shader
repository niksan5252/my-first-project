// Gives the cue cubes a sense of volume/bevel without any scene Light — this project's tunnel
// scenes have zero Light components (a deliberate choice for the pure-emission neon look), so
// URP Lit's normal specular/diffuse response never shows regardless of Smoothness/Metallic (see
// NeonTunnelGenerator's floor material comments for the same constraint). Instead this shader
// fakes it entirely in the fragment stage: a fixed-direction half-Lambert term (independent of
// any scene Light) shades the base color across the faces, and a Fresnel/rim term brightens the
// emission color at grazing angles — together reading as a lit, beveled surface purely from
// view/normal geometry. _BaseColor/_EmissionColor keep the exact same names CueSpawner already
// sets per-instance via MaterialPropertyBlock, so the existing per-cue color-alternation code
// needs no changes.
Shader "Custom/CueVolumetric"
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
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

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
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float4 _RimColor;
                float4 _FakeLightDir;
                float _FakeLightIntensity;
                float _RimPower;
                float _RimIntensity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                // Written out rather than via GetWorldSpaceViewDir() so the rim term's one input
                // is obvious at a glance: this shader is entirely driven by dot(N, V), and a
                // mesh whose normals face the wrong way silently turns the rim into a full-
                // surface flood (see RoundedCubeMeshBuilder.AddTri for exactly that bug).
                float3 V = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float3 L = normalize(_FakeLightDir.xyz);

                // Half-Lambert: never fully black on the away side, just dimmer — a cheap,
                // stylized "fill light" look rather than a harsh single-sided highlight.
                float ndotl = saturate(dot(N, L) * 0.5 + 0.5);
                float3 lit = _BaseColor.rgb * ndotl * _FakeLightIntensity;

                // Rim carries its own color instead of being a multiple of the emission fill, so
                // a plate can stay a moderate solid color (dark icon linework painted on it then
                // survives bloom) while its silhouette still gets a bright edge outline.
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimIntensity;
                float3 color = _EmissionColor.rgb + _RimColor.rgb * rim + lit;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
