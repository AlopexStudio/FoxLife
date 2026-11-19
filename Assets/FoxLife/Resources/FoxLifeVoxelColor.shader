// Вокселы с цветом в вершинах (земля, трава, цветы): в цвете уже учтены оттенок и затенение углов.
Shader "FoxLife/VoxelColor"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0,1)) = 0.06
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        half _Glossiness;
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
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
