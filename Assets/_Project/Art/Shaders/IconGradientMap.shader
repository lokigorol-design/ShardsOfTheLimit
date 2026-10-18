Shader "Shards/IconGradientMap"
{
    // Colors a grayscale ability icon master by a resonant palette (port of colorize_icon.py v2):
    // brightness → effect gradient, the same brightness → background gradient, soft brightness mask between them,
    // tint into dark areas away from the center, inner shadow at the frame, rounded-square alpha and the gold frame on top.
    // All color math happens on authored sRGB values (gamma space), as in the reference script.
    Properties
    {
        _MainTex ("Master (grayscale)", 2D) = "black" {}
        _MaskTex ("Soft effect mask", 2D) = "black" {}
        _FgRamp ("Effect gradient", 2D) = "white" {}
        _BgRamp ("Background gradient", 2D) = "black" {}
        _FrameTex ("Frame", 2D) = "black" {}
        _TintColor ("Tint (sRGB)", Vector) = (0.06, 0.24, 0.24, 1)
        _TintStrength ("Tint strength", Float) = 0
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
            sampler2D _MaskTex;
            sampler2D _FgRamp;
            sampler2D _BgRamp;
            sampler2D _FrameTex;
            float4 _TintColor;
            float _TintStrength;

            // Frame geometry as fractions of the icon size (colorize_icon.py).
            static const float Radius = 0.115;
            static const float OuterInk = 0.016;
            static const float Gold = 0.050;
            static const float InnerInk = 0.013;

            float3 ToGamma(float3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return LinearToGammaSpace(c);
            #endif
            }

            float3 ToOutput(float3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return GammaToLinearSpace(c);
            #endif
            }

            float RoundedRectSdf(float2 uv)
            {
                float2 p = uv - 0.5;
                float h = 0.5 - Radius;
                float2 q = abs(p) - h;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - Radius;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float lum = saturate(ToGamma(tex2D(_MainTex, uv).rgb).r);
                // Ramps hold authored sRGB bytes in a non-sRGB texture: sampled values are the gamma values themselves.
                float3 fg = tex2D(_FgRamp, float2(lum, 0.5)).rgb;
                float3 bg = tex2D(_BgRamp, float2(lum, 0.5)).rgb;
                float m = tex2D(_MaskTex, uv).r;
                float3 rgb = lerp(bg, fg, m);

                float w = saturate(length(uv - 0.5) * 1.6) * (1 - lum) * _TintStrength;
                rgb = lerp(rgb, _TintColor.rgb, w);

                float d = RoundedRectSdf(uv);
                float pixel = max(fwidth(d), 1e-4);
                float innerEdge = -(OuterInk + Gold + InnerInk * 0.5);
                float inner = 1 - smoothstep(innerEdge - pixel * 0.5, innerEdge + pixel * 0.5, d);
                float edge = -(OuterInk + Gold + InnerInk);
                float t = saturate((d - (edge - 0.035)) / 0.035);
                rgb *= 1 - 0.55 * t * t;

                float4 frame = tex2D(_FrameTex, uv);
                frame.rgb = ToGamma(frame.rgb);
                float alpha = frame.a + inner * (1 - frame.a);
                float3 color = (frame.rgb * frame.a + rgb * inner * (1 - frame.a)) / max(alpha, 1e-4);
                return fixed4(ToOutput(color), alpha);
            }
            ENDCG
        }
    }
}
