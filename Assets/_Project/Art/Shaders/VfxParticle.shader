Shader "Shards/VfxParticle"
{
    // Camera-facing particle quads built by VfxSystem: vertex colour, procedural soft disc, no texture.
    // uv2.x = hardness (0 soft smoke ... 0.9 hard spark), uv2.y = 1 for a flame teardrop.
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 shape : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.shape = v.uv2;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.uv * 2 - 1;
                // Teardrop: narrow the top half so flames lick upwards.
                d.x *= lerp(1, 1 + saturate(d.y) * 1.6, i.shape.y);
                float r = length(d);
                float alpha = smoothstep(1, i.shape.x, r);
                fixed4 c = fixed4(i.color.rgb, i.color.a * alpha);
                return c;
            }
            ENDCG
        }
    }
}