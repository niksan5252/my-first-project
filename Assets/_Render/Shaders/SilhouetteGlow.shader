// Offline silhouette render: soft neon glow around the outline.
// Pass 0 keeps only the outline ring of the outlined image, pass 1 is a separable gaussian blur,
// pass 2 puts the outlined image over the glow. Everything is premultiplied alpha.
Shader "Hidden/SilhouetteRender/Glow"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _GlowTex ("Glow", 2D) = "black" {}
        _Direction ("Blur direction (texels)", Vector) = (1, 0, 0, 0)
        _Intensity ("Glow intensity", Float) = 0.8
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _GlowTex;
        float4 _Direction;
        float _Intensity;
        ENDCG

        Pass // 0: outline ring only (colour where the body is not)
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            fixed4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float ring = max(c.r, max(c.g, c.b)); // the body is pure black, only the ring has colour
                return float4(c.rgb, ring);
            }
            ENDCG
        }

        Pass // 1: 13-tap gaussian along _Direction
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            fixed4 frag(v2f_img i) : SV_Target
            {
                static const float w[7] = { 0.1964, 0.1748, 0.1232, 0.0689, 0.0305, 0.0107, 0.0030 };
                float2 step = _Direction.xy * _MainTex_TexelSize.xy;
                float4 sum = tex2D(_MainTex, i.uv) * w[0];
                for (int k = 1; k < 7; k++)
                {
                    sum += tex2D(_MainTex, i.uv + step * k) * w[k];
                    sum += tex2D(_MainTex, i.uv - step * k) * w[k];
                }
                return sum;
            }
            ENDCG
        }

        Pass // 2: outlined image over the glow
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            fixed4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float4 glow = saturate(tex2D(_GlowTex, i.uv) * _Intensity);
                return src + glow * (1 - src.a);
            }
            ENDCG
        }
    }
}
