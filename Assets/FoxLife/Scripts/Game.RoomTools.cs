using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public partial class Game
    {
        static readonly Vector2Int[] RoomSides =
        {
            Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up
        };

        class RoomDrag
        {
            public House.Room room;
            public RectInt bounds;
            public Vector2Int start, side;
            public Vector2Int delta = new Vector2Int(int.MinValue, int.MinValue);
            public RoomPlan plan;
        }

        internal class RoomPlan
        {
            public readonly Dictionary<(int x, int z, bool v), EdgeKind> edges = new Dictionary<(int, int, bool), EdgeKind>();
            public readonly List<Furniture> furniture = new List<Furniture>();
            public Vector2Int destination, offset;
            public int cost;
        }

        class RoomFinish
        {
            public int level, roof, direction;
            public float pitch;
            public string name;
            public Vector2Int cell;
            public StyleData floor, wall;

            public RoomFinish(House.Room room)
            {
                level = room.level; roof = room.roofType; direction = room.roofDir; pitch = room.roofPitch;
                cell = room.cells[0]; name = room.name; floor = room.st.Get(0).Clone(); wall = room.st.Get(1).Clone();
            }

            public void Apply(House house, Vector2Int anchor)
            {
                var room = house.RoomAt(level, anchor);
                if (room == null) return;
                room.name = name; room.st.displayName = name;
                room.roofType = roof; room.roofDir = direction; room.roofPitch = pitch;
                room.st.SetStyle(0, floor.Clone()); room.st.SetStyle(1, wall.Clone());
            }
        }

        RoomDrag roomDrag;
        public string RoomToolHint { get; private set; }

        static RectInt RoomBounds(House.Room room)
        {
            var a = room.cells[0];
            var b = a;
            foreach (var c in room.cells) { a = Vector2Int.Min(a, c); b = Vector2Int.Max(b, c); }
            return new RectInt(a, b - a + Vector2Int.one);
        }

        void UpdateRoomTool()
        {
            if (Input.GetMouseButtonDown(1)) { roomDrag = null; RoomToolHint = null; HideWallGhost(); return; }
            if (!GroundPoint(out var point))
            {
                if (Input.GetMouseButtonUp(0)) { roomDrag = null; RoomToolHint = null; HideWallGhost(); }
                return;
            }
            var cell = GridMap.CellOf(point);
            if (roomDrag == null)
            {
                HideWallGhost();
                if (ui.MouseOverUI || !Input.GetMouseButtonDown(0)) return;
                bool resize = wallTool == WallTool.ResizeRoom;
                var room = house.RoomAt(house.viewLevel, cell);
                Vector2Int side = default;
                if (resize)
                {
                    float nearest = 0.32f;
                    foreach (var direction in RoomSides)
                    {
                        float distance = direction.x < 0 ? point.x - cell.x : direction.x > 0 ? cell.x + 1 - point.x
                            : direction.y < 0 ? point.z - cell.y : cell.y + 1 - point.z;
                        var edge = RoomEdge(cell, direction);
                        if (distance > nearest || !House.IsBoundary(house.GetEdge(house.viewLevel, edge.x, edge.z, edge.v))) continue;
                        var inside = house.RoomAt(house.viewLevel, cell);
                        var outside = house.RoomAt(house.viewLevel, cell + direction);
                        if (inside == outside) continue;
                        var candidate = inside ?? outside;
                        if (candidate == null) continue;
                        nearest = distance;
                        side = inside == candidate ? direction : -direction;
                        room = candidate;
                    }
                    if (side == default) { Log("Начни перетаскивание у стены комнаты."); return; }
                }
                if (room == null || !MayEditLot(room.lot)) { Log("Здесь нет комнаты, которую можно изменить."); return; }
                var bounds = RoomBounds(room);
                if (resize && bounds.width * bounds.height != room.cells.Count) { Log("За стену можно растянуть прямоугольную комнату."); return; }
                roomDrag = new RoomDrag { room = room, bounds = bounds, side = side,
                    start = resize ? new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z)) : cell };
            }

            var drag = roomDrag;
            if (drag.room.level != house.viewLevel) { roomDrag = null; RoomToolHint = null; HideWallGhost(); return; }
            bool moving = drag.side == default;
            var delta = moving ? cell - drag.start : new Vector2Int(
                drag.side.x == 0 ? 0 : Mathf.RoundToInt(point.x) - drag.start.x,
                drag.side.y == 0 ? 0 : Mathf.RoundToInt(point.z) - drag.start.y);
            RectInt destination = RoomDestination(drag.bounds, drag.side, delta);
            if (drag.delta != delta)
            {
                drag.delta = delta;
                drag.plan = PlanRoom(drag.room, destination, drag.side, delta, out string reason);
                RoomToolHint = reason ?? (drag.plan != null && drag.plan.cost > 0 ? $"Стоимость — {drag.plan.cost} мон." : null);
            }
            float y = drag.room.elev + House.LevelY(drag.room.level);
            if (destination.width > 0 && destination.height > 0)
                ShowWallGhost(new Vector3(destination.center.x, y + 0.07f, destination.center.y),
                    new Vector3(destination.width, 0.14f, destination.height),
                    drag.plan == null ? new Color(1f, 0.3f, 0.25f) : new Color(0.3f, 0.8f, 0.45f));
            else HideWallGhost();

            if (!Input.GetMouseButtonUp(0)) return;
            roomDrag = null;
            RoomToolHint = null;
            HideWallGhost();
            if (delta == default || ui.MouseOverUI) return;
            var plan = PlanRoom(drag.room, destination, drag.side, delta, out string error);
            if (plan == null) { Log(error); return; }
            ApplyRoomPlan(drag.room, plan, moving);
        }

        static RectInt RoomDestination(RectInt b, Vector2Int side, Vector2Int delta)
        {
            if (side == default) return new RectInt(b.position + delta, b.size);
            int x = b.xMin + (side.x < 0 ? delta.x : 0);
            int z = b.yMin + (side.y < 0 ? delta.y : 0);
            return new RectInt(x, z, b.width + side.x * delta.x, b.height + side.y * delta.y);
        }

        static (int x, int z, bool v) RoomEdge(Vector2Int cell, Vector2Int direction) =>
            (cell.x + (direction.x > 0 ? 1 : 0), cell.y + (direction.y > 0 ? 1 : 0), direction.x != 0);

        internal RoomPlan PlanRoom(House.Room room, RectInt destination, Vector2Int side, Vector2Int delta, out string error)
        {
            error = "Комната не помещается на участке.";
            var lot = house.LotById(room.lot);
            if (destination.width < 1 || destination.height < 1 || !lot.rect.Contains(destination.min) ||
                !lot.rect.Contains(destination.max - Vector2Int.one)) return null;

            bool moving = side == default;
            int level = room.level;
            var source = new HashSet<Vector2Int>(room.cells);
            var target = new HashSet<Vector2Int>();
            if (moving) foreach (var c in source) target.Add(c + delta);
            else foreach (var c in destination.allPositionsWithin) target.Add(c);

            foreach (var c in target)
            {
                if (source.Contains(c)) continue;
                if (house.RoomAt(level, c) != null) { error = "Здесь уже есть другая комната."; return null; }
                bool supported = level == 0 ? house.HasFloor(0, c) : house.RoomAt(level - 1, c) != null;
                if (!supported) { error = "Под новой частью комнаты нет подходящей опоры."; return null; }
            }
            foreach (var c in source)
            {
                if (target.Contains(c)) continue;
                if (level + 1 < House.Levels && house.RoomAt(level + 1, c) != null)
                { error = "Эта часть поддерживает комнату этажом выше."; return null; }
                if (level > 0 && foxes.Exists(f => f != null && f.Level == level && f.Cell == c))
                { error = "Сначала выведи лисичку из этой части комнаты."; return null; }
            }

            var plan = new RoomPlan { offset = moving ? delta : Vector2Int.zero };
            foreach (var c in target) { plan.destination = c; break; }
            var oldEdges = new Dictionary<(int x, int z, bool v), EdgeKind>();
            var innerEdges = new Dictionary<(int x, int z, bool v), EdgeKind>();
            foreach (var c in source)
                foreach (var dir in RoomSides)
                {
                    var key = RoomEdge(c, dir);
                    var kind = house.GetEdge(level, key.x, key.z, key.v);
                    if (source.Contains(c + dir))
                    {
                        if (kind == EdgeKind.None || dir.x < 0 || dir.y < 0) continue;
                        if (!moving && (!target.Contains(c) || !target.Contains(c + dir)))
                        { error = "Изменению комнаты мешает внутренняя перегородка."; return null; }
                        innerEdges[key] = kind;
                        if (moving) plan.edges[key] = EdgeKind.None;
                        continue;
                    }
                    oldEdges[key] = kind;
                    if (house.RoomAt(level, c + dir) == null) plan.edges[key] = EdgeKind.None;
                }

            var reusedOpenings = new HashSet<(int x, int z, bool v)>();
            var removable = new HashSet<(int x, int z, bool v)>(plan.edges.Keys);
            bool veranda = !house.RoomHasWalls(room);
            foreach (var c in target)
                foreach (var dir in RoomSides)
                {
                    var key = RoomEdge(c, dir);
                    var existing = house.GetEdge(level, key.x, key.z, key.v);
                    if (target.Contains(c + dir))
                    {
                        if (existing != EdgeKind.None && !removable.Contains(key) && (moving || !innerEdges.ContainsKey(key)))
                        { error = "На новом месте есть стена или забор."; return null; }
                        continue;
                    }

                    if (!House.ValidEdge(key.x, key.z, key.v) || house.EdgeLot(key.x, key.z, key.v) != room.lot)
                        return null;
                    if (existing != EdgeKind.None && !oldEdges.ContainsKey(key) && !removable.Contains(key))
                    { error = "Контур пересекает существующую стену или забор."; return null; }

                    var original = key;
                    if (moving || dir == side) original = (key.x - delta.x, key.z - delta.y, key.v);
                    bool copied = oldEdges.TryGetValue(original, out var kind);
                    if (!copied) kind = veranda ? EdgeKind.Railing : EdgeKind.Wall;
                    if (copied && kind != EdgeKind.Wall && kind != EdgeKind.Railing)
                    {
                        if (original == key || removable.Contains(original)) reusedOpenings.Add(original);
                        else kind = House.IsRailing(kind) ? EdgeKind.Railing : EdgeKind.Wall;
                    }
                    if (existing != EdgeKind.None && !removable.Contains(key)) kind = existing;
                    plan.edges[key] = kind;
                }
            if (moving) foreach (var item in innerEdges)
                plan.edges[(item.Key.x + delta.x, item.Key.z + delta.y, item.Key.v)] = item.Value;
            foreach (var item in oldEdges)
                if (item.Value != EdgeKind.Wall && item.Value != EdgeKind.Railing && !reusedOpenings.Contains(item.Key) &&
                    plan.edges.TryGetValue(item.Key, out var final) && final != item.Value)
                { error = "При изменении пропадёт проём. Сначала перенеси его."; return null; }

            EdgeKind PlannedEdge(int x, int z, bool v) => plan.edges.TryGetValue((x, z, v), out var kind) ? kind : house.GetEdge(level, x, z, v);
            foreach (var key in plan.edges.Keys)
            {
                var next = House.AlongEdge(key, 1);
                var previous = House.AlongEdge(key, -1);
                var kind = PlannedEdge(key.x, key.z, key.v);
                bool starts = House.IsCenteredOpening(kind);
                bool ends = kind == EdgeKind.OpeningEnd;
                if (starts != (PlannedEdge(next.x, next.z, next.v) == EdgeKind.OpeningEnd) ||
                    ends != House.IsCenteredOpening(PlannedEdge(previous.x, previous.z, previous.v)) ||
                    starts && !House.OpeningJointClear(key, PlannedEdge))
                { error = "Сначала перенеси проём со стыка стен."; return null; }
            }

            var stationary = new HashSet<Vector2Int>();
            foreach (var item in furniture)
            {
                if (item == null || item.level != level || item.lot != room.lot || item.isGhost) continue;
                bool inside = false, outside = false, inTarget = false, cutOff = false;
                foreach (var c in item.Footprint())
                {
                    bool old = source.Contains(c), next = target.Contains(c);
                    inside |= old; outside |= !old; inTarget |= next;
                    cutOff |= old && !next;
                }
                if (moving && inside)
                {
                    if (outside || item.def.stair || item.def.id == "plate")
                    { error = "Сначала убери лестницу, посуду или предмет, который выступает за контур."; return null; }
                    plan.furniture.Add(item);
                }
                else
                {
                    if (cutOff || inTarget && !inside)
                    { error = "Изменению комнаты мешает мебель."; return null; }
                    if (inTarget && outside)
                    {
                        foreach (var c in item.Footprint())
                            if (!target.Contains(c)) { error = "Стена пересечёт мебель."; return null; }
                    }
                    foreach (var c in item.Footprint()) stationary.Add(c);
                }
                if (item.def.wallMounted)
                {
                    var anchor = moving && inside ? item.anchor + delta : item.anchor;
                    foreach (var support in MountEdges(item.def, anchor, item.rot))
                        if (PlannedEdge(support.x, support.z, support.v) != EdgeKind.Wall)
                        { error = "Сначала сними настенный ковёр."; return null; }
                }
            }
            foreach (var item in plan.furniture)
                foreach (var c in item.Footprint())
                    if (stationary.Contains(c + delta)) { error = "На новом месте стоит мебель."; return null; }

            int added = 0, removed = 0;
            foreach (var item in plan.edges)
            {
                var key = item.Key;
                var previous = house.GetEdge(level, key.x, key.z, key.v);
                if (previous == EdgeKind.None && item.Value != EdgeKind.None) added++;
                if (previous != EdgeKind.None && item.Value == EdgeKind.None) removed++;
            }
            if (!freeBuild && !moving)
            {
                int diff = (added - removed) * (veranda ? RailingPrice : WallPrice);
                plan.cost = diff < 0 ? diff / 2 : diff;
                if (plan.cost > money) { error = $"Нужно ещё {Mathf.CeilToInt(plan.cost - money)} мон."; return null; }
            }
            error = null;
            return plan;
        }

        List<RoomFinish> RoomFinishes(int lot) => house.rooms.FindAll(r => r.lot == lot).ConvertAll(r => new RoomFinish(r));

        internal void ApplyRoomPlan(House.Room room, RoomPlan plan, bool moving)
        {
            int level = room.level;
            var lot = house.LotById(room.lot);
            var before = Snap();
            var previousFinishes = RoomFinishes(lot.id);
            var finish = new RoomFinish(room);
            foreach (var f in plan.furniture)
            {
                foreach (var fox in foxes) if (fox != null) fox.CancelFor(f);
                Unregister(f);
            }
            foreach (var item in plan.edges)
            {
                var e = item.Key;
                house.SetEdge(level, e.x, e.z, e.v, item.Value);
            }
            house.Rebuild(lot);
            finish.Apply(house, plan.destination);
            house.RebuildRoofs(lot);
            foreach (var f in plan.furniture) { f.Place(f.anchor + plan.offset, f.rot); Register(f); }
            if (plan.cost != 0) AddMoney(-plan.cost, "Изменение комнаты");

            var after = Snap();
            var nextFinishes = RoomFinishes(lot.id);
            void Restore(BuildSnap from, BuildSnap to, List<RoomFinish> finishes)
            {
                ApplySnap(from, to);
                foreach (var state in finishes) state.Apply(house, state.cell);
                house.RebuildRoofs(lot);
                money += to.money - from.money;
            }
            PushUndo(moving ? "перенос комнаты" : "изменение комнаты",
                () => Restore(after, before, previousFinishes), () => Restore(before, after, nextFinishes));
        }
    }
}
