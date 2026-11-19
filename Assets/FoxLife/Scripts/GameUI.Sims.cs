using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Раскладка интерфейса в духе Sims 4: внизу слева — время и живой портрет выбранной лисички с настроением,
    /// рядом — портреты семьи; внизу справа — панель со вкладками-значками; вверху справа — режимы игры;
    /// вверху слева — всплывающие уведомления. Все значки рисуются кодом.
    /// </summary>
    public partial class GameUI
    {
        // ================= значки =================

        readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        Texture2D texCircleMask, texGem, texRing, texDot;

        delegate float Shape(float x, float y); // покрытие 0..1 в точке (координаты 0..1)

        static Texture2D Paint(int size, Shape s)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 4 подвыборки на пиксель — мягкие края
                    float a = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        float fx = (x + 0.25f + (k & 1) * 0.5f) / size, fy = 1f - (y + 0.25f + (k >> 1) * 0.5f) / size;
                        a += Mathf.Clamp01(s(fx, fy));
                    }
                    px[y * size + x] = new Color(1, 1, 1, a / 4f);
                }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        static float Circle(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) < r * r ? 1 : 0;
        static float Ring(float x, float y, float cx, float cy, float r, float w) { float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); return Mathf.Abs(d - r) < w ? 1 : 0; }
        static float Box(float x, float y, float x0, float y0, float x1, float y1) => x >= x0 && x <= x1 && y >= y0 && y <= y1 ? 1 : 0;
        static float Tri(float x, float y, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (x - b.x) * (a.y - b.y) - (a.x - b.x) * (y - b.y);
            float d2 = (x - c.x) * (b.y - c.y) - (b.x - c.x) * (y - c.y);
            float d3 = (x - a.x) * (c.y - a.y) - (c.x - a.x) * (y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return neg && pos ? 0 : 1;
        }
        static float Star(float x, float y, float cx, float cy, float r, int n, float inner)
        {
            float dx = x - cx, dy = y - cy;
            float ang = Mathf.Atan2(dy, dx) + Mathf.PI / 2f;
            float seg = Mathf.PI * 2f / n;
            float t = Mathf.Abs(Mathf.Repeat(ang, seg) - seg / 2f) / (seg / 2f);
            float rr = Mathf.Lerp(r, r * inner, t);
            return dx * dx + dy * dy < rr * rr ? 1 : 0;
        }
        static float Heart(float x, float y)
        {
            float px = (x - 0.5f) * 2.4f, py = (0.55f - y) * 2.4f;
            float a = px * px + py * py - 1f;
            return a * a * a - px * px * py * py * py < 0 ? 1 : 0;
        }
        static float Max(params float[] v) { float m = 0; foreach (var f in v) m = Mathf.Max(m, f); return m; }

        Texture2D Icon(string id)
        {
            if (icons.TryGetValue(id, out var t)) return t;
            Shape s;
            switch (id)
            {
                case "needs": s = (x, y) => Max(Box(x, y, 0.18f, 0.55f, 0.32f, 0.85f), Box(x, y, 0.43f, 0.3f, 0.57f, 0.85f), Box(x, y, 0.68f, 0.42f, 0.82f, 0.85f)); break;
                case "mood": s = (x, y) => Mathf.Max(Ring(x, y, 0.5f, 0.5f, 0.36f, 0.06f), Max(Circle(x, y, 0.37f, 0.4f, 0.05f), Circle(x, y, 0.63f, 0.4f, 0.05f), Ring(x, y, 0.5f, 0.45f, 0.2f, 0.045f) * (y > 0.55f ? 1 : 0))); break;
                case "fox": s = (x, y) => Max(Tri(x, y, new Vector2(0.18f, 0.15f), new Vector2(0.42f, 0.38f), new Vector2(0.2f, 0.48f)), Tri(x, y, new Vector2(0.82f, 0.15f), new Vector2(0.58f, 0.38f), new Vector2(0.8f, 0.48f)),
                    Tri(x, y, new Vector2(0.16f, 0.36f), new Vector2(0.84f, 0.36f), new Vector2(0.5f, 0.88f))); break;
                case "skills": s = (x, y) => Star(x, y, 0.5f, 0.53f, 0.4f, 5, 0.45f); break;
                case "work": s = (x, y) => Max(Box(x, y, 0.14f, 0.36f, 0.86f, 0.82f), Mathf.Max(Box(x, y, 0.36f, 0.2f, 0.64f, 0.36f) - Box(x, y, 0.43f, 0.26f, 0.57f, 0.36f), 0)); break;
                case "wants": s = (x, y) => Max(Star(x, y, 0.45f, 0.55f, 0.36f, 4, 0.28f), Star(x, y, 0.78f, 0.24f, 0.15f, 4, 0.3f)); break;
                case "friends": s = Heart; break;
                case "family": s = (x, y) => Max(Tri(x, y, new Vector2(0.5f, 0.12f), new Vector2(0.12f, 0.48f), new Vector2(0.88f, 0.48f)), Box(x, y, 0.22f, 0.46f, 0.78f, 0.86f) - Box(x, y, 0.43f, 0.62f, 0.57f, 0.86f)); break;
                case "diary": s = (x, y) => Mathf.Max(Box(x, y, 0.2f, 0.14f, 0.8f, 0.86f) - Box(x, y, 0.3f, 0.3f, 0.7f, 0.34f) - Box(x, y, 0.3f, 0.44f, 0.7f, 0.48f) - Box(x, y, 0.3f, 0.58f, 0.6f, 0.62f), 0); break;
                case "live": s = (x, y) => Max(Circle(x, y, 0.5f, 0.64f, 0.2f), Circle(x, y, 0.26f, 0.42f, 0.09f), Circle(x, y, 0.42f, 0.26f, 0.09f), Circle(x, y, 0.58f, 0.26f, 0.09f), Circle(x, y, 0.74f, 0.42f, 0.09f)); break;
                case "build": s = (x, y) => Max(Tri(x, y, new Vector2(0.5f, 0.1f), new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.48f)), Box(x, y, 0.2f, 0.46f, 0.8f, 0.88f) - Box(x, y, 0.42f, 0.6f, 0.58f, 0.88f), Box(x, y, 0.66f, 0.14f, 0.76f, 0.34f)); break;
                case "style": s = (x, y) => Mathf.Max(Circle(x, y, 0.5f, 0.5f, 0.4f) - Max(Circle(x, y, 0.35f, 0.35f, 0.07f), Circle(x, y, 0.58f, 0.3f, 0.07f), Circle(x, y, 0.7f, 0.5f, 0.07f), Circle(x, y, 0.42f, 0.66f, 0.1f)), 0); break;
                case "create": s = (x, y) => Max(Circle(x, y, 0.5f, 0.42f, 0.18f), Box(x, y, 0.26f, 0.66f, 0.74f, 0.88f) * Circle(x, y, 0.5f, 0.92f, 0.32f)); break;
                case "phone": s = (x, y) => Mathf.Max(Box(x, y, 0.32f, 0.1f, 0.68f, 0.9f) - Box(x, y, 0.38f, 0.2f, 0.62f, 0.7f), Circle(x, y, 0.5f, 0.8f, 0.05f)); break;
                case "menu": s = (x, y) => Max(Box(x, y, 0.2f, 0.25f, 0.8f, 0.33f), Box(x, y, 0.2f, 0.46f, 0.8f, 0.54f), Box(x, y, 0.2f, 0.67f, 0.8f, 0.75f)); break;
                case "pause": s = (x, y) => Max(Box(x, y, 0.28f, 0.22f, 0.43f, 0.78f), Box(x, y, 0.57f, 0.22f, 0.72f, 0.78f)); break;
                case "play1": s = (x, y) => Tri(x, y, new Vector2(0.3f, 0.2f), new Vector2(0.3f, 0.8f), new Vector2(0.78f, 0.5f)); break;
                case "play2": s = (x, y) => Max(Tri(x, y, new Vector2(0.14f, 0.24f), new Vector2(0.14f, 0.76f), new Vector2(0.5f, 0.5f)), Tri(x, y, new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.76f), new Vector2(0.86f, 0.5f))); break;
                case "play3": s = (x, y) => Max(Tri(x, y, new Vector2(0.06f, 0.28f), new Vector2(0.06f, 0.72f), new Vector2(0.34f, 0.5f)), Tri(x, y, new Vector2(0.36f, 0.28f), new Vector2(0.36f, 0.72f), new Vector2(0.64f, 0.5f)), Tri(x, y, new Vector2(0.66f, 0.28f), new Vector2(0.66f, 0.72f), new Vector2(0.94f, 0.5f))); break;
                case "roof": s = (x, y) => Mathf.Max(Tri(x, y, new Vector2(0.5f, 0.18f), new Vector2(0.08f, 0.62f), new Vector2(0.92f, 0.62f)) - Tri(x, y, new Vector2(0.5f, 0.36f), new Vector2(0.28f, 0.6f), new Vector2(0.72f, 0.6f)), 0); break;
                case "walls": s = (x, y) => Max(Box(x, y, 0.12f, 0.2f, 0.3f, 0.82f), Box(x, y, 0.41f, 0.42f, 0.59f, 0.82f), Box(x, y, 0.7f, 0.64f, 0.88f, 0.82f)); break;
                case "names": s = (x, y) => Mathf.Max(Max(Box(x, y, 0.14f, 0.3f, 0.72f, 0.7f), Tri(x, y, new Vector2(0.72f, 0.3f), new Vector2(0.72f, 0.7f), new Vector2(0.9f, 0.5f))) - Circle(x, y, 0.27f, 0.5f, 0.06f), 0); break;
                case "phrases": s = (x, y) => Max(Box(x, y, 0.14f, 0.22f, 0.86f, 0.62f), Tri(x, y, new Vector2(0.28f, 0.6f), new Vector2(0.48f, 0.6f), new Vector2(0.22f, 0.86f))); break;
                case "auto": s = (x, y) => Mathf.Max(Ring(x, y, 0.5f, 0.5f, 0.3f, 0.07f) - Box(x, y, 0.45f, 0.1f, 0.9f, 0.32f), Tri(x, y, new Vector2(0.62f, 0.12f), new Vector2(0.62f, 0.38f), new Vector2(0.86f, 0.25f))); break;
                case "sun": s = (x, y) => Mathf.Max(Circle(x, y, 0.5f, 0.5f, 0.2f), Star(x, y, 0.5f, 0.5f, 0.42f, 8, 0.6f) - Circle(x, y, 0.5f, 0.5f, 0.27f)); break;
                case "cloud": s = (x, y) => Max(Circle(x, y, 0.36f, 0.58f, 0.2f), Circle(x, y, 0.58f, 0.48f, 0.24f), Circle(x, y, 0.75f, 0.62f, 0.15f), Box(x, y, 0.2f, 0.62f, 0.85f, 0.77f)); break;
                case "fog": s = (x, y) => Max(Box(x, y, 0.12f, 0.3f, 0.75f, 0.38f), Box(x, y, 0.25f, 0.48f, 0.88f, 0.56f), Box(x, y, 0.12f, 0.66f, 0.7f, 0.74f)); break;
                case "rain": s = (x, y) => Max(Circle(x, y, 0.36f, 0.4f, 0.17f), Circle(x, y, 0.58f, 0.32f, 0.2f), Box(x, y, 0.2f, 0.42f, 0.8f, 0.55f), Box(x, y, 0.3f, 0.65f, 0.36f, 0.85f), Box(x, y, 0.5f, 0.62f, 0.56f, 0.82f), Box(x, y, 0.7f, 0.65f, 0.76f, 0.85f)); break;
                case "storm": s = (x, y) => Max(Circle(x, y, 0.36f, 0.36f, 0.17f), Circle(x, y, 0.58f, 0.28f, 0.2f), Box(x, y, 0.2f, 0.38f, 0.8f, 0.5f), Tri(x, y, new Vector2(0.55f, 0.5f), new Vector2(0.35f, 0.75f), new Vector2(0.52f, 0.72f)), Tri(x, y, new Vector2(0.52f, 0.66f), new Vector2(0.42f, 0.95f), new Vector2(0.62f, 0.68f))); break;
                default: s = (x, y) => Circle(x, y, 0.5f, 0.5f, 0.3f); break;
            }
            t = Paint(64, s);
            icons[id] = t;
            return t;
        }

        static readonly string[] WeatherIcons = { "sun", "cloud", "fog", "rain", "storm" };

        // ================= сплошные кружки и панели =================

        static readonly Color SimsBlue = new Color(0.17f, 0.6f, 0.84f);
        static readonly Color SimsBlueLight = new Color(0.86f, 0.94f, 0.99f);
        static readonly Color SimsInk = new Color(0.17f, 0.24f, 0.31f);

        void EnsureSims()
        {
            if (texCircleMask != null) return;
            // маска портрета: белое вокруг круга, прозрачное внутри
            texCircleMask = Paint(128, (x, y) => 1f - Circle(x, y, 0.5f, 0.5f, 0.46f));
            texRing = Paint(128, (x, y) => Ring(x, y, 0.5f, 0.5f, 0.465f, 0.035f));
            texDot = Paint(64, (x, y) => Circle(x, y, 0.5f, 0.5f, 0.46f));
            texGem = Paint(64, (x, y) => Max(Tri(x, y, new Vector2(0.5f, 0.04f), new Vector2(0.14f, 0.38f), new Vector2(0.86f, 0.38f)), Tri(x, y, new Vector2(0.14f, 0.38f), new Vector2(0.86f, 0.38f), new Vector2(0.5f, 0.96f))));
        }

        /// <summary>Круглая кнопка со значком (как в Sims 4). Возвращает true по нажатию.</summary>
        bool RoundBtn(Rect r, string icon, bool on, string tipText, Color? tint = null)
        {
            EnsureSims();
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover && !string.IsNullOrEmpty(tipText)) tip = tipText;
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.12f);
                GUI.DrawTexture(new Rect(r.x + 1, r.y + 2, r.width, r.height), texDot);
                GUI.color = on ? SimsBlue : hover ? SimsBlueLight : Color.white;
                GUI.DrawTexture(r, texDot);
                GUI.color = on ? new Color(0.1f, 0.45f, 0.68f) : new Color(0.75f, 0.82f, 0.88f);
                GUI.DrawTexture(r, texRing);
                GUI.color = on ? Color.white : tint ?? SimsInk;
                float p = r.width * 0.22f;
                GUI.DrawTexture(new Rect(r.x + p, r.y + p, r.width - 2 * p, r.height - 2 * p), Icon(icon));
                GUI.color = old;
            }
            return Clicked(GUI.Button(r, GUIContent.none, GUIStyle.none));
        }

        // ================= портреты =================

        Camera portraitCam;
        Light portraitLight;
        readonly Dictionary<Fox, RenderTexture> portraits = new Dictionary<Fox, RenderTexture>();
        float portraitTimer;
        int portraitIdx, portraitTick;

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        RenderTexture Portrait(Fox f)
        {
            if (f == null) return null;
            if (portraits.TryGetValue(f, out var rt) && rt != null) return rt;
            rt = new RenderTexture(160, 160, 16) { antiAliasing = 4 };
            portraits[f] = rt;
            RenderPortrait(f);
            return rt;
        }

        // В URP камеру нельзя рисовать изнутри OnGUI (идёт кадр) — портреты рисуются в LateUpdate, а здесь только очередь.
        readonly List<Fox> portraitQueue = new List<Fox>();
        void RenderPortrait(Fox f) { if (f != null && !portraitQueue.Contains(f)) portraitQueue.Add(f); }

        void LateUpdate()
        {
            UpdateFoxSeen();
            if (portraitQueue.Count == 0) return;
            var list = new List<Fox>(portraitQueue);
            portraitQueue.Clear();
            foreach (var f in list) RenderPortraitNow(f);
        }

        void RenderPortraitNow(Fox f)
        {
            if (f == null || !portraits.TryGetValue(f, out var rt)) return;
            if (portraitCam == null)
            {
                var go = new GameObject("Portrait camera");
                portraitCam = go.AddComponent<Camera>();
                portraitCam.enabled = false;
                portraitCam.clearFlags = CameraClearFlags.SolidColor;
                portraitCam.fieldOfView = 28f;
                portraitCam.nearClipPlane = 0.05f;
                portraitCam.farClipPlane = 6f;
                portraitCam.cullingMask = 1 << 30;
                var lg = new GameObject("Portrait light");
                lg.transform.SetParent(go.transform, false);
                portraitLight = lg.AddComponent<Light>();
                portraitLight.type = LightType.Directional;
                portraitLight.intensity = 0.9f;
                portraitLight.cullingMask = 1 << 30;
                portraitLight.enabled = false;
                lg.transform.localRotation = Quaternion.Euler(20, -25, 0);
            }
            var head = f.HeadPosition;
            var fwd = f.transform.forward; fwd.y = 0; fwd.Normalize();
            var side = Vector3.Cross(Vector3.up, fwd);
            portraitCam.transform.position = head + fwd * 1.15f * f.size + side * 0.35f * f.size + Vector3.up * 0.12f;
            portraitCam.transform.LookAt(head - Vector3.up * 0.06f);
            portraitCam.backgroundColor = Color.Lerp(SimsBlueLight, Fox.MoodColor(f.Mood), 0.25f);
            portraitCam.targetTexture = rt;
            SetLayer(f.transform, 30);
            portraitLight.enabled = true;
            var amb = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.66f);
            bool fogOn = RenderSettings.fog;
            RenderSettings.fog = false;
            PostFX.RenderTo(portraitCam, rt);
            RenderSettings.fog = fogOn;
            RenderSettings.ambientLight = amb;
            portraitLight.enabled = false;
            SetLayer(f.transform, 0);
        }

        void UpdatePortraits()
        {
            portraitTimer -= Time.unscaledDeltaTime;
            if (portraitTimer > 0) return;
            portraitTimer = 0.05f;
            // забытые лисички
            var dead = new List<Fox>();
            foreach (var kv in portraits) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var d in dead) { if (portraits[d] != null) portraits[d].Release(); portraits.Remove(d); }
            if (portraits.Count == 0) return;
            // по кругу — все нарисованные портреты (и в семье, и в меню города); выбранную лисичку — через раз, она на виду
            portraitTick++;
            Fox f;
            if ((portraitTick & 1) == 0 && G.selected != null && portraits.ContainsKey(G.selected)) f = G.selected;
            else
            {
                var keys = new List<Fox>(portraits.Keys);
                portraitIdx = (portraitIdx + 1) % keys.Count;
                f = keys[portraitIdx];
            }
            if (f.gameObject.activeInHierarchy) RenderPortrait(f);
        }

        void DrawPortrait(Rect r, Fox f)
        {
            EnsureSims();
            var rt = Portrait(f);
            if (Event.current.type != EventType.Repaint || rt == null) return;
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.14f);
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 3, r.width, r.height), texDot);
            GUI.color = Color.white;
            GUI.DrawTexture(r, rt);
            GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), texCircleMask);
            GUI.color = f == G.selected ? SimsBlue : new Color(0.8f, 0.86f, 0.9f);
            GUI.DrawTexture(r, texRing);
            GUI.color = old;
        }

        void Gem(Rect r, Color c)
        {
            EnsureSims();
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.2f);
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 2, r.width, r.height), texGem);
            GUI.color = c;
            GUI.DrawTexture(r, texGem);
            GUI.color = new Color(1, 1, 1, 0.45f);
            GUI.DrawTexture(new Rect(r.x + r.width * 0.25f, r.y + r.height * 0.1f, r.width * 0.25f, r.height * 0.3f), texGem);
            GUI.color = old;
        }

        // ================= режимы (вверху справа) =================

        void ModeBar()
        {
            float x0 = vw - 330, y0 = 10;
            var area = new Rect(x0 - 8, y0 - 4, 330, 120);
            Reg(area);
            bool town = G.mode == Mode.Town || G.townBuilder;
            bool plan = !town && (G.mode == Mode.Build || G.mode == Mode.Style);
            if (town)
            {
                (string icon, int k, string name)[] tm = { ("live", 0, "Семьи (F1)"), ("build", 1, "Участки, дома и ландшафт (F2)"), ("style", 2, "Стили (F3)") };
                int cur = !G.townBuilder ? 0 : G.mode == Mode.Style ? 2 : 1;
                for (int i = 0; i < 3; i++)
                {
                    var r = new Rect(x0 + i * 62, y0, 54, 54);
                    int k = tm[i].k;
                    if (RoundBtn(r, tm[i].icon, cur == k, tm[i].name)) Defer(() => G.TownMode(k));
                }
            }
            else if (plan)
            {
                // план обустройства с мольберта: строительство ⇄ стили, «лапка» — вернуться к жизни
                (string icon, Mode m, string name)[] pm = { ("live", Mode.Live, "Закончить план и вернуться к жизни (F1)"), ("build", Mode.Build, "Строительство (F2)"), ("style", Mode.Style, "Стили (F3)") };
                for (int i = 0; i < 3; i++)
                {
                    var r = new Rect(x0 + i * 62, y0, 54, 54);
                    var mm = pm[i].m;
                    if (RoundBtn(r, pm[i].icon, G.mode == mm, pm[i].name)) Defer(() => G.SetMode(mm));
                }
            }
            if (RoundBtn(new Rect(x0 + 3 * 62, y0, 54, 54), "walls", showViewTools, "Показать или спрятать кнопки этажей, крыши, стен и свободы воли (F4)")) Defer(() => showViewTools = !showViewTools);
            if (RoundBtn(new Rect(x0 + 4 * 62 + 10, y0 + 6, 42, 42), "menu", false, "Меню: сохранить, загрузить, город, настройки (Esc)")) Defer(() => screen = MenuScreen.Pause);
            if (!showViewTools) return;
            // вид: этажи, крыша, стены, свобода воли. Если кнопок режимов нет (в жизни) — в один ряд на их месте, иначе под ними
            bool side = !town && !plan;
            float y1 = side ? y0 + 7 : y0 + 62;
            float xs = side ? x0 + 180 - 7 * 44 : x0;
            if (side) Reg(new Rect(xs - 4, y1 - 4, 7 * 44 + 8, 48));
            else Reg(new Rect(x0 - 4, y1 - 4, 7 * 46 + 16, 48));
            Rect Slot(int i) => side ? new Rect(xs + i * 44, y1, 40, 40) : new Rect(x0 + (i < 2 ? i * 46 : i * 46 + 8), y1, 40, 40);
            for (int lv = 0; lv < House.Levels; lv++)
            {
                int k = lv;
                var r = Slot(lv);
                bool on = G.house.viewLevel == lv;
                if (RoundBtn(r, "build", on, $"Этаж {lv + 1} (PageUp / PageDown)")) Defer(() => G.SetViewLevel(k));
                var lbl = new GUIStyle(sBold) { alignment = TextAnchor.LowerRight, fontSize = 12 };
                lbl.normal.textColor = on ? Color.white : SimsInk;
                GUI.Label(new Rect(r.x, r.y, r.width - 4, r.height - 2), (lv + 1).ToString(), lbl);
            }
            if (RoundBtn(Slot(2), "roof", G.house.showRoof, "Показать или спрятать крыши")) Defer(() => { G.house.showRoof = !G.house.showRoof; G.house.ApplyVisibility(); });
            if (RoundBtn(Slot(3), "walls", G.house.wallMode != House.WallMode.Up, "Стены: " + G.house.WallModeName + " (нажми, чтобы сменить)")) Defer(() => G.house.CycleWallMode());
            if (RoundBtn(Slot(4), "auto", G.autonomy, "Свобода воли: " + (G.autonomy ? "лисички сами решают, чем заняться" : "выключена"))) Defer(() => G.autonomy = !G.autonomy);
            LabelBtn(Slot(5), "names", "Имена над лисичками", NameMode, m => NameMode = m);
            LabelBtn(Slot(6), "phrases", "Фразы лисичек", PhraseMode, m => PhraseMode = m);
        }

        /// <summary>Кнопка с тремя режимами: всегда, только видимых, никогда (по кругу).</summary>
        void LabelBtn(Rect r, string icon, string what, LabelMode m, System.Action<LabelMode> set)
        {
            string tip = what + ": " + LabelModeNames[(int)m] + (m == LabelMode.Visible ? " (не сквозь стены, крыши, вещи и холмы)" : "") + ". Нажми, чтобы сменить.";
            if (RoundBtn(r, icon, m != LabelMode.Never, tip)) Defer(() => set((LabelMode)(((int)m + 1) % 3)));
            if (Event.current.type == EventType.Repaint && m == LabelMode.Visible)
            {
                // «только видимые» — маленький глазок в углу кнопки
                var old = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.4f);
                GUI.DrawTexture(new Rect(r.xMax - 13, r.yMax - 13, 12, 12), texDot);
                GUI.color = old;
            }
        }

        // ================= время (внизу слева или вверху слева) =================

        void TimePanel(Rect r)
        {
            Begin(r);
            GUILayout.BeginHorizontal();
            string[] ic = { "pause", "play1", "play2", "play3" };
            string[] spTip = { "Пауза (пробел)", "Обычная скорость (1)", "Быстро (2)", "Очень быстро (3)" };
            var row = GUILayoutUtility.GetRect(4 * 36, 34, GUILayout.Width(4 * 36), GUILayout.Height(34));
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                if (RoundBtn(new Rect(row.x + i * 36, row.y, 32, 32), ic[i], G.speedIndex == i, spTip[i])) Defer(() => G.SetSpeed(k));
            }
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            ColorLabel(G.ClockText, sBold, SimsInk);
            var evs = TownEvents.Today(G.Day);
            if (evs.Count > 0)
            {
                var txt = new List<string>();
                foreach (var e in evs) txt.Add($"{TownEvents.Hours(e)} {e.name}");
                var r0 = GUILayoutUtility.GetLastRect();
                if (r0.Contains(Event.current.mousePosition)) tip = "Сегодня в городе: " + string.Join("; ", txt);
            }
            ColorLabel($"{Mathf.RoundToInt(G.money):N0} мон.".Replace(",", " ").Replace(" ", " "), sSmall, G.money < 0 ? new Color(0.75f, 0.2f, 0.15f) : new Color(0.2f, 0.5f, 0.25f));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            var wr = GUILayoutUtility.GetRect(34, 34, GUILayout.Width(34), GUILayout.Height(34));
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = G.weather.kind == WeatherKind.Clear ? new Color(0.95f, 0.7f, 0.2f) : G.weather.kind == WeatherKind.Storm ? new Color(0.35f, 0.38f, 0.5f) : new Color(0.45f, 0.6f, 0.75f);
                GUI.DrawTexture(wr, Icon(WeatherIcons[(int)G.weather.kind]));
                GUI.color = old;
            }
            if (wr.Contains(Event.current.mousePosition)) tip = "Погода: " + G.weather.Label.ToLower() + ". Меняется сама несколько раз в день.";
            GUILayout.EndHorizontal();
            End();
        }

        // ================= выбранная лисичка (внизу слева) =================

        void PortraitCard()
        {
            var f = G.selected;
            if (f == null) return;
            var r = new Rect(10, vh - 180, 420, 168);
            Begin(r);
            var pr = new Rect(14, 14, 128, 128);
            GUILayout.BeginHorizontal();
            GUILayout.Space(140);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(f.foxName, sTitle);
            GUILayout.FlexibleSpace();

            if (f.current != null && f.current.kind != ActKind.BeTalkedTo && Btn(new GUIContent("×", "Отменить текущее действие"), false, W(28), Hh(26)))
            {
                var c = f.current;
                Defer(() => f.Cancel(c));
            }
            GUILayout.EndHorizontal();
            ColorLabel(f.Summary + " · " + f.StageName, sSmall, SimsBlue * 0.8f);
            float mood = f.Mood;
            ColorLabel(Fox.MoodLabel(mood), sBold, Fox.MoodColor(mood) * 0.75f + Color.black * 0.25f);
            GUILayout.Label(f.ActivityText, sWrap, W(240));
            int qn = 0;
            GUILayout.BeginHorizontal();
            foreach (var q in f.queue)
            {
                if (qn++ >= 3) break;
                var qq = q;
                if (Btn(new GUIContent(qq.label.Length > 14 ? qq.label.Substring(0, 13) + "…" : qq.label, "В очереди: " + qq.label + ". Нажми, чтобы отменить."), false, Hh(24))) Defer(() => f.Cancel(qq));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            DrawPortrait(pr, f);
            Gem(new Rect(pr.xMax - 30, pr.y - 4, 28, 34), Fox.MoodColor(mood));
            if (pr.Contains(Event.current.mousePosition)) tip = "Настроение: " + Fox.MoodLabel(mood) + ". Нажми — камера к лисичке.";
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && pr.Contains(Event.current.mousePosition)) { Event.current.Use(); Defer(() => G.Select(null)); }
            if (pr.Contains(Event.current.mousePosition)) tip = $"{f.foxName}: {Fox.MoodLabel(mood).ToLower()}. Сейчас: {f.ActivityText.ToLower()}.\nНажми — камера следует за лисичкой. ПКМ — снять выбор.";
            if (GUI.Button(pr, GUIContent.none, GUIStyle.none)) Defer(() => { G.cam.Focus(f.transform.position, 9f); G.cam.Follow(f.transform); G.SetViewLevel(f.Level); });
            End();
        }

        void HouseholdStrip()
        {
            var fam = G.Household;
            if (fam.Count <= 1 && G.mode == Mode.Live || G.townBuilder) return;
            float s = 58;
            var r = new Rect(G.mode == Mode.Live ? 440 : 10, vh - 96, fam.Count * (s + 8) + 14, s + 26);
            Begin(r);
            for (int i = 0; i < fam.Count; i++)
            {
                var fox = fam[i];
                var pr = new Rect(12 + i * (s + 8), 8, s, s);
                DrawPortrait(pr, fox);
                Gem(new Rect(pr.xMax - 14, pr.y - 2, 13, 16), Fox.MoodColor(fox.Mood));
                var lbl = new GUIStyle(sSmall) { alignment = TextAnchor.UpperCenter, fontSize = 11, wordWrap = false, clipping = TextClipping.Clip };
                lbl.normal.textColor = SimsInk;
                GUI.Label(new Rect(pr.x - 4, pr.yMax + 1, s + 8, 16), fox.foxName, lbl);
                if (pr.Contains(Event.current.mousePosition)) tip = $"{fox.foxName} — {Fox.MoodLabel(fox.Mood).ToLower()}. Сейчас: {fox.ActivityText.ToLower()}.";
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && pr.Contains(Event.current.mousePosition)) { Event.current.Use(); Defer(() => G.Select(null)); }
                if (GUI.Button(pr, GUIContent.none, GUIStyle.none))
                    Defer(() =>
                    {
                        if (G.selected == fox) { G.cam.Focus(fox.transform.position, 10f); G.cam.Follow(fox.transform); G.SetViewLevel(fox.Level); }
                        G.Select(fox);
                        if (G.mode == Mode.Create) G.cam.Focus(fox.transform.position, 6f);
                    });
            }
            End();
        }

        // ================= переезд =================

        void MoveMenu()
        {
            var items = new List<(string, System.Action)>();
            int sale = G.SellPrice(House.PlayerLotId);
            foreach (var lot in G.VacantHomes())
            {
                var l = lot;
                int price = G.BuyPrice(l.id);
                bool house = G.furniture.Exists(f => f.lot == l.id) || G.house.rooms.Exists(r => r.lot == l.id);
                items.Add(($"{l.name}{(house ? " (дом с мебелью)" : " (пустой участок)")} — {price} мон.", () =>
                {
                    var confirm = new List<(string, System.Action)>
                    {
                        ($"Переехать: свой дом продаётся за {sale}, новый стоит {price}", () => G.MoveTo(l.id)),
                        ("Посмотреть на участок", () => G.cam.Focus(GridMap.Center(l.entry), 22f)),
                        ("Отмена", () => { }),
                    };
                    OpenMenu(Input.mousePosition, "Переезд: " + l.name, confirm);
                }));
            }
            if (items.Count == 0) items.Add(("Свободных домов пока нет", () => { }));
            OpenMenu(Input.mousePosition, "Куда переехать?", items);
        }

        // ================= телефон =================

        public void OpenPhone(Fox me) => PhoneMenu(me);

        void PhoneMenu(Fox me)
        {
            var items = new List<(string, System.Action)>();
            var known = new List<Fox>();
            foreach (var o in G.foxes)
                if (o != me && o.home != me.home && (Social.Rel(me, o) >= 5f || Family.Role(me, o) != null)) known.Add(o);
            known.Sort((a, b) => Social.Rel(me, b).CompareTo(Social.Rel(me, a)));
            foreach (var o in known)
            {
                var friend = o;
                items.Add(($"{friend.foxName} ({Social.RelLabel(Social.Rel(me, friend)).ToLower()})", () => FriendMenu(me, friend)));
            }
            var homes = new HashSet<int>();
            foreach (var o in known) homes.Add(o.home);
            if (known.Count == 0) items.Add(("Пока не с кем — познакомься с соседями!", () => { }));
            OpenMenu(Input.mousePosition, "Телефон: " + me.foxName, items);
        }

        void FriendMenu(Fox me, Fox friend)
        {
            var items = new List<(string, System.Action)>
            {
                ("Поболтать по телефону", () => me.Command(new FoxAction { kind = ActKind.Phone, other = friend, phoneMode = Fox.PhoneMode.Chat, label = "Звонит: " + friend.foxName })),
                ("Пригласить в гости", () => me.Command(new FoxAction { kind = ActKind.Phone, other = friend, phoneMode = Fox.PhoneMode.InviteOver, label = "Приглашает: " + friend.foxName })),
            };
            foreach (var lot in G.house.lots)
            {
                if (lot.removed) continue;
                if (!lot.Public || lot.kind == LotKind.School) continue;
                var l = lot;
                items.Add(("Позвать: " + l.name, () => me.Command(new FoxAction { kind = ActKind.Phone, other = friend, phoneMode = Fox.PhoneMode.InviteOut, placeLot = l.id, label = "Зовёт гулять: " + friend.foxName })));
            }
            items.Add(("Сходить в гости: " + G.house.LotById(friend.home)?.name, () => me.VisitHome(friend.home)));
            OpenMenu(Input.mousePosition, me.foxName + " → " + friend.foxName, items);
        }

        // ================= панель со вкладками (внизу справа) =================

        /// <summary>Открыта ли «мысль» — панель внизу справа. Без мыслей там ничего нет.</summary>
        public bool infoOpen;
        public float infoContentH = 300;
        Fox infoFor;

        public static readonly string[] ThoughtTitles = { "Самочувствие", "Чувства", "Характер", "Умения", "Работа", "Мечты", "Друзья", "Семья", "Прожитое" };

        public Rect InfoRect(float h) => new Rect(vw - 480, vh - h - 12, 470, h);

        /// <summary>Внизу справа — только то, чем лисичка сейчас занята в мыслях: мысль о себе, о другом или выбор одежды.</summary>
        void InfoPanel()
        {
            var f = G.selected;
            if (f != infoFor) { infoFor = f; infoOpen = false; thinkFox = null; clothesOpen = false; }
            if (f == null) return;
            if (clothesOpen) { ClothesPanel(); return; }
            if (!infoOpen) return;
            // высота — по содержимому (замеряется на прошлом кадре), но не больше половины экрана
            Begin(InfoRect(Mathf.Clamp(infoContentH + 74, 150, Mathf.Min(460, vh - 140))));
            if (thinkFox != null) { ThinkPanelContent(f); End(); return; }
            GUILayout.BeginHorizontal();
            string title = foxTab == 4 && f.IsChild ? "Школа" : ThoughtTitles[foxTab];
            GUILayout.Label(title, sTitle);
            GUILayout.FlexibleSpace();
            if (Btn(new GUIContent("×", "Перестать думать об этом"), false, W(30))) Defer(() => infoOpen = false);
            GUILayout.EndHorizontal();
            foxScroll = GUILayout.BeginScrollView(foxScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical();
            FoxTabContent(f);
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) infoContentH = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndScrollView();
            End();
        }

        // ================= уведомления (вверху слева) =================

        void Notifications()
        {
            float y = 10;
            int shown = 0;
            for (int i = G.log.Count - 1; i >= 0 && shown < 5; i--)
            {
                var e = G.log[i];
                float age = Time.time - e.born;
                if (age > 14f) break;
                shown++;
                float a = Mathf.Clamp01(1f - (age - 11f) / 3f) * Mathf.Clamp01(age * 4f);
                var c = new GUIContent($"{e.time}   {e.text}");
                float h = sWrap.CalcHeight(c, 330) + 20;
                var r = new Rect(10 - (1f - Mathf.Clamp01(age * 4f)) * 40f, y, 370, h);
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                GUI.Box(r, GUIContent.none, sPanel);
                if (Event.current.type == EventType.Repaint) Fill(new Rect(r.x + 6, r.y + 8, 4, r.height - 16), SimsBlue);
                GUI.Label(new Rect(r.x + 18, r.y + 9, 340, h - 14), c, sWrap);
                GUI.color = old;
                y += h + 6;
            }
        }
    }
}
