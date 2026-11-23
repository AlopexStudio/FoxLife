using UnityEngine;

namespace FoxLife
{
    public partial class Game
    {
        (int level, (int x, int z, bool v) edge, bool centered)? openingMove;
        public bool MovingOpening => openingMove.HasValue;
        static readonly Color SledgeRed = new Color(0.36f, 0.07f, 0.04f);

        static (int x, int z, bool v) NearestWall(Vector3 point, out float distance)
        {
            int x = Mathf.RoundToInt(point.x), z = Mathf.RoundToInt(point.z);
            float dx = Mathf.Abs(point.x - x), dz = Mathf.Abs(point.z - z);
            distance = Mathf.Min(dx, dz);
            return dx <= dz ? (x, Mathf.FloorToInt(point.z), true) : (Mathf.FloorToInt(point.x), z, false);
        }

        void UpdateOpeningTool(Vector3 point, int level)
        {
            var edge = NearestWall(point, out float distance);
            if (RaycastWorld(out var hit, out _) && house.TryHitEdge(hit.transform, level, out var visibleEdge, out _))
            {
                edge = visibleEdge;
                point = hit.point;
            }
            else if (distance > 0.3f) { HideWallGhost(); return; }

            var want = wallTool == WallTool.Door ? EdgeKind.Door : wallTool == WallTool.Window ? EdgeKind.Window
                : wallTool == WallTool.Gate ? EdgeKind.Gate : wallTool == WallTool.RailingGap ? EdgeKind.RailingOpen : EdgeKind.Arch;
            edge = SnapOpening(level, edge, point, want, out bool centered);
            int lot = house.EdgeLot(edge.x, edge.z, edge.v);
            if (!House.ValidEdge(edge.x, edge.z, edge.v) || !MayEditLot(lot)) { HideWallGhost(); return; }

            bool ok = CanPlaceOpening(level, edge, want, centered);
            float width = 0.96f;
            var center = house.OpeningCenter(level, edge.x, edge.z, edge.v, centered) + Vector3.up;
            ShowWallGhost(center, edge.v ? new Vector3(0.22f, 2.04f, width) : new Vector3(width, 2.04f, 0.22f),
                ok ? new Color(0.45f, 0.7f, 1f) : new Color(0.95f, 0.35f, 0.3f));
            if (!Input.GetMouseButtonDown(0)) return;
            if (ok)
            {
                if (MovingOpening) MoveOpening(level, edge, want, centered);
                else PlaceOpening(level, edge, want, centered);
            }
            else Log(want == EdgeKind.Gate ? "Калитка ставится в забор." : want == EdgeKind.RailingOpen ? "Вход делается в перилах."
                : "Здесь нужен свободный участок стены. Чтобы заменить проём, кликни по его центру.");
        }

        internal (int x, int z, bool v) SnapOpening(int level, (int x, int z, bool v) edge, Vector3 point, EdgeKind want, out bool centered)
        {
            centered = false;
            if (!House.IsFramedOpening(want)) return edge;
            float along = edge.v ? point.z : point.x;
            int vertex = Mathf.RoundToInt(along);
            if (Mathf.Abs(along - vertex) > 0.25f) return edge;
            var start = edge.v ? (edge.x, vertex - 1, true) : (vertex - 1, edge.z, false);
            centered = CanCenterOpening(level, start);
            return centered ? start : edge;
        }

        EdgeKind OpeningSupport(int level, (int x, int z, bool v) edge)
        {
            if (openingMove is { } move && move.level == level &&
                (edge == move.edge || move.centered && edge == House.AlongEdge(move.edge, 1))) return EdgeKind.Wall;
            return house.GetEdge(level, edge.x, edge.z, edge.v);
        }

        bool CanCenterOpening(int level, (int x, int z, bool v) edge) => MovingOpening
            ? house.CanCenterOpening(level, edge, (x, z, v) => OpeningSupport(level, (x, z, v)))
            : house.CanCenterOpening(level, edge);

