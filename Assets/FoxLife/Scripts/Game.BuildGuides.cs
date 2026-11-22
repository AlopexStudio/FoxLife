using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoxLife
{
    public partial class Game
    {
        public bool showBuildGrid = true;
        public bool showBuildBorders = true;
        MeshRenderer guideRenderer;
        Mesh guideMesh;
        (int lot, int level, int revision, bool grid, bool borders, bool town) guideState;

        void UpdateBuildGuides()
        {
            bool visible = !loading && house != null && mode == Mode.Build && ui.screen == GameUI.MenuScreen.None &&
                (showBuildGrid || showBuildBorders);
            if (!visible)
            {
                if (guideRenderer != null) guideRenderer.enabled = false;
                return;
            }

            var lot = townBuilder ? house.LotAt(GridMap.CellOf(cam.target)) : house.PlayerLot;
            var state = (lot?.id ?? -1, house.viewLevel, house.BuildGuideRevision, showBuildGrid, showBuildBorders, townBuilder);
            if (guideRenderer == null)
            {
                var go = new GameObject("Сетка и границы строительства");
                go.transform.SetParent(house.transform, false);
                guideMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                go.AddComponent<MeshFilter>().sharedMesh = guideMesh;
                guideRenderer = go.AddComponent<MeshRenderer>();
                guideRenderer.sharedMaterial = new Material(Shader.Find("FoxLife/Particle"));
                guideRenderer.sharedMaterial.SetFloat("_Unlit", 1f);
                guideRenderer.shadowCastingMode = ShadowCastingMode.Off;
                guideRenderer.receiveShadows = false;
            }
            else if (guideState == state) { guideRenderer.enabled = true; return; }
            guideRenderer.enabled = true;
            guideState = state;
            BuildGuideMesh(lot);
        }

        void BuildGuideMesh(House.Lot lot)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            int level = house.viewLevel;

            if (showBuildGrid && lot != null && !lot.removed)
            {
                var rect = lot.rect;
                var color = new Color32(255, 255, 255, 92);
                for (int x = rect.xMin; x <= rect.xMax; x++) Strip(x, rect.yMin, rect.yMax, true, false, 0.021f, color);
                for (int z = rect.yMin; z <= rect.yMax; z++) Strip(z, rect.xMin, rect.xMax, false, false, 0.021f, color);
            }
            if (showBuildBorders)
            {
                if (townBuilder) foreach (var entry in house.lots) Border(entry);
                else Border(lot);
            }
            guideMesh.Clear();
            guideMesh.SetVertices(vertices);
            guideMesh.SetColors(colors);
            guideMesh.SetTriangles(triangles, 0);

            void Border(House.Lot entry)
            {
                if (entry == null || entry.removed) return;
                var rect = entry.rect;
                var color = new Color32(255, 255, 255, 208);
                Strip(rect.xMin, rect.yMin, rect.yMax, true, true, 0.064f, color);
                Strip(rect.xMax, rect.yMin, rect.yMax, true, true, 0.064f, color);
                Strip(rect.yMin, rect.xMin, rect.xMax, false, true, 0.064f, color);
                Strip(rect.yMax, rect.xMin, rect.xMax, false, true, 0.064f, color);
            }

            void Strip(float fixedAxis, float from, float to, bool alongZ, bool border, float width, Color32 color)
            {
                for (float p = from; p < to; p += VoxelTerrain.S)
                {
                    float next = Mathf.Min(p + VoxelTerrain.S, to);
                    float mid = (p + next) * 0.5f;
                    float x = alongZ ? fixedAxis : mid, z = alongZ ? mid : fixedAxis;
                    float y = Mathf.Max(Height(x - (alongZ ? 0.02f : 0), z - (alongZ ? 0 : 0.02f), border),
                        Height(x + (alongZ ? 0.02f : 0), z + (alongZ ? 0 : 0.02f), border)) + (border ? 0.043f : 0.027f);
                    float a = fixedAxis - width * 0.5f, b = fixedAxis + width * 0.5f;
                    int i = vertices.Count;
                    vertices.Add(alongZ ? new Vector3(a, y, p) : new Vector3(p, y, a));
                    vertices.Add(alongZ ? new Vector3(b, y, p) : new Vector3(p, y, b));
                    vertices.Add(alongZ ? new Vector3(b, y, next) : new Vector3(next, y, b));
                    vertices.Add(alongZ ? new Vector3(a, y, next) : new Vector3(next, y, a));
                    colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
                    triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
                    triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
                }
            }

            float Height(float x, float z, bool border)
            {
                var cell = GridMap.CellOf(new Vector3(x, 0, z));
                if (!border && (level > 0 || house.RoomAt(0, cell) != null)) return house.FloorY(level, cell);
                return house.terrain.GroundAt(x, z);
            }
        }

        void ClearBuildGuides()
        {
            if (guideRenderer != null)
            {
                Destroy(guideRenderer.sharedMaterial);
                Destroy(guideRenderer.gameObject);
            }
            if (guideMesh != null) Destroy(guideMesh);
            guideRenderer = null;
            guideMesh = null;
        }
    }
}
