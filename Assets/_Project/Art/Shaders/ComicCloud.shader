Shader "Shards/ComicCloud"
{
    Properties { _Color ("Color", Color) = (0.4, 1, 0.05, 0.4) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            fixed4 _Color;
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 edge = min(i.uv, 1-i.uv);
                float outline = step(min(edge.x, edge.y), 0.028);
                float dots = step(length(frac(i.uv*12)-0.5), 0.19);
                return fixed4(lerp(_Color.rgb, float3(0.03,0.035,0.02), max(outline,dots*0.7)), lerp(0.18,0.8,max(outline,dots*0.6)));
            }
            ENDCG
        }
    }
}
