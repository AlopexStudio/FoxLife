using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    [Flags]
    public enum ActTag { None = 0, Creative = 1, Routine = 2, Tidy = 4, Solitary = 8, Comfort = 16 }

    public enum Pose { Stand, Use, Sit, SitFloor, Lie, Bath, Talk }

    public enum Need { Hunger = 0, Energy = 1, Hygiene = 2, Fun = 3, Social = 4 }

    /// <summary>Действие с предметом.</summary>
    public class Interaction
    {
        public string name;
        public string verb;              // что показывать в панели: «Читает»
        public float duration = 30;      // игровые минуты
        public Pose pose = Pose.Use;
        public bool onObject;            // лиса забирается на предмет (кровать, диван, ванна)
        public bool sleep;
        public bool endWhenFull;
        public float hunger, energy, hygiene, fun, social; // изменение нужды в минуту
        public ActTag tags;
        public bool makesPlate, removesObject;
        public float cooldown;           // сколько игровых минут после использования действие не интересно
        public Skill skill = Skill.None; // какой навык растёт
        public string hold;              // что держит во рту: plate, book, food, ball
        public string carryTo;           // отнести предмет к другому предмету (тарелку — к раковине)
        public LifeStage minStage = LifeStage.Kit;
        public LifeStage maxStage = LifeStage.Elder;
        public int slotOnly = -1;
        public bool fromSeat;
        public float hygieneCap = 100f;  // выше этого гигиену так не поднять (умыться — не то же, что помыться)            // смотреть с дивана или кресла, если они стоят напротив
        public bool openWardrobe;
        public bool openBuild;           // набросать план перестройки: открывает строительство, пока игрок не закончит        // открыть выбор одежды и ждать, пока игрок не закончит        // только это место (у прилавка: 0 — покупатель, 1 — продавец)
        public bool playerOnly;          // только по команде игрока
        public System.Func<Fox, Furniture, string> blocked; // почему сейчас нельзя (null — можно)
        public float skillRate = 1f;     // опыта в минуту
        public string bubble;
        public Action<Fox> onFinish;

        public float Gain(Need n)
        {
            switch (n)
            {
                case Need.Hunger: return hunger;
                case Need.Energy: return energy;
                case Need.Hygiene: return hygiene;
                case Need.Fun: return fun;
                default: return social;
            }
        }
    }

    public class UseSlot
    {
        public List<Vector2Int> stands = new List<Vector2Int>(); // локальные клетки, где стоит лиса
        public Vector3 pose;  // локальная позиция «на предмете»
        public float yaw;
    }

    public class FurnitureDef
    {
        public string id, name, category, description;
        public int w = 1, d = 1;
        public bool walkable;
        public bool buildable = true;
        public bool stair;               // лестница на этаж выше
        public int price;
        public List<UseSlot> slots = new List<UseSlot>();
        public List<Interaction> actions = new List<Interaction>();
        public Action<Furniture, Styleable, Transform> build;
    }

    public class Furniture : MonoBehaviour
    {
        public FurnitureDef def;
        public int rot;
        public Vector2Int anchor;
        public Styleable style;
        public Transform model;
        public Fox[] occupants;
        public bool isGhost;
        public Light lamp;
        /// <summary>Яркость лампы 0..1 (0.5 — как задумано) и цвет её света.</summary>
        public float lightLevel = 0.5f;
        public Color lightColor = new Color(1f, 0.82f, 0.58f);
        float baseIntensity = -1f, baseRange;

        public void ApplyLight()
        {
            if (lamp == null) return;
            if (baseIntensity < 0) { baseIntensity = lamp.intensity; baseRange = lamp.range; if (def.id == "campfire") lightColor = lamp.color; }
            lamp.intensity = baseIntensity * lightLevel * 2f * Props.LightK;
            lamp.range = baseRange * Mathf.Lerp(0.6f, 1.35f, lightLevel);
            if (def.id != "campfire") lamp.color = lightColor;
        }
        public int screenPart = -1;
        public int inUse;
        public float lastFinished = -99999f;
        public int level;
        public int lot = -1;   // участок, на котором стоит
        /// <summary>Сетевой номер предмета (одинаковый у всех игроков; 0 — не в сетевой игре).</summary>
        public int nid;
        public float boughtAt = -1f;   // когда куплен (игровые минуты); -1 — давно

        /// <summary>Доворот к 90° (только у построек города: мосты, фонари, статуи можно поставить наискосок).</summary>
        public float fineYaw;
        public bool FineRotate => def.id.StartsWith("p_");

        public Vector2Int Eff => Size(def, rot);
        public static Vector2Int Size(FurnitureDef d, int rot) => rot % 2 == 0 ? new Vector2Int(d.w, d.d) : new Vector2Int(d.d, d.w);

        public void Place(Vector2Int a, int r)
        {
            anchor = a;
            rot = ((r % 4) + 4) % 4;
            var e = Eff;
            float y = Game.I != null && Game.I.house != null && Game.I.house.terrain != null ? Game.I.house.FurnitureY(def, a, rot, level) : House.LevelY(level);
            transform.position = new Vector3(a.x + e.x / 2f, y, a.y + e.y / 2f);
            transform.rotation = Quaternion.Euler(0, 90 * rot + fineYaw, 0);
        }

        public Vector2Int LocalToCell(Vector2Int local)
        {
            var l = new Vector3(local.x + 0.5f - def.w / 2f, 0, local.y + 0.5f - def.d / 2f);
            var w = transform.position + transform.rotation * l;
            return new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.z));
        }

        public static IEnumerable<Vector2Int> FootprintAt(FurnitureDef def, Vector2Int a, int rot)
        {
            var e = Size(def, rot);
            for (int x = 0; x < e.x; x++)
                for (int z = 0; z < e.y; z++)
                    yield return new Vector2Int(a.x + x, a.y + z);
        }

        public IEnumerable<Vector2Int> Footprint() => FootprintAt(def, anchor, rot);

        public Vector3 PosePos(int s) => transform.TransformPoint(def.slots[s].pose);
        public Quaternion PoseRot(int s) => transform.rotation * Quaternion.Euler(0, def.slots[s].yaw, 0);

        public void SetColliders(bool on)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        public bool HasFreeSlot(Fox f)
        {
            if (def.slots.Count == 0) return false;
            for (int i = 0; i < occupants.Length; i++) if (occupants[i] == null || occupants[i] == f) return true;
            return false;
        }

        /// <summary>Ищет свободное место и ближайшую достижимую клетку, откуда им пользоваться.</summary>
        public bool FindSlot(Fox f, House house, out int slot, out Vector2Int stand, out List<Vector3> path, int only = -1)
        {
            slot = -1; stand = default; path = null;
            float best = float.MaxValue;
            var grid = house.grids[level];
            var from = f.Cell;
            for (int s = 0; s < def.slots.Count; s++)
            {
                if (occupants[s] != null && occupants[s] != f) continue;
                if (only >= 0 && s != only) continue;
                foreach (var st in def.slots[s].stands)
                {
                    var cell = LocalToCell(st);
                    if (!grid.InBounds(cell)) continue;
                    var occ = grid.At(cell);
                    if (occ != null && occ != this) continue;
                    if (occ == this && !def.walkable) continue;
                    if (!grid.HasFloorAt(cell) || Fox.Taken(f, level, cell)) continue;
                    if (ThroughWall(grid, cell)) continue; // нельзя пользоваться предметом через стену
                    var p = house.FindPath(f.Level, from, level, cell);
                    if (p == null) continue;
                    if (p.Count < best) { best = p.Count; slot = s; stand = cell; path = p; }
                }
            }
            return slot >= 0;
        }

        /// <summary>Отделена ли клетка от предмета стеной, окном или забором (соседняя клетка предмета за постройкой).</summary>
        bool ThroughWall(GridMap grid, Vector2Int cell)
        {
            bool touching = false;
            foreach (var c in Footprint())
            {
                if (c == cell) return false;
                var d = c - cell;
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) != 1) continue;
                touching = true;
                if (!grid.HasStructure(cell, c)) return false;
            }
            return touching;
        }

        void Update()
        {
            if (screenPart >= 0 && style != null)
            {
                // картинку показывает MediaFX (воксельные пиксели); сам экран остаётся тёмным
            }
        }
    }

    /// <summary>Каталог мебели. Вся мебель собрана из простых форм и раскрашивается инструментом стиля.</summary>
    public static class Catalog
    {
        public static readonly string[] Categories = { "Спальня", "Кухня", "Гостиная", "Ванная", "Развлечения", "Декор", "Сад", "Дом" };
        /// <summary>В строительстве города есть ещё «Город» и «Природа» — всё, из чего собрана Лисья Долина.</summary>
        public static readonly string[] TownCategories = { "Спальня", "Кухня", "Гостиная", "Ванная", "Развлечения", "Декор", "Сад", "Дом", "Город", "Природа" };

        /// <summary>Глава романа: скромный гонорар (больше двух глав в день — за полцены), каждая пятая — книга издана.</summary>
        public static void FinishChapter(Fox f)
        {
            int c = f.SkillLevel(Skill.Creativity);
            int day = Game.I.Day;
            if (f.Tally("chapter_day") != day) { f.tally["chapter_day"] = day; f.tally["chapter_today"] = 0; }
            f.Count("chapter_today");
            float k = f.Tally("chapter_today") > 2 ? 0.5f : 1f;
            Game.I.AddMoneyFor(f, Mathf.RoundToInt((10 + c * 9) * k), $"{f.foxName}: гонорар за главу романа");
            f.Count("chapter");
            if (f.Tally("chapter") % 5 == 0)
            {
                f.Count("novel");
                int bonus = 120 + c * 45;
                Game.I.AddMoneyFor(f, bonus, $"{f.foxName}: роман издан!");
                f.AddMoodlet("Мой роман издан!", 15, 1440);
                f.Remember($"Издан роман №{f.Tally("novel")}");
                f.Say("Мою книгу напечатали!");
            }
        }

        static FurnitureDef TownProp(string id, string name, string cat, int w, int d, System.Action<Transform, Vector3, float> make, bool walkable = false, string desc = null)
        {
            return new FurnitureDef
            {
                id = id, name = name, category = cat, w = w, d = d, walkable = walkable, description = desc,
                build = (f, st, m) =>
                {
                    st.displayName = name;
                    make(m, m.position, m.eulerAngles.y);
                    foreach (var r in m.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                },
            };
        }

        static void AddTownProps(List<FurnitureDef> all)
        {
            const string C = "Город", N = "Природа";
            Vector3 Fwd(float yaw, float d) => Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, d);
            all.Add(TownProp("p_clock", "Часовая башня", C, 3, 3, (r, p, y) => Props.ClockTower(r, p, y + 180f)));
            all.Add(TownProp("p_mill", "Мельница", C, 6, 6, (r, p, y) => Props.Windmill(r, p, y)));
            all.Add(TownProp("p_well", "Колодец", C, 2, 2, (r, p, y) => Props.Well(r, p, y)));
            all.Add(TownProp("p_lamp", "Уличный фонарь", C, 1, 1, (r, p, y) => Props.StreetLamp(r, p, y)));
            all.Add(TownProp("p_statue", "Статуя лисы", C, 1, 1, (r, p, y) => Props.FoxStatue(r, p, y)));
            all.Add(TownProp("p_statue2", "Статуя лисы с книгой", C, 1, 1, (r, p, y) => Props.FoxStatue(r, p, y, true)));
            all.Add(TownProp("p_dome", "Купол обсерватории", C, 4, 4, (r, p, y) => Props.Dome(r, p), false, "Можно поставить на землю или на крышу постройки"));
            all.Add(TownProp("p_tent", "Палатка", C, 3, 3, (r, p, y) => Props.Tent(r, p, y, "#E9A93E")));
            all.Add(TownProp("p_tent2", "Зелёная палатка", C, 3, 3, (r, p, y) => Props.Tent(r, p, y, "#7FA36B")));
            all.Add(TownProp("p_stall", "Прилавок с навесом", C, 2, 1, (r, p, y) => Props.MarketStall(r, p, y, UnityEngine.Random.Range(0, 9))));
            all.Add(TownProp("p_parasol", "Зонтик от солнца", C, 1, 1, (r, p, y) => Props.Parasol(r, p, "#E57373")));
            all.Add(TownProp("p_parasol2", "Голубой зонтик", C, 1, 1, (r, p, y) => Props.Parasol(r, p, "#64B5F6")));
            all.Add(TownProp("p_pole", "Столб для гирлянды", C, 1, 1, (r, p, y) => Props.Pole(r, p, 3.4f)));
            all.Add(TownProp("p_bunting", "Гирлянда флажков", C, 6, 1, (r, p, y) =>
            {
                var a = p + Quaternion.Euler(0, y, 0) * new Vector3(-2.8f, 3.2f, 0);
                var b = p + Quaternion.Euler(0, y, 0) * new Vector3(2.8f, 3.2f, 0);
                Props.Pole(r, a - Vector3.up * 3.2f, 3.4f); Props.Pole(r, b - Vector3.up * 3.2f, 3.4f);
                Props.Bunting(r, a + Vector3.up * 0.2f, b + Vector3.up * 0.2f);
            }, false, "Два столба и флажки между ними"));
            all.Add(TownProp("p_sign", "Вывеска кафе", C, 2, 1, (r, p, y) => Props.Sign(r, p + Vector3.up * 2f, y, 0)));
            all.Add(TownProp("p_sign2", "Вывеска библиотеки", C, 2, 1, (r, p, y) => Props.Sign(r, p + Vector3.up * 2f, y, 1)));
            all.Add(TownProp("p_awning", "Маркиза", C, 3, 1, (r, p, y) => Props.Awning(r, p + Vector3.up * 1.9f, y, 3.2f, "#E57373")));
            all.Add(TownProp("p_mailbox", "Почтовый ящик", C, 1, 1, (r, p, y) => Props.Mailbox(r, p, y, "#6FA3D9")));
            all.Add(TownProp("p_arch", "Цветочная арка", C, 1, 1, (r, p, y) => Props.FlowerArch(r, p, y), true));
            all.Add(TownProp("p_hedge", "Живая изгородь", C, 4, 1, (r, p, y) => Props.Hedge(r, p, y, 3.8f, UnityEngine.Random.Range(0, 999))));
            all.Add(TownProp("p_bridge", "Каменный мост", C, 3, 8, (r, p, y) => Props.Bridge(r, p, y, 8f, 2.6f, false), true, "Через ручей или овраг"));
            all.Add(TownProp("p_bridge2", "Деревянный мост", C, 2, 6, (r, p, y) => Props.Bridge(r, p, y, 6f, 1.8f, true), true));
            all.Add(TownProp("p_ancient", "Древнее дерево", N, 7, 7, (r, p, y) => Props.AncientTree(r, p - Vector3.up * 0.2f), false, "Огромное, с дверцей, окошком и фонариками"));
            all.Add(TownProp("p_bush", "Куст", N, 1, 1, (r, p, y) => Props.Bush(r, p, UnityEngine.Random.Range(0, 999))));
            all.Add(TownProp("p_rock", "Камень", N, 1, 1, (r, p, y) => Props.Rock(r, p - Vector3.up * 0.1f, UnityEngine.Random.Range(0, 99), 0.9f)));
            all.Add(TownProp("p_boulder", "Валун", N, 2, 2, (r, p, y) => Props.Rock(r, p - Vector3.up * 0.15f, UnityEngine.Random.Range(0, 99), 2f)));
            all.Add(TownProp("p_log", "Поваленное бревно", N, 2, 1, (r, p, y) => Props.Log(r, p, y)));
            all.Add(TownProp("p_mush", "Грибы", N, 1, 1, (r, p, y) => Props.Mushrooms(r, p, UnityEngine.Random.Range(0, 99)), true));
            all.Add(TownProp("p_lily", "Кувшинки", N, 2, 2, (r, p, y) => Props.LilyPads(r, p + Vector3.up * 0.02f, UnityEngine.Random.Range(0, 99)), true, "Для пруда или ручья"));
        }

        static List<FurnitureDef> all;
        public static List<FurnitureDef> All { get { if (all == null) Build(); return all; } }

        /// <summary>Поесть за столом: садится на стул, ест, оставляет тарелку на столе.</summary>
        public static readonly Interaction EatAtTable = new Interaction
        {
            name = "Поесть за столом", verb = "Ест за столом", duration = 14, pose = Pose.Sit, onObject = true,
            hunger = 0.9f, fun = 0.12f, social = 0.06f, hold = "food", bubble = "Приятного аппетита!",
        };

        /// <summary>Высота поверхности, на которую можно поставить тарелку.</summary>
        public static float SurfaceHeight(string id)
        {
            switch (id)
            {
                case "table": return 0.735f;
                case "counter": return 0.86f;
                case "cafecounter": return 0.95f;
                case "schooldesk": return 0.58f;
                case "computer": return 0.63f;
                case "nightstand": return 0.5f;
                case "dresser": return 0.8f;
                default: return -1f;
            }
        }

        /// <summary>Брусок (или цилиндр) от точки a до точки b.</summary>
        static void Seg(Styleable st, int part, Transform m, Vector3 a, Vector3 b, float thick, bool round = false)
        {
            var d = b - a;
            var e = Quaternion.FromToRotation(Vector3.up, d.normalized).eulerAngles;
            if (round) Shapes.Cyl(st, part, m, (a + b) * 0.5f, thick, d.magnitude, e);
            else Shapes.Box(st, part, m, (a + b) * 0.5f, V(thick, d.magnitude, thick), e);
        }

        public static FurnitureDef Get(string id)
        {
            foreach (var d in All) if (d.id == id) return d;
            return null;
        }

        // ---------- короткие помощники ----------
        static Vector2Int C(int x, int z) => new Vector2Int(x, z);
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static UseSlot Slot(Vector3 pose, float yaw, params Vector2Int[] stands)
        {
            var s = new UseSlot { pose = pose, yaw = yaw };
            s.stands.AddRange(stands);
            return s;
        }

        static void Mood(Fox f, bool cond, string name, float value, float minutes = 180)
        {
            if (cond) f.AddMoodlet(name, value, minutes);
        }

        static void Build()
        {
            all = new List<FurnitureDef>();
            BuildDefs();
            // цены
            var prices = new Dictionary<string, int>
            {
                { "bed", 300 }, { "doublebed", 550 }, { "foxbed", 120 }, { "wardrobe", 250 }, { "fridge", 600 }, { "stove", 450 },
                { "counter", 150 }, { "sink", 200 }, { "table", 220 }, { "chair", 60 }, { "sofa", 500 }, { "armchair", 280 },
                { "tv", 400 }, { "bookshelf", 220 }, { "easel", 180 }, { "guitar", 260 }, { "computer", 700 }, { "ball", 15 },
                { "bath", 650 }, { "plant", 40 }, { "lamp", 90 }, { "rug", 150 }, { "roundrug", 90 }, { "stairs", 400 },
                { "dresser", 220 }, { "nightstand", 90 }, { "pouf", 70 }, { "painting", 120 }, { "clock", 60 }, { "vase", 45 },
                { "bench", 160 }, { "gardenlamp", 110 }, { "swing", 320 }, { "flowerbed", 60 }, { "birdbath", 140 }, { "fountain", 900 }, { "cafecounter", 700 },
                { "telescope", 600 }, { "bridge", 350 }, { "piano", 900 }, { "schooldesk", 120 }, { "chalkboard", 150 }, { "marketstall", 400 }, { "campfire", 250 }, { "berrybush", 80 },
            };
            AddTownProps(all);
            foreach (var d in all) if (prices.TryGetValue(d.id, out var pr)) d.price = pr;
            // навыки
            Sk("Приготовить ужин", Skill.Cooking, 1f); Sk("Перекусить", Skill.Cooking, 0.15f);
            Sk("Рисовать", Skill.Creativity, 1f); Sk("Писать роман", Skill.Creativity, 0.8f);
            Sk("Играть на гитаре", Skill.Music, 1f);
            Sk("Почитать", Skill.Logic, 0.8f); Sk("Сидеть в интернете", Skill.Logic, 0.3f);
            Sk("Помыть посуду", Skill.Housework, 1.5f); Sk("Полить", Skill.Housework, 1f);
        }

        static void Sk(string action, Skill s, float rate)
        {
            foreach (var d in all)
                foreach (var it in d.actions)
                    if (it.name == action) { it.skill = s; it.skillRate = rate; }
        }

        static void BuildDefs()
        {

            // ===== Спальня =====
            var sleep = new Interaction
            {
                name = "Спать", verb = "Спит", duration = 600, pose = Pose.Lie, onObject = true, sleep = true, endWhenFull = true,
                energy = 0.15f, tags = ActTag.Comfort, bubble = "Спокойной ночи",
                onFinish = f => Mood(f, f.needs[(int)Need.Energy] > 90, "Выспаться удалось", 8, 240),
            };
            var nap = new Interaction
            {
                name = "Вздремнуть", verb = "Дремлет", duration = 60, pose = Pose.Lie, onObject = true, sleep = true,
                energy = 0.26f, tags = ActTag.Comfort, bubble = "Пять минуточек…",
            };

            all.Add(new FurnitureDef
            {
                id = "bed", name = "Кровать", category = "Спальня", w = 1, d = 2,
                description = "Сон восстанавливает бодрость.",
                slots = { Slot(V(0, 0.46f, -0.05f), 0, C(1, 0), C(-1, 0), C(1, 1), C(-1, 1), C(0, -1)) },
                actions = { sleep, nap },
                build = (f, st, m) =>
                {
                    st.AddPart("Каркас", StyleData.Make("Доски", "#8B5A2B"));
                    st.AddPart("Одеяло", StyleData.Make("Клетка", "#4F7CAC", "#DDE6F0"));
                    st.AddPart("Матрас и подушка", StyleData.Make("Ткань", "#F2EEE6"));
                    Shapes.Box(st, 0, m, V(0, 0.18f, 0), V(0.92f, 0.22f, 1.9f));
                    Shapes.Box(st, 0, m, V(0, 0.45f, 0.93f), V(0.92f, 0.72f, 0.08f));
                    Shapes.Box(st, 0, m, V(0, 0.3f, -0.93f), V(0.92f, 0.42f, 0.08f));
                    Shapes.Box(st, 2, m, V(0, 0.36f, 0), V(0.84f, 0.12f, 1.78f));
                    Shapes.Box(st, 1, m, V(0, 0.44f, -0.28f), V(0.88f, 0.07f, 1.22f));
                    Shapes.Box(st, 2, m, V(0, 0.47f, 0.64f), V(0.6f, 0.1f, 0.3f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "doublebed", name = "Двуспальная кровать", category = "Спальня", w = 2, d = 2,
                description = "Места хватит на двоих.",
                slots =
                {
                    Slot(V(-0.45f, 0.46f, -0.05f), 0, C(-1, 0), C(-1, 1), C(0, -1)),
                    Slot(V(0.45f, 0.46f, -0.05f), 0, C(2, 0), C(2, 1), C(1, -1)),
                },
                actions = { sleep, nap },
                build = (f, st, m) =>
                {
                    st.AddPart("Каркас", StyleData.Make("Дерево", "#6E4A2E"));
                    st.AddPart("Одеяло", StyleData.Make("Ромбы", "#B85C5C", "#F0D9C6"));
                    st.AddPart("Матрас и подушки", StyleData.Make("Ткань", "#F5F0E8"));
                    Shapes.Box(st, 0, m, V(0, 0.18f, 0), V(1.92f, 0.22f, 1.9f));
                    Shapes.Box(st, 0, m, V(0, 0.5f, 0.93f), V(1.92f, 0.8f, 0.08f));
                    Shapes.Box(st, 0, m, V(0, 0.3f, -0.93f), V(1.92f, 0.42f, 0.08f));
                    Shapes.Box(st, 2, m, V(0, 0.36f, 0), V(1.84f, 0.12f, 1.78f));
                    Shapes.Box(st, 1, m, V(0, 0.44f, -0.28f), V(1.88f, 0.07f, 1.22f));
                    Shapes.Box(st, 2, m, V(-0.45f, 0.47f, 0.64f), V(0.7f, 0.1f, 0.3f));
                    Shapes.Box(st, 2, m, V(0.45f, 0.47f, 0.64f), V(0.7f, 0.1f, 0.3f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "foxbed", name = "Лежанка", category = "Спальня", w = 1, d = 1,
                description = "Круглая мягкая лежанка, чтобы свернуться клубочком.",
                slots = { Slot(V(0, 0.08f, 0), 0, C(0, -1), C(1, 0), C(-1, 0), C(0, 1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Свернуться клубочком", verb = "Лежит клубочком", duration = 90, pose = Pose.Lie, onObject = true, sleep = true,
                        energy = 0.2f, fun = 0.1f, tags = ActTag.Comfort | ActTag.Solitary, bubble = "Уютно",
                        onFinish = f => f.AddMoodlet("Уютненько", 5, 120),
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Бортик", StyleData.Make("Вельвет", "#9C6B4E"));
                    st.AddPart("Подушка", StyleData.Make("Лисьи следы", "#F3E3CC", "#C98B5B"));
                    Shapes.Cyl(st, 0, m, V(0, 0.08f, 0), 0.9f, 0.16f);
                    Shapes.Cyl(st, 1, m, V(0, 0.1f, 0), 0.7f, 0.14f);
                },
            });

            all.Add(new FurnitureDef
            {
                id = "wardrobe", name = "Шкаф", category = "Спальня", w = 1, d = 1,
                description = "Одежда на все случаи. Можно подобрать новый наряд.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1)) },
                actions =
                {
                    new Interaction { name = "Выбрать наряд", verb = "Выбирает наряд", duration = 9999, playerOnly = true, openWardrobe = true, bubble = "Что бы надеть?" },
                    new Interaction { name = "Подобрать новый наряд", verb = "Примеряет наряды", duration = 15, fun = 0.5f, tags = ActTag.Creative, cooldown = 1440, bubble = "Как мне это?",
                        onFinish = fx => { fx.NewLook(); Mood(fx, true, "Обновка!", 5, 360); } },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Корпус", StyleData.Make("Дерево", "#A97C50"));
                    st.AddPart("Ручки", StyleData.Make("Гладкий", "#D4B26A", null, 1, 0.6f));
                    Shapes.Box(st, 0, m, V(0, 0.85f, 0.05f), V(0.9f, 1.7f, 0.6f));
                    Shapes.Box(st, 0, m, V(0, 0.85f, -0.255f), V(0.01f, 1.6f, 0.01f));
                    Shapes.Box(st, 1, m, V(-0.07f, 0.95f, -0.27f), V(0.04f, 0.2f, 0.04f));
                    Shapes.Box(st, 1, m, V(0.07f, 0.95f, -0.27f), V(0.04f, 0.2f, 0.04f));
                },
            });

            // ===== Кухня =====
            all.Add(new FurnitureDef
            {
                id = "fridge", name = "Холодильник", category = "Кухня",
                description = "Быстро перекусить.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Перекусить", verb = "Перекусывает", duration = 25, hunger = 1.4f, makesPlate = true, bubble = "Ням!", hold = "food",
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Корпус", StyleData.Make("Гладкий", "#E9EEF0", null, 1, 0.55f));
                    st.AddPart("Ручки", StyleData.Make("Гладкий", "#9AA3A8", null, 1, 0.7f));
                    Shapes.Box(st, 0, m, V(0, 0.8f, 0.02f), V(0.82f, 1.6f, 0.76f));
                    Shapes.Box(st, 1, m, V(0, 1.05f, -0.365f), V(0.82f, 0.015f, 0.01f));
                    Shapes.Box(st, 1, m, V(0.3f, 1.3f, -0.39f), V(0.05f, 0.35f, 0.05f));
                    Shapes.Box(st, 1, m, V(0.3f, 0.7f, -0.39f), V(0.05f, 0.3f, 0.05f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "stove", name = "Плита", category = "Кухня",
                description = "Приготовить настоящий ужин — сытнее, чем перекус.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Приготовить ужин", verb = "Готовит", duration = 40, hunger = 2.1f, makesPlate = true, bubble = "Пахнет вкусно!",
                        minStage = LifeStage.Teen,
                        onFinish = f =>
                        {
                            float burn = 0.3f - f.SkillLevel(Skill.Cooking) * 0.06f;
                            if (UnityEngine.Random.value < burn)
                            {
                                f.needs[(int)Need.Hunger] = Mathf.Max(0, f.needs[(int)Need.Hunger] - 35);
                                f.AddMoodlet("Ужин подгорел", -5, 180);
                                f.Say("Подгорело!");
                            }
                            else Mood(f, true, f.SkillLevel(Skill.Cooking) >= 6 ? "Ужин от шефа" : "Вкусный ужин", 8 + f.SkillLevel(Skill.Cooking), 240);
                        },
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Корпус", StyleData.Make("Гладкий", "#F3F1EC", null, 1, 0.4f));
                    st.AddPart("Варочная панель", StyleData.Make("Гладкий", "#2B2B2B", null, 1, 0.7f));
                    st.AddPart("Ручка", StyleData.Make("Гладкий", "#B9B9B9", null, 1, 0.7f));
                    Shapes.Box(st, 0, m, V(0, 0.425f, 0.02f), V(0.82f, 0.85f, 0.76f));
                    Shapes.Box(st, 1, m, V(0, 0.865f, 0.02f), V(0.82f, 0.03f, 0.76f));
                    for (int i = 0; i < 4; i++)
                        Shapes.Cyl(st, 2, m, V(i % 2 == 0 ? -0.2f : 0.2f, 0.885f, i < 2 ? -0.16f : 0.2f), 0.22f, 0.012f);
                    Shapes.Box(st, 1, m, V(0, 0.42f, -0.36f), V(0.62f, 0.4f, 0.02f));
                    Shapes.Box(st, 2, m, V(0, 0.7f, -0.39f), V(0.55f, 0.04f, 0.04f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "counter", name = "Кухонная тумба", category = "Кухня",
                description = "Столешница с шкафчиком.",
                build = (f, st, m) =>
                {
                    st.AddPart("Шкафчик", StyleData.Make("Дерево", "#C8A27A"));
                    st.AddPart("Столешница", StyleData.Make("Мрамор", "#EDEDED", "#9C9C9C", 1, 0.5f));
                    st.AddPart("Ручка", StyleData.Make("Гладкий", "#8C8C8C", null, 1, 0.7f));
                    Shapes.Box(st, 0, m, V(0, 0.4f, 0.03f), V(0.96f, 0.8f, 0.72f));
                    Shapes.Box(st, 1, m, V(0, 0.825f, 0), V(1f, 0.05f, 0.8f));
                    Shapes.Box(st, 2, m, V(0, 0.65f, -0.34f), V(0.3f, 0.03f, 0.03f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "sink", name = "Раковина", category = "Кухня",
                description = "Умыться можно и здесь.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1)) },
                actions =
                {
                    new Interaction { name = "Умыться", verb = "Умывается", duration = 15, hygiene = 1.6f, hygieneCap = 70f, endWhenFull = true, bubble = "Плюх" },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Шкафчик", StyleData.Make("Дерево", "#C8A27A"));
                    st.AddPart("Столешница", StyleData.Make("Мрамор", "#EDEDED", "#9C9C9C", 1, 0.5f));
                    st.AddPart("Металл", StyleData.Make("Гладкий", "#B0B8BF", null, 1, 0.8f));
                    Shapes.Box(st, 0, m, V(0, 0.4f, 0.03f), V(0.96f, 0.8f, 0.72f));
                    Shapes.Box(st, 1, m, V(0, 0.825f, 0), V(1f, 0.05f, 0.8f));
                    Shapes.Box(st, 2, m, V(0, 0.853f, -0.02f), V(0.5f, 0.012f, 0.4f));
                    Shapes.Cyl(st, 2, m, V(0, 0.95f, 0.26f), 0.05f, 0.2f);
                    Shapes.Box(st, 2, m, V(0, 1.04f, 0.18f), V(0.04f, 0.04f, 0.18f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "table", name = "Обеденный стол", category = "Кухня", w = 2, d = 1,
                build = (f, st, m) =>
                {
                    st.AddPart("Столешница", StyleData.Make("Дерево", "#A0703C"));
                    st.AddPart("Ножки", StyleData.Make("Дерево", "#7A5230"));
                    Shapes.Box(st, 0, m, V(0, 0.7f, 0), V(1.9f, 0.06f, 0.9f));
                    for (int i = 0; i < 4; i++)
                        Shapes.Cyl(st, 1, m, V(i % 2 == 0 ? -0.85f : 0.85f, 0.335f, i < 2 ? -0.36f : 0.36f), 0.07f, 0.67f);
                },
            });

            all.Add(new FurnitureDef
            {
                id = "chair", name = "Стул", category = "Кухня",
                slots = { Slot(V(0, 0.48f, -0.02f), 180, C(0, -1), C(1, 0), C(-1, 0)) },
                actions =
                {
                    new Interaction { name = "Посидеть", verb = "Сидит", duration = 30, pose = Pose.Sit, onObject = true, energy = 0.05f, fun = 0.1f },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Дерево", StyleData.Make("Дерево", "#A0703C"));
                    st.AddPart("Сиденье", StyleData.Make("Клетка", "#C65B3A", "#F0D9B5"));
                    Shapes.Box(st, 1, m, V(0, 0.45f, 0), V(0.46f, 0.06f, 0.46f));
                    for (int i = 0; i < 4; i++)
                        Shapes.Box(st, 0, m, V(i % 2 == 0 ? -0.19f : 0.19f, 0.21f, i < 2 ? -0.19f : 0.19f), V(0.05f, 0.42f, 0.05f));
                    Shapes.Box(st, 0, m, V(0, 0.74f, 0.21f), V(0.46f, 0.52f, 0.05f));
                },
            });

            // ===== Гостиная =====
            var rest = new Interaction
            {
                name = "Отдохнуть", verb = "Отдыхает", duration = 45, pose = Pose.Sit, onObject = true,
                energy = 0.08f, fun = 0.35f, tags = ActTag.Comfort,
            };
            all.Add(new FurnitureDef
            {
                id = "sofa", name = "Диван", category = "Гостиная", w = 2, d = 1,
                description = "Отдохнуть или вздремнуть.",
                slots =
                {
                    Slot(V(-0.45f, 0.42f, -0.05f), 180, C(0, -1)),
                    Slot(V(0.45f, 0.42f, -0.05f), 180, C(1, -1)),
                },
                actions = { rest, nap },
                build = (f, st, m) =>
                {
                    st.AddPart("Каркас", StyleData.Make("Гладкий", "#6B4F3A"));
                    st.AddPart("Обивка", StyleData.Make("Вельвет", "#C65B3A"));
                    st.AddPart("Подушки", StyleData.Make("Горошек", "#F0D9B5", "#C65B3A"));
                    Shapes.Box(st, 0, m, V(0, 0.16f, 0), V(1.9f, 0.16f, 0.86f));
                    for (int i = 0; i < 4; i++)
                        Shapes.Box(st, 0, m, V(i % 2 == 0 ? -0.86f : 0.86f, 0.04f, i < 2 ? -0.36f : 0.36f), V(0.08f, 0.08f, 0.08f));
                    Shapes.Box(st, 1, m, V(0, 0.32f, -0.04f), V(1.62f, 0.18f, 0.78f));
                    Shapes.Box(st, 1, m, V(0, 0.58f, 0.33f), V(1.9f, 0.55f, 0.2f));
                    Shapes.Box(st, 1, m, V(-0.87f, 0.42f, 0), V(0.16f, 0.4f, 0.86f));
                    Shapes.Box(st, 1, m, V(0.87f, 0.42f, 0), V(0.16f, 0.4f, 0.86f));
                    Shapes.Box(st, 2, m, V(-0.55f, 0.6f, 0.18f), V(0.36f, 0.32f, 0.1f), V(-12, 8, 0));
                    Shapes.Box(st, 2, m, V(0.55f, 0.6f, 0.18f), V(0.36f, 0.32f, 0.1f), V(-12, -8, 0));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "armchair", name = "Кресло", category = "Гостиная",
                slots = { Slot(V(0, 0.42f, -0.05f), 180, C(0, -1)) },
                actions = { rest },
                build = (f, st, m) =>
                {
                    st.AddPart("Каркас", StyleData.Make("Гладкий", "#6B4F3A"));
                    st.AddPart("Обивка", StyleData.Make("Кожа", "#8E5B3C"));
                    Shapes.Box(st, 0, m, V(0, 0.16f, 0), V(0.86f, 0.16f, 0.86f));
                    Shapes.Box(st, 1, m, V(0, 0.32f, -0.04f), V(0.6f, 0.18f, 0.78f));
                    Shapes.Box(st, 1, m, V(0, 0.6f, 0.33f), V(0.86f, 0.6f, 0.2f));
                    Shapes.Box(st, 1, m, V(-0.36f, 0.42f, 0), V(0.14f, 0.4f, 0.86f));
                    Shapes.Box(st, 1, m, V(0.36f, 0.42f, 0), V(0.14f, 0.4f, 0.86f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "tv", name = "Телевизор", category = "Развлечения",
                description = "Привычное развлечение. Особенно нравится тем, кто не любит перемен.",
                slots =
                {
                    Slot(V(0, 0, -2f), 0, C(0, -2), C(-1, -2), C(1, -2), C(0, -3)),
                    Slot(V(0, 0, -2f), 0, C(1, -2), C(-1, -2), C(0, -3), C(1, -3)),
                    Slot(V(0, 0, -2f), 0, C(-1, -2), C(-1, -3), C(0, -3), C(1, -3)),
                    Slot(V(0, 0, -2f), 0, C(0, -3), C(1, -3), C(-1, -3), C(0, -4)),
                },
                actions =
                {
                    new Interaction
                    {
                        name = "Смотреть телевизор", verb = "Смотрит телевизор", duration = 60, pose = Pose.SitFloor, fun = 0.9f, fromSeat = true,
                        tags = ActTag.Routine, bubble = "О, моя передача!",
                        onFinish = f =>
                        {
                            Mood(f, f.pers.O <= 4, "Любимая передача", 7);
                            Mood(f, f.pers.O >= 9, "Опять одно и то же", -5);
                        },
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Тумба", StyleData.Make("Доски", "#5A4636"));
                    st.AddPart("Корпус", StyleData.Make("Гладкий", "#2A2A2E", null, 1, 0.6f));
                    st.AddPart("Экран", StyleData.Make("Гладкий", "#15181E", null, 1, 0.9f));
                    f.screenPart = 2;
                    Shapes.Box(st, 0, m, V(0, 0.2f, 0.05f), V(0.92f, 0.4f, 0.5f));
                    Shapes.Box(st, 1, m, V(0, 0.7f, 0.12f), V(0.86f, 0.52f, 0.06f));
                    Shapes.Box(st, 2, m, V(0, 0.7f, 0.085f), V(0.78f, 0.44f, 0.01f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "bookshelf", name = "Книжный шкаф", category = "Развлечения",
                description = "Чтение особенно радует любознательных.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Почитать", verb = "Читает", duration = 60, pose = Pose.SitFloor, fun = 0.7f,
                        tags = ActTag.Creative | ActTag.Solitary, bubble = "Интересно…", hold = "book",
                        onFinish = f =>
                        {
                            Mood(f, f.pers.O >= 7, "Интересная книга", 8);
                            Mood(f, f.pers.O <= 3, "Скучное чтиво", -4);
                        },
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Корпус", StyleData.Make("Доски", "#7A5230"));
                    st.AddPart("Книги", StyleData.Make("Полоска", "#B33A3A", "#2E5E8C", 4f));
                    Shapes.Box(st, 0, m, V(0, 0.8f, 0.3f), V(0.92f, 1.6f, 0.05f));
                    Shapes.Box(st, 0, m, V(-0.44f, 0.8f, 0.1f), V(0.05f, 1.6f, 0.44f));
                    Shapes.Box(st, 0, m, V(0.44f, 0.8f, 0.1f), V(0.05f, 1.6f, 0.44f));
                    float[] ys = { 0.03f, 0.43f, 0.83f, 1.23f, 1.6f };
                    foreach (var y in ys) Shapes.Box(st, 0, m, V(0, y, 0.1f), V(0.86f, 0.04f, 0.42f));
                    for (int i = 0; i < 4; i++)
                        Shapes.Box(st, 1, m, V(0, ys[i] + 0.17f, 0.12f), V(0.8f - (i % 2) * 0.2f, 0.3f, 0.32f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "easel", name = "Мольберт", category = "Развлечения",
                description = "Творчество для открытых новому.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Придумать дизайн", verb = "Придумывает дизайн", duration = 9999, playerOnly = true, openBuild = true, bubble = "А что, если здесь…",
                    },
                    new Interaction
                    {
                        name = "Рисовать", verb = "Рисует", duration = 60, fun = 0.8f, tags = ActTag.Creative | ActTag.Solitary,
                        bubble = "Вдохновение!",
                        blocked = (fx, e) => MediaFX.PaintBlocked(fx, e),
                        onFinish = f =>
                        {
                            Mood(f, f.pers.O >= 7, "Вдохновение", 10);
                            Mood(f, f.pers.O <= 3, "Ничего не выходит", -5);
                            MediaFX.FinishPainting(f, f.current != null ? f.current.furn : null);
                        },
                    },
                    new Interaction
                    {
                        name = "Выкинуть рисунок", verb = "Выкидывает рисунок", duration = 4, bubble = "Начнём с чистого листа.",
                        blocked = (fx, e) => MediaFX.DiscardBlocked(e),
                        onFinish = f => MediaFX.Discard(f, f.current != null ? f.current.furn : null),
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Дерево", StyleData.Make("Дерево", "#B58A5A"));
                    st.AddPart("Холст", StyleData.Make("Ткань", "#FAF7F0"));
                    Shapes.Box(st, 0, m, V(-0.25f, 0.65f, 0.05f), V(0.07f, 1.3f, 0.07f), V(8, 0, -8));
                    Shapes.Box(st, 0, m, V(0.25f, 0.65f, 0.05f), V(0.07f, 1.3f, 0.07f), V(8, 0, 8));
                    Shapes.Box(st, 0, m, V(0, 0.6f, 0.3f), V(0.07f, 1.2f, 0.07f), V(-20, 0, 0));
                    Shapes.Box(st, 0, m, V(0, 0.62f, -0.02f), V(0.65f, 0.04f, 0.08f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "guitar", name = "Гитара", category = "Развлечения",
                description = "Творчество. Те, кто рядом, тоже радуются хорошей музыке.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1), C(1, 0), C(-1, 0)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Играть на гитаре", verb = "Играет на гитаре", duration = 45, pose = Pose.SitFloor, fun = 0.85f,
                        tags = ActTag.Creative, bubble = "Трям-трям!",
                        onFinish = f =>
                        {
                            Mood(f, f.pers.O >= 6, "Музыка для души", 6);
                            foreach (var o in Game.I.foxes)
                                if (o != f && !o.sleeping && Vector3.Distance(o.transform.position, f.transform.position) < 5f)
                                    o.AddMoodlet(o.pers.O <= 3 ? "Шумно" : "Хорошая музыка", o.pers.O <= 3 ? -3 : 2 + f.SkillLevel(Skill.Music) * 0.6f, 120);
                        },
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Корпус", StyleData.Make("Дерево", "#C9873F"));
                    st.AddPart("Гриф и подставка", StyleData.Make("Гладкий", "#3A2A20"));
                    st.AddPart("Струны", StyleData.Make("Гладкий", "#F4EBDD", null, 1, 0.6f));
                    // гитара стоит на подставке, чуть откинувшись назад; все детали — вдоль одной оси
                    const float lean = 14f;
                    var up = Quaternion.Euler(lean, 0, 0) * Vector3.up;
                    var fwd = Quaternion.Euler(lean, 0, 0) * Vector3.back; // лицевая сторона деки
                    var b0 = V(0, 0.07f, -0.02f);
                    Vector3 A(float s, float front = 0) => b0 + up * s + fwd * front;
                    var rot = V(lean, 0, 0);
                    Shapes.Box(st, 1, m, V(0, 0.02f, 0.02f), V(0.42f, 0.04f, 0.34f));
                    Shapes.Box(st, 1, m, V(0, 0.07f, -0.1f), V(0.36f, 0.08f, 0.05f));
                    Seg(st, 1, m, V(0, 0.04f, 0.16f), A(0.78f, -0.06f), 0.04f);
                    Shapes.Ball(st, 0, m, A(0.2f), V(0.4f, 0.42f, 0.1f), rot);
                    Shapes.Ball(st, 0, m, A(0.47f), V(0.3f, 0.3f, 0.095f), rot);
                    Shapes.Cyl(st, 1, m, A(0.36f, 0.045f), 0.1f, 0.012f, V(lean - 90, 0, 0));
                    Shapes.Box(st, 1, m, A(0.84f, 0.01f), V(0.065f, 0.62f, 0.035f), rot);
                    Shapes.Box(st, 1, m, A(1.19f, 0.01f), V(0.1f, 0.13f, 0.04f), rot);
                    Shapes.Box(st, 2, m, A(0.66f, 0.05f), V(0.035f, 1.0f, 0.008f), rot);
                    Shapes.Box(st, 1, m, A(0.14f, 0.05f), V(0.12f, 0.025f, 0.02f), rot);
                },
            });

            all.Add(new FurnitureDef
            {
                id = "computer", name = "Компьютерный стол", category = "Развлечения", w = 2, d = 1,
                description = "Посидеть в интернете или написать роман.",
                slots =
                {
                    Slot(V(0, 0, -0.8f), 0, C(0, -1), C(1, -1)),
                    Slot(V(0, 0, -0.8f), 0, C(1, -1), C(0, -1)),
                },
                actions =
                {
                    new Interaction
                    {
                        name = "Сидеть в интернете", verb = "Сидит в интернете", duration = 60, pose = Pose.SitFloor, fun = 0.8f,
                        social = 0.15f, tags = ActTag.Routine, bubble = "Котики…",
                    },
                    new Interaction
                    {
                        name = "Взять лисёнка из приюта", verb = "Оформляет документы", duration = 30, pose = Pose.SitFloor,
                        minStage = LifeStage.Adult, playerOnly = true, bubble = "Анкета для приюта…",
                        onFinish = f => Game.I.AdoptKit(f),
                    },
                    new Interaction
                    {
                        name = "Писать роман", verb = "Пишет роман", duration = 90, pose = Pose.SitFloor, fun = 0.55f,
                        tags = ActTag.Creative | ActTag.Solitary, bubble = "Глава первая…", minStage = LifeStage.Teen,
                        onFinish = f =>
                        {
                            Mood(f, f.pers.O >= 7, "Глава готова", 8, 240);
                            Catalog.FinishChapter(f);
                            Mood(f, f.pers.C <= 3, "Опять бросил(а) на середине", -3, 120);
                        },
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Стол", StyleData.Make("Дерево", "#B58A5A"));
                    st.AddPart("Компьютер", StyleData.Make("Гладкий", "#2A2A2E", null, 1, 0.6f));
                    st.AddPart("Экран", StyleData.Make("Гладкий", "#15181E", null, 1, 0.9f));
                    f.screenPart = 2;
                    Shapes.Box(st, 0, m, V(0, 0.6f, 0.05f), V(1.8f, 0.05f, 0.75f));
                    Shapes.Box(st, 0, m, V(-0.85f, 0.3f, 0.05f), V(0.06f, 0.6f, 0.7f));
                    Shapes.Box(st, 0, m, V(0.85f, 0.3f, 0.05f), V(0.06f, 0.6f, 0.7f));
                    Shapes.Box(st, 1, m, V(0, 0.65f, 0.25f), V(0.25f, 0.04f, 0.18f));
                    Shapes.Box(st, 1, m, V(0, 0.8f, 0.27f), V(0.05f, 0.3f, 0.04f));
                    Shapes.Box(st, 1, m, V(0, 0.95f, 0.24f), V(0.75f, 0.45f, 0.04f));
                    Shapes.Box(st, 2, m, V(0, 0.95f, 0.215f), V(0.68f, 0.38f, 0.01f));
                    Shapes.Box(st, 1, m, V(0, 0.63f, -0.12f), V(0.5f, 0.02f, 0.16f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "ball", name = "Мячик", category = "Развлечения",
                description = "Погонять мяч. Можно ходить сквозь него.",
                walkable = true,
                slots = { Slot(V(0, 0, 0), 0, C(0, -1), C(1, 0), C(-1, 0), C(0, 1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Поиграть с мячиком", verb = "Играет с мячиком", duration = 30, fun = 1.0f, energy = -0.1f,
                        bubble = "Мячик!", hold = "ball", onFinish = f => Mood(f, f.pers.E >= 7, "Веселуха", 5),
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Мяч", StyleData.Make("Звёздочки", "#E5484D", "#F5E6C8"));
                    Shapes.Ball(st, 0, m, V(0.2f, 0.15f, 0.1f), V(0.3f, 0.3f, 0.3f));
                },
            });

            // ===== Ванная =====
            all.Add(new FurnitureDef
            {
                id = "bath", name = "Ванна", category = "Ванная", w = 1, d = 2,
                description = "В тёплой ванне можно вымыться и отдохнуть.",
                slots = { Slot(V(0, 0.22f, 0), 0, C(1, 0), C(-1, 0), C(1, 1), C(-1, 1), C(0, -1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Принять ванну", verb = "Принимает ванну", duration = 45, pose = Pose.Bath, onObject = true,
                        hygiene = 2.3f, fun = 0.2f, tags = ActTag.Comfort, bubble = "Буль-буль",
                        onFinish = f => f.AddMoodlet("Свежесть", 6, 240),
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Ванна", StyleData.Make("Гладкий", "#F7F7F5", null, 1, 0.7f));
                    st.AddPart("Вода", StyleData.Make("Гладкий", "#7EC8E3", null, 1, 0.95f));
                    st.AddPart("Ножки", StyleData.Make("Гладкий", "#C9A646", null, 1, 0.8f));
                    Shapes.Box(st, 0, m, V(0, 0.36f, 0), V(0.9f, 0.5f, 1.82f));
                    Shapes.Box(st, 1, m, V(0, 0.612f, 0), V(0.76f, 0.01f, 1.66f));
                    for (int i = 0; i < 4; i++)
                        Shapes.Ball(st, 2, m, V(i % 2 == 0 ? -0.36f : 0.36f, 0.06f, i < 2 ? -0.78f : 0.78f), V(0.12f, 0.12f, 0.12f));
                },
            });

            // ===== Декор =====
            all.Add(new FurnitureDef
            {
                id = "plant", name = "Растение", category = "Декор",
                description = "Аккуратным лисичкам нравится его поливать.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1), C(1, 0), C(-1, 0), C(0, 1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Полить", verb = "Поливает цветок", duration = 10, fun = 0.3f, tags = ActTag.Tidy, bubble = "Расти большим",
                        cooldown = 360,
                        onFinish = f => Mood(f, f.pers.C >= 7, "Всё под контролем", 4, 120),
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Горшок", StyleData.Make("Плитка", "#C2683F", "#A3532F", 2f));
                    st.AddPart("Листья", StyleData.Make("Гладкий", "#4E8A3E"));
                    Shapes.Cyl(st, 0, m, V(0, 0.18f, 0), 0.42f, 0.36f);
                    Shapes.Ball(st, 1, m, V(0, 0.6f, 0), V(0.55f, 0.55f, 0.55f));
                    Shapes.Ball(st, 1, m, V(0.12f, 0.88f, 0.05f), V(0.4f, 0.45f, 0.4f));
                    Shapes.Ball(st, 1, m, V(-0.14f, 0.8f, -0.08f), V(0.35f, 0.4f, 0.35f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "lamp", name = "Торшер", category = "Декор",
                description = "Сам включается вечером.",
                build = (f, st, m) =>
                {
                    st.AddPart("Основание", StyleData.Make("Гладкий", "#3B3B3B", null, 1, 0.6f));
                    st.AddPart("Абажур", StyleData.Make("Ткань", "#F2D8A7"));
                    Shapes.Cyl(st, 0, m, V(0, 0.02f, 0), 0.36f, 0.04f);
                    Shapes.Box(st, 0, m, V(0, 0.7f, 0), V(0.08f, 1.36f, 0.08f));
                    Shapes.MeshPart(Shapes.Cone(), st, 1, m, V(0, 1.42f, 0), V(0.5f, 0.32f, 0.5f), default, true);
                    var lg = new GameObject("Light");
                    lg.transform.SetParent(m, false);
                    lg.transform.localPosition = V(0, 1.3f, 0);
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.range = 6f;
                    l.intensity = 1.4f;
                    l.color = new Color(1f, 0.82f, 0.58f);
                    l.shadows = LightShadows.None;
                    l.enabled = false;
                    f.lamp = l;
                },
            });

            all.Add(new FurnitureDef
            {
                id = "rug", name = "Ковёр", category = "Декор", w = 3, d = 2, walkable = true,
                description = "По ковру можно ходить.",
                build = (f, st, m) =>
                {
                    st.AddPart("Ковёр", StyleData.Make("Ромбы", "#D9A25F", "#8C3B2E", 0.8f));
                    st.AddPart("Кайма", StyleData.Make("Ворс", "#8C3B2E"));
                    Shapes.Box(st, 1, m, V(0, 0.008f, 0), V(2.8f, 0.015f, 1.8f));
                    Shapes.Box(st, 0, m, V(0, 0.012f, 0), V(2.6f, 0.016f, 1.6f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "roundrug", name = "Круглый коврик", category = "Декор", w = 2, d = 2, walkable = true,
                build = (f, st, m) =>
                {
                    st.AddPart("Коврик", StyleData.Make("Ворс", "#9DB7C9"));
                    Shapes.Cyl(st, 0, m, V(0, 0.008f, 0), 1.8f, 0.015f);
                },
            });

            // ===== Декор для дома =====
            all.Add(new FurnitureDef
            {
                id = "dresser", name = "Комод", category = "Спальня",
                build = (f, st, m) =>
                {
                    st.AddPart("Корпус", StyleData.Make("Дерево", "#C8A27A"));
                    st.AddPart("Ручки", StyleData.Make("Гладкий", "#D4B26A", null, 1, 0.6f));
                    Shapes.Box(st, 0, m, V(0, 0.45f, 0.1f), V(0.9f, 0.9f, 0.55f));
                    for (int i = 0; i < 3; i++)
                    {
                        Shapes.Box(st, 0, m, V(0, 0.18f + i * 0.27f, -0.18f), V(0.82f, 0.2f, 0.02f));
                        Shapes.Box(st, 1, m, V(0, 0.18f + i * 0.27f, -0.21f), V(0.16f, 0.04f, 0.04f));
                    }
                },
            });
            all.Add(new FurnitureDef
            {
                id = "nightstand", name = "Тумбочка с ночником", category = "Спальня",
                build = (f, st, m) =>
                {
                    st.AddPart("Тумбочка", StyleData.Make("Дерево", "#B58A5A"));
                    st.AddPart("Ночник", StyleData.Make("Ткань", "#F7DE9E"));
                    Shapes.Box(st, 0, m, V(0, 0.25f, 0.15f), V(0.5f, 0.5f, 0.45f));
                    Shapes.Cyl(st, 1, m, V(0, 0.62f, 0.15f), 0.22f, 0.24f);
                    var lg = new GameObject("Light"); lg.transform.SetParent(m, false); lg.transform.localPosition = V(0, 0.7f, 0.15f);
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 3f; l.intensity = 0.8f; l.color = new Color(1f, 0.85f, 0.6f); l.enabled = false;
                    f.lamp = l;
                },
            });
            all.Add(new FurnitureDef
            {
                id = "pouf", name = "Пуф", category = "Гостиная",
                slots = { Slot(V(0, 0.3f, 0), 180, C(0, -1), C(1, 0), C(-1, 0), C(0, 1)) },
                actions = { new Interaction { name = "Посидеть", verb = "Сидит на пуфе", duration = 30, pose = Pose.Sit, onObject = true, energy = 0.05f, fun = 0.15f, tags = ActTag.Comfort } },
                build = (f, st, m) =>
                {
                    st.AddPart("Обивка", StyleData.Make("Вельвет", "#E58C84"));
                    Shapes.Cyl(st, 0, m, V(0, 0.17f, 0), 0.6f, 0.34f);
                },
            });
            all.Add(new FurnitureDef
            {
                id = "painting", name = "Картина", category = "Декор", walkable = true,
                description = "Для этого предмета нужна стена.",
                build = (f, st, m) =>
                {
                    st.AddPart("Рама", StyleData.Make("Дерево", "#B98A4E", null, 1, 0.4f));
                    st.AddPart("Холст", StyleData.Make("Цветочки", "#F3E5C8", "#E58C84"));
                    Shapes.Box(st, 0, m, V(0, 1.25f, 0.43f), V(0.7f, 0.55f, 0.05f));
                    Shapes.Box(st, 1, m, V(0, 1.25f, 0.4f), V(0.58f, 0.43f, 0.03f));
                },
            });
            all.Add(new FurnitureDef
            {
                id = "clock", name = "Настенные часы", category = "Декор", walkable = true,
                description = "Для этого предмета нужна стена.",
                build = (f, st, m) =>
                {
                    st.AddPart("Корпус", StyleData.Make("Гладкий", "#8B5A2B"));
                    st.AddPart("Циферблат", StyleData.Make("Гладкий", "#FFF8EE"));
                    st.AddPart("Стрелки", StyleData.Make("Гладкий", "#2E2420"));
                    Shapes.Cyl(st, 0, m, V(0, 1.5f, 0.43f), 0.38f, 0.06f, V(90, 0, 0));
                    Shapes.Cyl(st, 1, m, V(0, 1.5f, 0.395f), 0.3f, 0.02f, V(90, 0, 0));
                    Shapes.Box(st, 2, m, V(0, 1.55f, 0.38f), V(0.03f, 0.12f, 0.02f));
                    Shapes.Box(st, 2, m, V(0.04f, 1.5f, 0.38f), V(0.09f, 0.03f, 0.02f));
                },
            });
            all.Add(new FurnitureDef
            {
                id = "vase", name = "Ваза с цветами", category = "Декор",
                build = (f, st, m) =>
                {
                    st.AddPart("Ваза", StyleData.Make("Гладкий", "#9FC6E6", null, 1, 0.7f));
                    st.AddPart("Цветы", StyleData.Make("Горошек", "#F5A3C1", "#F7DE7E"));
                    st.AddPart("Столик", StyleData.Make("Дерево", "#B58A5A"));
                    Shapes.Cyl(st, 2, m, V(0, 0.3f, 0), 0.45f, 0.06f);
                    Shapes.Cyl(st, 2, m, V(0, 0.15f, 0), 0.08f, 0.3f);
                    Shapes.Cyl(st, 0, m, V(0, 0.45f, 0), 0.18f, 0.26f);
                    Shapes.Ball(st, 1, m, V(0, 0.68f, 0), V(0.32f, 0.26f, 0.32f));
                },
            });

            // ===== Сад =====
            all.Add(new FurnitureDef
            {
                id = "bench", name = "Садовая скамейка", category = "Сад", w = 2, d = 1,
                slots = { Slot(V(-0.45f, 0.42f, -0.05f), 180, C(0, -1)), Slot(V(0.45f, 0.42f, -0.05f), 180, C(1, -1)) },
                actions = { new Interaction { name = "Посидеть на скамейке", verb = "Отдыхает на скамейке", duration = 40, pose = Pose.Sit, onObject = true, energy = 0.06f, fun = 0.3f, tags = ActTag.Comfort,
                    onFinish = fx => Mood(fx, fx.pers.O >= 6, "Свежий воздух", 4, 180) } },
                build = (f, st, m) =>
                {
                    st.AddPart("Доски", StyleData.Make("Доски", "#B98A5A"));
                    st.AddPart("Ножки", StyleData.Make("Гладкий", "#4A4744", null, 1, 0.5f));
                    Shapes.Box(st, 0, m, V(0, 0.42f, 0), V(1.8f, 0.06f, 0.5f));
                    Shapes.Box(st, 0, m, V(0, 0.75f, 0.24f), V(1.8f, 0.3f, 0.05f), V(-10, 0, 0));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Shapes.Box(st, 1, m, V(0.8f * s, 0.21f, 0), V(0.06f, 0.42f, 0.45f));
                        Shapes.Box(st, 1, m, V(0.8f * s, 0.6f, 0.24f), V(0.06f, 0.4f, 0.06f));
                    }
                },
            });
            all.Add(new FurnitureDef
            {
                id = "gardenlamp", name = "Садовый фонарь", category = "Сад",
                description = "Сам загорается вечером.",
                build = (f, st, m) =>
                {
                    st.AddPart("Столб", StyleData.Make("Гладкий", "#3B3B3B", null, 1, 0.6f));
                    st.AddPart("Плафон", StyleData.Make("Гладкий", "#FFF1C9", null, 1, 0.8f));
                    Shapes.Cyl(st, 0, m, V(0, 0.05f, 0), 0.3f, 0.1f);
                    Shapes.Box(st, 0, m, V(0, 0.8f, 0), V(0.1f, 1.5f, 0.1f));
                    Shapes.Box(st, 1, m, V(0, 1.65f, 0), V(0.25f, 0.3f, 0.25f));
                    Shapes.MeshPart(Shapes.Pyramid(), st, 0, m, V(0, 1.88f, 0), V(0.36f, 0.18f, 0.36f), default, true);
                    var lg = new GameObject("Light"); lg.transform.SetParent(m, false); lg.transform.localPosition = V(0, 1.65f, 0);
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 6f; l.intensity = 1.2f; l.color = new Color(1f, 0.85f, 0.6f); l.enabled = false;
                    f.lamp = l;
                },
            });
            all.Add(new FurnitureDef
            {
                id = "swing", name = "Качели", category = "Сад", w = 2, d = 1,
                slots = { Slot(V(0, 0.45f, 0), 180, C(0, -1), C(1, -1)) },
                actions = { new Interaction { name = "Покачаться", verb = "Качается на качелях", duration = 30, pose = Pose.Sit, onObject = true, fun = 1.1f, energy = -0.05f, bubble = "Уииии!",
                    onFinish = fx => Mood(fx, fx.pers.E >= 6 || fx.IsChild, "Покачались!", 7, 180) } },
                build = (f, st, m) =>
                {
                    st.AddPart("Рама", StyleData.Make("Дерево", "#9C785C"));
                    st.AddPart("Сиденье", StyleData.Make("Доски", "#E58C84"));
                    st.AddPart("Верёвки", StyleData.Make("Гладкий", "#E8DCC8"));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Shapes.Box(st, 0, m, V(0.9f * s, 1.0f, -0.22f), V(0.12f, 2.05f, 0.12f), V(10, 0, 0));
                        Shapes.Box(st, 0, m, V(0.9f * s, 1.0f, 0.22f), V(0.12f, 2.05f, 0.12f), V(-10, 0, 0));
                        Shapes.Box(st, 2, m, V(0.35f * s, 1.25f, 0), V(0.03f, 1.6f, 0.03f));
                    }
                    Shapes.Box(st, 0, m, V(0, 2.02f, 0), V(1.95f, 0.1f, 0.1f));
                    Shapes.Box(st, 1, m, V(0, 0.44f, 0), V(0.8f, 0.06f, 0.4f));
                },
            });
            all.Add(new FurnitureDef
            {
                id = "flowerbed", name = "Клумба", category = "Сад",
                slots = { Slot(V(0, 0, 0), 0, C(0, -1), C(1, 0), C(-1, 0), C(0, 1)) },
                actions = { new Interaction { name = "Полить клумбу", verb = "Поливает клумбу", duration = 12, fun = 0.3f, tags = ActTag.Tidy, cooldown = 360,
                    onFinish = fx => Mood(fx, fx.pers.C >= 6, "Цветы радуют", 4, 180) } },
                build = (f, st, m) =>
                {
                    st.AddPart("Бортик", StyleData.Make("Камень", "#C9C1B5", "#A8A095"));
                    st.AddPart("Цветы", StyleData.Make("Горошек", "#F5A3C1", "#F7DE7E", 2f));
                    st.AddPart("Земля", StyleData.Make("Ворс", "#8A6A4E"));
                    Shapes.Box(st, 0, m, V(0, 0.12f, 0), V(0.92f, 0.24f, 0.92f));
                    Shapes.Box(st, 2, m, V(0, 0.2f, 0), V(0.78f, 0.1f, 0.78f));
                    Shapes.Ball(st, 1, m, V(0, 0.32f, 0), V(0.72f, 0.3f, 0.72f));
                },
            });
            all.Add(new FurnitureDef
            {
                id = "bridge", name = "Садовый мостик", category = "Сад", w = 1, d = 3, walkable = true,
                description = "Горбатый деревянный мостик — через ручеёк, пруд или просто клумбу.",
                build = (f, st, m) =>
                {
                    st.AddPart("Настил", StyleData.Make("Доски", "#B98A5A", null, 2f));
                    st.AddPart("Перила", StyleData.Make("Дерево", "#7A5230"));
                    // настил дугой, доски чуть неровные
                    for (int i = 0; i < 9; i++)
                    {
                        float z = -1.3f + i * 0.325f;
                        float y = 0.06f + Mathf.Sin((i / 8f) * Mathf.PI) * 0.18f;
                        float tilt = Mathf.Cos((i / 8f) * Mathf.PI) * -24f;
                        Shapes.Box(st, 0, m, V((i % 3 - 1) * 0.012f, y, z), V(0.98f, 0.06f, 0.33f), V(tilt, (i % 2) * 1.5f - 0.7f, 0));
                    }
                    foreach (var x in new[] { -0.47f, 0.47f })
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            float z = -1.2f + i * 0.6f;
                            float y = 0.06f + Mathf.Sin(((z + 1.3f) / 2.6f) * Mathf.PI) * 0.18f;
                            Shapes.Box(st, 1, m, V(x, y + 0.25f, z), V(0.06f, 0.5f, 0.06f));
                        }
                        for (int i = 0; i < 4; i++)
                        {
                            float z0 = -1.2f + i * 0.6f, z1 = z0 + 0.6f;
                            float y0 = 0.06f + Mathf.Sin(((z0 + 1.3f) / 2.6f) * Mathf.PI) * 0.18f + 0.5f;
                            float y1 = 0.06f + Mathf.Sin(((z1 + 1.3f) / 2.6f) * Mathf.PI) * 0.18f + 0.5f;
                            float ang = Mathf.Atan2(y1 - y0, 0.6f) * Mathf.Rad2Deg;
                            Shapes.Box(st, 1, m, V(x, (y0 + y1) / 2f, (z0 + z1) / 2f), V(0.07f, 0.06f, 0.64f), V(-ang, 0, 0));
                        }
                    }
                },
            });
            all.Add(new FurnitureDef
            {
                id = "birdbath", name = "Поилка для птиц", category = "Сад",
                build = (f, st, m) =>
                {
                    st.AddPart("Камень", StyleData.Make("Бетон", "#D8D2C8"));
                    st.AddPart("Вода", StyleData.Make("Гладкий", "#86BCE6", null, 1, 0.9f));
                    Shapes.Cyl(st, 0, m, V(0, 0.05f, 0), 0.4f, 0.1f);
                    Shapes.Cyl(st, 0, m, V(0, 0.4f, 0), 0.14f, 0.7f);
                    Shapes.Cyl(st, 0, m, V(0, 0.8f, 0), 0.7f, 0.12f);
                    Shapes.Cyl(st, 1, m, V(0, 0.86f, 0), 0.55f, 0.03f);
                },
            });

            all.Add(new FurnitureDef
            {
                id = "fountain", name = "Фонтан", category = "Сад", w = 2, d = 2,
                description = "Журчит и блестит на солнце. Можно бросить монетку и загадать желание.",
                slots = { Slot(V(0, 0, 0), 0, C(0, -1), C(1, -1), C(-1, 0)), Slot(V(0, 0, 0), 0, C(0, 2), C(1, 2), C(2, 1)) },
                actions =
                {
                    new Interaction { name = "Полюбоваться фонтаном", verb = "Любуется фонтаном", duration = 25, fun = 0.7f, energy = 0.02f, tags = ActTag.Comfort,
                        onFinish = fx => Mood(fx, fx.pers.O >= 5, "Красивый фонтан", 4, 180) },
                    new Interaction { name = "Бросить монетку", verb = "Загадывает желание", duration = 6, fun = 1.2f, cooldown = 600, bubble = "Хочу, чтобы…",
                        onFinish = fx => { Game.I.Charge(fx, 1, "Монетка в фонтан"); Mood(fx, UnityEngine.Random.value < 0.5f, "Загадано желание", 6, 720); } },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Камень", StyleData.Make("Камень", "#D8D2C8", "#BDB5AA"));
                    st.AddPart("Вода", StyleData.Make("Гладкий", "#86BCE6", null, 1, 0.9f));
                    Shapes.Cyl(st, 0, m, V(0, 0.22f, 0), 1.9f, 0.44f);
                    Shapes.Cyl(st, 1, m, V(0, 0.42f, 0), 1.7f, 0.04f);
                    Shapes.Cyl(st, 0, m, V(0, 0.75f, 0), 0.3f, 1.0f);
                    Shapes.Cyl(st, 0, m, V(0, 1.2f, 0), 1.0f, 0.12f);
                    Shapes.Cyl(st, 1, m, V(0, 1.27f, 0), 0.82f, 0.03f);
                    Shapes.Cyl(st, 0, m, V(0, 1.45f, 0), 0.16f, 0.4f);
                    Shapes.Ball(st, 1, m, V(0, 1.7f, 0), V(0.22f, 0.22f, 0.22f));
                },
            });

            all.Add(new FurnitureDef
            {
                id = "cafecounter", name = "Барная стойка", category = "Кухня", w = 2, d = 1,
                description = "Какао и пирожные. В кафе за угощение нужно платить.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1)), Slot(V(0, 0, -0.8f), 0, C(1, -1)) },
                actions =
                {
                    new Interaction { name = "Выпить какао", verb = "Пьёт какао", duration = 20, hunger = 0.6f, fun = 0.5f, social = 0.3f, bubble = "Вкусно!",
                        onFinish = fx => { Game.I.Charge(fx, 8, "Какао"); Mood(fx, true, "Тёплое какао", 5, 240); } },
                    new Interaction { name = "Съесть пирожное", verb = "Ест пирожное", duration = 25, hunger = 2.2f, fun = 0.3f, hold = "food",
                        onFinish = fx => { Game.I.Charge(fx, 15, "Пирожное"); Mood(fx, fx.pers.O >= 6, "Новый вкус", 4, 180); } },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Стойка", StyleData.Make("Доски", "#B9844F"));
                    st.AddPart("Столешница", StyleData.Make("Мрамор", "#F3EEE8", "#C9BFB4", 1, 0.5f));
                    st.AddPart("Угощения", StyleData.Make("Горошек", "#F5A3C1", "#FFF9F0", 2f));
                    Shapes.Box(st, 0, m, V(0, 0.45f, 0.05f), V(1.92f, 0.9f, 0.7f));
                    Shapes.Box(st, 1, m, V(0, 0.93f, 0), V(2f, 0.06f, 0.84f));
                    Shapes.Box(st, 1, m, V(0.5f, 1.12f, 0.15f), V(0.7f, 0.3f, 0.4f));
                    Shapes.Ball(st, 2, m, V(0.35f, 1.04f, 0.1f), V(0.16f, 0.12f, 0.16f));
                    Shapes.Ball(st, 2, m, V(0.62f, 1.04f, 0.18f), V(0.16f, 0.12f, 0.16f));
                    Shapes.Cyl(st, 2, m, V(-0.55f, 1.06f, 0.1f), 0.14f, 0.2f);
                    Shapes.Cyl(st, 2, m, V(-0.3f, 1.06f, 0.15f), 0.14f, 0.2f);
                },
            });

            // ===== Город: работа и отдых =====
            all.Add(new FurnitureDef
            {
                id = "telescope", name = "Телескоп", category = "Развлечения",
                description = "Звёзды, планеты и луна. Ночью смотреть интереснее.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1), C(-1, 0), C(1, 0)) },
                actions = { new Interaction { name = "Смотреть в телескоп", verb = "Смотрит на звёзды", duration = 40, fun = 0.7f, tags = ActTag.Creative | ActTag.Solitary,
                    onFinish = fx => Mood(fx, Game.I.Hour >= 20f || Game.I.Hour < 5f, "Видели падающую звезду", 7, 360) } },
                build = (f, st, m) =>
                {
                    st.AddPart("Труба", StyleData.Make("Гладкий", "#C9A24A", null, 1, 0.7f));
                    st.AddPart("Тренога", StyleData.Make("Дерево", "#7A5534"));
                    st.AddPart("Крепление", StyleData.Make("Гладкий", "#3A3A40", null, 1, 0.5f));
                    // тренога: ноги сходятся в головке, на ней — труба, смотрящая в небо
                    var head = V(0, 0.98f, 0);
                    foreach (var a in new[] { 0f, 120f, 240f })
                    {
                        float r = (a + 60f) * Mathf.Deg2Rad;
                        Seg(st, 1, m, V(Mathf.Sin(r) * 0.36f, 0, Mathf.Cos(r) * 0.36f), head + V(Mathf.Sin(r) * 0.05f, -0.04f, Mathf.Cos(r) * 0.05f), 0.075f);
                    }
                    Shapes.Box(st, 2, m, head, V(0.16f, 0.1f, 0.16f));
                    Shapes.Box(st, 2, m, head + V(0, 0.09f, 0), V(0.06f, 0.1f, 0.06f));
                    var dir = Quaternion.Euler(-38f, 0, 0) * Vector3.forward;   // вверх и вперёд
                    var mid = head + V(0, 0.18f, 0);
                    Seg(st, 0, m, mid - dir * 0.42f, mid + dir * 0.55f, 0.17f, true);
                    Seg(st, 0, m, mid + dir * 0.5f, mid + dir * 0.62f, 0.21f, true);         // бленда
                    Seg(st, 2, m, mid - dir * 0.52f, mid - dir * 0.4f, 0.08f, true);         // окуляр
                    Seg(st, 2, m, mid + V(0, 0.12f, 0) - dir * 0.1f, mid + V(0, 0.12f, 0) + dir * 0.18f, 0.045f, true); // искатель
                },
            });
            all.Add(new FurnitureDef
            {
                id = "piano", name = "Пианино", category = "Развлечения", w = 2, d = 1,
                description = "Гаммы, этюды и вальсы.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1), C(1, -1)) },
                actions = { new Interaction { name = "Играть на пианино", verb = "Играет на пианино", duration = 40, fun = 0.9f, tags = ActTag.Creative, bubble = "♪ До-ре-ми ♪",
                    onFinish = fx => Mood(fx, fx.SkillLevel(Skill.Music) >= 3, "Красивая мелодия", 6, 240) } },
                build = (f, st, m) =>
                {
                    st.AddPart("Корпус", StyleData.Make("Дерево", "#4A2E22", null, 1, 0.6f));
                    st.AddPart("Клавиши", StyleData.Make("Полоска", "#FFFDF8", "#222222", 12f, 0.5f));
                    Shapes.Box(st, 0, m, V(0, 0.65f, 0.12f), V(1.8f, 1.3f, 0.5f));
                    Shapes.Box(st, 1, m, V(0, 0.76f, -0.22f), V(1.6f, 0.06f, 0.24f));
                    Shapes.Box(st, 0, m, V(0, 0.72f, -0.24f), V(1.7f, 0.06f, 0.3f));
                    foreach (var s in new[] { -0.8f, 0.8f }) Shapes.Box(st, 0, m, V(s, 0.36f, -0.3f), V(0.08f, 0.72f, 0.08f));
                },
            });
            all.Add(new FurnitureDef
            {
                id = "schooldesk", name = "Парта", category = "Развлечения",
                description = "Делать уроки — дело серьёзное.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1)) },
                actions = { new Interaction { name = "Делать уроки", verb = "Делает уроки", duration = 30, fun = -0.05f, pose = Pose.Sit, tags = ActTag.Routine, maxStage = LifeStage.Teen } },
                build = (f, st, m) =>
                {
                    st.AddPart("Столешница", StyleData.Make("Дерево", "#C99A66"));
                    st.AddPart("Ножки", StyleData.Make("Гладкий", "#5B7FA8", null, 1, 0.5f));
                    st.AddPart("Тетрадь", StyleData.Make("Клетка", "#FFFFFF", "#8CC7E8", 6f));
                    Shapes.Box(st, 0, m, V(0, 0.55f, 0.05f), V(0.85f, 0.05f, 0.6f));
                    foreach (var x in new[] { -0.38f, 0.38f }) Shapes.Box(st, 1, m, V(x, 0.27f, 0.05f), V(0.05f, 0.55f, 0.5f));
                    Shapes.Box(st, 2, m, V(0.1f, 0.59f, 0f), V(0.3f, 0.02f, 0.22f), V(0, 12, 0));
                },
            });
            all.Add(new FurnitureDef
            {
                id = "chalkboard", name = "Школьная доска", category = "Декор", w = 3, d = 1,
                slots = { Slot(V(0, 0, -0.8f), 0, C(1, -1), C(0, -1), C(2, -1)) },
                build = (f, st, m) =>
                {
                    st.AddPart("Доска", StyleData.Make("Гладкий", "#3E5E4A"));
                    st.AddPart("Рама", StyleData.Make("Дерево", "#B98A5A"));
                    st.AddPart("Мел", StyleData.Make("Гладкий", "#F4F1EC"));
                    Shapes.Box(st, 1, m, V(0, 1.15f, 0.4f), V(2.8f, 1.3f, 0.06f));
                    Shapes.Box(st, 0, m, V(0, 1.15f, 0.36f), V(2.6f, 1.1f, 0.04f));
                    Shapes.Box(st, 2, m, V(-0.6f, 1.3f, 0.33f), V(0.8f, 0.04f, 0.02f), V(0, 0, 8));
                    Shapes.Box(st, 2, m, V(0.5f, 1.0f, 0.33f), V(0.6f, 0.04f, 0.02f), V(0, 0, -5));
                    Shapes.Box(st, 1, m, V(0, 0.48f, 0.35f), V(2.6f, 0.04f, 0.12f));
                },
            });
            all.Add(new FurnitureDef
            {
                id = "marketstall", name = "Прилавок", category = "Сад", w = 2, d = 1,
                description = "Фрукты, ягоды, мёд. Продавец стоит за прилавком.",
                slots = { Slot(V(0, 0, -0.8f), 0, C(0, -1), C(1, -1)), Slot(V(0, 0, 0.8f), 180, C(0, 1), C(1, 1)) },
                actions = { new Interaction { name = "Купить фрукты", verb = "Покупает фрукты", duration = 12, hunger = 1.8f, fun = 0.3f, hold = "food", slotOnly = 0,
                    onFinish = fx => { Game.I.Charge(fx, 6, "Фрукты на рынке"); Mood(fx, true, "Свежие фрукты", 4, 180); } } },
                build = (f, st, m) =>
                {
                    st.AddPart("Дерево", StyleData.Make("Доски", "#B98A5A"));
                    st.AddPart("Навес", StyleData.Make("Полоска", "#FFFFFF", f.anchor.x % 2 == 0 ? "#E57373" : "#64B5F6", 2f));
                    st.AddPart("Фрукты 1", StyleData.Make("Гладкий", "#E0525E", null, 1, 0.5f));
                    st.AddPart("Фрукты 2", StyleData.Make("Гладкий", "#F2B33D", null, 1, 0.5f));
                    st.AddPart("Фрукты 3", StyleData.Make("Гладкий", "#7CB342", null, 1, 0.5f));
                    Shapes.Box(st, 0, m, V(0, 0.45f, 0), V(1.85f, 0.9f, 0.7f));
                    Shapes.Box(st, 0, m, V(0, 0.92f, 0), V(1.95f, 0.06f, 0.8f));
                    foreach (var x in new[] { -0.92f, 0.92f })
                    {
                        Shapes.Box(st, 0, m, V(x, 1.15f, -0.36f), V(0.08f, 2.3f, 0.08f));
                        Shapes.Box(st, 0, m, V(x, 1.3f, 0.36f), V(0.08f, 2.6f, 0.08f));
                    }
                    Shapes.Box(st, 1, m, V(0, 2.45f, 0), V(2.2f, 0.06f, 1.2f), V(-14, 0, 0));
                    for (int i = 0; i < 3; i++)
                        for (int k = 0; k < 4; k++)
                            Shapes.Ball(st, 2 + i, m, V(-0.6f + i * 0.6f + (k % 2) * 0.2f - 0.1f, 1.02f, -0.15f + (k / 2) * 0.18f), V(0.15f, 0.15f, 0.15f));
                },
            });
            all.Add(new FurnitureDef
            {
                id = "campfire", name = "Костёр", category = "Сад", w = 2, d = 2,
                description = "Тепло, треск поленьев и истории до звёзд.",
                slots =
                {
                    Slot(V(0, 0, 0), 0, C(0, -1), C(1, -1)), Slot(V(0, 0, 0), 0, C(0, 2), C(1, 2)),
                    Slot(V(0, 0, 0), 0, C(-1, 0), C(-1, 1)), Slot(V(0, 0, 0), 0, C(2, 0), C(2, 1)),
                },
                actions =
                {
                    new Interaction { name = "Посидеть у костра", verb = "Греется у костра", duration = 40, pose = Pose.SitFloor, fun = 0.7f, social = 0.3f, energy = 0.03f, tags = ActTag.Comfort,
                        onFinish = fx => Mood(fx, true, "Уютный костёр", 6, 240) },
                    new Interaction { name = "Пожарить зефир", verb = "Жарит зефир", duration = 20, pose = Pose.SitFloor, hunger = 1.0f, fun = 0.8f, bubble = "Ммм, зефирка!",
                        onFinish = fx => Mood(fx, fx.IsChild || fx.pers.O >= 6, "Жареный зефир", 6, 240) },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Камни", StyleData.Make("Камень", "#B7B5B8", "#9C9AA0", 2f));
                    st.AddPart("Поленья", StyleData.Make("Дерево", "#7A5534"));
                    st.AddPart("Огонь", StyleData.Make("Гладкий", "#FFB347", null, 1, 0.2f));
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i / 8f * Mathf.PI * 2f;
                        Shapes.Ball(st, 0, m, V(Mathf.Cos(a) * 0.55f, 0.08f, Mathf.Sin(a) * 0.55f), V(0.28f, 0.2f, 0.28f));
                    }
                    Shapes.Box(st, 1, m, V(0, 0.1f, 0), V(0.8f, 0.12f, 0.14f), V(0, 30, 0));
                    Shapes.Box(st, 1, m, V(0, 0.16f, 0), V(0.8f, 0.12f, 0.14f), V(0, -40, 0));
                    Shapes.MeshPart(Shapes.Cone(), st, 2, m, V(0, 0.45f, 0), V(0.45f, 0.6f, 0.45f), default, true);
                    Shapes.MeshPart(Shapes.Cone(), st, 2, m, V(0.12f, 0.35f, 0.08f), V(0.25f, 0.4f, 0.25f), default, true);
                    var lg = new GameObject("Light"); lg.transform.SetParent(m, false); lg.transform.localPosition = V(0, 0.7f, 0);
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 7f; l.intensity = 1.6f; l.color = new Color(1f, 0.6f, 0.3f); l.enabled = false;
                    f.lamp = l;
                    MaterialFactory.SetHighlight(st.parts[2].mat, new Color(0.9f, 0.45f, 0.1f));
                    m.gameObject.AddComponent<CampfireFX>().fireLight = l;
                },
            });
            all.Add(new FurnitureDef
            {
                id = "berrybush", name = "Ягодный куст", category = "Сад",
                description = "Малина и смородина. Ягоды снова вырастают через несколько часов.",
                slots = { Slot(V(0, 0, 0), 0, C(0, -1), C(1, 0), C(-1, 0), C(0, 1)) },
                actions = { new Interaction { name = "Собрать ягоды", verb = "Собирает ягоды", duration = 15, hunger = 0.8f, fun = 0.4f, cooldown = 240, bubble = "Ягодка!",
                    onFinish = fx => Mood(fx, true, "Лесные ягоды", 4, 180) } },
                build = (f, st, m) =>
                {
                    st.AddPart("Листва", StyleData.Make("Ворс", "#5E9A48"));
                    st.AddPart("Ягоды", StyleData.Make("Гладкий", "#C2304A", null, 1, 0.6f));
                    Shapes.Ball(st, 0, m, V(0, 0.35f, 0), V(0.85f, 0.7f, 0.85f));
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 0.9f;
                        Shapes.Ball(st, 1, m, V(Mathf.Cos(a) * 0.36f, 0.3f + (i % 3) * 0.12f, Mathf.Sin(a) * 0.36f), V(0.1f, 0.1f, 0.1f));
                    }
                },
            });

            all.Add(new FurnitureDef
            {
                id = "stairs", name = "Лестница", category = "Дом", w = 1, d = 3, stair = true,
                description = "Ведёт на второй этаж. Наверху, куда она выходит, должна быть комната.",
                build = (f, st, m) =>
                {
                    st.AddPart("Ступени", StyleData.Make("Доски", "#C99A66"));
                    st.AddPart("Перила", StyleData.Make("Дерево", "#8B5A2B"));
                    const int steps = 8;
                    float run = 3f / steps, rise = House.WallH / steps;
                    for (int i = 0; i < steps; i++)
                    {
                        float hgt = (i + 1) * rise;
                        Shapes.Box(st, 0, m, V(0, hgt / 2f, -1.5f + (i + 0.5f) * run), V(0.7f, hgt, run));
                    }
                    float ang = Mathf.Atan2(House.WallH, 3f) * Mathf.Rad2Deg;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Shapes.Box(st, 1, m, V(0.33f * s, 1.0f + 0.75f, 0), V(0.06f, 0.06f, 3.6f), V(-ang, 0, 0));
                        Shapes.Box(st, 1, m, V(0.33f * s, 0.4f, -1.45f), V(0.07f, 0.8f, 0.07f));
                        Shapes.Box(st, 1, m, V(0.33f * s, House.WallH + 0.4f, 1.45f), V(0.07f, 0.8f, 0.07f));
                        Shapes.Box(st, 1, m, V(0.34f * s, 1.0f, 0), V(0.04f, 0.3f, 3.5f), V(-ang, 0, 0));
                    }
                },
            });

            all.Add(new FurnitureDef
            {
                id = "plate", name = "Грязная тарелка", category = "Декор", walkable = true, buildable = false,
                description = "Кто-то не убрал за собой.",
                slots = { Slot(V(0, 0, 0), 0, C(0, 0), C(1, 0), C(-1, 0), C(0, 1), C(0, -1)) },
                actions =
                {
                    new Interaction
                    {
                        name = "Помыть посуду", verb = "Моет посуду", duration = 12, fun = 0.1f, tags = ActTag.Tidy, removesObject = true,
                        hold = "plate", carryTo = "sink",
                        bubble = "Чистота!",
                        onFinish = f =>
                        {
                            Mood(f, f.pers.C >= 7, "Порядок в доме", 5, 120);
                            Mood(f, f.pers.C <= 3, "Скукотища", -3, 60);
                        },
                    },
                },
                build = (f, st, m) =>
                {
                    st.AddPart("Тарелка", StyleData.Make("Гладкий", "#F4F1EA", null, 1, 0.6f));
                    st.AddPart("Крошки", StyleData.Make("Гладкий", "#9A6B3F"));
                    float ox = UnityEngine.Random.Range(-0.2f, 0.2f), oz = UnityEngine.Random.Range(-0.2f, 0.2f);
                    Shapes.Cyl(st, 0, m, V(ox, 0.012f, oz), 0.3f, 0.02f);
                    Shapes.Ball(st, 1, m, V(ox + 0.04f, 0.03f, oz), V(0.07f, 0.03f, 0.06f));
                    Shapes.Ball(st, 1, m, V(ox - 0.05f, 0.028f, oz + 0.04f), V(0.05f, 0.025f, 0.05f));
                },
            });
        }
    }
}
