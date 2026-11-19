using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FoxLife.Lab
{
    /// <summary>
    /// Лаборатория стиля: пробные рендеры кубического стиля. В игру не входит.
    /// Запуск: FoxLife.exe -stylelab "папка" — строит сцену, делает снимки двух вариантов палитры и закрывается.
    /// </summary>
    public class StyleLab : MonoBehaviour
    {
        public static bool Active;
        static string outDir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Detect()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-stylelab") { Active = true; outDir = args[i + 1]; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Active && outDir != null) new GameObject("StyleLab").AddComponent<StyleLab>();
        }

        // ---------------- плитки ----------------

        public enum T
        {
            GrassTop, GrassSide, Soil, Stone, Planks, LogSide, LogTop, Leaves, Glass, Gravel, Water, Roof,
            Wool, Cobble, WoolRed, FurOrange, FurWhite, FurDark, FurSilver, FurSand, FlowerPink, FlowerYellow,
            PlanksDark, Lantern, Count,
        }

        class Palette
        {
            public string id, title;
            public readonly Color[] c = new Color[(int)T.Count];
            public float noise;
            public Color sky, sun, ambient;
            public float sunIntensity;
            public Vector3 sunEuler;
        }

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        static Palette Classic()
        {
            var p = new Palette { id = "A", title = "классический пиксельный", noise = 0.11f };
            void S(T t, string h) => p.c[(int)t] = H(h);
            S(T.GrassTop, "#5C9E3A"); S(T.GrassSide, "#86603A"); S(T.Soil, "#86603A"); S(T.Stone, "#808285");
            S(T.Planks, "#B5844C"); S(T.LogSide, "#5E4428"); S(T.LogTop, "#B88E58"); S(T.Leaves, "#3D7F31");
            S(T.Glass, "#BFE2F2"); S(T.Gravel, "#8D877E"); S(T.Water, "#3368C9"); S(T.Roof, "#A8452F");
            S(T.Wool, "#EDE6D6"); S(T.Cobble, "#76777A"); S(T.WoolRed, "#C23B35"); S(T.FurOrange, "#E0702A");
            S(T.FurWhite, "#F3EFE8"); S(T.FurDark, "#2E2420"); S(T.FurSilver, "#9AA0A8"); S(T.FurSand, "#D9B279");
            S(T.FlowerPink, "#E5689A"); S(T.FlowerYellow, "#F2CE45"); S(T.PlanksDark, "#7A5434"); S(T.Lantern, "#FFD27A");
            p.sky = H("#8FC3F2"); p.sun = H("#FFF4DE"); p.ambient = H("#8E959F"); p.sunIntensity = 1.15f; p.sunEuler = new Vector3(52, 140, 0);
            return p;
        }

        static Palette Pastel()
        {
            var p = new Palette { id = "B", title = "уютный пастельный", noise = 0.05f };
            void S(T t, string h) => p.c[(int)t] = H(h);
            S(T.GrassTop, "#94C77A"); S(T.GrassSide, "#B48D6E"); S(T.Soil, "#B48D6E"); S(T.Stone, "#B3B4B9");
            S(T.Planks, "#E2B98A"); S(T.LogSide, "#9A7458"); S(T.LogTop, "#E3C394"); S(T.Leaves, "#7FB56A");
            S(T.Glass, "#D7EEF7"); S(T.Gravel, "#C9C1B5"); S(T.Water, "#7FB2E5"); S(T.Roof, "#D9846B");
            S(T.Wool, "#FBF4E6"); S(T.Cobble, "#A9A8AC"); S(T.WoolRed, "#E58C84"); S(T.FurOrange, "#EE9450");
            S(T.FurWhite, "#FFF8EF"); S(T.FurDark, "#5C4A42"); S(T.FurSilver, "#BEC3CB"); S(T.FurSand, "#EACB98");
            S(T.FlowerPink, "#F5A3C1"); S(T.FlowerYellow, "#F7DE7E"); S(T.PlanksDark, "#B98E66"); S(T.Lantern, "#FFE2A6");
            p.sky = H("#F7D9C4"); p.sun = H("#FFE6C9"); p.ambient = H("#B4A9A6"); p.sunIntensity = 1.0f; p.sunEuler = new Vector3(38, 205, 0);
            return p;
        }

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static Color Shade(Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1);

        Texture2D MakeTile(T t, Palette p)
        {
            const int N = 16;
            var px = new Color[N * N];
            Color c = p.c[(int)t];
            float nz = p.noise;
            int seed = (int)t * 31 + 7;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = Hash(x, y, seed) - 0.5f;
                    Color o = Shade(c, 1f + r * nz * 2f);
                    switch (t)
                    {
                        case T.GrassSide:
                        {
                            int fringe = 13 - Mathf.FloorToInt(Hash(x, 0, 99) * 3f);
                            o = y >= fringe ? Shade(p.c[(int)T.GrassTop], 1f + r * nz * 2f) : Shade(c, 1f + r * nz * 2f);
                            break;
                        }
                        case T.Soil:
                            if (Hash(x, y, 5) > 0.9f) o = Shade(c, 0.78f);
                            break;
                        case T.Stone:
                        {
                            float blot = Hash(x / 4, y / 3, 11);
                            o = Shade(c, 0.9f + blot * 0.2f + r * nz);
                            if (Hash(x, y, 12) > 0.94f) o = Shade(c, 0.7f);
                            break;
                        }
                        case T.Planks:
                        case T.PlanksDark:
                        {
                            int row = y / 4;
                            int seam = (row % 2 == 0) ? 4 : 12;
                            float k = 0.94f + Hash(row, 0, 21) * 0.12f + (Hash(x, row, 22) - 0.5f) * nz;
                            if (y % 4 == 0 || x == seam) k = 0.72f;
                            o = Shade(c, k);
                            break;
                        }
                        case T.LogSide:
                            o = Shade(c, (x % 4 == 1 ? 0.8f : 1.05f) + r * nz);
                            break;
                        case T.LogTop:
                        {
                            int d = Mathf.Max(Mathf.Abs(x - 7), Mathf.Abs(y - 7));
                            o = d >= 7 ? Shade(p.c[(int)T.LogSide], 1f + r * nz) : Shade(c, d % 2 == 0 ? 0.92f : 1.04f);
                            break;
                        }
                        case T.Leaves:
                            o = Hash(x, y, 31) > 0.82f ? Shade(c, 0.7f) : Shade(c, 1f + r * nz * 3f);
                            break;
                        case T.Glass:
                        {
                            bool frame = x == 0 || y == 0 || x == N - 1 || y == N - 1;
                            bool shine = (x - y == 3 || x - y == 4) && x > 3 && x < 12;
                            o = frame ? Shade(p.c[(int)T.PlanksDark], 1f) : shine ? Color.Lerp(c, Color.white, 0.7f) : c;
                            break;
                        }
                        case T.Gravel:
                            o = Shade(c, 0.8f + Hash(x, y, 41) * 0.4f);
                            break;
                        case T.Water:
                            o = Shade(c, (Hash(x / 3, y, 51) > 0.85f ? 1.2f : 1f) + r * nz * 0.5f);
                            break;
                        case T.Roof:
                        {
                            int row = y / 4;
                            int off = (row % 2) * 2;
                            bool line = y % 4 == 3 || (x + off) % 4 == 0;
                            o = Shade(c, line ? 0.75f : 1f + r * nz);
                            break;
                        }
                        case T.Cobble:
                        {
                            int cx = (x + (y / 5 % 2) * 3) / 5, cy = y / 5;
                            bool gap = (x + (y / 5 % 2) * 3) % 5 == 0 || y % 5 == 0;
                            o = gap ? Shade(c, 0.65f) : Shade(c, 0.88f + Hash(cx, cy, 61) * 0.24f + r * nz * 0.5f);
                            break;
                        }
                        case T.Wool:
                        case T.WoolRed:
                            o = Shade(c, 1f + ((x + y) % 2 == 0 ? 0.03f : -0.03f) + r * nz * 0.5f);
                            break;
                        case T.Lantern:
                        {
                            bool frame = x <= 1 || y <= 1 || x >= N - 2 || y >= N - 2;
                            o = frame ? Shade(p.c[(int)T.FurDark], 1f) : Shade(c, 1f + r * 0.1f);
                            break;
                        }
                    }
                    px[y * N + x] = o;
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = t.ToString() };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ---------------- сетка с подсетками по плиткам ----------------

        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly Dictionary<int, List<int>> tris = new Dictionary<int, List<int>>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, int tile, float u, float vv)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, vv)); uv.Add(new Vector2(u, vv)); uv.Add(new Vector2(u, 0));
                if (!tris.TryGetValue(tile, out var l)) tris[tile] = l = new List<int>();
                l.Add(i); l.Add(i + 1); l.Add(i + 2); l.Add(i); l.Add(i + 2); l.Add(i + 3);
            }

            // dir: 0 +Y, 1 -Y, 2 +X, 3 -X, 4 +Z, 5 -Z
            public void Face(Vector3 o, Vector3 s, int dir, int tile, float density)
            {
                Vector3 P(float x, float y, float z) => o + new Vector3(x * s.x, y * s.y, z * s.z);
                switch (dir)
                {
                    case 0: Quad(P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), Vector3.up, tile, s.z * density, s.x * density); break;
                    case 1: Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3.down, tile, s.x * density, s.z * density); break;
                    case 2: Quad(P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), Vector3.right, tile, s.z * density, s.y * density); break;
                    case 3: Quad(P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), P(0, 0, 0), Vector3.left, tile, s.z * density, s.y * density); break;
                    case 4: Quad(P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), P(0, 0, 1), Vector3.forward, tile, s.x * density, s.y * density); break;
                    default: Quad(P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0), Vector3.back, tile, s.x * density, s.y * density); break;
                }
            }

            public GameObject Build(string name, Transform parent, Material[] mats)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetUVs(0, uv);
                var keys = new List<int>(tris.Keys);
                mesh.subMeshCount = keys.Count;
                var m = new Material[keys.Count];
                for (int i = 0; i < keys.Count; i++) { mesh.SetTriangles(tris[keys[i]], i); m[i] = mats[keys[i]]; }
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = m;
                return go;
            }
        }

        // ---------------- мир из блоков ----------------

        const int WX = 46, WY = 14, WZ = 46;
        T?[,,] world;
        Material[] mats;
        Transform root;
        readonly List<GameObject> props = new List<GameObject>();

        T Top(T b) => b == T.GrassTop ? T.GrassTop : b == T.LogSide ? T.LogTop : b;
        T Side(T b) => b == T.GrassTop ? T.GrassSide : b;
        T Bottom(T b) => b == T.GrassTop ? T.Soil : b == T.LogSide ? T.LogTop : b;

        bool Solid(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < WX && y < WY && z < WZ && world[x, y, z].HasValue;
        bool SeeThrough(T b) => b == T.Glass || b == T.Water || b == T.Leaves;

        void Set(int x, int y, int z, T? b) { if (x >= 0 && y >= 0 && z >= 0 && x < WX && y < WY && z < WZ) world[x, y, z] = b; }

        void BuildWorld()
        {
            world = new T?[WX, WY, WZ];
            // холмы
            for (int x = 0; x < WX; x++)
                for (int z = 0; z < WZ; z++)
                {
                    float hN = Mathf.PerlinNoise(x * 0.09f + 3.1f, z * 0.09f + 7.7f);
                    int h = 3 + Mathf.RoundToInt(hN * 4f);
                    bool flat = x >= 11 && x <= 28 && z >= 5 && z <= 27;
                    if (flat) h = 4;
                    for (int y = 0; y < h; y++)
                        world[x, y, z] = y == h - 1 ? T.GrassTop : y >= h - 3 ? T.Soil : T.Stone;
                }
            // пруд
            for (int x = 30; x <= 36; x++)
                for (int z = 13; z <= 19; z++)
                {
                    float d = new Vector2(x - 33, (z - 16) * 1.2f).magnitude;
                    if (d > 3.6f) continue;
                    for (int y = 2; y < WY; y++) world[x, y, z] = null;
                    world[x, 2, z] = T.Water;
                    world[x, 1, z] = T.Soil;
                }
            // домик 9x7 на высоте 4
            int x0 = 15, z0 = 17, w = 9, d0 = 7, fy = 4;
            for (int x = x0; x < x0 + w; x++)
                for (int z = z0; z < z0 + d0; z++)
                {
                    world[x, fy - 1, z] = T.Cobble;
                    bool edgeX = x == x0 || x == x0 + w - 1, edgeZ = z == z0 || z == z0 + d0 - 1;
                    if (!edgeX && !edgeZ) { world[x, fy - 1, z] = T.Planks; continue; }
                    for (int y = fy; y < fy + 3; y++)
                    {
                        bool corner = edgeX && edgeZ;
                        T b = corner ? T.LogSide : T.Planks;
                        if (!corner && y == fy + 1 && ((edgeZ && (x - x0) % 3 == 1) || (edgeX && (z - z0) % 3 == 2))) b = T.Glass;
                        world[x, y, z] = b;
                    }
                }
            int doorX = x0 + w / 2;
            world[doorX, fy, z0] = null; world[doorX, fy + 1, z0] = null;
            world[doorX - 1, fy + 1, z0] = T.Planks; world[doorX + 1, fy + 1, z0] = T.Planks;
            // двускатная крыша
            for (int k = 0; k <= 4; k++)
            {
                int y = fy + 3 + k;
                for (int x = x0 - 1; x <= x0 + w; x++)
                {
                    int za = z0 - 1 + k, zb = z0 + d0 - k;
                    if (za > zb) continue;
                    world[x, y, za] = T.Roof;
                    world[x, y, zb] = T.Roof;
                    if (x == x0 || x == x0 + w - 1)
                        for (int z = za + 1; z < zb; z++) world[x, y, z] = T.PlanksDark;
                }
            }
            // дорожка к двери
            for (int z = 4; z < z0; z++)
            {
                int y = 3;
                world[doorX, y, z] = T.Gravel;
                if (z % 2 == 0) world[doorX - 1, y, z] = T.Gravel;
            }
            // деревья
            var rnd = new System.Random(5);
            Vector2Int[] trees = { new(5, 8), new(8, 32), new(36, 30), new(40, 8), new(28, 38), new(4, 22), new(39, 40), new(12, 40), new(22, 6) };
            foreach (var t in trees)
            {
                int gy = 0;
                for (int y = WY - 1; y >= 0; y--) if (world[t.x, y, t.y].HasValue) { gy = y + 1; break; }
                int hgt = 4 + rnd.Next(2);
                for (int y = gy; y < gy + hgt; y++) world[t.x, y, t.y] = T.LogSide;
                int top = gy + hgt;
                for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dy = -2; dy <= 1; dy++)
                        {
                            if (Mathf.Abs(dx) + Mathf.Abs(dz) + Mathf.Abs(dy + 0.5f) > 3.6f) continue;
                            int xx = t.x + dx, yy = top + dy, zz = t.y + dz;
                            if (xx == t.x && zz == t.y && dy < 0) continue;
                            if (!Solid(xx, yy, zz)) Set(xx, yy, zz, T.Leaves);
                        }
            }
        }

        void MeshWorld()
        {
            var mb = new MB();
            Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.right, Vector3Int.left, new(0, 0, 1), new(0, 0, -1) };
            for (int x = 0; x < WX; x++)
                for (int y = 0; y < WY; y++)
                    for (int z = 0; z < WZ; z++)
                    {
                        var b = world[x, y, z];
                        if (!b.HasValue) continue;
                        for (int f = 0; f < 6; f++)
                        {
                            var nb = new Vector3Int(x, y, z) + dirs[f];
                            if (Solid(nb.x, nb.y, nb.z))
                            {
                                var nbt = world[nb.x, nb.y, nb.z].Value;
                                if (!SeeThrough(nbt) || nbt == b.Value) continue;
                            }
                            T tile = f == 0 ? Top(b.Value) : f == 1 ? Bottom(b.Value) : Side(b.Value);
                            mb.Face(new Vector3(x, y, z), Vector3.one, f, (int)tile, 1f);
                        }
                    }
            mb.Build("world", root, mats);
        }

        // ---------------- коробки: мебель и лисички ----------------

        const float BoxDensity = 2f; // 32 пикселя на метр у предметов и лисичек

        GameObject Box(Transform parent, Vector3 center, Vector3 size, T side, T? top = null, Vector3 euler = default)
        {
            var mb = new MB();
            var o = -size / 2f;
            for (int f = 0; f < 6; f++) mb.Face(o, size, f, (int)(f <= 1 && top.HasValue ? top.Value : side), BoxDensity);
            var go = mb.Build("box", parent, mats);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        Transform Group(string name, Vector3 pos, float yaw = 0)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.position = pos;
            t.rotation = Quaternion.Euler(0, yaw, 0);
            props.Add(t.gameObject);
            return t;
        }

        /// <summary>Своя кубическая лисичка: корпус, голова с мордочкой, уши с тёмными кончиками, пушистый хвост.</summary>
        void Fox(Vector3 pos, float yaw, T fur, T light, T dark, float scale = 1f, float ears = 1f, bool lying = false, bool kit = false)
        {
            var f = Group("fox", pos, yaw);
            var m = new GameObject("model").transform;
            m.SetParent(f, false);
            m.localScale = Vector3.one * scale;
            // спящая лисичка сворачивается клубочком: лапы спрятаны, голова опущена, хвост обёрнут сбоку
            float bodyY = lying ? 0.24f : 0.46f;
            float hs = kit ? 1.25f : 1f;
            Box(m, new Vector3(0, bodyY, 0), new Vector3(0.46f, 0.38f, 0.78f), fur);
            Box(m, new Vector3(0, bodyY - 0.06f, 0.37f), new Vector3(0.36f, 0.26f, 0.08f), light);
            var h = new GameObject("head").transform;
            h.SetParent(m, false);
            h.localPosition = lying ? new Vector3(0.05f, 0.34f, 0.5f) : new Vector3(0, 0.74f, 0.48f);
            if (lying) h.localRotation = Quaternion.Euler(12, -15, 0);
            h.localScale = Vector3.one * hs;
            Box(h, new Vector3(0, 0, 0), new Vector3(0.44f, 0.36f, 0.36f), fur);
            Box(h, new Vector3(0, -0.12f, 0.03f), new Vector3(0.46f, 0.12f, 0.32f), light);
            Box(h, new Vector3(0, -0.07f, 0.25f), new Vector3(0.2f, 0.14f, 0.16f), light);
            Box(h, new Vector3(0, -0.02f, 0.335f), new Vector3(0.08f, 0.06f, 0.02f), dark);
            Box(h, new Vector3(-0.11f, 0.05f, 0.181f), new Vector3(0.07f, 0.08f, 0.01f), dark);
            Box(h, new Vector3(0.11f, 0.05f, 0.181f), new Vector3(0.07f, 0.08f, 0.01f), dark);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(h, new Vector3(0.13f * s, 0.24f * ears, -0.04f), new Vector3(0.13f, 0.16f * ears, 0.06f), fur);
                Box(h, new Vector3(0.13f * s, 0.24f * ears + 0.08f * ears, -0.04f), new Vector3(0.131f, 0.05f, 0.062f), dark);
            }
            if (!lying)
            {
                for (int i = 0; i < 4; i++)
                    Box(m, new Vector3(i % 2 == 0 ? -0.13f : 0.13f, 0.14f, i < 2 ? 0.27f : -0.27f), new Vector3(0.12f, 0.28f, 0.12f), dark);
                Box(m, new Vector3(0, 0.52f, -0.58f), new Vector3(0.22f, 0.22f, 0.36f), fur, null, new Vector3(-18, 0, 0));
                Box(m, new Vector3(0, 0.6f, -0.84f), new Vector3(0.26f, 0.26f, 0.24f), fur, null, new Vector3(-18, 0, 0));
                Box(m, new Vector3(0, 0.66f, -1.0f), new Vector3(0.24f, 0.24f, 0.12f), light, null, new Vector3(-18, 0, 0));
            }
            else
            {
                Box(m, new Vector3(0.3f, 0.16f, -0.25f), new Vector3(0.24f, 0.22f, 0.5f), fur, null, new Vector3(0, -30, 0));
                Box(m, new Vector3(0.38f, 0.16f, 0.12f), new Vector3(0.24f, 0.22f, 0.3f), fur, null, new Vector3(0, 10, 0));
                Box(m, new Vector3(0.3f, 0.16f, 0.36f), new Vector3(0.22f, 0.2f, 0.16f), light, null, new Vector3(0, 30, 0));
            }
        }

        void Flower(Vector3 p, T color)
        {
            var g = Group("flower", p);
            Box(g, new Vector3(0, 0.12f, 0), new Vector3(0.05f, 0.24f, 0.05f), T.Leaves);
            Box(g, new Vector3(0, 0.27f, 0), new Vector3(0.16f, 0.1f, 0.16f), color);
            Box(g, new Vector3(0, 0.29f, 0), new Vector3(0.06f, 0.08f, 0.17f), T.FlowerYellow);
        }

        void Furnish(Vector3 o)
        {
            // кровать
            var bed = Group("bed", o + new Vector3(1.5f, 0, 4.0f));
            Box(bed, new Vector3(0, 0.2f, 0), new Vector3(1f, 0.32f, 1.9f), T.PlanksDark);
            Box(bed, new Vector3(0, 0.55f, 0.93f), new Vector3(1f, 0.7f, 0.12f), T.PlanksDark);
            Box(bed, new Vector3(0, 0.42f, 0), new Vector3(0.94f, 0.14f, 1.82f), T.Wool);
            Box(bed, new Vector3(0, 0.47f, -0.25f), new Vector3(0.97f, 0.1f, 1.25f), T.WoolRed);
            Box(bed, new Vector3(0, 0.52f, 0.62f), new Vector3(0.6f, 0.12f, 0.3f), T.Wool);
            // стол и стулья
            var table = Group("table", o + new Vector3(5.2f, 0, 2.5f));
            Box(table, new Vector3(0, 0.66f, 0), new Vector3(1.6f, 0.1f, 0.9f), T.Planks);
            for (int i = 0; i < 4; i++) Box(table, new Vector3(i % 2 == 0 ? -0.68f : 0.68f, 0.31f, i < 2 ? -0.32f : 0.32f), new Vector3(0.12f, 0.62f, 0.12f), T.LogSide);
            Box(table, new Vector3(-0.3f, 0.78f, 0), new Vector3(0.3f, 0.14f, 0.3f), T.Lantern);
            for (int s = -1; s <= 1; s += 2)
            {
                var ch = Group("chair", o + new Vector3(5.2f + 0.45f * s, 0, 1.6f), 0);
                Box(ch, new Vector3(0, 0.4f, 0), new Vector3(0.46f, 0.08f, 0.46f), T.Planks);
                for (int i = 0; i < 4; i++) Box(ch, new Vector3(i % 2 == 0 ? -0.18f : 0.18f, 0.18f, i < 2 ? -0.18f : 0.18f), new Vector3(0.07f, 0.36f, 0.07f), T.PlanksDark);
                Box(ch, new Vector3(0, 0.7f, -0.2f), new Vector3(0.46f, 0.55f, 0.07f), T.PlanksDark);
            }
            // книжная полка
            var shelf = Group("shelf", o + new Vector3(3.5f, 0, 5.75f));
            Box(shelf, new Vector3(0, 0.8f, 0), new Vector3(1.2f, 1.6f, 0.4f), T.PlanksDark);
            T[] books = { T.WoolRed, T.Glass, T.FlowerYellow, T.Leaves, T.FlowerPink };
            for (int r = 0; r < 3; r++)
                for (int b = 0; b < 6; b++)
                    Box(shelf, new Vector3(-0.45f + b * 0.18f, 0.3f + r * 0.48f, -0.1f), new Vector3(0.14f, 0.32f - (b % 3) * 0.04f, 0.26f), books[(b + r) % books.Length]);
            // ковёр
            var rug = Group("rug", o + new Vector3(3.6f, 0, 2.8f));
            Box(rug, new Vector3(0, 0.015f, 0), new Vector3(2.4f, 0.03f, 1.6f), T.WoolRed);
            Box(rug, new Vector3(0, 0.03f, 0), new Vector3(2.0f, 0.03f, 1.2f), T.FlowerPink);
            // кухня
            var kitchen = Group("kitchen", o + new Vector3(6.5f, 0, 5.5f));
            Box(kitchen, new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.9f, 0.8f), T.Cobble, T.Stone);
            Box(kitchen, new Vector3(-0.45f, 0.92f, 0), new Vector3(0.5f, 0.04f, 0.5f), T.FurDark);
            // цветок в горшке
            var pot = Group("pot", o + new Vector3(0.6f, 0, 0.6f));
            Box(pot, new Vector3(0, 0.2f, 0), new Vector3(0.4f, 0.4f, 0.4f), T.Roof);
            Box(pot, new Vector3(0, 0.6f, 0), new Vector3(0.5f, 0.45f, 0.5f), T.Leaves);
        }

        /// <summary>Комната в разрезе, как в игре: пол, две стены, мебель, лисички.</summary>
        void BuildRoom(Vector3 o)
        {
            var mb = new MB();
            for (int x = 0; x < 8; x++)
                for (int z = 0; z < 7; z++)
                {
                    mb.Face(o + new Vector3(x, -1, z), Vector3.one, 0, (int)T.Planks, 1f);
                }
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    T t = y == 1 && (x == 2 || x == 5) ? T.Glass : T.Planks;
                    for (int f = 0; f < 6; f++) mb.Face(o + new Vector3(x, y, 7), Vector3.one, f, (int)(x == 0 || x == 7 ? (f <= 1 ? T.LogTop : T.LogSide) : t), 1f);
                }
                for (int z = 0; z < 7; z++)
                {
                    T t = y == 1 && z == 3 ? T.Glass : T.Planks;
                    for (int f = 0; f < 6; f++) mb.Face(o + new Vector3(-1, y, z), Vector3.one, f, (int)t, 1f);
                }
            }
            mb.Build("room", root, mats);
            Furnish(o);
        }

        // ---------------- запуск ----------------

        Camera cam;
        Light sun;

        void Start() => StartCoroutine(Run());

        void Setup(Palette p)
        {
            if (root != null) Destroy(root.gameObject);
            props.Clear();
            root = new GameObject("lab-" + p.id).transform;
            mats = new Material[(int)T.Count];
            var sh = Shader.Find("FoxLife/LabBlock");
            for (int i = 0; i < mats.Length; i++)
            {
                var m = new Material(sh) { mainTexture = MakeTile((T)i, p) };
                m.SetFloat("_Glossiness", (T)i == T.Water || (T)i == T.Glass ? 0.6f : 0.05f);
                if ((T)i == T.Lantern) m.SetColor("_EmissionColor", Color.white * 0.9f);
                mats[i] = m;
            }
            BuildWorld();
            MeshWorld();

            // лисички во дворе
            Fox(new Vector3(17.2f, 4, 13.5f), 160, T.FurOrange, T.FurWhite, T.FurDark);
            Fox(new Vector3(18.6f, 4, 12.8f), 205, T.FurSilver, T.FurWhite, T.FurDark, 0.95f);
            Fox(new Vector3(21.4f, 4, 14.2f), 230, T.FurSand, T.FurWhite, T.FurSand, 0.9f, 1.6f);
            Fox(new Vector3(18.1f, 4, 14.5f), 150, T.FurOrange, T.FurWhite, T.FurDark, 0.55f, 1f, false, true);
            Flower(new Vector3(14.5f, 4, 15.5f), T.FlowerPink);
            Flower(new Vector3(15.3f, 4, 14.6f), T.FlowerYellow);
            Flower(new Vector3(23.6f, 4, 15.2f), T.FlowerPink);
            Flower(new Vector3(24.5f, 4, 14.1f), T.WoolRed);
            Flower(new Vector3(22.8f, 4, 13.2f), T.FlowerYellow);

            // комната в разрезе — в стороне от мира
            var ro = new Vector3(80, 0, 0);
            BuildRoom(ro);
            Fox(ro + new Vector3(3.2f, 0, 2.4f), 200, T.FurOrange, T.FurWhite, T.FurDark);
            Fox(ro + new Vector3(4.4f, 0, 3.4f), 140, T.FurSilver, T.FurWhite, T.FurDark, 0.55f, 1f, false, true);
            Fox(ro + new Vector3(1.45f, 0.5f, 3.6f), 180, T.FurSand, T.FurWhite, T.FurSand, 0.85f, 1.6f, true);

            cam.backgroundColor = p.sky;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = p.sky;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 95f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = p.ambient;
            sun.color = p.sun;
            sun.intensity = p.sunIntensity;
            sun.transform.rotation = Quaternion.Euler(p.sunEuler);
        }

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
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 200f;
            sun = new GameObject("LabSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            QualitySettings.shadowDistance = 70f;
            QualitySettings.antiAliasing = 4;

            foreach (var p in new[] { Classic(), Pastel() })
            {
                Setup(p);
                yield return new WaitForSeconds(0.5f);
                yield return Shot($"{p.id}_1_overview", new Vector3(-6, 22, -10), new Vector3(22, 3, 20));
                yield return Shot($"{p.id}_2_cottage", new Vector3(12, 8.5f, 4), new Vector3(19.5f, 5, 17));
                yield return Shot($"{p.id}_3_foxes", new Vector3(19.2f, 5.4f, 9.4f), new Vector3(19.2f, 4.4f, 13.8f), 40f);
                yield return Shot($"{p.id}_4_room", new Vector3(84, 8.5f, -5.5f), new Vector3(83.5f, 0.3f, 3.2f), 45f);
            }
            Application.Quit();
        }
    }
}
