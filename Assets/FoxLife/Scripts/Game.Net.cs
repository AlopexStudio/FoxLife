using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Игра по сети со стороны мира: применить пришедшие изменения, чьи деньги и чья свобода воли.</summary>
    public partial class Game
    {
        /// <summary>Скорость, которую выбрал этот игрок (по сети время идёт с самой медленной из выбранных).</summary>
        public int wantSpeed = 1;

        /// <summary>Режим мэра работает только в одиночной игре: по сети каждый строит у себя.</summary>
        bool Mayor => mayorMode && !Net.Active;

        /// <summary>Сами ли решают лисички этой семьи, чем заняться (у семей друзей — как выбрал друг).</summary>
        public bool AutonomyFor(int home)
        {
            if (home == House.PlayerLotId) return autonomy;
            if (Net.IsHost) { var p = Net.I.PeerOf(home); if (p != null) return p.autonomy; }
            return true;
        }

        /// <summary>Наряд выбран вручную и лисичка сама не переодевается.</summary>
        public bool ManualOutfits(int home)
        {
            if (home == House.PlayerLotId) return !autoOutfits;
            if (Net.IsHost) { var p = Net.I.PeerOf(home); if (p != null) return !p.autoOutfits; }
            return false;
        }

        /// <summary>Отключились от друга: вернуться на главный экран со своим городом.</summary>
        public void ReturnToTitle(string note)
        {
            StartCoroutine(TitleRoutine(note));
        }

        IEnumerator TitleRoutine(string note)
        {
            started = false;
            mode = Mode.Live;
            ui.screen = GameUI.MenuScreen.Title;
            Town.ResetLots();
            townName = "Лисья Долина";
            removedProps.Clear();
            loading = true;
            loadingText = "Возвращаемся домой…";
            loadingProgress = 0.02f;
            yield return null; yield return null;
            yield return ResetWorldAsync(true);
            SetupTitleScene();
            loading = false;
            SetSpeed(1);
            if (!string.IsNullOrEmpty(note)) ui.Note(note);
        }

        /// <summary>У клиента: лисички больше нет в городе хоста.</summary>
        public void NetDropFox(Fox f)
        {
            if (f == null) return;
            foxes.Remove(f);
            foreach (var o in foxes)
            {
                o.rel.Remove(f);
                o.romance.Remove(f);
                if (o.side != null && o.side.other == f) o.side = null;
                if (o.current != null && o.current.other == f) o.current = null;
            }
            foreach (var fu in furniture)
                for (int i = 0; i < fu.occupants.Length; i++)
                    if (fu.occupants[i] == f) fu.occupants[i] = null;
            if (selected == f) Select(Household.Count > 0 ? Household[0] : null);
            if (styleTarget == f.style) styleTarget = null;
            Destroy(f.gameObject);
        }

        /// <summary>Поставить, передвинуть или оставить как есть предмет, о котором сообщили по сети.</summary>
        public Furniture NetUpsertFurniture(Furniture f, FurnitureDef def, Vector2Int a, int rot, int level, float yaw)
        {
            if (f != null && f.def != def) { RemoveFurniture(f); f = null; }
            if (f != null)
            {
                if (f.anchor != a || f.rot != rot || f.level != level || Mathf.Abs(f.fineYaw - yaw) > 0.01f)
                {
                    Unregister(f);
                    f.level = level;
                    f.fineYaw = yaw;
                    f.Place(a, rot);
                    Register(f);
                }
                return f;
            }
            f = SpawnFurniture(def, a, rot, false, level);
            if (Mathf.Abs(yaw) > 0.01f) { Unregister(f); f.fineYaw = yaw; f.Place(a, rot); Register(f); }
            return f;
        }

        // ---------------- постройка участка ----------------

        static int CmpEdge(SaveEdge a, SaveEdge b)
        {
            if (a.level != b.level) return a.level.CompareTo(b.level);
            if (a.x != b.x) return a.x.CompareTo(b.x);
            if (a.z != b.z) return a.z.CompareTo(b.z);
            return a.v.CompareTo(b.v);
        }

        Styleable[] LotSurfaces(House.Lot l) => new[] { l.facade, l.openings, l.fence, l.roof, l.railing, l.ground };

        public NetLot NetLotData(int id)
        {
            var lot = house != null ? house.LotById(id) : null;
            if (lot == null) return null;
            var d = new NetLot { lot = id };
            foreach (var t in house.Layout())
                if (t.k != EdgeKind.None && house.EdgeLot(t.x, t.z, t.v) == id)
                    d.edges.Add(new SaveEdge { level = t.level, x = t.x, z = t.z, v = t.v, kind = (int)t.k });
            d.edges.Sort(CmpEdge);
            foreach (var r in house.rooms)
            {
                if (r.lot != id || r.cells.Count == 0) continue;
                var c = r.cells[0];
                foreach (var x in r.cells) if (x.x < c.x || (x.x == c.x && x.y < c.y)) c = x;
                d.rooms.Add(new SaveRoom
                {
                    level = r.level, x = c.x, z = c.y, name = r.name, roofType = r.roofType, roofDir = r.roofDir, roofPitch = r.roofPitch,
                    floor = r.st.Get(0).Clone(), wall = r.st.parts.Count > 1 ? r.st.Get(1).Clone() : null,
                });
            }
            d.rooms.Sort((a, b) => a.level != b.level ? a.level.CompareTo(b.level) : a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
            foreach (var st in LotSurfaces(lot))
            {
                var sl = new StyleList();
                if (st != null) foreach (var p in st.parts) sl.s.Add(p.style.Clone());
                d.surf.Add(sl);
            }
            return d;
        }

        public void NetApplyLot(NetLot d)
        {
            var lot = house.LotById(d.lot);
            if (lot == null) return;
            var want = new Dictionary<(int, int, int, bool), EdgeKind>();
            foreach (var e in d.edges) want[(e.level, e.x, e.z, e.v)] = (EdgeKind)e.kind;
            foreach (var t in house.Layout())
            {
                if (t.k == EdgeKind.None || house.EdgeLot(t.x, t.z, t.v) != d.lot) continue;
                if (!want.ContainsKey((t.level, t.x, t.z, t.v))) house.SetEdge(t.level, t.x, t.z, t.v, EdgeKind.None);
            }
            foreach (var kv in want)
            {
                var k = kv.Key;
                if (house.GetEdge(k.Item1, k.Item2, k.Item3, k.Item4) != kv.Value) house.SetEdge(k.Item1, k.Item2, k.Item3, k.Item4, kv.Value);
            }
            house.Rebuild(lot);
            foreach (var sr in d.rooms)
            {
                var room = house.RoomAt(sr.level, new Vector2Int(sr.x, sr.z));
                if (room == null) continue;
                if (!string.IsNullOrEmpty(sr.name)) { room.name = sr.name; room.st.displayName = sr.name; }
                if (sr.floor != null) room.st.SetStyle(0, sr.floor);
                if (sr.wall != null && room.st.parts.Count > 1) room.st.SetStyle(1, sr.wall);
                room.roofType = Mathf.Clamp(sr.roofType, 0, 3);
                room.roofDir = sr.roofDir;
                room.roofPitch = sr.roofPitch > 0 ? sr.roofPitch : 0.62f;
            }
            house.RebuildRoofs(lot);
            var surf = LotSurfaces(lot);
            for (int i = 0; i < surf.Length && i < d.surf.Count; i++)
                if (surf[i] != null && d.surf[i] != null) SaveSystem.ApplyStylesTo(surf[i], d.surf[i].s);
            ApplyFurnitureVisibility();
        }

        // ---------------- земля ----------------

        public void NetApplyTerrain(List<(int, Vector2)> cols, List<(Vector3, int)> remF, List<(Vector3, int)> addF,
            List<(Vector3, float, bool)> remT, List<(Vector3, float, bool)> addT)
        {
            if (cols.Count > 0)
            {
                var rect = house.terrain.SetColumns(cols);
                house.UpdateGrid(rect);
                house.SettleTrees(new Vector3(rect.center.x, 0, rect.center.y), Mathf.Max(rect.width, rect.height));
            }
            RestorePlants(remF, addF, remT, addT);
        }

        /// <summary>Мазок кисти земли (или его отмена) — друзьям.</summary>
        void NetStroke(List<(int idx, Vector2 before, Vector2 after)> cols, bool toBefore,
            List<(Vector3 p, int seed)> fromF, List<(Vector3 p, int seed)> toF, List<(Vector3 p, float s, bool pine)> fromT, List<(Vector3 p, float s, bool pine)> toT)
        {
            if (!Net.Active) return;
            var cl = new List<(int, Vector2)>();
            foreach (var c in cols) cl.Add((c.idx, toBefore ? c.before : c.after));
            Net.SendTerrain(cl, fromF.FindAll(x => !toF.Contains(x)), toF.FindAll(x => !fromF.Contains(x)),
                fromT.FindAll(x => !toT.Contains(x)), toT.FindAll(x => !fromT.Contains(x)));
        }

        public void NetRemoveProp(bool tree, string key, Vector3 pos)
        {
            if (tree)
            {
                var rec = house.trees.Find(t => t.st != null && (t.p - pos).sqrMagnitude < 0.01f);
                if (rec != null) RemoveProp(rec.st.transform);
            }
            else if (key != null && !removedProps.Contains(key)) RemovePropByKey(key);
        }
    }
}
