// Шейдер лаборатории: цвет из вершин (каждый воксел — свой оттенок, в цвете уже «запечено» затенение углов).
Shader "FoxLife/LabVoxel"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0,1)) = 0.08
        _Emission ("Emission", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        half _Glossiness;
        half _Emission;
        struct Input { float4 vcol; };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vcol = v.color;
        }
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 c = IN.vcol.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            c = GammaToLinearSpace(c);
            #endif
            o.Albedo = c;
            o.Emission = c * _Emission;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
