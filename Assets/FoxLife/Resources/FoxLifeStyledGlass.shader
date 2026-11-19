// Полупрозрачное стекло: тот же узор и два цвета, что у «FoxLife/Styled», но с прозрачностью. URP.
Shader "FoxLife/StyledGlass"
{
    Properties
    {
        _MainTex ("Pattern (R shade, G mask)", 2D) = "white" {}
        _Color ("Color 1", Color) = (1,1,1,1)
        _Color2 ("Color 2", Color) = (0.5,0.5,0.5,1)
        _Scale ("Tiles per meter", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.9
        _Highlight ("Highlight", Color) = (0,0,0,0)
        _Voxel ("Voxel size", Float) = 0
        _UseAO ("Use vertex AO", Float) = 0
        _Alpha ("Opacity", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "DisableBatching"="True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "FoxLit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _Color2;
                float _Scale;
                half _Glossiness;
                half4 _Highlight;
                float _Voxel;
                float _UseAO;
                half _Alpha;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float3 objNormal : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float fogCoord : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.localPos = v.positionOS.xyz;
                o.objNormal = v.normalOS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = FoxSafeNormal(i.objNormal);
                float3 objScale = float3(
                    length(UNITY_MATRIX_M._m00_m10_m20),
                    length(UNITY_MATRIX_M._m01_m11_m21),
                    length(UNITY_MATRIX_M._m02_m12_m22));
                float3 ps = i.localPos * objScale;
                if (_Voxel > 0) ps = (floor((ps - n * (_Voxel * 0.5)) / _Voxel) + 0.5) * _Voxel;
                float3 an = abs(n);
                an = an * an; an = an * an;
                an /= (an.x + an.y + an.z + 1e-5);
                float3 sp = ps * _Scale;
                half4 t = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sp.zy, 0) * an.x
                        + SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sp.xz, 0) * an.y
                        + SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sp.xy, 0) * an.z;
                half3 col = lerp(_Color.rgb, _Color2.rgb, t.g) * t.r;
                return FoxShade(col, _Alpha, _Glossiness, _Highlight.rgb, i.positionWS, FoxSafeNormal(i.normalWS), i.positionCS, i.fogCoord);
            }
            ENDHLSL
        }
    }
}
