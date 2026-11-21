// Общее освещение «Лисьей жизни» под URP: PBR URP (солнце с тенями, фонари через Forward+, туман),
// а «окружение» — ровный цвет _FoxAmbient, который игра выставляет сама (утро, день, ночь, погода).
#ifndef FOXLIFE_LIT_INCLUDED
#define FOXLIFE_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float4 _FoxAmbient; // линейный цвет окружения

// Времена года (задаёт игра каждый кадр): оттенок листвы, доля осени и весеннего цветения, снег.
float4 _FoxFoliageTint;
float _FoxAutumn, _FoxSpring, _FoxSnow;

float FoxHash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

// плавный шум по земле: пятна цветения и осенних оттенков без резких границ
float FoxNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    float a = FoxHash(float3(i, 0)), b = FoxHash(float3(i + float2(1, 0), 0));
    float c = FoxHash(float3(i + float2(0, 1), 0)), d = FoxHash(float3(i + float2(1, 1), 0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Листва по сезону (весной часть деревьев цветёт розовым, осенью кроны рыжие, жёлтые и бордовые — у каждого
// дерева своя) и снег на всём, что смотрит вверх. foliage и snowOk — 0/1 у материала.
half3 FoxSeason(half3 c, half3 nWS, float3 seed, float foliage, float snowOk)
{
    if (foliage > 0)
    {
        float h = FoxNoise(seed.xz / 9.0);
        half lum = dot(c, half3(0.3, 0.59, 0.11));
        // осень: от рыжего через золотой к бордовому — плавно по долине
        half3 aut = h < 0.5 ? lerp(half3(1.0, 0.45, 0.12), half3(1.0, 0.72, 0.2), smoothstep(0.3, 0.5, h))
                            : lerp(half3(1.0, 0.72, 0.2), half3(0.8, 0.26, 0.16), smoothstep(0.62, 0.8, h));
        c = lerp(c, lum * aut * 2.2, _FoxAutumn * foliage);
        // весна: цветущие пятна нежно-розового (светлее там, где листва светлее)
        half3 bloom = half3(0.98, 0.62, 0.76) * (0.55 + lum * 1.8);
        c = lerp(c, lerp(c * 1.06, bloom, smoothstep(0.42, 0.62, h)), _FoxSpring * foliage);
        c *= lerp(half3(1, 1, 1), (half3)_FoxFoliageTint.rgb, foliage);
    }
    if (snowOk > 0 && _FoxSnow > 0)
    {
        half s = smoothstep(0.45, 0.8, nWS.y) * _FoxSnow * snowOk;
        c = lerp(c, half3(0.86, 0.9, 0.97), s);
    }
    return c;
}

// Защита от z-fighting: слой (одежда поверх меха, мебель поверх стен) чуть ближе к камере вдоль луча взгляда —
// на экране ничего не сдвигается, меняется только глубина.
float3 FoxDepthBias(float3 wp, float layer)
{
    if (layer <= 0) return wp;
    float3 toCam = _WorldSpaceCameraPos - wp;
    float d = length(toCam);
    return wp + toCam / max(d, 1e-3) * layer * (0.0012 + d * 0.00035);
}

half3 FoxSafeNormal(float3 n)
{
    float l = length(n);
    return l > 1e-4 ? (half3)(n / l) : half3(0, 1, 0); // нулевая нормаль не даёт «битых» пикселей
}

half4 FoxShade(half3 albedo, half alpha, half smoothness, half3 emission, float3 positionWS, half3 normalWS, float4 positionCS, float fogCoord)
{
    InputData id = (InputData)0;
    id.positionWS = positionWS;
    id.positionCS = positionCS;
    id.normalWS = normalWS;
    id.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    id.shadowCoord = TransformWorldToShadowCoord(positionWS);
    id.fogCoord = fogCoord;
    id.bakedGI = _FoxAmbient.rgb;
    id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    id.shadowMask = half4(1, 1, 1, 1);

    SurfaceData sd = (SurfaceData)0;
    sd.albedo = albedo;
    sd.alpha = alpha;
    sd.smoothness = smoothness;
    sd.occlusion = 1;
    sd.emission = emission;
    sd.metallic = 0;
    sd.specular = 0;

    half4 c = UniversalFragmentPBR(id, sd);
    c.rgb = MixFog(c.rgb, fogCoord);
    c.a = alpha;
    return c;
}

#endif
