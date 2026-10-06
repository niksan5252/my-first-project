// UI shader for the video trainer (VideoSilhouette): draws the baked loops of filmed frames
// (Texture2DArray, one slice a frame). Each clip is sampled at two neighbouring frames and blended
// by the fraction between them, so a 24 fps take moves smoothly at any speed; the outgoing clip of
// a phrase change is blended out under the incoming one by `_Mix`. All blends are premultiplied,
// so the neon contour never darkens at a seam.
Shader "Tunnel/SilhouetteFrames"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _FramesA ("Clip (incoming)", 2DArray) = "" {}
        _FramesB ("Clip (outgoing)", 2DArray) = "" {}
        _A ("A: frame0, frame1, fraction", Vector) = (0, 0, 0, 0)
        _B ("B: frame0, frame1, fraction", Vector) = (0, 0, 0, 0)
        _Mix ("A over B", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_FramesA);
            UNITY_DECLARE_TEX2DARRAY(_FramesB);
            float4 _A, _B;
            float _Mix;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 Premul(float4 c) { return float4(c.rgb * c.a, c.a); }

            float4 frag(v2f i) : SV_Target
            {
                float4 a = lerp(Premul(UNITY_SAMPLE_TEX2DARRAY(_FramesA, float3(i.uv, _A.x))),
                                Premul(UNITY_SAMPLE_TEX2DARRAY(_FramesA, float3(i.uv, _A.y))), _A.z);
                float4 b = lerp(Premul(UNITY_SAMPLE_TEX2DARRAY(_FramesB, float3(i.uv, _B.x))),
                                Premul(UNITY_SAMPLE_TEX2DARRAY(_FramesB, float3(i.uv, _B.y))), _B.z);
                float4 c = lerp(b, a, _Mix);
                c.rgb *= i.color.rgb;
                return c * i.color.a;
            }
            ENDCG
        }
    }
}
