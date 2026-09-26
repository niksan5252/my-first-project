// An LED wall for the club theme: a grid of round diodes whose pattern changes every bar and
// flashes on every beat, brighter when the room's energy is up (ClubBeat's _Club* globals).
// Four patterns take turns - a checkerboard that swaps on the beat, bars sweeping across, a
// random sparkle, and one of the pattern-pack textures scrolling through the grid. Unlit and
// capped: the diodes glow in the theme's two colours and never wash out to white.
Shader "Tunnel/ClubLedWall"
{
    Properties
    {
        _ColorA ("Colour A", Color) = (1, 0.45, 0.05, 1)
        _ColorB ("Colour B", Color) = (0.55, 0.15, 1, 1)
        _Intensity ("Intensity", Range(0, 2.5)) = 1.4
        _Cells ("Cells (x, y)", Vector) = (32, 22, 0, 0)
        _PatternTex ("Pattern (pattern pack)", 2D) = "white" {}
        _Seed ("Seed", Float) = 0
        _Base ("Unlit diode level", Range(0, 0.3)) = 0.06
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA, _ColorB;
                half _Intensity;
                float4 _Cells;
                float4 _PatternTex_ST;
                float _Seed;
                half _Base;
            CBUFFER_END
            TEXTURE2D(_PatternTex); SAMPLER(sampler_PatternTex);

            float _ClubSongTime, _ClubBeatPhase, _ClubBeatIndex, _ClubBar, _ClubEnergy;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed * 17.13) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                float2 grid = i.uv * _Cells.xy;
                float2 cell = floor(grid);
                float2 f = frac(grid) - 0.5;
                half dot_ = smoothstep(0.42, 0.30, length(f));        // a round diode

                float beat = _ClubBeatIndex;
                float phase = _ClubBeatPhase;
                float pulse = pow(1.0 - phase, 2.0);
                int pattern = (int)fmod(_ClubBar + _Seed, 4.0);
                half on;
                if (pattern == 0)       // checkerboard, swapping on every beat
                    on = fmod(floor(cell.x / 2.0) + floor(cell.y / 2.0) + beat, 2.0) < 1.0 ? 1.0 : 0.15;
                else if (pattern == 1)  // bars sweeping across, one pass per beat
                    on = saturate(1.0 - abs(frac(cell.x / _Cells.x * 2.0 - (beat + phase) * 0.5) - 0.5) * 6.0);
                else if (pattern == 2)  // sparkle: a new random set of diodes on every beat
                    on = hash(cell + beat) > 0.62 ? 1.0 : 0.05;
                else                    // a pattern-pack tile scrolling up the wall
                {
                    float2 puv = cell / _Cells.y * 3.0 + float2(0.0, (beat + phase) * 0.25);
                    on = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, puv).r > 0.5 ? 1.0 : 0.08;
                }
                // Colour by row bands that shift each bar, so the two colours move too.
                half mixAB = step(0.5, frac((cell.y + _ClubBar * 3.0) / 8.0));
                half3 col = lerp(_ColorA.rgb, _ColorB.rgb, mixAB);
                half level = on * (0.45 + 0.55 * pulse) * _ClubEnergy;
                half3 c = col * (_Base + level) * _Intensity * dot_;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
