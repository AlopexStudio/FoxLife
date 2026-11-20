using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Картинки предметов для каталога стройки: предмет собирается «призраком» глубоко под землёй, снимается
    /// отдельной камерой в маленькую текстуру и сразу убирается. Каждая картинка рисуется один раз (по 2 за кадр,
    /// в LateUpdate — в URP камеры нельзя рисовать из OnGUI).
    /// </summary>
    public partial class GameUI
    {
        readonly Dictionary<string, RenderTexture> thumbs = new Dictionary<string, RenderTexture>();
        readonly List<FurnitureDef> thumbQueue = new List<FurnitureDef>();
        Camera thumbCam;
        Light thumbLight;
        static readonly Color ThumbBg = new Color(0.95f, 0.92f, 0.87f);

        /// <summary>Картинка предмета (или null, пока не нарисована — тогда встаёт в очередь).</summary>
        Texture Thumb(FurnitureDef d)
        {
            if (d == null) return null;
            if (thumbs.TryGetValue(d.id, out var rt) && rt != null && rt.IsCreated()) return rt;
            if (!thumbQueue.Contains(d)) thumbQueue.Add(d);
            return null;
        }

        void RenderThumbs()
        {
            if (G == null || G.house == null || G.loading) return;
            for (int n = 0; n < 2 && thumbQueue.Count > 0; n++)
            {
                var d = thumbQueue[0];
                thumbQueue.RemoveAt(0);
                RenderThumbNow(d);
            }
        }

        void RenderThumbNow(FurnitureDef d)
        {
            if (thumbs.TryGetValue(d.id, out var old) && old != null && old.IsCreated()) return;
            var rt = new RenderTexture(176, 150, 16) { antiAliasing = 4 };
            rt.Create();
            thumbs[d.id] = rt;
            Furniture f = null;
            try
            {
                f = G.SpawnFurniture(d, new Vector2Int(0, 0), 0, true);
                f.transform.position = new Vector3(0, -400f, 0);
                SetLayer(f.transform, 30);
                var rs = f.GetComponentsInChildren<Renderer>(true);
                bool any = false;
                var b = new Bounds(f.transform.position, Vector3.one * 0.5f);
                foreach (var r in rs)
                {
                    if (r is ParticleSystemRenderer || !r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (thumbCam == null)
                {
                    var go = new GameObject("Thumb camera");
                    thumbCam = go.AddComponent<Camera>();
                    thumbCam.enabled = false;
                    thumbCam.clearFlags = CameraClearFlags.SolidColor;
                    thumbCam.fieldOfView = 24f;
                    thumbCam.cullingMask = 1 << 30;
                    var lg = new GameObject("Thumb light");
                    lg.transform.SetParent(go.transform, false);
                    thumbLight = lg.AddComponent<Light>();
                    thumbLight.type = LightType.Directional;
                    thumbLight.intensity = 1.0f;
                    thumbLight.cullingMask = 1 << 30;
                    thumbLight.shadows = LightShadows.None;
                    thumbLight.enabled = false;
                    lg.transform.localRotation = Quaternion.Euler(25, -30, 0);
                }
                // спереди-сбоку-сверху: у большинства вещей «лицо» смотрит к −z
                var dir = new Vector3(0.75f, 0.8f, -1.25f).normalized;
                float rad = Mathf.Max(0.35f, b.extents.magnitude);
                float dist = rad / Mathf.Tan(thumbCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                thumbCam.transform.position = b.center + dir * dist;
                thumbCam.transform.LookAt(b.center);
                thumbCam.nearClipPlane = Mathf.Max(0.02f, dist - rad * 2f);
                thumbCam.farClipPlane = dist + rad * 3f;
                thumbCam.backgroundColor = ThumbBg;
                thumbCam.targetTexture = rt;
                thumbLight.enabled = true;
                var amb = RenderSettings.ambientLight;
                RenderSettings.ambientLight = new Color(0.66f, 0.64f, 0.66f);
                bool fogOn = RenderSettings.fog;
                RenderSettings.fog = false;
                PostFX.RenderTo(thumbCam, rt);
                RenderSettings.fog = fogOn;
                RenderSettings.ambientLight = amb;
                thumbLight.enabled = false;
            }
            catch (System.Exception e) { Debug.LogWarning("FoxLife: картинка предмета " + d.id + ": " + e.Message); }
            finally { if (f != null) Destroy(f.gameObject); }
        }

        // ---------- каталог карточками ----------

        string buildSearch = "";

        /// <summary>Сетка карточек: картинка, название, цена. Наведи — описание; нажми — поставить.</summary>
        void CatalogGrid(string[] cats, float width)
        {
            const float cw = 106, ch = 128, gap = 6;
            int cols = Mathf.Max(1, Mathf.FloorToInt((width + gap) / (cw + gap)));
            var list = new List<FurnitureDef>();
            string q = (buildSearch ?? "").Trim().ToLowerInvariant();
            foreach (var d in Catalog.All)
            {
                if (!d.buildable || System.Array.IndexOf(cats, d.category) < 0) continue;
                if (q.Length > 0 ? !d.name.ToLowerInvariant().Contains(q) : d.category != cats[buildCat]) continue;
                list.Add(d);
            }
            if (list.Count == 0) { GUILayout.Label(q.Length > 0 ? "Ничего не нашлось." : "Здесь пока пусто.", sSmall); return; }
            var nameSt = new GUIStyle(sSmall) { alignment = TextAnchor.UpperCenter, fontSize = 11, wordWrap = true, clipping = TextClipping.Clip };
            var priceSt = new GUIStyle(sBold) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            for (int i = 0; i < list.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(i + cols, list.Count); k++)
                {
                    var d = list[k];
                    var r = GUILayoutUtility.GetRect(cw, ch, W(cw), Hh(ch));
                    bool afford = G.CanAfford(d);
                    bool hover = r.Contains(Event.current.mousePosition);
                    bool cur = G.ghost != null && G.ghost.def == d;
                    if (Event.current.type == EventType.Repaint)
                    {
                        Fill(r, cur ? new Color(0.98f, 0.62f, 0.32f) : hover ? new Color(0.86f, 0.92f, 0.97f) : new Color(1f, 0.99f, 0.97f));
                        var tr = new Rect(r.x + 4, r.y + 4, r.width - 8, 76);
                        var tex = Thumb(d);
                        var oldc = GUI.color;
                        if (!afford) GUI.color = new Color(1, 1, 1, 0.45f);
                        if (tex != null) GUI.DrawTexture(tr, tex, ScaleMode.ScaleAndCrop);
                        else Fill(tr, ThumbBg);
                        GUI.color = oldc;
                    }
                    else Thumb(d);
                    priceSt.normal.textColor = !afford ? new Color(0.75f, 0.3f, 0.25f) : cur ? Color.white : new Color(0.25f, 0.45f, 0.2f);
                    nameSt.normal.textColor = cur ? Color.white : SimsInk;
                    GUI.Label(new Rect(r.x + 3, r.y + 82, r.width - 6, 30), d.name, nameSt);
                    if (!G.freeBuild) GUI.Label(new Rect(r.x, r.yMax - 20, r.width, 18), d.price + " мон.", priceSt);
                    if (hover) tip = d.name + (string.IsNullOrEmpty(d.description) ? "" : "\n" + d.description) + (afford ? "" : "\nНе хватает монет.");
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none) && afford) { var dd = d; Defer(() => G.StartPlacing(dd)); }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
        }
    }
}
