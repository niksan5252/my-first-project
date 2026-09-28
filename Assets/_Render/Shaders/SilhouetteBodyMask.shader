// Offline silhouette render: the body as a flat opaque black mask on a transparent background.
// Drawn directly by SilhouetteRenderTool through a CommandBuffer, so it does not depend on URP passes.
Shader "Hidden/SilhouetteRender/BodyMask"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, vertex));
            }

            fixed4 frag() : SV_Target
            {
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
