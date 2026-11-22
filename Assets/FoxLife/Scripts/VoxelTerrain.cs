using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Земля из вокселов по 25 см, хранится картой высот: у каждого столбика — высота верха и вид поверхности.
    /// Долина с улицами и участками, пригорки, горы со снежными вершинами и скалами, лес, ручей и пруд.
    /// Цвет каждого воксела считается на лету (трава, земля, камень, снег), затенение углов — в вершинах.
    /// </summary>
    public class VoxelTerrain : MonoBehaviour
    {
        public const float S = 0.25f;
        const float MinX = Town.MinX, MinZ = Town.MinZ, SizeX = Town.W, SizeZ = Town.H, MinY = -2f;
        const int NY = 80;
        const float WaterY = -0.25f;
        int NX, NZ;
        short[,] top;        // число занятых вокселов в столбике (верх = MinY + top * S)
        byte[,] surf;        // вид поверхности
        float[,] offset;     // правки рельефа игроком (метры), поверх природной формы
        float[,] baseH;      // форма земли города (считается один раз)

        float BaseH(int x, int z) => baseH[Mathf.Clamp(x, 0, NX - 1), Mathf.Clamp(z, 0, NZ - 1)];
        Material mat;

        enum Surf : byte { Grass, Road, Curb, Gravel, Path, Sand, Plaza, Forest, Rock, Snow, Wild, Meadow, PathX }

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        static readonly Color GrassA = H("#9ACB7C"), GrassB = H("#8BC071"), GrassC = H("#A9D488");
        static readonly Color Soil = H("#B8906F"), SoilDark = H("#A27D60"), Stone = H("#B7B5B8"), StoneDark = H("#9C9AA0");
        static readonly Color PathA = H("#E2D6C1"), PathB = H("#D4C6AE"), PathC = H("#C9BBA3");
        static readonly Color Water = H("#86BCE6"), WaterLight = H("#A9D2F0");
        static readonly Color LeafA = H("#86BD6E"), LeafB = H("#76B062"), LeafLight = H("#A3D18A");
        static readonly Color Pink = H("#F5A3C1"), Yellow = H("#F7DE7E"), Lilac = H("#C9B4E8"), White = H("#FFF9F0"), Orange = H("#E9A93E");
        static readonly Color RoadA = H("#CDBFAE"), RoadB = H("#BBAE9D"), RoadC = H("#D8CBB9"), RoadD = H("#C4B2A6"), Curb = H("#E6DDD0");
        static readonly Color PlazaA = H("#E3D3BE"), PlazaB = H("#CDB3A0"), Gravel = H("#DCCFB8"), GravelB = H("#CFC0A6");
        static readonly Color WildPink = H("#F3A6C4"), WildYellow = H("#F6DC74"), WildWhite = H("#FFF8EE"), WildBlue = H("#A9C3F0"), Moss = H("#7DB466"), Sand = H("#E3D2A8");
        static readonly Color ForestA = H("#6FA457"), ForestB = H("#7AAE5E"), Needles = H("#8A7A55"), Fern = H("#5E9448");
        static readonly Color RockA = H("#BDB8B4"), RockB = H("#A9A39E"), RockC = H("#C9C4BE"), SnowA = H("#F7F9FC"), SnowB = H("#E8EEF6");

        public static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static Color Vary(Color c, int x, int y, int z, float a = 0.045f)
        {
            float r = (Hash(x, y, z) - 0.5f) * 2f * a;
            return new Color(Mathf.Clamp01(c.r + r), Mathf.Clamp01(c.g + r * 0.9f), Mathf.Clamp01(c.b + r * 0.8f), 1f);
        }

        static Color Pick(int x, int y, int z, params Color[] cs) => cs[Mathf.Min(cs.Length - 1, (int)(Hash(x, y, z) * cs.Length))];

        public static float Height(float x, float z) => Town.Height(x, z);

        /// <summary>Цвет мощёной поверхности: камешки 2×2 вокселя со сдвигом рядов, на площади — кольца.</summary>
        static Color Paving(int x, int y, int z, float wx, float wz, bool plaza)
        {
            if (plaza)
            {
                float r = new Vector2(wx - Town.SquareCenter.x, wz - Town.SquareCenter.y).magnitude;
                if (r < Town.PlazaR)
                {
                    int ring = Mathf.FloorToInt(r / 0.75f);
                    float ang = Mathf.Atan2(wz - Town.SquareCenter.y, wx - Town.SquareCenter.x);
                    int seg = Mathf.FloorToInt((ang + Mathf.PI) / (Mathf.PI * 2f) * (8 + ring * 6));
                    Color baseC = ring % 2 == 0 ? PlazaA : PlazaB;
                    if (r > Town.PlazaR - 0.35f) baseC = Curb;
                    return Vary(baseC, seg, ring, 1, 0.035f);
                }
            }
            int bz = z >> 1;
            int bx = (x + (bz & 1)) >> 1;
            var c = Pick(bx, 0, bz, RoadA, RoadB, RoadC, RoadD);
            return Vary(c, x, y, z, 0.025f);
        }

        // ---------- клетки под домами ----------

        float[,] houseFloor;   // высота пола первого этажа в клетке города (NaN — нет комнаты)
        readonly Dictionary<Vector2Int, GameObject> chunks = new Dictionary<Vector2Int, GameObject>();
        const int Chunk = 64;

        bool InHouse(float x, float z, out float floor)
        {
            floor = 0;
            if (houseFloor == null) return false;
            int ix = Mathf.FloorToInt(x) - Town.MinX, iz = Mathf.FloorToInt(z) - Town.MinZ;
            if (ix < 0 || iz < 0 || ix >= Town.W || iz >= Town.H) return false;
            floor = houseFloor[ix, iz];
            return !float.IsNaN(floor);
        }

        bool InHouse(float x, float z) => InHouse(x, z, out _);

        /// <summary>Земля под комнатами первого этажа — чуть ниже пола (на своей высоте у каждого дома).</summary>
        public void SetLowered(RectInt rect, System.Func<int, int, float> roomFloor)
        {
            if (houseFloor == null)
            {
                houseFloor = new float[Town.W, Town.H];
                for (int i = 0; i < Town.W; i++) for (int k = 0; k < Town.H; k++) houseFloor[i, k] = float.NaN;
            }
            int n = Mathf.RoundToInt(1f / S);
            for (int x = rect.xMin; x < rect.xMax; x++)
                for (int z = rect.yMin; z < rect.yMax; z++)
                {
                    int ix = x - Town.MinX, iz = z - Town.MinZ;
                    if (ix < 0 || iz < 0 || ix >= Town.W || iz >= Town.H) continue;
                    float v = roomFloor(x, z);
                    float old = houseFloor[ix, iz];
                    if ((float.IsNaN(v) && float.IsNaN(old)) || v == old) continue;
                    houseFloor[ix, iz] = v;
                    if (top == null) continue;
                    int vx0 = Mathf.RoundToInt((x - MinX) / S), vz0 = Mathf.RoundToInt((z - MinZ) / S);
                    for (int i = vx0; i < vx0 + n; i++)
                        for (int k = vz0; k < vz0 + n; k++)
                            if (i >= 0 && k >= 0 && i < NX && k < NZ) FillColumn(i, k);
                    for (int dx = -1; dx <= n; dx += n + 1)
                        for (int dz = -1; dz <= n; dz += n + 1)
                        {
                            int i = Mathf.Clamp(vx0 + dx, 0, NX - 1), k = Mathf.Clamp(vz0 + dz, 0, NZ - 1);
                            Remesh((i / Chunk) * Chunk, (k / Chunk) * Chunk);
                        }
                }
        }

        // ---------- столбики ----------

        void FillColumn(int x, int z)
        {
            float wx = MinX + (x + 0.5f) * S, wz = MinZ + (z + 0.5f) * S;
            float h;
            bool house = InHouse(wx, wz, out float floor);
            h = house ? floor - 0.25f : baseH[x, z] + offset[x, z];
            int t = Mathf.Clamp(Mathf.RoundToInt((h - MinY) / S), 1, NY - 1);
            top[x, z] = (short)t;
            Surf s = Surf.Grass;
            if (!house)
            {
                int road = Town.StreetKind(wx, wz);
                int lotId = Town.LotIdAt(wx, wz);
                var kind = lotId >= 0 ? Town.Lots[lotId].kind : LotKind.Home;
                bool square = lotId >= 0 && (kind == LotKind.Square || kind == LotKind.Market);
                float creek = Town.CreekDist(wx, wz), pond = Town.PondDist(wx, wz);
                bool bank = creek < Town.Creek.half + 0.9f || pond < Town.PondR + 0.9f;
                float tY = MinY + t * S;
                if (road == 1 || square) s = kind == LotKind.Square && lotId >= 0 ? Surf.Plaza : Surf.Road;
                else if (road == 2) s = Surf.Curb;
                else if (road == 3) s = Surf.Gravel;
                else if (Town.OnLotPath(wx, wz)) s = Surf.Path;
                else if (tY < -0.3f || (bank && tY < 0.1f)) s = Surf.Sand;
                else if (lotId >= 0 && tY > Town.SnowLine - 1.5f + (Mathf.PerlinNoise(wx * 0.15f, wz * 0.15f) - 0.5f) * 1.5f) s = Surf.Snow;   // площадка в горах — под снегом, как всё вокруг
                else if (lotId >= 0) s = Hash(x, 41, z) < Mathf.SmoothStep(0, 1, Town.ForestAmount(wx, wz)) ? Surf.Forest : Surf.Grass;
                else
                {
                    // крутизна — по соседям (камень на обрывах)
                    float slope = Mathf.Abs(BaseH(x + 4, z) - BaseH(x - 4, z)) + Mathf.Abs(BaseH(x, z + 4) - BaseH(x, z - 4));
                    float snow = Town.SnowLine + (Mathf.PerlinNoise(wx * 0.15f, wz * 0.15f) - 0.5f) * 1.5f;
                    if (tY > snow) s = Surf.Snow;
                    else if (slope > 2.4f && Town.MountainMask(wx, wz) > 0.2f) s = Surf.Rock;
                    else if (Hash(x, 41, z) < Mathf.SmoothStep(0, 1, Town.ForestAmount(wx, wz))) s = Surf.Forest;
                    else
                    {
                        float meadow = Mathf.PerlinNoise(wx * 0.11f + 40f, wz * 0.11f + 9f);
                        s = meadow > 0.62f && Hash(x, 77, z) < (meadow - 0.62f) * 0.35f ? Surf.Wild : Surf.Grass;
                    }
                }
            }
            if (!house && paint != null && paint[x, z] != 0)
            {
                switch ((Paint)paint[x, z])
                {
                    case Paint.Road: s = Surf.Road; break;
                    case Paint.Stones: s = Surf.Path; break;
                    case Paint.StonesX: s = Surf.PathX; break;
                    case Paint.Path: s = Surf.Gravel; break;
                    case Paint.Forest: s = Surf.Forest; break;
                    case Paint.Grass:
                    {
                        float meadow = Mathf.PerlinNoise(wx * 0.11f + 40f, wz * 0.11f + 9f);
                        s = meadow > 0.62f && Hash(x, 77, z) < (meadow - 0.62f) * 0.35f ? Surf.Wild : Surf.Grass;
                        break;
                    }
                }
            }
            // у нарисованной дороги (и у старой, рядом с которой рисовали) — бортик по краю
            if (!house && paint != null && s == Surf.Road && (paint[x, z] != 0 || NearPaint(x, z)))
                for (int f = 2; f < 6; f++)
                    if (!RoadCol(x + N[f].x, z + N[f].z)) { s = Surf.Curb; break; }
            surf[x, z] = (byte)s;
        }

        bool NearPaint(int x, int z)
        {
            for (int f = 2; f < 6; f++)
            {
                int i = x + N[f].x, k = z + N[f].z;
                if (i >= 0 && k >= 0 && i < NX && k < NZ && paint[i, k] != 0) return true;
            }
            return false;
        }

        bool RoadCol(int i, int k)
        {
            if (i < 0 || k < 0 || i >= NX || k >= NZ) return true;
            if (paint[i, k] != 0) return paint[i, k] == (byte)Paint.Road;
            float wx = MinX + (i + 0.5f) * S, wz = MinZ + (k + 0.5f) * S;
            if (InHouse(wx, wz)) return true;
            int lot = Town.LotIdAt(wx, wz);
            if (lot >= 0 && (Town.Lots[lot].kind == LotKind.Square || Town.Lots[lot].kind == LotKind.Market)) return true;
            int sk = Town.StreetKind(wx, wz);
            return sk == 1 || sk == 2;
        }

        // ---------- покраска земли: дороги, тропинки, газон, подлесок ----------

        public enum Paint : byte { None, Road, Path, Grass, Forest, Stones, StonesX }
        byte[,] paint;

        // ---------- отмена: журнал столбиков за мазок кисти ----------

        /// <summary>Пока не null — запоминаем прежнее состояние каждого изменённого столбика (x — смещение высоты, y — покраска).</summary>
        public Dictionary<int, Vector2> undoLog;

        void LogColumn(int i, int k)
        {
            if (undoLog == null) return;
            int idx = i * NZ + k;
            if (!undoLog.ContainsKey(idx)) undoLog[idx] = new Vector2(offset[i, k], paint[i, k]);
        }

        public Vector2 ColumnState(int idx) => new Vector2(offset[idx / NZ, idx % NZ], paint[idx / NZ, idx % NZ]);

        /// <summary>Вернуть столбикам состояние; возвращает затронутый прямоугольник (в клетках мира).</summary>
        public RectInt SetColumns(List<(int idx, Vector2 state)> cols)
        {
            int i0 = int.MaxValue, i1 = int.MinValue, k0 = int.MaxValue, k1 = int.MinValue;
            foreach (var (idx, st) in cols)
            {
                int i = idx / NZ, k = idx % NZ;
                offset[i, k] = st.x;
                paint[i, k] = (byte)Mathf.RoundToInt(st.y);
                i0 = Mathf.Min(i0, i); i1 = Mathf.Max(i1, i); k0 = Mathf.Min(k0, k); k1 = Mathf.Max(k1, k);
            }
            if (i0 > i1) return new RectInt(0, 0, 0, 0);
            for (int i = Mathf.Max(0, i0 - 1); i <= Mathf.Min(NX - 1, i1 + 1); i++)
                for (int k = Mathf.Max(0, k0 - 1); k <= Mathf.Min(NZ - 1, k1 + 1); k++)
                    FillColumn(i, k);
            for (int cx = (Mathf.Max(0, i0 - 1) / Chunk) * Chunk; cx <= Mathf.Min(NX - 1, i1 + 1); cx += Chunk)
                for (int cz = (Mathf.Max(0, k0 - 1) / Chunk) * Chunk; cz <= Mathf.Min(NZ - 1, k1 + 1); cz += Chunk)
                    Remesh(cx, cz);
            float x0 = MinX + i0 * S, x1 = MinX + (i1 + 1) * S, z0 = MinZ + k0 * S, z1 = MinZ + (k1 + 1) * S;
            foreach (var f in flowers)
                if (f.go != null && f.p.x > x0 - 1 && f.p.x < x1 + 1 && f.p.z > z0 - 1 && f.p.z < z1 + 1)
                    f.go.transform.position = new Vector3(0, GroundAt(f.p.x, f.p.z), 0);
            return new RectInt(Mathf.FloorToInt(x0) - 2, Mathf.FloorToInt(z0) - 2, Mathf.CeilToInt(x1 - x0) + 4, Mathf.CeilToInt(z1 - z0) + 4);
        }

        public bool IsRoad(float wx, float wz)
        {
            var surface = PaintAt(wx, wz);
            return surface != Paint.None ? surface == Paint.Road : Town.OnRoad(wx, wz);
        }

        internal Paint PaintAt(float wx, float wz)
        {
            int i = Mathf.FloorToInt((wx - MinX) / S), k = Mathf.FloorToInt((wz - MinZ) / S);
            return paint != null && i >= 0 && k >= 0 && i < NX && k < NZ ? (Paint)paint[i, k] : Paint.None;
        }

        /// <summary>Кисть покраски: края кисти чуть рваные, чтобы дорожки не выглядели нарисованными по линейке.</summary>
        public bool PaintBrush(Vector3 p, float radius, Paint v, bool spray = false,
            System.Func<float, float, bool> allow = null, Vector3? from = null)
        {
            var a = from.GetValueOrDefault(p);
            var span = new Vector2(p.x - a.x, p.z - a.z);
            float length = span.sqrMagnitude;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, p.x) - radius - MinX) / S));
            int i1 = Mathf.Min(NX - 1, Mathf.CeilToInt((Mathf.Max(a.x, p.x) + radius - MinX) / S));
            int k0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, p.z) - radius - MinZ) / S));
            int k1 = Mathf.Min(NZ - 1, Mathf.CeilToInt((Mathf.Max(a.z, p.z) + radius - MinZ) / S));
            bool paving = v == Paint.Stones || v == Paint.StonesX;
            bool changed = false;
            for (int i = i0; i <= i1; i++)
                for (int k = k0; k <= k1; k++)
                {
                    float wx = MinX + (i + 0.5f) * S, wz = MinZ + (k + 0.5f) * S;
                    var q = new Vector2(wx - a.x, wz - a.z);
                    if (length > 0.001f) q -= span * Mathf.Clamp01(Vector2.Dot(q, span) / length);
                    float d = q.magnitude;
                    if (d > radius - (v == Paint.Road || paving || v == Paint.None ? 0f : Hash(i, 61, k) * 0.35f) || InHouse(wx, wz)) continue;
                    if (paving && IsWaterColumn(i, k) || allow != null && !allow(wx, wz)) continue;
                    if (v == Paint.None && paint[i, k] != (byte)Paint.Stones && paint[i, k] != (byte)Paint.StonesX) continue;
                    // спрей: редкие точки, гуще к середине — мягкий переход (как опушка между газоном и подлеском)
                    if (spray && Random.value > 0.18f * (1f - d / radius) + 0.02f) continue;
                    if (paint[i, k] == (byte)v) continue;
                    LogColumn(i, k);
                    paint[i, k] = (byte)v;
                    FillColumn(i, k);
                    changed = true;
                }
            if (!changed) return false;
            // соседи по краю — бортики могли появиться или исчезнуть
            for (int i = Mathf.Max(0, i0 - 1); i <= Mathf.Min(NX - 1, i1 + 1); i++)
                for (int k = Mathf.Max(0, k0 - 1); k <= Mathf.Min(NZ - 1, k1 + 1); k++)
                    if (i == i0 - 1 || i == i1 + 1 || k == k0 - 1 || k == k1 + 1) FillColumn(i, k);
            for (int cx = (Mathf.Max(0, i0 - 1) / Chunk) * Chunk; cx <= Mathf.Min(NX - 1, i1 + 1); cx += Chunk)
                for (int cz = (Mathf.Max(0, k0 - 1) / Chunk) * Chunk; cz <= Mathf.Min(NZ - 1, k1 + 1); cz += Chunk)
                    Remesh(cx, cz);
            return true;
        }

        public List<Vector3> PaintData()
        {
            var l = new List<Vector3>();
            if (paint == null) return l;
            for (int i = 0; i < NX; i++)
                for (int k = 0; k < NZ; k++)
                    if (paint[i, k] != 0) l.Add(new Vector3(i, k, paint[i, k]));
            return l;
        }

        public void LoadPaint(List<Vector3> data)
        {
            bool any = data != null && data.Count > 0;
            if (!any) foreach (var b in paint) if (b != 0) { any = true; break; }
            if (!any) return;
            System.Array.Clear(paint, 0, paint.Length);
            if (data != null)
                foreach (var d in data)
                {
                    int i = (int)d.x, k = (int)d.y;
                    if (i >= 0 && k >= 0 && i < NX && k < NZ) paint[i, k] = (byte)Mathf.Clamp((int)d.z, 0, 6);
                }
            for (int x = 0; x < NX; x++) for (int z = 0; z < NZ; z++) FillColumn(x, z);
            for (int cx = 0; cx < NX; cx += Chunk)
                for (int cz = 0; cz < NZ; cz += Chunk)
                    Remesh(cx, cz);
        }

        /// <summary>Цвет воксела: верх — по виду поверхности, ниже — земля, ещё ниже — камень.</summary>
        Color32 ColorAt(int x, int y, int z, int t, Surf s)
        {
            int depth = t - 1 - y;
            if (s == Surf.Snow) return depth < 2 ? Vary(depth == 0 ? SnowA : SnowB, x, y, z, 0.02f) : Vary(Hash(x, y, z) > 0.5f ? RockA : RockB, x, y, z, 0.03f);
            if (s == Surf.Rock) return Vary(Pick(x, y / 2, z, RockA, RockB, RockC), x, y, z, 0.03f);
            if (depth == 0)
            {
                float wx = MinX + (x + 0.5f) * S, wz = MinZ + (z + 0.5f) * S;
                switch (s)
                {
                    case Surf.Road: return Paving(x, y, z, wx, wz, false);
                    case Surf.Plaza: return Paving(x, y, z, wx, wz, true);
                    case Surf.Curb: return Vary(Curb, x, y, z, 0.025f);
                    case Surf.Gravel: return Vary(Hash(x, 3, z) > 0.5f ? Gravel : GravelB, x, y, z, 0.03f);
                    case Surf.PathX: return ((x >> 1) % 3 == 2) ? Vary(GrassA, x, y, z) : Vary(Pick(x >> 1, 0, z >> 1, PathA, PathB, PathC), x, y, z, 0.02f);
                    case Surf.Path: return ((z >> 1) % 3 == 2) ? Vary(GrassA, x, y, z) : Vary(Pick(x >> 1, 0, z >> 1, PathA, PathB, PathC), x, y, z, 0.02f);
                    case Surf.Sand: return Vary(Hash(x, 9, z) > 0.7f ? Moss : Sand, x, y, z, 0.03f);
                    case Surf.Forest:
                    {
                        float hsh = Hash(x, 11, z);
                        return Vary(hsh > 0.9f ? Needles : hsh > 0.8f ? Fern : Mathf.PerlinNoise(wx * 0.3f, wz * 0.3f) > 0.5f ? ForestA : ForestB, x, y, z, 0.03f);
                    }
                    case Surf.Wild: return Color.Lerp(Pick(x, 78, z, WildPink, WildYellow, WildWhite, WildBlue), GrassA, 0.35f);
                    default:
                    {
                        float patch = Mathf.PerlinNoise(wx * 0.25f, wz * 0.25f);
                        return Vary(patch > 0.62f ? GrassC : patch < 0.38f ? GrassB : GrassA, x, y, z);
                    }
                }
            }
            if (depth < 4 && s != Surf.Road && s != Surf.Plaza) return Vary(Hash(x, y, z * 3) > 0.85f ? SoilDark : Soil, x, y, z, 0.03f);
            return Vary(Hash(x, y * 7, z) > 0.8f ? StoneDark : Stone, x, y, z, 0.03f);
        }

        public float HeightAt(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt((x - MinX) / S), 0, NX - 1), k = Mathf.Clamp(Mathf.FloorToInt((z - MinZ) / S), 0, NZ - 1);
            return MinY + top[i, k] * S;
        }

        public void Build() { var e = BuildAsync(); while (e.MoveNext()) { } }

        /// <summary>Постройка по частям: между частями кадр успевает нарисовать экран загрузки.</summary>
        public System.Collections.IEnumerator BuildAsync()
        {
            NX = Mathf.RoundToInt(SizeX / S);
            NZ = Mathf.RoundToInt(SizeZ / S);
            top = new short[NX, NZ];
            surf = new byte[NX, NZ];
            paint = new byte[NX, NZ];
            offset = new float[NX, NZ];
            baseH = new float[NX, NZ];
            for (int x = 0; x < NX; x++)
            {
                for (int z = 0; z < NZ; z++)
                    baseH[x, z] = Height(MinX + (x + 0.5f) * S, MinZ + (z + 0.5f) * S);
                if (x % 24 == 0) yield return null;
            }
            mat = new Material(Shader.Find("FoxLife/VoxelColor"));
            for (int x = 0; x < NX; x++)
            {
                for (int z = 0; z < NZ; z++)
                    FillColumn(x, z);
                if (x % 48 == 0) yield return null;
            }
            for (int cx = 0; cx < NX; cx += Chunk)
                for (int cz = 0; cz < NZ; cz += Chunk)
                    Remesh(cx, cz);
            Flowers();
        }

        // ---------- сетка ----------

        public bool deferMesh = true;
        readonly HashSet<Vector2Int> dirty = new HashSet<Vector2Int>();

        void Remesh(int cx, int cz)
        {
            if (deferMesh) { dirty.Add(new Vector2Int(cx, cz)); return; }
            MeshChunk(cx, cz, Mathf.Min(NX, cx + Chunk), Mathf.Min(NZ, cz + Chunk));
        }

        public System.Collections.IEnumerator FlushMeshAsync()
        {
            deferMesh = false;
            var keys = new List<Vector2Int>(dirty);
            dirty.Clear();
            int n = 0;
            foreach (var k in keys)
            {
                MeshChunk(k.x, k.y, Mathf.Min(NX, k.x + Chunk), Mathf.Min(NZ, k.y + Chunk));
                if (++n % 3 == 0) yield return null;
            }
            foreach (var f in flowers) if (f.go != null) f.go.transform.position = new Vector3(0, GroundAt(f.p.x, f.p.z), 0);
        }

        public void FlushMesh()
        {
            deferMesh = false;
            foreach (var k in dirty) MeshChunk(k.x, k.y, Mathf.Min(NX, k.x + Chunk), Mathf.Min(NZ, k.y + Chunk));
            dirty.Clear();
            foreach (var f in flowers) if (f.go != null) f.go.transform.position = new Vector3(0, GroundAt(f.p.x, f.p.z), 0);
        }

        void MeshChunk(int x0, int z0, int x1, int z1)
        {
            var key = new Vector2Int(x0, z0);
            if (chunks.TryGetValue(key, out var old) && old != null) Destroy(old);
            var go = MeshHeights(x0, z0, x1, z1);
            chunks[key] = go;
            if (go != null)
            {
                go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                go.AddComponent<TerrainChunk>();
            }
        }

        static readonly Vector3Int[] N = { Vector3Int.up, Vector3Int.down, Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
        static readonly Vector3Int[] U = { new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 1), new Vector3Int(0, 1, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 0, 0) };
        static readonly Vector3Int[] V = { new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0) };
        static readonly float[] AoK = { 0.6f, 0.74f, 0.87f, 1f };

        readonly List<Vector3> wv = new List<Vector3>(); readonly List<Color32> wc = new List<Color32>(); readonly List<int> wtri = new List<int>();
        static Material waterMat;

        void WaterQuad(Vector3 c, bool shore, float depth)
        {
            int b = wv.Count;
            float h = S * 0.5f;
            wv.Add(c + new Vector3(-h, 0, -h)); wv.Add(c + new Vector3(-h, 0, h)); wv.Add(c + new Vector3(h, 0, h)); wv.Add(c + new Vector3(h, 0, -h));
            var col = new Color32((byte)(shore ? 255 : 0), (byte)(depth * 255), 0, 255);
            for (int i = 0; i < 4; i++) wc.Add(col);
            wtri.Add(b); wtri.Add(b + 1); wtri.Add(b + 2); wtri.Add(b); wtri.Add(b + 2); wtri.Add(b + 3);
        }

        readonly List<Vector3> mv = new List<Vector3>(); readonly List<Vector3> mn = new List<Vector3>();
        readonly List<Color32> mc = new List<Color32>(); readonly List<int> mt = new List<int>();

        int T(int x, int z) => (uint)x < (uint)NX && (uint)z < (uint)NZ ? top[x, z] : 0;
        bool IsWater(int x, int z) => MinY + top[x, z] * S < WaterY - 0.01f;
        int WaterTop => Mathf.RoundToInt((WaterY - MinY) / S);
        // занятость с учётом воды (вода — тоже сплошной объём для соседей)
        bool F(int x, int y, int z) { if ((uint)x >= (uint)NX || (uint)z >= (uint)NZ) return false; int t = top[x, z]; if (y < t) return true; return y < WaterTop && t < WaterTop; }

        void Quad(Vector3 o, Vector3Int n, Vector3Int u, Vector3Int v, Color32 c, int[] ao)
        {
            int i0 = mv.Count;
            int[] du = { -1, -1, 1, 1 }, dv = { -1, 1, 1, -1 };
            for (int k = 0; k < 4; k++)
            {
                mv.Add(o + ((Vector3)u * du[k] + (Vector3)v * dv[k]) * (0.5f + Voxelizer.Inflate) * S);
                mn.Add(n);
                float kk = AoK[ao[k]];
                mc.Add(new Color32((byte)(c.r * kk), (byte)(c.g * kk), (byte)(c.b * kk), 255));
            }
            if (ao[0] + ao[2] < ao[1] + ao[3]) { mt.Add(i0 + 1); mt.Add(i0 + 2); mt.Add(i0 + 3); mt.Add(i0 + 1); mt.Add(i0 + 3); mt.Add(i0); }
            else { mt.Add(i0); mt.Add(i0 + 1); mt.Add(i0 + 2); mt.Add(i0); mt.Add(i0 + 2); mt.Add(i0 + 3); }
        }

        readonly int[] aoBuf = new int[4];

        void FaceAo(int ox, int oy, int oz, int f)
        {
            int[] du = { -1, -1, 1, 1 }, dv = { -1, 1, 1, -1 };
            var u = U[f]; var v = V[f];
            for (int k = 0; k < 4; k++)
            {
                bool s1 = F(ox + u.x * du[k], oy + u.y * du[k], oz + u.z * du[k]);
                bool s2 = F(ox + v.x * dv[k], oy + v.y * dv[k], oz + v.z * dv[k]);
                bool s3 = F(ox + u.x * du[k] + v.x * dv[k], oy + u.y * du[k] + v.y * dv[k], oz + u.z * du[k] + v.z * dv[k]);
                aoBuf[k] = s1 && s2 ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (s3 ? 1 : 0));
            }
        }

        /// <summary>Сетка куска по карте высот: верхние грани и видимые боковые стенки.</summary>
        GameObject MeshHeights(int x0, int z0, int x1, int z1)
        {
            mv.Clear(); mn.Clear(); mc.Clear(); mt.Clear();
            wv.Clear(); wc.Clear(); wtri.Clear();
            var origin = new Vector3(MinX, MinY, MinZ);
            int wt = WaterTop;
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    int t = top[x, z];
                    var s = (Surf)surf[x, z];
                    bool water = t < wt;
                    // верх
                    if (water)
                    {
                        // дно видно сквозь воду: песок с галькой, глубже — темнее и зеленее
                        int dpt = wt - t;
                        var bed = Color.Lerp(Hash(x, 5, z) > 0.75f ? (Color)Moss : (Color)Sand, new Color(0.25f, 0.42f, 0.42f), Mathf.Clamp01(dpt / 7f));
                        FaceAo(x, t, z, 0);
                        Quad(origin + new Vector3(x + 0.5f, t, z + 0.5f) * S, N[0], U[0], V[0], Vary(bed, x, t, z, 0.03f), aoBuf);
                        // поверхность — отдельной прозрачной сеткой
                        bool shore = false;
                        for (int f = 2; f < 6 && !shore; f++)
                        {
                            int nx = x + N[f].x, nz = z + N[f].z;
                            if ((uint)nx < (uint)NX && (uint)nz < (uint)NZ && top[nx, nz] >= wt) shore = true;
                        }
                        WaterQuad(origin + new Vector3(x + 0.5f, wt, z + 0.5f) * S, shore, Mathf.Clamp01(dpt / 6f));
                    }
                    else
                    {
                        FaceAo(x, t, z, 0);
                        Quad(origin + new Vector3(x + 0.5f, t, z + 0.5f) * S, N[0], U[0], V[0], ColorAt(x, t - 1, z, t, s), aoBuf);
                    }
                    // боковые стенки до высоты соседа
                    int myTop = t; // под водой берега тоже видны — вода прозрачная
                    for (int f = 2; f < 6; f++)
                    {
                        int nx = x + N[f].x, nz = z + N[f].z;
                        if ((uint)nx >= (uint)NX || (uint)nz >= (uint)NZ) continue;
                        int nt = top[nx, nz];
                        int nTop = nt;
                        for (int y = nTop; y < myTop; y++)
                        {
                            Color32 c = y < wt ? Vary(Sand, x, y, z, 0.03f) : ColorAt(x, y, z, t, s);
                            FaceAo(nx, y, nz, f);
                            Quad(origin + (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) + (Vector3)N[f] * 0.5f) * S, N[f], U[f], V[f], c, aoBuf);
                        }
                    }
                }
            if (mv.Count == 0) return null;
            var go = new GameObject("terrain");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(mv); mesh.SetNormals(mn); mesh.SetColors(mc); mesh.SetTriangles(mt, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            if (wv.Count > 0)
            {
                if (waterMat == null) waterMat = new Material(Shader.Find("FoxLife/Water"));
                var wgo = new GameObject("вода");
                wgo.transform.SetParent(go.transform, false);
                var wm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                wm.SetVertices(wv); wm.SetColors(wc); wm.SetTriangles(wtri, 0);
                wm.RecalculateNormals(); wm.RecalculateBounds();
                wgo.AddComponent<MeshFilter>().sharedMesh = wm;
                var wr = wgo.AddComponent<MeshRenderer>();
                wr.sharedMaterial = waterMat;
                wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                wr.receiveShadows = false;
            }
            return go;
        }

        /// <summary>Общий построитель сетки с цветом у каждого воксела и затенением углов.</summary>
        public static GameObject MeshColored(System.Func<int, int, int, bool> filled, System.Func<int, int, int, Color32> color,
            int x0, int y0, int z0, int x1, int y1, int z1, float s, Vector3 origin, Material mat, Transform parent, string name)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var col = new List<Color32>();
            var t = new List<int>();
            int[] du = { -1, -1, 1, 1 }, dv = { -1, 1, 1, -1 };
            int[] ao = new int[4];
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        if (!filled(x, y, z)) continue;
                        var bc = color(x, y, z);
                        for (int f = 0; f < 6; f++)
                        {
                            var o = new Vector3Int(x, y, z) + N[f];
                            if (filled(o.x, o.y, o.z)) continue;
                            var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) + (Vector3)N[f] * 0.5f;
                            int i0 = v.Count;
                            for (int k = 0; k < 4; k++)
                            {
                                var a = o + U[f] * du[k];
                                var b = o + V[f] * dv[k];
                                var c = o + U[f] * du[k] + V[f] * dv[k];
                                bool s1 = filled(a.x, a.y, a.z), s2 = filled(b.x, b.y, b.z), s3 = filled(c.x, c.y, c.z);
                                ao[k] = s1 && s2 ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (s3 ? 1 : 0));
                                v.Add(origin + (center + ((Vector3)U[f] * du[k] + (Vector3)V[f] * dv[k]) * (0.5f + Voxelizer.Inflate)) * s);
                                n.Add(N[f]);
                                float kk = AoK[ao[k]];
                                col.Add(new Color32((byte)(bc.r * kk), (byte)(bc.g * kk), (byte)(bc.b * kk), 255));
                            }
                            if (ao[0] + ao[2] < ao[1] + ao[3]) { t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0 + 3); t.Add(i0 + 1); t.Add(i0 + 3); t.Add(i0); }
                            else { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
                        }
                    }
            if (v.Count == 0) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            return go;
        }

        // ---------- цветы из мелких вокселов ----------

        public class FlowerSpot { public Vector3 p; public int seed; public GameObject go; }
        public readonly List<FlowerSpot> flowers = new List<FlowerSpot>();

        void Flowers()
        {
            int placed = 0;
            for (int i = 0; i < 3000 && placed < 450; i++)
            {
                float wx = MinX + 1 + Hash(i, 1, 9) * (SizeX - 2), wz = MinZ + 1 + Hash(i, 2, 9) * (SizeZ - 2);
                int lot = Town.LotIdAt(wx, wz);
                if (Town.OnLotPath(wx, wz) || Town.StreetKind(wx, wz) != 0 || (lot >= 0 && lot != 4) || Town.IsCreek(wx, wz) || Town.CreekDist(wx, wz) < 2f) continue;
                if (Height(wx, wz) < -0.2f) continue;
                placed++;
                AddFlowers(new Vector3(wx, 0, wz), i);
            }
        }

        public FlowerSpot AddFlowers(Vector3 p, int seed)
        {
            var spot = new FlowerSpot { p = new Vector3(p.x, 0, p.z), seed = seed };
            spot.go = MakeFlowers(spot.p, seed);
            flowers.Add(spot);
            return spot;
        }

        public void ClearFlowers()
        {
            foreach (var f in flowers) if (f.go != null) Destroy(f.go);
            flowers.Clear();
        }

        public int RemoveFlowers(Vector3 p, float r)
        {
            int n = 0;
            for (int i = flowers.Count - 1; i >= 0; i--)
            {
                var f = flowers[i];
                if (new Vector2(f.p.x - p.x, f.p.z - p.z).magnitude > r) continue;
                if (f.go != null) Destroy(f.go);
                flowers.RemoveAt(i);
                n++;
            }
            return n;
        }

        /// <summary>Кустик цветов и травинок из мелких вокселов.</summary>
        GameObject MakeFlowers(Vector3 p, int i)
        {
            const float fs = 0.0625f;
            const int R = 16, HT = 10;
            var cells2 = new Color32[R * HT * R];
            void S2(int x, int y, int z, Color c) { if (x >= 0 && y >= 0 && z >= 0 && x < R && y < HT && z < R) cells2[(y * R + z) * R + x] = c; }
            Color[] petals = { Pink, Yellow, Lilac, White };
            bool fern = i < 0; // подлесок: папоротники и травинки без цветов
            int count = fern ? 4 + (int)(Hash(i, 5, 1) * 4) : 2 + (int)(Hash(i, 5, 1) * 4);
            for (int k = 0; k < count; k++)
            {
                int cx = 3 + (int)(Hash(i, k, 2) * 10), cz = 3 + (int)(Hash(i, k, 3) * 10);
                int h = 3 + (int)(Hash(i, k, 4) * 4);
                bool flower = !fern && Hash(i, k, 6) > 0.35f;
                for (int y = 0; y < h; y++) S2(cx, y, cz, Vary(fern ? Fern : LeafB, cx, y, cz, 0.04f));
                if (flower)
                {
                    var pc = petals[(int)(Hash(i, k, 7) * petals.Length) % petals.Length];
                    S2(cx, h, cz, pc == Yellow ? Orange : Yellow);
                    S2(cx + 1, h, cz, pc); S2(cx - 1, h, cz, pc); S2(cx, h, cz + 1, pc); S2(cx, h, cz - 1, pc); S2(cx, h + 1, cz, pc);
                }
                else
                    for (int b = 0; b < 3; b++)
                        for (int y = 0; y < h - 1 + b % 2; y++) S2(cx + (y > 2 ? b - 1 : 0), y, cz + (b == 1 ? 1 : 0), Vary(fern ? (b == 1 ? Fern : ForestA) : b == 1 ? LeafA : LeafLight, cx, y, cz + b, 0.04f));
            }
            var origin = new Vector3(p.x - R * fs / 2, 0, p.z - R * fs / 2);
            var go = MeshColored((x, y, z) => x >= 0 && y >= 0 && z >= 0 && x < R && y < HT && z < R && cells2[(y * R + z) * R + x].a > 0,
                (x, y, z) => cells2[(y * R + z) * R + x], 0, 0, 0, R, HT, R, fs, origin, mat, transform, "flowers");
            if (go != null) go.transform.position = new Vector3(0, GroundAt(p.x, p.z), 0);
            return go;
        }

        // ================= правка рельефа =================

        public enum Brush { Raise, Lower, Flatten, Pond, Fill, Smooth }

        public static bool InLot(float x, float z) => x >= 0 && z >= 0 && x < House.W && z < House.H;

        /// <summary>Высота поверхности земли или воды в точке.</summary>
        public float GroundAt(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt((x - MinX) / S), 0, NX - 1), k = Mathf.Clamp(Mathf.FloorToInt((z - MinZ) / S), 0, NZ - 1);
            float t = MinY + top[i, k] * S;
            return Mathf.Max(t, IsWaterColumn(i, k) ? WaterY : t);
        }

        /// <summary>Природная высота земли в клетке (без домов): по ней ставится дом.</summary>
        public float NaturalAt(Vector2Int c)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt((c.x + 0.5f - MinX) / S), 0, NX - 1), k = Mathf.Clamp(Mathf.FloorToInt((c.y + 0.5f - MinZ) / S), 0, NZ - 1);
            float wx = MinX + (i + 0.5f) * S, wz = MinZ + (k + 0.5f) * S;
            return Mathf.Round((baseH[i, k] + offset[i, k]) * 4f) / 4f;
        }

        /// <summary>Высота, на которой стоит лисичка в клетке (верх земли посередине клетки).</summary>
        public float SurfaceAt(Vector2Int c) => GroundAt(c.x + 0.5f, c.y + 0.5f);

        bool IsWaterColumn(int i, int k) => MinY + top[i, k] * S < WaterY - 0.05f;

        /// <summary>Вода в клетке (там нельзя ходить и ставить мебель).</summary>
        public bool IsWaterCell(Vector2Int c)
        {
            int i0 = Mathf.FloorToInt((c.x - MinX) / S), k0 = Mathf.FloorToInt((c.y - MinZ) / S);
            int n = Mathf.RoundToInt(1f / S), wet = 0, wt = WaterTop;
            for (int i = i0; i < i0 + n; i++)
                for (int k = k0; k < k0 + n; k++)
                {
                    if (i < 0 || k < 0 || i >= NX || k >= NZ) continue;
                    if (top[i, k] < wt || IsWaterColumn(i, k)) wet++;
                }
            return wet * 3 >= n * n; // треть клетки под водой — уже не пройти
        }

        /// <summary>Перепад высот внутри клетки (по углам): на крутых склонах не ходят.</summary>
        public float CellRoughness(Vector2Int c)
        {
            float a = GroundAt(c.x + 0.15f, c.y + 0.15f), b = GroundAt(c.x + 0.85f, c.y + 0.15f), d = GroundAt(c.x + 0.15f, c.y + 0.85f), e = GroundAt(c.x + 0.85f, c.y + 0.85f);
            return Mathf.Max(Mathf.Max(a, b), Mathf.Max(d, e)) - Mathf.Min(Mathf.Min(a, b), Mathf.Min(d, e));
        }

        /// <summary>Кисть рельефа. На своём участке — всё, кроме места под комнатами; чужие участки и улицы не трогаем.</summary>
        public bool ApplyBrush(Vector3 p, float radius, Brush mode, float flatH)
        {
            int i0 = Mathf.Max(0, Mathf.FloorToInt((p.x - radius - MinX) / S)), i1 = Mathf.Min(NX - 1, Mathf.CeilToInt((p.x + radius - MinX) / S));
            int k0 = Mathf.Max(0, Mathf.FloorToInt((p.z - radius - MinZ) / S)), k1 = Mathf.Min(NZ - 1, Mathf.CeilToInt((p.z + radius - MinZ) / S));
            bool changed = false;
            for (int i = i0; i <= i1; i++)
                for (int k = k0; k <= k1; k++)
                {
                    float wx = MinX + (i + 0.5f) * S, wz = MinZ + (k + 0.5f) * S;
                    float d = new Vector2(wx - p.x, wz - p.z).magnitude;
                    if (d > radius || InHouse(wx, wz)) continue;
                    int lotId = Town.LotIdAt(wx, wz);
                    bool anywhere = Game.I != null && Game.I.townBuilder; // в строительстве города — без разницы, участок или нет
                    if (!anywhere && ((lotId >= 0 && lotId != House.PlayerLotId) || Town.StreetKind(wx, wz) != 0 || Town.OnLotPath(wx, wz))) continue;
                    bool lot = lotId == House.PlayerLotId;
                    float w = 1f - (d / radius) * (d / radius);
                    float bh = baseH[i, k];
                    float cur = bh + offset[i, k];
                    float target = cur;
                    switch (mode)
                    {
                        case Brush.Raise: target = cur + 0.25f * w + 0.01f; break;
                        case Brush.Lower: target = cur - 0.25f * w - 0.01f; break;
                        case Brush.Flatten: target = Mathf.Lerp(cur, flatH, Mathf.Clamp01(w * 1.5f)); break;
                        case Brush.Pond: target = Mathf.Min(cur, -0.75f); break;
                        case Brush.Fill: target = cur < -0.2f ? (lot ? Town.LotElev(House.PlayerLotId) : Mathf.Max(bh, 0f)) : cur; break;
                        case Brush.Smooth:
                        {
                            // среднее по соседям (радиус ~0.75 м): холмики оплывают, ступеньки сглаживаются постепенно
                            float sum = 0, n = 0;
                            for (int di = -3; di <= 3; di++)
                                for (int dk = -3; dk <= 3; dk++)
                                {
                                    int ii = Mathf.Clamp(i + di, 0, NX - 1), kk = Mathf.Clamp(k + dk, 0, NZ - 1);
                                    float ww = 1f / (1f + di * di + dk * dk);
                                    sum += (baseH[ii, kk] + offset[ii, kk]) * ww; n += ww;
                                }
                            target = Mathf.Lerp(cur, sum / n, 0.45f * w);
                            break;
                        }
                    }
                    target = Mathf.Clamp(target, -1.5f, 16f);
                    if (Mathf.Abs(target - cur) < 0.001f) continue;
                    LogColumn(i, k);
                    offset[i, k] = target - bh;
                    FillColumn(i, k);
                    changed = true;
                }
            if (!changed) return false;
            for (int cx = (Mathf.Max(0, i0 - 1) / Chunk) * Chunk; cx <= Mathf.Min(NX - 1, i1 + 1); cx += Chunk)
                for (int cz = (Mathf.Max(0, k0 - 1) / Chunk) * Chunk; cz <= Mathf.Min(NZ - 1, k1 + 1); cz += Chunk)
                    Remesh(cx, cz);
            foreach (var f in flowers)
                if (f.go != null && new Vector2(f.p.x - p.x, f.p.z - p.z).magnitude < radius + 1f)
                    f.go.transform.position = new Vector3(0, GroundAt(f.p.x, f.p.z), 0);
            return true;
        }

        // ---------- сохранение правок ----------

        public List<Vector3> OffsetData()
        {
            var l = new List<Vector3>();
            for (int i = 0; i < NX; i++)
                for (int k = 0; k < NZ; k++)
                    if (Mathf.Abs(offset[i, k]) > 0.001f) l.Add(new Vector3(i, k, offset[i, k]));
            return l;
        }

        public void LoadOffsets(List<Vector3> data)
        {
            bool any = data != null && data.Count > 0;
            if (!any) foreach (var o in offset) if (o != 0) { any = true; break; }
            if (!any) return;
            System.Array.Clear(offset, 0, offset.Length);
            if (data != null)
                foreach (var d in data)
                {
                    int i = (int)d.x, k = (int)d.y;
                    if (i >= 0 && k >= 0 && i < NX && k < NZ) offset[i, k] = d.z;
                }
            for (int x = 0; x < NX; x++) for (int z = 0; z < NZ; z++) FillColumn(x, z);
            for (int cx = 0; cx < NX; cx += Chunk)
                for (int cz = 0; cz < NZ; cz += Chunk)
                    Remesh(cx, cz);
            foreach (var f in flowers) if (f.go != null) f.go.transform.position = new Vector3(0, GroundAt(f.p.x, f.p.z), 0);
        }
    }

    /// <summary>Метка куска земли — для кисти ландшафта.</summary>
    public class TerrainChunk : MonoBehaviour { }
}
