using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Стиль одной детали: узор + два цвета + масштаб + блеск.</summary>
    [System.Serializable]
    public class StyleData
    {
        public int pattern;
        public Color c1 = Color.white;
        public Color c2 = new Color(0.5f, 0.5f, 0.5f);
        public float scale = 1f;
        public float gloss = 0.15f;

        public StyleData Clone() => (StyleData)MemberwiseClone();

        public static Color Hex(string hex)
        {
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        public static StyleData Make(string pattern, string hex1, string hex2 = null, float scale = 1f, float gloss = 0.15f)
        {
            return new StyleData
            {
                pattern = Patterns.IndexOf(pattern),
                c1 = Hex(hex1),
                c2 = hex2 != null ? Hex(hex2) : Hex(hex1) * 0.7f,
                scale = scale,
                gloss = gloss,
            };
        }
    }

    public enum SurfaceKind { Object, Floor, Wall, Fox, Outdoor }

    public class StylePart
    {
        public string name;
        public StyleData style;
        public Material mat;
        public List<Renderer> renderers = new List<Renderer>();
    }

    /// <summary>Всё, что можно перекрасить: мебель, стены, полы, деревья и сами лисички.</summary>
    public class Styleable : MonoBehaviour
    {
        public string displayName;
        public SurfaceKind kind = SurfaceKind.Object;
        public bool hidden;   // служебная поверхность: не подсвечивается и не красится
        public float depthLayer; // добавка к слою глубины (одежда: чем «внешнее», тем выше)
        [System.NonSerialized] public int primCount;
        public readonly List<StylePart> parts = new List<StylePart>();

        public int AddPart(string name, StyleData s, bool glass = false)
        {
            var p = new StylePart { name = name, style = s.Clone(), mat = glass ? MaterialFactory.CreateGlass() : MaterialFactory.Create() };
            p.mat.name = displayName + " / " + name;
            parts.Add(p);
            // слой глубины против z-fighting: стены и земля — 0, предметы — 1, детали одного предмета — чуть выше друг друга
            float layer = (kind == SurfaceKind.Object ? 1f : kind == SurfaceKind.Fox ? 0.6f : 0f) + (parts.Count - 1) * 0.12f + depthLayer;
            if (layer > 0) p.mat.SetFloat("_DepthBias", layer);
            MaterialFactory.Apply(p.mat, p.style);
            return parts.Count - 1;
        }

        public void Register(Renderer r, int part)
        {
            r.sharedMaterial = parts[part].mat;
            parts[part].renderers.Add(r);
            var sr = r.gameObject.GetComponent<StyleRef>();
            if (sr == null) sr = r.gameObject.AddComponent<StyleRef>();
            sr.owner = this;
            sr.part = part;
        }

        public StyleData Get(int part) => parts[part].style;

        public void ClearRenderers() { foreach (var p in parts) p.renderers.Clear(); }

        public void SetStyle(int part, StyleData s)
        {
            if (part < 0 || part >= parts.Count) return;
            parts[part].style = s.Clone();
            MaterialFactory.Apply(parts[part].mat, parts[part].style);
            if (notify && kind != SurfaceKind.Fox && Game.I != null) Game.I.NotifyStyle();
            Net.StyleChanged(this);
        }

        /// <summary>false во время загрузки, чтобы перекраска из сохранения не считалась действием игрока.</summary>
        public static bool notify = true;

        /// <summary>Вставить стиль целого предмета: детали сопоставляются по имени, затем по номеру.</summary>
        public void PasteAll(Styleable from)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                StyleData s = null;
                foreach (var p in from.parts) if (p.name == parts[i].name) { s = p.style; break; }
                if (s == null) s = from.parts[i % from.parts.Count].style;
                SetStyle(i, s);
            }
        }

        public void SetHighlight(int part, Color c)
        {
            for (int i = 0; i < parts.Count; i++)
                MaterialFactory.SetHighlight(parts[i].mat, (part < 0 || part == i) ? c : Color.black);
        }

        public void ClearHighlight()
        {
            foreach (var p in parts) MaterialFactory.SetHighlight(p.mat, Color.black);
        }

        void OnDestroy()
        {
            foreach (var p in parts) if (p.mat != null) Destroy(p.mat);
        }
    }

    /// <summary>Висит на каждом коллайдере, чтобы по клику найти деталь.</summary>
    public class StyleRef : MonoBehaviour
    {
        public Styleable owner;
        public int part;
    }

    public static class MaterialFactory
    {
        static Shader shader;
        static bool custom;

        static void Init()
        {
            if (shader != null) return;
            shader = Shader.Find("FoxLife/Styled");
            custom = shader != null && shader.isSupported;
            if (!custom)
            {
                shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            }
        }

        public static Material Create()
        {
            Init();
            return new Material(shader);
        }

        static Shader glassShader;

        /// <summary>Полупрозрачный материал для стёкол.</summary>
        public static Material CreateGlass()
        {
            Init();
            if (glassShader == null) glassShader = Shader.Find("FoxLife/StyledGlass");
            return glassShader != null && custom ? new Material(glassShader) : new Material(shader);
        }

        public static void Apply(Material m, StyleData s)
        {
            var p = Patterns.Get(s.pattern);
            if (custom)
            {
                m.SetTexture("_MainTex", p.tex);
                m.SetColor("_Color", s.c1);
                m.SetColor("_Color2", s.c2);
                m.SetFloat("_Scale", p.tilesPerMeter * s.scale);
                m.SetFloat("_Glossiness", s.gloss);
            }
            else
            {
                m.color = s.c1;
                m.mainTexture = p.preview;
            }
        }

        public static void SetHighlight(Material m, Color c)
        {
            if (custom) m.SetColor("_Highlight", c);
            else
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c);
            }
        }
    }
}
