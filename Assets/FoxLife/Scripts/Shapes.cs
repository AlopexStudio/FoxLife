using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Простые формы, из которых собраны мебель и лисички.</summary>
    public static class Shapes
    {
        static Mesh cone, pyramid;

        public static GameObject Part(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Styleable st, int part, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = t.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            // Совпадающие грани соседних деталей мерцают (z-fighting). Каждая следующая
            // коробка или цилиндр предмета чуть-чуть больше предыдущей — грани больше не совпадают.
            if ((st.kind == SurfaceKind.Object || st.kind == SurfaceKind.Fox) && (t == PrimitiveType.Cube || t == PrimitiveType.Cylinder))
            {
                float eps = 0.0009f * (++st.primCount);
                scale += new Vector3(eps, t == PrimitiveType.Cylinder ? eps * 0.5f : eps, eps);
            }
            go.transform.localScale = scale;
            st.Register(go.GetComponent<Renderer>(), part);
            return go;
        }

        public static GameObject Box(Styleable st, int part, Transform m, Vector3 pos, Vector3 size, Vector3 euler = default)
            => Part(PrimitiveType.Cube, m, pos, size, st, part, euler);

        public static GameObject Ball(Styleable st, int part, Transform m, Vector3 pos, Vector3 size, Vector3 euler = default)
            => Part(PrimitiveType.Sphere, m, pos, size, st, part, euler);

        /// <summary>Цилиндр: diameter по X/Z, height — полная высота.</summary>
        public static GameObject Cyl(Styleable st, int part, Transform m, Vector3 pos, float diameter, float height, Vector3 euler = default)
            => Part(PrimitiveType.Cylinder, m, pos, new Vector3(diameter, height * 0.5f, diameter), st, part, euler);

        public static GameObject MeshPart(Mesh mesh, Styleable st, int part, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler = default, bool collider = false)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            st.Register(r, part);
            return go;
        }

        /// <summary>Конус вдоль +Y: основание диаметром 1 на y=-0.5, вершина на y=+0.5.</summary>
        public static Mesh Cone()
        {
            if (cone != null) return cone;
            const int seg = 18;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float slope = 0.5f; // radius / height
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f);
                Vector3 tip = new Vector3(0, 0.5f, 0);
                Vector3 n0 = new Vector3(Mathf.Cos(a0), slope, Mathf.Sin(a0)).normalized;
                Vector3 n1 = new Vector3(Mathf.Cos(a1), slope, Mathf.Sin(a1)).normalized;
                Vector3 nm = new Vector3(Mathf.Cos((a0 + a1) / 2), slope, Mathf.Sin((a0 + a1) / 2)).normalized;
                int b = v.Count;
                v.Add(p0); v.Add(tip); v.Add(p1);
                n.Add(n0); n.Add(nm); n.Add(n1);
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
                b = v.Count;
                v.Add(p0); v.Add(p1); v.Add(new Vector3(0, -0.5f, 0));
                n.Add(Vector3.down); n.Add(Vector3.down); n.Add(Vector3.down);
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
            }
            cone = new Mesh { name = "Cone" };
            cone.SetVertices(v);
            cone.SetNormals(n);
            cone.SetTriangles(t, 0);
            cone.RecalculateBounds();
            return cone;
        }

        /// <summary>Четырёхгранная пирамида: квадратное основание 1x1 на y=-0.5, вершина на y=+0.5.</summary>
        public static Mesh Pyramid()
        {
            if (pyramid != null) return pyramid;
            Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f), b = new Vector3(0.5f, -0.5f, -0.5f);
            Vector3 c = new Vector3(0.5f, -0.5f, 0.5f), d = new Vector3(-0.5f, -0.5f, 0.5f);
            Vector3 top = new Vector3(0, 0.5f, 0);
            var v = new List<Vector3>();
            var t = new List<int>();
            void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
            {
                int i = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            Tri(a, top, b); Tri(b, top, c); Tri(c, top, d); Tri(d, top, a);
            Tri(a, b, c); Tri(a, c, d);
            pyramid = new Mesh { name = "Pyramid" };
            pyramid.SetVertices(v);
            pyramid.SetTriangles(t, 0);
            pyramid.RecalculateNormals();
            pyramid.RecalculateBounds();
            return pyramid;
        }
    }
}
