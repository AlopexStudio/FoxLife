using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public enum EdgeKind { None = 0, Wall = 1, Door = 2, Window = 3, Arch = 4, Fence = 5, Gate = 6, Railing = 7, RailingOpen = 8 }

    public enum LotKind { Home, Park, Cafe, Library, Square, School, Market, Workshop, Observatory, Camp }

    /// <summary>
    /// Город и постройки. Весь город — одна клеточная сетка (1 м), поделённая на участки.
    /// Стены стоят на рёбрах сетки, на стенах — двери, окна, проёмы. Этажей два.
    /// Комнаты находятся сами: замкнутые стенами области внутри участка. Над верхними комнатами строится крыша.
    /// У каждого участка свои фасад, окна, перила, забор и крыша. Пересборка идёт по одному участку.
    /// Участок, на который смотрит камера, «открыт»: стены опускаются, крыша прячется, видны этажи по выбору.
    /// </summary>
    public class House : MonoBehaviour
    {
        public const int W = 20, H = 16, Levels = 2;   // W, H — размер участка игрока
        public const float WallH = 2.0f, CutH = 0.25f, DoorH = 1.45f, FenceH = 0.8f;
        const float Half = 0.0625f;

        public enum WallMode { Auto, Up, Down }
        public WallMode wallMode = WallMode.Auto;
        public int viewLevel;
        public bool showRoof;
        public int focusLot;

        public GridMap[] grids;
        public GridMap grid => grids[0];
        public VoxelTerrain terrain;

        // ---------- участки ----------
        public class Lot
        {
            public int id;
            public string name;
            public RectInt rect;
            public LotKind kind;
            public bool frontSouth;
            public Transform root;
            public Styleable facade, openings, fence, roof, railing, ground;
            public Vector2Int entry;
            public bool Public => kind != LotKind.Home;
            public bool windowDecor = true;
            public bool removed;
        }

        public readonly List<Lot> lots = new List<Lot>();
        /// <summary>Участок нашей семьи (меняется при переезде).</summary>
        public static int PlayerLotId;
        public Lot PlayerLot => lots[Mathf.Clamp(PlayerLotId, 0, lots.Count - 1)];
        public Styleable facade => PlayerLot.facade;
        public Styleable openings => PlayerLot.openings;
        public Styleable fence => PlayerLot.fence;
        public Styleable roof => PlayerLot.roof;
        public Styleable railing => PlayerLot.railing;
        public Styleable lot => PlayerLot.ground;

        public Lot LotAt(Vector2Int c)
        {
            foreach (var l in lots) if (l.rect.Contains(c)) return l;
            return null;
        }

        public Lot LotById(int id) => id >= 0 && id < lots.Count ? lots[id] : null;
        public Vector2Int EntryCell => PlayerLot.entry;
        public static RectInt TownRect => new RectInt(Town.MinX, Town.MinZ, Town.W, Town.H);

        // ---------- комнаты ----------
        public class Room
        {
            public int id, level, lot;
            public float elev;   // высота пола первого этажа этого дома
            public int roofType, roofDir;
            public float roofPitch = 0.62f;
            public string name;
            public readonly List<Vector2Int> cells = new List<Vector2Int>();
            public Styleable st;
        }

        public readonly List<Room> rooms = new List<Room>();
        readonly Dictionary<int, Room> roomById = new Dictionary<int, Room>();
        int[][,] roomOf;   // id комнаты (0 — нет) на каждой клетке города
        int nextRoomId = 1;

        class Edge
        {
            public EdgeKind kind;
            public int level, lot;
            public GameObject root, full, cut, opening;
            public readonly List<Transform> lowSlabs = new List<Transform>();
            public readonly List<GameObject> highSlabs = new List<GameObject>();
            public int state = -1;
            public bool needCut;
            public float baseY;
            public List<Renderer> holes;
        }

        readonly Dictionary<Vector3Int, Edge> edges = new Dictionary<Vector3Int, Edge>();
        Transform roomsRoot, wallsRoot, roofsRoot, treesRoot;
        public Transform propsRoot;
        /// <summary>Клетки, занятые украшениями города (башня, фонари, изгороди): там не ходят.</summary>
        public readonly HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();
        readonly List<(GameObject go, int level, Room room, int lot)> roofs = new List<(GameObject, int, Room, int)>();

        public HashSet<Vector2Int> stairHoles(int level)
        {
            var s = new HashSet<Vector2Int>();
            if (Game.I == null || level <= 0) return s;
            foreach (var f in Game.I.furniture)
                if (f.def.stair && f.level == level - 1)
                    foreach (var c in f.Footprint()) s.Add(c);
            return s;
        }

        public class StairLink
        {
            public Furniture f;
            public int low;
            public Vector2Int bottom, top;
            public Vector3 bottomEdge, topEdge;
        }

        public List<StairLink> StairLinks()
        {
            var l = new List<StairLink>();
            if (Game.I == null) return l;
            foreach (var f in Game.I.furniture)
            {
                if (!f.def.stair || f.level + 1 >= Levels) continue;
                float hd = f.def.d / 2f;
                l.Add(new StairLink
                {
                    f = f, low = f.level,
                    bottom = f.LocalToCell(new Vector2Int(0, -1)),
                    top = f.LocalToCell(new Vector2Int(0, f.def.d)),
                    bottomEdge = f.transform.TransformPoint(new Vector3(0, 0, -hd)),
                    topEdge = f.transform.TransformPoint(new Vector3(0, WallH, hd)),
                });
            }
            return l;
        }

        /// <summary>Путь с учётом этажей: если этажи разные — через ближайшую подходящую лестницу.</summary>
        public List<Vector3> FindPath(int fromLevel, Vector2Int from, int toLevel, Vector2Int to)
        {
            fromLevel = Mathf.Clamp(fromLevel, 0, Levels - 1);
            toLevel = Mathf.Clamp(toLevel, 0, Levels - 1);
            if (fromLevel == toLevel) return grids[fromLevel].FindPath(from, to);
            List<Vector3> best = null;
            foreach (var s in StairLinks())
            {
                bool up = s.low == fromLevel && s.low + 1 == toLevel;
                bool down = s.low == toLevel && s.low + 1 == fromLevel;
                if (!up && !down) continue;
                var a = up ? s.bottom : s.top;
                var b = up ? s.top : s.bottom;
                if (!grids[toLevel].HasFloorAt(b) && toLevel > 0) continue;
                var p1 = grids[fromLevel].FindPath(from, a);
                if (p1 == null) continue;
                var p2 = grids[toLevel].FindPath(b, to);
                if (p2 == null) continue;
                var path = new List<Vector3>(p1);
                if (up) { path.Add(s.bottomEdge); path.Add(s.topEdge); }
                else { path.Add(s.topEdge); path.Add(s.bottomEdge); }
                path.Add(GridMap.Center(b) + Vector3.up * grids[toLevel].HeightAt(b));
                path.AddRange(p2);
                if (best == null || path.Count < best.Count) best = path;
            }
            return best;
        }

        static Vector3Int Key(int level, int x, int z, bool v) => new Vector3Int(x, z, level * 2 + (v ? 1 : 0));

        public static bool ValidEdge(int x, int z, bool v)
        {
            int x0 = Town.MinX, z0 = Town.MinZ, x1 = Town.MinX + Town.W, z1 = Town.MinZ + Town.H;
            return v ? x >= x0 && x <= x1 && z >= z0 && z < z1 : x >= x0 && x < x1 && z >= z0 && z <= z1;
        }

        public static bool IsWallLike(EdgeKind k) => k == EdgeKind.Wall || k == EdgeKind.Door || k == EdgeKind.Window || k == EdgeKind.Arch;
        public static bool IsFenceLike(EdgeKind k) => k == EdgeKind.Fence || k == EdgeKind.Gate;
        public static bool IsBoundary(EdgeKind k) => IsWallLike(k) || IsRailing(k);
        public static bool IsRailing(EdgeKind k) => k == EdgeKind.Railing || k == EdgeKind.RailingOpen;

        bool RoomHasWalls(Room r)
        {
            foreach (var c in r.cells)
            {
                if (IsWallLike(GetEdge(r.level, c.x, c.y, true)) || IsWallLike(GetEdge(r.level, c.x + 1, c.y, true))) return true;
                if (IsWallLike(GetEdge(r.level, c.x, c.y, false)) || IsWallLike(GetEdge(r.level, c.x, c.y + 1, false))) return true;
            }
            return false;
        }

        public void RebuildRoofs() => RebuildRoofs(PlayerLot);

        public void RebuildRoofs(Lot l)
        {
            BuildRoofs(l);
            ApplyVisibility();
        }

        public EdgeKind GetEdge(int level, int x, int z, bool v) => edges.TryGetValue(Key(level, x, z, v), out var e) ? e.kind : EdgeKind.None;
        public EdgeKind GetEdge(int x, int z, bool v) => GetEdge(0, x, z, v);

        public int EdgeLot(int x, int z, bool v)
        {
            Sides(x, z, v, out var a, out var b);
            var l = LotAt(b) ?? LotAt(a);
            return l != null ? l.id : -1;
        }

        public void SetEdge(int level, int x, int z, bool v, EdgeKind k)
        {
            if (!ValidEdge(x, z, v) || level < 0 || level >= Levels) return;
            var key = Key(level, x, z, v);
            if (k == EdgeKind.None)
            {
                if (edges.TryGetValue(key, out var old)) { DestroyEdge(old); edges.Remove(key); }
                return;
            }
            if (!edges.TryGetValue(key, out var e)) edges[key] = e = new Edge { level = level, lot = EdgeLot(x, z, v) };
            e.kind = k;
        }

        public void SetEdge(int x, int z, bool v, EdgeKind k) => SetEdge(0, x, z, v, k);

        static void DestroyEdge(Edge e)
        {
            if (e.root != null) Destroy(e.root);
            if (e.opening != null) Destroy(e.opening);
        }

        public static void Sides(int x, int z, bool v, out Vector2Int a, out Vector2Int b)
        {
            a = v ? new Vector2Int(x - 1, z) : new Vector2Int(x, z - 1);
            b = new Vector2Int(x, z);
        }

        public static float LevelY(int level) => level * WallH;

        // ---------- высоты ----------

        /// <summary>Высота пола в клетке на этаже: у комнаты — пол дома, на улице — земля.</summary>
        public float FloorY(int level, Vector2Int c)
        {
            var r = RoomAt(level, c);
            if (r != null) return r.elev + LevelY(level);
            if (level == 0)
            {
                float deck = Town.BridgeDeck(c.x + 0.5f, c.y + 0.5f);
                if (!float.IsNaN(deck)) return deck;
                return terrain != null ? terrain.SurfaceAt(c) : 0f;
            }
            var below = RoomAt(0, c);
            return (below != null ? below.elev : terrain.SurfaceAt(c)) + LevelY(level);
        }

        /// <summary>На каком этаже точка (по высоте над полом первого этажа в этой клетке).</summary>
        public int LevelOf(Vector3 p)
        {
            var c = GridMap.CellOf(p);
            var r = RoomAt(0, c);
            float b = r != null ? r.elev : terrain != null ? terrain.SurfaceAt(c) : 0f;
            return Mathf.Clamp(Mathf.RoundToInt((p.y - b) / WallH), 0, Levels - 1);
        }

        /// <summary>Высота, на которую ставится предмет: самый высокий пол под ним.</summary>
        public float FurnitureY(FurnitureDef def, Vector2Int a, int rot, int level)
        {
            if (level == 0 && (def.id == "p_bridge" || def.id == "p_bridge2") && terrain != null)
            {
                // мост — на высоте берегов (над водой — чуть выше воды)
                float b = float.MinValue;
                foreach (var c in Furniture.FootprintAt(def, a, rot)) if (!terrain.IsWaterCell(c)) b = Mathf.Max(b, terrain.SurfaceAt(c));
                return b == float.MinValue ? 0.05f : Mathf.Max(b - 0.08f, -0.2f);
            }
            float y = float.MinValue;
            foreach (var c in Furniture.FootprintAt(def, a, rot)) y = Mathf.Max(y, FloorY(level, c));
            return y == float.MinValue ? LevelY(level) : y;
        }

        /// <summary>Высота, от которой стоит ребро (стена, перила, забор).</summary>
        float EdgeBase(int level, int x, int z, bool v)
        {
            Sides(x, z, v, out var a, out var b);
            var ra = RoomAt(level, a); var rb = RoomAt(level, b);
            if (ra != null || rb != null) return Mathf.Max(ra != null ? ra.elev : float.MinValue, rb != null ? rb.elev : float.MinValue) + LevelY(level);
            var ga = RoomAt(0, a); var gb = RoomAt(0, b);
            if (level > 0 && (ga != null || gb != null)) return Mathf.Max(ga != null ? ga.elev : float.MinValue, gb != null ? gb.elev : float.MinValue) + LevelY(level);
            float mx = v ? x : x + 0.5f, mz = v ? z + 0.5f : z;
            return terrain.GroundAt(mx, mz) + LevelY(level);
        }

        /// <summary>Высота дома: самая высокая природная земля под его комнатами (дом на холмике — выше, в котловане — ниже).</summary>
        void ComputeElevations(Lot lot)
        {
            var ground = rooms.FindAll(r => r.lot == lot.id && r.level == 0);
            foreach (var r in ground)
            {
                float e = float.MinValue;
                foreach (var c in r.cells) e = Mathf.Max(e, terrain.NaturalAt(c));
                r.elev = e == float.MinValue ? 0 : e;
            }
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 50)
            {
                changed = false;
                foreach (var r in ground)
                    foreach (var c in r.cells)
                        foreach (var d in dirs)
                        {
                            var o = RoomAt(0, c + d);
                            if (o == null || o == r || o.lot != lot.id || Mathf.Abs(o.elev - r.elev) < 0.001f) continue;
                            float m = Mathf.Max(o.elev, r.elev);
                            o.elev = r.elev = m;
                            changed = true;
                        }
            }
            foreach (var r in rooms)
            {
                if (r.lot != lot.id || r.level == 0) continue;
                float e = float.MinValue;
                foreach (var c in r.cells) { var below = RoomAt(0, c); if (below != null) e = Mathf.Max(e, below.elev); }
                if (e == float.MinValue) foreach (var c in r.cells) e = Mathf.Max(e, terrain.NaturalAt(c));
                r.elev = e;
            }
        }

        public float GroundUnder(Vector3 p) => FloorY(0, GridMap.CellOf(p));

        public static bool InTown(Vector2Int c) => c.x >= Town.MinX && c.y >= Town.MinZ && c.x < Town.MinX + Town.W && c.y < Town.MinZ + Town.H;

        /// <summary>Номер комнаты в клетке (больше 0) или -1. Достаточно проверять «>= 0».</summary>
        public int RoomIndexAt(int level, Vector2Int c)
        {
            if (level < 0 || level >= Levels || roomOf == null || !InTown(c)) return -1;
            int id = roomOf[level][c.x - Town.MinX, c.y - Town.MinZ];
            return id > 0 ? id : -1;
        }

        public int RoomIndexAt(Vector2Int c) => RoomIndexAt(0, c);
        public Room RoomAt(int level, Vector2Int c) { int id = RoomIndexAt(level, c); return id > 0 && roomById.TryGetValue(id, out var r) ? r : null; }
        public Room RoomAt(Vector2Int c) => RoomAt(0, c);

        bool Walkable0(Vector2Int c)
        {
            if (RoomIndexAt(0, c) >= 0) return true;
            if (blocked.Contains(c)) return false;
            if (Town.OnBridge(c.x + 0.5f, c.y + 0.5f)) return true;
            return !terrain.IsWaterCell(c) && terrain.CellRoughness(c) < 0.9f;
        }

        public bool HasFloor(int level, Vector2Int c) => level == 0
            ? InTown(c) && Walkable0(c)
            : RoomIndexAt(level, c) >= 0 && !stairHoles(level).Contains(c);

        public List<Styleable> Surfaces()
        {
            var l = new List<Styleable>();
            foreach (var s in GetComponentsInChildren<Styleable>(true))
                if (!s.hidden && s.displayName != null && (roomsRoot == null || !s.transform.IsChildOf(roomsRoot)) && (treesRoot == null || !s.transform.IsChildOf(treesRoot))) l.Add(s);
            return l;
        }

        // ================= постройка =================

        public void Build() { var e = BuildAsync(); while (e.MoveNext()) { } }

        public System.Collections.IEnumerator BuildAsync()
        {
            Props.Lanterns.Clear();
            Props.LampLights.Clear();
            grids = new GridMap[Levels];
            for (int l = 0; l < Levels; l++) grids[l] = new GridMap(Town.MinX, Town.MinZ, Town.W, Town.H) { baseY = LevelY(l) };
            roomOf = new int[Levels][,];
            for (int l = 0; l < Levels; l++) roomOf[l] = new int[Town.W, Town.H];
            roomsRoot = new GameObject("rooms").transform; roomsRoot.SetParent(transform, false);
            wallsRoot = new GameObject("walls").transform; wallsRoot.SetParent(transform, false);
            roofsRoot = new GameObject("roofs").transform; roofsRoot.SetParent(transform, false);
            treesRoot = new GameObject("trees").transform; treesRoot.SetParent(transform, false);
            propsRoot = new GameObject("props").transform; propsRoot.SetParent(transform, false);

            foreach (var def in Town.Lots) { var nl = MakeLot(def.name, def.rect, def.kind, def.frontSouth); nl.removed = def.removed; lots.Add(nl); }

            // невидимая земля улиц — чтобы кликом отправить лисичку куда угодно по городу
            var streets = NewStyleable("Улицы", SurfaceKind.Outdoor);
            streets.hidden = true;
            streets.AddPart("Улица", StyleData.Make("Гладкий", "#C9BBA3"));
            var tr = TownRect;
            Shapes.Box(streets, 0, streets.transform, new Vector3(tr.center.x, -0.02f, tr.center.y), new Vector3(tr.width, 0.02f, tr.height)).GetComponent<Renderer>().forceRenderingOff = true;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            terrain = new GameObject("Земля").AddComponent<VoxelTerrain>();
            terrain.transform.SetParent(transform, false);
            var tb = terrain.BuildAsync();
            while (tb.MoveNext()) yield return null;
            if (Game.I != null) { Game.I.loadingProgress = 0.4f; Game.I.loadingText = "Ставим часовую башню, мельницу и мосты…"; }
            yield return null;
            long tTerrain = sw.ElapsedMilliseconds;
            Town.BuildLandmarks(this);
            yield return null;
            UpdateGrid(TownRect);
            if (Game.I != null) { Game.I.loadingProgress = 0.5f; Game.I.loadingText = "Сажаем лес и рощи…"; }
            yield return null;
            var pt = Town.PlantTownTreesAsync(this);
            while (pt.MoveNext()) yield return null;
            long tTrees = sw.ElapsedMilliseconds;
            BuildYard();
            DefaultLayout();
            Debug.Log($"FoxLife: время постройки — земля {tTerrain} мс, деревья {tTrees - tTerrain} мс, дом {sw.ElapsedMilliseconds - tTrees} мс");
        }

        Lot MakeLot(string name, RectInt rect, LotKind kind, bool frontSouth)
        {
            var l = new Lot { id = lots.Count, name = name, rect = rect, kind = kind, frontSouth = frontSouth };
            l.root = new GameObject("Участок: " + name).transform;
            l.root.SetParent(transform, false);
            l.facade = NewStyleable("Фасад", SurfaceKind.Wall, l.root);
            l.facade.AddPart("Фасад", StyleData.Make("Доски", "#E3B98C", null, 1.2f));
            l.openings = NewStyleable("Окна и двери", SurfaceKind.Wall, l.root);
            l.openings.AddPart("Рамы", StyleData.Make("Гладкий", "#FFF8EE", null, 1, 0.4f));
            l.openings.AddPart("Стекло", StyleData.Make("Гладкий", "#CDE8F5", null, 1, 0.95f), true);
            l.openings.AddPart("Дверь", StyleData.Make("Доски", "#A8774F"));
            l.openings.AddPart("Ставни", StyleData.Make("Доски", "#5B8FB9", null, 2f));
            l.openings.AddPart("Ящик для цветов", StyleData.Make("Доски", "#A8774F", null, 2f));
            l.openings.AddPart("Цветы на окнах", StyleData.Make("Горошек", "#F5A3C1", "#FFF2B3", 3f));
            l.railing = NewStyleable("Перила и колонны", SurfaceKind.Wall, l.root);
            l.railing.AddPart("Перила", StyleData.Make("Гладкий", "#FFF8EE", null, 1, 0.3f));
            l.railing.AddPart("Колонны", StyleData.Make("Гладкий", "#F4EBDD", null, 1, 0.3f));
            l.fence = NewStyleable("Забор", SurfaceKind.Wall, l.root);
            l.fence.AddPart("Забор", StyleData.Make("Доски", "#F4EBDD", null, 1.5f));
            l.roof = NewStyleable("Крыша", SurfaceKind.Wall, l.root);
            l.roof.AddPart("Черепица", StyleData.Make("Плитка", "#DE8A70", "#C9745D", 2.5f));
            l.roof.AddPart("Конёк", StyleData.Make("Гладкий", "#B95F4A"));
            l.roof.AddPart("Труба", StyleData.Make("Кирпич", "#C98B76", "#EADBD0", 2f));
            l.ground = NewStyleable("Участок", SurfaceKind.Outdoor, l.root);
            l.ground.hidden = true;
            l.ground.AddPart("Участок", StyleData.Make("Гладкий", "#9ACB7C"));
            var c = rect.center;
            Shapes.Box(l.ground, 0, l.ground.transform, new Vector3(c.x, -0.012f, c.y), new Vector3(rect.width, 0.02f, rect.height)).GetComponent<Renderer>().forceRenderingOff = true;
            l.entry = new Vector2Int((int)c.x, frontSouth ? rect.yMin : rect.yMax - 1);
            return l;
        }

        public void ClearEdges(Lot l)
        {
            var keys = new List<Vector3Int>();
            foreach (var kv in edges) if (kv.Value.lot == l.id) keys.Add(kv.Key);
            foreach (var k in keys) { DestroyEdge(edges[k]); edges.Remove(k); }
        }

        void ClearAllEdges()
        {
            foreach (var e in edges.Values) DestroyEdge(e);
            edges.Clear();
        }

        /// <summary>Стартовый дом: гостиная с кухней и спальня, проём между ними, дверь, окна, заборчик.</summary>
        public void DefaultLayout()
        {
            ClearEdges(PlayerLot);
            const int x0 = 0, z0 = 0, x1 = 14, z1 = 10, split = 9;
            for (int x = x0; x < x1; x++) { SetEdge(x, z0, false, EdgeKind.Wall); SetEdge(x, z1, false, EdgeKind.Wall); }
            for (int z = z0; z < z1; z++) { SetEdge(x0, z, true, EdgeKind.Wall); SetEdge(x1, z, true, EdgeKind.Wall); SetEdge(split, z, true, EdgeKind.Wall); }
            SetEdge(split, 4, true, EdgeKind.Arch); SetEdge(split, 5, true, EdgeKind.Arch);
            SetEdge(4, 0, false, EdgeKind.Door);
            foreach (var x in new[] { 1, 7, 11 }) SetEdge(x, 0, false, EdgeKind.Window);
            foreach (var x in new[] { 4, 11 }) SetEdge(x, 10, false, EdgeKind.Window);
            foreach (var z in new[] { 3, 7 }) SetEdge(0, z, true, EdgeKind.Window);
            SetEdge(14, 2, true, EdgeKind.Window);
            for (int z = 0; z < 14; z++) SetEdge(19, z, true, EdgeKind.Fence);
            SetEdge(19, 6, true, EdgeKind.Gate);
            Rebuild();
            var living = RoomAt(new Vector2Int(1, 1));
            if (living != null)
            {
                living.name = "Гостиная"; living.st.displayName = living.name;
                living.st.SetStyle(0, StyleData.Make("Доски", "#B9844F"));
                living.st.SetStyle(1, StyleData.Make("Полоска", "#F1E4CF", "#E8D6B8", 0.6f));
            }
            var bed = RoomAt(new Vector2Int(12, 1));
            if (bed != null)
            {
                bed.name = "Спальня"; bed.st.displayName = bed.name;
                bed.st.SetStyle(0, StyleData.Make("Паркет", "#C99A66", "#A9764A"));
                bed.st.SetStyle(1, StyleData.Make("Цветочки", "#DCE7E1", "#C3D6CB", 0.8f));
            }
        }

        public List<(int level, int x, int z, bool v, EdgeKind k)> Layout()
        {
            var l = new List<(int, int, int, bool, EdgeKind)>();
            foreach (var kv in edges) l.Add((kv.Key.z / 2, kv.Key.x, kv.Key.y, kv.Key.z % 2 == 1, kv.Value.kind));
            return l;
        }

        public System.Collections.IEnumerator SetLayoutAsync(List<(int level, int x, int z, bool v, EdgeKind k)> layout)
        {
            ClearAllEdges();
            foreach (var t in layout) SetEdge(t.level, t.x, t.z, t.v, t.k);
            foreach (var l in lots) { Rebuild(l); yield return null; }
        }

        public void SetLayout(List<(int level, int x, int z, bool v, EdgeKind k)> layout)
        {
            ClearAllEdges();
            foreach (var t in layout) SetEdge(t.level, t.x, t.z, t.v, t.k);
            RebuildAll();
        }

        Styleable NewStyleable(string name, SurfaceKind kind, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            var s = go.AddComponent<Styleable>();
            s.displayName = name;
            s.kind = kind;
            return s;
        }

        // ================= пересборка =================

        public void Rebuild() => Rebuild(PlayerLot);

        public void RebuildAll() { foreach (var l in lots) Rebuild(l); }

        /// <summary>Пересобрать один участок: комнаты, полы, стены, проёмы, крыши, сетку пути, землю под домом.</summary>
        public void Rebuild(Lot lot)
        {
            if (lot == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pt = new long[4];
            FindRooms(lot);
            pt[0] = sw.ElapsedMilliseconds;

            foreach (var e in edges.Values)
            {
                if (e.lot != lot.id) continue;
                DestroyEdge(e);
                e.root = e.full = e.cut = e.opening = null;
                e.lowSlabs.Clear(); e.highSlabs.Clear();
                e.state = -1;
            }
            lot.facade.ClearRenderers();
            lot.openings.ClearRenderers();
            lot.fence.ClearRenderers();
            lot.railing.ClearRenderers();

            foreach (var r in rooms)
            {
                if (r.lot != lot.id) continue;
                r.st.ClearRenderers();
                foreach (Transform ch in r.st.transform) Destroy(ch.gameObject);
                var floor = new GameObject("floor").transform;
                floor.SetParent(r.st.transform, false);
                var holes = stairHoles(r.level);
                float y = r.elev + LevelY(r.level);
                var left = new HashSet<Vector2Int>(r.cells);
                left.ExceptWith(holes);
                foreach (var c in r.cells)
                {
                    if (!left.Contains(c)) continue;
                    int x1 = c.x;
                    while (left.Contains(new Vector2Int(x1 + 1, c.y))) x1++;
                    int z1 = c.y;
                    while (true)
                    {
                        bool row = true;
                        for (int x = c.x; x <= x1 && row; x++) row = left.Contains(new Vector2Int(x, z1 + 1));
                        if (!row) break;
                        z1++;
                    }
                    for (int x = c.x; x <= x1; x++) for (int z = c.y; z <= z1; z++) left.Remove(new Vector2Int(x, z));
                    float w = x1 - c.x + 1, d = z1 - c.y + 1;
                    Shapes.Box(r.st, 0, floor, new Vector3(c.x + w / 2f, y - 0.05f, c.y + d / 2f), new Vector3(w, 0.1f, d));
                }
                Voxelizer.Bake(floor, Voxelizer.HouseVoxel);
            }
            pt[1] = sw.ElapsedMilliseconds;

            for (int level = 0; level < Levels; level++)
            {
                var posts = CornerPosts(level, lot.rect);
                var columns = RailingColumns(level, lot.rect);
                foreach (var kv in edges)
                {
                    if (kv.Key.z / 2 != level || kv.Value.lot != lot.id) continue;
                    posts.TryGetValue(kv.Key, out var p);
                    if (IsFenceLike(kv.Value.kind)) BuildFence(kv.Key, kv.Value, lot);
                    else if (IsRailing(kv.Value.kind)) { columns.TryGetValue(kv.Key, out var cols); BuildRailing(kv.Key, kv.Value, cols, lot); }
                    else BuildEdge(kv.Key, kv.Value, p, lot);
                }
            }

            pt[2] = sw.ElapsedMilliseconds;

            BuildRoofs(lot);
            pt[3] = sw.ElapsedMilliseconds;
            terrain.SetLowered(lot.rect, (x, z) => { var r = RoomAt(0, new Vector2Int(x, z)); return r != null ? r.elev : float.NaN; });
            var gr = new RectInt(lot.rect.xMin - 1, lot.rect.yMin - 1, lot.rect.width + 2, lot.rect.height + 2);
            UpdateGrid(gr);
            FindEntry(lot);
            ApplyVisibility();
        }

        Dictionary<Vector3Int, List<(Vector3 c, Vector2Int cell)>> CornerPosts(int level, RectInt rect)
        {
            var posts = new Dictionary<Vector3Int, List<(Vector3 c, Vector2Int cell)>>();
            bool Has(int x, int z, bool v) => IsWallLike(GetEdge(level, x, z, v));
            for (int vx = rect.xMin; vx <= rect.xMax; vx++)
                for (int vz = rect.yMin; vz <= rect.yMax; vz++)
                {
                    bool east = Has(vx, vz, false), west = Has(vx - 1, vz, false);
                    bool north = Has(vx, vz, true), south = Has(vx, vz - 1, true);
                    int count = (east ? 1 : 0) + (west ? 1 : 0) + (north ? 1 : 0) + (south ? 1 : 0);
                    if (count < 2) continue;
                    Vector3Int owner = east ? Key(level, vx, vz, false) : west ? Key(level, vx - 1, vz, false) : north ? Key(level, vx, vz, true) : Key(level, vx, vz - 1, true);
                    void Q(bool covered, float dx, float dz, Vector2Int cell)
                    {
                        if (covered) return;
                        if (!posts.TryGetValue(owner, out var l)) posts[owner] = l = new List<(Vector3, Vector2Int)>();
                        l.Add((new Vector3(vx + dx * Half / 2f, 0, vz + dz * Half / 2f), cell));
                    }
                    Q(east || north, 1, 1, new Vector2Int(vx, vz));
                    Q(west || north, -1, 1, new Vector2Int(vx - 1, vz));
                    Q(east || south, 1, -1, new Vector2Int(vx, vz - 1));
                    Q(west || south, -1, -1, new Vector2Int(vx - 1, vz - 1));
                }
            return posts;
        }

        void FindRooms(Lot lot)
        {
            var rect = lot.rect;
            var old = rooms.FindAll(r => r.lot == lot.id);
            rooms.RemoveAll(r => r.lot == lot.id);
            foreach (var r in old) roomById.Remove(r.id);
            // старые номера комнат в прямоугольнике — для наследования стиля
            var oldIds = new int[Levels, rect.width, rect.height];
            for (int level = 0; level < Levels; level++)
                for (int x = 0; x < rect.width; x++)
                    for (int z = 0; z < rect.height; z++)
                    {
                        int ix = rect.xMin + x - Town.MinX, iz = rect.yMin + z - Town.MinZ;
                        oldIds[level, x, z] = roomOf[level][ix, iz];
                        roomOf[level][ix, iz] = 0;
                    }
            var used = new HashSet<Room>();
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            for (int level = 0; level < Levels; level++)
            {
                var seen = new bool[rect.width, rect.height];
                for (int sx = 0; sx < rect.width; sx++)
                    for (int sz = 0; sz < rect.height; sz++)
                    {
                        if (seen[sx, sz]) continue;
                        var cells = new List<Vector2Int>();
                        bool isOut = false;
                        var stack = new Stack<Vector2Int>();
                        var s0 = new Vector2Int(rect.xMin + sx, rect.yMin + sz);
                        stack.Push(s0);
                        seen[sx, sz] = true;
                        while (stack.Count > 0)
                        {
                            var c = stack.Pop();
                            cells.Add(c);
                            foreach (var d in dirs)
                            {
                                var n = c + d;
                                bool boundary = WallBetween(level, c, n);
                                if (!rect.Contains(n)) { if (!boundary) isOut = true; continue; }
                                if (boundary || seen[n.x - rect.xMin, n.y - rect.yMin]) continue;
                                seen[n.x - rect.xMin, n.y - rect.yMin] = true;
                                stack.Push(n);
                            }
                        }
                        if (isOut) continue;

                        Room best = null; int bestN = 0;
                        var counts = new Dictionary<int, int>();
                        foreach (var c in cells) { int o = oldIds[level, c.x - rect.xMin, c.y - rect.yMin]; if (o > 0) counts[o] = counts.TryGetValue(o, out var n0) ? n0 + 1 : 1; }
                        foreach (var kv in counts)
                        {
                            var cand = old.Find(r => r.id == kv.Key);
                            if (cand != null && kv.Value > bestN && !used.Contains(cand)) { bestN = kv.Value; best = cand; }
                        }
                        Room room;
                        if (best != null) { room = best; used.Add(best); room.cells.Clear(); }
                        else
                        {
                            room = new Room { id = nextRoomId++, level = level, lot = lot.id };
                            room.name = (level > 0 ? "Комната наверху " : "Комната ") + room.id;
                            room.st = NewStyleable(room.name, SurfaceKind.Floor, roomsRoot);
                            room.st.AddPart("Пол", StyleData.Make("Доски", "#D2A679"));
                            room.st.AddPart("Стены", StyleData.Make("Гладкий", "#F4E6D2"));
                        }
                        room.level = level;
                        room.lot = lot.id;
                        room.cells.AddRange(cells);
                        if (best == null && !RoomHasWalls(room))
                        {
                            room.name = "Веранда " + room.id; room.st.displayName = room.name;
                            room.roofType = (int)RoofType.Hip;
                        }
                        foreach (var c in cells) roomOf[level][c.x - Town.MinX, c.y - Town.MinZ] = room.id;
                        rooms.Add(room);
                        roomById[room.id] = room;
                    }
            }
            foreach (var r in old) if (!used.Contains(r) && r.st != null) Destroy(r.st.gameObject);
            ComputeElevations(lot);
        }

        bool WallBetween(int level, Vector2Int a, Vector2Int b)
        {
            if (a.y == b.y && Mathf.Abs(a.x - b.x) == 1) return IsBoundary(GetEdge(level, Mathf.Max(a.x, b.x), a.y, true));
            if (a.x == b.x && Mathf.Abs(a.y - b.y) == 1) return IsBoundary(GetEdge(level, a.x, Mathf.Max(a.y, b.y), false));
            return false;
        }

        (Styleable st, int part) SideStyle(int level, Vector2Int cell, Lot lot)
        {
            var r = RoomAt(level, cell);
            return r != null ? (r.st, 1) : (lot.facade, 0);
        }

        void BuildEdge(Vector3Int key, Edge e, List<(Vector3 c, Vector2Int cell)> posts, Lot lot)
        {
            bool v = key.z % 2 == 1;
            int level = key.z / 2;
            int x = key.x, z = key.y;
            float by = EdgeBase(level, x, z, v);
            e.baseY = by;
            Sides(x, z, v, out var ca, out var cb);
            var sa = SideStyle(level, ca, lot);
            var sb = SideStyle(level, cb, lot);
            e.root = new GameObject($"edge {level}:{x},{z},{(v ? "|" : "-")}");
            e.root.transform.SetParent(wallsRoot, false);
            var rt = e.root.transform;
            var prims = new List<Renderer>();

            float y0 = e.kind == EdgeKind.Arch ? DoorH : 0f;
            float h = WallH - y0;
            Vector3 ctr = (v ? new Vector3(x, 0, z + 0.5f) : new Vector3(x + 0.5f, 0, z)) + Vector3.up * by;
            Vector3 off = v ? new Vector3(Half / 2f, 0, 0) : new Vector3(0, 0, Half / 2f);
            Vector3 size = v ? new Vector3(Half, h, 1f) : new Vector3(1f, h, Half);
            var slabA = Shapes.Box(sa.st, sa.part, rt, ctr - off + Vector3.up * (y0 + h / 2f), size);
            var slabB = Shapes.Box(sb.st, sb.part, rt, ctr + off + Vector3.up * (y0 + h / 2f), size);
            prims.Add(slabA.GetComponent<Renderer>());
            prims.Add(slabB.GetComponent<Renderer>());
            if (y0 > 0) { e.highSlabs.Add(slabA); e.highSlabs.Add(slabB); }
            else { e.lowSlabs.Add(slabA.transform); e.lowSlabs.Add(slabB.transform); }
            if (posts != null)
                foreach (var p in posts)
                {
                    var ps = SideStyle(level, p.cell, lot);
                    var post = Shapes.Box(ps.st, ps.part, rt, p.c + Vector3.up * (by + WallH / 2f), new Vector3(Half, WallH, Half));
                    prims.Add(post.GetComponent<Renderer>());
                    e.lowSlabs.Add(post.transform);
                }

            List<Renderer> holes = null;
            if (e.kind == EdgeKind.Door || e.kind == EdgeKind.Window)
            {
                e.opening = BuildOpening(ctr, !v, e.kind == EdgeKind.Door, lot);
                holes = new List<Renderer>(e.opening.GetComponentsInChildren<Renderer>(true));
                holes.RemoveAll(r => r.transform.parent != null && r.transform.parent.name == "створка");
                bool aIn = RoomIndexAt(level, ca) >= 0, bIn = RoomIndexAt(level, cb) >= 0;
                if (e.kind == EdgeKind.Window && aIn != bIn && lot.windowDecor)
                    WindowDecor(e.opening.transform, ctr, !v, aIn ? 1f : -1f, lot, level);
            }

            float vox = Voxelizer.HouseVoxel;
            e.full = Voxelizer.BakeRenderers(transform, prims, vox, rt, holes);
            // низкая «срезанная» стена строится, только когда её впервые опустят
            e.needCut = y0 == 0;
            e.holes = holes;
            if (e.opening != null)
            {
                var hl = e.opening.transform.Find("створка");
                if (hl != null)
                {
                    Voxelizer.Bake(hl, vox);
                    hl.localRotation = Quaternion.identity;
                }
                Voxelizer.Bake(e.opening.transform, vox);
            }
            foreach (var pl in pendingLeaves) doors.Add(new DoorLeaf { pivot = pl.t, center = pl.t.parent.TransformPoint(pl.c), lot = lot.id, level = level, a = ca, b = cb });
            pendingLeaves.Clear();
        }

        static void ShadowOnly(GameObject go, bool on)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        }

        void EnsureCut(Edge e, int level)
        {
            if (!e.needCut || e.cut != null || e.root == null) return;
            e.needCut = false;
            float by = e.baseY;
            var low = new List<Renderer>();
            foreach (var s in e.lowSlabs) { var r = s.GetComponent<Renderer>(); r.forceRenderingOff = false; low.Add(r); SetHeight(s, by, CutH); }
            e.cut = Voxelizer.BakeRenderers(transform, low, Voxelizer.HouseVoxel, e.root.transform, e.holes);
            foreach (var s in e.lowSlabs) SetHeight(s, by, WallH);
            foreach (var r in low) r.forceRenderingOff = true;
            if (e.cut != null) e.cut.SetActive(false);
        }

        Dictionary<Vector3Int, List<Vector3>> RailingColumns(int level, RectInt rect)
        {
            var map = new Dictionary<Vector3Int, List<Vector3>>();
            bool R(int x, int z, bool v) => IsRailing(GetEdge(level, x, z, v));
            for (int vx = rect.xMin; vx <= rect.xMax; vx++)
                for (int vz = rect.yMin; vz <= rect.yMax; vz++)
                {
                    Vector3Int? owner = null;
                    if (R(vx, vz, false)) owner = Key(level, vx, vz, false);
                    else if (R(vx - 1, vz, false)) owner = Key(level, vx - 1, vz, false);
                    else if (R(vx, vz, true)) owner = Key(level, vx, vz, true);
                    else if (R(vx, vz - 1, true)) owner = Key(level, vx, vz - 1, true);
                    if (owner == null) continue;
                    if (!map.TryGetValue(owner.Value, out var l)) map[owner.Value] = l = new List<Vector3>();
                    l.Add(new Vector3(vx, 0, vz));
                }
            return map;
        }

        void BuildRailing(Vector3Int key, Edge e, List<Vector3> columns, Lot lot)
        {
            bool v = key.z % 2 == 1;
            int level = key.z / 2;
            int x = key.x, z = key.y;
            float by = EdgeBase(level, x, z, v);
            var railing = lot.railing;
            e.root = new GameObject($"railing {level}:{x},{z}");
            e.root.transform.SetParent(wallsRoot, false);
            var rt = e.root.transform;
            var a = new Vector3(x, by, z);
            var b = v ? new Vector3(x, by, z + 1) : new Vector3(x + 1, by, z);
            var dir = (b - a).normalized;
            Vector3 S(float along, float hgt, float th) => v ? new Vector3(th, hgt, along) : new Vector3(along, hgt, th);
            var mid = (a + b) / 2f;
            if (e.kind == EdgeKind.Railing)
            {
                Shapes.Box(railing, 0, rt, mid + Vector3.up * 0.86f, S(1f, 0.08f, 0.125f));
                Shapes.Box(railing, 0, rt, mid + Vector3.up * 0.12f, S(1f, 0.06f, 0.0625f));
                for (int i = 1; i < 5; i++)
                    Shapes.Box(railing, 0, rt, a + dir * (i * 0.2f) + Vector3.up * 0.48f, S(0.0625f, 0.7f, 0.0625f));
            }
            if (columns != null)
                foreach (var c0 in columns)
                {
                    var c = c0 + Vector3.up * by;
                    Shapes.Box(railing, 1, rt, c + Vector3.up * WallH / 2f, new Vector3(0.16f, WallH, 0.16f));
                    Shapes.Box(railing, 1, rt, c + Vector3.up * 0.05f, new Vector3(0.26f, 0.1f, 0.26f));
                    Shapes.Box(railing, 1, rt, c + Vector3.up * (WallH - 0.05f), new Vector3(0.26f, 0.1f, 0.26f));
                }
            e.full = Voxelizer.Bake(rt, Voxelizer.HouseVoxel);
        }

        void BuildFence(Vector3Int key, Edge e, Lot lot)
        {
            bool v = key.z % 2 == 1;
            int x = key.x, z = key.y;
            var fence = lot.fence;
            e.root = new GameObject($"fence {x},{z}");
            e.root.transform.SetParent(wallsRoot, false);
            var rt = e.root.transform;
            float fy = EdgeBase(0, x, z, v);
            Vector3 a = new Vector3(x, fy, z);
            Vector3 b = v ? new Vector3(x, fy, z + 1) : new Vector3(x + 1, fy, z);
            Vector3 dir = (b - a).normalized;
            Vector3 S(float along, float hgt, float thick) => v ? new Vector3(thick, hgt, along) : new Vector3(along, hgt, thick);
            float post = 0.125f;
            Shapes.Box(fence, 0, rt, a + Vector3.up * FenceH / 2f + dir * post / 2f, S(post, FenceH, post));
            Shapes.Box(fence, 0, rt, b + Vector3.up * FenceH / 2f - dir * post / 2f, S(post, FenceH, post));
            var mid = (a + b) / 2f;
            if (e.kind == EdgeKind.Fence)
            {
                Shapes.Box(fence, 0, rt, mid + Vector3.up * 0.3f, S(1f - post * 2, 0.1f, 0.0625f));
                Shapes.Box(fence, 0, rt, mid + Vector3.up * 0.62f, S(1f - post * 2, 0.1f, 0.0625f));
                for (int i = 0; i < 3; i++)
                    Shapes.Box(fence, 0, rt, a + dir * (0.3f + i * 0.2f) + Vector3.up * 0.4f, S(0.0625f, 0.7f, 0.0625f));
            }
            else
            {
                for (int i = 0; i < 4; i++)
                    Shapes.Box(fence, 0, rt, a + dir * (0.22f + i * 0.19f) + Vector3.up * 0.32f, S(0.0625f, 0.56f, 0.0625f));
                Shapes.Box(fence, 0, rt, mid + Vector3.up * 0.5f, S(1f - post * 2, 0.08f, 0.0625f));
            }
            e.full = Voxelizer.Bake(rt, Voxelizer.HouseVoxel);
        }

        static void SetHeight(Transform t, float baseY, float h)
        {
            var sc = t.localScale; sc.y = h; t.localScale = sc;
            var p = t.localPosition; p.y = baseY + h / 2f; t.localPosition = p;
        }

        /// <summary>Створка двери: распахивается, когда рядом лисичка, и закрывается за ней.</summary>
        public class DoorLeaf
        {
            public Transform pivot;
            public Vector3 center;
            public float angle;
            public int lot = -1, level;
            public Vector2Int a, b;   // клетки по обе стороны двери
        }

        public readonly List<DoorLeaf> doors = new List<DoorLeaf>();
        readonly List<(Transform t, Vector3 c)> pendingLeaves = new List<(Transform t, Vector3 c)>();

        /// <summary>Клетка снаружи двери (не в комнате) и внутри.</summary>
        /// <summary>Снаружи — сторона без комнаты; если комнаты с обеих сторон (крыльцо-веранда) — та, что ближе к входу участка.</summary>
        bool AOutside(DoorLeaf d)
        {
            bool ra = RoomAt(d.level, d.a) != null, rb = RoomAt(d.level, d.b) != null;
            if (ra != rb) return !ra;
            var lot = LotById(d.lot);
            if (lot == null) return true;
            return (d.a - lot.entry).sqrMagnitude <= (d.b - lot.entry).sqrMagnitude;
        }
        public Vector2Int DoorOutside(DoorLeaf d) => AOutside(d) ? d.a : d.b;
        public Vector2Int DoorInside(DoorLeaf d) => AOutside(d) ? d.b : d.a;

        /// <summary>Входная дверь дома: на первом этаже, ведёт на улицу, ближайшая к входу участка.</summary>
        public DoorLeaf FrontDoor(int lotId) => FrontDoor(lotId, true) ?? FrontDoor(lotId, false);

        DoorLeaf FrontDoor(int lotId, bool strict)
        {
            var lot = LotById(lotId);
            DoorLeaf best = null; float bd = float.MaxValue;
            foreach (var d in doors)
            {
                if (d.pivot == null || d.lot != lotId || d.level != 0) continue;
                if (strict && (RoomAt(0, d.a) == null) == (RoomAt(0, d.b) == null)) continue; // дверь между комнатами
                float dist = lot != null ? (GridMap.Center(lot.entry) - d.center).sqrMagnitude : 0f;
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        /// <summary>Дверь в этой точке (клик по двери или стене рядом с ней).</summary>
        public DoorLeaf DoorNear(Vector3 p)
        {
            foreach (var d in doors)
            {
                if (d.pivot == null) continue;
                float dy = p.y - d.center.y;
                if (dy < -0.2f || dy > 1.75f) continue;
                float dx = p.x - d.center.x, dz = p.z - d.center.z;
                if (dx * dx + dz * dz < 0.5f * 0.5f) return d;
            }
            return null;
        }

        public void UpdateDoors(List<Fox> foxes, float dt)
        {
            for (int i = doors.Count - 1; i >= 0; i--)
            {
                var d = doors[i];
                if (d.pivot == null) { doors.RemoveAt(i); continue; }
                bool near = false;
                foreach (var f in foxes)
                {
                    if (f == null || !f.gameObject.activeInHierarchy) continue;
                    var p = f.transform.position;
                    float dy = p.y - d.center.y;
                    if (dy < -0.6f || dy > 1.6f) continue;
                    float dx = p.x - d.center.x, dz = p.z - d.center.z;
                    if (dx * dx + dz * dz >= 1.35f * 1.35f) continue;
                    if (d.lot >= 0 && !f.CanEnter(d.lot)) continue; // без ключа дверь не открывается
                    near = true; break;
                }
                float target = near ? 92f : 0f;
                if (Mathf.Abs(d.angle - target) < 0.1f) continue;
                d.angle = Mathf.MoveTowards(d.angle, target, dt * (near ? 300f : 160f));
                d.pivot.localRotation = Quaternion.Euler(0, d.angle, 0);
            }
        }

        GameObject BuildOpening(Vector3 center, bool alongX, bool door, Lot lot)
        {
            var openings = lot.openings;
            var g = new GameObject(door ? "door" : "window").transform;
            g.SetParent(openings.transform, false);
            float d = Half * 2f + 0.04f;
            Vector3 S(float a, float b, float c) => alongX ? new Vector3(a, b, c) : new Vector3(c, b, a);
            Vector3 P(float a, float b) => center + (alongX ? new Vector3(a, b, 0) : new Vector3(0, b, a));
            if (door)
            {
                float w = 0.78f, h = 1.5f;
                Shapes.Box(openings, 0, g, P(0, h + 0.04f), S(w + 0.16f, 0.08f, d));
                Shapes.Box(openings, 0, g, P(-w / 2 - 0.04f, h / 2), S(0.08f, h, d));
                Shapes.Box(openings, 0, g, P(w / 2 + 0.04f, h / 2), S(0.08f, h, d));
                // створка — на отдельной петле, открывается, когда лисичка подходит
                var hinge = new GameObject("створка").transform;
                hinge.SetParent(g, false);
                var hp = P(-w / 2, 0);
                hinge.localPosition = hp;
                float leafD = Mathf.Min(0.1f, d - 0.02f);
                Shapes.Box(openings, 2, hinge, P(0, h / 2) - hp, S(w, h, leafD));
                Shapes.Box(openings, 1, hinge, P(0, h * 0.72f) - hp, S(0.3f, 0.3f, leafD + 0.01f));
                Shapes.Ball(openings, 0, hinge, P(0.27f, 0.75f) - hp, new Vector3(0.07f, 0.07f, 0.07f) + S(0, 0, leafD + 0.06f));
                // дыра в стене — на весь проём (створка тоньше стены)
                var hole = Shapes.Box(openings, 2, g, P(0, h / 2), S(w, h, d - 0.02f));
                hole.GetComponent<Renderer>().forceRenderingOff = true;
                pendingLeaves.Add((hinge, center));
            }
            else
            {
                float w = 0.76f, h = 0.85f, y = 1.15f;
                Shapes.Box(openings, 1, g, P(0, y), S(w, h, d - 0.02f));
                Shapes.Box(openings, 0, g, P(0, y + h / 2), S(w + 0.12f, 0.08f, d));
                Shapes.Box(openings, 0, g, P(0, y - h / 2), S(w + 0.16f, 0.09f, d + 0.06f));
                Shapes.Box(openings, 0, g, P(-w / 2, y), S(0.07f, h, d));
                Shapes.Box(openings, 0, g, P(w / 2, y), S(0.07f, h, d));
                Shapes.Box(openings, 0, g, P(0, y), S(0.045f, h, d + 0.005f));
                Shapes.Box(openings, 0, g, P(0, y + 0.1f), S(w, 0.04f, d + 0.005f));
            }
            return g.gameObject;
        }

        /// <summary>Ставни по бокам окна и ящик с цветами под ним — с наружной стороны.</summary>
        void WindowDecor(Transform g, Vector3 center, bool alongX, float outSign, Lot lot, int level)
        {
            var o = lot.openings;
            Vector3 N = (alongX ? Vector3.forward : Vector3.right) * outSign;
            Vector3 A = alongX ? Vector3.right : Vector3.forward;
            Vector3 S(float a, float h, float n) => alongX ? new Vector3(a, h, n) : new Vector3(n, h, a);
            float y = 1.15f;
            foreach (var side in new[] { -1f, 1f })
            {
                var p = center + A * side * 0.56f + N * (Half + 0.05f) + Vector3.up * y;
                Shapes.Box(o, 3, g, p, S(0.32f, 0.86f, 0.05f));
                Shapes.Box(o, 3, g, p + N * 0.02f + Vector3.up * 0.2f, S(0.22f, 0.04f, 0.03f));
                Shapes.Box(o, 3, g, p + N * 0.02f - Vector3.up * 0.2f, S(0.22f, 0.04f, 0.03f));
            }
            var bp = center + N * (Half + 0.13f) + Vector3.up * (y - 0.56f);
            Shapes.Box(o, 4, g, bp, S(0.84f, 0.18f, 0.2f));
            for (int i = 0; i < 4; i++)
                Shapes.Ball(o, 5, g, bp + A * (-0.3f + i * 0.2f) + Vector3.up * 0.13f, S(0.22f, 0.18f, 0.2f));
        }

        // ================= крыши =================

        void BuildRoofs(Lot lot)
        {
            for (int i = roofs.Count - 1; i >= 0; i--)
                if (roofs[i].lot == lot.id) { if (roofs[i].go != null) Destroy(roofs[i].go); roofs.RemoveAt(i); }
            lot.roof.ClearRenderers();
            var rect = lot.rect;
            var seen = new bool[Levels, rect.width, rect.height];
            var rects = new List<(int level, int x0, int z0, int x1, int z1, Room room)>();
            for (int level = 0; level < Levels; level++)
                for (int sx = 0; sx < rect.width; sx++)
                    for (int sz = 0; sz < rect.height; sz++)
                    {
                        bool Top(int lx, int lz)
                        {
                            var c = new Vector2Int(rect.xMin + lx, rect.yMin + lz);
                            var rr = RoomAt(level, c);
                            if (rr == null || (level + 1 < Levels && RoomIndexAt(level + 1, c) >= 0)) return false;
                            var r0 = RoomAt(level, new Vector2Int(rect.xMin + sx, rect.yMin + sz));
                            return r0 == null || Mathf.Abs(r0.elev - rr.elev) < 0.001f;
                        }
                        if (seen[level, sx, sz] || !Top(sx, sz)) continue;
                        int x1 = sx;
                        while (x1 + 1 < rect.width && !seen[level, x1 + 1, sz] && Top(x1 + 1, sz)) x1++;
                        int z1 = sz;
                        while (z1 + 1 < rect.height)
                        {
                            bool row = true;
                            for (int x = sx; x <= x1 && row; x++) row = !seen[level, x, z1 + 1] && Top(x, z1 + 1);
                            if (!row) break;
                            z1++;
                        }
                        for (int x = sx; x <= x1; x++) for (int z = sz; z <= z1; z++) seen[level, x, z] = true;
                        var room = RoomAt(level, new Vector2Int(rect.xMin + sx, rect.yMin + sz));
                        rects.Add((level, rect.xMin + sx, rect.yMin + sz, rect.xMin + x1 + 1, rect.yMin + z1 + 1, room));
                    }
            // печная труба — над самой большой жилой частью дома
            int chim = -1, bestArea = 0;
            if (lot.kind == LotKind.Home || lot.kind == LotKind.Cafe)
                for (int i = 0; i < rects.Count; i++)
                {
                    var q = rects[i];
                    if (q.room == null || !RoomHasWalls(q.room)) continue;
                    int area = (q.x1 - q.x0) * (q.z1 - q.z0);
                    if (area > bestArea) { bestArea = area; chim = i; }
                }
            for (int i = 0; i < rects.Count; i++)
            {
                var q = rects[i];
                var go = RoofMesh(q.x0, q.z0, q.x1, q.z1, (q.room != null ? q.room.elev : 0f) + LevelY(q.level + 1), q.room, q.room == null || RoomHasWalls(q.room), lot, i == chim);
                if (go != null) roofs.Add((go, q.level, q.room, lot.id));
            }
        }

        /// <summary>Комната, над которой эта крыша (для выбора типа крыши кликом).</summary>
        public Room RoofRoomOf(GameObject go)
        {
            foreach (var r in roofs) if (r.go == go) return r.room;
            return null;
        }

        public string RoofInfo()
        {
            var l = new List<string>();
            foreach (var r in roofs) l.Add(r.go == null ? "null" : $"эт{r.level + 1} {(r.room != null ? r.room.name : "-")} {RoofTypeNames[r.room != null ? r.room.roofType : 0]} active={r.go.activeSelf}");
            return string.Join("; ", l);
        }

        public enum RoofType { Gable = 0, Hip = 1, Shed = 2, Flat = 3 }
        public static readonly string[] RoofTypeNames = { "Двускатная", "Четырёхскатная", "Односкатная", "Плоская" };

        GameObject RoofMesh(float rx0, float rz0, float rx1, float rz1, float baseY, Room room, bool gables, Lot lot, bool chimney = false)
        {
            const float vox = 0.125f, over = 0.375f, thick = 0.25f;
            var type = room != null ? (RoofType)room.roofType : RoofType.Gable;
            int dir = room != null ? room.roofDir : 0;
            float slope = room != null ? room.roofPitch : 0.62f;
            float cx = (rx0 + rx1) / 2f, cz = (rz0 + rz1) / 2f;
            float hx = (rx1 - rx0) / 2f + over, hz = (rz1 - rz0) / 2f + over;
            bool ridgeX = dir == 1 || (dir == 0 && rx1 - rx0 >= rz1 - rz0);

            float Top(float x, float z, out bool ridge)
            {
                float dx = hx - Mathf.Abs(x - cx), dz = hz - Mathf.Abs(z - cz);
                ridge = false;
                switch (type)
                {
                    case RoofType.Hip:
                    {
                        float d = Mathf.Min(dx, dz);
                        ridge = Mathf.Abs(dx - dz) < 0.12f || d > Mathf.Min(hx, hz) - 0.12f;
                        return baseY + d * slope;
                    }
                    case RoofType.Shed:
                    {
                        float t = dir == 0 ? z - (rz0 - over) : dir == 2 ? (rz1 + over) - z : dir == 1 ? x - (rx0 - over) : (rx1 + over) - x;
                        float full = dir % 2 == 0 ? hz * 2 : hx * 2;
                        ridge = t > full - 0.15f;
                        return baseY + 0.25f + t * slope * 0.5f;
                    }
                    case RoofType.Flat:
                        ridge = dx < 0.2f || dz < 0.2f;
                        return baseY + (ridge ? 0.5f : 0.25f);
                    default:
                    {
                        float d = ridgeX ? dz : dx;
                        ridge = d > (ridgeX ? hz : hx) - 0.15f;
                        return baseY + d * slope;
                    }
                }
            }

            float peak = 0.6f + Mathf.Max(hx, hz) * 2f * Mathf.Max(slope, 0.3f);
            var o = new Vector3(rx0 - over, baseY, rz0 - over);
            int nx = Mathf.CeilToInt((rx1 - rx0 + over * 2) / vox), nz = Mathf.CeilToInt((rz1 - rz0 + over * 2) / vox), ny = Mathf.CeilToInt(peak / vox) + (chimney ? 12 : 2);
            // печная труба у конька
            float chx, chz;
            if (type == RoofType.Gable && ridgeX) { chx = cx + (rx1 - rx0) * 0.25f; chz = cz + 0.45f; }
            else if (type == RoofType.Gable) { chx = cx + 0.45f; chz = cz + (rz1 - rz0) * 0.25f; }
            else { chx = cx + Mathf.Min(1f, (rx1 - rx0) * 0.2f); chz = cz + 0.4f; }
            float chTop = Top(chx, chz, out _) + 0.85f;
            var cells = new byte[nx * ny * nz];
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    float px = o.x + (x + 0.5f) * vox, pz = o.z + (z + 0.5f) * vox;
                    float top = Top(px, pz, out bool ridge);
                    bool inside = px > rx0 && px < rx1 && pz > rz0 && pz < rz1;
                    bool edge = inside && (Mathf.Abs(px - rx0) < vox * 0.6f || Mathf.Abs(px - rx1) < vox * 0.6f || Mathf.Abs(pz - rz0) < vox * 0.6f || Mathf.Abs(pz - rz1) < vox * 0.6f);
                    bool chim = chimney && Mathf.Abs(px - chx) < 0.25f && Mathf.Abs(pz - chz) < 0.25f;
                    bool chimCap = chimney && Mathf.Abs(px - chx) < 0.32f && Mathf.Abs(pz - chz) < 0.32f;
                    for (int y = 0; y < ny; y++)
                    {
                        float py = baseY + (y + 0.5f) * vox;
                        byte val = 0;
                        if (chim && py < chTop && py > top - thick) val = 4;
                        else if (chimCap && py < chTop && py > chTop - 0.13f) val = 4;
                        else if (py <= top && py > top - thick) val = ridge ? (byte)2 : (byte)1;
                        else if (gables && edge && py < top - thick && py > baseY) val = 3;
                        cells[(y * nz + z) * nx + x] = val;
                    }
                }
            var mesh = Voxelizer.BuildMesh(cells, nx, ny, nz, vox, o, 4);
            if (mesh == null) return null;
            var go = new GameObject("roof");
            go.transform.SetParent(roofsRoot, false);
            Vector3? go_smoke = null;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { lot.roof.parts[0].mat, lot.roof.parts[1].mat, lot.facade.parts[0].mat, lot.roof.parts[2].mat };
            lot.roof.parts[2].mat.SetFloat("_Voxel", vox); lot.roof.parts[2].mat.SetFloat("_UseAO", 1f);
            if (chimney) go_smoke = new Vector3(chx, chTop, chz);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            foreach (var m in new[] { lot.roof.parts[0].mat, lot.roof.parts[1].mat }) { m.SetFloat("_Voxel", vox); m.SetFloat("_UseAO", 1f); }
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            var sr = go.AddComponent<StyleRef>();
            sr.owner = lot.roof;
            sr.part = 0;
            if (go_smoke != null)
            {
                var sm = new GameObject("smoke");
                sm.transform.SetParent(go.transform, false);
                sm.transform.position = go_smoke.Value;
                sm.AddComponent<ChimneySmoke>();
            }
            return go;
        }

        // ================= сетки, вход, видимость =================

        public void UpdateGrid() => UpdateGrid(TownRect);

        public void UpdateGrid(RectInt rect)
        {
            foreach (var g in grids) g.ClearWalls(rect);
            foreach (var kv in edges)
            {
                bool v = kv.Key.z % 2 == 1;
                int level = kv.Key.z / 2;
                if (kv.Key.x < rect.xMin || kv.Key.x > rect.xMax || kv.Key.y < rect.yMin || kv.Key.y > rect.yMax) continue;
                var k = kv.Value.kind;
                bool blocks = k == EdgeKind.Wall || k == EdgeKind.Window || k == EdgeKind.Fence || k == EdgeKind.Railing;
                if (v) grids[level].SetWallX(kv.Key.x, kv.Key.y, blocks, true);
                else grids[level].SetWallZ(kv.Key.x, kv.Key.y, blocks, true);
            }
            for (int level = 0; level < Levels; level++)
            {
                var g = grids[level];
                var holes = stairHoles(level);
                for (int x = rect.xMin; x < rect.xMax; x++)
                    for (int z = rect.yMin; z < rect.yMax; z++)
                    {
                        var c = new Vector2Int(x, z);
                        if (!InTown(c)) continue;
                        bool floor = level == 0 ? Walkable0(c) : RoomIndexAt(level, c) >= 0 && !holes.Contains(c);
                        g.SetNoFloor(c, !floor);
                        g.SetHeight(c, FloorY(level, c));
                    }
            }
        }

        void FindEntry(Lot lot)
        {
            float best = float.MaxValue;
            var entry = new Vector2Int(int.MinValue, 0);
            var front = new Vector2Int((int)lot.rect.center.x, lot.frontSouth ? lot.rect.yMin : lot.rect.yMax - 1);
            foreach (var kv in edges)
            {
                if (kv.Value.kind != EdgeKind.Door || kv.Key.z / 2 != 0 || kv.Value.lot != lot.id) continue;
                Sides(kv.Key.x, kv.Key.y, kv.Key.z % 2 == 1, out var a, out var b);
                int ra = RoomIndexAt(a), rb = RoomIndexAt(b);
                Vector2Int inside;
                if (ra >= 0 && rb < 0) inside = a; else if (rb >= 0 && ra < 0) inside = b; else continue;
                float d = Vector2Int.Distance(inside, front);
                if (d < best) { best = d; entry = inside; }
            }
            lot.entry = entry.x != int.MinValue ? entry : front;
        }

        /// <summary>Скрыт ли этот этаж в клетке (только на «открытом» участке выше выбранного этажа).</summary>
        public bool HiddenAt(int level, Vector2Int c)
        {
            if (level <= viewLevel) return false;
            var l = LotAt(c);
            return l != null && l.id == focusLot;
        }

        /// <summary>Украшения, которые ведут себя как крыша (купол обсерватории): прячутся вместе с крышей.</summary>
        readonly List<(GameObject go, int lot)> roofProps = new List<(GameObject, int)>();
        public void RoofProp(int lot, GameObject go) => roofProps.Add((go, lot));

        public void ApplyVisibility()
        {
            // спрятанные этажи и крыши не видно, но тень они отбрасывают как обычно
            foreach (var p in roofProps) if (p.go != null) Shown(p.go, p.lot != focusLot || showRoof);
            foreach (var r in rooms) if (r.st != null) Shown(r.st.gameObject, r.lot != focusLot || r.level <= viewLevel);
            foreach (var r in roofs)
                if (r.go != null) Shown(r.go, r.lot != focusLot || (showRoof && r.level <= viewLevel));
            foreach (var e in edges.Values) e.state = -1;
            if (Game.I != null) Game.I.ApplyFurnitureVisibility();
        }

        static void Shown(GameObject go, bool shown)
        {
            if (!go.activeSelf) go.SetActive(true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = shown ? UnityEngine.Rendering.ShadowCastingMode.TwoSided : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = shown;
        }

        public void SetViewLevel(int level)
        {
            viewLevel = Mathf.Clamp(level, 0, Levels - 1);
            ApplyVisibility();
        }

        // ================= двор и деревья =================

        void BuildYard()
        {
            for (int i = 0; i < 6; i++)
            {
                var f = NewStyleable("Цветы", SurfaceKind.Outdoor, PlayerLot.root);
                f.AddPart("Цветы", StyleData.Make("Гладкий", i % 2 == 0 ? "#E46A8C" : "#F2C14E"));
                f.AddPart("Листья", StyleData.Make("Гладкий", "#4F8A3C"));
                float fx = i < 3 ? 1.2f + i * 0.9f : 6.0f + (i - 3) * 0.9f;
                var pos = new Vector3(fx, 0, -0.7f);
                Shapes.Ball(f, 1, f.transform, pos + new Vector3(0, 0.12f, 0), new Vector3(0.45f, 0.25f, 0.45f));
                Shapes.Ball(f, 0, f.transform, pos + new Vector3(0, 0.26f, 0), new Vector3(0.28f, 0.14f, 0.28f));
                Voxelizer.Bake(f.transform, Voxelizer.FurnitureVoxel);
            }
        }

        public class TreeRec { public Vector3 p; public float s; public bool pine; public Styleable st; }
        public readonly List<TreeRec> trees = new List<TreeRec>();

        public TreeRec PlantTree(Vector3 p, float s, bool pine)
        {
            var rec = new TreeRec { p = new Vector3(p.x, 0, p.z), s = s, pine = pine };
            rec.st = Tree(new Vector3(p.x, 0, p.z), s, pine);
            rec.st.transform.position = new Vector3(0, terrain.GroundAt(p.x, p.z), 0);
            trees.Add(rec);
            return rec;
        }

        public int RemoveTrees(Vector3 p, float r)
        {
            int n = 0;
            for (int i = trees.Count - 1; i >= 0; i--)
            {
                if (new Vector2(trees[i].p.x - p.x, trees[i].p.z - p.z).magnitude > r) continue;
                if (trees[i].st != null) Destroy(trees[i].st.gameObject);
                trees.RemoveAt(i);
                n++;
            }
            return n;
        }

        public void ClearTrees()
        {
            foreach (var t in trees) if (t.st != null) Destroy(t.st.gameObject);
            trees.Clear();
        }

        public void SettleTrees(Vector3 p, float r)
        {
            foreach (var t in trees)
                if (t.st != null && new Vector2(t.p.x - p.x, t.p.z - p.z).magnitude < r + 1f)
                    t.st.transform.position = new Vector3(0, terrain.GroundAt(t.p.x, t.p.z), 0);
        }

        Styleable Tree(Vector3 p, float s, bool pine)
        {
            var tr = NewStyleable(pine ? "Ёлка" : "Дерево", SurfaceKind.Outdoor, treesRoot);
            tr.AddPart("Ствол", StyleData.Make("Дерево", "#7A5534"));
            tr.AddPart("Крона", StyleData.Make("Ворс", pine ? "#3E6B45" : "#5E9A48"));
            Shapes.Cyl(tr, 0, tr.transform, p + Vector3.up * 0.6f * s, 0.25f * s, 1.2f * s);
            if (pine)
            {
                for (int i = 0; i < 3; i++)
                    Shapes.MeshPart(Shapes.Cone(), tr, 1, tr.transform, p + Vector3.up * (1.3f + i * 0.6f) * s,
                        new Vector3(1.8f - i * 0.45f, 1.1f, 1.8f - i * 0.45f) * s, default, true);
            }
            else
            {
                Shapes.Ball(tr, 1, tr.transform, p + Vector3.up * 1.8f * s, new Vector3(1.7f, 1.5f, 1.7f) * s);
                Shapes.Ball(tr, 1, tr.transform, p + new Vector3(0.45f, 1.5f, 0.2f) * s, new Vector3(1.1f, 1f, 1.1f) * s);
            }
            Voxelizer.Bake(tr.transform, Voxelizer.OutdoorVoxel);
            return tr;
        }

        // ================= опускание стен =================

        public void CycleWallMode() => wallMode = (WallMode)(((int)wallMode + 1) % 3);

        public string WallModeName => wallMode == WallMode.Auto ? "авто" : wallMode == WallMode.Up ? "подняты" : "опущены";

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // «открытый» участок — тот, на который смотрит камера
            if (Game.I != null && Game.I.cam != null)
            {
                var l = LotAt(GridMap.CellOf(Game.I.cam.target));
                int f = l != null && l.kind != LotKind.Park && l.kind != LotKind.Square && l.kind != LotKind.Market && l.kind != LotKind.Camp ? l.id : -1;
                // в чужой дом заглянуть можно, только если у кого-то из семьи есть от него ключ
                if (f >= 0 && Game.I.mode == Mode.Live && !Game.I.townBuilder && !Game.I.HouseholdCanEnter(f)) f = -1;
                if (f != focusLot) { focusLot = f; ApplyVisibility(); }
            }
            Vector3 toCam = -cam.transform.forward; toCam.y = 0; toCam.Normalize();
            foreach (var kv in edges)
            {
                var e = kv.Value;
                if (e.root == null) continue;
                int level = kv.Key.z / 2;
                int state;
                if (e.lot != focusLot) state = 0; // чужие дома видны целиком
                else if (level > viewLevel) state = 2;
                else if (IsFenceLike(e.kind) || IsRailing(e.kind) || level < viewLevel) state = 0;
                else if (wallMode == WallMode.Up || showRoof) state = 0;
                else if (wallMode == WallMode.Down) state = 1;
                else
                {
                    bool v = kv.Key.z % 2 == 1;
                    var n = v ? Vector3.right : Vector3.forward;
                    float facing = Vector3.Dot(n, toCam);
                    if (Mathf.Abs(facing) < 0.3f) state = 0;
                    else
                    {
                        Sides(kv.Key.x, kv.Key.y, v, out var a, out var b);
                        var near = facing > 0 ? b : a;
                        var far = facing > 0 ? a : b;
                        bool nearIn = RoomIndexAt(level, near) >= 0, farIn = RoomIndexAt(level, far) >= 0;
                        state = !nearIn || (nearIn && farIn) ? 1 : 0;
                    }
                }
                if (state == e.state) continue;
                e.state = state;
                // спрятанные и опущенные стены остаются «тенью»: свет в окна падает как надо
                bool hidden = state == 2, down = state == 1;
                e.root.SetActive(true);
                foreach (var col in e.root.GetComponentsInChildren<Collider>(true)) col.enabled = !hidden;
                if (down) EnsureCut(e, level);
                if (e.full != null) { e.full.SetActive(true); ShadowOnly(e.full, hidden || down); }
                if (e.cut != null) e.cut.SetActive(down);
                if (e.opening != null) e.opening.SetActive(state == 0);
                foreach (var s in e.highSlabs) s.SetActive(state == 0);
                    float by = e.baseY;
                foreach (var s in e.lowSlabs) SetHeight(s, by, down ? CutH : WallH);
            }
        }
    }
}

namespace FoxLife
{
    /// <summary>Дымок из печной трубы: несколько полупрозрачных клубков поднимаются и тают.</summary>
    public class ChimneySmoke : MonoBehaviour
    {
        readonly List<(Transform t, Material m, float phase)> puffs = new List<(Transform, Material, float)>();

        void Start()
        {
            for (int i = 0; i < 4; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                var m = MaterialFactory.CreateGlass();
                MaterialFactory.Apply(m, StyleData.Make("Гладкий", "#F4F1EC"));
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                puffs.Add((go.transform, m, i / 4f));
            }
        }

        void Update()
        {
            float t = Time.time * 0.12f;
            foreach (var p in puffs)
            {
                float k = Mathf.Repeat(t + p.phase, 1f);
                p.t.localPosition = new Vector3(Mathf.Sin((k + p.phase) * 6f) * 0.25f * k, k * 2.4f, k * 0.6f);
                p.t.localScale = Vector3.one * (0.14f + k * 0.32f);
                p.t.localRotation = Quaternion.Euler(k * 40f, k * 70f + p.phase * 90f, 0);
                p.m.SetFloat("_Alpha", Mathf.Sin(k * Mathf.PI) * 0.75f);
            }
        }
    }
}
