// Neon wire grid for Theme_Synthwave: the road, the plain around it running to the horizon, and
// the wireframe mountains. Lines are drawn in WORLD space on the X/Z plane, so on flat ground they
// are the classic grid and on a height-field the same lines drape over the slopes as a wire mesh.
//
//   * Scroll: the grid runs towards the camera at _SynthScrollSpeed x _ScrollScale m/s of SONG
//     time (_ClubSongTime, set by ClubBeat) - the tunnel's speed, locked to the music.
//   * Pulse and wave come from GLOBALS on one of two channels (_Channel: 0 = ground, 1 =
//     mountains), set per frame by SynthwaveEnvironmentDecorator from DOTween tweens on the song
//     clock. Globals rather than material properties because the road's material is the theme's
//     floor asset: writing into it in Play would change the asset on disk.
//       _SynthPulseN  - brightness added to the lines;
//       _SynthWaveN   - (position, width, intensity): a band of extra light at that position
//                       along _WaveAxis (the ground: a pulse rushing in along Z; the mountains:
//                       a wave rising up their slopes on a drop).
//   * Distance fade to _HorizonColor (the fog/horizon colour of the sky), by its own curve, and
//     lines resolved to their average brightness before they can alias into moire far away.
// Opaque; the fill is a dark unlit colour, the lines emission (HDR).
Shader "Tunnel/SynthGrid"
{
    Properties
    {
        _FillColor ("Fill", Color) = (0.02, 0.008, 0.05, 1)
        _LineColor ("Line", Color) = (1, 0.35, 0.55, 1)
        _LineColorFar ("Line (far / high)", Color) = (0.55, 0.25, 1, 1)
        _ColorSplit ("Colour split distance", Float) = 60
        _Cell ("Cell size (x, z)", Vector) = (2, 2, 0, 0)
        _LineWidth ("Line width (m)", Float) = 0.06
        _Intensity ("Line intensity", Range(0, 3)) = 1
        _Channel ("Channel (0 ground, 1 mountains)", Float) = 0
        _ScrollScale ("Scroll (x tunnel speed)", Float) = 1
        _WaveAxis ("Wave axis", Vector) = (0, 0, 1, 0)
        _HorizonColor ("Horizon colour", Color) = (0.42, 0.1, 0.34, 1)
        _FadeStart ("Fade start", Float) = 30
        _FadeEnd ("Fade end", Float) = 420
        _FadeAmount ("Fade amount", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Grid"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FillColor, _LineColor, _LineColorFar;
                float _ColorSplit;
                float4 _Cell;
                float _LineWidth, _Intensity, _Channel, _ScrollScale;
                float4 _WaveAxis;
                half4 _HorizonColor;
                float _FadeStart, _FadeEnd, _FadeAmount;
            CBUFFER_END
            float _ClubSongTime, _SynthScrollSpeed;
            float _SynthPulse0, _SynthPulse1;
            float4 _SynthWave0, _SynthWave1;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // Coverage of a line of `width` cells centred on each integer, filtered by the
            // screen-space footprint so it tends to its average (width) instead of aliasing.
            float GridLine(float coord, float width)
            {
                float fw = max(fwidth(coord), 1e-5);
                float d = abs(frac(coord + 0.5) - 0.5);
                float sharp = saturate((width * 0.5 - d) / fw + 0.5);
                float blend = saturate((fw - width) / (0.5 - width));
                return lerp(sharp, width, blend);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                bool mountains = _Channel > 0.5;
                float pulse = mountains ? _SynthPulse1 : _SynthPulse0;
                float4 waveP = mountains ? _SynthWave1 : _SynthWave0;
                float2 coord = float2(p.x / _Cell.x, (p.z + _ClubSongTime * _SynthScrollSpeed * _ScrollScale) / _Cell.y);
                float lx = GridLine(coord.x, _LineWidth / _Cell.x);
                float lz = GridLine(coord.y, _LineWidth / _Cell.y);
                float lines = max(lx, lz);

                float dist = distance(p, _WorldSpaceCameraPos);
                half3 lineColor = lerp(_LineColor.rgb, _LineColorFar.rgb, saturate(dist / max(_ColorSplit, 1.0) - 0.5));
                float wave = exp(-pow((dot(p, _WaveAxis.xyz) - waveP.x) / max(waveP.y, 0.01), 2.0)) * waveP.z;
                half3 c = _FillColor.rgb + lineColor * lines * (_Intensity + pulse + wave);
                // The fill takes a trace of the wave too, so it reads as light passing over the shape.
                c += lineColor * wave * 0.06;

                float f = saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 1.0));
                c = lerp(c, _HorizonColor.rgb, f * f * (3.0 - 2.0 * f) * _FadeAmount);
                return half4(c, 1);
            }
            ENDHLSL
        }
        // Depth for the post stack (and anything that reads the depth texture).
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
