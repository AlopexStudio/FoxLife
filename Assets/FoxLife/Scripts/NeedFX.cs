using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Нужды видны без интерфейса: волны запаха при низкой гигиене, тучка при скуке, окорочок при голоде,
    /// «Zz» при усталости, многоточие при одиночестве. Маленькие воксельные значки над головой.
    /// </summary>
    public class NeedFX : MonoBehaviour
    {
        public Fox fox;
        readonly GameObject[] icons = new GameObject[5];
        readonly List<Transform> stink = new List<Transform>();
        float t;

        static readonly Vector3 V0 = Vector3.zero;

        void Start()
        {
            icons[0] = Icon("Голод", "#C98B5A", (st, tr) =>
            {
                Shapes.Ball(st, 0, tr, new Vector3(0, 0, 0), new Vector3(0.16f, 0.12f, 0.12f));
                Shapes.Box(st, 1, tr, new Vector3(0.1f, 0.06f, 0), new Vector3(0.1f, 0.03f, 0.03f), new Vector3(0, 0, 35));
            }, "#FFF8EE");
            icons[1] = Icon("Усталость", "#8FA8D8", (st, tr) =>
            {
                Shapes.Box(st, 0, tr, new Vector3(-0.04f, 0.06f, 0), new Vector3(0.1f, 0.025f, 0.02f));
                Shapes.Box(st, 0, tr, new Vector3(-0.04f, 0f, 0), new Vector3(0.1f, 0.025f, 0.02f), new Vector3(0, 0, 50));
                Shapes.Box(st, 0, tr, new Vector3(-0.04f, -0.06f, 0), new Vector3(0.1f, 0.025f, 0.02f));
                Shapes.Box(st, 0, tr, new Vector3(0.08f, 0.1f, 0), new Vector3(0.06f, 0.018f, 0.02f));
                Shapes.Box(st, 0, tr, new Vector3(0.08f, 0.07f, 0), new Vector3(0.06f, 0.018f, 0.02f), new Vector3(0, 0, 50));
                Shapes.Box(st, 0, tr, new Vector3(0.08f, 0.04f, 0), new Vector3(0.06f, 0.018f, 0.02f));
            });
            // запах — три волнистые струйки, поднимаются и тают
            var sm = new GameObject("Запах").transform;
            sm.SetParent(transform, false);
            for (int i = 0; i < 3; i++)
            {
                var s = Icon("струйка", "#9CC46A", (st, tr) =>
                {
                    for (int k = 0; k < 4; k++) Shapes.Box(st, 0, tr, new Vector3(Mathf.Sin(k * 1.4f) * 0.025f, k * 0.04f, 0), new Vector3(0.025f, 0.04f, 0.025f));
                }, null, sm);
                s.transform.localPosition = new Vector3((i - 1) * 0.12f, 0, 0);
                stink.Add(s.transform);
            }
            icons[2] = sm.gameObject;
            icons[3] = Icon("Скука", "#9AA3AE", (st, tr) =>
            {
                Shapes.Ball(st, 0, tr, new Vector3(-0.05f, 0, 0), new Vector3(0.12f, 0.08f, 0.08f));
                Shapes.Ball(st, 0, tr, new Vector3(0.04f, 0.02f, 0), new Vector3(0.13f, 0.1f, 0.09f));
                Shapes.Box(st, 1, tr, new Vector3(-0.03f, -0.07f, 0), new Vector3(0.015f, 0.04f, 0.015f));
                Shapes.Box(st, 1, tr, new Vector3(0.03f, -0.08f, 0), new Vector3(0.015f, 0.04f, 0.015f));
            }, "#86BCE6");
            icons[4] = Icon("Одиночество", "#FFFFFF", (st, tr) =>
            {
                Shapes.Box(st, 0, tr, V0, new Vector3(0.22f, 0.12f, 0.03f));
                for (int k = -1; k <= 1; k++) Shapes.Box(st, 1, tr, new Vector3(k * 0.06f, 0, -0.02f), new Vector3(0.03f, 0.03f, 0.02f));
            }, "#5B7FA8");
            foreach (var ic in icons) ic.SetActive(false);
        }

        GameObject Icon(string name, string color, System.Action<Styleable, Transform> build, string color2 = null, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            var st = go.AddComponent<Styleable>();
            st.kind = SurfaceKind.Outdoor; st.hidden = true; st.displayName = null;
            st.AddPart("a", StyleData.Make("Гладкий", color, null, 1, 0.4f));
            st.AddPart("b", StyleData.Make("Гладкий", color2 ?? color, null, 1, 0.4f));
            build(st, go.transform);
            Voxelizer.Bake(go.transform, 0.025f);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void Update()
        {
            if (fox == null || icons[0] == null) return;
            t += Time.deltaTime;
            var n = fox.needs;
            bool visible = fox.gameObject.activeInHierarchy && !fox.atWork;
            float[] urg = new float[5];
            for (int i = 0; i < 5; i++) urg[i] = Mathf.Clamp01((30f - n[i]) / 30f); // 30% → 0, 0% → 1
            if (fox.sleeping) urg[1] = 0;
            int shown = 0;
            var up = fox.transform.position + Vector3.up * (1.05f * Mathf.Max(0.7f, fox.size));
            var camYaw = Quaternion.Euler(0, Camera.main != null ? Camera.main.transform.eulerAngles.y : 0, 0);
            for (int i = 0; i < icons.Length; i++)
            {
                bool v = urg[i] > 0.001f && visible;
                if (icons[i].activeSelf != v) icons[i].SetActive(v);
                if (!v || i == 2) continue;
                // чем сильнее нужда, тем крупнее значок и живее подпрыгивает; при нуле — пульсирует
                float k = urg[i];
                float pulse = k > 0.95f ? 1f + Mathf.Abs(Mathf.Sin(t * 6f)) * 0.25f : 1f;
                float sc = Mathf.Lerp(0.55f, 1.15f, k) * pulse;
                var tr = icons[i].transform;
                tr.localScale = Vector3.one * sc;
                tr.position = up + camYaw * new Vector3((shown - 0.5f) * 0.3f, Mathf.Sin(t * (2f + k * 4f) + i) * (0.03f + k * 0.04f), 0);
                tr.rotation = camYaw;
                shown++;
            }
            if (icons[2].activeSelf)
            {
                icons[2].transform.position = fox.transform.position + Vector3.up * 0.55f;
                icons[2].transform.rotation = Quaternion.Euler(0, Camera.main != null ? Camera.main.transform.eulerAngles.y : 0, 0);
                icons[2].transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.2f, urg[2]);
                for (int i = 0; i < stink.Count; i++)
                {
                    float k = Mathf.Repeat(t * 0.6f + i * 0.33f, 1f);
                    stink[i].localPosition = new Vector3((i - 1) * 0.14f + Mathf.Sin(t * 3 + i) * 0.03f, k * 0.45f, 0);
                    stink[i].localScale = Vector3.one * Mathf.Sin(k * Mathf.PI);
                }
            }
        }
    }
}
