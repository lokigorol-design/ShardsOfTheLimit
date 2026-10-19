Shader "Shards/ComicPost"
{
    // Full-screen comic-book look (URP Full Screen Pass, after post-processing):
    // ink outlines from depth and normal edges, banded "cel" lighting that keeps the hue, halftone dots in the shadows
    // and a warm paper tint. The global _ComicStrength (0..1) blends it in; 0 shows the original image.
    Properties
    {
        _InkColor ("Ink", Color) = (0.06, 0.05, 0.07, 1)
        _OutlineThickness ("Outline thickness (px)", Float) = 1.6
        _DepthEdge ("Depth edge threshold", Float) = 0.035
        _NormalEdge ("Normal edge threshold", Float) = 0.35
        _Bands ("Light bands", Float) = 4
        _BandSnap ("Band snap", Range(0, 1)) = 0.7
        _Saturation ("Saturation", Float) = 1.18
        _HalftoneCell ("Halftone cell (px)", Float) = 5
        _HalftoneStart ("Halftone starts below luminance", Range(0, 1)) = 0.3
        _HalftoneInk ("Halftone darkness", Range(0, 1)) = 0.4
        _PaperTint ("Paper tint", Color) = (1, 0.97, 0.9, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "ComicPost"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _ComicStrength;
            float4 _InkColor;
            float _OutlineThickness;
            float _DepthEdge;
            float _NormalEdge;
            float _Bands;
            float _BandSnap;
            float _Saturation;
            float _HalftoneCell;
            float _HalftoneStart;
            float _HalftoneInk;
            float4 _PaperTint;

            bool IsBackground(float raw)
            {
            #if UNITY_REVERSED_Z
                return raw < 1e-6;
            #else
                return raw > 1 - 1e-6;
            #endif
            }

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_ComicStrength <= 0.001) return source;

                float3 color = source.rgb;
                float lum = dot(color, float3(0.299, 0.587, 0.114));

                // Banded light: snap brightness to a few steps but keep the hue.
                float banded = floor(lum * _Bands + 0.5) / _Bands;
                banded = lerp(lum, max(banded, 0.04), _BandSnap);
                color *= banded / max(lum, 1e-3);
                float grey = dot(color, float3(0.299, 0.587, 0.114));
                color = max(0, lerp(grey.xxx, color, _Saturation));

                // Halftone dots in the shadows, rotated 45° like a print screen.
                float2 pixel = uv * _ScreenParams.xy;
                float2 rotated = float2(pixel.x + pixel.y, pixel.x - pixel.y) * 0.7071;
                float2 cell = frac(rotated / _HalftoneCell) - 0.5;
                float darkness = saturate((_HalftoneStart - lum) / _HalftoneStart);
                float radius = sqrt(darkness) * 0.5;
                float dots = 1 - smoothstep(radius - 0.08, radius + 0.02, length(cell));
                color = lerp(color, color * (1 - _HalftoneInk) + _InkColor.rgb * _HalftoneInk * 0.4, dots * step(0.001, darkness));

                // Ink outlines: depth discontinuities (silhouettes, ledges) and normal creases.
                float raw = SampleSceneDepth(uv);
                float edge = 0;
                if (!IsBackground(raw))
                {
                    float2 texel = _OutlineThickness / _ScreenParams.xy;
                    float d0 = LinearEyeDepth(raw, _ZBufferParams);
                    float dL = EyeDepth(uv - float2(texel.x, 0));
                    float dR = EyeDepth(uv + float2(texel.x, 0));
                    float dD = EyeDepth(uv - float2(0, texel.y));
                    float dU = EyeDepth(uv + float2(0, texel.y));
                    float depthEdge = (abs(dL + dR - 2 * d0) + abs(dU + dD - 2 * d0)) / max(d0, 1e-3);
                    float3 n0 = SampleSceneNormals(uv);
                    float3 nL = SampleSceneNormals(uv - float2(texel.x, 0));
                    float3 nR = SampleSceneNormals(uv + float2(texel.x, 0));
                    float3 nD = SampleSceneNormals(uv - float2(0, texel.y));
                    float3 nU = SampleSceneNormals(uv + float2(0, texel.y));
                    float normalEdge = (1 - dot(n0, nL)) + (1 - dot(n0, nR)) + (1 - dot(n0, nD)) + (1 - dot(n0, nU));
                    edge = max(smoothstep(_DepthEdge, _DepthEdge * 2.5, depthEdge),
                               smoothstep(_NormalEdge, _NormalEdge * 1.8, normalEdge));
                    // Far geometry gets thinner, lighter ink.
                    edge *= saturate(1.6 - d0 / 60);
                }

                color *= _PaperTint.rgb;
                color = lerp(color, _InkColor.rgb, saturate(edge));
                return half4(lerp(source.rgb, color, _ComicStrength), source.a);
            }
            ENDHLSL
        }
    }
}
