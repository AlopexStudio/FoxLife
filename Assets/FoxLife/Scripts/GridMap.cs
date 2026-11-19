using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Клеточная карта одного этажа всего города (1 клетка = 1 м): мебель, стены между клетками, пол, поиск пути.
    /// Клетки задаются мировыми координатами; MinX/MinZ — левый нижний угол карты (может быть отрицательным).
    /// </summary>
    public class GridMap
    {
        public readonly int W, H, MinX, MinZ;
        readonly Furniture[,] occ;
        readonly bool[,] wallX;   // ребро между (x-1,z) и (x,z)
        readonly bool[,] wallZ;   // ребро между (x,z-1) и (x,z)
        readonly bool[,] structX, structZ; // любая постройка на ребре
        readonly bool[,] noFloor; // нет пола (вода, дыра, второй этаж вне комнат)
        readonly float[,] height;  // на какой высоте стоит лисичка в клетке
        public const float MaxStep = 0.8f; // выше этого перепада между соседними клетками не шагнуть
        public float baseY;

        public GridMap(int minX, int minZ, int w, int h)
        {
            MinX = minX; MinZ = minZ; W = w; H = h;
            occ = new Furniture[w, h];
            wallX = new bool[w + 1, h];
            wallZ = new bool[w, h + 1];
            structX = new bool[w + 1, h];
            structZ = new bool[w, h + 1];
            noFloor = new bool[w, h];
            height = new float[w, h];
        }

        public bool InBounds(Vector2Int c) => c.x >= MinX && c.y >= MinZ && c.x < MinX + W && c.y < MinZ + H;
        public Furniture At(Vector2Int c) => InBounds(c) ? occ[c.x - MinX, c.y - MinZ] : null;
        public bool IsFree(Vector2Int c) => InBounds(c) && occ[c.x - MinX, c.y - MinZ] == null && !noFloor[c.x - MinX, c.y - MinZ];
        public void SetOcc(Vector2Int c, Furniture f) { if (InBounds(c)) occ[c.x - MinX, c.y - MinZ] = f; }

        public void Clear(Furniture f)
        {
            for (int x = 0; x < W; x++)
                for (int z = 0; z < H; z++)
                    if (occ[x, z] == f) occ[x, z] = null;
        }

        public void SetNoFloor(Vector2Int c, bool v) { if (InBounds(c)) noFloor[c.x - MinX, c.y - MinZ] = v; }
        public bool HasFloorAt(Vector2Int c) => InBounds(c) && !noFloor[c.x - MinX, c.y - MinZ];
        public void SetHeight(Vector2Int c, float h) { if (InBounds(c)) height[c.x - MinX, c.y - MinZ] = h; }
        public float HeightAt(Vector2Int c) => InBounds(c) ? height[c.x - MinX, c.y - MinZ] : baseY;
        bool StepOk(Vector2Int a, Vector2Int b) => Mathf.Abs(height[a.x - MinX, a.y - MinZ] - height[b.x - MinX, b.y - MinZ]) <= MaxStep;

        /// <summary>Сбросить стены и пол в прямоугольнике мира (при пересборке одного участка).</summary>
        public void ClearWalls(RectInt r)
        {
            for (int x = r.xMin; x <= r.xMax; x++)
                for (int z = r.yMin; z <= r.yMax; z++)
                {
                    int ix = x - MinX, iz = z - MinZ;
                    if (ix >= 0 && iz >= 0 && ix <= W && iz < H) { wallX[ix, iz] = false; structX[ix, iz] = false; }
                    if (ix >= 0 && iz >= 0 && ix < W && iz <= H) { wallZ[ix, iz] = false; structZ[ix, iz] = false; }
                    if (ix >= 0 && iz >= 0 && ix < W && iz < H) noFloor[ix, iz] = false;
                }
        }

        public void SetWallX(int x, int z, bool blocks, bool structure)
        {
            int ix = x - MinX, iz = z - MinZ;
            if (ix < 0 || iz < 0 || ix > W || iz >= H) return;
            wallX[ix, iz] = blocks; structX[ix, iz] = structure;
        }

        public void SetWallZ(int x, int z, bool blocks, bool structure)
        {
            int ix = x - MinX, iz = z - MinZ;
            if (ix < 0 || iz < 0 || ix >= W || iz > H) return;
            wallZ[ix, iz] = blocks; structZ[ix, iz] = structure;
        }

        bool WX(bool[,] a, int x, int z) { int ix = x - MinX, iz = z - MinZ; return ix >= 0 && iz >= 0 && ix <= W && iz < H && a[ix, iz]; }
        bool WZ(bool[,] a, int x, int z) { int ix = x - MinX, iz = z - MinZ; return ix >= 0 && iz >= 0 && ix < W && iz <= H && a[ix, iz]; }

        /// <summary>Есть ли на ребре между соседними клетками что-то построенное (мебель не должна его пересекать).</summary>
        public bool HasStructure(Vector2Int a, Vector2Int b)
        {
            if (a.y == b.y && Mathf.Abs(a.x - b.x) == 1) return WX(structX, Mathf.Max(a.x, b.x), a.y);
            if (a.x == b.x && Mathf.Abs(a.y - b.y) == 1) return WZ(structZ, a.x, Mathf.Max(a.y, b.y));
            return false;
        }

        public bool EdgeBlocked(Vector2Int a, Vector2Int b)
        {
            if (a.y == b.y && Mathf.Abs(a.x - b.x) == 1) return WX(wallX, Mathf.Max(a.x, b.x), a.y);
            if (a.x == b.x && Mathf.Abs(a.y - b.y) == 1) return WZ(wallZ, a.x, Mathf.Max(a.y, b.y));
            return false;
        }

        public static Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
        public static Vector3 Center(Vector2Int c) => new Vector3(c.x + 0.5f, 0, c.y + 0.5f);

        public Vector2Int ClampCell(Vector2Int c) => new Vector2Int(Mathf.Clamp(c.x, MinX, MinX + W - 1), Mathf.Clamp(c.y, MinZ, MinZ + H - 1));

        static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        bool Passable(Vector2Int c, Vector2Int start)
        {
            if (!InBounds(c)) return false;
            if (c == start) return true;
            int ix = c.x - MinX, iz = c.y - MinZ;
            return occ[ix, iz] == null && !noFloor[ix, iz];
        }

        bool CanStep(Vector2Int a, Vector2Int d, Vector2Int start)
        {
            var b = a + d;
            if (!Passable(b, start)) return false;
            if (!InBounds(a) || !StepOk(a, b)) return false;
            if (d.x == 0 || d.y == 0) return !EdgeBlocked(a, b);
            var h = new Vector2Int(a.x + d.x, a.y);
            var v = new Vector2Int(a.x, a.y + d.y);
            if (!Passable(h, start) || !Passable(v, start)) return false;
            if (!StepOk(a, h) || !StepOk(a, v) || !StepOk(h, b) || !StepOk(v, b)) return false;
            return !EdgeBlocked(a, h) && !EdgeBlocked(h, b) && !EdgeBlocked(a, v) && !EdgeBlocked(v, b);
        }

        // переиспользуемые массивы поиска (метка поколения вместо очистки)
        float[] gCost; int[] came; int[] stamp; bool[] closed; int gen;
        readonly List<(float f, int i)> heap = new List<(float, int)>();

        void Push(float f, int i)
        {
            heap.Add((f, i));
            int k = heap.Count - 1;
            while (k > 0)
            {
                int p = (k - 1) / 2;
                if (heap[p].f <= heap[k].f) break;
                (heap[p], heap[k]) = (heap[k], heap[p]);
                k = p;
            }
        }

        (float f, int i) Pop()
        {
            var top = heap[0];
            var last = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count > 0)
            {
                heap[0] = last;
                int k = 0;
                while (true)
                {
                    int l = k * 2 + 1, r = l + 1, m = k;
                    if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                    if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                    if (m == k) break;
                    (heap[m], heap[k]) = (heap[k], heap[m]);
                    k = m;
                }
            }
            return top;
        }

        /// <summary>Путь из центров клеток (без стартовой). null — дороги нет.</summary>
        public List<Vector3> FindPath(Vector2Int start, Vector2Int goal)
        {
            start = ClampCell(start);
            if (!InBounds(goal)) return null;
            if (start == goal) return new List<Vector3>();
            int gx = goal.x - MinX, gz = goal.y - MinZ;
            if (occ[gx, gz] != null || noFloor[gx, gz]) return null;

            int n = W * H;
            if (gCost == null || gCost.Length != n) { gCost = new float[n]; came = new int[n]; stamp = new int[n]; closed = new bool[n]; gen = 0; }
            gen++;
            int Idx(Vector2Int c) => (c.y - MinZ) * W + (c.x - MinX);
            Vector2Int Cell(int i) => new Vector2Int(i % W + MinX, i / W + MinZ);
            float G(int i) => stamp[i] == gen ? gCost[i] : float.MaxValue;
            float Hf(Vector2Int c) { float dx = Mathf.Abs(c.x - goal.x), dz = Mathf.Abs(c.y - goal.y); return Mathf.Max(dx, dz) + 0.4142f * Mathf.Min(dx, dz); }

            int s = Idx(start), gi = Idx(goal);
            heap.Clear();
            stamp[s] = gen; gCost[s] = 0; came[s] = -1; closed[s] = false;
            Push(Hf(start), s);
            bool found = false;
            int guard = 0;
            while (heap.Count > 0 && guard++ < n * 4)
            {
                var (_, cur) = Pop();
                if (cur == gi) { found = true; break; }
                if (stamp[cur] == gen && closed[cur]) continue;
                closed[cur] = true;
                var cc = Cell(cur);
                float gc = G(cur);
                foreach (var d in Dirs)
                {
                    if (!CanStep(cc, d, start)) continue;
                    var nb = cc + d;
                    int ni = Idx(nb);
                    if (stamp[ni] == gen && closed[ni]) continue;
                    float cost = gc + ((d.x != 0 && d.y != 0) ? 1.4142f : 1f);
                    if (cost < G(ni))
                    {
                        if (stamp[ni] != gen) { stamp[ni] = gen; closed[ni] = false; }
                        gCost[ni] = cost;
                        came[ni] = cur;
                        Push(cost + Hf(nb), ni);
                    }
                }
            }
            if (!found) return null;
            var path = new List<Vector3>();
            for (int i = gi; i != s; i = came[i]) { var cc = Cell(i); path.Add(Center(cc) + Vector3.up * height[cc.x - MinX, cc.y - MinZ]); }
            path.Reverse();
            return path;
        }
    }
}
