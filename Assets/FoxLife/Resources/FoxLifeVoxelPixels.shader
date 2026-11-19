// Пиксели-вокселы экранов и холстов: цвет в вершинах, освещение как у всего мира, экраны ещё и светятся (_Glow).
Shader "FoxLife/VoxelPixels"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _Glow ("Glow", Float) = 0
        _DepthBias ("Depth bias layer", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "DisableBatching"="True" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        half _Glossiness;
        float _Glow;
        float _DepthBias;
        struct Input { float4 vcol; };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vcol = v.color;
            if (_DepthBias > 0)
            {
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 toCam = _WorldSpaceCameraPos - wp;
                float d = length(toCam);
                wp += toCam / max(d, 1e-3) * _DepthBias * (0.0012 + d * 0.00035);
                v.vertex.xyz = mul(unity_WorldToObject, float4(wp, 1)).xyz;
            }
        }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 c = IN.vcol.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            c = GammaToLinearSpace(c);
            #endif
            o.Albedo = c * (1 - saturate(_Glow) * 0.6);
            o.Emission = c * _Glow;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
