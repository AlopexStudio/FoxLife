using UnityEngine;

namespace FoxLife
{
    public partial class Game
    {
        static readonly Color EyedropperBlue = new Color(0.06f, 0.17f, 0.31f);

        public bool CanQuickBuild => started && mode == Mode.Live && ui.DeveloperMode &&
            (Net.I == null || !Net.I.NeedsPicker) && foxes.Exists(IsPlayer);

        internal void QuickBuild()
        {
            if (!CanQuickBuild) return;
            ui.CloseMenu();
            SetMode(Mode.Build);
            cam.Follow(null);
            var lot = house.PlayerLot;
            if (!lot.rect.Contains(GridMap.CellOf(cam.target)))
            {
                var center = lot.rect.center;
                cam.Focus(new Vector3(center.x, house.terrain.GroundAt(center.x, center.y), center.y), 24f);
            }
        }

        internal void ToggleBuildTool(WallTool tool) => SetWallTool(wallTool == tool ? WallTool.None : tool);

        bool TryBuildEdgeAtCursor(int level, RaycastHit hit, out (int x, int z, bool v) edge, out bool opening)
        {
            if (hit.collider != null && house.TryHitEdge(hit.transform, level, out edge, out opening)) return true;
            opening = false;
            edge = default;
            if (!GroundPoint(out var point)) return false;
            edge = NearestWall(point, out float distance);
            return distance < 0.25f && house.GetEdge(level, edge.x, edge.z, edge.v) != EdgeKind.None;
        }

        bool CanSampleFurniture(Furniture item)
        {
            if (item == null || item.isGhost || !item.def.buildable || item.level != house.viewLevel) return false;
            var categories = townBuilder ? Catalog.TownCategories : Catalog.Categories;
            return System.Array.IndexOf(categories, item.def.category) >= 0;
        }

        internal bool SampleBuildFurniture(Furniture item)
        {
            if (!CanSampleFurniture(item) || !StartPlacing(item.def, item)) return false;
            ui.ShowBuildSample(item.def);
            return true;
        }

        WallTool SampledWallTool(int level, (int x, int z, bool v) edge)
        {
            var kind = house.GetEdge(level, edge.x, edge.z, edge.v);
            if (kind == EdgeKind.OpeningEnd)
            {
                var first = House.AlongEdge(edge, -1);
                kind = house.GetEdge(level, first.x, first.z, first.v);
            }
            switch (House.OpeningKind(kind))
            {
                case EdgeKind.Wall: return WallTool.Wall;
                case EdgeKind.Door: return WallTool.Door;
                case EdgeKind.Window: return WallTool.Window;
                case EdgeKind.Arch: return WallTool.Arch;
                case EdgeKind.Fence: return WallTool.Fence;
                case EdgeKind.Gate: return WallTool.Gate;
                case EdgeKind.Railing: return WallTool.Railing;
                case EdgeKind.RailingOpen: return WallTool.RailingGap;
                default: return WallTool.None;
            }
        }

        internal bool SampleBuildWall(int level, (int x, int z, bool v) edge)
        {
            var tool = SampledWallTool(level, edge);
            if (tool == WallTool.None) return false;
            SetWallTool(tool);
            ui.ShowBuildSample();
            return true;
        }

        void UpdateEyedropper()
        {
            if (Input.GetMouseButtonDown(1)) { SetWallTool(WallTool.None); return; }
            if (ui.MouseOverUI) { ClearHover(); house.ClearBuildHighlight(); return; }

            int level = house.viewLevel;
            bool click = Input.GetMouseButtonDown(0);
            if (RaycastWorld(out var hit, out var part) && part.owner.GetComponentInParent<Furniture>() is Furniture item)
            {
                house.ClearBuildHighlight();
                bool allowed = CanSampleFurniture(item);
                SetHover(allowed ? item.style : null, -1, EyedropperBlue);
                if (allowed && click) SampleBuildFurniture(item);
                return;
            }

            ClearHover();
            if (!TryBuildEdgeAtCursor(level, hit, out var edge, out bool opening)) { house.ClearBuildHighlight(); return; }
            house.SetBuildHighlight(level, edge, opening, EyedropperBlue);
            if (click) SampleBuildWall(level, edge);
        }
    }
}