        bool CanPlaceOpening(int level, (int x, int z, bool v) edge, EdgeKind want, bool centered)
        {
            int lot = house.EdgeLot(edge.x, edge.z, edge.v);
            if (house.LotById(lot) == null || !MayEditLot(lot)) return false;
            var kind = OpeningSupport(level, edge);
            if (centered) return CanCenterOpening(level, edge) && (MovingOpening ? kind == EdgeKind.Wall : House.OpeningKind(kind) != want);
            if (MovingOpening) return kind == EdgeKind.Wall;
            if (want == EdgeKind.Gate) return kind == EdgeKind.Fence;
            if (want == EdgeKind.RailingOpen) return kind == EdgeKind.Railing;
            return House.IsWallLike(kind) && !House.IsCenteredOpening(kind) && kind != EdgeKind.OpeningEnd && kind != want;
        }

        internal bool PlaceOpening(int level, (int x, int z, bool v) edge, EdgeKind want, bool centered)
        {
            if (!CanPlaceOpening(level, edge, want, centered)) return false;
            int price = OpeningPrice(want);
            if (!freeBuild && money < price) { Log($"Для покупки нужно {price} мон."); return false; }
            var before = Snap();
            string name = want == EdgeKind.Door ? "дверь" : want == EdgeKind.Window ? "окно" : want == EdgeKind.Gate ? "калитка"
                : want == EdgeKind.RailingOpen ? "вход в перилах" : "проём";
            if (!freeBuild) AddMoney(-price, "Строительство · " + name);
            WriteOpening(level, edge, want, centered);
            house.Rebuild(house.LotById(house.EdgeLot(edge.x, edge.z, edge.v)));
            PushBuild(name, before, Snap());
            foreach (var fox in foxes) fox.OnEvent(new GameEvent { kind = "buy" });
            return true;
        }

        void WriteOpening(int level, (int x, int z, bool v) edge, EdgeKind want, bool centered)
        {
            var kind = centered ? want == EdgeKind.Door ? EdgeKind.CenterDoor : want == EdgeKind.Window ? EdgeKind.CenterWindow : EdgeKind.CenterArch : want;
            house.SetEdge(level, edge.x, edge.z, edge.v, kind);
            if (centered)
            {
                var next = House.AlongEdge(edge, 1);
                house.SetEdge(level, next.x, next.z, next.v, EdgeKind.OpeningEnd);
            }
        }

        internal void StartMovingOpening(int level, (int x, int z, bool v) edge)
        {
            var kind = house.GetEdge(level, edge.x, edge.z, edge.v);
            var opening = House.OpeningKind(kind);
            if (!MayEditLot(house.EdgeLot(edge.x, edge.z, edge.v)) || !House.IsFramedOpening(kind)) return;
            SetWallTool(opening == EdgeKind.Door ? WallTool.Door : opening == EdgeKind.Window ? WallTool.Window : WallTool.Arch);
            openingMove = (level, edge, House.IsCenteredOpening(kind));
        }

        internal bool MoveOpening(int level, (int x, int z, bool v) edge, EdgeKind want, bool centered)
        {
            if (openingMove is not { } move || House.OpeningKind(house.GetEdge(move.level, move.edge.x, move.edge.z, move.edge.v)) != want ||
                !CanPlaceOpening(level, edge, want, centered)) return false;
            if (move.level != level || move.edge != edge || move.centered != centered)
            {
                Recorded("перенос проёма", () =>
                {
                    var sourceLot = house.LotById(house.EdgeLot(move.edge.x, move.edge.z, move.edge.v));
                    var targetLot = house.LotById(house.EdgeLot(edge.x, edge.z, edge.v));
                    WriteOpening(move.level, move.edge, EdgeKind.Wall, false);
                    if (move.centered) WriteOpening(move.level, House.AlongEdge(move.edge, 1), EdgeKind.Wall, false);
                    WriteOpening(level, edge, want, centered);
                    house.Rebuild(sourceLot);
                    if (targetLot != sourceLot) house.Rebuild(targetLot);
                });
            }
            SetWallTool(WallTool.None);
            return true;
        }

