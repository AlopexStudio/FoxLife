// Серая текстура-узор (R = светотень, G = маска второго цвета), раскрашенная двумя цветами.
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
        Tags { "RenderType"="Opaque" "DisableBatching"="True" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _Color2;
        float _Scale;
        half _Glossiness;
        fixed4 _Highlight;
        float _Voxel;
        float _UseAO;
        float _DepthBias;

        struct Input
        {
            float4 localPosAo;   // xyz — позиция в объекте, w — затенение угла
            float3 objNormal;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.localPosAo = float4(v.vertex.xyz, v.color.r);
            o.objNormal = v.normal;
            // защита от z-fighting: слой (одежда поверх меха, мебель поверх стен) чуть ближе к камере
            // вдоль луча взгляда — на экране ничего не сдвигается, меняется только глубина
            if (_DepthBias > 0)
            {
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 toCam = _WorldSpaceCameraPos - wp;
                float d = length(toCam);
                wp += toCam / max(d, 1e-3) * _DepthBias * (0.0012 + d * 0.00035);
                v.vertex.xyz = mul(unity_WorldToObject, float4(wp, 1)).xyz;
            }
        }

        float hash3(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float nl = length(IN.objNormal);
            float3 n = nl > 1e-4 ? IN.objNormal / nl : float3(0, 1, 0); // нулевая нормаль не даёт «битых» пикселей
            float3 p = IN.localPosAo.xyz;
            float3 objScale = float3(
                length(unity_ObjectToWorld._m00_m10_m20),
                length(unity_ObjectToWorld._m01_m11_m21),
                length(unity_ObjectToWorld._m02_m12_m22));
            float vary = 1;
            float ao = 1;
            float3 ps = p * objScale; // позиция в метрах
            if (_Voxel > 0)
            {
                // центр воксела, которому принадлежит эта грань
                float3 cell = floor((ps - n * (_Voxel * 0.5)) / _Voxel);
                ps = (cell + 0.5) * _Voxel;
                vary = 1 + (hash3(cell) - 0.5) * 0.07;
                if (_UseAO > 0) ao = IN.localPosAo.w;
            }
            float3 an = abs(n);
            an = pow(an, 4);
            an /= (an.x + an.y + an.z + 1e-5);
            float3 sp = ps * _Scale;
            fixed4 tx = tex2Dlod(_MainTex, float4(sp.zy, 0, 0));
            fixed4 ty = tex2Dlod(_MainTex, float4(sp.xz, 0, 0));
            fixed4 tz = tex2Dlod(_MainTex, float4(sp.xy, 0, 0));
            fixed4 t = tx * an.x + ty * an.y + tz * an.z;
            fixed3 col = lerp(_Color.rgb, _Color2.rgb, t.g) * t.r * vary;
            #ifndef UNITY_COLORSPACE_GAMMA
            ao = GammaToLinearSpace(float3(ao, ao, ao)).r;
            #endif
            o.Albedo = col * ao;
            o.Emission = _Highlight.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
