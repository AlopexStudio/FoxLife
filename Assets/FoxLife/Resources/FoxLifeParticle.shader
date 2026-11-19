// Частицы: цвет вершины × цвет материала, полупрозрачно. Освещаются солнцем и окружением (_Unlit = 1 — светятся сами).
Shader "FoxLife/Particle"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Unlit ("Unlit", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            fixed4 _Color;
            float _Unlit;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; UNITY_FOG_COORDS(1) };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                fixed4 c = v.color * _Color;
                float3 light = UNITY_LIGHTMODEL_AMBIENT.rgb * 1.3 + _LightColor0.rgb * 0.75;
                c.rgb *= lerp(min(light, 1.6), 1.0, saturate(_Unlit));
                o.color = c;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { fixed4 c = i.color; UNITY_APPLY_FOG(i.fogCoord, c); return c; }
            ENDCG
        }
    }
}
