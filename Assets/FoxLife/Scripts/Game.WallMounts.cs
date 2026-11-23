using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public partial class Game
    {
        static readonly Vector2Int[] MountDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        static IEnumerable<(int x, int z, bool v)> MountEdges(FurnitureDef def, Vector2Int anchor, int rotation)
        {
            var back = MountDirections[rotation & 3];
            foreach (var cell in Furniture.FootprintAt(def, anchor, rotation)) yield return RoomEdge(cell, back);
        }

        bool MountedFurnitureHidden(Furniture item)
        {
            if (!item.def.wallMounted) return false;
            foreach (var edge in MountEdges(item.def, item.anchor, item.rot))
                if (!house.WallMountVisible(item.level, edge)) return true;
            return false;
        }

        internal void RefreshMountedFurnitureVisibility()
        {
            foreach (var item in furniture)
                if (item != null && item.def.wallMounted && !item.isGhost)
                    item.gameObject.SetActive(!FurnitureHidden(item));
        }

        bool HasFurnitureOnWall(int level, (int x, int z, bool v) edge)
        {
            foreach (var item in furniture)
            {
                if (item == null || item.level != level || !item.def.wallMounted) continue;
                foreach (var support in MountEdges(item.def, item.anchor, item.rot))
                    if (support == edge) return true;
            }
            return false;
        }

        bool TrySnapWallMount(FurnitureDef def, out Vector2Int anchor, out int rotation)
        {
            anchor = default;
            rotation = 0;
            int level = house.viewLevel;
            if (!RaycastWorld(out var hit, out _) || !house.TryHitEdge(hit.transform, level, out var edge, out _)) return false;
            if (house.GetEdge(level, edge.x, edge.z, edge.v) != EdgeKind.Wall) return false;
            float normal = edge.v ? hit.normal.x : hit.normal.z;
            if (Mathf.Abs(normal) < 0.2f)
                normal = edge.v ? camera3d.transform.position.x - edge.x : camera3d.transform.position.z - edge.z;
            bool positiveSide = normal >= 0f;
            rotation = edge.v ? (positiveSide ? 3 : 1) : (positiveSide ? 2 : 0);
            float along = edge.v ? hit.point.z : hit.point.x;
            int start = Mathf.RoundToInt(along - def.w * 0.5f);
            int cross = (edge.v ? edge.x : edge.z) - (positiveSide ? 0 : 1);
            anchor = edge.v ? new Vector2Int(cross, start) : new Vector2Int(start, cross);
            return true;
        }
    }
}
