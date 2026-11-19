using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public enum ClothSlot { Head, Neck, Body, Eyes }

    /// <summary>Вещь гардероба: из простых форм, каждая деталь красится той же системой стиля.</summary>
    public class ClothingDef
    {
        public string id, name;
        public ClothSlot slot;
        public (string name, StyleData style)[] parts;
        public Action<Styleable, Transform> build;
        public bool glassLast; // последняя деталь — стекло (очки)
    }

    [Serializable]
    public class OutfitItem
    {
        public string id;
        public List<StyleData> styles = new List<StyleData>();
    }

    [Serializable]
    public class Outfit
    {
        public List<OutfitItem> items = new List<OutfitItem>();
        public OutfitItem Get(ClothSlot s) => items.Find(i => Wardrobe.Get(i.id) != null && Wardrobe.Get(i.id).slot == s);
    }

    public static class Wardrobe
    {
        public static readonly string[] OutfitNames = { "Повседневный", "Пижама", "Дождевик" };
        public static readonly string[] SlotNames = { "Голова", "Шея", "Тело", "Глаза" };

        static List<ClothingDef> all;
        static Mesh fezCrown;
        public static List<ClothingDef> All { get { if (all == null) Build(); return all; } }
        public static ClothingDef Get(string id) { foreach (var d in All) if (d.id == id) return d; return null; }
        public static List<ClothingDef> ForSlot(ClothSlot s) => All.FindAll(d => d.slot == s);

        static StyleData M(string p, string a, string b = null, float sc = 1f, float g = 0.15f) => StyleData.Make(p, a, b, sc, g);
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static void Add(string id, string name, ClothSlot slot, (string, StyleData)[] parts, Action<Styleable, Transform> build, bool glass = false)
            => all.Add(new ClothingDef { id = id, name = name, slot = slot, parts = parts, build = build, glassLast = glass });

        static void Build()
        {
            all = new List<ClothingDef>();
            // ----- голова (относительно головы) -----
            Add("beanie", "Шапочка с помпоном", ClothSlot.Head, new[] { ("Шапка", M("Вельвет", "#E57373")), ("Помпон", M("Ворс", "#FFF8EE")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.17f, -0.01f), V(0.47f, 0.13f, 0.39f));
                Shapes.Box(st, 0, t, V(0, 0.11f, -0.01f), V(0.49f, 0.04f, 0.41f));
                Shapes.Ball(st, 1, t, V(0, 0.28f, -0.01f), V(0.13f, 0.13f, 0.13f));
            });
            Add("beret", "Беретик", ClothSlot.Head, new[] { ("Берет", M("Ткань", "#5B7FA8")), ("Хвостик", M("Гладкий", "#2E3A5C")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0.04f, 0.2f, -0.02f), V(0.42f, 0.06f, 0.38f), V(0, 0, 9));
                Shapes.Box(st, 1, t, V(0.06f, 0.25f, -0.02f), V(0.03f, 0.05f, 0.03f));
            });
            Add("strawhat", "Соломенная шляпа", ClothSlot.Head, new[] { ("Солома", M("Ткань", "#E9D3A0", null, 3f)), ("Лента", M("Гладкий", "#E57373")) }, (st, t) =>
            {
                Shapes.Cyl(st, 0, t, V(0, 0.2f, -0.01f), 0.72f, 0.03f);
                Shapes.Cyl(st, 0, t, V(0, 0.27f, -0.01f), 0.34f, 0.14f);
                Shapes.Cyl(st, 1, t, V(0, 0.23f, -0.01f), 0.35f, 0.04f);
            });
            Add("bow", "Бант", ClothSlot.Head, new[] { ("Бант", M("Горошек", "#F5A3C1", "#FFFFFF", 4f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(-0.07f, 0.22f, 0.03f), V(0.11f, 0.09f, 0.05f), V(0, 0, 15));
                Shapes.Box(st, 0, t, V(0.07f, 0.22f, 0.03f), V(0.11f, 0.09f, 0.05f), V(0, 0, -15));
                Shapes.Box(st, 0, t, V(0, 0.22f, 0.04f), V(0.05f, 0.06f, 0.06f));
            });
            Add("wreath", "Цветочный венок", ClothSlot.Head, new[] { ("Цветы", M("Гладкий", "#F7DE7E")), ("Цветы 2", M("Гладкий", "#F5A3C1")), ("Листья", M("Ворс", "#6FA85A")) }, (st, t) =>
            {
                for (int i = 0; i < 10; i++)
                {
                    float a = i / 10f * Mathf.PI * 2f;
                    var p = V(Mathf.Sin(a) * 0.22f, 0.19f, Mathf.Cos(a) * 0.19f - 0.01f);
                    Shapes.Ball(st, 2, t, p, V(0.09f, 0.06f, 0.09f));
                    if (i % 2 == 0) Shapes.Ball(st, i % 4 == 0 ? 0 : 1, t, p + V(0, 0.035f, 0), V(0.075f, 0.06f, 0.075f));
                }
            });
            Add("nightcap", "Ночной колпак", ClothSlot.Head, new[] { ("Колпак", M("Звёздочки", "#5B7FA8", "#F2D98A", 3f)), ("Помпон", M("Ворс", "#FFF8EE")) }, (st, t) =>
            {
                Shapes.MeshPart(Shapes.Cone(), st, 0, t, V(0, 0.32f, -0.04f), V(0.42f, 0.36f, 0.38f), V(-18, 0, 0));
                Shapes.Ball(st, 1, t, V(0, 0.48f, -0.12f), V(0.1f, 0.1f, 0.1f));
            });
            Add("souwester", "Зюйдвестка", ClothSlot.Head, new[] { ("Шляпа", M("Гладкий", "#F2C14E", null, 1, 0.6f)) }, (st, t) =>
            {
                Shapes.Cyl(st, 0, t, V(0, 0.2f, -0.03f), 0.6f, 0.03f);
                Shapes.Box(st, 0, t, V(0, 0.27f, -0.02f), V(0.44f, 0.13f, 0.38f));
            });
            // ----- шея (относительно корпуса) -----
            Add("scarf", "Шарф", ClothSlot.Neck, new[] { ("Шарф", M("Полоска", "#E57373", "#FFF8EE", 3f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.22f, 0.33f), V(0.5f, 0.12f, 0.2f));
                Shapes.Box(st, 0, t, V(0.14f, 0.09f, 0.44f), V(0.1f, 0.24f, 0.04f), V(0, 0, 6));
            });
            Add("bowtie", "Бабочка", ClothSlot.Neck, new[] { ("Бабочка", M("Гладкий", "#B33A3A")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(-0.07f, 0.16f, 0.42f), V(0.11f, 0.09f, 0.04f));
                Shapes.Box(st, 0, t, V(0.07f, 0.16f, 0.42f), V(0.11f, 0.09f, 0.04f));
                Shapes.Box(st, 0, t, V(0, 0.16f, 0.43f), V(0.05f, 0.05f, 0.05f));
            });
            Add("bell", "Колокольчик", ClothSlot.Neck, new[] { ("Лента", M("Гладкий", "#5B8FB9")), ("Колокольчик", M("Гладкий", "#E8C55A", null, 1, 0.8f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.2f, 0.35f), V(0.47f, 0.05f, 0.13f));
                Shapes.Ball(st, 1, t, V(0, 0.13f, 0.43f), V(0.09f, 0.09f, 0.09f));
            });
            Add("kerchief", "Шейный платок", ClothSlot.Neck, new[] { ("Платок", M("Клетка", "#7FA36B", "#F4EBDD", 3f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.21f, 0.34f), V(0.48f, 0.08f, 0.16f));
                Shapes.MeshPart(Shapes.Pyramid(), st, 0, t, V(0, 0.11f, 0.42f), V(0.22f, 0.16f, 0.04f), V(180, 0, 0));
            });
            // ----- тело (относительно корпуса) -----
            Add("sweater", "Свитер", ClothSlot.Body, new[] { ("Свитер", M("Полоска", "#9DB7C9", "#F4EBDD", 2f)), ("Ворот", M("Вельвет", "#F4EBDD")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.07f, 0.03f), V(0.5f, 0.42f, 0.6f));
                Shapes.Box(st, 1, t, V(0, 0.24f, 0.33f), V(0.42f, 0.1f, 0.08f));
            });
            Add("vest", "Жилетка", ClothSlot.Body, new[] { ("Жилетка", M("Клетка", "#A0703C", "#7A5230", 2f)), ("Пуговицы", M("Гладкий", "#E8C55A", null, 1, 0.8f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.09f, 0.08f), V(0.5f, 0.36f, 0.42f));
                for (int i = 0; i < 2; i++) Shapes.Box(st, 1, t, V(0.08f, 0.13f - i * 0.1f, 0.3f), V(0.035f, 0.035f, 0.03f));
            });
            Add("apron", "Фартук", ClothSlot.Body, new[] { ("Фартук", M("Клетка", "#F4EBDD", "#E57373", 3f)), ("Завязки", M("Гладкий", "#E57373")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, -0.02f, 0.4f), V(0.38f, 0.32f, 0.03f));
                Shapes.Box(st, 1, t, V(0, 0.1f, 0.1f), V(0.49f, 0.04f, 0.6f));
                foreach (var x in new[] { -0.15f, 0.15f }) Shapes.Box(st, 1, t, V(x, 0.2f, 0.4f), V(0.03f, 0.13f, 0.035f));
            });
            Add("pajama", "Пижама", ClothSlot.Body, new[] { ("Пижама", M("Горошек", "#C9DCEB", "#FFFFFF", 3f)), ("Отделка", M("Гладкий", "#5B7FA8")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.06f, 0), V(0.5f, 0.42f, 0.82f));
                Shapes.Box(st, 1, t, V(0, 0.24f, 0.4f), V(0.4f, 0.06f, 0.05f));
                foreach (var x in new[] { -0.13f, 0.13f })
                    foreach (var z in new[] { -0.27f, 0.27f })
                        Shapes.Box(st, 0, t, V(x, -0.17f, z), V(0.12f, 0.12f, 0.12f));
            });
            Add("raincoat", "Плащ-дождевик", ClothSlot.Body, new[] { ("Плащ", M("Гладкий", "#F2C14E", null, 1, 0.65f)), ("Пуговицы", M("Гладкий", "#5A4A3A")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.04f, 0), V(0.53f, 0.46f, 0.86f));
                Shapes.Box(st, 0, t, V(0, 0.27f, 0.32f), V(0.48f, 0.08f, 0.2f));
                for (int i = 0; i < 3; i++) Shapes.Box(st, 1, t, V(0, 0.18f - i * 0.1f, 0.44f), V(0.04f, 0.04f, 0.03f));
            });
            // ----- глаза (относительно головы) -----
            Add("glasses", "Круглые очки", ClothSlot.Eyes, new[] { ("Оправа", M("Гладкий", "#5A4A3A", null, 1, 0.7f)), ("Стёкла", M("Гладкий", "#DDEEFF", null, 1, 0.95f)) }, (st, t) =>
            {
                foreach (var s in new[] { -0.11f, 0.11f })
                {
                    Shapes.Cyl(st, 0, t, V(s, 0.05f, 0.2f), 0.15f, 0.02f, V(90, 0, 0));
                    Shapes.Cyl(st, 1, t, V(s, 0.05f, 0.212f), 0.11f, 0.01f, V(90, 0, 0));
                    Shapes.Box(st, 0, t, V(s * 2f, 0.06f, 0.08f), V(0.015f, 0.02f, 0.24f));
                    // шарнир: от края оправы к дужке (раньше между ними был зазор)
                    Shapes.Box(st, 0, t, V(Mathf.Sign(s) * 0.2f, 0.06f, 0.2f), V(0.055f, 0.02f, 0.02f));
                }
                Shapes.Box(st, 0, t, V(0, 0.06f, 0.205f), V(0.08f, 0.02f, 0.015f));
            }, true);
            Add("sunglasses", "Солнечные очки", ClothSlot.Eyes, new[] { ("Оправа", M("Гладкий", "#222222", null, 1, 0.8f)), ("Стёкла", M("Гладкий", "#2E3A5C", null, 1, 0.95f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.07f, 0.2f), V(0.42f, 0.03f, 0.02f));
                foreach (var s in new[] { -0.1f, 0.1f }) Shapes.Box(st, 1, t, V(s, 0.04f, 0.205f), V(0.15f, 0.08f, 0.015f));
                foreach (var s in new[] { -0.22f, 0.22f }) Shapes.Box(st, 0, t, V(s, 0.07f, 0.08f), V(0.015f, 0.02f, 0.24f));
            }, true);
            More();
        }

        /// <summary>Ещё вещи: кепка, цилиндр, наушники, заколки, бусы, галстук, худи, комбинезон, платье, накидка, очки разных форм…</summary>
        static void More()
        {
            // ----- голова -----
            Add("cap", "Кепка", ClothSlot.Head, new[] { ("Кепка", M("Ткань", "#5B7FA8")), ("Козырёк", M("Гладкий", "#2E3A5C")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.18f, -0.01f), V(0.46f, 0.12f, 0.41f));
                Shapes.Box(st, 1, t, V(0, 0.13f, 0.22f), V(0.36f, 0.025f, 0.2f), V(-8, 0, 0));
                Shapes.Box(st, 1, t, V(0, 0.25f, -0.02f), V(0.05f, 0.03f, 0.05f));
            });
            Add("tophat", "Цилиндр", ClothSlot.Head, new[] { ("Цилиндр", M("Гладкий", "#2A2A2E", null, 1, 0.4f)), ("Лента", M("Гладкий", "#B33A3A")) }, (st, t) =>
            {
                Shapes.Cyl(st, 0, t, V(0.02f, 0.2f, -0.02f), 0.42f, 0.025f);
                Shapes.Cyl(st, 0, t, V(0.02f, 0.33f, -0.02f), 0.26f, 0.26f);
                Shapes.Cyl(st, 1, t, V(0.02f, 0.24f, -0.02f), 0.265f, 0.05f);
            });
            Add("fez", "Феска", ClothSlot.Head, new[] { ("Фетр", M("Ткань", "#AA3548")), ("Кисточка", M("Ткань", "#31252D")) }, (st, t) =>
            {
                Shapes.MeshPart(FezCrown(), st, 0, t, V(0, 0.29f, 0.087f), V(0.27f, 0.23f, 0.19f), collider: true);
                Shapes.Cyl(st, 1, t, V(0, 0.407f, 0.087f), 0.036f, 0.012f);
                Shapes.Box(st, 1, t, V(0.012f, 0.41f, 0.13f), V(0.012f, 0.01f, 0.085f), V(0, 16, 0));
                Shapes.Box(st, 1, t, V(0.025f, 0.366f, 0.177f), V(0.012f, 0.088f, 0.012f), V(-8, 0, 0));
                Shapes.Cyl(st, 1, t, V(0.025f, 0.31f, 0.184f), 0.034f, 0.062f, V(-8, 0, 0));
            });
            Add("earmuffs", "Наушники-грелки", ClothSlot.Head, new[] { ("Мех", M("Ворс", "#F5A3C1")), ("Дужка", M("Гладкий", "#C9B4E8")) }, (st, t) =>
            {
                foreach (var x in new[] { -0.25f, 0.25f }) Shapes.Ball(st, 0, t, V(x, 0.06f, -0.02f), V(0.1f, 0.14f, 0.14f));
                Shapes.Box(st, 1, t, V(0, 0.2f, -0.02f), V(0.5f, 0.03f, 0.05f));
                foreach (var x in new[] { -0.25f, 0.25f }) Shapes.Box(st, 1, t, V(x, 0.14f, -0.02f), V(0.03f, 0.12f, 0.05f));
            });
            Add("flowerclip", "Цветочек-заколка", ClothSlot.Head, new[] { ("Лепестки", M("Гладкий", "#F7DE7E")), ("Серединка", M("Гладкий", "#E9A93E")) }, (st, t) =>
            {
                var c = V(0.15f, 0.2f, 0.06f);
                for (int i = 0; i < 5; i++)
                {
                    float a = i / 5f * Mathf.PI * 2f;
                    Shapes.Ball(st, 0, t, c + V(Mathf.Cos(a) * 0.04f, Mathf.Sin(a) * 0.04f, 0), V(0.05f, 0.05f, 0.03f));
                }
                Shapes.Ball(st, 1, t, c + V(0, 0, 0.01f), V(0.04f, 0.04f, 0.035f));
            });
            Add("bandana", "Бандана", ClothSlot.Head, new[] { ("Бандана", M("Горошек", "#E57373", "#FFFFFF", 4f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.15f, -0.01f), V(0.48f, 0.08f, 0.4f));
                Shapes.Box(st, 0, t, V(0.12f, 0.12f, -0.24f), V(0.06f, 0.1f, 0.04f), V(0, 0, 25));
                Shapes.Box(st, 0, t, V(0.17f, 0.12f, -0.24f), V(0.06f, 0.1f, 0.04f), V(0, 0, -25));
            });
            // ----- шея -----
            Add("pearls", "Бусы", ClothSlot.Neck, new[] { ("Бусины", M("Гладкий", "#FFF8EE", null, 1, 0.85f)) }, (st, t) =>
            {
                for (int i = 0; i < 11; i++)
                {
                    float a = (i / 10f - 0.5f) * Mathf.PI * 0.95f;
                    Shapes.Ball(st, 0, t, V(Mathf.Sin(a) * 0.22f, 0.15f + Mathf.Abs(Mathf.Sin(a)) * 0.06f, 0.33f + Mathf.Cos(a) * 0.1f), V(0.05f, 0.05f, 0.05f));
                }
            });
            Add("tie", "Галстук", ClothSlot.Neck, new[] { ("Галстук", M("Полоска", "#5B7FA8", "#F2D98A", 4f)), ("Воротник", M("Гладкий", "#FFFFFF")) }, (st, t) =>
            {
                Shapes.Box(st, 1, t, V(0, 0.22f, 0.36f), V(0.46f, 0.06f, 0.12f));
                Shapes.Box(st, 0, t, V(0, 0.18f, 0.43f), V(0.06f, 0.05f, 0.04f));
                Shapes.Box(st, 0, t, V(0, 0.07f, 0.43f), V(0.08f, 0.18f, 0.03f));
            });
            Add("lei", "Гирлянда из цветов", ClothSlot.Neck, new[] { ("Цветы", M("Гладкий", "#F5A3C1")), ("Цветы 2", M("Гладкий", "#F7DE7E")) }, (st, t) =>
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = (i / 11f - 0.5f) * Mathf.PI;
                    Shapes.Ball(st, i % 2, t, V(Mathf.Sin(a) * 0.24f, 0.16f + Mathf.Abs(Mathf.Sin(a)) * 0.05f, 0.32f + Mathf.Cos(a) * 0.11f), V(0.08f, 0.07f, 0.08f));
                }
            });
            Add("longscarf", "Длинный шарф", ClothSlot.Neck, new[] { ("Шарф", M("Клетка", "#7FA36B", "#F2C14E", 3f)), ("Кисточки", M("Ворс", "#F2C14E")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.22f, 0.33f), V(0.52f, 0.13f, 0.22f));
                Shapes.Box(st, 0, t, V(-0.12f, 0.268f, 0.06f), V(0.11f, 0.035f, 0.5f), V(0, 4, 0));
                Shapes.Box(st, 1, t, V(-0.13f, 0.268f, -0.215f), V(0.12f, 0.03f, 0.06f), V(0, 4, 0));
                Shapes.Box(st, 0, t, V(0.13f, 0.07f, 0.44f), V(0.1f, 0.26f, 0.035f), V(0, 0, 5));
                Shapes.Box(st, 1, t, V(0.14f, -0.08f, 0.44f), V(0.11f, 0.05f, 0.04f), V(0, 0, 5));
            });
            // ----- тело -----
            Add("hoodie", "Худи", ClothSlot.Body, new[] { ("Худи", M("Вельвет", "#C9B4E8")), ("Карман", M("Гладкий", "#A48BC9")), ("Шнурки", M("Гладкий", "#FFFFFF")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.07f, 0.03f), V(0.52f, 0.44f, 0.62f));
                Shapes.Box(st, 0, t, V(0, 0.28f, 0.16f), V(0.44f, 0.12f, 0.2f));
                Shapes.Box(st, 1, t, V(0, -0.05f, 0.32f), V(0.3f, 0.1f, 0.03f));
                foreach (var x in new[] { -0.06f, 0.06f }) Shapes.Box(st, 2, t, V(x, 0.13f, 0.34f), V(0.02f, 0.1f, 0.02f));
            });
            Add("overalls", "Комбинезон", ClothSlot.Body, new[] { ("Ткань", M("Ткань", "#5B7FA8", null, 2f)), ("Пуговицы", M("Гладкий", "#E8C55A", null, 1, 0.8f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, -0.02f, 0), V(0.51f, 0.3f, 0.84f));
                Shapes.Box(st, 0, t, V(0, 0.12f, 0.3f), V(0.3f, 0.2f, 0.04f));
                foreach (var x in new[] { -0.12f, 0.12f }) Shapes.Box(st, 1, t, V(x, 0.2f, 0.33f), V(0.04f, 0.04f, 0.02f));
                foreach (var x in new[] { -0.13f, 0.13f })
                    foreach (var z in new[] { -0.27f, 0.27f })
                        Shapes.Box(st, 0, t, V(x, -0.17f, z), V(0.12f, 0.1f, 0.12f));
            });
            Add("dress", "Платье", ClothSlot.Body, new[] { ("Платье", M("Горошек", "#F5A3C1", "#FFFFFF", 3f)), ("Пояс", M("Гладкий", "#E57373")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.08f, 0.05f), V(0.5f, 0.38f, 0.73f));
                Shapes.Box(st, 0, t, V(0, 0.0f, -0.13f), V(0.58f, 0.26f, 0.6f));
                Shapes.Box(st, 0, t, V(0, -0.1f, -0.15f), V(0.64f, 0.12f, 0.62f));
                Shapes.Box(st, 1, t, V(0, 0.08f, 0.19f), V(0.52f, 0.4f, 0.05f));
            });
            Add("cape", "Накидка", ClothSlot.Body, new[] { ("Накидка", M("Звёздочки", "#2E3A5C", "#F2D98A", 3f)), ("Застёжка", M("Гладкий", "#E8C55A", null, 1, 0.8f)) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.27f, -0.04f), V(0.54f, 0.05f, 0.76f));
                foreach (var x in new[] { -0.265f, 0.265f }) Shapes.Box(st, 0, t, V(x, 0.13f, -0.07f), V(0.03f, 0.3f, 0.7f));
                Shapes.Box(st, 0, t, V(0, 0.22f, 0.36f), V(0.5f, 0.07f, 0.1f));
                Shapes.Ball(st, 1, t, V(0, 0.22f, 0.42f), V(0.07f, 0.07f, 0.04f));
            });
            Add("tshirt", "Футболка", ClothSlot.Body, new[] { ("Футболка", M("Гладкий", "#F2C14E")), ("Рисунок", M("Гладкий", "#E57373")) }, (st, t) =>
            {
                Shapes.Box(st, 0, t, V(0, 0.08f, 0.125f), V(0.5f, 0.36f, 0.59f));
                Shapes.Ball(st, 1, t, V(0, 0.06f, 0.42f), V(0.13f, 0.13f, 0.02f));
                foreach (var x in new[] { -0.13f, 0.13f }) Shapes.Box(st, 0, t, V(x, -0.14f, 0.27f), V(0.145f, 0.08f, 0.145f));
            });
            // ----- глаза -----
            Add("heartglasses", "Очки-сердечки", ClothSlot.Eyes, new[] { ("Оправа", M("Гладкий", "#E57373", null, 1, 0.7f)), ("Стёкла", M("Гладкий", "#F5A3C1", null, 1, 0.95f)) }, (st, t) =>
            {
                foreach (var s in new[] { -0.11f, 0.11f })
                {
                    // сердечко: два крупных «горбика» сверху, острый кончик снизу
                    Shapes.Box(st, 1, t, V(s, 0.033f, 0.208f), V(0.072f, 0.072f, 0.012f), V(0, 0, 45));
                    Shapes.Ball(st, 1, t, V(s - 0.032f, 0.067f, 0.208f), V(0.078f, 0.078f, 0.012f));
                    Shapes.Ball(st, 1, t, V(s + 0.032f, 0.067f, 0.208f), V(0.078f, 0.078f, 0.012f));
                    Shapes.Box(st, 0, t, V(Mathf.Sign(s) * 0.2f, 0.07f, 0.2f), V(0.07f, 0.02f, 0.02f));
                    Shapes.Box(st, 0, t, V(Mathf.Sign(s) * 0.225f, 0.07f, 0.08f), V(0.015f, 0.02f, 0.24f));
                }
                Shapes.Box(st, 0, t, V(0, 0.07f, 0.205f), V(0.06f, 0.02f, 0.015f));
            }, true);
            Add("squareglasses", "Квадратные очки", ClothSlot.Eyes, new[] { ("Оправа", M("Гладкий", "#2A2A2E", null, 1, 0.7f)), ("Стёкла", M("Гладкий", "#DDEEFF", null, 1, 0.95f)) }, (st, t) =>
            {
                foreach (var s in new[] { -0.11f, 0.11f })
                {
                    Shapes.Box(st, 0, t, V(s, 0.05f, 0.2f), V(0.15f, 0.12f, 0.02f));
                    Shapes.Box(st, 1, t, V(s, 0.05f, 0.212f), V(0.11f, 0.08f, 0.01f));
                    Shapes.Box(st, 0, t, V(Mathf.Sign(s) * 0.21f, 0.08f, 0.2f), V(0.05f, 0.02f, 0.02f));
                    Shapes.Box(st, 0, t, V(Mathf.Sign(s) * 0.225f, 0.08f, 0.08f), V(0.015f, 0.02f, 0.24f));
                }
                Shapes.Box(st, 0, t, V(0, 0.07f, 0.205f), V(0.08f, 0.02f, 0.015f));
            }, true);
            Add("monocle", "Монокль", ClothSlot.Eyes, new[] { ("Оправа", M("Гладкий", "#E8C55A", null, 1, 0.85f)), ("Стекло", M("Гладкий", "#DDEEFF", null, 1, 0.95f)) }, (st, t) =>
            {
                Shapes.Cyl(st, 0, t, V(0.11f, 0.05f, 0.2f), 0.15f, 0.02f, V(90, 0, 0));
                Shapes.Cyl(st, 1, t, V(0.11f, 0.05f, 0.212f), 0.11f, 0.01f, V(90, 0, 0));
                Shapes.Box(st, 0, t, V(0.175f, -0.07f, 0.205f), V(0.012f, 0.2f, 0.012f), V(0, 0, 12));
            }, true);
        }

        // Плоский верх и сужающиеся стенки. Отдельные вершины крышек сохраняют чёткий край.
        static Mesh FezCrown()
        {
            if (fezCrown != null) return fezCrown;
            const int sides = 18;
            const float topRadius = 0.39f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float angle = i * 2f * Mathf.PI / sides;
                var radial = V(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var bottom = radial * 0.5f + Vector3.down * 0.5f;
                var top = radial * topRadius + Vector3.up * 0.5f;
                var wallNormal = (radial + Vector3.up * (0.5f - topRadius)).normalized;
                int ring = vertices.Count;
                int next = (i + 1) % sides * 4;
                vertices.AddRange(new[] { bottom, top, bottom, top });
                normals.AddRange(new[] { wallNormal, wallNormal, Vector3.down, Vector3.up });
                triangles.AddRange(new[]
                {
                    ring, ring + 1, next,
                    next, ring + 1, next + 1,
                    ring + 2, next + 2, sides * 4,
                    ring + 3, sides * 4 + 1, next + 3
                });
            }
            vertices.AddRange(new[] { Vector3.down * 0.5f, Vector3.up * 0.5f });
            normals.AddRange(new[] { Vector3.down, Vector3.up });
            fezCrown = new Mesh { name = "Феска" };
            fezCrown.SetVertices(vertices);
            fezCrown.SetNormals(normals);
            fezCrown.SetTriangles(triangles, 0);
            return fezCrown;
        }

        public static OutfitItem Item(string id)
        {
            var d = Get(id);
            var it = new OutfitItem { id = id };
            if (d != null) foreach (var p in d.parts) it.styles.Add(p.style.Clone());
            return it;
        }

        /// <summary>Наряды для новой лисички: повседневный — по характеру и случаю, пижама и дождевик — всегда.</summary>
        public static Outfit[] Defaults(Fox f, int seed = 0)
        {
            var o = new[] { new Outfit(), new Outfit(), new Outfit() };
            var r = new System.Random((f.id != null ? f.id.GetHashCode() : 0) ^ seed);
            string[] heads = { "beanie", "beret", "strawhat", "bow", "wreath", "cap", "tophat", "earmuffs", "flowerclip", "bandana" };
            string[] necks = { "scarf", "bowtie", "bell", "kerchief", "pearls", "tie", "lei", "longscarf" };
            string[] bodies = { "sweater", "vest", "hoodie", "overalls", "dress", "cape", "tshirt" };
            if (r.NextDouble() < 0.35) o[0].items.Add(Recolor(Item(heads[r.Next(heads.Length)]), r));
            if (r.NextDouble() < 0.55) o[0].items.Add(Recolor(Item(necks[r.Next(necks.Length)]), r));
            if (r.NextDouble() < 0.25) o[0].items.Add(Recolor(Item(bodies[r.Next(bodies.Length)]), r));
            if (f.pers.O >= 8 && r.NextDouble() < 0.5) o[0].items.Add(Item(r.NextDouble() < 0.7 ? "glasses" : "squareglasses"));
            else if (r.NextDouble() < 0.06) o[0].items.Add(Item(r.NextDouble() < 0.5 ? "heartglasses" : "monocle"));
            o[1].items.Add(Recolor(Item("pajama"), r));
            if (r.NextDouble() < 0.5) o[1].items.Add(Item("nightcap"));
            o[2].items.Add(Recolor(Item("raincoat"), r));
            o[2].items.Add(Item("souwester"));
            return o;
        }

        static readonly string[] Pastels = { "#E57373", "#9DB7C9", "#7FA36B", "#F2C14E", "#C9B4E8", "#F5A3C1", "#5B7FA8", "#E9A93E", "#8FC3E6" };

        static OutfitItem Recolor(OutfitItem it, System.Random r)
        {
            if (it.styles.Count > 0)
            {
                var s = it.styles[0].Clone();
                s.c1 = StyleData.Hex(Pastels[r.Next(Pastels.Length)]);
                it.styles[0] = s;
            }
            return it;
        }
    }
}
