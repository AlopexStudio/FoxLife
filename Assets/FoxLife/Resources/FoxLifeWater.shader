// Вода: полупрозрачная, у берега светлее и с пеной, в глубине — синее; рябь «пиксельная» (в шаг вокселя),
// солнечные блики мягкие (без выжженных пятен), отражение неба по краю. URP.
Shader "FoxLife/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.38, 0.78, 0.86, 0.55)
        _Deep ("Deep", Color) = (0.12, 0.42, 0.7, 0.9)
        _Sky ("Sky reflection", Color) = (0.82, 0.92, 1, 1)
        _Foam ("Foam", Color) = (1, 1, 1, 1)
        _Pixel ("Pixel size", Float) = 0.125
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "FoxLit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Deep, _Sky, _Foam;
                float _Pixel;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 wp : TEXCOORD0; half4 col : COLOR; float fogCoord : TEXCOORD1; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.wp);
                o.col = v.color;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float H(float2 p, float t)
            {
                return sin(p.x * 1.7 + t * 1.3) * 0.5 + sin(p.y * 2.3 - t * 1.1) * 0.4 + sin((p.x + p.y) * 3.1 + t * 2.0) * 0.25
                     + sin((p.x * 0.7 - p.y * 1.3) * 5.0 - t * 2.7) * 0.12;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                // рябь берётся в центре «пикселя» — как у вокселей
                float2 q = (floor(i.wp.xz / _Pixel) + 0.5) * _Pixel;
                float e = 0.12;
                float h0 = H(q, t), hx = H(q + float2(e, 0), t), hz = H(q + float2(0, e), t);
                float3 n = normalize(float3(-(hx - h0) / e * 0.06, 1, -(hz - h0) / e * 0.06));
                float depth = i.col.g, shore = i.col.r;
                half4 c = lerp(_Shallow, _Deep, saturate(depth * 1.3));
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                Light ml = GetMainLight();
                float3 l = normalize(ml.direction);
                float diff = saturate(dot(n, l)) * 0.35 + 0.65;
                float3 light = _FoxAmbient.rgb * 1.2 + ml.color * 0.6;
                c.rgb *= diff * min(light, 1.4);
                // небо в воде под углом
                float fres = pow(1 - saturate(dot(n, v)), 3);
                c.rgb = lerp(c.rgb, _Sky.rgb * min(light, 1.2), fres * 0.35);
                c.a = saturate(c.a + fres * 0.3);
                // блики солнца: мягкие и ограниченные по яркости
                float3 hv = normalize(l + v);
                float spec = pow(saturate(dot(n, hv)), 90) * 0.9;
                c.rgb += min(spec, 0.9) * ml.color;
                // пена у берега: бегущие полоски
                float foam = shore * smoothstep(0.55, 0.9, sin(t * 1.6 + (q.x + q.y) * 2.1) * 0.5 + 0.5 + (1 - depth) * 0.3);
                c.rgb = lerp(c.rgb, _Foam.rgb * min(light, 1.3), foam * 0.65);
                c.a = max(c.a, foam * 0.9);
                c.rgb = MixFog(c.rgb, i.fogCoord);
                return c;
            }
            ENDHLSL
        }
    }
}
