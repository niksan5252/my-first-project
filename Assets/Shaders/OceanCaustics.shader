// Theme_Ocean's sea floor - the road and the sand either side: a dark sandy bed with sunlight
// caustics dancing over it, from two layers of the Kenney caustics mask drifting across each other
// (blended and squared: bright where the lines cross). Mapped in world X/Z; the pattern travels with the road
// at the tunnel's speed on SONG time and its own drift runs on a phase from C#:
//   _ClubSongTime      - set by ClubBeat;
//   _SynthScrollSpeed  - the tunnel's speed, set by the environment decorator (shared name);
//   _OceanCausticPhase - drift phase, faster on drops (OceanEnvironmentDecorator, DOTween on the
//                        song clock);
//   _OceanCausticGain  - brightness, with a lift on the beat.
// Globals, not material properties: the road's material is the theme's floor asset.
// Fades to _FadeColor (the fog) by its own distance curve. Opaque, unlit.
Shader "Tunnel/OceanCaustics"
{
    Properties
    {
        _SandColor ("Sand", Color) = (0.02, 0.05, 0.08, 1)
        _LightColor ("Caustic light", Color) = (0.35, 0.85, 0.9, 1)
        _Caustics ("Caustics mask", 2D) = "black" {}
        _Scale ("World scale (m per tile)", Float) = 7
        _Intensity ("Intensity", Range(0, 3)) = 0.8
        _ScrollScale ("Scroll (x tunnel speed)", Float) = 1
        _FadeColor ("Fade colour (= fog)", Color) = (0.01, 0.06, 0.12, 1)
        _FadeStart ("Fade start", Float) = 15
        _FadeEnd ("Fade end", Float) = 120
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Caustics"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SandColor, _LightColor;
                float4 _Caustics_ST;
                float _Scale, _Intensity, _ScrollScale;
                half4 _FadeColor;
                float _FadeStart, _FadeEnd;
            CBUFFER_END
            float _ClubSongTime, _SynthScrollSpeed, _OceanCausticPhase;
            float _OceanCausticGain;
            TEXTURE2D(_Caustics);
            SAMPLER(sampler_linear_repeat);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                float2 uv = float2(p.x, p.z + _ClubSongTime * _SynthScrollSpeed * _ScrollScale) / _Scale;
                float ph = _OceanCausticPhase;
                float a = SAMPLE_TEXTURE2D(_Caustics, sampler_linear_repeat, uv + float2(ph * 0.13, ph * 0.05)).a;
                float b = SAMPLE_TEXTURE2D(_Caustics, sampler_linear_repeat, uv * 0.73 + float2(-ph * 0.07, ph * 0.11) + 0.37).a;
                // The two drifting layers blended, then squared: bright where their lines cross,
                // a faint web elsewhere.
                float web = a * 0.55 + b * 0.45;
                web = web * web;
                half3 c = _SandColor.rgb + _LightColor.rgb * web * _Intensity * max(_OceanCausticGain, 0.0);
                float dist = distance(p, _WorldSpaceCameraPos);
                float f = saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 1.0));
                c = lerp(c, _FadeColor.rgb, f * f * (3.0 - 2.0 * f));
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
