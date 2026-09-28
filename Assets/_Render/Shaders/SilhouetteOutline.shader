// Offline silhouette render: turns the body mask into black body + coloured outline along the outer contour only.
// The outline is a dilation of the mask alpha, so it can only appear outside the silhouette: overlapping
// limbs, hair and clothing never produce lines inside the body. Output is premultiplied alpha.
Shader "Hidden/SilhouetteRender/Outline"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "black" {}
        _RadiusPx ("Outline radius (px)", Float) = 6
        _OutlineColor ("Outline colour", Color) = (0, 0.85, 1, 1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _RadiusPx;
            float4 _OutlineColor;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float body = tex2D(_MainTex, i.uv).a;

                // Disk dilation: the strongest mask coverage within the radius, softened over the last pixel.
                float outline = 0;
                int r = (int)ceil(_RadiusPx);
                for (int y = -r; y <= r; y++)
                {
                    for (int x = -r; x <= r; x++)
                    {
                        float dist = length(float2(x, y));
                        float falloff = saturate(_RadiusPx + 0.5 - dist);
                        if (falloff <= 0) continue;
                        float a = tex2D(_MainTex, i.uv + float2(x, y) * _MainTex_TexelSize.xy).a;
                        outline = max(outline, a * falloff);
                    }
                }

                float alpha = max(body, outline);
                // Outline colour where only the outline covers, black over the body; premultiplied by alpha.
                float rim = saturate(alpha - body);
                return fixed4(_OutlineColor.rgb * rim, alpha);
            }
            ENDCG
        }
    }
}
