// UI shader for the trainer panel: turns the figure's RenderTexture ALPHA into a contour and a soft
// halo in the theme colour, drawn additively. With the figure image itself hidden this IS the
// figure - a line drawing, the panel's dark glass showing through inside it. The contour is found
// by dilating the alpha in a ring of taps, `_ContourPx` wide (set from the texture's height so it
// keeps its weight at 1440p and 4K alike); the halo comes from the mip chain.
Shader "Tunnel/SilhouetteGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Figure", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _GlowColor ("Glow (theme)", Color) = (0, 0.85, 1, 1)
        _Outline ("Contour", Range(0, 2)) = 1.0
        _ContourPx ("Contour width (px)", Range(1, 12)) = 3.5
        _ClosePx ("Gap closing (px)", Range(0, 12)) = 3
        _Halo ("Halo", Range(0, 2)) = 0.7
        _HaloRadius ("Halo radius (UV)", Range(0, 0.1)) = 0.018
        _HaloMip ("Halo mip", Range(0, 6)) = 3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _GlowColor;
            float _Outline, _ContourPx, _ClosePx, _Halo, _HaloRadius, _HaloMip;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float A(float2 uv, float lod) { return tex2Dlod(_MainTex, float4(uv, 0, lod)).a; }

            fixed4 frag(v2f i) : SV_Target
            {
                // The X Bot is built of segments with thin gaps between them (waist, chest,
                // joints). Seen as a mask those gaps would draw lines across the body, so the mask
                // is first closed: `center` is the alpha dilated by _ClosePx, which fills any gap
                // narrower than twice that, and the contour is the band just outside it.
                float2 px = _MainTex_TexelSize.xy;
                float center = A(i.uv, 0);
                float ring = 0, halo = 0;
                [unroll] for (int k = 0; k < 12; k++)
                {
                    float ang = k * 0.5235988;
                    float2 d = float2(cos(ang), sin(ang));
                    center = max(center, A(i.uv + d * px * _ClosePx, 0));
                    center = max(center, A(i.uv + d * px * _ClosePx * 0.5, 0));
                    ring = max(ring, A(i.uv + d * px * (_ClosePx + _ContourPx), 0));
                    ring = max(ring, A(i.uv + d * px * (_ClosePx + _ContourPx * 0.5), 0));
                    halo += A(i.uv + d * float2(_HaloRadius * px.x / px.y, _HaloRadius), _HaloMip);
                }
                halo = halo / 12.0 + A(i.uv, _HaloMip + 1) * 0.5;
                float contour = saturate(ring - center) * _Outline;
                float glow = saturate(contour + halo * _Halo * (1 - center));
                return fixed4(_GlowColor.rgb, glow * i.color.a);
            }
            ENDCG
        }
    }
}
