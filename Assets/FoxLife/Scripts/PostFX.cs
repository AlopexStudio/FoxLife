using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FoxLife
{
    /// <summary>
    /// Постобработка (URP): свечение (bloom) фонарей, окон и экранов, тональная кривая ACES, тёплая цветокоррекция,
    /// виньетка — через глобальный объём URP, который создаётся здесь же; «миниатюра» (верх и низ кадра расплываются)
    /// и «антиблик» — свои полноэкранные проходы (Settings/FoxRenderer). Ночью свечение сильнее, порог ниже.
    /// Ещё здесь каждый кадр передаётся шейдерам цвет окружения (_FoxAmbient).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PostFX : MonoBehaviour
    {
        public static bool enabledFx = true, tiltShift = true;
        static readonly int AmbientId = Shader.PropertyToID("_FoxAmbient"), TiltId = Shader.PropertyToID("_FoxTiltShift");

        Volume volume;
        Bloom bloom;
        SplitToning split;
        // что сейчас за окном (задаёт игра): время года, закат/рассвет; погоду берём у Weather
        public static int season;
        public static float dusk;
        ColorAdjustments color;
        Vignette vignette;
        UniversalAdditionalCameraData camData;

        public static void Load()
        {
            enabledFx = PlayerPrefs.GetInt("fox_post", 1) == 1;
            tiltShift = PlayerPrefs.GetInt("fox_tilt", 1) == 1;
        }

        public static void SaveSettings()
        {
            PlayerPrefs.SetInt("fox_post", enabledFx ? 1 : 0);
            PlayerPrefs.SetInt("fox_tilt", tiltShift ? 1 : 0);
            PlayerPrefs.Save();
        }

        void Start()
        {
            var cam = GetComponent<Camera>();
            cam.allowHDR = true;
            camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.stopNaN = true; // «битые» пиксели не попадают в свечение
            camData.antialiasing = AntialiasingMode.None;

            var go = new GameObject("Постобработка");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            bloom = profile.Add<Bloom>(true);
            bloom.scatter.Override(0.65f);
            bloom.clamp.Override(8f);
            bloom.highQualityFiltering.Override(true);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(10f);
            color.contrast.Override(4f);
            split = profile.Add<SplitToning>(true);
            split.balance.Override(10f);
            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.26f);
            vignette.smoothness.Override(0.45f);
        }

        void LateUpdate()
        {
            // окружение для шейдеров (в URP «ровный» цвет окружения задаём сами — погода и время суток его меняют)
            Shader.SetGlobalColor(AmbientId, RenderSettings.ambientLight.linear);

            var game = Game.I;
            float night = 0;
            if (game != null) { float h = game.Hour; night = Mathf.Clamp01(Mathf.InverseLerp(19f, 21f, h)) + Mathf.Clamp01(Mathf.InverseLerp(6.5f, 5f, h)); night = Mathf.Clamp01(night); }
            if (camData != null) camData.renderPostProcessing = enabledFx;
            float camDist = game != null && game.cam != null ? game.cam.dist : 30f;
            Shader.SetGlobalFloat(TiltId, enabledFx && tiltShift ? 0.85f * Mathf.InverseLerp(10f, 45f, camDist) : 0f);
            if (bloom == null) return;
            // светятся только по-настоящему яркие места (костёр, фонари ночью)
            bloom.threshold.Override(Mathf.Lerp(1.5f, 1.0f, night));
            bloom.intensity.Override(Mathf.Lerp(0.3f, 0.9f, night));
            // цветокоррекция по свету и погоде: тёплый день, золотой закат, синяя ночь, холодный дождь, пастельный туман
            var w = game != null ? game.weather : null;
            float rain = w != null ? Mathf.Clamp01(w.rain + w.storm * 0.5f) : 0f, fog = w != null ? w.fog : 0f;
            Color filter = Color.Lerp(new Color(1.03f, 1.0f, 0.96f), new Color(0.95f, 0.98f, 1.08f), night);
            filter = Color.Lerp(filter, new Color(1.08f, 0.97f, 0.88f), dusk * (1f - night) * 0.8f);
            filter = Color.Lerp(filter, new Color(0.95f, 0.98f, 1.04f), rain * 0.7f);
            filter = Color.Lerp(filter, new Color(1.02f, 1.0f, 1.03f), fog * 0.6f);
            if (season == 3) filter = Color.Lerp(filter, new Color(0.97f, 0.99f, 1.04f), 0.5f);
            color.colorFilter.Override(filter);
            color.postExposure.Override(Mathf.Log(Mathf.Lerp(1.12f, 1.25f, night) + fog * 0.03f, 2f));
            color.saturation.Override(10f + dusk * 8f - rain * 12f - fog * 8f + (season == 2 ? 6f : 0f));
            color.contrast.Override(4f - fog * 5f - rain * 3f);
            // тени чуть сиреневые, света тёплые (как в уютных открытках); ночью тени синее
            split.shadows.Override(Color.Lerp(new Color(0.56f, 0.5f, 0.62f), new Color(0.38f, 0.45f, 0.7f), night));
            split.highlights.Override(Color.Lerp(new Color(0.62f, 0.55f, 0.46f), new Color(0.66f, 0.5f, 0.38f), dusk));
            bloom.intensity.Override(Mathf.Lerp(0.3f, 1.05f, night) + dusk * 0.15f);
        }

        /// <summary>Отрисовать камеру в текстуру (портреты, отладка) — в URP через запрос рендера.</summary>
        public static void RenderTo(Camera cam, RenderTexture rt)
        {
            float tilt = Shader.GetGlobalFloat(TiltId);
            Shader.SetGlobalFloat(TiltId, 0f); // портреты — без «миниатюры»
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            Shader.SetGlobalFloat(TiltId, tilt);
        }
    }
}
