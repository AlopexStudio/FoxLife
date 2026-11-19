using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public class PatternDef
    {
        public string name;
        public Texture2D tex;      // R = светотень, G = маска второго цвета
        public Texture2D preview;  // серое превью для интерфейса
        public float tilesPerMeter;
        public bool usesColor2;
    }

    /// <summary>
    /// Небольшая библиотека бесшовных серых узоров, которые генерируются при запуске.
    /// Цвет задаётся отдельно, поэтому двадцати узоров хватает, чтобы покрасить что угодно во что угодно.
    /// </summary>
    public static class Patterns
    {
        const int Size = 256;
        const int PreviewSize = 64;

        delegate void Gen(float u, float v, out float shade, out float mask);

        static List<PatternDef> list;

        public static List<PatternDef> All
        {
            get { if (list == null) Build(); return list; }
        }

        public static PatternDef Get(int i) => All[Mathf.Clamp(i, 0, All.Count - 1)];

        public static int IndexOf(string name)
        {
            var l = All;
            for (int i = 0; i < l.Count; i++) if (l[i].name == name) return i;
            return 0;
        }

        static void Build()
        {
            list = new List<PatternDef>();
            Add("Гладкий", 1f, false, Smooth);
            Add("Доски", 0.8f, false, Planks);
            Add("Паркет", 1f, true, Parquet);
            Add("Дерево", 1f, false, Wood);
            Add("Кирпич", 1f, true, Brick);
            Add("Плитка", 1.2f, true, Tiles);
            Add("Шахматка", 1f, true, Checker);
            Add("Ткань", 2f, false, Weave);
            Add("Вельвет", 1.5f, false, Corduroy);
            Add("Клетка", 1.2f, true, Plaid);
            Add("Полоска", 1.5f, true, Stripes);
            Add("Горошек", 1.5f, true, Dots);
            Add("Ромбы", 1.2f, true, Argyle);
            Add("Цветочки", 1.5f, true, Flowers);
            Add("Лисьи следы", 1.2f, true, Paws);
            Add("Ворс", 2f, false, Fur);
            Add("Мрамор", 0.7f, true, Marble);
            Add("Бетон", 1f, false, Concrete);
            Add("Камень", 0.8f, true, Cobble);
            Add("Кожа", 1.5f, false, Leather);
            Add("Соты", 1.5f, true, Hex);
            Add("Звёздочки", 1.5f, true, Stars);
            Add("Фахверк", 0.5f, true, Timber);
            Add("Чешуя", 2.5f, true, Scales);
            Add("Ёлочка", 1.5f, true, Herringbone);
        }

        /// <summary>Фахверк: штукатурка в деревянной раме с подкосами. Плитка 2×2 м — ровно на этаж.</summary>
        static void Timber(float u, float v, out float s, out float m)
        {
            const float bw = 0.035f;
            float hu = Frac(u * 2f);                 // половина плитки — одна панель
            bool beam = v < bw || v > 1f - bw * 0.6f || Mathf.Abs(v - 0.5f) < bw * 0.6f || hu < bw * 1.2f;
            if (!beam && v < 0.5f)
            {
                // подкос по диагонали нижней панели, в соседних панелях — навстречу
                float d = (Mathf.FloorToInt(u * 2f) & 1) == 0 ? hu - v * 2f : (1f - hu) - v * 2f;
                beam = Mathf.Abs(d) < bw * 2.2f;
            }
            if (beam) { s = 0.82f + (Fbm(u, v, 16, 3, 3) - 0.5f) * 0.25f; m = 1; }
            else { s = 0.94f + (Fbm(u, v, 32, 3, 6) - 0.5f) * 0.1f; m = 0; }
        }

        /// <summary>Черепица-чешуйка, как на сказочных башенках.</summary>
        static void Scales(float u, float v, out float s, out float m)
        {
            const int rows = 6, cols = 6;
            float rv = v * rows;
            int r = Mathf.FloorToInt(rv);
            float off = (r & 1) * 0.5f;
            float cu = u * cols + off;
            int ci = Mathf.FloorToInt(cu);
            float fu = cu - ci - 0.5f, fv = rv - r;
            float d = Mathf.Sqrt(fu * fu + fv * fv * 0.9f);
            if (d > 0.58f)
            {
                // выше — край соседнего ряда
                float fu2 = Frac(u * cols + ((r + 1) & 1) * 0.5f) - 0.5f, fv2 = fv - 1f;
                d = Mathf.Sqrt(fu2 * fu2 + fv2 * fv2 * 0.9f);
            }
            bool edge = d > 0.5f && d < 0.58f;
            s = 0.75f + 0.25f * fv + Hash(Wrap(ci, cols), Wrap(r, rows), 4) * 0.1f;
            m = edge ? 1 : 0;
        }

        static void Herringbone(float u, float v, out float s, out float m)
        {
            const int n = 8;
            float x = u * n, y = v * n;
            int col = Mathf.FloorToInt(x);
            float yy = y + ((col & 1) == 0 ? x - col : 1f - (x - col));
            int row = Mathf.FloorToInt(yy);
            float fy = yy - row, fx = x - col;
            bool line = fy < 0.08f || fx < 0.06f;
            s = line ? 0.85f : 0.8f + Hash(Wrap(col, n), Wrap(row, n * 2), 7) * 0.18f;
            m = line ? 1 : 0;
        }

        static void Add(string name, float tiles, bool color2, Gen gen)
        {
            var px = new Color32[Size * Size];
            var pv = new Color32[PreviewSize * PreviewSize];
            int step = Size / PreviewSize;
            for (int y = 0; y < Size; y++)
            {
                float v = (y + 0.5f) / Size;
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size;
                    gen(u, v, out float s, out float m);
                    s = Mathf.Clamp01(s);
                    m = Mathf.Clamp01(m);
                    px[y * Size + x] = new Color32((byte)(s * 255), (byte)(m * 255), 0, 255);
                    if (x % step == 0 && y % step == 0)
                    {
                        float g = s * (1f - 0.45f * m);
                        byte b = (byte)(g * 255);
                        pv[(y / step) * PreviewSize + x / step] = new Color32(b, b, b, 255);
                    }
                }
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);

            var prev = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false)
            {
                name = name + " preview",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            prev.SetPixels32(pv);
            prev.Apply(false, true);

            list.Add(new PatternDef { name = name, tex = tex, preview = prev, tilesPerMeter = tiles, usesColor2 = color2 });
        }

        // ---------- шум ----------

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static int Wrap(int a, int p) { int r = a % p; return r < 0 ? r + p : r; }
        static float Frac(float x) => x - Mathf.Floor(x);
        static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        // бесшовный value noise: px, py — число ячеек решётки по осям на один тайл
        static float Noise(float u, float v, int px, int py, int seed)
        {
            float x = u * px, y = v * py;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int xa = Wrap(x0, px), xb = Wrap(x0 + 1, px), ya = Wrap(y0, py), yb = Wrap(y0 + 1, py);
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int px, int py, int oct, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < oct; i++)
            {
                sum += Noise(u, v, px, py, seed + i * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                px *= 2; py *= 2;
            }
            return sum / norm;
        }

        static float Fbm(float u, float v, int p, int oct, int seed) => Fbm(u, v, p, p, oct, seed);

        static void Voronoi(float u, float v, int n, int seed, out float f1, out float f2, out float id)
        {
            float x = u * n, y = v * n;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = 10; f2 = 10; id = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx, gy = cy + dy;
                    int wx = Wrap(gx, n), wy = Wrap(gy, n);
                    float ppx = gx + 0.15f + 0.7f * Hash(wx, wy, seed);
                    float ppy = gy + 0.15f + 0.7f * Hash(wx, wy, seed + 1);
                    float d = Mathf.Sqrt((ppx - x) * (ppx - x) + (ppy - y) * (ppy - y));
                    if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, seed + 2); }
                    else if (d < f2) f2 = d;
                }
        }

        // ---------- генераторы ----------

        static void Smooth(float u, float v, out float s, out float m)
        {
            s = 0.95f + 0.05f * Fbm(u, v, 8, 3, 1);
            m = 0;
        }

        static void Planks(float u, float v, out float s, out float m)
        {
            const int rows = 4;
            float row = v * rows;
            int r = Mathf.FloorToInt(row);
            float fr = row - r;
            float uu = Frac(u + Hash(r, 0, 5));
            float segF = uu * 2f;
            int seg = Mathf.FloorToInt(segF);
            float fs = segF - seg;
            float grain = Fbm(u, v, 3, 48, 3, 11 + r);
            s = 0.8f + Hash(r, seg, 7) * 0.12f + (grain - 0.5f) * 0.3f;
            float gap = Mathf.Min(fr, 1 - fr);
            float seam = Mathf.Min(fs, 1 - fs);
            if (gap < 0.025f || seam < 0.008f) s *= 0.55f;
            m = 0;
        }

        static void Parquet(float u, float v, out float s, out float m)
        {
            int bx = Mathf.FloorToInt(u * 4), by = Mathf.FloorToInt(v * 4);
            int checker = (bx + by) & 1;
            float lu = Frac(u * 4), lv = Frac(v * 4);
            float across = checker == 0 ? lv : lu;
            float stripF = across * 3;
            int strip = Mathf.FloorToInt(stripF);
            float fs = stripF - strip;
            float grain = checker == 0 ? Fbm(u, v, 8, 64, 3, 3) : Fbm(u, v, 64, 8, 3, 3);
            s = 0.8f + Hash(bx * 3 + strip, by, 9) * 0.1f + (grain - 0.5f) * 0.25f;
            float edge = Mathf.Min(Mathf.Min(lu, 1 - lu), Mathf.Min(lv, 1 - lv));
            if (fs < 0.05f || edge < 0.015f) s *= 0.6f;
            m = checker;
        }

        static void Wood(float u, float v, out float s, out float m)
        {
            float n = Fbm(u, v, 2, 6, 3, 21);
            float w = Frac(v * 6 + n * 2f);
            float line = SS(0.8f, 1f, w);
            s = 0.88f - line * 0.22f + (Fbm(u, v, 4, 64, 3, 22) - 0.5f) * 0.18f;
            m = 0;
        }

        static void Brick(float u, float v, out float s, out float m)
        {
            const int rows = 8, cols = 4;
            int r = Mathf.FloorToInt(v * rows);
            float off = (r & 1) * 0.5f;
            float bu = u * cols + off;
            int bi = Mathf.FloorToInt(bu);
            float fu = bu - bi;
            float fv = v * rows - r;
            bool mortar = fv < 0.12f || fu < 0.06f;
            if (mortar) { s = 0.92f + 0.05f * Fbm(u, v, 32, 2, 4); m = 1; }
            else
            {
                s = 0.72f + Hash(Wrap(bi, cols), r, 9) * 0.2f + (Fbm(u, v, 32, 3, 8) - 0.5f) * 0.2f;
                m = 0;
            }
        }

        static void Tiles(float u, float v, out float s, out float m)
        {
            float fu = Frac(u * 4), fv = Frac(v * 4);
            float edge = Mathf.Min(Mathf.Min(fu, 1 - fu), Mathf.Min(fv, 1 - fv));
            if (edge < 0.035f) { s = 0.9f; m = 1; }
            else
            {
                s = 0.97f - 0.08f * (1 - SS(0.035f, 0.1f, edge)) + (Fbm(u, v, 16, 2, 5) - 0.5f) * 0.04f;
                m = 0;
            }
        }

        static void Checker(float u, float v, out float s, out float m)
        {
            int c = (Mathf.FloorToInt(u * 4) + Mathf.FloorToInt(v * 4)) & 1;
            s = 0.96f + 0.04f * Fbm(u, v, 16, 2, 6);
            m = c;
        }

        static void Weave(float u, float v, out float s, out float m)
        {
            const int n = 32;
            float x = u * n, y = v * n;
            int cell = (Mathf.FloorToInt(x) + Mathf.FloorToInt(y)) & 1;
            float t = cell == 0 ? Frac(y) : Frac(x);
            s = 0.8f + 0.2f * Mathf.Sin(Mathf.PI * t) + (Fbm(u, v, 16, 2, 12) - 0.5f) * 0.06f;
            m = 0;
        }

        static void Corduroy(float u, float v, out float s, out float m)
        {
            s = 0.74f + 0.26f * Mathf.Sin(Mathf.PI * Frac(u * 24)) + (Fbm(u, v, 8, 2, 13) - 0.5f) * 0.05f;
            m = 0;
        }

        static void Plaid(float u, float v, out float s, out float m)
        {
            float bu = Frac(u * 2), bv = Frac(v * 2);
            float mu = bu < 0.38f ? 1 : 0;
            float mv = bv < 0.38f ? 1 : 0;
            m = (mu + mv) * 0.5f;
            bool line = Mathf.Abs(bu - 0.68f) < 0.018f || Mathf.Abs(bv - 0.68f) < 0.018f;
            float weave = 0.93f + 0.07f * Mathf.Sin(Mathf.PI * Frac((u + v) * 64));
            s = (line ? 0.62f : 0.97f) * weave;
        }

        static void Stripes(float u, float v, out float s, out float m)
        {
            float f = Frac(u * 4);
            m = f < 0.5f ? 0 : 1;
            s = 0.97f;
        }

        static void Dots(float u, float v, out float s, out float m)
        {
            int r = Mathf.FloorToInt(v * 4);
            float x = u * 4 + (r & 1) * 0.5f;
            float fx = Frac(x) - 0.5f, fy = Frac(v * 4) - 0.5f;
            float d = Mathf.Sqrt(fx * fx + fy * fy);
            m = 1 - SS(0.2f, 0.225f, d);
            s = 0.97f;
        }

        static void Argyle(float u, float v, out float s, out float m)
        {
            float x = Frac(u * 2) - 0.5f, y = Frac(v * 2) - 0.5f;
            float d = Mathf.Abs(x) + Mathf.Abs(y);
            m = 1 - SS(0.47f, 0.5f, d);
            bool line = Mathf.Abs(Frac((u + v) * 4) - 0.5f) < 0.02f || Mathf.Abs(Frac((u - v) * 4) - 0.5f) < 0.02f;
            s = line ? 0.68f : 0.97f;
        }

        static void Flowers(float u, float v, out float s, out float m)
        {
            int r = Mathf.FloorToInt(v * 4);
            float x = Frac(u * 4 + (r & 1) * 0.5f) - 0.5f, y = Frac(v * 4) - 0.5f;
            float rad = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(y, x);
            float petal = 0.17f + 0.13f * Mathf.Abs(Mathf.Cos(2.5f * ang));
            m = 1 - SS(petal - 0.02f, petal, rad);
            s = rad < 0.065f ? 0.7f : 0.97f;
        }

        static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return 1 - SS(0.85f, 1f, Mathf.Sqrt(dx * dx + dy * dy));
        }

        static void Paws(float u, float v, out float s, out float m)
        {
            int r = Mathf.FloorToInt(v * 3);
            int c = Mathf.FloorToInt(u * 3 + (r & 1) * 0.5f);
            float x = Frac(u * 3 + (r & 1) * 0.5f) - 0.5f, y = Frac(v * 3) - 0.5f;
            float a = ((Wrap(c, 3) + r) & 1) == 0 ? 0.35f : -0.35f;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            float rx = x * ca - y * sa, ry = x * sa + y * ca;
            float mm = Ellipse(rx, ry, 0, -0.08f, 0.14f, 0.11f);
            mm = Mathf.Max(mm, Ellipse(rx, ry, -0.165f, 0.07f, 0.055f, 0.065f));
            mm = Mathf.Max(mm, Ellipse(rx, ry, -0.06f, 0.16f, 0.055f, 0.065f));
            mm = Mathf.Max(mm, Ellipse(rx, ry, 0.06f, 0.16f, 0.055f, 0.065f));
            mm = Mathf.Max(mm, Ellipse(rx, ry, 0.165f, 0.07f, 0.055f, 0.065f));
            m = mm;
            s = 0.97f;
        }

        static void Fur(float u, float v, out float s, out float m)
        {
            s = 0.78f + 0.14f * Fbm(u, v, 64, 2, 31) + 0.1f * Fbm(u, v, 8, 3, 32);
            m = 0;
        }

        static void Marble(float u, float v, out float s, out float m)
        {
            float n = Fbm(u, v, 4, 5, 41);
            float a = 2 * Mathf.PI * ((u + v) * 2) + n * 7f;
            m = Mathf.Pow(1 - Mathf.Abs(Mathf.Sin(a)), 14);
            s = 0.95f - 0.07f * n;
        }

        static void Concrete(float u, float v, out float s, out float m)
        {
            s = 0.8f + 0.14f * Fbm(u, v, 16, 4, 51);
            int px = Mathf.FloorToInt(u * Size), py = Mathf.FloorToInt(v * Size);
            if (Hash(px, py, 52) > 0.985f) s -= 0.18f;
            m = 0;
        }

        static void Cobble(float u, float v, out float s, out float m)
        {
            Voronoi(u, v, 5, 61, out float f1, out float f2, out float id);
            float edge = f2 - f1;
            m = 1 - SS(0.05f, 0.11f, edge);
            s = Mathf.Lerp(0.72f + id * 0.2f + (Fbm(u, v, 16, 3, 62) - 0.5f) * 0.12f, 0.88f, m);
        }

        static void Leather(float u, float v, out float s, out float m)
        {
            Voronoi(u, v, 24, 71, out float f1, out float f2, out float id);
            float crease = 1 - SS(0f, 0.14f, f2 - f1);
            s = 0.9f - crease * 0.18f + (Fbm(u, v, 8, 3, 72) - 0.5f) * 0.08f;
            m = 0;
        }

        static void Hex(float u, float v, out float s, out float m)
        {
            // шестиугольная сетка, бесшовная на тайле 4 x 2 ячейки
            float x = u * 4f, y = v * 4f * 0.8660254f;
            float hy = 1.7320508f;
            // две подрешётки центров ячеек
            Vector2 p = new Vector2(x, y);
            Vector2 a = new Vector2(Mathf.Round(x), Mathf.Round(y / hy) * hy);
            Vector2 b = new Vector2(Mathf.Floor(x) + 0.5f, (Mathf.Floor(y / hy) + 0.5f) * hy);
            float d = Mathf.Min(HexDist(p - a), HexDist(p - b)); // 0 в центре, 0.5 на границе
            m = SS(0.43f, 0.47f, d);
            s = 0.97f - (1 - m) * 0.07f * SS(0.25f, 0.43f, d);
        }

        static float HexDist(Vector2 q)
        {
            q.x = Mathf.Abs(q.x); q.y = Mathf.Abs(q.y);
            return Mathf.Max(q.x, 0.5f * q.x + 0.8660254f * q.y);
        }

        static void Stars(float u, float v, out float s, out float m)
        {
            int r = Mathf.FloorToInt(v * 4);
            float x = Frac(u * 4 + (r & 1) * 0.5f) - 0.5f, y = Frac(v * 4) - 0.5f;
            float rad = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(y, x) + Mathf.PI / 2f;
            float k = Mathf.Abs(Mathf.Cos(2.5f * ang));
            float star = Mathf.Lerp(0.09f, 0.22f, Mathf.Pow(k, 3));
            m = 1 - SS(star - 0.015f, star, rad);
            s = 0.97f;
        }
    }
}
