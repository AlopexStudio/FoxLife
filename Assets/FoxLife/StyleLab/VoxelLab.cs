using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FoxLife.Lab
{
    /// <summary>
    /// Лаборатория стиля №2: мелкий воксельный арт (как в редакторах воксельной графики) —
    /// плавный рельеф из мелких вокселов, цвет у каждого воксела свой, мягкое затенение углов.
    /// Запуск: FoxLife.exe -voxellab "папка". В игру не входит.
    /// </summary>
    public class VoxelLab : MonoBehaviour
    {
        static string outDir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Detect()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-voxellab") { outDir = args[i + 1]; StyleLab.Active = true; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (outDir != null) new GameObject("VoxelLab").AddComponent<VoxelLab>();
        }

        // ================= воксельная сетка =================

        public class VGrid
        {
            public readonly int nx, ny, nz;
            public readonly float s;
            public readonly Color32[] c;
            public Vector3 origin;

            public VGrid(int nx, int ny, int nz, float s, Vector3 origin = default)
            {
                this.nx = nx; this.ny = ny; this.nz = nz; this.s = s; this.origin = origin;
                c = new Color32[nx * ny * nz];
            }

            public bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz;
            public bool Filled(int x, int y, int z) => In(x, y, z) && c[(y * nz + z) * nx + x].a > 0;
            public void Set(int x, int y, int z, Color col) { if (In(x, y, z)) c[(y * nz + z) * nx + x] = col; }
            public void Clear(int x, int y, int z) { if (In(x, y, z)) c[(y * nz + z) * nx + x] = default; }
            public Color32 Get(int x, int y, int z) => c[(y * nz + z) * nx + x];
            public int I(float m) => Mathf.FloorToInt(m / s);
            public Vector3 Center(int x, int y, int z) => origin + new Vector3((x + 0.5f) * s, (y + 0.5f) * s, (z + 0.5f) * s);

            /// <summary>Заполнить все вокселы, центр которых попадает внутрь фигуры.</summary>
            public void Fill(Vector3 min, Vector3 max, System.Func<Vector3, bool> inside, System.Func<Vector3, int, int, int, Color> color)
            {
                min -= origin; max -= origin;
                int x0 = Mathf.Max(0, I(min.x)), y0 = Mathf.Max(0, I(min.y)), z0 = Mathf.Max(0, I(min.z));
                int x1 = Mathf.Min(nx - 1, I(max.x)), y1 = Mathf.Min(ny - 1, I(max.y)), z1 = Mathf.Min(nz - 1, I(max.z));
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var p = Center(x, y, z);
                            if (inside(p)) Set(x, y, z, color(p, x, y, z));
                        }
            }

            public void Box(Vector3 min, Vector3 max, System.Func<Vector3, int, int, int, Color> color) =>
                Fill(min, max, p => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z, color);
        }

        static readonly Vector3Int[] N = { Vector3Int.up, Vector3Int.down, Vector3Int.right, Vector3Int.left, new(0, 0, 1), new(0, 0, -1) };
        static readonly Vector3Int[] U = { new(1, 0, 0), new(0, 0, 1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0), new(1, 0, 0) };
        static readonly Vector3Int[] V = { new(0, 0, 1), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0), new(0, 1, 0) };
        static readonly float[] AoK = { 0.58f, 0.72f, 0.86f, 1f };

        /// <summary>Сетка с отсечением скрытых граней и затенением углов (ambient occlusion) в цвете вершин.</summary>
        public static void Mesh(VGrid g, Transform parent, Vector3 offset, Material mat, string name, int chunk = 48)
        {
            for (int cx = 0; cx < g.nx; cx += chunk)
                for (int cz = 0; cz < g.nz; cz += chunk)
                {
                    var v = new List<Vector3>();
                    var n = new List<Vector3>();
                    var col = new List<Color32>();
                    var t = new List<int>();
                    for (int y = 0; y < g.ny; y++)
                        for (int z = cz; z < Mathf.Min(g.nz, cz + chunk); z++)
                            for (int x = cx; x < Mathf.Min(g.nx, cx + chunk); x++)
                            {
                                if (!g.Filled(x, y, z)) continue;
                                var baseCol = g.Get(x, y, z);
                                var cell = new Vector3Int(x, y, z);
                                for (int f = 0; f < 6; f++)
                                {
                                    var o = cell + N[f];
                                    if (g.Filled(o.x, o.y, o.z)) continue;
                                    var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) + (Vector3)N[f] * 0.5f;
                                    int[] du = { -1, -1, 1, 1 }, dv = { -1, 1, 1, -1 };
                                    int[] ao = new int[4];
                                    int i0 = v.Count;
                                    for (int k = 0; k < 4; k++)
                                    {
                                        var a = o + U[f] * du[k];
                                        var b = o + V[f] * dv[k];
                                        var cc = o + U[f] * du[k] + V[f] * dv[k];
                                        bool s1 = g.Filled(a.x, a.y, a.z), s2 = g.Filled(b.x, b.y, b.z), s3 = g.Filled(cc.x, cc.y, cc.z);
                                        ao[k] = s1 && s2 ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (s3 ? 1 : 0));
                                        var p = center + ((Vector3)U[f] * du[k] + (Vector3)V[f] * dv[k]) * 0.5f;
                                        v.Add(p * g.s + offset);
                                        n.Add(N[f]);
                                        float kk = AoK[ao[k]];
                                        col.Add(new Color32((byte)(baseCol.r * kk), (byte)(baseCol.g * kk), (byte)(baseCol.b * kk), 255));
                                    }
                                    if (ao[0] + ao[2] < ao[1] + ao[3])
                                    { t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0 + 3); t.Add(i0 + 1); t.Add(i0 + 3); t.Add(i0); }
                                    else
                                    { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
                                }
                            }
                    if (v.Count == 0) continue;
                    var go = new GameObject(name);
                    go.transform.SetParent(parent, false);
                    var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.SetVertices(v);
                    mesh.SetNormals(n);
                    mesh.SetColors(col);
                    mesh.SetTriangles(t, 0);
                    mesh.RecalculateBounds();
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
        }

        // ================= палитра и шум =================

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Небольшой разброс оттенка у каждого воксела — главный приём воксельного арта.</summary>
        static Color Vary(Color c, int x, int y, int z, float amount = 0.05f)
        {
            float r = (Hash(x, y, z) - 0.5f) * 2f * amount;
            return new Color(Mathf.Clamp01(c.r + r), Mathf.Clamp01(c.g + r * 0.9f), Mathf.Clamp01(c.b + r * 0.8f), 1f);
        }

        static Color Pick(int x, int y, int z, params Color[] cs) => cs[Mathf.Min(cs.Length - 1, (int)(Hash(x, y, z) * cs.Length))];

        static readonly Color GrassA = H("#9ACB7C"), GrassB = H("#8BC071"), GrassC = H("#A9D488"), GrassDry = H("#C2CF8A");
        static readonly Color Soil = H("#B8906F"), SoilDark = H("#A27D60"), Stone = H("#B7B5B8"), StoneDark = H("#9C9AA0");
        static readonly Color PathA = H("#E2D6C1"), PathB = H("#D4C6AE"), PathC = H("#C9BBA3");
        static readonly Color Water = H("#86BCE6"), WaterLight = H("#A9D2F0");
        static readonly Color Wood = H("#E3B98C"), WoodDark = H("#C29369"), WoodLine = H("#B98A60"), Trunk = H("#9C785C");
        static readonly Color Roof = H("#DE8A70"), RoofDark = H("#C9745D"), Glass = H("#CDE8F5"), Frame = H("#FFF8EE");
        static readonly Color LeafA = H("#86BD6E"), LeafB = H("#76B062"), LeafLight = H("#A3D18A");
        static readonly Color Pink = H("#F5A3C1"), Yellow = H("#F7DE7E"), Lilac = H("#C9B4E8"), White = H("#FFF9F0");
        static readonly Color Wall = H("#F6E7D6"), WallStripe = H("#EFDAC5"), Baseboard = H("#D7B08A");
        static readonly Color RugA = H("#F1A9A0"), RugB = H("#FBE3C9"), BedSheet = H("#FBF4E6"), Blanket = H("#9FC6E6"), BlanketB = H("#C3DDF1");

        // лисички
        static readonly Color FurOrange = H("#EE9450"), FurSilver = H("#BEC3CB"), FurSand = H("#EACB98"), FurWhite = H("#FFF8EF");
        static readonly Color FurDark = H("#5C4A42"), Eye = H("#2E2522");

        // ================= сцена =================

        Material mat, glowMat;
        Transform root;
        Camera cam;
        Light sun;

        const float S = 0.25f;               // воксел мира
        const int NX = 144, NY = 34, NZ = 144; // 36 x 8.5 x 36 метров

        float Terrain(float x, float z)
        {
            float n = Mathf.PerlinNoise(x * 0.06f + 3.1f, z * 0.06f + 7.7f) * 1.9f + Mathf.PerlinNoise(x * 0.17f + 1.3f, z * 0.17f + 5.2f) * 0.35f;
            float h = 0.6f + n;
            float d = new Vector2(x - 18f, (z - 17f) * 1.1f).magnitude;
            h = Mathf.Lerp(1.25f, h, Mathf.SmoothStep(0, 1, (d - 6.5f) / 4.5f));
            float dp = new Vector2(x - 27f, z - 8.5f).magnitude;
            h = Mathf.Lerp(0.35f, h, Mathf.SmoothStep(0, 1, (dp - 1.8f) / 2.2f));
            return h;
        }

        float PathOffset(float z) => Mathf.Sin(z * 0.35f) * 0.9f;
        bool OnPath(float x, float z) => z < 14f && z > 0.5f && Mathf.Abs(x - 18f - PathOffset(z)) < 0.75f;

        void BuildWorld()
        {
            var g = new VGrid(NX, NY, NZ, S);
            var heights = new int[NX, NZ];
            for (int x = 0; x < NX; x++)
                for (int z = 0; z < NZ; z++)
                {
                    float wx = (x + 0.5f) * S, wz = (z + 0.5f) * S;
                    int top = Mathf.Max(1, Mathf.RoundToInt(Terrain(wx, wz) / S));
                    heights[x, z] = top;
                    float patch = Mathf.PerlinNoise(wx * 0.25f, wz * 0.25f);
                    bool path = OnPath(wx, wz);
                    float pathEdge = Mathf.Abs(wx - 18f - PathOffset(wz));
                    for (int y = 0; y < top; y++)
                    {
                        Color c;
                        if (y == top - 1)
                        {
                            if (path) c = Pick(x, y, z, PathA, PathB, PathC);
                            else if (wz < 14.2f && wz > 0.3f && pathEdge < 1.3f) c = Vary(Color.Lerp(GrassDry, GrassA, (pathEdge - 0.75f) / 0.55f), x, y, z, 0.04f);
                            else c = Vary(patch > 0.62f ? GrassC : patch < 0.38f ? GrassB : GrassA, x, y, z, 0.045f);
                        }
                        else if (y >= top - 4) c = Vary(Hash(x, y, z * 3) > 0.85f ? SoilDark : Soil, x, y, z, 0.03f);
                        else c = Vary(Hash(x, y * 7, z) > 0.8f ? StoneDark : Stone, x, y, z, 0.03f);
                        g.Set(x, y, z, c);
                    }
                    // вода в пруду
                    int water = Mathf.RoundToInt(0.85f / S);
                    for (int y = top; y < water; y++)
                        g.Set(x, y, z, y == water - 1 ? Vary(Hash(x, 0, z) > 0.9f ? WaterLight : Water, x, y, z, 0.02f) : Water);
                }

            // цветы и кочки травы
            for (int i = 0; i < 900; i++)
            {
                int x = (int)(Hash(i, 1, 9) * NX), z = (int)(Hash(i, 2, 9) * NZ);
                float wx = (x + 0.5f) * S, wz = (z + 0.5f) * S;
                if (OnPath(wx, wz) || (wx > 13 && wx < 23.5f && wz > 13 && wz < 21.5f)) continue;
                int top = heights[x, z];
                if (top < Mathf.RoundToInt(0.85f / S)) continue;
                float r = Hash(i, 3, 9);
                if (i % 6 == 0) Flowers(new Vector3(wx, top * S, wz), i);
            }

            var hg = new VGrid(88, 66, 80, S / 2f, new Vector3(13f, 0, 12f));
            BuildHouse(hg);
            Mesh(hg, root, hg.origin, mat, "house", 64);
            Vector2[] trees = { new(5, 6), new(7, 27), new(31, 26), new(33, 5), new(24, 32), new(3, 16), new(32, 33), new(12, 33), new(15, 4) };
            int ti = 0;
            foreach (var t in trees) Tree(g, t, heights[g.I(t.x), g.I(t.y)] * S, ti++);
            // камни
            Vector2[] rocks = { new(24.5f, 11f), new(29.5f, 11.5f), new(10f, 9f) };
            foreach (var r in rocks)
            {
                float y0 = heights[g.I(r.x), g.I(r.y)] * S;
                var c = new Vector3(r.x, y0, r.y);
                g.Fill(c - Vector3.one * 0.8f, c + Vector3.one * 0.8f, p => ((p - c).x * (p - c).x + (p - c).y * (p - c).y * 2.2f + (p - c).z * (p - c).z) < 0.42f,
                    (p, x, y, z) => Vary(p.y > y0 + 0.2f ? Stone : StoneDark, x, y, z, 0.04f));
            }
            Mesh(g, root, Vector3.zero, mat, "world");
        }

        /// <summary>Кустик цветов и травинок из мелких вокселов (6 см): стебельки, лепестки крестиком, жёлтая серединка.</summary>
        void Flowers(Vector3 at, int seed)
        {
            const float fs = 0.0625f;
            var g = new VGrid(16, 10, 16, fs, at - new Vector3(0.5f, 0, 0.5f));
            Color[] petals = { Pink, Yellow, Lilac, White };
            int count = 2 + (int)(Hash(seed, 5, 1) * 4);
            for (int k = 0; k < count; k++)
            {
                int cx = 3 + (int)(Hash(seed, k, 2) * 10), cz = 3 + (int)(Hash(seed, k, 3) * 10);
                int h = 3 + (int)(Hash(seed, k, 4) * 4);
                bool flower = Hash(seed, k, 6) > 0.35f;
                for (int y = 0; y < h; y++) g.Set(cx, y, cz, Vary(LeafB, cx, y, cz, 0.04f));
                if (flower)
                {
                    var pc = petals[(int)(Hash(seed, k, 7) * petals.Length) % petals.Length];
                    g.Set(cx, h, cz, Yellow == pc ? H("#E9A93E") : Yellow);
                    g.Set(cx + 1, h, cz, pc); g.Set(cx - 1, h, cz, pc); g.Set(cx, h, cz + 1, pc); g.Set(cx, h, cz - 1, pc);
                    g.Set(cx, h + 1, cz, pc);
                }
                else
                {
                    // травинки веером
                    for (int b = 0; b < 3; b++)
                    {
                        int dx = b - 1;
                        for (int y = 0; y < h - 1 + b % 2; y++) g.Set(cx + (y > 2 ? dx : 0), y, cz + (b == 1 ? 1 : 0), Vary(b == 1 ? LeafA : LeafLight, cx, y, cz + b, 0.04f));
                    }
                }
            }
            Mesh(g, root, g.origin, mat, "flowers", 64);
        }

        void BuildHouse(VGrid g)
        {
            float x0 = 14f, x1 = 22f, z0 = 14f, z1 = 20f, fy = 1.25f, wh = 2.5f, t = g.s * 2f;
            // фундамент из камня
            g.Box(new Vector3(x0 - 0.25f, 0, z0 - 0.25f), new Vector3(x1 + 0.25f, fy - 0.01f, z1 + 0.25f),
                (p, x, y, z) => Vary(Pick(x, y, z, Stone, StoneDark, Stone), x, y, z, 0.03f));
            // пол
            g.Box(new Vector3(x0, fy - 0.25f, z0), new Vector3(x1, fy - 0.01f, z1), (p, x, y, z) => Vary(x % 4 == 0 ? WoodLine : Wood, x, y, z, 0.03f));
            // стены из досок
            System.Func<Vector3, int, int, int, Color> plank = (p, x, y, z) =>
            {
                bool corner = (p.x < x0 + 0.5f || p.x > x1 - 0.5f) && (p.z < z0 + 0.5f || p.z > z1 - 0.5f);
                if (corner) return Vary(WoodDark, x, y, z, 0.03f);
                if (y % 3 == 0) return Vary(WoodLine, x, y, z, 0.02f);
                if (((x + z) + (y / 3) * 11) % 23 == 0) return Vary(WoodLine, x, y, z, 0.02f);
                return Vary(Wood, x, y, z, 0.035f);
            };
            g.Box(new Vector3(x0, fy, z0), new Vector3(x1, fy + wh, z0 + t), plank);
            g.Box(new Vector3(x0, fy, z1 - t), new Vector3(x1, fy + wh, z1), plank);
            g.Box(new Vector3(x0, fy, z0), new Vector3(x0 + t, fy + wh, z1), plank);
            g.Box(new Vector3(x1 - t, fy, z0), new Vector3(x1, fy + wh, z1), plank);

            // окна: стекло в белой раме, под окнами ящики с цветами
            void Window(float a, float b, bool front, float zf)
            {
                var min = new Vector3(a, fy + 0.85f, zf - 0.01f);
                var max = new Vector3(b, fy + 1.85f, zf + t + 0.01f);
                g.Box(min, max, (p, x, y, z) =>
                {
                    bool frame = p.x < a + 0.13f || p.x > b - 0.13f || p.y < min.y + 0.13f || p.y > max.y - 0.13f || Mathf.Abs(p.x - (a + b) / 2) < 0.07f || Mathf.Abs(p.y - (min.y + max.y) / 2) < 0.05f;
                    return frame ? Frame : Vary(Glass, x, y, z, 0.02f);
                });
                if (front)
                {
                    g.Box(new Vector3(a, fy + 0.6f, zf - 0.3f), new Vector3(b, fy + 0.8f, zf - 0.01f), (p, x, y, z) => Vary(WoodDark, x, y, z, 0.03f));
                    g.Fill(new Vector3(a + 0.1f, fy + 0.8f, zf - 0.3f), new Vector3(b - 0.1f, fy + 1.1f, zf - 0.05f),
                        p => p.y < fy + 0.9f || Hash(g.I(p.x - g.origin.x), 0, g.I(p.z - g.origin.z)) > 0.35f,
                        (p, x, y, z) => p.y > fy + 0.96f ? Pick(x / 2, 1, z, Pink, Yellow, Lilac, White) : Vary(Hash(x, y, z) > 0.5f ? LeafA : LeafB, x, y, z, 0.04f));
                }
            }
            Window(14.75f, 16.25f, true, z0);
            Window(19.75f, 21.25f, true, z0);
            Window(17.25f, 18.75f, false, z1 - t);
            // двери
            g.Box(new Vector3(17.5f, fy, z0 - 0.01f), new Vector3(18.5f, fy + 1.75f, z0 + t + 0.01f),
                (p, x, y, z) => Mathf.Abs(p.x - 18.3f) < 0.13f && Mathf.Abs(p.y - (fy + 0.9f)) < 0.13f ? Yellow : Vary(x % 2 == 0 ? WoodDark : WoodLine, x, y, z, 0.03f));
            g.Box(new Vector3(17.25f, fy + 1.75f, z0 - 0.05f), new Vector3(18.75f, fy + 2.0f, z0 + t), (p, x, y, z) => Frame);
            // крыльцо
            g.Box(new Vector3(17f, fy - 0.25f, z0 - 1f), new Vector3(19f, fy - 0.01f, z0), (p, x, y, z) => Vary(WoodDark, x, y, z, 0.03f));

            // двускатная крыша со свесами: мелкие ступени дают почти ровный скат
            float ridgeZ = (z0 + z1) / 2f, half = (z1 - z0) / 2f + 0.6f, baseY = fy + wh;
            float RoofY(float z) => baseY + (half - Mathf.Abs(z - ridgeZ)) * 0.62f;
            g.Fill(new Vector3(x0 - 0.6f, baseY - 0.3f, ridgeZ - half), new Vector3(x1 + 0.6f, baseY + 3.2f, ridgeZ + half),
                p => p.y <= RoofY(p.z) && p.y > RoofY(p.z) - 0.5f,
                (p, x, y, z) => Vary(Mathf.Abs(p.z - ridgeZ) < 0.15f ? RoofDark : (z % 4 == 0 || (x + (z / 4) * 3) % 6 == 0 ? RoofDark : Roof), x, y, z, 0.035f));
            // фронтоны
            g.Fill(new Vector3(x0, baseY, z0), new Vector3(x1, baseY + 3.2f, z1),
                p => (p.x < x0 + t || p.x > x1 - t) && p.y < RoofY(p.z) - 0.2f,
                (p, x, y, z) => Vary(y % 3 == 0 ? WoodLine : Wood, x, y, z, 0.03f));
            // круглое окошко на фронтоне
            var ow = new Vector3(x0, baseY + 0.9f, ridgeZ);
            g.Fill(ow - new Vector3(0.1f, 0.5f, 0.5f), ow + new Vector3(0.3f, 0.5f, 0.5f),
                p => new Vector2(p.y - ow.y, p.z - ow.z).magnitude < 0.45f && p.x < x0 + t + 0.02f,
                (p, x, y, z) => new Vector2(p.y - ow.y, p.z - ow.z).magnitude > 0.3f ? Frame : Glass);
            // труба
            g.Box(new Vector3(20f, baseY + 0.5f, 18f), new Vector3(20.75f, baseY + 3.5f, 18.75f),
                (p, x, y, z) => Vary(y % 2 == 0 ? StoneDark : Stone, x, y, z, 0.04f));
        }

        void Tree(VGrid g, Vector2 pos, float groundY, int seed)
        {
            float trunkH = 2.2f + Hash(seed, 1, 1) * 0.8f;
            var basePos = new Vector3(pos.x, groundY, pos.y);
            g.Fill(basePos - new Vector3(0.5f, 0, 0.5f), basePos + new Vector3(0.5f, trunkH + 0.5f, 0.5f),
                p => new Vector2(p.x - pos.x, p.z - pos.y).magnitude < 0.32f + Mathf.Max(0, 0.5f - (p.y - groundY)) * 0.5f,
                (p, x, y, z) => Vary((x + z) % 3 == 0 ? WoodLine : Trunk, x, y, z, 0.03f));
            var top = basePos + Vector3.up * (trunkH + 0.9f);
            var blobs = new List<(Vector3 c, float r)>
            {
                (top, 1.45f), (top + new Vector3(0.9f, -0.4f, 0.3f), 1.05f), (top + new Vector3(-0.8f, -0.3f, -0.5f), 1.1f), (top + new Vector3(0.1f, 0.8f, 0.2f), 0.95f),
            };
            g.Fill(top - Vector3.one * 2.8f, top + Vector3.one * 2.8f, p =>
            {
                foreach (var b in blobs)
                {
                    float jitter = (Mathf.PerlinNoise(p.x * 1.7f + seed, p.z * 1.7f + p.y) - 0.5f) * 0.45f;
                    if ((p - b.c).magnitude < b.r + jitter) return true;
                }
                return false;
            }, (p, x, y, z) =>
            {
                float up = (p.y - top.y + 1.5f) / 3f;
                var c = up > 0.7f && Hash(x, y, z) > 0.4f ? LeafLight : Hash(x, y, z) > 0.5f ? LeafA : LeafB;
                return Vary(c, x, y, z, 0.04f);
            });
        }

        // ================= лисички из мелких вокселов =================

        static bool InEll(Vector3 p, Vector3 c, Vector3 r)
        {
            var d = p - c;
            return (d.x * d.x) / (r.x * r.x) + (d.y * d.y) / (r.y * r.y) + (d.z * d.z) / (r.z * r.z) <= 1f;
        }

        void Fox(Vector3 pos, float yaw, Color fur, Color light, Color dark, float scale = 1f, float ears = 1f, bool sleeping = false, bool kit = false)
        {
            const float fs = 0.04f;
            var g = new VGrid(22, 30, 42, fs);
            var off = new Vector3(-0.44f, 0, -0.95f); // локальный ноль в сетке
            float hs = kit ? 1.25f : 1f;
            Vector3 L(float x, float y, float z) => new Vector3(x, y, z) - off;

            float by = sleeping ? 0.2f : 0.4f;
            var headC = sleeping ? L(0.02f, 0.27f, 0.35f) : L(0, 0.66f, 0.36f);

            g.Fill(Vector3.zero, new Vector3(g.nx, g.ny, g.nz) * fs, p =>
            {
                if (InEll(p, L(0, by, 0), new Vector3(0.21f, 0.19f, 0.37f))) return true;              // тело
                if (InEll(p, headC, new Vector3(0.19f, 0.17f, 0.17f) * hs)) return true;                  // голова
                if (InEll(p, headC + new Vector3(0, -0.06f, 0.18f) * hs, new Vector3(0.075f, 0.065f, 0.12f) * hs)) return true; // мордочка
                for (int s = -1; s <= 1; s += 2)
                {
                    // уши — сужающиеся к кончику
                    var eb = headC + new Vector3(0.1f * s, 0.12f, -0.02f) * hs;
                    float eh = 0.2f * ears * hs;
                    float tt = (p.y - eb.y) / eh;
                    if (tt >= 0 && tt <= 1)
                    {
                        float cx = eb.x + s * 0.04f * tt * hs;
                        if (Mathf.Abs(p.x - cx) < 0.075f * hs * (1 - tt) + 0.01f && Mathf.Abs(p.z - eb.z) < 0.035f * hs * (1 - tt * 0.6f) + 0.012f) return true;
                    }
                    if (!sleeping)
                        for (int k = -1; k <= 1; k += 2)
                        {
                            var lc = L(0.11f * s, 0, 0.2f * k);
                            if (p.y < by - 0.05f && new Vector2(p.x - lc.x, p.z - lc.z).magnitude < 0.06f) return true; // лапы
                        }
                }
                // хвост
                Vector3[] tailC = sleeping
                    ? new[] { L(0.24f, 0.17f, -0.25f), L(0.31f, 0.17f, 0.05f), L(0.25f, 0.16f, 0.3f) }
                    : new[] { L(0, 0.44f, -0.42f), L(0, 0.52f, -0.62f), L(0, 0.6f, -0.78f) };
                float[] tr = { 0.1f, 0.135f, 0.115f };
                for (int i = 0; i < 3; i++) if ((p - tailC[i]).magnitude < tr[i]) return true;
                return false;
            }, (p, x, y, z) =>
            {
                var q = p + off;
                Color c = fur;
                // светлая грудка, щёки, мордочка, кончик хвоста
                if (InEll(p, L(0, by - 0.04f, 0.24f), new Vector3(0.16f, 0.17f, 0.16f))) c = light;
                if (InEll(p, headC + new Vector3(0, -0.08f, 0.06f) * hs, new Vector3(0.2f, 0.1f, 0.16f) * hs)) c = light;
                if (InEll(p, headC + new Vector3(0, -0.06f, 0.18f) * hs, new Vector3(0.08f, 0.07f, 0.13f) * hs)) c = light;
                if (sleeping ? (p - L(0.25f, 0.16f, 0.3f)).magnitude < 0.1f : q.z < -0.74f) c = light;
                // тёмные лапы и кончики ушей
                if (!sleeping && q.y < by - 0.1f) c = dark;
                if (p.y > headC.y + 0.12f * hs + 0.13f * ears * hs) c = dark;
                // нос и глаза
                var nose = headC + new Vector3(0, -0.03f, 0.3f) * hs;
                if ((p - nose).magnitude < 0.035f * hs) c = Eye;
                for (int s = -1; s <= 1; s += 2)
                {
                    var e = headC + new Vector3(0.075f * s, 0.035f, 0.14f) * hs;
                    if (sleeping ? (Mathf.Abs(p.y - e.y) < 0.012f && Mathf.Abs(p.x - e.x) < 0.035f && p.z > e.z - 0.02f) : (p - e).magnitude < 0.03f * hs) c = Eye;
                }
                return Vary(c, x, y, z, 0.035f);
            });

            var go = new GameObject("fox");
            go.transform.SetParent(root, false);
            Mesh(g, go.transform, off, mat, "fox", 64);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
        }

        // ================= комната в разрезе =================

        void BuildRoom(Vector3 at)
        {
            const float rs = 0.125f;
            var g = new VGrid(66, 26, 58, rs); // 8.25 x 3.25 x 7.25 м
            float W = 8f, D = 7f, wh = 3f;
            // пол из досок
            g.Box(new Vector3(0, 0, 0), new Vector3(W, 0.124f, D), (p, x, y, z) =>
            {
                int row = z / 3;
                bool seam = z % 3 == 0 || (x + row * 7) % 19 == 0;
                return Vary(seam ? WoodLine : (row % 2 == 0 ? Wood : H("#DDB385")), x, y, z, 0.025f);
            });
            // стены с обоями и плинтусом
            System.Func<Vector3, int, int, int, Color> wall = (p, x, y, z) =>
            {
                if (p.y < 0.4f) return Vary(Baseboard, x, y, z, 0.02f);
                return Vary(((p.x + p.z) * 2f % 1f) < 0.12f ? WallStripe : Wall, x, y, z, 0.015f);
            };
            g.Box(new Vector3(0, 0, D), new Vector3(W, wh, D + 0.24f), wall);
            g.Box(new Vector3(-0.24f, 0, 0), new Vector3(0, wh, D + 0.24f), wall);
            // окна
            foreach (var wx in new[] { 2f, 5.5f })
                g.Box(new Vector3(wx, 1.1f, D - 0.01f), new Vector3(wx + 1.25f, 2.3f, D + 0.1f), (p, x, y, z) =>
                    p.x < wx + 0.13f || p.x > wx + 1.12f || p.y < 1.23f || p.y > 2.17f || Mathf.Abs(p.x - wx - 0.625f) < 0.06f ? Frame : Glass);

            // круглый ковёр с кольцами
            var rc = new Vector3(4f, 0.125f, 3f);
            g.Fill(rc - new Vector3(1.6f, 0, 1.2f), rc + new Vector3(1.6f, 0.12f, 1.2f),
                p => new Vector2((p.x - rc.x) / 1.55f, (p.z - rc.z) / 1.15f).magnitude < 1f,
                (p, x, y, z) => Vary(((int)(new Vector2((p.x - rc.x) / 1.55f, (p.z - rc.z) / 1.15f).magnitude * 5f)) % 2 == 0 ? RugA : RugB, x, y, z, 0.02f));

            // кровать
            var b0 = new Vector3(0.4f, 0.125f, 3.6f);
            g.Box(b0, b0 + new Vector3(1.1f, 0.38f, 2.1f), (p, x, y, z) => Vary(WoodDark, x, y, z, 0.03f));
            g.Box(b0 + new Vector3(0, 0, 1.95f), b0 + new Vector3(1.1f, 0.95f, 2.1f), (p, x, y, z) => Vary(y % 2 == 0 ? WoodDark : WoodLine, x, y, z, 0.03f));
            g.Box(b0 + new Vector3(0.05f, 0.38f, 0.05f), b0 + new Vector3(1.05f, 0.5f, 1.95f), (p, x, y, z) => Vary(BedSheet, x, y, z, 0.015f));
            g.Box(b0 + new Vector3(0.0f, 0.5f, 0.05f), b0 + new Vector3(1.1f, 0.62f, 1.35f), (p, x, y, z) => Vary(((x / 2) + (z / 2)) % 2 == 0 ? Blanket : BlanketB, x, y, z, 0.015f));
            g.Box(b0 + new Vector3(0.2f, 0.5f, 1.45f), b0 + new Vector3(0.9f, 0.68f, 1.85f), (p, x, y, z) => Vary(White, x, y, z, 0.01f));

            // книжная полка
            var s0 = new Vector3(3.2f, 0.125f, 6.45f);
            g.Box(s0, s0 + new Vector3(1.4f, 1.9f, 0.5f), (p, x, y, z) =>
            {
                var l = p - s0;
                bool shelfBoard = l.y < 0.13f || ((l.y - 0.1f) % 0.6f) < 0.12f || l.x < 0.13f || l.x > 1.27f || l.z > 0.38f;
                if (shelfBoard) return Vary(WoodDark, x, y, z, 0.03f);
                if (l.z < 0.12f) return default;
                return Pick(x / 1, 3, 1, Pink, Blanket, Yellow, Lilac, LeafA, RugA);
            });

            // стол и стулья
            var t0 = new Vector3(5.6f, 0.125f, 2.2f);
            g.Box(t0 + new Vector3(0, 0.7f, 0), t0 + new Vector3(1.6f, 0.82f, 1f), (p, x, y, z) => Vary(Wood, x, y, z, 0.03f));
            foreach (var lx in new[] { 0.1f, 1.38f }) foreach (var lz in new[] { 0.1f, 0.78f })
                g.Box(t0 + new Vector3(lx, 0, lz), t0 + new Vector3(lx + 0.12f, 0.7f, lz + 0.12f), (p, x, y, z) => Vary(WoodDark, x, y, z, 0.03f));
            g.Box(t0 + new Vector3(0.3f, 0.82f, 0.35f), t0 + new Vector3(0.55f, 1.0f, 0.6f), (p, x, y, z) => Yellow); // свеча-фонарик
            foreach (var cxp in new[] { 0.2f, 0.95f })
            {
                var c0 = t0 + new Vector3(cxp, 0, -0.75f);
                g.Box(c0 + new Vector3(0, 0.4f, 0), c0 + new Vector3(0.5f, 0.5f, 0.5f), (p, x, y, z) => Vary(Pink, x, y, z, 0.02f));
                g.Box(c0 + new Vector3(0, 0, 0), c0 + new Vector3(0.1f, 0.4f, 0.1f), (p, x, y, z) => WoodDark);
                g.Box(c0 + new Vector3(0.4f, 0, 0), c0 + new Vector3(0.5f, 0.4f, 0.1f), (p, x, y, z) => WoodDark);
                g.Box(c0 + new Vector3(0, 0, 0), c0 + new Vector3(0.5f, 0.95f, 0.1f), (p, x, y, z) => p.y > c0.y + 0.5f || p.x < c0.x + 0.12f || p.x > c0.x + 0.38f ? Vary(WoodDark, x, y, z, 0.03f) : default);
            }

            // кухня: тумба с каменной столешницей
            var k0 = new Vector3(6.1f, 0.125f, 6.2f);
            g.Box(k0, k0 + new Vector3(1.8f, 0.85f, 0.75f), (p, x, y, z) => Vary((p.x - k0.x) % 0.6f < 0.13f ? WoodLine : Wood, x, y, z, 0.03f));
            g.Box(k0 + new Vector3(-0.05f, 0.85f, -0.05f), k0 + new Vector3(1.85f, 0.97f, 0.8f), (p, x, y, z) => Vary(Stone, x, y, z, 0.04f));

            // растение в горшке
            var pc = new Vector3(0.6f, 0.125f, 0.6f);
            g.Fill(pc - new Vector3(0.3f, 0, 0.3f), pc + new Vector3(0.3f, 0.45f, 0.3f), p => new Vector2(p.x - pc.x, p.z - pc.z).magnitude < 0.25f + (p.y - pc.y) * 0.15f,
                (p, x, y, z) => Vary(y % 2 == 0 ? Roof : RoofDark, x, y, z, 0.03f));
            var lc = pc + Vector3.up * 0.8f;
            g.Fill(lc - Vector3.one * 0.6f, lc + Vector3.one * 0.6f, p => (p - lc).magnitude < 0.45f + (Mathf.PerlinNoise(p.x * 4, p.z * 4 + p.y) - 0.5f) * 0.25f,
                (p, x, y, z) => Vary(Hash(x, y, z) > 0.5f ? LeafA : LeafLight, x, y, z, 0.04f));

            var go = new GameObject("room");
            go.transform.SetParent(root, false);
            Mesh(g, go.transform, at, mat, "room", 64);
        }

        // ================= запуск =================

        void Start() => StartCoroutine(Run());

        IEnumerator Shot(string name, Vector3 pos, Vector3 look, float fov = 40f)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(look);
            cam.fieldOfView = fov;
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            yield return new WaitForSeconds(0.4f);
        }

        IEnumerator Run()
        {
            Directory.CreateDirectory(outDir);
            foreach (var l in FindObjectsByType<Light>()) l.enabled = false;
            cam = new GameObject("LabCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = H("#F7D9C4");
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            sun = new GameObject("LabSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.color = H("#FFE6C9");
            sun.intensity = 1.05f;
            sun.transform.rotation = Quaternion.Euler(40, 210, 0);
            QualitySettings.shadowDistance = 60f;
            QualitySettings.antiAliasing = 4;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = H("#E9DCE6");
            RenderSettings.ambientEquatorColor = H("#D9C3B6");
            RenderSettings.ambientGroundColor = H("#9C8C7E");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = H("#F7D9C4");
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 90f;

            mat = new Material(Shader.Find("FoxLife/LabVoxel"));
            root = new GameObject("voxel-lab").transform;

            BuildWorld();
            float gy = 1.25f;
            Fox(new Vector3(16.9f, gy, 12.4f), 160, FurOrange, FurWhite, FurDark);
            Fox(new Vector3(18.4f, gy, 11.8f), 205, FurSilver, FurWhite, FurDark, 0.95f);
            Fox(new Vector3(20.6f, gy, 12.9f), 235, FurSand, FurWhite, H("#D9B07D"), 0.9f, 1.6f);
            Fox(new Vector3(15.9f, gy, 13.0f), 150, FurOrange, FurWhite, FurDark, 0.55f, 1f, false, true);

            var ro = new Vector3(80, 0, 0);
            BuildRoom(ro);
            Fox(ro + new Vector3(3.6f, 0.25f, 2.6f), 200, FurOrange, FurWhite, FurDark);
            Fox(ro + new Vector3(4.8f, 0.25f, 3.6f), 140, FurSilver, FurWhite, FurDark, 0.55f, 1f, false, true);
            Fox(ro + new Vector3(0.95f, 0.75f, 4.3f), 180, FurSand, FurWhite, H("#D9B07D"), 0.85f, 1.6f, true);

            yield return new WaitForSeconds(0.5f);
            yield return Shot("C_1_overview", new Vector3(-5, 20, -8), new Vector3(19, 2, 17));
            yield return Shot("C_2_cottage", new Vector3(11.5f, 6.5f, 3.5f), new Vector3(18.5f, 3f, 15f));
            yield return Shot("C_3_foxes", new Vector3(18.3f, 2.9f, 8.6f), new Vector3(18.2f, 1.7f, 12.6f), 42f);
            yield return Shot("C_4_room", new Vector3(84.2f, 8.5f, -5f), new Vector3(83.8f, 0.3f, 3.4f), 45f);
            Application.Quit();
        }
    }
}
