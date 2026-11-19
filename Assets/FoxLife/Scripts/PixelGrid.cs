using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Сетка маленьких вокселей с цветом в вершинах: экран телевизора, монитор, холст.
    /// Каждый «пиксель» — кубик; новые мазки на холсте выпрыгивают из него и застывают выпуклыми.
    /// </summary>
    public class PixelGrid : MonoBehaviour
    {
        int w, h;
        float pw, ph, baseD, relief, gap;
        Mesh mesh;
        Color32[] cols;
        float[] depth, popT, target;
        Vector3[] verts;
        Color32[] vcols;
        bool animating, dirty;
        static Shader shader;
        public Color32 backColor = new Color32(40, 40, 46, 255);

        public void Build(int w, int h, Vector2 size, float baseDepth, float gap, float glow, float relief)
        {
            this.w = w; this.h = h; this.baseD = baseDepth; this.relief = relief; this.gap = gap;
            pw = size.x / w; ph = size.y / h;
            int n = w * h;
            cols = new Color32[n];
            depth = new float[n]; popT = new float[n]; target = new float[n];
            for (int i = 0; i < n; i++) { depth[i] = target[i] = baseD; popT[i] = -1; cols[i] = new Color32(20, 22, 28, 255); }
            verts = new Vector3[n * 24];
            vcols = new Color32[n * 24];
            var tris = new int[n * 36];
            for (int i = 0; i < n; i++)
                for (int f = 0; f < 6; f++)
                {
                    int v = i * 24 + f * 4, t = i * 36 + f * 6;
                    tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                    tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                }
            mesh = new Mesh { name = "пиксели" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            Rebuild();
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            if (shader == null) shader = Shader.Find("FoxLife/VoxelPixels");
            var mat = new Material(shader);
            mat.SetFloat("_Glow", glow);
            mat.SetFloat("_DepthBias", 1.6f);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void SetColors(Color32[] c)
        {
            System.Array.Copy(c, cols, Mathf.Min(c.Length, cols.Length));
            dirty = true;
        }

        /// <summary>Мазок: пиксель вспучивается и застывает чуть выпуклым.</summary>
        public void Pop(int i, float height01)
        {
            if (i < 0 || i >= popT.Length) return;
            popT[i] = Time.time;
            target[i] = baseD + relief * height01;
            animating = true;
        }

        public void Flatten()
        {
            for (int i = 0; i < depth.Length; i++) { depth[i] = target[i] = baseD; popT[i] = -1; }
            dirty = true;
        }

        void Update()
        {
            if (animating)
            {
                animating = false;
                float now = Time.time;
                for (int i = 0; i < popT.Length; i++)
                {
                    if (popT[i] < 0) continue;
                    float k = (now - popT[i]) / 0.35f;
                    if (k >= 1) { depth[i] = target[i]; popT[i] = -1; }
                    else
                    {
                        // выпрыгивает выше и оседает
                        depth[i] = Mathf.Lerp(baseD, target[i], k) + Mathf.Sin(k * Mathf.PI) * relief * 1.6f;
                        animating = true;
                    }
                }
                dirty = true;
            }
            if (dirty) { dirty = false; Rebuild(); }
        }

        void Rebuild()
        {
            float g = gap;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float x0 = (x - w * 0.5f) * pw + g, x1 = x0 + pw - 2 * g;
                    float y0 = (y - h * 0.5f) * ph + g, y1 = y0 + ph - 2 * g;
                    float d = -depth[i];
                    int v = i * 24;
                    // перед (смотрит в -Z)
                    Q(v, new Vector3(x0, y0, d), new Vector3(x0, y1, d), new Vector3(x1, y1, d), new Vector3(x1, y0, d));
                    // бока
                    Q(v + 4, new Vector3(x1, y0, d), new Vector3(x1, y1, d), new Vector3(x1, y1, 0), new Vector3(x1, y0, 0));
                    Q(v + 8, new Vector3(x0, y0, 0), new Vector3(x0, y1, 0), new Vector3(x0, y1, d), new Vector3(x0, y0, d));
                    Q(v + 12, new Vector3(x0, y1, d), new Vector3(x0, y1, 0), new Vector3(x1, y1, 0), new Vector3(x1, y1, d));
                    Q(v + 16, new Vector3(x0, y0, 0), new Vector3(x0, y0, d), new Vector3(x1, y0, d), new Vector3(x1, y0, 0));
                    Q(v + 20, new Vector3(x1, y0, 0), new Vector3(x1, y1, 0), new Vector3(x0, y1, 0), new Vector3(x0, y0, 0));
                    var c = cols[i];
                    var side = new Color32((byte)(c.r * 0.78f), (byte)(c.g * 0.78f), (byte)(c.b * 0.78f), 255);
                    for (int k = 0; k < 4; k++) vcols[v + k] = c;
                    for (int k = 4; k < 20; k++) vcols[v + k] = side;
                    for (int k = 20; k < 24; k++) vcols[v + k] = backColor;
                }
            mesh.vertices = verts;
            mesh.colors32 = vcols;
            if (mesh.triangles.Length > 0) mesh.RecalculateNormals();
        }

        void Q(int v, Vector3 a, Vector3 b, Vector3 c, Vector3 d) { verts[v] = a; verts[v + 1] = b; verts[v + 2] = c; verts[v + 3] = d; }
    }
}
