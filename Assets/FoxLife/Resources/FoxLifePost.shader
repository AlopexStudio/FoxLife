// Постобработка: мягкое свечение (bloom), плёночная тональная кривая, тёплая цветокоррекция,
// виньетка и «миниатюра» — края кадра чуть размыты, как у макросъёмки игрушечного городка.
Shader "Hidden/FoxLife/Post"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    sampler2D _BlurTex;
    float _Threshold, _Knee, _BloomStrength;
    float _Exposure, _Saturation, _Contrast, _Vignette, _TiltShift;
    float4 _Warm;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    half3 Box4(float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-d, -d, d, d);
        return (tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb) * 0.25;
    }

    // 0: яркие места (с мягким порогом) и уменьшение
    half Luma(half3 c) { return dot(c, half3(0.2126, 0.7152, 0.0722)); }

    // Светлые точки и тонкие светлые линии в щёлках между гранями вокселей: пиксель не может быть ярче,
    // чем «меньший из максимумов» соседей по вертикали и по горизонтали. Край большой яркой области так не страдает,
    // а одиночный блик или щель в 1 пиксель — гаснет. Звёзды на тёмном небе не трогаем.
    // «Битый» пиксель (NaN или бесконечность — бывает на вырожденных гранях): свечение размазывает его
    // в яркое пятно с тёмным ободком. Проверка по битам — компилятор её не выбросит.
    bool Bad(float3 c)
    {
        uint3 b = asuint(c) & 0x7F800000u;
        return any(b == 0x7F800000u);
    }

    float3 Safe(float2 uv)
    {
        float3 c = tex2D(_MainTex, uv).rgb;
        return Bad(c) ? float3(-1, -1, -1) : clamp(c, 0, 8);
    }

    half3 Despeckle(float2 uv)
    {
        float2 t = _MainTex_TexelSize.xy;
        float3 c = Safe(uv);
        float3 l = Safe(uv - float2(t.x, 0)), r = Safe(uv + float2(t.x, 0));
        float3 d = Safe(uv - float2(0, t.y)), u = Safe(uv + float2(0, t.y));
        // битые соседи не считаем; битый центр — заменяем средним целых соседей
        float4 acc = 0;
        if (l.x >= 0) acc += float4(l, 1); else l = 0;
        if (r.x >= 0) acc += float4(r, 1); else r = 0;
        if (d.x >= 0) acc += float4(d, 1); else d = 0;
        if (u.x >= 0) acc += float4(u, 1); else u = 0;
        if (c.x < 0) return acc.w > 0 ? acc.rgb / acc.w : 0;
        half3 lim = min(max(l, r), max(d, u));
        half nb = max(max(Luma(l), Luma(r)), max(Luma(d), Luma(u)));
        if (nb < 0.06) return c;
        // режем только яркость, оттенок сохраняем (иначе у оранжевого окна на фоне неба появлялся синий ободок)
        half lc = Luma(c), ll = Luma(lim) * 1.08 + 0.02;
        return lc > ll ? c * (ll / max(lc, 1e-4)) : c;
    }

    half4 fragPrefilter (v2f i) : SV_Target
    {
        // среднее Кариса: яркий одиночный пиксель (блик на грани вокселя) не раздувается в мигающее пятно
        float4 o = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        half3 a = min(Despeckle(i.uv + o.xy), 6), b = min(Despeckle(i.uv + o.zy), 6);
        half3 c2 = min(Despeckle(i.uv + o.xw), 6), d = min(Despeckle(i.uv + o.zw), 6);
        half wa = 1 / (1 + Luma(a)), wb = 1 / (1 + Luma(b)), wc = 1 / (1 + Luma(c2)), wd = 1 / (1 + Luma(d));
        half3 c = (a * wa + b * wb + c2 * wc + d * wd) / (wa + wb + wc + wd);
        half br = max(c.r, max(c.g, c.b));
        half soft = clamp(br - _Threshold + _Knee, 0, 2 * _Knee);
        soft = soft * soft / (4 * _Knee + 1e-4);
        half contrib = max(soft, br - _Threshold) / max(br, 1e-4);
        return half4(c * contrib, 1);
    }
    // 1: уменьшение
    half4 fragDown (v2f i) : SV_Target { return half4(Box4(i.uv, 1), 1); }
    // 2: увеличение (смешивается с уровнем выше аддитивно)
    half4 fragUp (v2f i) : SV_Target { return half4(Box4(i.uv, 0.5), 1); }

    half3 ACES(half3 x)
    {
        const half a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
        return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
    }

    // 3: итог
    half4 fragFinal (v2f i) : SV_Target
    {
        half3 c = Despeckle(i.uv);
        // миниатюра: сверху и снизу кадр мягко расплывается
        float band = saturate((abs(i.uv.y - 0.5) - 0.2) / 0.3);
        band = band * band * _TiltShift;
        half3 blur = tex2D(_BlurTex, i.uv).rgb;
        if (Bad(blur)) blur = c;
        c = lerp(c, blur, band);
        half3 bloom = tex2D(_BloomTex, i.uv).rgb;
        if (!Bad(bloom)) c += min(bloom, 4) * _BloomStrength;
        c *= _Exposure;
        c = ACES(c * 1.05) * 1.04;
        // цвет: чуть теплее, сочнее и мягче по контрасту
        c *= _Warm.rgb;
        half l = dot(c, half3(0.2126, 0.7152, 0.0722));
        c = lerp(l.xxx, c, _Saturation);
        c = saturate((c - 0.5) * _Contrast + 0.5);
        // виньетка
        float2 v = (i.uv - 0.5) * float2(1.0, 0.8);
        c *= 1 - _Vignette * smoothstep(0.25, 0.85, dot(v, v) * 2.2);
        return half4(c, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPrefilter
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
            ENDCG }
        Pass { Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragFinal
            ENDCG }
    }
}
