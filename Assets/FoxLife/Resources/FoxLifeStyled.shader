// Серая текстура-узор (R = светотень, G = маска второго цвета), раскрашенная двумя цветами. URP.
// Воксельный режим (_Voxel > 0): узор берётся один раз на воксел — по центру ячейки, у каждого воксела
// чуть свой оттенок, а затенение углов приходит из цвета вершин. Поэтому перекраска не требует пересборки сетки.
Shader "FoxLife/Styled"
{
    Properties
    {
        _MainTex ("Pattern (R shade, G mask)", 2D) = "white" {}
        _Color ("Color 1", Color) = (1,1,1,1)
        _Color2 ("Color 2", Color) = (0.5,0.5,0.5,1)
        _Scale ("Tiles per meter", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.15
        _Highlight ("Highlight", Color) = (0,0,0,0)
        _Voxel ("Voxel size", Float) = 0
        _UseAO ("Use vertex AO", Float) = 0
        _DepthBias ("Depth bias layer", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half4 _Color2;
            float _Scale;
            half _Glossiness;
            half4 _Highlight;
            float _Voxel;
            float _UseAO;
            float _DepthBias;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "FoxLit.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 localPosAo : TEXCOORD0;   // xyz — позиция в объекте, w — затенение угла
                float3 objNormal : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float fogCoord : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 wp = FoxDepthBias(TransformObjectToWorld(v.positionOS.xyz), _DepthBias);
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.localPosAo = float4(v.positionOS.xyz, v.color.r);
                o.objNormal = v.normalOS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = FoxSafeNormal(i.objNormal);
                float3 objScale = float3(
                    length(UNITY_MATRIX_M._m00_m10_m20),
                    length(UNITY_MATRIX_M._m01_m11_m21),
                    length(UNITY_MATRIX_M._m02_m12_m22));
                float vary = 1;
                float ao = 1;
                float3 ps = i.localPosAo.xyz * objScale; // позиция в метрах
                if (_Voxel > 0)
                {
                    // центр воксела, которому принадлежит эта грань
                    float3 cell = floor((ps - n * (_Voxel * 0.5)) / _Voxel);
                    ps = (cell + 0.5) * _Voxel;
                    vary = 1 + (hash3(cell) - 0.5) * 0.07;
                    if (_UseAO > 0) ao = i.localPosAo.w;
                }
                float3 an = abs(n);
                an = an * an; an = an * an;
                an /= (an.x + an.y + an.z + 1e-5);
                float3 sp = ps * _Scale;
                half4 tx = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sp.zy, 0);
                half4 ty = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sp.xz, 0);
                half4 tz = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sp.xy, 0);
                half4 t = tx * an.x + ty * an.y + tz * an.z;
                half3 col = lerp(_Color.rgb, _Color2.rgb, t.g) * t.r * vary;
                ao = SRGBToLinear(ao);
                return FoxShade(col * ao, 1, _Glossiness, _Highlight.rgb, i.positionWS, FoxSafeNormal(i.normalWS), i.positionCS, i.fogCoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
}
