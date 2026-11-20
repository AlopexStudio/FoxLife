using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public enum LotPreposition { Automatic, In, On }

    /// <summary>
    /// Лисья Долина — сказочный городок в долине между холмами. На севере горы с обсерваторией, на западе лес
    /// с поляной для пикников, по долине течёт ручей. Извилистые улочки идут по рельефу и уходят за край карты.
    /// У каждого участка своя высота; дома стоят на земле, а не на одной плоскости.
    /// </summary>
    public static class Town
    {
        public const int MinX = -96, MinZ = -84, W = 232, H = 160;

        public struct LotDef
        {
            public string name;
            public string[] nameOverrides;
            public LotPreposition preposition;
            public RectInt rect;
            public LotKind kind;
            public bool frontSouth;
            public float pathX;   // где дорожка от двери к улице
            public int template;  // вид дома: 0 — домик с крылечком, 1 — двухэтажный, 2 — с башенкой
            public bool removed;  // участок снесён в режиме строительства города
            public bool customMoney; // деньги новой семьи заданы вручную…
            public int startMoney;   // …вот столько (иначе — по тому, пустой ли участок)
        }

        /// <summary>Участки этого города (можно менять в режиме строительства города; хранятся в сохранении).</summary>
        static LotDef[] lots;
        public static LotDef[] Lots { get { if (lots == null) lots = (LotDef[])DefaultLots.Clone(); return lots; } }

        public static void ResetLots() => SetLots((LotDef[])DefaultLots.Clone());

        public static void SetLots(LotDef[] l)
        {
            lots = l;
            lotElev = null; flat = null; lotPaths = null;
            foreach (var s in Streets) s.prof = null;
        }

        /// <summary>Участок есть и он нужного вида (для достопримечательностей, привязанных к участкам).</summary>
        public static bool Alive(int i, LotKind k) => i >= 0 && i < Lots.Length && !Lots[i].removed && Lots[i].kind == k;

        /// <summary>Снесённый участок: прямоугольник нулевого размера далеко в углу карты.</summary>
        public static RectInt Nowhere => new RectInt(MinX - 500, MinZ - 500, 0, 0);

        static LotDef L(string n, int x, int z, int w, int h, LotKind k, bool south, float px, int tpl = 0) =>
            new LotDef { name = n, rect = new RectInt(x, z, w, h), kind = k, frontSouth = south, pathX = px, template = tpl };

        static readonly LotDef[] DefaultLots =
        {
            L("Лисий дом", 0, 0, 20, 16, LotKind.Home, true, 4.5f),
            L("Дом у ручья", -26, 2, 18, 14, LotKind.Home, true, -18.5f, 0),
            L("Дом с башенкой", 34, 4, 18, 14, LotKind.Home, true, 39.5f, 2),
            L("Дом на пригорке", 58, 2, 18, 14, LotKind.Home, true, 65.5f, 1),
            L("Парк «Лисья поляна»", -44, -42, 26, 28, LotKind.Park, false, -24.5f),
            L("Кафе «Тёплая норка»", -2, -36, 16, 13, LotKind.Cafe, false, 6.5f),
            L("Библиотека", 26, -34, 18, 15, LotKind.Library, false, 35.5f),
            L("Дом у мельничной тропы", 52, -30, 18, 14, LotKind.Home, false, 59.5f, 0),
            L("Площадь Часов", 2, -22, 16, 13, LotKind.Square, false, 10.5f),
            L("Школа «Лисёнок»", 88, -28, 22, 17, LotKind.School, false, 99.5f),
            L("Яблоневый участок", 88, 2, 22, 14, LotKind.Home, true, 95.5f, -1),
            L("Мастерская «Кисточка»", -70, 2, 18, 14, LotKind.Workshop, true, -60.5f),
            L("Обсерватория", 24, 48, 16, 14, LotKind.Observatory, true, 32.5f),
            L("Лесная поляна", -86, -30, 20, 16, LotKind.Camp, false, -76.5f),
            L("Домик лесника", -62, -34, 18, 14, LotKind.Home, false, -54.5f, 1),
            L("Участок у серпантина", 42, 20, 16, 12, LotKind.Home, false, 44.5f, -1),
            L("Восточный участок", 114, 0, 18, 14, LotKind.Home, true, 120.5f, -1),
        };

        static readonly Dictionary<string, string[]> LotForms = new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Лисий дом"] = "Лисьего дома|Лисьему дому|Лисий дом|Лисьим домом|Лисьем доме".Split('|'),
            ["Дом у ручья"] = "дома у ручья|дому у ручья|дом у ручья|домом у ручья|доме у ручья".Split('|'),
            ["Дом с башенкой"] = "дома с башенкой|дому с башенкой|дом с башенкой|домом с башенкой|доме с башенкой".Split('|'),
            ["Дом на пригорке"] = "дома на пригорке|дому на пригорке|дом на пригорке|домом на пригорке|доме на пригорке".Split('|'),
            ["Парк «Лисья поляна»"] = "парка «Лисья поляна»|парку «Лисья поляна»|парк «Лисья поляна»|парком «Лисья поляна»|парке «Лисья поляна»".Split('|'),
            ["Кафе «Тёплая норка»"] = "кафе «Тёплая норка»|кафе «Тёплая норка»|кафе «Тёплая норка»|кафе «Тёплая норка»|кафе «Тёплая норка»".Split('|'),
            ["Библиотека"] = "библиотеки|библиотеке|библиотеку|библиотекой|библиотеке".Split('|'),
            ["Дом у мельничной тропы"] = "дома у мельничной тропы|дому у мельничной тропы|дом у мельничной тропы|домом у мельничной тропы|доме у мельничной тропы".Split('|'),
            ["Площадь Часов"] = "площади Часов|площади Часов|площадь Часов|площадью Часов|площади Часов".Split('|'),
            ["Школа «Лисёнок»"] = "школы «Лисёнок»|школе «Лисёнок»|школу «Лисёнок»|школой «Лисёнок»|школе «Лисёнок»".Split('|'),
            ["Яблоневый участок"] = "Яблоневого участка|Яблоневому участку|Яблоневый участок|Яблоневым участком|Яблоневом участке".Split('|'),
            ["Мастерская «Кисточка»"] = "мастерской «Кисточка»|мастерской «Кисточка»|мастерскую «Кисточка»|мастерской «Кисточка»|мастерской «Кисточка»".Split('|'),
            ["Обсерватория"] = "обсерватории|обсерватории|обсерваторию|обсерваторией|обсерватории".Split('|'),
            ["Лесная поляна"] = "Лесной поляны|Лесной поляне|Лесную поляну|Лесной поляной|Лесной поляне".Split('|'),
            ["Домик лесника"] = "домика лесника|домику лесника|домик лесника|домиком лесника|домике лесника".Split('|'),
            ["Участок у серпантина"] = "участка у серпантина|участку у серпантина|участок у серпантина|участком у серпантина|участке у серпантина".Split('|'),
            ["Восточный участок"] = "Восточного участка|Восточному участку|Восточный участок|Восточным участком|Восточном участке".Split('|'),
        };

        static readonly string[] SiteForms = { "участка", "участку", "участок", "участком", "участке" };

        static string LotForm(string name, string[] overrides, NameCase nameCase)
        {
            return RussianCases.Override(overrides, nameCase) ??
                (name != null && LotForms.TryGetValue(name, out var forms) ? RussianCases.Override(forms, nameCase) : null);
        }

        public static string LotNameIn(int id, string name, NameCase nameCase)
        {
            if (nameCase == NameCase.Nominative) return name;
            var overrides = id >= 0 && id < Lots.Length ? Lots[id].nameOverrides : null;
            return LotForm(name, overrides, nameCase) ?? $"{RussianCases.Override(SiteForms, nameCase)} «{name}»";
        }

        public static string LotLocation(int id, string name, LotKind kind, bool destination)
        {
            var def = id >= 0 && id < Lots.Length ? Lots[id] : default;
            var nameCase = destination ? NameCase.Accusative : NameCase.Prepositional;
            string form = LotForm(name, def.nameOverrides, nameCase);
            if (form == null) return $"на {RussianCases.Override(SiteForms, nameCase)} «{name}»";
            bool on = def.preposition == LotPreposition.On || def.preposition == LotPreposition.Automatic &&
                (kind == LotKind.Square || kind == LotKind.Market || kind == LotKind.Camp ||
                 (name != null && name.IndexOf("участок", System.StringComparison.OrdinalIgnoreCase) >= 0));
            return (on ? "на " : "в ") + form;
        }

        // ================= улицы, ручей, пруд =================

        public class Line
        {
            public Vector2[] p;
            public float half;
            public bool footpath;
            public Rect box;
            public float[] cum;      // длина от начала до точки i
            public float[] prof;     // высота дороги через каждый метр
            public Line(float half, bool foot, params Vector2[] pts)
            {
                p = pts; this.half = half; footpath = foot;
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                foreach (var q in pts) { x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.y); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.y); }
                box = Rect.MinMaxRect(x0 - half - 4, z0 - half - 4, x1 + half + 4, z1 + half + 4);
                cum = new float[pts.Length];
                for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + (pts[i] - pts[i - 1]).magnitude;
            }

            public float Length => cum[cum.Length - 1];

            /// <summary>Расстояние до осевой линии; t — где на линии (метры от начала), dir — направление.</summary>
            public float Nearest(float x, float z, out float t, out Vector2 dir)
            {
                float best = float.MaxValue; dir = Vector2.right; t = 0;
                var q = new Vector2(x, z);
                for (int i = 0; i + 1 < p.Length; i++)
                {
                    var a = p[i]; var b = p[i + 1];
                    var ab = b - a;
                    float k = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                    float d = (a + ab * k - q).magnitude;
                    if (d < best) { best = d; dir = ab.normalized; t = cum[i] + k * ab.magnitude; }
                }
                return best;
            }

            public float Dist(float x, float z, out Vector2 dir) => Nearest(x, z, out _, out dir);
            public float Dist(float x, float z) => box.Contains(new Vector2(x, z)) ? Nearest(x, z, out _, out _) : float.MaxValue;

            public Vector2 PointAt(float t)
            {
                for (int i = 0; i + 1 < p.Length; i++)
                    if (t <= cum[i + 1] || i + 2 == p.Length)
                        return Vector2.Lerp(p[i], p[i + 1], Mathf.Clamp01((t - cum[i]) / Mathf.Max(0.001f, cum[i + 1] - cum[i])));
                return p[p.Length - 1];
            }

            public float HeightAt(float t)
            {
                if (prof == null) return 0;
                float f = Mathf.Clamp(t, 0, prof.Length - 1.001f);
                int i = (int)f;
                return Mathf.Lerp(prof[i], prof[i + 1], f - i);
            }
        }

        static Vector2 P(float x, float z) => new Vector2(x, z);

        public static readonly Line[] Streets =
        {
            // главная улица: из леса через весь городок и за холмы на восток
            new Line(1.5f, false, P(-97, -12), P(-84, -10), P(-72, -6), P(-60, -2.5f), P(-48, -4), P(-40, -5), P(-33, -5.5f), P(-24, -4.5f), P(-12, -5.5f), P(0, -6),
                P(10, -6.5f), P(22, -6), P(34, -4.5f), P(48, -3.5f), P(62, -5), P(74, -6.5f), P(88, -6), P(100, -5.5f), P(118, -7), P(137, -8)),
            // серпантин на север, в горы к обсерватории
            new Line(1.4f, false, P(22, -6), P(24.5f, 2), P(26, 10), P(27, 18), P(29.5f, 26), P(36, 33), P(52, 36.5f), P(61, 40), P(52, 44), P(38, 45), P(30, 45.5f), P(32.5f, 48)),
            // на юг, мимо кафе и библиотеки, и дальше за край долины
            new Line(1.4f, false, P(18, -18), P(21, -24), P(22, -32), P(20, -40), P(17, -50), P(14, -62), P(12, -85)),
            // тропинки парка
            new Line(0.8f, true, P(-24.5f, -14), P(-24, -18), P(-22.5f, -20.5f)),
            new Line(0.8f, true, P(-24, -18), P(-28, -19), P(-31.5f, -19.5f), P(-36, -20), P(-40, -22)),
            new Line(0.8f, true, P(-22.5f, -25), P(-24, -29), P(-25.5f, -34)),
            // лесная тропа к роднику
            new Line(0.9f, true, P(-72, -6), P(-78, 6), P(-74, 20), P(-82, 34), P(-78, 50), P(-86, 64)),
            // переулок от главной улицы на площадь Часов
            new Line(1.5f, false, P(8.5f, -6.2f), P(9.5f, -8.4f), P(10.5f, -10.5f)),
            // тропа к мельнице
            new Line(0.9f, true, P(27, 18), P(18, 26), P(10, 33), P(8, 36)),
        };

        public static readonly Line Creek = new Line(1.25f, false, P(-94, 66), P(-80, 52), P(-66, 38), P(-54, 26), P(-43, 16), P(-37, 8), P(-34, 0), P(-33, -6), P(-32, -14), P(-31, -21), P(-30, -27));
        public static readonly Vector2 PondCenter = new Vector2(-30f, -30f);
        public static float PondDist(float x, float z) => new Vector2(x - PondCenter.x, (z - PondCenter.y) * 1.2f).magnitude;
        public const float PondR = 4f;

        public static readonly Vector2 SquareCenter = new Vector2(10f, -16f);
        public const float PlazaR = 6.5f;
        public const float SnowLine = 10.5f;

        // холмы вокруг долины (x, z, высота, радиус)
        static readonly Vector4[] Hills =
        {
            new Vector4(6, 38, 3.2f, 8), new Vector4(-52, 14, 2.2f, 9), new Vector4(86, 22, 3f, 12), new Vector4(72, -44, 2.4f, 10),
            new Vector4(-55, -50, 2.8f, 12), new Vector4(60, 30, 2f, 8), new Vector4(-20, 32, 1.8f, 7), new Vector4(120, -40, 3.5f, 14),
            new Vector4(-12, -66, 2.4f, 12), new Vector4(50, -66, 3f, 14),
        };
        public static readonly Vector2 MillPos = new Vector2(6f, 38f);
        /// <summary>Древнее дерево между площадью, кафе и парком.</summary>
        public static readonly Vector2 AncientPos = new Vector2(-8f, -16f);

        public static bool InForest(float x, float z) => ForestAmount(x, z) > 0.5f;

        /// <summary>Насколько здесь лес (0 — поле, 1 — чаща): на опушке плавный переход.</summary>
        public static float ForestAmount(float x, float z)
        {
            float a = Mathf.InverseLerp(-44f, -56f, x + (Mathf.PerlinNoise(z * 0.05f, 3f) - 0.5f) * 12f + (Mathf.PerlinNoise(x * 0.15f, z * 0.15f) - 0.5f) * 5f);
            float b = Mathf.InverseLerp(-44f, -56f, z + (Mathf.PerlinNoise(x * 0.06f, 7f) - 0.5f) * 10f) * Mathf.InverseLerp(-16f, -26f, x);
            return Mathf.Clamp01(Mathf.Max(a, b));
        }
        public static float MountainMask(float x, float z) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(36f, 58f, z + (Mathf.PerlinNoise(x * 0.02f + 7f, 1f) - 0.5f) * 14f));

        public static bool OnRoad(float x, float z)
        {
            foreach (var s in Streets) if (!s.footpath && s.Dist(x, z) < s.half) return true;
            return false;
        }

        /// <summary>0 — нет; 1 — улица; 2 — край улицы (бордюр); 3 — тропинка.</summary>
        public static int StreetKind(float x, float z)
        {
            int k = 0;
            foreach (var s in Streets)
            {
                float d = s.Dist(x, z);
                if (d >= s.half) continue;
                if (s.footpath) { if (k == 0) k = 3; continue; }
                k = d > s.half - 0.3f && k != 1 ? 2 : 1;
                if (k == 1) return 1;
            }
            return k;
        }

        public static bool OnFootpath(float x, float z)
        {
            foreach (var s in Streets) if (s.footpath && s.Dist(x, z) < s.half) return true;
            return false;
        }

        public static float CreekDist(float x, float z) => Creek.Dist(x, z);
        public static bool IsCreek(float x, float z) => CreekDist(x, z) < Creek.half || PondDist(x, z) < PondR;

        public static int LotIdAt(float x, float z)
        {
            for (int i = 0; i < Lots.Length; i++)
            {
                var r = Lots[i].rect;
                if (x >= r.xMin && x < r.xMax && z >= r.yMin && z < r.yMax) return i;
            }
            return -1;
        }

        public const float OpenHour = 8f, CloseHour = 21f;
        public static bool IsOpen(float hour) => hour >= OpenHour && hour < CloseHour;

        // ---------- дорожки от дверей к улице ----------

        public struct LotPath { public float x, z0, z1, h0, h1; }
        static List<LotPath> lotPaths;

        static List<LotPath> LotPaths()
        {
            if (lotPaths != null) return lotPaths;
            lotPaths = new List<LotPath>();
            for (int li = 0; li < Lots.Length; li++)
            {
                var l = Lots[li];
                if (l.kind == LotKind.Square || l.removed) continue;
                if (l.kind == LotKind.Home && l.template < 0) continue; // к пустому участку дорожку не ведём
                float dir = l.frontSouth ? -1f : 1f;
                float z = l.frontSouth ? l.rect.yMin : l.rect.yMax;
                float start = z, endH = LotElev(li);
                bool reached = false;
                for (int i = 0; i < 40; i++)
                {
                    float zz = z + dir * (i * 0.5f + 0.25f);
                    if (Mathf.Abs(zz - start) > 16f) break;
                    int other = LotIdAt(l.pathX, zz);
                    if (other >= 0 && Lots[other].kind == LotKind.Square) { z = zz; endH = LotElev(other); reached = true; break; }
                    float rh = RoadHeight(l.pathX, zz, out float rd);
                    if (rd < 1.2f) { z = zz; endH = rh; reached = true; break; }
                    z = zz;
                }
                if (!reached) continue; // дороги рядом нет — дорожку не тянем через полдолины
                bool down = z < start;
                lotPaths.Add(new LotPath { x = l.pathX, z0 = Mathf.Min(start, z), z1 = Mathf.Max(start, z), h0 = down ? endH : LotElev(li), h1 = down ? LotElev(li) : endH });
            }
            return lotPaths;
        }

        public static bool OnLotPath(float x, float z)
        {
            foreach (var p in LotPaths()) if (Mathf.Abs(x - p.x) < 0.75f && z >= p.z0 && z <= p.z1) return true;
            return false;
        }

        // ---------- мосты ----------

        public struct BridgeDef { public Vector2 c; public float yaw, length, width, y; public bool wooden; }
        static List<BridgeDef> bridges;

        public static List<BridgeDef> Bridges()
        {
            if (bridges != null) return bridges;
            bridges = new List<BridgeDef>();
            foreach (var s in Streets)
            {
                float best = float.MaxValue; Vector2 bc = default, bd = default;
                for (int i = 0; i + 1 < s.p.Length; i++)
                    for (float k = 0; k <= 1f; k += 0.01f)
                    {
                        var q = Vector2.Lerp(s.p[i], s.p[i + 1], k);
                        float d = CreekDist(q.x, q.y);
                        if (d < best) { best = d; bc = q; bd = (s.p[i + 1] - s.p[i]).normalized; }
                    }
                if (best > 0.8f) continue;
                bridges.Add(new BridgeDef { c = bc, yaw = Mathf.Atan2(bd.x, bd.y) * Mathf.Rad2Deg, length = s.footpath ? 7.5f : 8.5f, width = s.footpath ? 1.8f : 3.4f, wooden = s.footpath, y = 0.05f });
            }
            return bridges;
        }

        /// <summary>Высота настила моста в точке (или NaN, если не на мосту). Считаются только мосты, которые реально стоят.</summary>
        public static float BridgeDeck(float x, float z)
        {
            foreach (var b in BridgeMark.All)
                if (b.Live && b.Covers(x, z, 0f)) return b.transform.position.y + 0.11f;
            return float.NaN;
        }

        public static bool OnBridge(float x, float z)
        {
            foreach (var b in BridgeMark.All)
                if (b.Live && b.Covers(x, z, 0.2f)) return true;
            return false;
        }

        // ================= рельеф =================

        /// <summary>Природная форма земли, без дорог и участков: долина, пригорки, холмы, горы, ложбина ручья.</summary>
        public static float Natural(float x, float z)
        {
            float core = Mathf.Max(Mathf.Max(-50f - x, 0, x - 116f), Mathf.Max(-46f - z, 0, z - 30f));
            float c = 1f - Mathf.SmoothStep(0, 1, core / 14f);
            float gentle = Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f + 9f) * 1.7f - 0.45f;
            float n = Mathf.PerlinNoise(x * 0.045f + 3.1f, z * 0.045f + 7.7f) * 2.6f + Mathf.PerlinNoise(x * 0.15f + 1.3f, z * 0.15f + 5.2f) * 0.5f - 0.5f;
            foreach (var hl in Hills)
            {
                float d = new Vector2(x - hl.x, z - hl.y).magnitude / hl.w;
                if (d < 1.6f) n += hl.z * Mathf.Exp(-d * d * 1.6f);
            }
            float h = Mathf.Lerp(Mathf.Max(0.1f, n), gentle, c);
            // горы на севере
            float m = MountainMask(x, z);
            if (m > 0)
            {
                float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.035f + 2f, z * 0.035f + 4f) * 2f - 1f);
                h += m * (4.5f + ridge * 9f + Mathf.PerlinNoise(x * 0.09f, z * 0.09f) * 2.2f);
            }
            // ложбина ручья
            float cd = CreekDist(x, z);
            if (cd < 16f) h = Mathf.Lerp(-0.05f, h, Mathf.SmoothStep(0, 1, (cd - 2f) / 14f));
            float dp = PondDist(x, z);
            if (dp < 14f) h = Mathf.Lerp(-0.05f, h, Mathf.SmoothStep(0, 1, (dp - 4f) / 10f));
            return h;
        }

        static float[] lotElev;

        /// <summary>Высота участка: средняя природная высота, кратная четверти метра.</summary>
        public static float LotElev(int i)
        {
            if (lotElev == null)
            {
                lotElev = new float[Lots.Length];
                for (int k = 0; k < Lots.Length; k++)
                {
                    var r = Lots[k].rect;
                    if (Lots[k].removed || r.width <= 0) { lotElev[k] = 0; continue; }
                    float sum = 0; int n = 0;
                    for (int x = r.xMin; x <= r.xMax; x += 2) for (int z = r.yMin; z <= r.yMax; z += 2) { sum += Natural(x, z); n++; }
                    float e = sum / n;
                    if (k == 0) e = 0;                       // лисий дом — на прежней высоте
                    lotElev[k] = Mathf.Round(Mathf.Max(0f, e) * 4f) / 4f;
                }
            }
            return lotElev[i];
        }

        /// <summary>Профили дорог: природная высота вдоль линии, сглаженная, без крутых подъёмов.</summary>
        static void BuildProfiles()
        {
            foreach (var s in Streets)
            {
                if (s.prof != null) continue;
                int n = Mathf.CeilToInt(s.Length) + 1;
                var raw = new float[n];
                for (int i = 0; i < n; i++) { var q = s.PointAt(i); raw[i] = Mathf.Max(0f, Natural(q.x, q.y)); }
                // у мостов дорога у воды, у участков — на высоте участка
                for (int i = 0; i < n; i++)
                {
                    var q = s.PointAt(i);
                    if (CreekDist(q.x, q.y) < 4f) { raw[i] = 0.05f; continue; }
                    for (int li = 0; li < Lots.Length; li++)
                    {
                        if (Lots[li].removed) continue;
                        float d = RectDist(Lots[li].rect, q.x, q.y, 0f);
                        if (d < 8f) raw[i] = Mathf.Lerp(LotElev(li), raw[i], d / 8f);
                    }
                }
                var sm = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float sum = 0, w = 0;
                    for (int k = -8; k <= 8; k++) { int j = Mathf.Clamp(i + k, 0, n - 1); float ww = 1f / (1 + Mathf.Abs(k)); sum += raw[j] * ww; w += ww; }
                    sm[i] = sum / w;
                }
                const float maxSlope = 0.3f;
                for (int i = 1; i < n; i++) sm[i] = Mathf.Clamp(sm[i], sm[i - 1] - maxSlope, sm[i - 1] + maxSlope);
                for (int i = n - 2; i >= 0; i--) sm[i] = Mathf.Clamp(sm[i], sm[i + 1] - maxSlope, sm[i + 1] + maxSlope);
                s.prof = sm;
            }
        }

        /// <summary>Высота ближайшей улицы или тропы и расстояние до её края.</summary>
        public static float RoadHeight(float x, float z, out float edgeDist)
        {
            BuildProfiles();
            edgeDist = float.MaxValue; float h = 0;
            foreach (var s in Streets)
            {
                if (!s.box.Contains(new Vector2(x, z))) continue;
                float d = s.Nearest(x, z, out float t, out _) - s.half;
                if (d < edgeDist) { edgeDist = d; h = s.HeightAt(t); }
            }
            return h;
        }

        // расстояние до «застроенного»: участков, улиц, дорожек. Сетка 1 м на всю землю (для деревьев и кустов).
        const int FX0 = MinX, FZ0 = MinZ, FW = W + 1, FH = H + 1;
        static float[,] flat;

        static float RectDist(RectInt r, float x, float z, float pad)
        {
            float dx = Mathf.Max(r.xMin - pad - x, 0, x - (r.xMax + pad));
            float dz = Mathf.Max(r.yMin - pad - z, 0, z - (r.yMax + pad));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static void BuildFlat()
        {
            flat = new float[FW, FH];
            for (int i = 0; i < FW; i++)
                for (int k = 0; k < FH; k++)
                {
                    float x = FX0 + i, z = FZ0 + k;
                    float d = float.MaxValue;
                    foreach (var l in Lots) if (!l.removed) d = Mathf.Min(d, RectDist(l.rect, x, z, 1f));
                    foreach (var s in Streets) d = Mathf.Min(d, s.Dist(x, z) - s.half - 1f);
                    foreach (var p in LotPaths()) d = Mathf.Min(d, RectDist(new RectInt((int)(p.x - 1), (int)p.z0, 2, (int)(p.z1 - p.z0) + 1), x, z, 0.5f));
                    flat[i, k] = Mathf.Max(0, d);
                }
        }

        public static float FlatDist(float x, float z)
        {
            if (flat == null) BuildFlat();
            float fx = Mathf.Clamp(x - FX0, 0, FW - 1.001f), fz = Mathf.Clamp(z - FZ0, 0, FH - 1.001f);
            int i = (int)fx, k = (int)fz;
            float u = fx - i, v = fz - k;
            return Mathf.Lerp(Mathf.Lerp(flat[i, k], flat[i + 1, k], u), Mathf.Lerp(flat[i, k + 1], flat[i + 1, k + 1], u), v);
        }

        /// <summary>Высота земли: природа, к которой прижаты улицы (по своему профилю) и участки (по своей высоте).</summary>
        public static float Height(float x, float z)
        {
            float h = Natural(x, z);
            // участки ровные, на своей высоте
            for (int i = 0; i < Lots.Length; i++)
            {
                if (Lots[i].removed) continue;
                float d = RectDist(Lots[i].rect, x, z, 0.25f);
                float lb = 3.5f + Mathf.Min(12f, Mathf.Abs(h - LotElev(i)) * 1.8f);
                d += (Mathf.PerlinNoise(x * 0.25f + i, z * 0.25f) - 0.5f) * 1.2f * Mathf.Clamp01(d);
                if (d < lb) h = Mathf.Lerp(LotElev(i), h, Mathf.SmoothStep(0, 1, Mathf.Max(0, d) / lb));
            }
            // улицы и тропы — поверх: дорога не кренится вбок
            float rh = RoadHeight(x, z, out float rd);
            float rb = 3f + Mathf.Min(10f, Mathf.Abs(h - rh) * 1.6f);
            if (rd < rb) h = Mathf.Lerp(rh, h, Mathf.SmoothStep(0, 1, Mathf.Max(0, rd) / rb));
            // дорожки от дверей
            foreach (var p in LotPaths())
            {
                float dx = Mathf.Abs(x - p.x) - 0.75f;
                if (dx > 2.5f || z < p.z0 - 0.5f || z > p.z1 + 0.5f) continue;
                float ph = Mathf.Lerp(p.h0, p.h1, Mathf.InverseLerp(p.z0, p.z1, z));
                h = Mathf.Lerp(ph, h, Mathf.SmoothStep(0, 1, Mathf.Max(0, dx) / 2.5f));
            }
            // ручей с пологими берегами и пруд
            float cd = CreekDist(x, z);
            if (cd < 3.2f) h = Mathf.Min(h, Mathf.Lerp(-0.75f, h, Mathf.SmoothStep(0, 1, (cd - Creek.half) / 2f)));
            float dp = PondDist(x, z);
            if (dp < PondR + 2f) h = Mathf.Min(h, Mathf.Lerp(-0.75f, h, Mathf.SmoothStep(0, 1, (dp - PondR + 0.5f) / 2.5f)));
            return h;
        }

        // ================= деревья =================

        public static void PlantTownTrees(House h) { var e = PlantTownTreesAsync(h); while (e.MoveNext()) { } }

        public static System.Collections.IEnumerator PlantTownTreesAsync(House h)
        {
            var rnd = new System.Random(77);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            // двор игрока
            h.PlantTree(new Vector3(16.5f, 0, 2.5f), 1.1f, false);
            h.PlantTree(new Vector3(17f, 0, 12.5f), 0.9f, true);
            h.PlantTree(new Vector3(2.5f, 0, 13.5f), 1f, false);
            bool Free(float x, float z, float fdMin)
            {
                if (FlatDist(x, z) < fdMin || CreekDist(x, z) < 2.5f || PondDist(x, z) < PondR + 1.5f) return false;
                if (new Vector2(x - MillPos.x, z - MillPos.y).magnitude < 6f) return false;
                if (new Vector2(x - AncientPos.x, z - AncientPos.y).magnitude < 13f) return false;
                return true;
            }
            // лес на западе: густо, ёлки вперемешку с лиственными
            int placed = 0;
            for (int i = 0; i < 6000 && placed < 260; i++)
            {
                float x = R(MinX + 1, -40f), z = R(MinZ + 1, MinZ + H - 1);
                if (!InForest(x, z) || !Free(x, z, 1.2f)) continue;
                if (Natural(x, z) > SnowLine - 1f) continue;
                h.PlantTree(new Vector3(x, 0, z), R(0.9f, 1.6f), rnd.NextDouble() < 0.55);
                placed++;
                if (placed % 12 == 0) yield return null;
            }
            // горы: ёлки до линии снега, реже к вершинам
            placed = 0;
            for (int i = 0; i < 5000 && placed < 150; i++)
            {
                float x = R(MinX + 1, MinX + W - 1), z = R(30f, MinZ + H - 1);
                if (MountainMask(x, z) < 0.3f || !Free(x, z, 1.2f)) continue;
                float ht = Natural(x, z);
                if (ht > SnowLine - 0.5f || rnd.NextDouble() < ht / SnowLine * 0.7f) continue;
                h.PlantTree(new Vector3(x, 0, z), R(0.8f, 1.3f), rnd.NextDouble() < 0.85);
                placed++;
                if (placed % 12 == 0) yield return null;
            }
            // рощи на холмах и пригорках долины
            placed = 0;
            for (int i = 0; i < 4000 && placed < 170; i++)
            {
                float x = R(-50f, MinX + W - 1), z = R(MinZ + 1, 40f);
                if (InForest(x, z) || MountainMask(x, z) > 0.3f || !Free(x, z, 1.2f)) continue;
                float fd = FlatDist(x, z);
                float clump = Mathf.PerlinNoise(x * 0.08f + 11f, z * 0.08f + 3f);
                if (rnd.NextDouble() > clump * 1.2f + (fd > 8 ? 0.2f : -0.2f)) continue;
                h.PlantTree(new Vector3(x, 0, z), R(0.8f, 1.4f), fd > 6 && rnd.NextDouble() < 0.4);
                placed++;
                if (placed % 12 == 0) yield return null;
            }
            // парк
            Vector2[] spots = { new Vector2(-42, -16), new Vector2(-41, -30), new Vector2(-40, -39), new Vector2(-20, -39), new Vector2(-34, -40),
                new Vector2(-19, -30), new Vector2(-19, -16), new Vector2(-37, -15), new Vector2(-21, -38), new Vector2(-36, -24) };
            foreach (var s in spots) h.PlantTree(new Vector3(s.x + 0.5f, 0, s.y + 0.5f), R(1f, 1.45f), rnd.NextDouble() < 0.25);
            // поляна в лесу: кольцо деревьев
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f;
                var p = new Vector2(-76f, -22f) + new Vector2(Mathf.Cos(a) * 11.5f, Mathf.Sin(a) * 9.5f);
                if (FlatDist(p.x, p.y) < 0.5f && LotIdAt(p.x, p.y) < 0) continue;
                if (LotIdAt(p.x, p.y) >= 0) continue;
                h.PlantTree(new Vector3(p.x, 0, p.y), R(1.1f, 1.6f), i % 2 == 0);
            }
        }

        // ================= украшения города (часть мира, строятся вместе с землёй) =================

        public static void BuildLandmarks(House h)
        {
            var root = h.propsRoot;
            float G(float x, float z) => h.terrain.GroundAt(x, z);
            Vector3 At(float x, float z) => new Vector3(x, G(x, z), z);
            Vector3 OnLot(int lot, float x, float z) => new Vector3(x, LotElev(lot), z);
            void Block(int x0, int z0, int w, int d) { for (int x = x0; x < x0 + w; x++) for (int z = z0; z < z0 + d; z++) h.blocked.Add(new Vector2Int(x, z)); }

            // мосты
            foreach (var b in Bridges())
                Props.Bridge(root, new Vector3(b.c.x, b.y, b.c.y), b.yaw, b.length, b.width, b.wooden);

            // площадь Часов
            if (Alive(8, LotKind.Square)) {
            Props.ClockTower(root, OnLot(8, 16.5f, -10.5f), 180f);
            Block(15, -12, 3, 3);
            Props.Well(root, OnLot(8, 16.5f, -19.5f), 30f);
            Block(16, -20, 1, 1);

            var poles = new[] { OnLot(8, 2.3f, -9.3f), OnLot(8, 17.7f, -22.7f), OnLot(8, 2.3f, -22.7f), OnLot(8, 13.6f, -9.3f) };
            foreach (var p in poles) Props.Pole(root, p, 3.4f);
            var top = Vector3.up * 3.4f;
            var tower = OnLot(8, 16.5f, -10.5f) + Vector3.up * 6.2f;
            Props.Bunting(root, poles[0] + top, poles[3] + top);
            Props.Bunting(root, poles[0] + top, poles[2] + top);
            Props.Bunting(root, poles[2] + top, poles[1] + top);
            Props.Bunting(root, poles[3] + top, tower);
            Props.Bunting(root, poles[1] + top, tower);
            foreach (var p in new[] { OnLot(8, 2.5f, -21f), OnLot(8, 17.5f, -14f) }) Props.StreetLamp(root, p, 90f);
            foreach (var tx in new[] { 3, 13 }) Props.Parasol(root, OnLot(8, tx + 1f, -20.5f), tx == 3 ? "#E57373" : "#64B5F6");
            }

            // древнее дерево
            Props.AncientTree(root, At(AncientPos.x, AncientPos.y) - Vector3.up * 0.2f);
            for (int x = -3; x <= 3; x++) for (int z = -3; z <= 3; z++) if (x * x + z * z <= 10) h.blocked.Add(new Vector2Int((int)AncientPos.x + x, (int)AncientPos.y + z));
            // мельница на холме
            {
                var toRoad = new Vector2(10f, 33f) - MillPos;
                float millYaw = Mathf.Atan2(-toRoad.x, -toRoad.y) * Mathf.Rad2Deg;   // крылья (локальная -Z) — к тропе
                Props.Windmill(root, new Vector3(MillPos.x, G(MillPos.x, MillPos.y) - 0.3f, MillPos.y), millYaw);
            }
            // статуи
            if (Alive(4, LotKind.Park)) { Props.FoxStatue(root, OnLot(4, -20.5f, -17.5f), 200f); Block(-21, -18, 1, 1); }
            if (Alive(6, LotKind.Library)) { Props.FoxStatue(root, OnLot(6, 31.5f, -19.5f), 180f, true); Block(31, -20, 1, 1); }
            // пруд
            Props.LilyPads(root, new Vector3(PondCenter.x, -0.25f, PondCenter.y), 5);

            // фонари вдоль улиц и тропинок (только в долине — в лесу и горах их нет)
            int li = 0;
            foreach (var s in Streets)
            {
                float step = s.footpath ? 7f : 9f;
                for (float t = step * 0.5f; t < s.Length; t += step)
                {
                    li++;
                    var q0 = s.PointAt(t);
                    var dir = (s.PointAt(Mathf.Min(s.Length, t + 0.5f)) - s.PointAt(Mathf.Max(0, t - 0.5f))).normalized;
                    float sd = (li & 1) == 0 ? 1 : -1;
                    var side = new Vector2(dir.y, -dir.x) * sd;
                    var q = q0 + side * (s.half + 0.55f);
                    if (InForest(q.x, q.y) || MountainMask(q.x, q.y) > 0.4f) continue;
                    int lot = LotIdAt(q.x, q.y);
                    if (OnRoad(q.x, q.y) || OnBridge(q.x, q.y) || IsCreek(q.x, q.y) || OnLotPath(q.x, q.y)) continue;
                    if (lot >= 0 && Lots[lot].kind == LotKind.Home) continue;
                    if (q.x < MinX + 1 || q.x > MinX + W - 1 || q.y < MinZ + 1 || q.y > MinZ + H - 1) continue;
                    bool near = false;
                    foreach (var b2 in Bridges()) if ((b2.c - q).magnitude < 5f) near = true;
                    if (near) continue;
                    float yaw = Mathf.Atan2(-side.x, -side.y) * Mathf.Rad2Deg;
                    Props.StreetLamp(root, At(q.x, q.y), yaw);
                    h.blocked.Add(new Vector2Int(Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y)));
                }
            }

            // у домов: живая изгородь вдоль улицы, арка над дорожкой, почтовый ящик
            for (int i = 1; i < Lots.Length; i++)
            {
                var l = Lots[i];
                if (l.kind != LotKind.Home || l.removed || l.template < 0) continue; // у пустых участков — ничего лишнего
                float e = LotElev(i);
                int row = l.frontSouth ? l.rect.yMin : l.rect.yMax - 1;
                float zc = row + 0.5f;
                int gapA = Mathf.FloorToInt(l.pathX - 1.5f), gapB = Mathf.FloorToInt(l.pathX + 1.5f);
                if (gapA - l.rect.xMin > 1)
                {
                    float x0 = l.rect.xMin + 0.5f, x1 = gapA;
                    Props.Hedge(root, new Vector3((x0 + x1) / 2f, e, zc), 0, x1 - x0, i * 10);
                    Block(l.rect.xMin, row, gapA - l.rect.xMin, 1);
                }
                if (l.rect.xMax - gapB > 2)
                {
                    float x0 = gapB + 1, x1 = l.rect.xMax - 0.5f;
                    Props.Hedge(root, new Vector3((x0 + x1) / 2f, e, zc), 0, x1 - x0, i * 10 + 5);
                    Block(gapB + 1, row, l.rect.xMax - gapB - 1, 1);
                }
                Props.FlowerArch(root, new Vector3(l.pathX, e, zc), 0);
                Props.Mailbox(root, new Vector3(l.pathX + 1.2f, e, zc), l.frontSouth ? 180f : 0f, i % 2 == 0 ? "#6FA3D9" : "#E57373");
            }
            // вывески и маркизы
            if (Alive(5, LotKind.Cafe))
            {
                Props.Sign(root, OnLot(5, 7.6f, -23.94f) + Vector3.up * 2f, 180f, 0);
                Props.Awning(root, OnLot(5, 2.5f, -23.93f) + Vector3.up * 1.9f, 180f, 3.2f, "#E57373");
                Props.Awning(root, OnLot(5, 10.5f, -23.93f) + Vector3.up * 1.9f, 180f, 3.2f, "#E57373");
            }
            if (Alive(6, LotKind.Library)) Props.Sign(root, OnLot(6, 36.6f, -19.94f) + Vector3.up * 2f, 180f, 1);

            // обсерватория: купол на крыше
            if (Alive(12, LotKind.Observatory)) h.RoofProp(12, Props.Dome(root, OnLot(12, 32f, 53f) + Vector3.up * 2.35f).gameObject);

            // лес: грибы, поваленные брёвна, валуны, палатки на поляне
            var rnd = new System.Random(31);
            int n = 0;
            for (int i = 0; i < 4000 && n < 90; i++)
            {
                float x = MinX + 1 + (float)rnd.NextDouble() * (W - 2), z = MinZ + 1 + (float)rnd.NextDouble() * (H - 2);
                if (FlatDist(x, z) < 3.2f || CreekDist(x, z) < 2.5f || PondDist(x, z) < PondR + 1.5f) continue;
                if (new Vector2(x - AncientPos.x, z - AncientPos.y).magnitude < 12f) continue;
                bool forest = InForest(x, z);
                float m = MountainMask(x, z);
                if (forest && rnd.NextDouble() < 0.45) Props.Mushrooms(root, At(x, z), i);
                else if (forest && rnd.NextDouble() < 0.3) Props.Log(root, At(x, z), (float)rnd.NextDouble() * 180f);
                else if (m > 0.3f || rnd.NextDouble() < 0.25) Props.Rock(root, At(x, z) - Vector3.up * 0.15f, i, 0.8f + m * 1.6f);
                else Props.Bush(root, At(x, z), i);
                n++;
            }
            // кусты на пригорках долины
            for (int i = 0; i < 1500 && n < 160; i++)
            {
                float x = -50 + (float)rnd.NextDouble() * 180, z = -80 + (float)rnd.NextDouble() * 115;
                if (InForest(x, z) || FlatDist(x, z) < 1.5f || CreekDist(x, z) < 2.2f || PondDist(x, z) < PondR + 1f) continue;
                if (new Vector2(x - AncientPos.x, z - AncientPos.y).magnitude < 10f) continue;
                Props.Bush(root, At(x, z), i);
                n++;
            }
            foreach (var p in new[] { new Vector2(-35, -33), new Vector2(-25, -27), new Vector2(-34, -26), new Vector2(-43, -35), new Vector2(-19, -22) })
                Props.Bush(root, At(p.x, p.y), (int)(p.x * 7 + p.y));
            // палатки на лесной поляне
            if (Alive(13, LotKind.Camp))
            {
                Props.Tent(root, OnLot(13, -82.5f, -26f), 30f, "#E9A93E");
                Block(-84, -28, 3, 3);
                Props.Tent(root, OnLot(13, -70.5f, -27f), -20f, "#7FA36B");
                Block(-72, -29, 3, 3);
            }
            // родник у лесной тропы
            Props.Rock(root, At(-86f, 63f), 3, 2.5f);
            Props.Rock(root, At(-90f, 60f), 4, 1.8f);
        }

        // ================= постройки и жители =================

        class Frame
        {
            public int x0, z0, w, d;
            public bool south;   // вход с юга
            public int Z(int lz) => south ? z0 + lz : z0 + d - 1 - lz;   // клетка ряда lz (0 — у входа)
            public int EZ(int k) => south ? z0 + k : z0 + d - k;          // линия ребра
        }

        static readonly (string p, string a, string b)[] Facades =
        {
            ("Фахверк", "#FBF1DE", "#8A5A3C"), ("Фахверк", "#F6E0D6", "#7A4E3A"), ("Гладкий", "#F6D7C3", null), ("Доски", "#BFD7EA", null),
            ("Фахверк", "#EAF2E3", "#6E5440"), ("Камень", "#E3D9C9", "#C4B8A4"), ("Гладкий", "#F7E7B4", null), ("Доски", "#E8D5F0", null),
        };
        static readonly (string a, string b)[] Roofs =
        {
            ("#D9775F", "#B95F4A"), ("#5F9EA0", "#3F7678"), ("#8E7AB5", "#6E5A95"), ("#7FA36B", "#5F8350"), ("#C98F6B", "#A97050"), ("#5B7FA8", "#43658A"),
        };
        static readonly string[] Shutters = { "#5B8FB9", "#7FA36B", "#C9655A", "#8E7AB5", "#E0B04A", "#4F8A86" };
        static readonly (string p, string a, string b)[] Floors =
        {
            ("Доски", "#D2A679", null), ("Паркет", "#C99A66", "#A9764A"), ("Ёлочка", "#C8955F", "#A97A4A"), ("Плитка", "#E8E1D6", "#D3CABD"), ("Шахматка", "#EDE4D6", "#C9B8A3"),
        };
        static readonly (string p, string a, string b)[] InWalls =
        {
            ("Гладкий", "#F4E6D2", null), ("Полоска", "#F1E4CF", "#E8D6B8"), ("Цветочки", "#DCE7E1", "#C3D6CB"), ("Горошек", "#F6E3E3", "#EBC9C9"),
            ("Клетка", "#E6EEF4", "#CFDDE8"), ("Ромбы", "#F3EAD6", "#E2D3B5"), ("Гладкий", "#E3EED9", null),
        };

        public static readonly string[] NeighborNames =
        {
            "Пряник", "Ромашка", "Вереск", "Черника", "Ольха", "Клюква", "Дымка", "Бублик", "Сосенка", "Ириска",
            "Жёлудь", "Листик", "Кедр", "Мята", "Облепиха", "Шалфей", "Тмин", "Василёк", "Ежевика", "Калинка",
        };

        static StyleData S((string p, string a, string b) t, float scale = 1f) => StyleData.Make(t.p, t.a, t.b, scale);

        /// <summary>Застроить город: соседские дома, кафе, библиотеку, парк, площадь, и поселить соседей.</summary>
        public static void Populate(Game g) { var e = PopulateAsync(g); while (e.MoveNext()) { } }

        public static System.Collections.IEnumerator PopulateAsync(Game g)
        {
            var h = g.house;
            bool oldNotify = Styleable.notify;
            Styleable.notify = false;
            var rnd = new System.Random(2024);
            var usedNames = new HashSet<string>();
            foreach (var f in g.foxes) usedNames.Add(f.foxName);
            int family = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var lot in h.lots)
            {
                if (lot.id == 0 || Lots[lot.id].removed) continue;
                switch (lot.kind)
                {
                    case LotKind.Home:
                        if (Lots[lot.id].template < 0) break; // свободный участок — ждёт новосёлов
                        var fr = BuildHome(g, lot, rnd);
                        SettleFamily(g, lot, fr, rnd, family++, usedNames);
                        break;
                    case LotKind.Cafe: BuildCafe(g, lot, rnd); break;
                    case LotKind.Library: BuildLibrary(g, lot, rnd); break;
                    case LotKind.Park: BuildPark(g, lot); break;
                    case LotKind.Square: BuildSquare(g, lot); break;
                    case LotKind.School: BuildSchool(g, lot, rnd); break;
                    case LotKind.Market: BuildMarket(g, lot); break;
                    case LotKind.Workshop: BuildWorkshop(g, lot, rnd); break;
                    case LotKind.Observatory: BuildObservatory(g, lot, rnd); break;
                    case LotKind.Camp: BuildCamp(g, lot); break;
                }
                g.loadingProgress = Mathf.Min(0.95f, g.loadingProgress + 0.015f);
                yield return null;
            }
            Styleable.notify = oldNotify;
        }

        static void Box(House h, Frame f, int level, int lx0, int lz0, int lx1, int lz1)
        {
            for (int lx = lx0; lx < lx1; lx++) { h.SetEdge(level, f.x0 + lx, f.EZ(lz0), false, EdgeKind.Wall); h.SetEdge(level, f.x0 + lx, f.EZ(lz1), false, EdgeKind.Wall); }
            for (int lz = lz0; lz < lz1; lz++) { h.SetEdge(level, f.x0 + lx0, f.Z(lz), true, EdgeKind.Wall); h.SetEdge(level, f.x0 + lx1, f.Z(lz), true, EdgeKind.Wall); }
        }

        static void HEdge(House h, Frame f, int level, int lx, int k, EdgeKind kind) => h.SetEdge(level, f.x0 + lx, f.EZ(k), false, kind);
        static void VEdge(House h, Frame f, int level, int lxLine, int lz, EdgeKind kind) => h.SetEdge(level, f.x0 + lxLine, f.Z(lz), true, kind);

        static void Exterior(House.Lot lot, System.Random rnd, (string, string, string)? facade = null, (string, string)? roof = null, string shutter = null)
        {
            lot.facade.SetStyle(0, S(facade ?? Facades[rnd.Next(Facades.Length)]));
            var rf = roof ?? Roofs[rnd.Next(Roofs.Length)];
            lot.roof.SetStyle(0, StyleData.Make(rnd.Next(3) == 0 ? "Плитка" : "Чешуя", rf.Item1, rf.Item2, 2.5f));
            lot.roof.SetStyle(1, StyleData.Make("Гладкий", rf.Item2));
            lot.openings.SetStyle(3, StyleData.Make("Доски", shutter ?? Shutters[rnd.Next(Shutters.Length)], null, 2f));
        }

        static House.Room StyleRoom(House h, Frame f, int level, int lx, int lz, string name, System.Random rnd, int roofType, float pitch, (string, string, string)? floor = null, (string, string, string)? wall = null)
        {
            var r = h.RoomAt(level, new Vector2Int(f.x0 + lx, f.Z(lz)));
            if (r == null) return null;
            r.name = name; r.st.displayName = name;
            r.st.SetStyle(0, S(floor ?? Floors[rnd.Next(Floors.Length)]));
            r.st.SetStyle(1, S(wall ?? InWalls[rnd.Next(InWalls.Length)], 0.7f));
            r.roofType = roofType;
            r.roofDir = 0;
            r.roofPitch = pitch;
            return r;
        }

        static Furniture Put(Game g, House.Lot lot, Frame f, string id, int lx, int lz, int rot = 0, int level = 0)
        {
            var def = Catalog.Get(id);
            if (def == null) return null;
            var sz = Furniture.Size(def, rot);
            int r2 = f.south ? rot : rot == 0 ? 2 : rot == 2 ? 0 : rot;
            var a = new Vector2Int(f.x0 + lx, f.south ? f.z0 + lz : f.z0 + f.d - lz - sz.y);
            return PutAt(g, lot, def, a, r2, level);
        }

        static Furniture PutAt(Game g, House.Lot lot, FurnitureDef def, Vector2Int a, int rot, int level = 0)
        {
            if (!g.CanPlace(def, a, rot, null, level, lot.id)) { Debug.LogWarning($"FoxLife: город — не встало {def.id} в {a} ({lot.name})"); return null; }
            return g.SpawnFurniture(def, a, rot, false, level);
        }

        static Furniture PutAt(Game g, House.Lot lot, string id, int x, int z, int rot = 0) => PutAt(g, lot, Catalog.Get(id), new Vector2Int(x, z), rot);

        static Frame BuildHome(Game g, House.Lot lot, System.Random rnd)
        {
            var h = g.house;
            var def = Lots[lot.id];
            int tpl = def.template;
            int w = tpl == 2 ? 13 : 13 + rnd.Next(2), d = tpl == 0 ? 9 : 9 + rnd.Next(2), s = w - 5;
            int gap = tpl == 0 ? 3 : 1;
            var f = new Frame { w = w, d = d, south = lot.frontSouth };
            f.x0 = Mathf.RoundToInt(def.pathX - 4.5f);
            f.z0 = f.south ? lot.rect.yMin + gap : lot.rect.yMax - d - gap;
            Box(h, f, 0, 0, 0, w, d);
            for (int lz = 0; lz < d; lz++) VEdge(h, f, 0, s, lz, EdgeKind.Wall);
            int archA = tpl == 1 ? d - 4 : d / 2 - 1;
            VEdge(h, f, 0, s, archA, EdgeKind.Arch); VEdge(h, f, 0, s, archA + 1, EdgeKind.Arch);
            HEdge(h, f, 0, 4, 0, EdgeKind.Door);
            foreach (var lx in new[] { 1, 7, s + 2 }) HEdge(h, f, 0, lx, 0, EdgeKind.Window);
            foreach (var lx in new[] { 5, s + 2 }) HEdge(h, f, 0, lx, d, EdgeKind.Window);
            VEdge(h, f, 0, 0, 2, EdgeKind.Window); VEdge(h, f, 0, 0, d - 3, EdgeKind.Window);
            VEdge(h, f, 0, w, 2, EdgeKind.Window);
            if (tpl == 0)
            {
                // крылечко под крышей перед дверью
                for (int lz = -2; lz < 0; lz++) { VEdge(h, f, 0, 2, lz, EdgeKind.Railing); VEdge(h, f, 0, 7, lz, EdgeKind.Railing); }
                for (int lx = 2; lx < 7; lx++) HEdge(h, f, 0, lx, -2, lx == 4 ? EdgeKind.RailingOpen : EdgeKind.Railing);
            }
            if (tpl == 1)
            {
                // второй этаж над спальней
                Box(h, f, 1, s, 0, w, d);
                HEdge(h, f, 1, s + 2, 0, EdgeKind.Window); HEdge(h, f, 1, s + 2, d, EdgeKind.Window);
                VEdge(h, f, 1, w, 2, EdgeKind.Window); VEdge(h, f, 1, w, d - 3, EdgeKind.Window);
                VEdge(h, f, 1, s, d - 2, EdgeKind.Window);
            }
            if (tpl == 2)
            {
                // башенка у угла: два этажа, высокая островерхая крыша
                Box(h, f, 0, w, 0, w + 3, 3);
                VEdge(h, f, 0, w, 1, EdgeKind.Arch);
                HEdge(h, f, 0, w + 1, 0, EdgeKind.Window); VEdge(h, f, 0, w + 3, 1, EdgeKind.Window);
                Box(h, f, 1, w, 0, w + 3, 3);
                HEdge(h, f, 1, w + 1, 0, EdgeKind.Window); VEdge(h, f, 1, w + 3, 1, EdgeKind.Window); HEdge(h, f, 1, w + 1, 3, EdgeKind.Window);
                VEdge(h, f, 1, w, 1, EdgeKind.Window);
            }
            if (tpl != 2 && rnd.NextDouble() < 0.7 && lot.rect.xMax - 1 > f.x0 + w + 1) // забор не должен попасть на стену дома
            {
                // штакетник по боковой границе
                int fx = lot.rect.xMax - 1;
                for (int z = lot.rect.yMin + 1; z < lot.rect.yMax - 1; z++) h.SetEdge(0, fx, z, true, EdgeKind.Fence);
                h.SetEdge(0, fx, (lot.rect.yMin + lot.rect.yMax) / 2, true, EdgeKind.Gate);
            }
            Exterior(lot, rnd);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            h.Rebuild(lot);
            long t1 = sw.ElapsedMilliseconds;
            bool hip = rnd.Next(3) == 0;
            float pitch = 0.85f + (float)rnd.NextDouble() * 0.25f;
            int roof = hip ? (int)House.RoofType.Hip : (int)House.RoofType.Gable;
            StyleRoom(h, f, 0, 1, 1, "Гостиная", rnd, roof, pitch);
            StyleRoom(h, f, 0, s + 1, 1, "Спальня", rnd, roof, pitch);
            if (tpl == 0) StyleRoom(h, f, 0, 4, -1, "Крыльцо", rnd, (int)House.RoofType.Hip, 0.5f, ("Доски", "#C99A66", null), ("Гладкий", "#FFF8EE", null));
            if (tpl == 1) StyleRoom(h, f, 1, s + 1, 1, "Мансарда", rnd, (int)House.RoofType.Gable, 1.05f);
            if (tpl == 2)
            {
                StyleRoom(h, f, 0, w + 1, 1, "Башенка", rnd, roof, pitch);
                StyleRoom(h, f, 1, w + 1, 1, "Верх башенки", rnd, (int)House.RoofType.Hip, 1.9f);
            }
            h.RebuildRoofs(lot);
            long t2 = sw.ElapsedMilliseconds;

            // мебель — по рядам от входа
            Put(g, lot, f, "fridge", 0, d - 1); Put(g, lot, f, "stove", 1, d - 1); Put(g, lot, f, "counter", 2, d - 1); Put(g, lot, f, "sink", 3, d - 1);
            Put(g, lot, f, "table", 1, d - 4); Put(g, lot, f, "chair", 1, d - 5, 2); Put(g, lot, f, "chair", 2, d - 5, 2);
            Put(g, lot, f, "sofa", 5, d - 6, 2); Put(g, lot, f, "tv", 6, d - 1); Put(g, lot, f, "bookshelf", s - 1, d - 1);
            Put(g, lot, f, "armchair", s - 1, 2, 1); Put(g, lot, f, "plant", 0, 0); Put(g, lot, f, "lamp", 4, d - 6);
            if (rnd.NextDouble() < 0.5) Put(g, lot, f, "easel", 1, 1, 2); else Put(g, lot, f, "guitar", 1, 1, 2);
            Put(g, lot, f, "bed", s + 1, d - 2); Put(g, lot, f, "bed", s + 3, d - 2);
            Put(g, lot, f, "wardrobe", w - 1, d - 4, 1); Put(g, lot, f, "bath", s + 3, 0, 1);
            Put(g, lot, f, "foxbed", s + 2, d - 5);
            if (tpl == 1)
            {
                Put(g, lot, f, "computer", s + 1, 0, 2);
                Put(g, lot, f, "stairs", s, 1, 0);
                Put(g, lot, f, "rug", s + 1, 4, 0, 1); Put(g, lot, f, "easel", s + 3, 1, 2, 1); Put(g, lot, f, "foxbed", s + 2, d - 2, 0, 1);
                Put(g, lot, f, "bookshelf", s + 1, d - 1, 0, 1); Put(g, lot, f, "lamp", w - 1, 0, 0, 1); Put(g, lot, f, "plant", w - 1, d - 1, 0, 1);
            }
            else Put(g, lot, f, "computer", s, 0, 2);
            if (tpl == 2) { Put(g, lot, f, "armchair", w + 1, 2, 2); Put(g, lot, f, "plant", w + 2, 0); }
            if (tpl == 0) { Put(g, lot, f, "pouf", 3, -1); Put(g, lot, f, "plant", 6, -1); }
            // двор
            int yz = f.south ? f.z0 + d + 1 : f.z0 - 2;
            if (yz >= lot.rect.yMin && yz < lot.rect.yMax)
            {
                PutAt(g, lot, "bench", f.x0 + 6, yz, f.south ? 2 : 0); // спинкой к дому
                PutAt(g, lot, "flowerbed", f.x0 + 1, yz);
                PutAt(g, lot, "flowerbed", f.x0 + 3, yz);
                PutAt(g, lot, "birdbath", f.x0 + 9, yz);
            }
            h.PlantTree(new Vector3(f.x0 + w + (tpl == 2 ? 3.5f : 1.8f), 0, (f.south ? lot.rect.yMax - 1.5f : lot.rect.yMin + 1.5f)), 1.05f, false);
            return f;
        }

        static void SettleFamily(Game g, House.Lot lot, Frame fr, System.Random rnd, int variant, HashSet<string> used)
        {
            string Name(bool female)
            {
                var n = Names.Pick(female, x => used.Contains(x), rnd);
                used.Add(n);
                return n;
            }
            Vector3 Pos(int i) => new Vector3(fr.x0 + 2.5f + i, 0, fr.Z(2) + 0.5f);
            Personality P() => new Personality(rnd.Next(1, 11), rnd.Next(1, 11), rnd.Next(1, 11), rnd.Next(1, 11), rnd.Next(1, 11));
            Fox Make(int i, bool female, LifeStage st)
            {
                var f = g.CreateFox(Name(female), P(), rnd.Next(Game.FurPresets.Length), Pos(i),
                    0.85f + (float)rnd.NextDouble() * 0.55f, 0.85f + (float)rnd.NextDouble() * 0.45f, 0.9f + (float)rnd.NextDouble() * 0.15f, female, st);
                f.home = lot.id;
                f.stageDays = rnd.Next(0, 6);
                f.ageDays = 10 + f.stageDays;
                f.RandomSkills(rnd);
                if (st >= LifeStage.Adult && st < LifeStage.Elder && rnd.NextDouble() < 0.55)
                {
                    f.career = Careers.All[rnd.Next(Careers.All.Length)];
                    f.careerLevel = rnd.Next(0, 3);
                    f.lastWorkDay = -1;
                }
                f.Remember("Жизнь " + lot.At);
                return f;
            }
            void Couple(Fox a, Fox b, bool married)
            {
                a.partnerId = b.id; b.partnerId = a.id;
                a.married = b.married = married;
                Social.AddRel(a, b, 65f);
                Social.AddRom(a, b, 70f);
            }
            void Kid(Fox kit, Fox mom, Fox dad)
            {
                if (mom != null) { kit.motherId = mom.id; Social.AddRel(kit, mom, 55f); }
                if (dad != null) { kit.fatherId = dad.id; Social.AddRel(kit, dad, 55f); }
                if (mom != null) kit.pers = Family.InheritPersonality(mom, dad ?? mom);
            }
            var fam = new List<Fox>();
            switch (variant % 4)
            {
                case 0:
                {
                    var a = Make(0, false, LifeStage.Adult); var b = Make(1, true, LifeStage.Adult);
                    Couple(a, b, true);
                    var k = Make(2, rnd.NextDouble() < 0.5, LifeStage.Kit); Kid(k, b, a);
                    fam.AddRange(new[] { a, b, k });
                    break;
                }
                case 1:
                {
                    var a = Make(0, false, LifeStage.Elder); var b = Make(1, true, LifeStage.Elder);
                    Couple(a, b, true);
                    fam.AddRange(new[] { a, b });
                    break;
                }
                case 2:
                {
                    var a = Make(0, rnd.NextDouble() < 0.5, LifeStage.Adult); var b = Make(1, rnd.NextDouble() < 0.5, LifeStage.Adult);
                    Social.AddRel(a, b, 45f);
                    fam.AddRange(new[] { a, b });
                    break;
                }
                default:
                {
                    var m = Make(0, true, LifeStage.Adult);
                    var t = Make(1, rnd.NextDouble() < 0.5, LifeStage.Teen); Kid(t, m, null);
                    var k = Make(2, rnd.NextDouble() < 0.5, LifeStage.Kit); Kid(k, m, null);
                    Social.AddRel(t, k, 40f);
                    fam.AddRange(new[] { m, t, k });
                    break;
                }
            }
            foreach (var a in fam) foreach (var b in fam) if (a != b && !a.rel.ContainsKey(b)) Social.AddRel(a, b, 30f);
            foreach (var a in fam)
                foreach (var p in g.foxes)
                    if (p.home == House.PlayerLotId && rnd.NextDouble() < 0.5) Social.AddRel(a, p, 5f + (float)rnd.NextDouble() * 12f);
        }

        static Frame PublicFrame(House h, House.Lot lot, int w, int d, int door, int[] windows)
        {
            var f = new Frame { w = w, d = d, south = lot.frontSouth, x0 = Mathf.RoundToInt(Lots[lot.id].pathX - 0.5f) - door };
            f.z0 = f.south ? lot.rect.yMin + 1 : lot.rect.yMax - 1 - f.d;
            Box(h, f, 0, 0, 0, w, d);
            HEdge(h, f, 0, door, 0, EdgeKind.Door);
            foreach (var lx in windows) HEdge(h, f, 0, lx, 0, EdgeKind.Window);
            foreach (var lx in new[] { w / 2 - 1, w / 2 + 1 }) HEdge(h, f, 0, lx, d, EdgeKind.Window);
            foreach (var lz in new[] { 3, d - 3 }) { VEdge(h, f, 0, 0, lz, EdgeKind.Window); VEdge(h, f, 0, w, lz, EdgeKind.Window); }
            return f;
        }

        static void BuildCafe(Game g, House.Lot lot, System.Random rnd)
        {
            var h = g.house;
            var f = PublicFrame(h, lot, 14, 9, 7, new[] { 2, 4, 10, 12 });
            Exterior(lot, rnd, ("Фахверк", "#FFF0E0", "#9A5B3E"), ("#E39A8C", "#C77B6C"), "#7FA36B");
            h.Rebuild(lot);
            StyleRoom(h, f, 0, 1, 1, "Зал кафе", rnd, (int)House.RoofType.Hip, 0.85f, ("Шахматка", "#F3E9DA", "#C79A7A"), ("Полоска", "#F6E3D0", "#EFCFB1"));
            h.RebuildRoofs(lot);
            Put(g, lot, f, "cafecounter", 5, 8); Put(g, lot, f, "cafecounter", 7, 8);
            Put(g, lot, f, "counter", 3, 8); Put(g, lot, f, "counter", 9, 8); Put(g, lot, f, "sink", 10, 8);
            foreach (var t in new[] { (1, 2), (1, 5), (11, 2), (11, 5) })
            {
                Put(g, lot, f, "table", t.Item1, t.Item2);
                Put(g, lot, f, "chair", t.Item1, t.Item2 - 1, 2); Put(g, lot, f, "chair", t.Item1 + 1, t.Item2 - 1, 2);
                Put(g, lot, f, "chair", t.Item1, t.Item2 + 1); Put(g, lot, f, "chair", t.Item1 + 1, t.Item2 + 1);
            }
            Put(g, lot, f, "plant", 0, 0); Put(g, lot, f, "plant", 13, 0); Put(g, lot, f, "plant", 0, 8); Put(g, lot, f, "plant", 13, 8);
            Put(g, lot, f, "lamp", 4, 4); Put(g, lot, f, "lamp", 9, 4);
            Put(g, lot, f, "guitar", 13, 4, 1);
            Put(g, lot, f, "stove", 11, 8); Put(g, lot, f, "stove", 12, 8);
        }

        static void BuildLibrary(Game g, House.Lot lot, System.Random rnd)
        {
            var h = g.house;
            var f = PublicFrame(h, lot, 16, 10, 8, new[] { 5, 11, 14 });
            // башенка-читальня в углу
            VEdge(h, f, 0, 3, 0, EdgeKind.Wall); VEdge(h, f, 0, 3, 1, EdgeKind.Arch); VEdge(h, f, 0, 3, 2, EdgeKind.Wall);
            for (int lx = 0; lx < 3; lx++) HEdge(h, f, 0, lx, 3, EdgeKind.Wall);
            HEdge(h, f, 0, 1, 0, EdgeKind.Window);
            Box(h, f, 1, 0, 0, 3, 3);
            HEdge(h, f, 1, 1, 0, EdgeKind.Window); VEdge(h, f, 1, 0, 1, EdgeKind.Window); VEdge(h, f, 1, 3, 1, EdgeKind.Window);
            Exterior(lot, rnd, ("Камень", "#E6DDCC", "#C9BDA8"), ("#5F9EA0", "#3F7678"), "#5B7FA8");
            h.Rebuild(lot);
            StyleRoom(h, f, 0, 8, 5, "Читальный зал", rnd, (int)House.RoofType.Gable, 0.95f, ("Ёлочка", "#B98A5A", "#94683F"), ("Ромбы", "#EFE6D2", "#DCCDAE"));
            StyleRoom(h, f, 0, 1, 1, "Уголок чтения", rnd, (int)House.RoofType.Hip, 0.9f, ("Ёлочка", "#B98A5A", "#94683F"), ("Звёздочки", "#2F3E5C", "#F2D98A"));
            StyleRoom(h, f, 1, 1, 1, "Звездочётная", rnd, (int)House.RoofType.Hip, 2.0f);
            h.RebuildRoofs(lot);
            foreach (var lx in new[] { 4, 5, 10, 11, 12, 13, 14 }) Put(g, lot, f, "bookshelf", lx, 9);
            foreach (var lz in new[] { 5, 6 }) { Put(g, lot, f, "bookshelf", 0, lz, 3); Put(g, lot, f, "bookshelf", 15, lz, 1); }
            foreach (var lx in new[] { 4, 6, 10, 12 }) Put(g, lot, f, "armchair", lx, 6);
            Put(g, lot, f, "computer", 0, 0, 2); Put(g, lot, f, "armchair", 1, 2, 3);
            Put(g, lot, f, "computer", 12, 0, 2);
            Put(g, lot, f, "table", 6, 3); Put(g, lot, f, "chair", 6, 2, 2); Put(g, lot, f, "chair", 7, 2, 2); Put(g, lot, f, "chair", 6, 4); Put(g, lot, f, "chair", 7, 4);
            Put(g, lot, f, "lamp", 5, 6); Put(g, lot, f, "lamp", 11, 6);
            Put(g, lot, f, "plant", 1, 9); Put(g, lot, f, "plant", 15, 9); Put(g, lot, f, "clock", 8, 9);
            Put(g, lot, f, "rug", 9, 2);
        }

        static void BuildPark(Game g, House.Lot lot)
        {
            var h = g.house;
            // беседка
            for (int x = -24; x < -21; x++)
            {
                h.SetEdge(0, x, -24, false, x == -23 ? EdgeKind.RailingOpen : EdgeKind.Railing);
                h.SetEdge(0, x, -21, false, x == -23 ? EdgeKind.RailingOpen : EdgeKind.Railing);
            }
            for (int z = -24; z < -21; z++) { h.SetEdge(0, -24, z, true, EdgeKind.Railing); h.SetEdge(0, -21, z, true, EdgeKind.Railing); }
            lot.roof.SetStyle(0, StyleData.Make("Чешуя", "#7FA36B", "#5F8350", 2.5f));
            lot.roof.SetStyle(1, StyleData.Make("Гладкий", "#5F8350"));
            h.Rebuild(lot);
            var gz = h.RoomAt(0, new Vector2Int(-23, -23));
            if (gz != null)
            {
                gz.name = "Беседка"; gz.st.displayName = gz.name;
                gz.roofType = (int)House.RoofType.Hip; gz.roofPitch = 0.9f;
                gz.st.SetStyle(0, StyleData.Make("Доски", "#D9B48A"));
                h.RebuildRoofs(lot);
            }
            PutAt(g, lot, "bench", -24, -23, 3);
            PutAt(g, lot, "swing", -42, -21, 3);
            PutAt(g, lot, "swing", -42, -25, 3);
            PutAt(g, lot, "ball", -39, -23);
            PutAt(g, lot, "bench", -37, -28, 3);
            PutAt(g, lot, "bench", -24, -31, 1);
            PutAt(g, lot, "easel", -22, -34, 2);
            PutAt(g, lot, "birdbath", -20, -26);
            foreach (var p in new[] { (-26, -21), (-26, -24), (-20, -21), (-22, -16), (-27, -16) }) PutAt(g, lot, "flowerbed", p.Item1, p.Item2);
            PutAt(g, lot, "fountain", -40, -36);
            // летняя эстрада: здесь работают музыканты
            for (int x = -30; x < -25; x++)
            {
                h.SetEdge(0, x, -40, false, EdgeKind.Railing);
                h.SetEdge(0, x, -36, false, x == -28 || x == -27 ? EdgeKind.RailingOpen : EdgeKind.Railing);
            }
            for (int z = -40; z < -36; z++) { h.SetEdge(0, -30, z, true, EdgeKind.Railing); h.SetEdge(0, -25, z, true, EdgeKind.Railing); }
            h.Rebuild(lot);
            var stage = h.RoomAt(0, new Vector2Int(-28, -38));
            if (stage != null)
            {
                stage.name = "Летняя эстрада"; stage.st.displayName = stage.name;
                stage.roofType = (int)House.RoofType.Hip; stage.roofPitch = 0.75f;
                stage.st.SetStyle(0, StyleData.Make("Ёлочка", "#D9B48A", "#C29A6E"));
                h.RebuildRoofs(lot);
            }
            PutAt(g, lot, "piano", -29, -39, 0);
            PutAt(g, lot, "guitar", -26, -38, 2);
            PutAt(g, lot, "bench", -26, -34, 0);

            PutAt(g, lot, "bench", -40, -33, 0);
        }

        static void BuildSchool(Game g, House.Lot lot, System.Random rnd)
        {
            var h = g.house;
            var f = PublicFrame(h, lot, 18, 11, 9, new[] { 3, 6, 12, 15 });
            // задняя стена: в середине — доска, окна по краям
            HEdge(h, f, 0, 8, 11, EdgeKind.Wall); HEdge(h, f, 0, 10, 11, EdgeKind.Wall);
            HEdge(h, f, 0, 2, 11, EdgeKind.Window); HEdge(h, f, 0, 15, 11, EdgeKind.Window);
            Exterior(lot, rnd, ("Кирпич", "#F2C8A8", "#E0B090"), ("#5B7FA8", "#43658A"), "#F2F0E6");
            h.Rebuild(lot);
            StyleRoom(h, f, 0, 9, 5, "Класс", rnd, (int)House.RoofType.Hip, 0.8f, ("Доски", "#D9B48A", null), ("Клетка", "#EAF2E3", "#D6E4CC"));
            h.RebuildRoofs(lot);
            foreach (var lz in new[] { 3, 5, 7 })
                foreach (var lx in new[] { 3, 5, 7, 11, 13, 15 })
                    Put(g, lot, f, "schooldesk", lx, lz);
            Put(g, lot, f, "chalkboard", 8, 10);
            Put(g, lot, f, "bookshelf", 0, 7, 3); Put(g, lot, f, "bookshelf", 0, 8, 3);
            Put(g, lot, f, "plant", 0, 0); Put(g, lot, f, "plant", 17, 0); Put(g, lot, f, "plant", 17, 10);
            Put(g, lot, f, "clock", 4, 10); Put(g, lot, f, "lamp", 1, 4);
            // школьный двор
            int y0 = lot.rect.yMin;
            PutAt(g, lot, "swing", 91, y0 + 1, 0);
            PutAt(g, lot, "swing", 95, y0 + 1, 0);
            PutAt(g, lot, "ball", 101, y0 + 2);
            PutAt(g, lot, "bench", 105, y0 + 1, 2);
            PutAt(g, lot, "flowerbed", 108, y0 + 3);
        }

        static void BuildMarket(Game g, House.Lot lot)
        {
            int x0 = lot.rect.xMin, z0 = lot.rect.yMin;
            // два ряда прилавков, продавцы стоят в проходе между рядами
            foreach (var dx in new[] { 2, 6, 14, 18 })
            {
                PutAt(g, lot, "marketstall", x0 + dx, z0 + 4, 0);
                PutAt(g, lot, "marketstall", x0 + dx, z0 + 8, 2);
            }
            PutAt(g, lot, "fountain", x0 + 10, z0 + 11);
            PutAt(g, lot, "bench", x0 + 2, z0 + 12, 0);
            PutAt(g, lot, "bench", x0 + 17, z0 + 12, 0);
            foreach (var dx in new[] { 1, 20 }) PutAt(g, lot, "flowerbed", x0 + dx, z0 + 1);
        }

        static void BuildWorkshop(Game g, House.Lot lot, System.Random rnd)
        {
            var h = g.house;
            var f = PublicFrame(h, lot, 14, 9, 7, new[] { 2, 4, 10, 12 });
            Exterior(lot, rnd, ("Доски", "#D9C8F0", null), ("#4F8A86", "#3C6E6A"), "#F2D98A");
            h.Rebuild(lot);
            StyleRoom(h, f, 0, 7, 4, "Мастерская", rnd, (int)House.RoofType.Gable, 1.0f, ("Доски", "#C99A66", null), ("Гладкий", "#FFF8EE", null));
            h.RebuildRoofs(lot);
            foreach (var lx in new[] { 2, 5, 9, 12 }) Put(g, lot, f, "easel", lx, 4, 2);
            Put(g, lot, f, "table", 6, 7); Put(g, lot, f, "chair", 6, 6, 2); Put(g, lot, f, "chair", 7, 6, 2);
            Put(g, lot, f, "bookshelf", 0, 8); Put(g, lot, f, "bookshelf", 13, 8);
            foreach (var lx in new[] { 3, 10 }) Put(g, lot, f, "painting", lx, 8);
            Put(g, lot, f, "plant", 0, 0); Put(g, lot, f, "plant", 13, 0); Put(g, lot, f, "lamp", 4, 1); Put(g, lot, f, "lamp", 10, 1);
            PutAt(g, lot, "easel", lot.rect.xMin + 3, lot.rect.yMax - 2, 2);
            PutAt(g, lot, "bench", lot.rect.xMin + 9, lot.rect.yMax - 2, 2);
        }

        static void BuildObservatory(Game g, House.Lot lot, System.Random rnd)
        {
            var h = g.house;
            var f = PublicFrame(h, lot, 8, 8, 4, new[] { 1, 6 });
            Exterior(lot, rnd, ("Камень", "#E6E1D8", "#C9C1B5"), ("#3F4E78", "#2E3A5C"), "#3F4E78");
            h.Rebuild(lot);
            StyleRoom(h, f, 0, 4, 4, "Зал обсерватории", rnd, (int)House.RoofType.Flat, 0.6f, ("Шахматка", "#E6E1D8", "#3F4E78"), ("Звёздочки", "#2F3E5C", "#F2D98A"));
            h.RebuildRoofs(lot);
            Put(g, lot, f, "telescope", 2, 5); Put(g, lot, f, "telescope", 5, 5);
            Put(g, lot, f, "computer", 1, 0, 2); Put(g, lot, f, "bookshelf", 7, 7, 1); Put(g, lot, f, "lamp", 0, 7);
            int zt = lot.rect.yMax - 2;
            PutAt(g, lot, "telescope", lot.rect.xMin + 2, zt);
            PutAt(g, lot, "telescope", lot.rect.xMax - 3, zt);
            PutAt(g, lot, "bench", lot.rect.xMin + 6, zt, 2);
        }

        static void BuildCamp(Game g, House.Lot lot)
        {
            PutAt(g, lot, "campfire", -78, -24);
            PutAt(g, lot, "bench", -78, -21, 0);
            PutAt(g, lot, "bench", -78, -26, 2);
            PutAt(g, lot, "bench", -81, -24, 3);
            PutAt(g, lot, "bench", -75, -24, 1);
            PutAt(g, lot, "berrybush", -85, -17);
            PutAt(g, lot, "berrybush", -68, -18);
            PutAt(g, lot, "berrybush", -68, -25);
            PutAt(g, lot, "berrybush", -86, -21);
            PutAt(g, lot, "berrybush", -76, -29);
            PutAt(g, lot, "guitar", -80, -18, 2);
        }

        static void BuildSquare(Game g, House.Lot lot)
        {
            int cx = 9, cz = -17;
            PutAt(g, lot, "fountain", cx, cz);
            PutAt(g, lot, "bench", cx, cz + 4, 0);
            PutAt(g, lot, "bench", cx, cz - 3, 2);
            PutAt(g, lot, "bench", cx - 4, cz, 3);
            PutAt(g, lot, "bench", cx + 5, cz, 1);
            foreach (var p in new[] { (cx - 2, cz + 3), (cx + 3, cz + 3), (cx - 2, cz - 2), (cx + 3, cz - 2) }) PutAt(g, lot, "flowerbed", p.Item1, p.Item2);
            // торговые ряды на площади: продавцы стоят за прилавками у края площади
            PutAt(g, lot, "marketstall", 3, -13, 3);
            PutAt(g, lot, "marketstall", 3, -18, 3);
            PutAt(g, lot, "marketstall", 5, -11, 2);
            PutAt(g, lot, "marketstall", 11, -11, 2);
            // летняя терраса кафе
            foreach (var tx in new[] { 3, 13 })
            {
                PutAt(g, lot, "table", tx, -21);
                PutAt(g, lot, "chair", tx, -20, 0); PutAt(g, lot, "chair", tx + 1, -20, 0);
                PutAt(g, lot, "chair", tx, -22, 2); PutAt(g, lot, "chair", tx + 1, -22, 2);
            }
        }
    }

    /// <summary>Метка моста: по ней лисы ходят над водой. Снесли мост — и пройти больше нельзя.</summary>
    public class BridgeMark : MonoBehaviour
    {
        public static readonly List<BridgeMark> All = new List<BridgeMark>();
        public float length, width;
        Furniture furn;
        bool checkedFurn;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        public bool Live
        {
            get
            {
                if (!checkedFurn) { furn = GetComponentInParent<Furniture>(); checkedFurn = true; }
                return furn == null || !furn.isGhost;
            }
        }

        public bool Covers(float x, float z, float inset)
        {
            var q = transform.InverseTransformPoint(new Vector3(x, transform.position.y, z));
            return Mathf.Abs(q.z) < length / 2f && Mathf.Abs(q.x) < width / 2f - inset;
        }
    }
}
