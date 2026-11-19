// Общее освещение «Лисьей жизни» под URP: PBR URP (солнце с тенями, фонари через Forward+, туман),
// а «окружение» — ровный цвет _FoxAmbient, который игра выставляет сама (утро, день, ночь, погода).
#ifndef FOXLIFE_LIT_INCLUDED
#define FOXLIFE_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float4 _FoxAmbient; // линейный цвет окружения

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
