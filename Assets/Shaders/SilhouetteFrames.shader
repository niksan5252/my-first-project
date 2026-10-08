// UI shader for the video trainer (VideoSilhouette): draws the baked loops of filmed frames
// (Texture2DArray, one slice a frame). Each clip shows the nearer of its two neighbouring frames and
// a change of move swaps at the middle of `_Mix` - never a blend of two poses, which reads as two
// see-through figures (video 12: the user saw the ghosts). A clip can be mirrored (its
// `.w`: the move done to the other side) and lowered by `_Bounce` (the stance bouncing on the beat).
// All blends are premultiplied, so the neon contour never darkens at a seam.
Shader "Tunnel/SilhouetteFrames"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _FramesA ("Clip (incoming)", 2DArray) = "" {}
        _FramesB ("Clip (outgoing)", 2DArray) = "" {}
        _A ("A: frame0, frame1, fraction, mirror", Vector) = (0, 0, 0, 0)
        _B ("B: frame0, frame1, fraction, mirror", Vector) = (0, 0, 0, 0)
        _Bounce ("Bounce A, B (share of height)", Vector) = (0, 0, 0, 0)
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
            float4 _A, _B, _Bounce;
            float _Mix;

            float2 Uv(float2 uv, float mirror, float bounce) { return float2(lerp(uv.x, 1 - uv.x, mirror), uv.y + bounce); }

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
                float2 ua = Uv(i.uv, _A.w, _Bounce.x), ub = Uv(i.uv, _B.w, _Bounce.y);
                float4 a = lerp(Premul(UNITY_SAMPLE_TEX2DARRAY(_FramesA, float3(ua, _A.x))),
                                Premul(UNITY_SAMPLE_TEX2DARRAY(_FramesA, float3(ua, _A.y))), step(0.5, _A.z));
                float4 b = lerp(Premul(UNITY_SAMPLE_TEX2DARRAY(_FramesB, float3(ub, _B.x))),
                                Premul(UNITY_SAMPLE_TEX2DARRAY(_FramesB, float3(ub, _B.y))), step(0.5, _B.z));
                float4 c = _Mix >= 0.5 ? a : b;
                c.rgb *= i.color.rgb;
                return c * i.color.a;
            }
            ENDCG
        }
    }
}
