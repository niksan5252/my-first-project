// Theme_Aurora's ice crystals and arches: faceted ice lit from inside. A dark translucent-looking
// body whose facets each catch a different amount of the inner light (flat normals, compared with
// a fixed "light inside" direction), a bright rim towards the silhouette, all in _Color x _Glow -
// set per object from C# (on the song clock) through a MaterialPropertyBlock. Opaque, with fog.
Shader "Tunnel/AuroraCrystal"
{
    Properties
    {
        _Color ("Colour", Color) = (0.2, 1, 0.6, 1)
        _Glow ("Glow", Range(0, 3)) = 0.8
        _Body ("Body darkness", Range(0, 0.5)) = 0.06
        _Rim ("Rim", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Glow, _Body, _Rim;
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
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float facet = 0.35 + 0.65 * saturate(0.5 + 0.5 * dot(n, normalize(float3(0.35, 0.8, -0.45))));
                float rim = pow(1.0 - saturate(abs(dot(n, v))), 3.0) * _Rim;
                half3 c = _Color.rgb * _Body + _Color.rgb * _Glow * (facet * 0.55 + rim);
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
