// Полноэкранный проход ДО постобработки URP: гасит «битые» пиксели (NaN/бесконечность) и одиночные светлые точки
// и тонкие светлые линии в щёлках между гранями вокселей — чтобы свечение (bloom) не раздувало их в пятна.
// Пиксель не может быть ярче «меньшего из максимумов» соседей по вертикали и по горизонтали; режется только
// яркость, оттенок сохраняется. Звёзды на тёмном небе не трогаем.
Shader "Hidden/FoxLife/Despeckle"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "Despeckle"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            bool Bad(float3 c)
            {
                uint3 b = asuint(c) & 0x7F800000u;
                return any(b == 0x7F800000u);
            }
            float3 Safe(float2 uv)
            {
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                return Bad(c) ? float3(-1, -1, -1) : clamp(c, 0, 8);
            }
            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            half4 Frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 t = _BlitTexture_TexelSize.xy;
                float3 c = Safe(uv);
                float3 l = Safe(uv - float2(t.x, 0)), r = Safe(uv + float2(t.x, 0));
                float3 d = Safe(uv - float2(0, t.y)), u = Safe(uv + float2(0, t.y));
                float4 acc = 0;
                if (l.x >= 0) acc += float4(l, 1); else l = 0;
                if (r.x >= 0) acc += float4(r, 1); else r = 0;
                if (d.x >= 0) acc += float4(d, 1); else d = 0;
                if (u.x >= 0) acc += float4(u, 1); else u = 0;
                if (c.x < 0) return half4(acc.w > 0 ? acc.rgb / acc.w : 0, 1);
                float3 lim = min(max(l, r), max(d, u));
                float nb = max(max(Luma(l), Luma(r)), max(Luma(d), Luma(u)));
                if (nb < 0.06) return half4(c, 1);
                float lc = Luma(c), ll = Luma(lim) * 1.08 + 0.02;
                return half4(lc > ll ? c * (ll / max(lc, 1e-4)) : c, 1);
            }
            ENDHLSL
        }
    }
}
