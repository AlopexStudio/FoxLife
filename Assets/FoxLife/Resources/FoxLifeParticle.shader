// Частицы: цвет вершины × цвет материала, полупрозрачно. Освещаются солнцем и окружением (_Unlit = 1 — светятся сами). URP.
Shader "FoxLife/Particle"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Unlit ("Unlit", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "FoxLit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Unlit;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float fogCoord : TEXCOORD0; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                half4 c = v.color * _Color;
                Light ml = GetMainLight();
                half3 light = _FoxAmbient.rgb * 1.3 + ml.color * 0.75;
                c.rgb *= lerp(min(light, 1.6), 1.0, saturate(_Unlit));
                o.color = c;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag (Varyings i) : SV_Target { half4 c = i.color; c.rgb = MixFog(c.rgb, i.fogCoord); return c; }
            ENDHLSL
        }
    }
}
