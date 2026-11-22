using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Городские украшения и достопримечательности: часовая башня, мельница, мостики, фонари, колодец,
    /// статуя, цветочные арки, живые изгороди, флажки, рыночный прилавок, маркизы, зонтики, кусты, кувшинки.
    /// Всё собрано из простых форм и запечено в вокселы; любую часть можно перекрасить «Созданием стиля».
    /// </summary>
    public static class Props
    {
        /// <summary>Стёкла фонарей: вечером светятся.</summary>
        public static readonly List<Material> Lanterns = new List<Material>();
        /// <summary>Свет уличных фонарей (включается вечером).</summary>
        public static readonly List<Light> LampLights = new List<Light>();

        /// <summary>В URP свет фонарей гаснет с расстоянием быстрее (физично), поэтому огни ярче, чем были в BIRP.</summary>
        public const float LightK = 2.6f;

        static void AddLight(Transform t, Vector3 local, float range, float intensity)
        {
            var lg = new GameObject("Light");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = local;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.range = range; l.intensity = intensity * LightK;
            l.color = new Color(1f, 0.82f, 0.55f);
            l.renderMode = LightRenderMode.Auto;
            l.enabled = false;
            LampLights.Add(l);
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        public static Styleable New(Transform root, string name, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var st = go.AddComponent<Styleable>();
            st.displayName = name;
            st.kind = SurfaceKind.Outdoor;
            return st;
        }

        static GameObject Bake(Styleable st, float vox) => Voxelizer.Bake(st.transform, vox);

        static void Lantern(Styleable st, int part) { if (!Lanterns.Contains(st.parts[part].mat)) Lanterns.Add(st.parts[part].mat); }

        // ---------------- часовая башня ----------------

        public static Styleable ClockTower(Transform root, Vector3 pos, float yaw)
        {
            var st = New(root, "Часовая башня", pos, yaw);
            st.AddPart("Камень", StyleData.Make("Камень", "#D9CFC0", "#B9AE9E", 1.2f));
            st.AddPart("Стены", StyleData.Make("Фахверк", "#FBF1DE", "#8A5A3C"));
            st.AddPart("Крыша", StyleData.Make("Чешуя", "#5F9EA0", "#3F7678", 2f));
            st.AddPart("Циферблат", StyleData.Make("Гладкий", "#FFF9EC", null, 1, 0.5f));
            st.AddPart("Отделка", StyleData.Make("Гладкий", "#C9A24A", null, 1, 0.7f));
            st.AddPart("Дверь", StyleData.Make("Доски", "#8B5A35"));
            var t = st.transform;
            Shapes.Box(st, 0, t, V(0, 1.5f, 0), V(3f, 3f, 3f));
            Shapes.Box(st, 0, t, V(0, 0.15f, 0), V(3.3f, 0.3f, 3.3f));
            Shapes.Box(st, 0, t, V(0, 3.05f, 0), V(3.25f, 0.2f, 3.25f));
            Shapes.Box(st, 1, t, V(0, 4.6f, 0), V(2.6f, 3f, 2.6f));
            Shapes.Box(st, 4, t, V(0, 6.15f, 0), V(2.9f, 0.18f, 2.9f));
            Shapes.Box(st, 1, t, V(0, 7.1f, 0), V(2.3f, 1.8f, 2.3f));
            Shapes.Box(st, 4, t, V(0, 8.05f, 0), V(2.7f, 0.15f, 2.7f));
            Shapes.MeshPart(Shapes.Pyramid(), st, 2, t, V(0, 9.6f, 0), V(3.1f, 3.1f, 3.1f), default, true);
            Shapes.Cyl(st, 4, t, V(0, 11.5f, 0), 0.1f, 1.2f);
            Shapes.Ball(st, 4, t, V(0, 11.3f, 0), V(0.28f, 0.28f, 0.28f));
            // дверь и арочные окошки
            Shapes.Box(st, 5, t, V(0, 0.9f, -1.52f), V(0.9f, 1.6f, 0.08f));
            Shapes.Ball(st, 5, t, V(0, 1.7f, -1.52f), V(0.9f, 0.6f, 0.08f));
            foreach (var s in new[] { -1f, 1f })
            {
                Shapes.Box(st, 3, t, V(s * 0.6f, 4.9f, -1.32f), V(0.35f, 0.7f, 0.06f));
                Shapes.Box(st, 3, t, V(s * 1.32f, 4.9f, 0), V(0.06f, 0.7f, 0.35f));
            }
            Bake(st, 0.125f);

            // четыре циферблата со стрелками; стрелки — отдельно, их крутит время игры
            var clock = st.gameObject.AddComponent<ClockHands>();
            for (int i = 0; i < 4; i++)
            {
                var face = new GameObject("face").transform;
                face.SetParent(t, false);
                face.localRotation = Quaternion.Euler(0, i * 90f, 0);
                face.localPosition = face.localRotation * V(0, 7.15f, -1.18f);
                var fs = face.gameObject.AddComponent<Styleable>();
                fs.kind = SurfaceKind.Outdoor; fs.displayName = "Часы";
                fs.hidden = true;
                Shapes.Cyl(st, 3, face, V(0, 0, 0), 1.3f, 0.06f, V(90, 0, 0));
                Shapes.Cyl(st, 4, face, V(0, 0, 0.01f), 1.45f, 0.04f, V(90, 0, 0));
                for (int k = 0; k < 12; k++)
                {
                    var a = k * 30f * Mathf.Deg2Rad;
                    Shapes.Box(st, 4, face, V(Mathf.Sin(a) * 0.5f, Mathf.Cos(a) * 0.5f, -0.05f), V(0.06f, k % 3 == 0 ? 0.16f : 0.08f, 0.03f), V(0, 0, -k * 30f));
                }
                Voxelizer.Bake(face, 0.0625f);
                var hour = new GameObject("hour").transform; hour.SetParent(face, false); hour.localPosition = V(0, 0, -0.07f);
                var minute = new GameObject("minute").transform; minute.SetParent(face, false); minute.localPosition = V(0, 0, -0.09f);
                Shapes.Box(st, 4, hour, V(0, 0.17f, 0), V(0.08f, 0.36f, 0.03f));
                Shapes.Box(st, 4, minute, V(0, 0.25f, 0), V(0.05f, 0.52f, 0.03f));
                clock.hours.Add(hour); clock.minutes.Add(minute);
            }
            return st;
        }

        // ---------------- мельница ----------------

        public static Styleable Windmill(Transform root, Vector3 pos, float yaw)
        {
            var st = New(root, "Мельница", pos, yaw);
            st.AddPart("Камень", StyleData.Make("Камень", "#E6DCCB", "#C7BBA6", 1.2f));
            st.AddPart("Крыша", StyleData.Make("Чешуя", "#C9655A", "#A84E45", 2f));
            st.AddPart("Дерево", StyleData.Make("Доски", "#A87B52"));
            st.AddPart("Крылья", StyleData.Make("Клетка", "#FFF6E6", "#E6D3B5", 2f));
            st.AddPart("Окна", StyleData.Make("Гладкий", "#8FC3E6", null, 1, 0.8f));
            var t = st.transform;
            for (int i = 0; i < 6; i++)
                Shapes.Cyl(st, 0, t, V(0, 0.55f + i * 1.0f, 0), 4.2f - i * 0.35f, 1.05f);
            Shapes.Cyl(st, 2, t, V(0, 6.2f, 0), 2.5f, 0.25f);
            Shapes.MeshPart(Shapes.Cone(), st, 1, t, V(0, 7.6f, 0), V(3f, 2.6f, 3f), default, true);
            Shapes.Box(st, 2, t, V(0, 0.9f, -2.0f), V(1f, 1.8f, 0.3f));
            Shapes.Box(st, 4, t, V(0, 3.4f, -1.75f), V(0.6f, 0.7f, 0.2f));
            Shapes.Box(st, 4, t, V(-1.6f, 2.4f, -0.6f), V(0.2f, 0.6f, 0.5f), V(0, -20, 0));
            Shapes.Box(st, 2, t, V(0, 6.5f, -1.45f), V(0.35f, 0.35f, 0.9f));
            Bake(st, 0.125f);

            var hub = new GameObject("sails").transform;
            hub.SetParent(t, false);
            hub.localPosition = V(0, 6.5f, -2.0f);
            var hs = hub.gameObject.AddComponent<Styleable>();
            hs.kind = SurfaceKind.Outdoor; hs.hidden = true;
            Shapes.Cyl(st, 2, hub, V(0, 0, 0), 0.5f, 0.3f, V(90, 0, 0));
            for (int i = 0; i < 4; i++)
            {
                var arm = new GameObject("arm").transform;
                arm.SetParent(hub, false);
                arm.localRotation = Quaternion.Euler(0, 0, i * 90f + 15f);
                Shapes.Box(st, 2, arm, V(0, 2.4f, 0), V(0.16f, 4.6f, 0.12f));
                Shapes.Box(st, 3, arm, V(0.42f, 2.8f, 0.04f), V(0.7f, 3.4f, 0.05f));
                for (int k = 0; k < 6; k++) Shapes.Box(st, 2, arm, V(0.42f, 1.2f + k * 0.62f, -0.02f), V(0.78f, 0.05f, 0.05f));
            }
            Voxelizer.Bake(hub, 0.125f, null, false);
            hub.gameObject.AddComponent<Spinner>().speed = 25f;
            return st;
        }

        // ---------------- мост ----------------

        /// <summary>Горбатый каменный мостик. Ось — вдоль +Z (yaw поворачивает), настил на уровне земли.</summary>
        public static Styleable Bridge(Transform root, Vector3 pos, float yaw, float length, float width, bool wooden)
        {
            var st = New(root, wooden ? "Деревянный мостик" : "Каменный мост", pos, yaw);
            var mark = st.gameObject.AddComponent<BridgeMark>();
            mark.length = length; mark.width = width;
            st.AddPart("Кладка", wooden ? StyleData.Make("Доски", "#B98A5A") : StyleData.Make("Камень", "#D8CDBB", "#B5A992", 1.3f));
            st.AddPart("Настил", wooden ? StyleData.Make("Доски", "#C99A66") : StyleData.Make("Плитка", "#CFC6B8", "#B8AE9F", 1.5f));
            st.AddPart("Перила", wooden ? StyleData.Make("Дерево", "#8B5A35") : StyleData.Make("Камень", "#E3DACB", "#C6BCAB", 1.2f));
            st.AddPart("Фонарь", StyleData.Make("Гладкий", "#3E4A45", null, 1, 0.6f));
            st.AddPart("Стекло", StyleData.Make("Гладкий", "#FFE7A8", null, 1, 0.9f));
            var t = st.transform;
            float hl = length / 2f, hw = width / 2f;
            Shapes.Box(st, 1, t, V(0, 0.04f, 0), V(width, 0.14f, length));
            // арка под настилом
            int n = 9;
            for (int i = 0; i < n; i++)
            {
                float z = -hl + (i + 0.5f) * length / n;
                float k = 1f - Mathf.Pow(z / hl, 2f);
                float depth = 0.25f + (1f - k) * 0.7f;
                Shapes.Box(st, 0, t, V(0, -0.08f - depth / 2f, z), V(width - 0.1f, depth, length / n + 0.01f));
            }
            // перила с тумбами
            foreach (var s in new[] { -1f, 1f })
            {
                float x = s * (hw - 0.1f);
                if (wooden)
                {
                    Shapes.Box(st, 2, t, V(x, 0.75f, 0), V(0.1f, 0.08f, length));
                    for (float z = -hl; z <= hl + 0.01f; z += length / 4f) Shapes.Box(st, 2, t, V(x, 0.4f, z), V(0.12f, 0.8f, 0.12f));
                }
                else
                {
                    Shapes.Box(st, 2, t, V(x, 0.3f, 0), V(0.22f, 0.6f, length));
                    Shapes.Box(st, 2, t, V(x, 0.65f, 0), V(0.3f, 0.1f, length + 0.1f));
                    foreach (var z in new[] { -hl + 0.2f, hl - 0.2f })
                    {
                        Shapes.Box(st, 2, t, V(x, 0.45f, z), V(0.4f, 0.9f, 0.4f));
                        Shapes.MeshPart(Shapes.Pyramid(), st, 2, t, V(x, 1.0f, z), V(0.42f, 0.2f, 0.42f), default, true);
                    }
                }
            }
            if (!wooden)
            {
                // фонари на въездах
                foreach (var s in new[] { -1f, 1f })
                {
                    var p = V(s * (hw - 0.1f), 0.9f, -hl + 0.2f);
                    Shapes.Box(st, 3, t, p + V(0, 0.45f, 0), V(0.12f, 0.9f, 0.12f));
                    Shapes.Box(st, 4, t, p + V(0, 1.05f, 0), V(0.22f, 0.3f, 0.22f));
                    Shapes.MeshPart(Shapes.Pyramid(), st, 3, t, p + V(0, 1.28f, 0), V(0.32f, 0.16f, 0.32f), default, true);
                }
                Lantern(st, 4);
            }
            Bake(st, 0.0625f);
            return st;
        }

        // ---------------- фонари ----------------

        public static Styleable StreetLamp(Transform root, Vector3 pos, float yaw)
        {
            var st = New(root, "Фонарь", pos, yaw);
            st.AddPart("Столб", StyleData.Make("Гладкий", "#3E4A45", null, 1, 0.6f));
            st.AddPart("Стекло", StyleData.Make("Гладкий", "#FFE7A8", null, 1, 0.9f));
            st.AddPart("Цветы", StyleData.Make("Горошек", "#F5A3C1", "#FFF2B3", 2f));
            var t = st.transform;
            Shapes.Cyl(st, 0, t, V(0, 0.1f, 0), 0.36f, 0.2f);
            Shapes.Cyl(st, 0, t, V(0, 1.4f, 0), 0.12f, 2.6f);
            Shapes.Box(st, 0, t, V(0, 2.55f, 0.25f), V(0.06f, 0.06f, 0.55f));
            Shapes.Box(st, 0, t, V(0, 2.42f, 0.5f), V(0.05f, 0.25f, 0.05f));
            Shapes.Box(st, 1, t, V(0, 2.15f, 0.5f), V(0.26f, 0.34f, 0.26f));
            Shapes.MeshPart(Shapes.Pyramid(), st, 0, t, V(0, 2.4f, 0.5f), V(0.36f, 0.18f, 0.36f), default, true);
            Bake(st, 0.0625f);
            Lantern(st, 1);
            AddLight(st.transform, V(0, 2.05f, 0.5f), 8f, 1.3f);
            return st;
        }

        // ---------------- колодец, статуя ----------------

        public static Styleable Well(Transform root, Vector3 pos, float yaw)
        {
            var st = New(root, "Колодец", pos, yaw);
            st.AddPart("Камень", StyleData.Make("Камень", "#D5CBBB", "#B3A794", 1.5f));
            st.AddPart("Дерево", StyleData.Make("Дерево", "#8B5A35"));
            st.AddPart("Крыша", StyleData.Make("Чешуя", "#C9655A", "#A84E45", 3f));
            st.AddPart("Вода", StyleData.Make("Гладкий", "#86BCE6", null, 1, 0.9f));
            var t = st.transform;
            Shapes.Cyl(st, 0, t, V(0, 0.35f, 0), 1.1f, 0.7f);
            Shapes.Cyl(st, 3, t, V(0, 0.62f, 0), 0.8f, 0.05f);
            foreach (var s in new[] { -1f, 1f }) Shapes.Box(st, 1, t, V(s * 0.48f, 1.1f, 0), V(0.1f, 1.5f, 0.1f));
            Shapes.Cyl(st, 1, t, V(0, 1.45f, 0), 0.12f, 1f, V(0, 0, 90));
            Shapes.MeshPart(Shapes.Pyramid(), st, 2, t, V(0, 2.05f, 0), V(1.35f, 0.6f, 1.0f), default, true);
            Shapes.Cyl(st, 1, t, V(0.1f, 1.05f, 0), 0.22f, 0.22f);
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable FoxStatue(Transform root, Vector3 pos, float yaw, bool reading = false)
        {
            var st = New(root, reading ? "Статуя лиса с книгой" : "Статуя лисы", pos, yaw);
            st.AddPart("Постамент", StyleData.Make("Мрамор", "#EDE7DD", "#C9C0B2"));
            st.AddPart("Бронза", StyleData.Make("Гладкий", "#B98B4E", null, 1, 0.75f));
            st.AddPart("Цветы", StyleData.Make("Горошек", "#F5A3C1", "#FFF2B3", 2.5f));
            var t = st.transform;
            Shapes.Box(st, 0, t, V(0, 0.45f, 0), V(0.9f, 0.9f, 0.9f));
            Shapes.Box(st, 0, t, V(0, 0.95f, 0), V(1.05f, 0.1f, 1.05f));
            Shapes.Box(st, 0, t, V(0, 0.06f, 0), V(1.1f, 0.12f, 1.1f));
            // сидящая лиса
            float y = 1f;
            Shapes.Box(st, 1, t, V(0, y + 0.32f, 0.05f), V(0.42f, 0.6f, 0.42f), V(-12, 0, 0));
            Shapes.Box(st, 1, t, V(0, y + 0.78f, -0.1f), V(0.38f, 0.32f, 0.34f));
            Shapes.Box(st, 1, t, V(0, y + 0.72f, -0.32f), V(0.18f, 0.14f, 0.16f));
            foreach (var s in new[] { -1f, 1f })
            {
                Shapes.MeshPart(Shapes.Pyramid(), st, 1, t, V(s * 0.12f, y + 1.04f, -0.08f), V(0.14f, 0.22f, 0.1f), default, true);
                Shapes.Box(st, 1, t, V(s * 0.12f, y + 0.12f, -0.18f), V(0.1f, 0.24f, 0.12f));
            }
            Shapes.Box(st, 1, t, V(0.25f, y + 0.12f, 0.15f), V(0.24f, 0.2f, 0.62f), V(0, -25, 0));
            Shapes.Box(st, 1, t, V(0.36f, y + 0.2f, -0.18f), V(0.2f, 0.22f, 0.24f), V(0, -25, 0));
            if (reading) Shapes.Box(st, 1, t, V(0, y + 0.5f, -0.3f), V(0.3f, 0.04f, 0.22f), V(-50, 0, 0));
            foreach (var s in new[] { -1f, 1f }) Shapes.Ball(st, 2, t, V(s * 0.75f, 0.2f, -0.2f), V(0.5f, 0.35f, 0.5f));
            Bake(st, 0.0625f);
            return st;
        }

        // ---------------- изгороди, арки, кусты ----------------

        /// <summary>Живая изгородь вдоль оси X (длина в метрах).</summary>
        public static Styleable Hedge(Transform root, Vector3 pos, float yaw, float length, int seed)
        {
            var st = New(root, "Живая изгородь", pos, yaw);
            st.AddPart("Листва", StyleData.Make("Ворс", "#6FA85A"));
            st.AddPart("Цветы", StyleData.Make("Горошек", "#FFFFFF", "#F5A3C1", 3f));
            var t = st.transform;
            Shapes.Box(st, 0, t, V(0, 0.35f, 0), V(length, 0.7f, 0.6f));
            int n = Mathf.Max(1, Mathf.RoundToInt(length / 0.8f));
            for (int i = 0; i < n; i++)
            {
                float x = -length / 2f + (i + 0.5f) * length / n;
                Shapes.Ball(st, 0, t, V(x, 0.72f, 0), V(0.8f, 0.4f, 0.62f));
                if (VoxelTerrain.Hash(seed, i, 3) > 0.55f) Shapes.Ball(st, 1, t, V(x + 0.15f, 0.82f, -0.2f), V(0.22f, 0.16f, 0.22f));
            }
            Bake(st, 0.125f);
            return st;
        }

        public static Styleable FlowerArch(Transform root, Vector3 pos, float yaw)
        {
            var st = New(root, "Цветочная арка", pos, yaw);
            st.AddPart("Решётка", StyleData.Make("Гладкий", "#FFF8EE", null, 1, 0.3f));
            st.AddPart("Листва", StyleData.Make("Ворс", "#6FA85A"));
            st.AddPart("Цветы", StyleData.Make("Горошек", "#F5A3C1", "#FFFFFF", 2.5f));
            var t = st.transform;
            foreach (var s in new[] { -1f, 1f })
            {
                Shapes.Box(st, 0, t, V(s * 0.75f, 1f, 0), V(0.1f, 2f, 0.3f));
                Shapes.Box(st, 1, t, V(s * 0.75f, 0.9f, 0), V(0.22f, 1.6f, 0.26f));
            }
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.PI * i / 8f;
                var p = V(-Mathf.Cos(a) * 0.75f, 2f + Mathf.Sin(a) * 0.45f, 0);
                Shapes.Ball(st, 1, t, p, V(0.34f, 0.3f, 0.32f));
                if (i % 2 == 0) Shapes.Ball(st, 2, t, p + V(0, 0.05f, -0.12f), V(0.2f, 0.18f, 0.16f));
            }
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable Bush(Transform root, Vector3 pos, int seed)
        {
            var st = New(root, "Куст", pos, VoxelTerrain.Hash(seed, 1, 1) * 360f);
            st.AddPart("Листва", StyleData.Make("Ворс", VoxelTerrain.Hash(seed, 2, 2) > 0.5f ? "#6FA85A" : "#5E9A48"));
            st.AddPart("Ягоды", StyleData.Make("Гладкий", VoxelTerrain.Hash(seed, 3, 3) > 0.5f ? "#E0525E" : "#F2F0F7"));
            var t = st.transform;
            float s = 0.7f + VoxelTerrain.Hash(seed, 4, 4) * 0.6f;
            Shapes.Ball(st, 0, t, V(0, 0.35f * s, 0), V(1.1f, 0.8f, 1f) * s);
            Shapes.Ball(st, 0, t, V(0.4f * s, 0.28f * s, 0.2f * s), V(0.7f, 0.6f, 0.7f) * s);
            for (int i = 0; i < 4; i++)
                if (VoxelTerrain.Hash(seed, i, 7) > 0.4f)
                    Shapes.Ball(st, 1, t, V((VoxelTerrain.Hash(seed, i, 8) - 0.5f) * s, 0.55f * s, (VoxelTerrain.Hash(seed, i, 9) - 0.5f) * s), V(0.12f, 0.12f, 0.12f));
            Bake(st, 0.125f);
            return st;
        }

        // ---------------- флажки, прилавок, маркизы, зонтики, вывески ----------------

        /// <summary>Гирлянда разноцветных флажков между двумя точками (провисает посередине).</summary>
        public static Styleable Bunting(Transform root, Vector3 a, Vector3 b)
        {
            var mid = (a + b) / 2f;
            var dir = b - a; dir.y = 0;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var st = New(root, "Флажки", mid, yaw);
            st.AddPart("Верёвка", StyleData.Make("Гладкий", "#F2E6D0"));
            st.AddPart("Флажки 1", StyleData.Make("Гладкий", "#F28C8C"));
            st.AddPart("Флажки 2", StyleData.Make("Гладкий", "#F7D774"));
            st.AddPart("Флажки 3", StyleData.Make("Гладкий", "#8CC7E8"));
            st.AddPart("Флажки 4", StyleData.Make("Гладкий", "#A8D98C"));
            var t = st.transform;
            float len = (b - a).magnitude;
            int n = Mathf.Max(4, Mathf.RoundToInt(len / 0.6f));
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n;
                float z = -len / 2f + k * len;
                float y = Mathf.Lerp(a.y, b.y, k) - mid.y - Mathf.Sin(k * Mathf.PI) * 0.8f;
                if (i < n) Shapes.Box(st, 0, t, V(0, y, z + len / n / 2f), V(0.03f, 0.03f, len / n + 0.02f));
                if (i > 0 && i < n)
                    Shapes.Box(st, 1 + i % 4, t, V(0, y - 0.16f, z), V(0.04f, 0.22f, 0.22f), V(45, 0, 0));
            }
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable Pole(Transform root, Vector3 pos, float h)
        {
            var st = New(root, "Столбик", pos, 0);
            st.AddPart("Дерево", StyleData.Make("Гладкий", "#FFF8EE", null, 1, 0.3f));
            st.AddPart("Шар", StyleData.Make("Гладкий", "#C9A24A", null, 1, 0.7f));
            Shapes.Cyl(st, 0, st.transform, V(0, h / 2f, 0), 0.14f, h);
            Shapes.Ball(st, 1, st.transform, V(0, h + 0.08f, 0), V(0.22f, 0.22f, 0.22f));
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable MarketStall(Transform root, Vector3 pos, float yaw, int seed)
        {
            var st = New(root, "Рыночный прилавок", pos, yaw);
            st.AddPart("Дерево", StyleData.Make("Доски", "#B98A5A"));
            st.AddPart("Навес", StyleData.Make("Полоска", "#FFFFFF", seed % 2 == 0 ? "#E57373" : "#64B5F6", 2f));
            st.AddPart("Ящики", StyleData.Make("Доски", "#D9B48A"));
            st.AddPart("Фрукты 1", StyleData.Make("Гладкий", "#E0525E", null, 1, 0.5f));
            st.AddPart("Фрукты 2", StyleData.Make("Гладкий", "#F2B33D", null, 1, 0.5f));
            st.AddPart("Фрукты 3", StyleData.Make("Гладкий", "#7CB342", null, 1, 0.5f));
            var t = st.transform;
            Shapes.Box(st, 0, t, V(0, 0.45f, 0), V(1.8f, 0.9f, 0.8f));
            Shapes.Box(st, 0, t, V(0, 0.92f, -0.05f), V(1.9f, 0.06f, 0.95f));
            foreach (var x in new[] { -0.9f, 0.9f })
            {
                Shapes.Box(st, 0, t, V(x, 1.1f, -0.42f), V(0.08f, 2.2f, 0.08f));
                Shapes.Box(st, 0, t, V(x, 1.3f, 0.42f), V(0.08f, 2.6f, 0.08f));
            }
            Shapes.Box(st, 1, t, V(0, 2.4f, -0.05f), V(2.2f, 0.06f, 1.35f), V(-14, 0, 0));
            for (int i = 0; i < 6; i++)
                Shapes.Box(st, 1, t, V(-0.92f + i * 0.37f, 2.18f, -0.72f), V(0.37f, 0.22f, 0.04f));
            for (int i = 0; i < 3; i++)
            {
                Shapes.Box(st, 2, t, V(-0.6f + i * 0.6f, 1.05f, -0.15f), V(0.5f, 0.2f, 0.4f), V(-10, 0, 0));
                for (int k = 0; k < 4; k++)
                    Shapes.Ball(st, 3 + i, t, V(-0.72f + i * 0.6f + (k % 2) * 0.22f, 1.2f, -0.25f + (k / 2) * 0.18f), V(0.15f, 0.15f, 0.15f));
            }
            Bake(st, 0.0625f);
            return st;
        }

        /// <summary>Полосатая маркиза над окнами (вдоль локальной оси X, свисает к -Z).</summary>
        public static Styleable Awning(Transform root, Vector3 pos, float yaw, float width, string color)
        {
            var st = New(root, "Маркиза", pos, yaw);
            st.AddPart("Ткань", StyleData.Make("Полоска", "#FFFFFF", color, 2f));
            st.AddPart("Кронштейны", StyleData.Make("Гладкий", "#3E4A45", null, 1, 0.6f));
            var t = st.transform;
            Shapes.Box(st, 0, t, V(0, 0, -0.4f), V(width, 0.05f, 0.9f), V(-22, 0, 0));
            int n = Mathf.Max(2, Mathf.RoundToInt(width / 0.4f));
            for (int i = 0; i < n; i++)
                Shapes.Ball(st, 0, t, V(-width / 2f + (i + 0.5f) * width / n, -0.2f, -0.82f), V(width / n, 0.22f, 0.06f));
            foreach (var s in new[] { -1f, 1f }) Shapes.Box(st, 1, t, V(s * (width / 2f - 0.05f), -0.2f, -0.4f), V(0.04f, 0.04f, 0.85f), V(-22, 0, 0));
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable Parasol(Transform root, Vector3 pos, string color)
        {
            var st = New(root, "Зонтик", pos, 0);
            st.AddPart("Ткань", StyleData.Make("Полоска", "#FFFFFF", color, 2.5f));
            st.AddPart("Стойка", StyleData.Make("Гладкий", "#FFF8EE", null, 1, 0.3f));
            Shapes.Box(st, 1, st.transform, V(0, 1.15f, 0), V(0.09f, 2.3f, 0.09f));
            Shapes.MeshPart(Shapes.Cone(), st, 0, st.transform, V(0, 2.25f, 0), V(2.2f, 0.5f, 2.2f), default, true);
            Bake(st, 0.0625f);
            return st;
        }

        /// <summary>Подвесная вывеска с картинкой: 0 — чашка, 1 — книга, 2 — конверт.</summary>
        public static Styleable Sign(Transform root, Vector3 pos, float yaw, int icon)
        {
            var st = New(root, "Вывеска", pos, yaw);
            st.AddPart("Кронштейн", StyleData.Make("Гладкий", "#3E4A45", null, 1, 0.6f));
            st.AddPart("Доска", StyleData.Make("Доски", "#E9D3B0"));
            st.AddPart("Знак", StyleData.Make("Гладкий", icon == 0 ? "#B0563E" : icon == 1 ? "#3F6E8C" : "#C9655A"));
            var t = st.transform;
            Shapes.Box(st, 0, t, V(0, 0.3f, -0.4f), V(0.05f, 0.05f, 0.85f));
            Shapes.Box(st, 0, t, V(0, 0.18f, -0.62f), V(0.03f, 0.22f, 0.03f));
            Shapes.Box(st, 1, t, V(0, -0.25f, -0.62f), V(0.06f, 0.65f, 0.65f));
            if (icon == 0)
            {
                Shapes.Cyl(st, 2, t, V(-0.05f, -0.28f, -0.62f), 0.26f, 0.28f);
                Shapes.Box(st, 2, t, V(-0.05f, -0.26f, -0.45f), V(0.04f, 0.14f, 0.08f));
            }
            else if (icon == 1)
            {
                Shapes.Box(st, 2, t, V(-0.05f, -0.25f, -0.7f), V(0.04f, 0.32f, 0.2f), V(12, 0, 0));
                Shapes.Box(st, 2, t, V(-0.05f, -0.25f, -0.52f), V(0.04f, 0.32f, 0.2f), V(-12, 0, 0));
            }
            else Shapes.Box(st, 2, t, V(-0.05f, -0.25f, -0.62f), V(0.04f, 0.28f, 0.4f));
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable Mailbox(Transform root, Vector3 pos, float yaw, string color)
        {
            var st = New(root, "Почтовый ящик", pos, yaw);
            st.AddPart("Ящик", StyleData.Make("Гладкий", color, null, 1, 0.5f));
            st.AddPart("Столбик", StyleData.Make("Дерево", "#8B5A35"));
            st.AddPart("Флажок", StyleData.Make("Гладкий", "#E0525E"));
            var t = st.transform;
            Shapes.Box(st, 1, t, V(0, 0.45f, 0), V(0.1f, 0.9f, 0.1f));
            Shapes.Box(st, 0, t, V(0, 1.0f, 0), V(0.26f, 0.22f, 0.42f));
            Shapes.Cyl(st, 0, t, V(0, 1.11f, 0), 0.26f, 0.42f, V(90, 0, 0));
            Shapes.Box(st, 2, t, V(0.15f, 1.12f, 0.08f), V(0.03f, 0.2f, 0.08f));
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable LilyPads(Transform root, Vector3 pos, int seed)
        {
            var st = New(root, "Кувшинки", pos, 0);
            st.AddPart("Листья", StyleData.Make("Гладкий", "#6FAE5E"));
            st.AddPart("Цветы", StyleData.Make("Гладкий", "#F7C6D9"));
            st.AddPart("Камыш", StyleData.Make("Гладкий", "#7C9A4A"));
            var t = st.transform;
            for (int i = 0; i < 6; i++)
            {
                float a = VoxelTerrain.Hash(seed, i, 1) * Mathf.PI * 2f, r = VoxelTerrain.Hash(seed, i, 2) * 2.2f;
                var p = V(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                Shapes.Cyl(st, 0, t, p, 0.5f + VoxelTerrain.Hash(seed, i, 3) * 0.3f, 0.04f);
                if (i % 2 == 0) Shapes.Ball(st, 1, t, p + V(0.05f, 0.08f, 0), V(0.16f, 0.12f, 0.16f));
            }
            for (int i = 0; i < 7; i++)
            {
                float a = VoxelTerrain.Hash(seed, i, 5) * Mathf.PI * 2f;
                var p = V(Mathf.Cos(a) * 3.1f, 0.3f, Mathf.Sin(a) * 2.6f);
                Shapes.Box(st, 2, t, p, V(0.05f, 0.7f + VoxelTerrain.Hash(seed, i, 6) * 0.4f, 0.05f));
                Shapes.Box(st, 2, t, p + V(0.12f, -0.05f, 0.05f), V(0.05f, 0.6f, 0.05f));
            }
            Bake(st, 0.0625f);
            return st;
        }
        // ---------------- лес, горы, поляна, обсерватория ----------------

        public static Styleable Rock(Transform root, Vector3 pos, int seed, float size)
        {
            var st = New(root, "Валун", pos, VoxelTerrain.Hash(seed, 5, 5) * 360f);
            st.AddPart("Камень", StyleData.Make("Камень", "#C2BDB7", "#A6A09A", 1.4f));
            st.AddPart("Мох", StyleData.Make("Ворс", "#7DB466"));
            var t = st.transform;
            Shapes.Ball(st, 0, t, V(0, 0.35f, 0) * size, V(1.2f, 0.85f, 1f) * size);
            Shapes.Ball(st, 0, t, V(0.45f, 0.22f, 0.25f) * size, V(0.7f, 0.55f, 0.7f) * size);
            if (VoxelTerrain.Hash(seed, 6, 6) > 0.5f) Shapes.Ball(st, 1, t, V(-0.1f, 0.68f, 0.05f) * size, V(0.6f, 0.2f, 0.5f) * size);
            Bake(st, 0.125f);
            return st;
        }

        public static Styleable Mushrooms(Transform root, Vector3 pos, int seed)
        {
            var st = New(root, "Грибы", pos, VoxelTerrain.Hash(seed, 1, 2) * 360f);
            bool red = VoxelTerrain.Hash(seed, 2, 3) > 0.5f;
            st.AddPart("Шляпки", StyleData.Make(red ? "Горошек" : "Гладкий", red ? "#E0525E" : "#B9844F", "#FFF8EE", 6f));
            st.AddPart("Ножки", StyleData.Make("Гладкий", "#F4EBDD"));
            var t = st.transform;
            int n = 2 + (int)(VoxelTerrain.Hash(seed, 3, 4) * 3);
            for (int i = 0; i < n; i++)
            {
                float a = i * 2.1f, r = 0.12f + i * 0.08f, s = 0.7f + VoxelTerrain.Hash(seed, i, 9) * 0.6f;
                var p = V(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                Shapes.Box(st, 1, t, p + V(0, 0.08f * s, 0), V(0.06f, 0.16f, 0.06f) * s);
                Shapes.Ball(st, 0, t, p + V(0, 0.17f * s, 0), V(0.22f, 0.11f, 0.22f) * s);
            }
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable Log(Transform root, Vector3 pos, float yaw)
        {
            var st = New(root, "Бревно", pos, yaw);
            st.AddPart("Кора", StyleData.Make("Дерево", "#7A5534"));
            st.AddPart("Срез", StyleData.Make("Гладкий", "#D9B48A"));
            st.AddPart("Мох", StyleData.Make("Ворс", "#7DB466"));
            var t = st.transform;
            Shapes.Cyl(st, 0, t, V(0, 0.22f, 0), 0.45f, 2.2f, V(0, 0, 90));
            Shapes.Cyl(st, 1, t, V(1.1f, 0.22f, 0), 0.38f, 0.04f, V(0, 0, 90));
            Shapes.Cyl(st, 1, t, V(-1.1f, 0.22f, 0), 0.38f, 0.04f, V(0, 0, 90));
            Shapes.Ball(st, 2, t, V(0.2f, 0.42f, 0), V(0.7f, 0.12f, 0.3f));
            Bake(st, 0.0625f);
            return st;
        }

        public static Styleable Tent(Transform root, Vector3 pos, float yaw, string color)
        {
            var st = New(root, "Палатка", pos, yaw + 180f);
            st.AddPart("Ткань", StyleData.Make("Полоска", color, "#FFF8EE", 1.5f));
            st.AddPart("Вход", StyleData.Make("Гладкий", "#5A4A3A"));
            var t = st.transform;
            st.AddPart("Коврик", StyleData.Make("Клетка", "#E9D3B0", "#C99A66", 3f));
            st.AddPart("Колышки", StyleData.Make("Дерево", "#8B5A35"));
            // открытая палатка-«домик»: два ската, задняя стенка, распахнутые полы, коврик внутри
            foreach (var sgn in new[] { -1f, 1f })
                Shapes.Box(st, 0, t, V(sgn * 0.52f, 0.66f, 0), V(0.05f, 1.62f, 2.2f), V(0, 0, sgn * 38f));
            Shapes.MeshPart(Shapes.Pyramid(), st, 0, t, V(0, 0.66f, 1.08f), V(2.0f, 1.32f, 0.05f), default, true);
            foreach (var sgn in new[] { -1f, 1f })
                Shapes.Box(st, 0, t, V(sgn * 0.85f, 0.5f, -1.25f), V(0.05f, 1.0f, 0.55f), V(0, sgn * -40f, sgn * 30f));
            Shapes.Box(st, 2, t, V(0, 0.03f, 0), V(1.5f, 0.04f, 2.0f));
            Shapes.Box(st, 3, t, V(0, 1.33f, 0), V(0.08f, 0.08f, 2.4f));
            // стойки под концами конька: верх стойки заходит в конёк, а не стоит рядом
            foreach (var z in new[] { -1.13f, 1.13f }) Shapes.Box(st, 3, t, V(0, 0.68f, z), V(0.07f, 1.38f, 0.07f));
            Bake(st, 0.0625f);
            return st;
        }

        /// <summary>
        /// Древнее дерево: огромный ствол с корнями, дверцей и круглым окошком, ветви, пышная крона
        /// и фонарики, которые вечером светятся.
        /// </summary>
        public static Styleable AncientTree(Transform root, Vector3 pos)
        {
            var st = New(root, "Древнее дерево", pos, 17f);
            st.AddPart("Кора", StyleData.Make("Дерево", "#7A5534", null, 0.6f));
            st.AddPart("Листва", StyleData.Make("Ворс", "#5E9A48", null, 0.6f));
            st.AddPart("Листва светлая", StyleData.Make("Ворс", "#7DB466", null, 0.6f));
            st.AddPart("Дверца", StyleData.Make("Доски", "#B98A5A"));
            st.AddPart("Фонарики", StyleData.Make("Гладкий", "#FFE7A8", null, 1, 0.9f));
            st.AddPart("Мох", StyleData.Make("Ворс", "#8FB07A"));
            var t = st.transform;
            // ствол: широкий у земли, чуть изгибается
            for (int i = 0; i < 12; i++)
            {
                float y = i * 0.85f, k = i / 11f;
                float r = Mathf.Lerp(3.4f, 1.7f, Mathf.Sqrt(k));
                var off = V(Mathf.Sin(k * 2.2f) * 0.6f, 0, Mathf.Cos(k * 1.7f) * 0.4f - 0.4f);
                Shapes.Cyl(st, 0, t, off + V(0, y + 0.45f, 0), r * 2f, 0.9f);
            }
            // корни
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * 360f + (i % 2) * 14f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                Shapes.Box(st, 0, t, dir * 3.4f + V(0, 0.35f, 0), V(1.1f, 0.9f, 3.2f), V(16, a, 0));
                Shapes.Box(st, 5, t, dir * 4.4f + V(0, 0.2f, 0), V(0.8f, 0.4f, 1.4f), V(10, a, 0));
            }
            // дверца и окошко
            Shapes.Box(st, 3, t, V(0, 1.1f, -3.25f), V(1.1f, 1.9f, 0.3f));
            Shapes.Ball(st, 3, t, V(0, 2.05f, -3.25f), V(1.1f, 0.8f, 0.3f));
            Shapes.Cyl(st, 4, t, V(1.3f, 4.2f, -2.85f), 0.7f, 0.2f, V(90, 0, 0));
            // Подвесы начинаются внутри веток, а нижний конец нити входит в фонарик.
            void HangLantern(Transform branch, float along, float length)
            {
                var anchor = t.InverseTransformPoint(branch.TransformPoint(V(0, -0.14f, along)));
                var lamp = anchor - V(0, length, 0);
                Shapes.Box(st, 5, t, (lamp + anchor) * 0.5f, V(0.19f, length, 0.19f));
                Shapes.Ball(st, 4, t, lamp, V(0.45f, 0.55f, 0.45f));
            }

            // ветви
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * 360f + 20f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var branch = Shapes.Box(st, 0, t, dir * 3.2f + V(0, 9.2f + (i % 3) * 0.8f, 0), V(1.0f, 1.0f, 6.5f), V(-28, a, 0)).transform;
                HangLantern(branch, 0.24f + (i % 2) * 0.12f, 1.6f + (i % 3) * 0.35f);
                if (i == 2) HangLantern(branch, 0.04f, 2.2f);
            }
            // крона
            for (int i = 0; i < 13; i++)
            {
                float a = i / 13f * Mathf.PI * 2f * 2.3f;
                float rr = i == 0 ? 0 : 3.5f + (i % 4) * 1.6f;
                float s = 5.5f + (i % 3) * 1.6f;
                Shapes.Ball(st, i % 3 == 0 ? 2 : 1, t, V(Mathf.Cos(a) * rr, 12.5f + (i % 4) * 1.3f + (i == 0 ? 2.5f : 0), Mathf.Sin(a) * rr), V(s, s * 0.75f, s));
            }
            Bake(st, 0.25f);
            Lantern(st, 4);
            AddLight(st.transform, V(0, 7.5f, -4.5f), 14f, 1.6f);
            AddLight(st.transform, V(4.5f, 7.5f, 3f), 12f, 1.2f);
            return st;
        }

        /// <summary>Купол обсерватории со щелью и трубой телескопа.</summary>
        public static Styleable Dome(Transform root, Vector3 pos)
        {
            var st = New(root, "Купол обсерватории", pos, 200f);
            st.AddPart("Купол", StyleData.Make("Гладкий", "#E8ECF2", null, 1, 0.6f));
            st.AddPart("Щель", StyleData.Make("Гладкий", "#2E3A5C"));
            st.AddPart("Телескоп", StyleData.Make("Гладкий", "#C9A24A", null, 1, 0.7f));
            var t = st.transform;
            Shapes.Cyl(st, 0, t, V(0, 0.3f, 0), 5.4f, 0.6f);
            for (int i = 0; i < 11; i++)
            {
                float hh = i * 0.25f, r = 2.6f * Mathf.Sqrt(Mathf.Max(0.02f, 1f - (hh / 2.7f) * (hh / 2.7f)));
                Shapes.Cyl(st, 0, t, V(0, 0.6f + hh + 0.125f, 0), r * 2f, 0.25f);
            }
            Shapes.Box(st, 1, t, V(0, 2.2f, -1.6f), V(0.9f, 2.6f, 1.8f), V(-35, 0, 0));
            Shapes.Cyl(st, 2, t, V(0, 2.9f, -2.2f), 0.5f, 2.2f, V(-55, 0, 0));
            Bake(st, 0.125f);
            // нижняя половина шара прячется в крыше
            return st;
        }

    }

    /// <summary>Стрелки часов на башне идут по игровому времени.</summary>
    public class ClockHands : MonoBehaviour
    {
        public readonly List<Transform> hours = new List<Transform>(), minutes = new List<Transform>();

        void Update()
        {
            if (Game.I == null) return;
            float m = Game.I.minutes % 720f;
            foreach (var h in hours) h.localRotation = Quaternion.Euler(0, 0, -m / 720f * 360f);
            foreach (var mm in minutes) mm.localRotation = Quaternion.Euler(0, 0, -(m % 60f) / 60f * 360f);
        }
    }

    /// <summary>Крутит предмет вокруг его оси Z (крылья мельницы).</summary>
    public class Spinner : MonoBehaviour
    {
        public float speed = 20f;
        void Update() => transform.Rotate(0, 0, speed * Time.deltaTime, Space.Self);
    }
}
