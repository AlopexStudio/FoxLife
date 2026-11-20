using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Отмена и повтор (Ctrl+Z, Ctrl+Shift+Z / Ctrl+Y) в строительстве и стилях.
    /// Стройка: до и после действия снимается «снимок» стен и мебели (с цветами) — отмена возвращает разницу
    /// (вместе с потраченными монетами). Земля: журнал изменённых столбиков за мазок кисти, плюс цветы и деревья.
    /// Стили: прежний и новый стиль детали; частые изменения одной детали (крутим цветовой круг) склеиваются в одно.
    /// </summary>
    public partial class Game
    {
        class UndoEntry { public string name; public Action undo, redo; public float time; public object key; }

        readonly List<UndoEntry> undoList = new List<UndoEntry>();
        readonly List<UndoEntry> redoList = new List<UndoEntry>();
        const int UndoMax = 60;

        public void ClearUndo() { undoList.Clear(); redoList.Clear(); }

        void PushUndo(string name, Action undo, Action redo, object key = null)
        {
            float now = Time.unscaledTime;
            if (key != null && undoList.Count > 0)
            {
                var last = undoList[undoList.Count - 1];
                if (Equals(last.key, key) && now - last.time < 1.5f) { last.redo = redo; last.time = now; redoList.Clear(); return; }
            }
            undoList.Add(new UndoEntry { name = name, undo = undo, redo = redo, time = now, key = key });
            if (undoList.Count > UndoMax) undoList.RemoveAt(0);
            redoList.Clear();
        }

        public void Undo()
        {
            if (undoList.Count == 0) { Log("Отменять нечего."); return; }
            CancelPlacing();
            var e = undoList[undoList.Count - 1];
            undoList.RemoveAt(undoList.Count - 1);
            e.undo();
            e.key = null;
            redoList.Add(e);
            Log("Отменено действие «" + e.name + "»");
        }

        public void Redo()
        {
            if (redoList.Count == 0) { Log("Повторять нечего."); return; }
            CancelPlacing();
            var e = redoList[redoList.Count - 1];
            redoList.RemoveAt(redoList.Count - 1);
            e.redo();
            undoList.Add(e);
            Log("Повторено действие «" + e.name + "»");
        }

        public bool CanUndo => undoList.Count > 0;
        public bool CanRedo => redoList.Count > 0;

        void UndoKeys()
        {
            if (mode != Mode.Build && mode != Mode.Style) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrl) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.Z)) { if (shift) Redo(); else Undo(); }
            else if (Input.GetKeyDown(KeyCode.Y)) Redo();
        }

        // ---------------- стили ----------------

        /// <summary>Изменить стиль детали с возможностью отмены.</summary>
        public void StyleEdit(Styleable st, int part, StyleData s)
        {
            if (st == null || part < 0 || part >= st.parts.Count) return;
            var before = st.Get(part).Clone();
            st.SetStyle(part, s);
            var after = s.Clone();
            PushUndo("оформление · " + (st.displayName ?? "деталь"),
                () => { if (st != null) st.SetStyle(part, before); },
                () => { if (st != null) st.SetStyle(part, after); },
                (st, part));
        }

        /// <summary>Сразу все детали предмета (кисть с Shift, «на весь предмет»).</summary>
        public void StyleEditAll(Styleable st, StyleData s)
        {
            if (st == null) return;
            var before = new List<StyleData>();
            foreach (var p in st.parts) before.Add(p.style.Clone());
            for (int i = 0; i < st.parts.Count; i++) st.SetStyle(i, s);
            var after = s.Clone();
            PushUndo("оформление · " + (st.displayName ?? "предмет"),
                () => { if (st != null) for (int i = 0; i < st.parts.Count && i < before.Count; i++) st.SetStyle(i, before[i]); },
                () => { if (st != null) for (int i = 0; i < st.parts.Count; i++) st.SetStyle(i, after); });
        }

        // ---------------- стройка: снимки стен и мебели ----------------

        class FurnRec
        {
            public string id; public Vector2Int a; public int rot, level; public float fine, light, bought; public Color lightColor;
            public List<StyleData> styles;
            public string Key => $"{id}@{a.x},{a.y},{level},{rot},{Mathf.RoundToInt(fine)}";
        }

        class BuildSnap
        {
            public Dictionary<string, (int level, int x, int z, bool v, EdgeKind k)> edges = new Dictionary<string, (int, int, int, bool, EdgeKind)>();
            public Dictionary<string, List<FurnRec>> furn = new Dictionary<string, List<FurnRec>>();
            public float money;
        }

        BuildSnap pendingBefore; // снимок до начала переноса предмета

        BuildSnap Snap()
        {
            var s = new BuildSnap { money = money };
            foreach (var e in house.Layout())
                if (e.k != EdgeKind.None) s.edges[$"{e.level},{e.x},{e.z},{e.v}"] = (e.level, e.x, e.z, e.v, e.k);
            foreach (var f in furniture)
            {
                if (f == null || f.def.id == "plate") continue;
                var r = new FurnRec { id = f.def.id, a = f.anchor, rot = f.rot, level = f.level, fine = f.fineYaw, light = f.lightLevel, lightColor = f.lightColor, bought = f.boughtAt, styles = new List<StyleData>() };
                foreach (var st in f.GetComponentsInChildren<Styleable>(true)) foreach (var p in st.parts) r.styles.Add(p.style.Clone());
                if (!s.furn.TryGetValue(r.Key, out var l)) s.furn[r.Key] = l = new List<FurnRec>();
                l.Add(r);
            }
            return s;
        }

        static bool SameSnap(BuildSnap a, BuildSnap b)
        {
            if (a.edges.Count != b.edges.Count || a.furn.Count != b.furn.Count) return false;
            foreach (var kv in a.edges) if (!b.edges.TryGetValue(kv.Key, out var o) || o.k != kv.Value.k) return false;
            foreach (var kv in a.furn) if (!b.furn.TryGetValue(kv.Key, out var o) || o.Count != kv.Value.Count) return false;
            return true;
        }

        /// <summary>Привести стены и мебель от состояния «from» к состоянию «to».</summary>
        void ApplySnap(BuildSnap from, BuildSnap to)
        {
            var lots = new HashSet<int>();
            foreach (var kv in to.edges)
                if (!from.edges.TryGetValue(kv.Key, out var o) || o.k != kv.Value.k)
                {
                    var e = kv.Value;
                    house.SetEdge(e.level, e.x, e.z, e.v, e.k);
                    lots.Add(house.EdgeLot(e.x, e.z, e.v));
                }
            foreach (var kv in from.edges)
                if (!to.edges.ContainsKey(kv.Key))
                {
                    var e = kv.Value;
                    house.SetEdge(e.level, e.x, e.z, e.v, EdgeKind.None);
                    lots.Add(house.EdgeLot(e.x, e.z, e.v));
                }
            // лишняя мебель — убрать
            foreach (var kv in from.furn)
            {
                to.furn.TryGetValue(kv.Key, out var want);
                int extra = kv.Value.Count - (want != null ? want.Count : 0);
                for (int i = 0; i < extra; i++)
                {
                    var r = kv.Value[0];
                    var f = furniture.Find(x => x != null && x.def.id == r.id && x.anchor == r.a && x.level == r.level && x.rot == r.rot && Mathf.RoundToInt(x.fineYaw) == Mathf.RoundToInt(r.fine));
                    if (f != null) RemoveFurniture(f);
                }
            }
            // недостающая — вернуть (с цветами и светом)
            foreach (var kv in to.furn)
            {
                from.furn.TryGetValue(kv.Key, out var have);
                int need = kv.Value.Count - (have != null ? have.Count : 0);
                for (int i = 0; i < need; i++)
                {
                    var r = kv.Value[Mathf.Min(i, kv.Value.Count - 1)];
                    var def = Catalog.Get(r.id);
                    if (def == null) continue;
                    var f = SpawnFurniture(def, r.a, r.rot, false, r.level);
                    if (Mathf.Abs(r.fine) > 0.01f) { Unregister(f); f.fineYaw = r.fine; f.Place(r.a, r.rot); Register(f); }
                    int k = 0;
                    foreach (var st in f.GetComponentsInChildren<Styleable>(true))
                        for (int p = 0; p < st.parts.Count && k < r.styles.Count; p++, k++) st.SetStyle(p, r.styles[k]);
                    f.lightLevel = r.light; f.lightColor = r.lightColor; f.boughtAt = r.bought;
                    f.ApplyLight();
                }
            }
            foreach (var id in lots) house.Rebuild(house.LotById(id) ?? house.PlayerLot);
        }

        /// <summary>Выполнить действие стройки так, чтобы его можно было отменить.</summary>
        void Recorded(string name, Action act, BuildSnap before = null)
        {
            before = before ?? Snap();
            act();
            PushBuild(name, before, Snap());
        }

        void PushBuild(string name, BuildSnap before, BuildSnap after)
        {
            if (SameSnap(before, after)) return;
            float dm = after.money - before.money;
            PushUndo(name,
                () => { ApplySnap(after, before); money -= dm; },
                () => { ApplySnap(before, after); money += dm; });
        }

        // ---------------- земля: мазок кисти ----------------

        Dictionary<int, Vector2> strokeOld;
        List<(Vector3 p, int seed)> strokeFlowers;
        List<(Vector3 p, float s, bool pine)> strokeTrees;
        float strokeMoney;
        string strokeName;

        List<(Vector3, int)> FlowerList() { var l = new List<(Vector3, int)>(); foreach (var f in house.terrain.flowers) l.Add((f.p, f.seed)); return l; }
        List<(Vector3, float, bool)> TreeList() { var l = new List<(Vector3, float, bool)>(); foreach (var t in house.trees) l.Add((t.p, t.s, t.pine)); return l; }

        void BeginStroke(string name)
        {
            if (strokeOld != null) EndStroke();
            strokeOld = new Dictionary<int, Vector2>();
            house.terrain.undoLog = strokeOld;
            strokeFlowers = FlowerList();
            strokeTrees = TreeList();
            strokeMoney = money;
            strokeName = name;
        }

        void EndStroke()
        {
            if (strokeOld == null) return;
            var t = house.terrain;
            t.undoLog = null;
            var cols = new List<(int idx, Vector2 before, Vector2 after)>();
            foreach (var kv in strokeOld) cols.Add((kv.Key, kv.Value, t.ColumnState(kv.Key)));
            var fb = strokeFlowers; var fa = FlowerList();
            var tb = strokeTrees; var ta = TreeList();
            float dm = money - strokeMoney;
            strokeOld = null;
            bool plantsChanged = fb.Count != fa.Count || tb.Count != ta.Count;
            if (cols.Count == 0 && !plantsChanged) return;
            NetStroke(cols, false, fb, fa, tb, ta);
            PushUndo(strokeName,
                () => { RestoreTerrain(cols, true); RestorePlants(fa, fb, ta, tb); money -= dm; NetStroke(cols, true, fa, fb, ta, tb); },
                () => { RestoreTerrain(cols, false); RestorePlants(fb, fa, tb, ta); money += dm; NetStroke(cols, false, fb, fa, tb, ta); });
        }

        void RestoreTerrain(List<(int idx, Vector2 before, Vector2 after)> cols, bool toBefore)
        {
            if (cols.Count == 0) return;
            var list = new List<(int, Vector2)>();
            foreach (var c in cols) list.Add((c.idx, toBefore ? c.before : c.after));
            var rect = house.terrain.SetColumns(list);
            house.UpdateGrid(rect);
            house.SettleTrees(new Vector3(rect.center.x, 0, rect.center.y), Mathf.Max(rect.width, rect.height));
        }

        /// <summary>Цветы и деревья: из набора «from» сделать набор «to» (по местам и видам).</summary>
        void RestorePlants(List<(Vector3 p, int seed)> fromF, List<(Vector3 p, int seed)> toF, List<(Vector3 p, float s, bool pine)> fromT, List<(Vector3 p, float s, bool pine)> toT)
        {
            var t = house.terrain;
            foreach (var f in fromF)
                if (!toF.Contains(f))
                {
                    var spot = t.flowers.Find(x => x.p == f.p && x.seed == f.seed);
                    if (spot != null) { if (spot.go != null) Destroy(spot.go); t.flowers.Remove(spot); }
                }
            foreach (var f in toF) if (!fromF.Contains(f)) t.AddFlowers(f.p, f.seed);
            foreach (var tr in fromT)
                if (!toT.Contains(tr))
                {
                    var rec = house.trees.Find(x => x.p == tr.p && Mathf.Approximately(x.s, tr.s) && x.pine == tr.pine);
                    if (rec != null) { if (rec.st != null) Destroy(rec.st.gameObject); house.trees.Remove(rec); }
                }
            foreach (var tr in toT) if (!fromT.Contains(tr)) house.PlantTree(tr.p, tr.s, tr.pine);
        }
    }
}
