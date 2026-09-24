// Wraps PlanetSurface.hlsl (Planet Generator Kit) in a plain URP shader instead of a Shader
// Graph — the downloaded kit only includes the Custom-Function-Node HLSL, noise texture and
// readme, no .shadergraph asset to reassign. This calls the kit's own PlanetSurface_float
// directly and blends the tunnel's two accent colors (cyan/magenta) by the land/ocean mask it
// returns, output as capped emission so it still reads with zero Lights in the scene, same as
// every other neon material in this project.
Shader "Custom/PlanetSurfaceNeon"
{
    Properties
    {
        _ColorOcean ("Ocean Color", Color) = (0, 0.898, 1, 1)
        _ColorLand ("Land Color", Color) = (1, 0, 0.8, 1)
        _Intensity ("Emission Intensity", Range(0, 2.5)) = 0.9
        _Scale ("Noise Scale", Float) = 0.15
        _LandThreshold ("Land Threshold", Range(0, 1)) = 0.5
        _EdgeSmoothness ("Edge Smoothness", Range(0.001, 0.5)) = 0.05
        _BlendSharpness ("Triplanar Blend Sharpness", Float) = 4.0
        _Octaves ("Noise Octaves", Range(1, 6)) = 4
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
            #include "Assets/PlanetGeneratorKit/PlanetSurface.hlsl"

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
                float4 _ColorOcean;
                float4 _ColorLand;
                float _Intensity;
                float _Scale;
                float _LandThreshold;
                float _EdgeSmoothness;
                float _BlendSharpness;
                float _Octaves;
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
                float land;
                PlanetSurface_float(IN.positionWS, normalize(IN.normalWS), _BlendSharpness,
                    _Scale, _LandThreshold, _EdgeSmoothness, _Octaves, land);

                float3 baseColor = lerp(_ColorOcean.rgb, _ColorLand.rgb, land);
                float3 emission = baseColor * _Intensity;

                return half4(emission, 1.0);
            }
            ENDHLSL
        }
    }
}
