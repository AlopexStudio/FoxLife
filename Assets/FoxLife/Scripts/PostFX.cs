using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Постобработка на камере: bloom (свечение фонарей, окон и экранов), тональная кривая ACES,
    /// тёплая цветокоррекция, виньетка и «миниатюра» (края кадра размыты, как у игрушечного городка).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PostFX : MonoBehaviour
    {
        public static bool enabledFx = true, tiltShift = true;
        Material mat;
        const int Levels = 5;
        readonly RenderTexture[] chain = new RenderTexture[Levels];

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
            var sh = Shader.Find("Hidden/FoxLife/Post");
            if (sh != null && sh.isSupported) mat = new Material(sh);
            GetComponent<Camera>().allowHDR = true;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null || !enabledFx) { Graphics.Blit(src, dst); return; }
            var game = Game.I;
            float night = 0;
            if (game != null) { float h = game.Hour; night = Mathf.Clamp01(Mathf.InverseLerp(19f, 21f, h)) + Mathf.Clamp01(Mathf.InverseLerp(6.5f, 5f, h)); night = Mathf.Clamp01(night); }

            // bloom: яркое → уменьшения → увеличения
            mat.SetFloat("_Threshold", Mathf.Lerp(1.5f, 1.0f, night)); // светятся только по-настоящему яркие места (костёр, фонари ночью)
            mat.SetFloat("_Knee", 0.5f);
            int w = src.width / 2, h2 = src.height / 2;
            var fmt = src.format;
            chain[0] = RenderTexture.GetTemporary(w, h2, 0, fmt);
            Graphics.Blit(src, chain[0], mat, 0);
            int n = 1;
            for (; n < Levels; n++)
            {
                w /= 2; h2 /= 2;
                if (w < 8 || h2 < 8) break;
                chain[n] = RenderTexture.GetTemporary(w, h2, 0, fmt);
                Graphics.Blit(chain[n - 1], chain[n], mat, 1);
            }
            for (int i = n - 2; i >= 0; i--) Graphics.Blit(chain[i + 1], chain[i], mat, 2);

            // размытая копия кадра для «миниатюры»
            RenderTexture blur = null, b1 = null;
            if (tiltShift)
            {
                b1 = RenderTexture.GetTemporary(src.width / 2, src.height / 2, 0, fmt);
                blur = RenderTexture.GetTemporary(src.width / 4, src.height / 4, 0, fmt);
                Graphics.Blit(src, b1, mat, 1);
                Graphics.Blit(b1, blur, mat, 1);
                mat.SetTexture("_BlurTex", blur);
            }
            float camDist = game != null && game.cam != null ? game.cam.dist : 30f;
            mat.SetFloat("_TiltShift", tiltShift ? 0.85f * Mathf.InverseLerp(10f, 45f, camDist) : 0f);
            mat.SetTexture("_BloomTex", chain[0]);
            mat.SetFloat("_BloomStrength", Mathf.Lerp(0.22f, 0.7f, night));
            mat.SetFloat("_Exposure", Mathf.Lerp(1.12f, 1.25f, night));
            mat.SetFloat("_Saturation", 1.1f);
            mat.SetFloat("_Contrast", 1.04f);
            mat.SetFloat("_Vignette", 0.28f);
            mat.SetVector("_Warm", Color.Lerp(new Color(1.03f, 1.0f, 0.96f), new Color(0.97f, 0.98f, 1.06f), night));
            Graphics.Blit(src, dst, mat, 3);

            for (int i = 0; i < n; i++) { RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
            if (b1 != null) RenderTexture.ReleaseTemporary(b1);
            if (blur != null) RenderTexture.ReleaseTemporary(blur);
        }
    }
}
