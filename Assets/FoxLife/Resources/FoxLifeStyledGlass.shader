// Полупрозрачное стекло: тот же узор и два цвета, что у «FoxLife/Styled», но с прозрачностью.
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard alpha:fade vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _Color2;
        float _Scale;
        half _Glossiness;
        fixed4 _Highlight;
        float _Voxel;
        half _Alpha;

        struct Input
        {
            float3 localPos;
            float3 objNormal;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.localPos = v.vertex.xyz;
            o.objNormal = v.normal;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float nl = length(IN.objNormal);
            float3 n = nl > 1e-4 ? IN.objNormal / nl : float3(0, 1, 0); // нулевая нормаль не даёт «битых» пикселей
            float3 objScale = float3(
                length(unity_ObjectToWorld._m00_m10_m20),
                length(unity_ObjectToWorld._m01_m11_m21),
                length(unity_ObjectToWorld._m02_m12_m22));
            float3 ps = IN.localPos * objScale;
            if (_Voxel > 0) ps = (floor((ps - n * (_Voxel * 0.5)) / _Voxel) + 0.5) * _Voxel;
            float3 an = abs(n);
            an = pow(an, 4);
            an /= (an.x + an.y + an.z + 1e-5);
            float3 sp = ps * _Scale;
            fixed4 t = tex2Dlod(_MainTex, float4(sp.zy, 0, 0)) * an.x + tex2Dlod(_MainTex, float4(sp.xz, 0, 0)) * an.y + tex2Dlod(_MainTex, float4(sp.xy, 0, 0)) * an.z;
            o.Albedo = lerp(_Color.rgb, _Color2.rgb, t.g) * t.r;
            o.Emission = _Highlight.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
            o.Alpha = _Alpha;
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
