using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Три режима: строительство города (всё можно, лисички не участвуют), редактирование семей и игра за семью.
    /// Здесь — строительство города: участки (новые, снос, имя, назначение), удаляемые декорации,
    /// сохранение города в файл, которым можно поделиться.
    /// </summary>
    public partial class Game
    {
        public string townName = "Лисья Долина";
        public bool townBuilder;
        public bool lotsDirty;
        public readonly List<string> removedProps = new List<string>();
        bool prevFree, prevMayor;

        // ---------- вход и выход ----------

        public void EnterTownBuilder(bool again = false)
        {
            if (!again) { prevFree = freeBuild; prevMayor = mayorMode; }
            townBuilder = true;
            freeBuild = true;
            mayorMode = true;
            Select(null);
            SetSpeed(0);
            ui.screen = GameUI.MenuScreen.None;
            ui.townEditing = false;
            mode = Mode.Build;
            house.focusLot = -2;
            cam.Focus(new Vector3(20f, 0, -5f), 999f);
            cam.dist = 80f; cam.pitch = 55f;
            ShowLotBorders(true);
            if (!again) Log("В городе можно строить и перестраивать бесплатно. Во вкладке «Участки» можно создавать, переименовывать и сносить участки.");
        }

        public void ExitTownBuilder()
        {
            CancelPlacing();
            SetWallTool(WallTool.None);
            SetLandTool(LandTool.None);
            lotTool = false;
            CancelResize();
            townBuilder = false;
            ShowLotBorders(false);
            freeBuild = prevFree;
            mayorMode = prevMayor;
            if (lotsDirty) { lotsDirty = false; StartCoroutine(RebuildTownRoutine()); }
            else EnterTown();
        }

        /// <summary>Участки поменялись — пересобираем мир (рельеф, дорожки, дома) из сохранения.</summary>
        IEnumerator RebuildTownRoutine(bool stay = false)
        {
            loading = true;
            loadingText = "Перестраиваем городок…";
            loadingProgress = 0.02f;
            yield return null; yield return null;
            string tmp = System.IO.Path.Combine(Application.persistentDataPath, "town_rebuild.tmp");
            bool ok = SaveSystem.Save(this, tmp);
            if (ok) yield return SaveSystem.LoadAsync(this, r => ok = r, tmp);
            loading = false;
            if (stay) { townBuilder = false; EnterTownBuilder(true); ui.buildTab = 3; }
            else EnterTown();
            if (!ok) ui.Note("Не получилось перестроить город.");
        }

        // ---------- участки ----------

        public bool CanEditLotsNow => townBuilder;

        public void RenameLot(int id, string name)
        {
            if (id < 0 || id >= Town.Lots.Length || string.IsNullOrWhiteSpace(name)) return;
            if (Town.Lots[id].name != name) Town.Lots[id].nameOverrides = null;
            Town.Lots[id].name = name;
            var l = house.LotById(id);
            if (l != null) { l.name = name; l.root.name = "Участок: " + name; }
        }

        public void SetLotKind(int id, LotKind k)
        {
            if (id < 0 || id >= Town.Lots.Length) return;
            if (Town.Lots[id].kind == k) return;
            if (k != LotKind.Home && FamilyOf(id).Count > 0) EvictFamily(id);
            Town.Lots[id].kind = k;
            var l = house.LotById(id);
            if (l != null) l.kind = k;
            lotsDirty = true;
        }

        public string CanRemoveLot(int id)
        {
            if (id == House.PlayerLotId) return "Здесь живёт семья, за которую вы играете";
            if (id < 0 || id >= Town.Lots.Length || Town.Lots[id].removed) return "Участка нет";
            return null;
        }

        /// <summary>Снести участок: всё на нём убрать, жильцов — в архив, землю — вернуть природе.</summary>
        public void RemoveLot(int id)
        {
            if (CanRemoveLot(id) != null) return;
            if (FamilyOf(id).Count > 0) EvictFamily(id);
            foreach (var f in new List<Furniture>(furniture)) if (f.lot == id) RemoveFurniture(f);
            foreach (var e in house.Layout())
                if (house.EdgeLot(e.x, e.z, e.v) == id) house.SetEdge(e.level, e.x, e.z, e.v, EdgeKind.None);
            var l = house.LotById(id);
            if (l != null) { house.Rebuild(l); l.removed = true; }
            Town.Lots[id].removed = true;
            Town.Lots[id].rect = Town.Nowhere;
            StartCoroutine(RebuildTownRoutine(true));
            Log($"Участок «{Town.Lots[id].name}» снесён.");
        }

        /// <summary>Проверка места под новый участок.</summary>
        public string CheckNewLot(RectInt r)
        {
            if (r.width < 8 || r.height < 8) return "Участок должен быть хотя бы 8×8";
            if (r.width > 40 || r.height > 40) return "Слишком большой участок (до 40×40)";
            if (r.xMin < Town.MinX + 2 || r.yMin < Town.MinZ + 2 || r.xMax > Town.MinX + Town.W - 2 || r.yMax > Town.MinZ + Town.H - 2) return "Слишком близко к краю долины";
            foreach (var l in Town.Lots)
            {
                if (l.removed) continue;
                var g = new RectInt(l.rect.x - 2, l.rect.y - 2, l.rect.width + 4, l.rect.height + 4);
                if (g.Overlaps(r)) return "Задевает участок «" + l.name + "»";
            }
            for (int x = r.xMin; x < r.xMax; x += 2)
                for (int z = r.yMin; z < r.yMax; z += 2)
                {
                    if (Town.IsCreek(x, z)) return "Здесь вода";
                    if (house.terrain.IsRoad(x + 0.5f, z + 0.5f)) return "Здесь дорога";
                }
            return null;
        }

        public void AddLot(RectInt r, LotKind kind)
        {
            if (CheckNewLot(r) != null) return;
            // вход — со стороны ближайшей дороги
            Town.RoadHeight(r.center.x, r.yMin - 3, out float dS);
            Town.RoadHeight(r.center.x, r.yMax + 3, out float dN);
            int n = 1;
            foreach (var l in Town.Lots) if (!l.removed) n++;
            var def = new Town.LotDef
            {
                name = "Новый участок " + n, rect = r, kind = kind, frontSouth = dS <= dN,
                pathX = Mathf.Floor(r.center.x) + 0.5f, template = -1,
            };
            var list = new List<Town.LotDef>(Town.Lots) { def };
            Town.SetLots(list.ToArray());
            StartCoroutine(RebuildTownRoutine(true));
            Log($"Добавлен участок «{def.name}». Его можно переименовать во вкладке «Участки».");
        }

        // ---------- размер участка: по клеточке с каждой стороны, с превью; город пересобирается один раз ----------

        public int resizeLot = -1;
        public RectInt resizeRect;
        GameObject resizeLine;

        public void BeginResize(int id)
        {
            if (id < 0 || id >= Town.Lots.Length || Town.Lots[id].removed) return;
            resizeLot = id;
            resizeRect = Town.Lots[id].rect;
            DrawResizePreview();
        }

        public void CancelResize()
        {
            resizeLot = -1;
            if (resizeLine != null) Destroy(resizeLine);
        }

        /// <summary>side: 0 — север (+Z), 1 — юг, 2 — запад, 3 — восток; d: +1 расширить, −1 сузить.</summary>
        public void NudgeLot(int side, int d)
        {
            if (resizeLot < 0) return;
            var r = resizeRect;
            switch (side)
            {
                case 0: r.height += d; break;
                case 1: r.y -= d; r.height += d; break;
                case 2: r.x -= d; r.width += d; break;
                case 3: r.width += d; break;
            }
            if (r.width < 1 || r.height < 1) return;
            resizeRect = r;
            DrawResizePreview();
        }

        public string CheckResize()
        {
            if (resizeLot < 0) return "Участок не выбран";
            var r = resizeRect;
            if (r.width < 8 || r.height < 8) return "Участок должен быть хотя бы 8×8";
            if (r.width > 40 || r.height > 40) return "Слишком большой участок (до 40×40)";
            if (r.xMin < Town.MinX + 2 || r.yMin < Town.MinZ + 2 || r.xMax > Town.MinX + Town.W - 2 || r.yMax > Town.MinZ + Town.H - 2) return "Слишком близко к краю долины";
            for (int i = 0; i < Town.Lots.Length; i++)
            {
                var l = Town.Lots[i];
                if (i == resizeLot || l.removed) continue;
                var g = new RectInt(l.rect.x - 2, l.rect.y - 2, l.rect.width + 4, l.rect.height + 4);
                if (g.Overlaps(r)) return "Задевает участок «" + l.name + "»";
            }
            foreach (var room in house.rooms)
                if (room.lot == resizeLot)
                    foreach (var c in room.cells)
                        if (!r.Contains(c)) return "Дом не помещается — сначала разбери его край";
            return null;
        }

        public void ApplyResize()
        {
            if (CheckResize() != null) return;
            int id = resizeLot;
            var r = resizeRect;
            // вещи и заборы, оставшиеся за новой границей, убираем
            foreach (var f in new List<Furniture>(furniture))
                if (f.lot == id && !r.Contains(f.anchor)) RemoveFurniture(f);
            foreach (var e in house.Layout())
                if (house.EdgeLot(e.x, e.z, e.v) == id && !r.Contains(new Vector2Int(e.x, e.z))) house.SetEdge(e.level, e.x, e.z, e.v, EdgeKind.None);
            Town.Lots[id].rect = r;
            if (Town.Lots[id].pathX < r.xMin + 1 || Town.Lots[id].pathX > r.xMax - 1) Town.Lots[id].pathX = Mathf.Floor(r.center.x) + 0.5f;
            Town.SetLots(Town.Lots);
            CancelResize();
            Log($"Участок «{Town.Lots[id].name}» теперь {r.width}×{r.height}.");
            StartCoroutine(RebuildTownRoutine(true));
        }

        void DrawResizePreview()
        {
            if (resizeLine == null)
            {
                resizeLine = new GameObject("новая граница");
                var lr0 = resizeLine.AddComponent<LineRenderer>();
                var mat = new Material(Shader.Find("FoxLife/Particle")) { color = Color.white };
                mat.SetFloat("_Unlit", 1f);
                lr0.sharedMaterial = mat;
                lr0.widthMultiplier = 0.22f;
                lr0.loop = true;
                lr0.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var lr = resizeLine.GetComponent<LineRenderer>();
            lr.startColor = lr.endColor = CheckResize() == null ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.45f, 0.4f);
            var r = resizeRect;
            var pts = new List<Vector3>();
            void Edge(Vector2 a, Vector2 b)
            {
                int n = Mathf.CeilToInt((b - a).magnitude);
                for (int i = 0; i < n; i++)
                {
                    var q = Vector2.Lerp(a, b, i / (float)n);
                    pts.Add(new Vector3(q.x, house.terrain.GroundAt(q.x, q.y) + 0.2f, q.y));
                }
            }
            Edge(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin));
            Edge(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax));
            Edge(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
            Edge(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin));
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
        }

        // ---------- границы участков ----------

        readonly List<GameObject> borders = new List<GameObject>();

        /// <summary>В строительстве города видны границы всех участков (пунктиром по земле).</summary>
        public void ShowLotBorders(bool on)
        {
            foreach (var b in borders) if (b != null) Destroy(b);
            borders.Clear();
            if (!on || house == null) return;
            var mat = new Material(Shader.Find("FoxLife/Particle")) { color = new Color(1f, 1f, 1f, 0.85f) };
            mat.SetFloat("_Unlit", 1f);
            foreach (var l in house.lots)
            {
                if (l.removed || l.rect.width <= 0) continue;
                var go = new GameObject("граница: " + l.name);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat;
                lr.widthMultiplier = 0.14f;
                lr.loop = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var r = l.rect;
                var pts = new List<Vector3>();
                void Edge(Vector2 a, Vector2 b)
                {
                    int n = Mathf.CeilToInt((b - a).magnitude);
                    for (int i = 0; i < n; i++)
                    {
                        var q = Vector2.Lerp(a, b, i / (float)n);
                        pts.Add(new Vector3(q.x, house.terrain.GroundAt(q.x, q.y) + 0.12f, q.y));
                    }
                }
                Edge(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin));
                Edge(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax));
                Edge(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
                Edge(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin));
                lr.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
                lr.startColor = lr.endColor = l.kind == LotKind.Home ? new Color(1f, 0.95f, 0.6f) : new Color(0.6f, 0.85f, 1f);
                borders.Add(go);
            }
        }

        // ---------- декорации ----------

        static string PropKey(Transform t)
        {
            var p = t.position;
            return t.name + "@" + p.x.ToString("0.0", CultureInfo.InvariantCulture) + "," + p.z.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>Декорация (изгородь, арка, ящик, куст, камень, фонарь…) или дерево — можно убрать.</summary>
        public bool IsRemovableProp(Styleable st, out Transform root)
        {
            root = null;
            if (st == null || house == null) return false;
            var t = st.transform;
            while (t != null && t.parent != house.propsRoot && t.parent != null && t.parent != house.transform) t = t.parent;
            if (t != null && t.parent == house.propsRoot) { root = t; return true; }
            foreach (var tr in house.trees) if (tr.st == st) { root = st.transform; return true; }
            return false;
        }

        public void RemoveProp(Transform root)
        {
            if (root == null) return;
            var tree = house.trees.Find(tr => tr.st != null && tr.st.transform == root);
            Net.PropRemoved(tree != null, tree == null ? PropKey(root) : null, tree != null ? tree.p : root.position);
            if (tree != null) house.trees.Remove(tree);
            else removedProps.Add(PropKey(root));
            Unblock(root);
            bool bridge = root.GetComponentInChildren<BridgeMark>(true) != null;
            root.gameObject.SetActive(false);
            if (bridge) house.UpdateGrid();
            Destroy(root.gameObject);
        }

        public void RemovePropByKey(string key)
        {
            if (house == null || house.propsRoot == null) return;
            foreach (Transform t in house.propsRoot)
                if (PropKey(t) == key)
                {
                    removedProps.Add(key); Unblock(t);
                    bool bridge = t.GetComponentInChildren<BridgeMark>(true) != null;
                    t.gameObject.SetActive(false);
                    if (bridge) house.UpdateGrid();
                    Destroy(t.gameObject); return;
                }
        }

        void Unblock(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            for (int x = Mathf.FloorToInt(b.min.x); x <= Mathf.FloorToInt(b.max.x); x++)
                for (int z = Mathf.FloorToInt(b.min.z); z <= Mathf.FloorToInt(b.max.z); z++)
                    house.blocked.Remove(new Vector2Int(x, z));
            house.UpdateGrid(new RectInt(Mathf.FloorToInt(b.min.x) - 1, Mathf.FloorToInt(b.min.z) - 1, Mathf.CeilToInt(b.size.x) + 3, Mathf.CeilToInt(b.size.z) + 3));
        }

        // ---------- новый участок: протянуть прямоугольник по земле ----------

        public bool lotTool;
        Vector2Int lotStart;
        bool lotDragging;
        RectInt lotRect;
        string lotWhy;
        GameObject lotPreview;
        public string lotToolHint;

        void UpdateLotTool()
        {
            if (!lotTool || !townBuilder) { if (lotPreview != null) lotPreview.SetActive(false); return; }
            if (lotPreview == null)
            {
                lotPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(lotPreview.GetComponent<Collider>());
                var mat = new Material(Shader.Find("FoxLife/Particle")) { color = new Color(0.4f, 0.75f, 1f, 0.35f) };
                mat.SetFloat("_Unlit", 1f);
                lotPreview.GetComponent<Renderer>().sharedMaterial = mat;
            }
            if (lotDragging && !Input.GetMouseButton(0))
            {
                lotDragging = false;
                if (lotWhy == null) { AddLot(lotRect, LotKind.Home); lotTool = false; }
                else Log(lotWhy + ".");
                lotPreview.SetActive(false);
                return;
            }
            var ray = camera3d.ScreenPointToRay(Input.mousePosition);
            if (ui.MouseOverUI || !Physics.Raycast(ray, out var hit, 600f)) { if (!lotDragging) lotPreview.SetActive(false); return; }
            var c = GridMap.CellOf(hit.point);
            if (Input.GetMouseButtonDown(0)) { lotStart = c; lotDragging = true; }
            var a = lotDragging ? lotStart : c;
            var r = new RectInt(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Abs(a.x - c.x) + 1, Mathf.Abs(a.y - c.y) + 1);
            string why = lotDragging ? CheckNewLot(r) : null;
            lotRect = r; lotWhy = why;
            lotToolHint = lotDragging ? $"{r.width}×{r.height}" + (why != null ? " — " + why : " — отпусти, чтобы создать") : "Нажми и протяни по земле, чтобы отметить новый участок";
            lotPreview.SetActive(true);
            lotPreview.transform.position = new Vector3(r.center.x, hit.point.y + 0.3f, r.center.y);
            lotPreview.transform.localScale = new Vector3(r.width, 0.6f, r.height);
            lotPreview.GetComponent<Renderer>().sharedMaterial.color = why == null ? new Color(0.4f, 0.75f, 1f, 0.35f) : new Color(1f, 0.45f, 0.4f, 0.35f);
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { lotDragging = false; lotTool = false; }
        }

        // ---------- города-файлы ----------

        public bool SaveTown(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            townName = name.Trim();
            return SaveSystem.Save(this, SaveSystem.TownPath(townName));
        }

        /// <summary>Новая игра в выбранном городе (из файла): потом — выбор семьи.</summary>
        public void NewGameFromTown(string path)
        {
            ui.screen = GameUI.MenuScreen.None;
            StartCoroutine(TownFileRoutine(path));
        }

        IEnumerator TownFileRoutine(string path)
        {
            loading = true;
            loadingText = "Открываем город…";
            loadingProgress = 0.02f;
            yield return null; yield return null;
            bool ok = false;
            yield return SaveSystem.LoadAsync(this, r => ok = r, path);
            loading = false;
            started = false;
            if (ok) { Log($"Добро пожаловать в город «{townName}»!"); EnterTown(); }
            else { ui.screen = GameUI.MenuScreen.Title; ui.Note("Не получилось открыть город."); }
        }
    }
}
