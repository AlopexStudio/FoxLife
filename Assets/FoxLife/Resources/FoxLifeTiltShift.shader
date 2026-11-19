// Полноэкранный проход ПОСЛЕ постобработки URP: «миниатюра» — верх и низ кадра мягко расплываются,
// как у макросъёмки игрушечного городка. Сила — глобальный _FoxTiltShift (игра выставляет по отдалению камеры).
Shader "Hidden/FoxLife/TiltShift"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "TiltShift"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _FoxTiltShift;

            half4 Frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float band = saturate((abs(uv.y - 0.5) - 0.2) / 0.3);
                band = band * band * _FoxTiltShift;
                if (band < 0.01) return c;
                // размытие диском: два кольца по 8 отсчётов, радиус растёт к краю кадра
                float2 px = _BlitTexture_TexelSize.xy * (band * 7.0);
                half3 sum = c.rgb;
                float w = 1;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float a = k * 0.785398 + 0.39;
                    float2 o = float2(cos(a), sin(a));
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + o * px).rgb;
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + o.yx * float2(-1, 1) * px * 0.5).rgb;
                    w += 2;
                }
                return half4(sum / w, c.a);
            }
            ENDHLSL
        }
    }
}
