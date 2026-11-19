using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Превращает предметы, собранные из простых фигур (куб, шар, цилиндр, капсула, конус, пирамида),
    /// в воксельные сетки. Фигуры остаются невидимыми «каркасом»: их коллайдеры нужны для кликов,
    /// а детали стиля (материалы) переходят на воксельную сетку — перекраска работает как раньше.
    /// Вокселы не отдельные объекты: всё склеивается в одну сетку, скрытые грани не рисуются.
    /// </summary>
    public static class Voxelizer
    {
        /// <summary>
        /// Грани чуть шире вокселя (в своей плоскости): большие склеенные грани соседствуют с мелкими, и на таких
        /// Т-образных стыках видеокарта оставляет микрощели — сквозь них просвечивали светлые точки, а свечение
        /// раздувало их в блики. Перекрытие в 3% вокселя закрывает щели и глазу не видно.
        /// </summary>
        public const float Inflate = 0.03f;

        public const float FurnitureVoxel = 0.0625f;
        public const float HouseVoxel = 0.0625f;
        public const float FoxVoxel = 0.03f;
        public const float OutdoorVoxel = 0.125f;

        enum Kind { Cube, Sphere, Cylinder, Capsule, Cone, Pyramid, Unknown }

        static Kind KindOf(MeshFilter mf)
        {
            if (mf == null || mf.sharedMesh == null) return Kind.Unknown;
            switch (mf.sharedMesh.name)
            {
                case "Cube": return Kind.Cube;
                case "Sphere": return Kind.Sphere;
                case "Cylinder": return Kind.Cylinder;
                case "Capsule": return Kind.Capsule;
                case "Cone": return Kind.Cone;
                case "Pyramid": return Kind.Pyramid;
                default: return Kind.Unknown;
            }
        }

        static bool Inside(Kind k, Vector3 q)
        {
            switch (k)
            {
                case Kind.Cube: return Mathf.Abs(q.x) <= 0.5f && Mathf.Abs(q.y) <= 0.5f && Mathf.Abs(q.z) <= 0.5f;
                case Kind.Sphere: return q.sqrMagnitude <= 0.25f;
                case Kind.Cylinder: return Mathf.Abs(q.y) <= 1f && q.x * q.x + q.z * q.z <= 0.25f;
                case Kind.Capsule:
                {
                    float y = Mathf.Clamp(q.y, -0.5f, 0.5f);
                    return new Vector3(q.x, q.y - y, q.z).sqrMagnitude <= 0.25f;
                }
                case Kind.Cone:
                {
                    if (q.y < -0.5f || q.y > 0.5f) return false;
                    float r = 0.5f * (0.5f - q.y);
                    return q.x * q.x + q.z * q.z <= r * r;
                }
                case Kind.Pyramid:
                {
                    if (q.y < -0.5f || q.y > 0.5f) return false;
                    float r = 0.5f * (0.5f - q.y);
                    return Mathf.Abs(q.x) <= r && Mathf.Abs(q.z) <= r;
                }
            }
            return false;
        }

        // локальные границы фигуры (в единицах фигуры)
        static Bounds LocalBounds(Kind k) => k == Kind.Cylinder || k == Kind.Capsule
            ? new Bounds(Vector3.zero, new Vector3(1, 2, 1))
            : new Bounds(Vector3.zero, Vector3.one);

        class Prim
        {
            public Renderer r;
            public Kind kind;
            public Matrix4x4 toLocal;   // из пространства сетки в единичное пространство фигуры
            public Vector3 inflate;     // растяжение тонких фигур, чтобы они не пропали
            public Bounds bounds;       // в пространстве сетки
            public int part;            // индекс материала
        }

        /// <summary>
        /// Запечь все фигуры-потомки (только прямые, если directOnly) пространства space в одну воксельную сетку.
        /// Возвращает созданный объект или null.
        /// </summary>
        /// <summary>Готовые сетки одинаковых предметов: у одной модели мебели сетка в своём пространстве всегда одна и та же.</summary>
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static GameObject Bake(Transform space, float vox, System.Func<Renderer, bool> filter = null, bool directOnly = true, Transform parentOverride = null, string cacheKey = null)
        {
            var list = new List<Renderer>();
            if (directOnly)
            {
                foreach (Transform ch in space)
                {
                    var r = ch.GetComponent<Renderer>();
                    if (r != null) list.Add(r);
                }
            }
            else list.AddRange(space.GetComponentsInChildren<Renderer>(true));
            if (filter != null) list.RemoveAll(r => !filter(r));
            return BakeRenderers(space, list, vox, parentOverride, null, cacheKey);
        }

        public static GameObject BakeRenderers(Transform space, List<Renderer> renderers, float vox, Transform parentOverride = null, List<Renderer> subtract = null, string cacheKey = null)
        {
            var prims = new List<Prim>();
            var mats = new List<Material>();
            var spaceInv = space.worldToLocalMatrix;
            Bounds total = default;
            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null || r.forceRenderingOff) continue;
                var kind = KindOf(r.GetComponent<MeshFilter>());
                if (kind == Kind.Unknown) continue;
                var m = spaceInv * r.transform.localToWorldMatrix; // единицы фигуры -> пространство сетки
                var lb = LocalBounds(kind);
                var b = new Bounds(m.MultiplyPoint3x4(lb.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    b.Encapsulate(m.MultiplyPoint3x4(c));
                }
                // тонкие фигуры (ковры, линии, экраны) раздуваем до толщины в один воксел
                var axisLen = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
                var size = Vector3.Scale(lb.size, axisLen);
                var infl = new Vector3(Mathf.Max(1f, vox * 1.05f / Mathf.Max(size.x, 1e-5f)), Mathf.Max(1f, vox * 1.05f / Mathf.Max(size.y, 1e-5f)), Mathf.Max(1f, vox * 1.05f / Mathf.Max(size.z, 1e-5f)));
                b.Expand(vox);
                int part = mats.IndexOf(r.sharedMaterial);
                if (part < 0) { mats.Add(r.sharedMaterial); part = mats.Count - 1; }
                prims.Add(new Prim { r = r, kind = kind, toLocal = m.inverse, inflate = infl, bounds = b, part = part });
                if (!any) { total = b; any = true; } else total.Encapsulate(b);
            }
            if (!any) return null;
            Mesh mesh = null;
            if (cacheKey != null && cache.TryGetValue(cacheKey, out mesh) && mesh != null)
            {
                foreach (var p in prims) p.r.forceRenderingOff = true;
                return MakeVoxelObject(space, mesh, mats, vox, parentOverride);
            }

            // сетка выровнена по пространству space: соседние предметы в одном пространстве совпадают вокселами
            var o = new Vector3(Mathf.Floor(total.min.x / vox), Mathf.Floor(total.min.y / vox), Mathf.Floor(total.min.z / vox)) * vox;
            int nx = Mathf.CeilToInt((total.max.x - o.x) / vox) + 1;
            int ny = Mathf.CeilToInt((total.max.y - o.y) / vox) + 1;
            int nz = Mathf.CeilToInt((total.max.z - o.z) / vox) + 1;
            if ((long)nx * ny * nz > 6_000_000) { Debug.LogWarning($"FoxLife: слишком большая воксельная сетка {space.name}"); return null; }
            var cells = new byte[nx * ny * nz]; // 0 — пусто, иначе part+1

            foreach (var p in prims)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((p.bounds.min.x - o.x) / vox)), x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((p.bounds.max.x - o.x) / vox));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((p.bounds.min.y - o.y) / vox)), y1 = Mathf.Min(ny - 1, Mathf.CeilToInt((p.bounds.max.y - o.y) / vox));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((p.bounds.min.z - o.z) / vox)), z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((p.bounds.max.z - o.z) / vox));
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var c = o + new Vector3((x + 0.5f) * vox, (y + 0.5f) * vox, (z + 0.5f) * vox);
                            var q = p.toLocal.MultiplyPoint3x4(c);
                            q = new Vector3(q.x / p.inflate.x, q.y / p.inflate.y, q.z / p.inflate.z);
                            if (Inside(p.kind, q)) cells[(y * nz + z) * nx + x] = (byte)(p.part + 1);
                        }
                p.r.forceRenderingOff = true; // фигура остаётся только каркасом для кликов
            }

            // проёмы: вырезаем вокселы, занятые окнами и дверями
            if (subtract != null)
                foreach (var r in subtract)
                {
                    var kind = KindOf(r.GetComponent<MeshFilter>());
                    if (kind == Kind.Unknown) continue;
                    var inv = (spaceInv * r.transform.localToWorldMatrix).inverse;
                    for (int y = 0; y < ny; y++)
                        for (int z = 0; z < nz; z++)
                            for (int x = 0; x < nx; x++)
                            {
                                int idx = (y * nz + z) * nx + x;
                                if (cells[idx] == 0) continue;
                                var q = inv.MultiplyPoint3x4(o + new Vector3((x + 0.5f) * vox, (y + 0.5f) * vox, (z + 0.5f) * vox));
                                if (Inside(kind, q * 0.999f)) cells[idx] = 0;
                            }
                }

            mesh = BuildMesh(cells, nx, ny, nz, vox, o, mats.Count);
            if (mesh == null) return null;
            if (cacheKey != null) cache[cacheKey] = mesh;
            return MakeVoxelObject(space, mesh, mats, vox, parentOverride);
        }

        static GameObject MakeVoxelObject(Transform space, Mesh mesh, List<Material> mats, float vox, Transform parentOverride)
        {
            var go = new GameObject("voxels");
            go.transform.SetParent(space, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats.ToArray();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            foreach (var m in mats) if (m != null && m.HasProperty("_Voxel")) { m.SetFloat("_Voxel", vox); m.SetFloat("_UseAO", 1f); }
            if (parentOverride != null) go.transform.SetParent(parentOverride, true);
            return go;
        }

        static readonly Vector3Int[] N = { Vector3Int.up, Vector3Int.down, Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
        static readonly Vector3Int[] U = { new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 1), new Vector3Int(0, 1, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 0, 0) };
        static readonly Vector3Int[] V = { new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0) };
        static readonly float[] AoK = { 0.6f, 0.74f, 0.87f, 1f };

        /// <summary>Сетка с отсечением скрытых граней; затенение углов — в цвете вершин; подсетка на каждую деталь.</summary>
        static byte[] maskBuf;

        static int Axis(Vector3Int d) => d.x != 0 ? 0 : d.y != 0 ? 1 : 2;

        /// <summary>
        /// Сетка с отсечением скрытых граней. Грани с затенением углов идут по одной (цвет вершин),
        /// а ровные незатенённые склеиваются в большие прямоугольники — рисунок вокселов всё равно рисует шейдер.
        /// </summary>
        public static Mesh BuildMesh(byte[] cells, int nx, int ny, int nz, float vox, Vector3 origin, int parts)
        {
            bool F(int x, int y, int z) => (uint)x < (uint)nx && (uint)y < (uint)ny && (uint)z < (uint)nz && cells[(y * nz + z) * nx + x] != 0;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var col = new List<Color32>();
            var tris = new List<int>[parts];
            for (int i = 0; i < parts; i++) tris[i] = new List<int>();
            int[] du = { -1, -1, 1, 1 }, dv = { -1, 1, 1, -1 };
            int[] ao = new int[4];
            int total = nx * ny * nz;
            if (maskBuf == null || maskBuf.Length < total) maskBuf = new byte[total];
            var mask = maskBuf;
            int[] dims = { nx, ny, nz };
            var full = new Color32(255, 255, 255, 255);

            for (int f = 0; f < 6; f++)
            {
                System.Array.Clear(mask, 0, total);
                var N0 = N[f]; var U0 = U[f]; var V0 = V[f];
                bool anyMerge = false;
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                        for (int x = 0; x < nx; x++)
                        {
                            int idx = (y * nz + z) * nx + x;
                            int part = cells[idx];
                            if (part == 0) continue;
                            int ox = x + N0.x, oy = y + N0.y, oz = z + N0.z;
                            if (F(ox, oy, oz)) continue;
                            bool flat = true;
                            for (int k = 0; k < 4; k++)
                            {
                                int ax = ox + U0.x * du[k], ay = oy + U0.y * du[k], az = oz + U0.z * du[k];
                                int bx = ox + V0.x * dv[k], by = oy + V0.y * dv[k], bz = oz + V0.z * dv[k];
                                bool s1 = F(ax, ay, az), s2 = F(bx, by, bz), s3 = F(ax + V0.x * dv[k], ay + V0.y * dv[k], az + V0.z * dv[k]);
                                ao[k] = s1 && s2 ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (s3 ? 1 : 0));
                                if (ao[k] != 3) flat = false;
                            }
                            if (flat) { mask[idx] = (byte)part; anyMerge = true; continue; }
                            var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) + (Vector3)N0 * 0.5f;
                            int i0 = v.Count;
                            for (int k = 0; k < 4; k++)
                            {
                                var pnt = center + ((Vector3)U0 * du[k] + (Vector3)V0 * dv[k]) * (0.5f + Inflate);
                                v.Add(origin + pnt * vox);
                                n.Add(N0);
                                byte g = (byte)(255 * AoK[ao[k]]);
                                col.Add(new Color32(g, g, g, 255));
                            }
                            var t = tris[part - 1];
                            if (ao[0] + ao[2] < ao[1] + ao[3]) { t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0 + 3); t.Add(i0 + 1); t.Add(i0 + 3); t.Add(i0); }
                            else { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
                        }
                if (!anyMerge) continue;

                // склейка ровных граней по слоям
                int a = Axis(N0), ua = Axis(U0), va = Axis(V0);
                var c = new int[3];
                int Idx(int sa, int su, int sv) { c[a] = sa; c[ua] = su; c[va] = sv; return (c[1] * nz + c[2]) * nx + c[0]; }
                for (int sa = 0; sa < dims[a]; sa++)
                    for (int sv = 0; sv < dims[va]; sv++)
                        for (int su = 0; su < dims[ua]; su++)
                        {
                            int part = mask[Idx(sa, su, sv)];
                            if (part == 0) continue;
                            int w = 1;
                            while (su + w < dims[ua] && mask[Idx(sa, su + w, sv)] == part) w++;
                            int h = 1;
                            while (sv + h < dims[va])
                            {
                                bool row = true;
                                for (int q = 0; q < w && row; q++) row = mask[Idx(sa, su + q, sv + h)] == part;
                                if (!row) break;
                                h++;
                            }
                            for (int r = 0; r < h; r++) for (int q = 0; q < w; q++) mask[Idx(sa, su + q, sv + r)] = 0;
                            // плоскость грани
                            float plane = sa + (N0.x + N0.y + N0.z > 0 ? 1f : 0f);
                            int i0 = v.Count;
                            for (int k = 0; k < 4; k++)
                            {
                                var p = new float[3];
                                p[a] = plane;
                                p[ua] = du[k] < 0 ? su - Inflate : su + w + Inflate;
                                p[va] = dv[k] < 0 ? sv - Inflate : sv + h + Inflate;
                                v.Add(origin + new Vector3(p[0], p[1], p[2]) * vox);
                                n.Add(N0);
                                col.Add(full);
                            }
                            var t = tris[part - 1];
                            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                        }
            }
            if (v.Count == 0) return null;
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "voxels" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(col);
            mesh.subMeshCount = parts;
            for (int i = 0; i < parts; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Отдельная сетка для одной фигуры, привязанная к ней (окна и двери, которые прячутся вместе со стеной).</summary>
        public static GameObject BakeSingle(Renderer r, Transform space, float vox)
        {
            return BakeRenderers(space, new List<Renderer> { r }, vox, r.transform);
        }

        /// <summary>Запечь все группы фигур в иерархии: каждая трансформа со своими прямыми фигурами — отдельная сетка (кости лисички двигаются независимо).</summary>
        public static void BakeTree(Transform root, float vox, System.Func<Renderer, bool> filter = null)
        {
            var spaces = new List<Transform>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (!spaces.Contains(r.transform.parent) && r.transform.parent != null) spaces.Add(r.transform.parent);
            foreach (var s in spaces) Bake(s, vox, filter);
        }
    }
}