        internal void DemolishOpening(int level, (int x, int z, bool v) edge)
        {
            var lot = house.LotById(house.EdgeLot(edge.x, edge.z, edge.v));
            var kind = house.GetEdge(level, edge.x, edge.z, edge.v);
            if (lot == null || !MayEditLot(lot.id) || !House.IsFramedOpening(kind)) return;
            Recorded("снос проёма", () =>
            {
                WriteOpening(level, edge, EdgeKind.Wall, false);
                if (House.IsCenteredOpening(kind)) WriteOpening(level, House.AlongEdge(edge, 1), EdgeKind.Wall, false);
                if (!freeBuild) AddMoney(OpeningPrice(kind), "Снос проёма");
                house.Rebuild(lot);
            });
        }

        static int OpeningPrice(EdgeKind kind)
        {
            switch (House.OpeningKind(kind))
            {
                case EdgeKind.Door: return DoorPrice;
                case EdgeKind.Window: return WindowPrice;
                case EdgeKind.Arch: return ArchPrice;
                case EdgeKind.Gate: return GatePrice;
                case EdgeKind.RailingOpen: return RailingGapPrice;
                default: return 0;
            }
        }

        int RemoveWallSegment(int level, (int x, int z, bool v) edge)
        {
            var kind = house.GetEdge(level, edge.x, edge.z, edge.v);
            int refund = (House.IsFenceLike(kind) ? FencePrice : House.IsRailing(kind) ? RailingPrice : WallPrice) + OpeningPrice(kind);
            if (House.IsCenteredOpening(kind) || kind == EdgeKind.OpeningEnd)
            {
                var other = House.AlongEdge(edge, kind == EdgeKind.OpeningEnd ? -1 : 1);
                var partner = house.GetEdge(level, other.x, other.z, other.v);
                if (kind == EdgeKind.OpeningEnd && House.IsCenteredOpening(partner))
                {
                    refund += OpeningPrice(partner);
                    house.SetEdge(level, other.x, other.z, other.v, EdgeKind.Wall);
                }
                else if (House.IsCenteredOpening(kind) && partner == EdgeKind.OpeningEnd)
                    house.SetEdge(level, other.x, other.z, other.v, EdgeKind.Wall);
            }
            house.SetEdge(level, edge.x, edge.z, edge.v, EdgeKind.None);
            return refund;
        }

        internal void DemolishWall(int level, (int x, int z, bool v) edge)
        {
            var lot = house.LotById(house.EdgeLot(edge.x, edge.z, edge.v));
            if (lot == null || !MayEditLot(lot.id) || house.GetEdge(level, edge.x, edge.z, edge.v) == EdgeKind.None) return;
            Recorded("снос стены", () =>
            {
                int refund = RemoveWallSegment(level, edge);
                if (!freeBuild) AddMoney(refund, "Снос стены");
                house.Rebuild(lot);
            });
        }

        void UpdateSledgehammer()
        {
            if (ui.MouseOverUI)
            {
                ClearHover();
                house.ClearBuildHighlight();
                return;
            }
            int level = house.viewLevel;
            bool click = Input.GetMouseButtonDown(0);
            if (RaycastWorld(out var hit, out var part))
            {
                var item = part.owner.GetComponentInParent<Furniture>();
                if (item != null)
                {
                    house.ClearBuildHighlight();
                    bool allowed = item.level == level && MayEditLot(item.lot) && item.def.id != "plate";
                    SetHover(allowed ? item.style : null, -1, SledgeRed);
                    if (click && allowed) SellFurniture(item);
                    return;
                }
            }
            if (TryBuildEdgeAtCursor(level, hit, out var edge, out bool opening))
            {
                SledgeWall(level, edge, opening, click);
                return;
            }
            ClearHover();
            house.ClearBuildHighlight();
        }

        void SledgeWall(int level, (int x, int z, bool v) edge, bool opening, bool click)
        {
            ClearHover();
            if (!MayEditLot(house.EdgeLot(edge.x, edge.z, edge.v))) { house.ClearBuildHighlight(); return; }
            house.SetBuildHighlight(level, edge, opening, SledgeRed);
            if (!click) return;
            if (opening) DemolishOpening(level, edge); else DemolishWall(level, edge);
        }
    }
}
