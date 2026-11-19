using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public enum Mode { Live, Build, Style, Create, Town }

    public class LogEntry
    {
        public string time, text;
        public float born;
    }

    /// <summary>Точка входа: собирает мир, крутит время, обрабатывает клики по миру.</summary>
    public partial class Game : MonoBehaviour
    {
        public static Game I;

        public House house;
        public readonly List<Furniture> furniture = new List<Furniture>();
        public readonly List<Fox> foxes = new List<Fox>();
        public Fox selected;
        public Mode mode = Mode.Live;
        public CameraRig cam;
        public Weather weather;
        public SoundSystem sound;
        public GameUI ui;
        Light sun;
        Camera camera3d;

        // время: 1 реальная секунда на скорости 1 = 1 игровая минута
        public float minutes = 8 * 60;
        public int speedIndex = 1;
        int prevSpeed = 1;
        public static readonly float[] Speeds = { 0f, 1f, 4f, 12f };
        public float SpeedMul => Speeds[speedIndex];
        public bool autonomy = true;

        // деньги
        public float money = StartMoney;
        public const float StartMoney = 1500f;
        public bool freeBuild;
        public bool autoOutfits = true;
        /// <summary>Режим мэра: можно перестраивать и перекрашивать весь город.</summary>
        public bool mayorMode;

        /// <summary>Можно ли перекрашивать это: свои лисички, их одежда, свой дом и вещи (или всё — в режиме мэра).</summary>
        public bool CanStyle(Styleable st)
        {
            if (st == null) return false;
            if (mayorMode) return true;
            var fox = st.GetComponentInParent<Fox>();
            if (fox != null) return IsPlayer(fox);
            var furn = st.GetComponent<Furniture>();
            if (furn != null) return furn.lot == House.PlayerLotId;
            var room = house.rooms.Find(r => r.st == st);
            if (room != null) return room.lot == House.PlayerLotId;
            return st.transform.IsChildOf(house.PlayerLot.root);
        }

        bool MayEditLot(int lot) => mayorMode || lot == House.PlayerLotId;   // наши лисички сами переодеваются ко сну и под дождь
        public int lastBillDay;
        int lastSocialHour = -1;

        // семья
        public const int MaxFoxes = 8;
        public const int TownCap = 45;   // больше лисичек городку не прокормить
        public int KidsAt(int home) { int n = 0; foreach (var f in foxes) if (f.home == home && f.stage <= LifeStage.Teen) n++; return n; }
        public Lifespan lifespan = Lifespan.Normal;
        public int lastAgeDay = 1;
        public readonly List<FoxRecord> archive = new List<FoxRecord>();
        float lastStyleNotify = -10f;

        public readonly List<LogEntry> log = new List<LogEntry>();

        // обустройство
        public Furniture ghost;
        public bool movingExisting;
        public bool placeValid;
        Vector2Int moveOrigAnchor;
        int moveOrigLevel;
        int moveOrigRot, placeRot;

        // создание стиля
        public Styleable styleTarget;
        public int stylePart;
        public StyleData clipboard;
        public string clipboardName;

        Styleable hoverStyle;
        int hoverPart = -1;
        Color hoverColor;

        public static readonly string[] NamePool =
        {
            "Рыжик", "Шишка", "Корица", "Огонёк", "Тыковка", "Хвостик", "Брусника", "Морошка",
            "Медок", "Искорка", "Каштан", "Уголёк", "Пушинка", "Рябинка", "Зефирка", "Лучик",
        };

        public static readonly (string name, string fur, string light, string dark)[] FurPresets =
        {
            ("Рыжий", "#D9672B", "#F4EEE6", "#2E221C"),
            ("Серебристый", "#8E949B", "#EEF0F2", "#2A2A2E"),
            ("Песочный", "#D8B07A", "#F6EBDD", "#B07E4E"),
            ("Полярный", "#EDEDEA", "#FFFFFF", "#9AA0A6"),
            ("Чернобурый", "#2F2B2B", "#D9D9D9", "#151313"),
            ("Огненный", "#E0471F", "#FFE9D6", "#5A2A1A"),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Lab.StyleLab.Active) return;
            if (I == null && FindAnyObjectByType<Game>() == null)
                new GameObject("FoxLife").AddComponent<Game>();
        }

        void Awake()
        {
            if (Lab.StyleLab.Active || (I != null && I != this)) { Destroy(gameObject); return; }
            I = this;
            Application.targetFrameRate = 60;
            Application.runInBackground = true; // игра не встаёт на паузу, когда окно не в фокусе
            _ = Patterns.All;

            SetupCameraAndLight();
            weather = new GameObject("Погода").AddComponent<Weather>();
            weather.Build();
            sound = gameObject.AddComponent<SoundSystem>();
            sound.Build();

            ui = gameObject.AddComponent<GameUI>();
            StartCoroutine(BootWorld());
        }

        // ================= игра, сохранения =================

        /// <summary>true после «Новой игры» или «Продолжить»: до этого не автосохраняемся, чтобы не затереть сохранение фоном меню.</summary>
        public bool started;
        float autosaveTimer;
        const float AutosaveSeconds = 180f;

        public void StartPlaying()
        {
            started = true;
            autosaveTimer = AutosaveSeconds;
            SetSpeed(1);
            ui.screen = GameUI.MenuScreen.None;
            if (mode == Mode.Town) mode = Mode.Live;
        }

        public bool SaveGame()
        {
            bool ok = SaveSystem.Save(this);
            if (ok) Log("Игра сохранена.");
            return ok;
        }

        public void ResetWorld(bool defaults) { var e = ResetWorldAsync(defaults); while (e.MoveNext()) { if (e.Current is IEnumerator inner) while (inner.MoveNext()) { } } }

        void ClearWorld()
        {
            ClearUndo();
            pendingBefore = null;
            EndTitleScene();
            CancelPlacing();
            ClearHover();
            if (ui != null) ui.CloseMenu();
            foreach (var f in furniture) if (f != null) Destroy(f.gameObject);
            furniture.Clear();
            foreach (var f in foxes) if (f != null) Destroy(f.gameObject);
            foxes.Clear();
            selected = null;
            styleTarget = null;
            if (house != null) Destroy(house.gameObject);
            House.PlayerLotId = 0;
            foreach (var f in stored) if (f != null) Destroy(f.gameObject);
            stored.Clear();
            lotMoney.Clear();
            storedMoney.Clear();
        }

        void OnApplicationQuit()
        {
            if (started) SaveSystem.Save(this);
        }

        void SetupCameraAndLight()
        {
            foreach (var l in FindObjectsByType<Light>()) l.enabled = false;

            camera3d = Camera.main;
            if (camera3d == null)
            {
                var cg = new GameObject("Main Camera") { tag = "MainCamera" };
                camera3d = cg.AddComponent<Camera>();
            }
            // «уши» — не у камеры высоко в небе, а рядом с тем местом, куда она смотрит
            foreach (var al in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) Destroy(al);
            ears = new GameObject("Слушатель").transform;
            ears.gameObject.AddComponent<AudioListener>();
            camera3d.clearFlags = CameraClearFlags.SolidColor;
            PostFX.Load();
            if (camera3d.GetComponent<PostFX>() == null) camera3d.gameObject.AddComponent<PostFX>();
            camera3d.fieldOfView = 40f;
            camera3d.nearClipPlane = 0.3f;
            camera3d.farClipPlane = 220f;
            cam = camera3d.gameObject.GetComponent<CameraRig>();
            if (cam == null) cam = camera3d.gameObject.AddComponent<CameraRig>();
            cam.target = new Vector3(7f, 0, 4f);

            var sg = new GameObject("Sun");
            sun = sg.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            sun.shadowBias = 0.02f;
            sun.shadowNormalBias = 0.15f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.skybox = null;
            // тени и огни настраиваются в ассете URP (Settings/FoxURP): дальность теней 45 м, Forward+
        }

        void BuildDefaultFurniture()
        {
            void P(string id, int x, int z, int r = 0)
            {
                var def = Catalog.Get(id);
                var a = new Vector2Int(x, z);
                if (def == null || !CanPlace(def, a, r, null)) { Debug.LogWarning($"FoxLife: не удалось поставить {id} в {a}"); return; }
                SpawnFurniture(def, a, r, false);
            }
            // гостиная и кухня
            P("fridge", 0, 9); P("stove", 1, 9); P("counter", 2, 9); P("sink", 3, 9);
            P("table", 1, 6); P("chair", 1, 5, 2); P("chair", 2, 5, 2);
            P("rug", 4, 5); P("sofa", 5, 4, 2); P("tv", 6, 9); P("bookshelf", 8, 9);
            P("armchair", 7, 2, 2); P("easel", 1, 1, 2); P("plant", 0, 0); P("plant", 8, 0);
            P("lamp", 4, 4); P("ball", 3, 3); P("guitar", 0, 4, 3);
            // спальня
            P("bed", 10, 8); P("bed", 12, 8); P("foxbed", 11, 5); P("wardrobe", 13, 6, 1);
            P("bath", 12, 0, 1); P("plant", 9, 9); P("lamp", 13, 4); P("roundrug", 10, 2); P("computer", 9, 0, 2);
        }

        void CreateDefaultFoxes()
        {
            CreateFox("Рыжик", new Personality(8, 3, 7, 6, 4), 0, new Vector3(4.5f, 0, 2.5f), 1f, 1.05f, 1f, false);
            CreateFox("Шишка", new Personality(3, 9, 3, 8, 7), 1, new Vector3(6.5f, 0, 2.5f), 0.95f, 1.15f, 0.95f, true);
            CreateFox("Корица", new Personality(10, 6, 9, 2, 8), 2, new Vector3(11.5f, 0, 3.5f), 1.45f, 0.9f, 0.9f, true);
            foreach (var f in foxes) { f.stageDays = Random.Range(2, 9); f.ageDays = 10 + f.stageDays; f.RandomSkills(); }
            foreach (var a in foxes)
                foreach (var b in foxes)
                    if (a != b && !a.rel.ContainsKey(b)) Social.AddRel(a, b, Random.Range(15f, 35f));
            foreach (var f in foxes) f.Remember("Новая жизнь в лисьем доме");
        }

        // ================= домохозяйства =================

        /// <summary>Лисичка из нашей семьи (живёт в лисьем доме, ею управляет игрок).</summary>
        public static bool IsPlayer(Fox f) => f != null && f.home == House.PlayerLotId;
        public List<Fox> Household => foxes.FindAll(f => f.home == House.PlayerLotId);
        public int HomeCount(int home) { int n = 0; foreach (var f in foxes) if (f.home == home) n++; return n; }
        public bool KnownToPlayer(Fox f) { if (IsPlayer(f)) return true; foreach (var o in foxes) if (o.home == House.PlayerLotId && Social.Rel(o, f) >= 20) return true; return false; }

        /// <summary>Запись в журнал только о своей семье — про соседей не шумим.</summary>
        public void LogFox(Fox f, string text) { if (IsPlayer(f)) Log(text); }
        public void LogPair(Fox a, Fox b, string text) { if (IsPlayer(a) || IsPlayer(b)) Log(text); }
        public void AddMoneyFor(Fox f, int amount, string reason) { if (IsPlayer(f)) AddMoney(amount, reason); }

        /// <summary>Платное угощение или монетка: платит только наша семья.</summary>
        public void Charge(Fox f, int amount, string reason)
        {
            if (!IsPlayer(f) || amount <= 0) return;
            AddMoney(-amount, $"{f.foxName}: {reason.ToLower()}");
        }

        public Fox CreateFox(string name, Personality p, int furPreset, Vector3 pos, float ears, float tail, float size, bool female = false, LifeStage stage = LifeStage.Adult)
        {
            var fp = FurPresets[Mathf.Clamp(furPreset, 0, FurPresets.Length - 1)];
            var go = new GameObject(name);
            if (house != null && house.terrain != null)
            {
                float fy = house.FloorY(0, GridMap.CellOf(pos));
                if (Mathf.Abs(pos.y) < 0.001f || pos.y < fy - 0.3f) pos.y = fy;
            }
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            var f = go.AddComponent<Fox>();
            f.foxName = name;
            f.pers = p;
            f.earSize = ears;
            f.tailFluff = tail;
            f.size = size;
            f.female = female;
            f.stage = stage;
            for (int i = 0; i < f.needs.Length; i++) f.needs[i] = Random.Range(55f, 90f);
            f.Build(StyleData.Make("Гладкий", fp.fur), StyleData.Make("Гладкий", fp.light), StyleData.Make("Гладкий", fp.dark));
            foxes.Add(f);
            if (stage != LifeStage.Adult) f.SetStage(stage, false);
            return f;
        }

        public Fox AddRandomFox(int home = -999)
        {
            if (home == -999) home = House.PlayerLotId;
            if (HomeCount(home) >= MaxFoxes) return null;
            var homeLot = house.LotById(home);
            bool fem = Random.value < 0.5f;
            string name = Names.Pick(fem, n => foxes.Exists(f => f.foxName == n) || stored.Exists(f => f.foxName == n));
            var cell = homeLot != null ? homeLot.entry : new Vector2Int(4, 2);
            var rr = homeLot != null ? homeLot.rect : new RectInt(0, 0, House.W, House.H);
            for (int i = 0; i < 30 && !house.grid.IsFree(cell); i++) cell = new Vector2Int(Random.Range(rr.xMin, rr.xMax), Random.Range(rr.yMin, rr.yMax));
            var fox = CreateFox(name, Personality.Random(), Random.Range(0, FurPresets.Length), GridMap.Center(cell),
                Random.Range(0.85f, 1.4f), Random.Range(0.85f, 1.3f), Random.Range(0.88f, 1.08f), fem);
            fox.home = home;
            fox.stageDays = Random.Range(0, 6);
            fox.ageDays = 10 + fox.stageDays;
            fox.RandomSkills();
            foreach (var o in foxes) if (o != fox && o.home == home) Social.AddRel(fox, o, Random.Range(25f, 45f));
            LogFox(fox, $"В доме новая лисичка: {name}!");
            fox.Remember("Переезд в лисий дом");
            return fox;
        }

        public void RemoveFox(Fox f, string reason = null)
        {
            if (HomeCount(f.home) <= 1) return;
            if (IsPlayer(f)) archive.Add(new FoxRecord
            {
                id = f.id, name = f.foxName, female = f.female, mother = f.motherId, father = f.fatherId, day = Day,
                reason = reason ?? (f.female ? "ушла жить в лес" : "ушёл жить в лес"),
            });
            foreach (var o in foxes)
            {
                if (o == f || o.partnerId != f.id) continue;
                o.partnerId = null;
                o.married = false;
            }
            foreach (var o in foxes) o.romance.Remove(f);
            foreach (var o in foxes)
            {
                if (o == f) continue;
                o.CancelFor(f);
                o.rel.Remove(f);
            }
            if (f.current != null) f.Cancel(f.current);
            foreach (var fu in furniture)
                for (int i = 0; i < fu.occupants.Length; i++)
                    if (fu.occupants[i] == f) fu.occupants[i] = null;
            foxes.Remove(f);
            if (selected == f) Select(Household.Count > 0 ? Household[0] : null);
            if (styleTarget == f.style) styleTarget = null;
            Destroy(f.gameObject);
        }

        public void Select(Fox f)
        {
            if (f != null && !IsPlayer(f)) return;
            if (selected != null) selected.SetSelected(false);
            selected = f;
            if (f != null) f.SetSelected(true);
        }

        public void SetViewLevel(int level)
        {
            house.SetViewLevel(level);
        }

        /// <summary>Мебель верхних этажей прячется только на «открытом» участке.</summary>
        public bool FurnitureHidden(Furniture fu) => fu.level > house.viewLevel && fu.lot == house.focusLot;

        public void ApplyFurnitureVisibility()
        {
            foreach (var fu in furniture) if (fu != null && !fu.isGhost) fu.gameObject.SetActive(!FurnitureHidden(fu));
        }

        public void SetMode(Mode m)
        {
            if (townBuilder && (m == Mode.Live || m == Mode.Create || m == Mode.Town)) return; // в строительстве города лисички не участвуют
            CancelPlacing();
            if (m != Mode.Build && m != Mode.Style) ClearUndo(); // история правок — только пока открыт план
            SetWallTool(WallTool.None);
            SetLandTool(LandTool.None);
            mode = m;
            ClearHover();
            if (m == Mode.Create && selected != null) cam.Focus(selected.transform.position, 6f);
            if (m != Mode.Style) ui.tool = GameUI.StyleTool.Select;
        }

        // ================= время и свет =================

        /// <summary>Перевести часы на этот час сегодняшнего дня (вперёд, если час уже прошёл).</summary>
        public void SetHour(float h)
        {
            float day = Mathf.Floor(minutes / 1440f) * 1440f;
            float t = day + h * 60f;
            if (t < minutes) t += 1440f;
            minutes = t;
            lastSocialHour = (int)(minutes / 60f);
        }

        public float Hour => (minutes % 1440f) / 60f;
        public int Day => (int)(minutes / 1440f) + 1;
        static readonly string[] Week = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        public string ClockText
        {
            get
            {
                int m = (int)(minutes % 1440f);
                return $"День {Day}, {Week[(Day - 1) % 7]}  {m / 60:00}:{m % 60:00}";
            }
        }

        public void SetSpeed(int i)
        {
            speedIndex = Mathf.Clamp(i, 0, Speeds.Length - 1);
            if (speedIndex > 0) prevSpeed = speedIndex;
        }

        public void TogglePause() => SetSpeed(speedIndex == 0 ? prevSpeed : 0);

        bool lanternsLit;
        float shownGlowK = -1f;
        Transform ears;
        bool[] lotLit = new bool[0];
        int[] lotLitShown = new int[0];
        Material[] lotGlass = new Material[0];
        Material lotGlassMat(int i) => i < lotGlass.Length ? lotGlass[i] : null;
        void SetLotGlassMat(int i, Material m)
        {
            if (lotGlass.Length <= i) System.Array.Resize(ref lotGlass, house.lots.Count);
            lotGlass[i] = m;
        }
        float shadowTimer;

        /// <summary>Ближайшие к камере лампы в домах отбрасывают тени — свет не пробивает стены.</summary>
        void UpdateLightShadows()
        {
            shadowTimer -= Time.unscaledDeltaTime;
            if (shadowTimer > 0) return;
            shadowTimer = 0.5f;
            var list = new List<(float d, Light l)>();
            foreach (var f in furniture)
                if (f.lamp != null && f.lamp.enabled && f.gameObject.activeInHierarchy)
                    list.Add(((f.transform.position - cam.target).sqrMagnitude, f.lamp));
            list.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < list.Count; i++)
            {
                var want = i < 4 ? LightShadows.Hard : LightShadows.None;
                if (list[i].l.shadows != want) list[i].l.shadows = want;
            }
        }

        void UpdateSky()
        {
            float h = Hour;
            float day = Mathf.Clamp01(Mathf.InverseLerp(5.5f, 7.5f, h)) * (1f - Mathf.Clamp01(Mathf.InverseLerp(18.5f, 20.5f, h)));
            float dusk = 1f - Mathf.Abs(day * 2f - 1f);
            float t = Mathf.Clamp01((h - 6f) / 14f);
            float elev = Mathf.Lerp(50f, 15f + 45f * Mathf.Sin(Mathf.PI * t), day);
            float az = Mathf.Lerp(160f, Mathf.Lerp(100f, 260f, t), day);
            sun.transform.rotation = Quaternion.Euler(elev, az, 0);
            Color dayCol = new Color(1f, 0.96f, 0.88f), nightCol = new Color(0.5f, 0.58f, 0.9f), duskCol = new Color(1f, 0.62f, 0.38f);
            sun.color = Color.Lerp(Color.Lerp(nightCol, dayCol, day), duskCol, dusk * 0.6f);
            sun.intensity = Mathf.Lerp(0.22f, 1.1f, day);
            RenderSettings.ambientLight = Color.Lerp(new Color(0.17f, 0.19f, 0.32f), new Color(0.56f, 0.56f, 0.6f), day);
            Color sky = Color.Lerp(new Color(0.05f, 0.07f, 0.16f), new Color(0.62f, 0.8f, 0.95f), day);
            var bg = Color.Lerp(sky, new Color(0.95f, 0.6f, 0.45f), dusk * 0.5f);
            if (weather != null) weather.ApplySky(sun, camera3d, ref bg);
            camera3d.backgroundColor = bg;
            bool lampsOn = day < 0.65f;
            // свет в доме — только если там кто-то не спит; общественные места светятся в часы работы
            var lots = house.lots;
            if (lotLit.Length != lots.Count) { lotLit = new bool[lots.Count]; lotLitShown = new int[lots.Count]; for (int i = 0; i < lotLitShown.Length; i++) lotLitShown[i] = -1; }
            for (int i = 0; i < lots.Count; i++) lotLit[i] = lampsOn && lots[i].Public && h >= 7f && h < 22.5f;
            if (lampsOn)
                foreach (var fx in foxes)
                {
                    if (fx.sleeping || fx.atWork || !fx.gameObject.activeInHierarchy) continue;
                    var lt = house.LotAt(fx.Cell);
                    if (lt != null && lt.id < lotLit.Length && lt.rect.Contains(fx.Cell)) lotLit[lt.id] = true;
                }
            // окна и фонари светятся мягко, пока не стемнело (иначе вдали — пятна-блики), ночью — ярче
            float glowK = Mathf.Lerp(0.3f, 0.75f, 1f - day);
            bool glowChanged = Mathf.Abs(glowK - shownGlowK) > 0.06f;
            if (glowChanged) { shownGlowK = glowK; for (int i = 0; i < lotLitShown.Length; i++) lotLitShown[i] = -1; }
            var glow = new Color(0.95f, 0.7f, 0.35f) * shownGlowK;
            for (int i = 0; i < lots.Count; i++)
            {
                int want = lotLit[i] ? 1 : 0;
                var gm = lots[i].openings != null && lots[i].openings.parts.Count > 1 ? lots[i].openings.parts[1].mat : null;
                if (gm != null && (lotLitShown[i] != want || gm != lotGlassMat(i)))
                {
                    lotLitShown[i] = want;
                    MaterialFactory.SetHighlight(gm, want == 1 ? glow : Color.black);
                    SetLotGlassMat(i, gm);
                }
            }
            foreach (var f in furniture)
            {
                if (f.lamp == null) continue;
                bool on = f.def.id == "campfire" || (lampsOn && (f.lot < 0 || f.lot >= lotLit.Length || lotLit[f.lot]));
                on &= f.lightLevel > 0.001f && NearTitle(f.transform.position);
                f.lamp.enabled = on;
                f.ApplyLight();
            }
            foreach (var l in Props.LampLights)
            {
                if (l == null) continue;
                bool on = lampsOn && NearTitle(l.transform.position);
                if (l.enabled != on) l.enabled = on;
            }
            if (lampsOn) UpdateLightShadows();
            if (lampsOn != lanternsLit || glowChanged)
            {
                lanternsLit = lampsOn;
                foreach (var m in Props.Lanterns) if (m != null) MaterialFactory.SetHighlight(m, lampsOn ? glow : Color.black);
            }
        }

        // ================= мебель =================

        public int PlateCount => PlatesOn(House.PlayerLotId);

        public int PlatesOn(int lot) { int n = 0; foreach (var f in furniture) if (f.def.id == "plate" && f.lot == lot) n++; return n; }

        public Furniture SpawnFurniture(FurnitureDef def, Vector2Int a, int rot, bool asGhost, int level = 0)
        {
            var go = new GameObject(def.name);
            var f = go.AddComponent<Furniture>();
            f.def = def;
            f.level = level;
            f.occupants = new Fox[Mathf.Max(1, def.slots.Count)];
            f.style = go.AddComponent<Styleable>();
            f.style.displayName = def.name;
            f.model = new GameObject("model").transform;
            f.model.SetParent(go.transform, false);
            f.Place(a, rot);
            var lt = house.LotAt(a);
            f.lot = lt != null ? lt.id : -1;
            def.build(f, f.style, f.model);
            Voxelizer.Bake(f.model, Voxelizer.FurnitureVoxel, null, true, null, def.id == "plate" ? null : "furn:" + def.id);
            if (f.lamp != null) foreach (var r in f.model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (asGhost) { f.isGhost = true; f.SetColliders(false); }
            else { Register(f); MediaFX.Attach(f); WaterFX.Attach(f); if (def.id == "plate") SettlePlate(f); }
            return f;
        }

        void RefreshGridAround(Furniture f)
        {
            var e = f.Eff;
            int r = Mathf.Max(e.x, e.y) + 2;
            house.UpdateGrid(new RectInt(f.anchor.x - r, f.anchor.y - r, e.x + r * 2, e.y + r * 2));
        }

        void Register(Furniture f)
        {
            if (!furniture.Contains(f)) furniture.Add(f);
            var lt = house.LotAt(f.anchor);
            f.lot = lt != null ? lt.id : -1;
            if (!f.def.walkable) foreach (var c in f.Footprint()) house.grids[f.level].SetOcc(c, f);
            if (f.def.stair) house.Rebuild(lt);
            f.gameObject.SetActive(!FurnitureHidden(f));
            if (IsBridge(f.def)) RefreshGridAround(f);
        }

        void Unregister(Furniture f)
        {
            furniture.Remove(f);
            house.grids[f.level].Clear(f);
            if (f.def.stair) house.Rebuild(house.LotById(f.lot));
            if (IsBridge(f.def)) RefreshGridAround(f);
        }

        public void RemoveFurniture(Furniture f)
        {
            if (f == null) return;
            foreach (var fox in foxes) fox.CancelFor(f);
            Unregister(f);
            if (styleTarget == f.style) styleTarget = null;
            if (hoverStyle == f.style) hoverStyle = null;
            Destroy(f.gameObject);
        }

        /// <summary>Можно ли поставить. lotId — на каком участке (-1 — на том, где угол предмета).</summary>
        public bool CanPlace(FurnitureDef def, Vector2Int a, int rot, Furniture ignore, int level = 0, int lotId = -1)
        {
            var g = house.grids[level];
            if (def.stair && level + 1 >= House.Levels) return false;
            var lot = lotId >= 0 ? house.LotById(lotId) : house.LotAt(a);
            if (lot == null && !townBuilder) return false;
            var cells = new List<Vector2Int>(Furniture.FootprintAt(def, a, rot));
            foreach (var c in cells)
            {
                if (!g.InBounds(c) || (lot != null && !lot.rect.Contains(c))) return false;
                if (townBuilder && lot == null && house.LotAt(c) != null) return false;
                if (level == 0 && !house.HasFloor(0, c) && !IsBridge(def)) return false; // не в воду и не на склон (мосты — можно)
                if (level > 0 && !house.HasFloor(level, c)) return false; // наверху — только на полу комнат
                if (!def.walkable)
                {
                    var o = g.At(c);
                    if (o != null && o != ignore) return false;
                }
            }
            if (level == 0 && !IsBridge(def))
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var c in cells) { float y = house.FloorY(0, c); lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                if (hi - lo > 0.3f) return false; // на неровной земле не встанет
            }
            foreach (var c in cells)
            {
                var r = c + Vector2Int.right;
                var u = c + Vector2Int.up;
                if (cells.Contains(r) && g.HasStructure(c, r)) return false;
                if (cells.Contains(u) && g.HasStructure(c, u)) return false;
            }
            return true;
        }

        /// <summary>Стол (или другая поверхность) перед стулом.</summary>
        public Furniture TableInFront(Furniture chair)
        {
            var c = GridMap.CellOf(chair.transform.TransformPoint(new Vector3(0, 0, -1f)));
            var t = house.grids[chair.level].At(c);
            return t != null && Catalog.SurfaceHeight(t.def.id) > 0 ? t : null;
        }

        /// <summary>Тарелка остаётся на столе перед стулом; несколько — складываются стопкой.</summary>
        public Furniture SpawnPlateOnTable(Fox fox, Furniture chair)
        {
            var t = chair != null ? TableInFront(chair) : null;
            if (t == null) return SpawnPlate(fox);
            if (PlatesOn(t.lot) >= 12) return null;
            var c = GridMap.CellOf(chair.transform.TransformPoint(new Vector3(0, 0, -1f)));
            return SpawnFurniture(Catalog.Get("plate"), c, Random.Range(0, 4), false, chair.level);
        }

        /// <summary>Тарелку на столе поднимаем на столешницу, поверх других — стопкой (чуть вкривь).</summary>
        void SettlePlate(Furniture f)
        {
            var under = house.grids[f.level].At(f.anchor);
            float h = under != null ? Catalog.SurfaceHeight(under.def.id) : -1f;
            int k = 0;
            foreach (var o in furniture) if (o != f && o.def.id == "plate" && o.anchor == f.anchor && o.level == f.level) k++;
            float y = Mathf.Max(0, h) + k * 0.028f;
            f.model.localPosition = new Vector3(f.model.localPosition.x, y, f.model.localPosition.z);
            if (k > 0) f.model.localRotation = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(0, 360f), Random.Range(-3f, 3f));
        }

        public Furniture SpawnPlate(Fox fox)
        {
            var here = house.LotAt(fox.Cell);
            if (PlatesOn(here != null ? here.id : -1) >= 8) return null;
            var def = Catalog.Get("plate");
            var start = fox.Cell;
            int lvl = fox.Level;
            var opts = new List<Vector2Int> { start };
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (dx != 0 || dz != 0) opts.Add(start + new Vector2Int(dx, dz));
            foreach (var c in opts)
            {
                if (!house.grids[lvl].IsFree(c) || house.LotAt(c) == null) continue;
                bool taken = false;
                foreach (var f in furniture) if (f.def.id == "plate" && f.anchor == c && f.level == lvl) { taken = true; break; }
                if (taken) continue;
                return SpawnFurniture(def, c, Random.Range(0, 4), false, lvl);
            }
            return null;
        }

        // ---------- режим обустройства ----------

        // ================= семья =================

        void NewDay()
        {
            lastAgeDay = Day;
            foreach (var f in new List<Fox>(foxes))
            {
                if (f == null || !f.AgeOneDay(lifespan)) continue;
                // уходят только если в доме останется хотя бы один взрослый или подросток
                int grown = foxes.FindAll(o => o != f && o.home == f.home && o.stage >= LifeStage.Teen).Count;
                if (grown > 0) PassAway(f);
            }
        }

        // ================= переезд =================

        public int LotFurnitureValue(int lotId) { int v = 0; foreach (var f in furniture) if (f.lot == lotId) v += f.def.price; return v; }
        public int BuyPrice(int lotId) => 2000 + LotFurnitureValue(lotId);
        public int SellPrice(int lotId) => 1500 + LotFurnitureValue(lotId) / 2;

        /// <summary>Свободные дома и участки: жилые участки, где никто не живёт.</summary>
        public List<House.Lot> VacantHomes() => house.lots.FindAll(l => !l.removed && l.kind == LotKind.Home && l.id != House.PlayerLotId && HomeCount(l.id) == 0);

        /// <summary>Продать свой дом, купить другой и переехать всей семьёй.</summary>
        public bool MoveTo(int lotId)
        {
            var lot = house.LotById(lotId);
            int old = House.PlayerLotId;
            if (lot == null || lotId == old || HomeCount(lotId) > 0) return false;
            int price = BuyPrice(lotId), sale = SellPrice(old);
            if (!freeBuild && money + sale < price) { Log($"Не хватает монет на переезд: нужно ещё {price - sale - Mathf.RoundToInt(money)}."); return false; }
            CancelPlacing();
            SetWallTool(WallTool.None);
            SetLandTool(LandTool.None);
            var oldLot = house.PlayerLot;
            int grant = MoveInGrant(lotId);
            if (!freeBuild) { AddMoney(sale, "Продажа: " + oldLot.name); AddMoney(-price, "Покупка: " + lot.name); }
            House.PlayerLotId = lotId;
            if (!freeBuild && grant > 0) AddMoney(grant, "Подъёмные на обустройство");
            int i = 0;
            foreach (var f in foxes)
            {
                if (f.home != old) continue;
                f.home = lotId;
                f.Remember("Переезд: " + lot.name);
                f.AddMoodlet("Новый дом!", 8, 1440);
                if (f.atWork) continue;
                f.CancelCurrentPublic();
                f.queue.Clear();
                var c = lot.entry + new Vector2Int(i % 3 - 1, i / 3);
                if (!house.grid.IsFree(c)) c = lot.entry;
                f.transform.position = GridMap.Center(c) + Vector3.up * house.FloorY(0, c);
                i++;
            }
            house.focusLot = -2;
            cam.Focus(GridMap.Center(lot.entry), 18f);
            Log($"Семья переехала: {lot.name}!");
            return true;
        }

        public void PassAway(Fox f)
        {
            if (KnownToPlayer(f)) Log($"{f.foxName} {(f.female ? "ушла" : "ушёл")} в звёздный лес... Светлая память.");
            foreach (var o in foxes)
            {
                if (o == f) continue;
                float rel = Social.Rel(o, f);
                string role = Family.Role(o, f);
                if (o.home != f.home && rel < 30 && role == null) continue;
                float grief = 8f + Mathf.Max(0, rel) / 8f + (role != null ? 8f : 0f);
                o.AddMoodlet("Тоска: " + f.foxName, -grief, 4320);
                o.Remember($"Прощание: {f.foxName} {(f.female ? "ушла" : "ушёл")} в звёздный лес", false);
            }
            RemoveFox(f, f.female ? "ушла в звёздный лес" : "ушёл в звёздный лес");
        }

        public void MakeCouple(Fox a, Fox b)
        {
            a.partnerId = b.id;
            b.partnerId = a.id;
            a.Remember("Вместе с " + b.foxName + "!");
            b.Remember("Вместе с " + a.foxName + "!");
            LogPair(a, b, $"{a.foxName} и {b.foxName} теперь пара!");
            a.OnEvent(new GameEvent { kind = "partner", other = b });
            b.OnEvent(new GameEvent { kind = "partner", other = a });
        }

        public void Marry(Fox a, Fox b)
        {
            a.married = b.married = true;
            a.AddMoodlet("Свадьба!", 20, 1440);
            b.AddMoodlet("Свадьба!", 20, 1440);
            a.Remember("Свадьба с " + b.foxName);
            b.Remember("Свадьба с " + a.foxName);
            LogPair(a, b, $"{a.foxName} и {b.foxName} поженились!");
            foreach (var o in foxes) if (o != a && o != b && (o.home == a.home || o.home == b.home)) o.AddMoodlet("Свадьба в семье", 6, 720);
            MoveIn(a, b);
            a.OnEvent(new GameEvent { kind = "married", other = b });
            b.OnEvent(new GameEvent { kind = "married", other = a });
        }

        /// <summary>После свадьбы супруги живут вместе: сосед переезжает к нам (если есть место), иначе — к супругу.</summary>
        void MoveIn(Fox a, Fox b)
        {
            if (a.home == b.home) return;
            Fox mover = IsPlayer(a) ? b : IsPlayer(b) ? a : b;
            Fox stay = mover == a ? b : a;
            if (HomeCount(stay.home) >= MaxFoxes) return;
            mover.home = stay.home;
            mover.Remember("Переезд к " + stay.foxName);
            if (IsPlayer(mover)) Log($"{mover.foxName} {(mover.female ? "переехала" : "переехал")} в лисий дом!");
            if (IsPlayer(stay) && selected == null) Select(stay);
        }

        public void StartPregnancy(Fox a, Fox b)
        {
            var mother = a.female ? a : b;
            var father = a.female ? b : a;
            mother.pregnantMinutes = 0;
            mother.babyFatherId = father.id;
            LogPair(mother, father, $"{mother.foxName} и {father.foxName} ждут лисят!");
            mother.Remember("Скоро будут лисята!");
            father.Remember("Скоро будут лисята!");
        }

        Fox NewKit(Fox a, Fox b, Vector3 pos, int home = 0)
        {
            bool kitFemale = Random.value < 0.5f;
            string name = Names.Pick(kitFemale, n => foxes.Exists(x => x.foxName == n) || archive.Exists(x => x.name == n) || stored.Exists(x => x.foxName == n));
            Personality pers = a != null ? Family.InheritPersonality(a, b) : Personality.Random();
            float ears = a != null ? (a.earSize + (b != null ? b.earSize : a.earSize)) / 2f + Random.Range(-0.1f, 0.1f) : Random.Range(0.85f, 1.4f);
            float tail = a != null ? (a.tailFluff + (b != null ? b.tailFluff : a.tailFluff)) / 2f + Random.Range(-0.1f, 0.1f) : Random.Range(0.85f, 1.3f);
            float size = a != null ? (a.size + (b != null ? b.size : a.size)) / 2f + Random.Range(-0.04f, 0.04f) : Random.Range(0.9f, 1.05f);
            var kit = CreateFox(name, pers, Random.Range(0, FurPresets.Length), pos, ears, tail, size, kitFemale, LifeStage.Kit);
            kit.home = home;
            if (a != null)
                for (int part = 0; part < 3; part++) kit.style.SetStyle(part, Family.InheritStyle(a, b, part));
            for (int i = 0; i < kit.needs.Length; i++) kit.needs[i] = 85f;
            foreach (var o in foxes)
            {
                if (o == kit || o.home != home) continue;
                string role = Family.Role(kit, o);
                Social.AddRel(kit, o, role == "мама" || role == "папа" ? 60f : role != null ? 40f : 20f);
            }
            foreach (var o in foxes) if (o != kit && o.home == home) o.AddMoodlet("Пополнение в семье", 12, 1440);
            return kit;
        }

        public void BirthKits(Fox mother)
        {
            var father = foxes.Find(f => f.id == mother.babyFatherId);
            int count = Random.value < 0.15f ? 2 : 1;
            for (int i = 0; i < count && HomeCount(mother.home) < MaxFoxes; i++)
            {
                var pos = mother.transform.position + new Vector3(Random.Range(-0.4f, 0.4f), 0, Random.Range(-0.4f, 0.4f));
                pos.y = 0;
                var kit = NewKit(null, null, pos, mother.home);
                kit.motherId = mother.id;
                kit.fatherId = mother.babyFatherId;
                // гены считаем после того, как известны родители
                kit.pers = Family.InheritPersonality(mother, father);
                for (int part = 0; part < 3; part++) kit.style.SetStyle(part, Family.InheritStyle(mother, father, part));
                foreach (var o in foxes)
                {
                    if (o == kit) continue;
                    string role = Family.Role(kit, o);
                    if (role != null) Social.AddRel(kit, o, 40f);
                }
                if (KnownToPlayer(mother)) Log($"У {mother.foxName} родился лисёнок: {kit.foxName}!");
                mother.Remember("Родился лисёнок: " + kit.foxName);
                if (father != null) father.Remember("Родился лисёнок: " + kit.foxName);
                kit.Remember(mother.foxName + (father != null ? " и " + father.foxName : "") + " — мои родители");
                mother.OnEvent(new GameEvent { kind = "baby" });
                if (father != null) father.OnEvent(new GameEvent { kind = "baby" });
            }
        }

        public void AdoptKit(Fox parent)
        {
            if (HomeCount(parent.home) >= MaxFoxes) { parent.Say("Дом уже полон..."); return; }
            if (money < 300 && !freeBuild) { parent.Say("Не хватает денег..."); Log("Для приюта нужно 300 мон."); return; }
            if (!freeBuild) AddMoney(-300, "Взнос в лисий приют");
            var cell = GridMap.CellOf(parent.transform.position);
            var kit = NewKit(null, null, GridMap.Center(cell) + new Vector3(0.3f, 0, 0.3f), parent.home);
            if (parent.female) kit.motherId = parent.id; else kit.fatherId = parent.id;
            var partner = parent.Partner;
            if (partner != null) { if (partner.female) kit.motherId = partner.id; else kit.fatherId = partner.id; }
            Social.AddRel(kit, parent, 40f);
            if (partner != null) Social.AddRel(kit, partner, 40f);
            Log($"{parent.foxName} берёт из приюта лисёнка: {kit.foxName}!");
            parent.Remember("Из приюта в семью: " + kit.foxName);
            parent.OnEvent(new GameEvent { kind = "baby" });
            if (partner != null) partner.OnEvent(new GameEvent { kind = "baby" });
        }

        // ================= стены, двери, окна =================

        public enum WallTool { None, Wall, Erase, Door, Window, Arch, Fence, Gate, Railing, RailingGap }
        public WallTool wallTool;
        public const int WallPrice = 10, DoorPrice = 120, WindowPrice = 80, ArchPrice = 40, FencePrice = 6, GatePrice = 30, RailingPrice = 15, RailingGapPrice = 10;
        bool wallDragging;
        Vector2Int wallStart, wallEnd;
        GameObject wallGhost;
        Material wallGhostMat;

        public void SetWallTool(WallTool t)
        {
            wallTool = t;
            if (t != WallTool.None) landTool = LandTool.None;
            wallDragging = false;
            HideWallGhost();
        }

        void HideWallGhost() { if (wallGhost != null && wallGhost.activeSelf) wallGhost.SetActive(false); }

        void ShowWallGhost(Vector3 center, Vector3 size, Color c)
        {
            if (wallGhost == null)
            {
                wallGhost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wallGhost.name = "wall ghost";
                Destroy(wallGhost.GetComponent<Collider>());
                wallGhostMat = MaterialFactory.CreateGlass();
                MaterialFactory.Apply(wallGhostMat, StyleData.Make("Гладкий", "#FFFFFF"));
                wallGhost.GetComponent<Renderer>().sharedMaterial = wallGhostMat;
                wallGhost.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            wallGhost.SetActive(true);
            wallGhost.transform.position = center;
            wallGhost.transform.localScale = size;
            wallGhostMat.SetColor("_Color", c);
            wallGhostMat.SetFloat("_Alpha", 0.45f);
        }

        /// <summary>Высота плоскости строительства: пол выбранного этажа на «открытом» участке (или земля под камерой).</summary>
        float ViewPlaneY()
        {
            var lot = house.LotById(house.focusLot) ?? house.LotAt(GridMap.CellOf(cam.target));
            float b = lot != null ? Town.LotElev(lot.id) : house.GroundUnder(cam.target);
            return b + House.LevelY(house.viewLevel);
        }

        bool GroundPoint(out Vector3 p)
        {
            p = default;
            var ray = camera3d.ScreenPointToRay(Input.mousePosition);
            return FloorPoint(ray, out p);
        }

        RectInt wallRect;

        static Vector3 RandXZ(float r) { var v = Random.insideUnitCircle * r; return new Vector3(v.x, 0, v.y); }

        void UpdateWallTool()
        {
            if (ui.MouseOverUI && !wallDragging) { HideWallGhost(); return; }
            if (!GroundPoint(out var p)) return;
            int lvl = house.viewLevel;
            float ly = ViewPlaneY();
            if ((wallTool == WallTool.Fence || wallTool == WallTool.Gate) && lvl > 0) { HideWallGhost(); if (Input.GetMouseButtonDown(0)) Log("Заборы ставятся только во дворе, на первом этаже."); return; }
            if (wallTool == WallTool.Wall || wallTool == WallTool.Erase || wallTool == WallTool.Fence || wallTool == WallTool.Railing)
            {
                if (!wallDragging) wallRect = townBuilder ? (house.LotAt(GridMap.CellOf(p))?.rect ?? house.PlayerLot.rect) : house.PlayerLot.rect;
                var pr = wallRect;
                var v = new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(p.x), pr.xMin, pr.xMax), Mathf.Clamp(Mathf.RoundToInt(p.z), pr.yMin, pr.yMax));
                var col = wallTool == WallTool.Erase ? new Color(0.95f, 0.35f, 0.3f) : new Color(0.4f, 0.9f, 0.45f);
                float gh = wallTool == WallTool.Fence ? House.FenceH : wallTool == WallTool.Railing ? 0.95f : 2.02f;
                if (!wallDragging)
                {
                    ShowWallGhost(new Vector3(v.x, ly + gh / 2f, v.y), new Vector3(0.16f, gh, 0.16f), col);
                    if (Input.GetMouseButtonDown(0)) { wallDragging = true; wallStart = v; }
                    return;
                }
                wallEnd = v;
                if (Mathf.Abs(wallEnd.x - wallStart.x) >= Mathf.Abs(wallEnd.y - wallStart.y)) wallEnd.y = wallStart.y; else wallEnd.x = wallStart.x;
                var a = new Vector3(wallStart.x, ly, wallStart.y);
                var b = new Vector3(wallEnd.x, ly, wallEnd.y);
                var size = new Vector3(Mathf.Abs(b.x - a.x) + 0.16f, gh, Mathf.Abs(b.z - a.z) + 0.16f);
                ShowWallGhost((a + b) / 2f + Vector3.up * gh / 2f, size, col);
                if (Input.GetMouseButtonDown(1)) { wallDragging = false; return; }
                if (Input.GetMouseButtonUp(0))
                {
                    wallDragging = false;
                    var wt = wallTool; var ws = wallStart; var we = wallEnd;
                    Recorded(wt == WallTool.Erase ? "снос стен" : wt == WallTool.Fence ? "забор" : wt == WallTool.Railing ? "перила" : "стены",
                        () => ApplyWallLine(ws, we, wt == WallTool.Erase, wt == WallTool.Fence ? EdgeKind.Fence : wt == WallTool.Railing ? EdgeKind.Railing : EdgeKind.Wall, lvl));
                }
                return;
            }

            // двери, окна, проёмы — на ребро под курсором
            var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
            float fx = p.x - c.x, fz = p.z - c.y;
            float[] d = { fx, 1 - fx, fz, 1 - fz };
            int k = 0; for (int i = 1; i < 4; i++) if (d[i] < d[k]) k = i;
            int ex = k == 1 ? c.x + 1 : c.x, ez = k == 3 ? c.y + 1 : c.y;
            bool vert = k <= 1;
            if (!House.ValidEdge(ex, ez, vert) || !MayEditLot(house.EdgeLot(ex, ez, vert))) { HideWallGhost(); return; }
            var kind = house.GetEdge(lvl, ex, ez, vert);
            var want = wallTool == WallTool.Door ? EdgeKind.Door : wallTool == WallTool.Window ? EdgeKind.Window : wallTool == WallTool.Gate ? EdgeKind.Gate : wallTool == WallTool.RailingGap ? EdgeKind.RailingOpen : EdgeKind.Arch;
            bool ok = want == EdgeKind.Gate ? kind == EdgeKind.Fence : want == EdgeKind.RailingOpen ? kind == EdgeKind.Railing : House.IsWallLike(kind) && kind != want;
            var center = vert ? new Vector3(ex, ly + 1f, ez + 0.5f) : new Vector3(ex + 0.5f, ly + 1f, ez);
            ShowWallGhost(center, vert ? new Vector3(0.22f, 2.04f, 1f) : new Vector3(1f, 2.04f, 0.22f), ok ? new Color(0.45f, 0.7f, 1f) : new Color(0.95f, 0.35f, 0.3f));
            if (Input.GetMouseButtonDown(0) && !ui.MouseOverUI)
            {
                if (!ok) { Log(want == EdgeKind.RailingOpen ? "Вход делается в перилах." : want == EdgeKind.Gate ? "Калитка ставится в забор." : kind == EdgeKind.None ? "Сначала поставь здесь стену." : "Здесь уже это стоит."); return; }
                int price = want == EdgeKind.Door ? DoorPrice : want == EdgeKind.Window ? WindowPrice : want == EdgeKind.Gate ? GatePrice : want == EdgeKind.RailingOpen ? RailingGapPrice : ArchPrice;
                if (!freeBuild && money < price) { Log($"Не хватает монет: нужно {price}."); return; }
                var doorBefore = Snap();
                if (!freeBuild) AddMoney(-price, want == EdgeKind.Door ? "Дверь" : want == EdgeKind.Window ? "Окно" : want == EdgeKind.Gate ? "Калитка" : want == EdgeKind.RailingOpen ? "Вход в перилах" : "Проём");
                house.SetEdge(lvl, ex, ez, vert, want);
                house.Rebuild(house.LotById(house.EdgeLot(ex, ez, vert)) ?? house.PlayerLot);
                PushBuild(want == EdgeKind.Door ? "дверь" : want == EdgeKind.Window ? "окно" : want == EdgeKind.Gate ? "калитка" : "проём", doorBefore, Snap());
                foreach (var fx2 in foxes) fx2.OnEvent(new GameEvent { kind = "buy" });
            }
        }

        bool EdgeCutsFurniture(int level, int x, int z, bool v)
        {
            House.Sides(x, z, v, out var a, out var b);
            var fa = house.grids[level].At(a);
            return fa != null && fa == house.grids[level].At(b);
        }

        void ApplyWallLine(Vector2Int s, Vector2Int e, bool erase, EdgeKind build, int level)
        {
            int unit = build == EdgeKind.Fence ? FencePrice : build == EdgeKind.Railing ? RailingPrice : WallPrice;
            bool alongX = s.y == e.y;
            int from = alongX ? Mathf.Min(s.x, e.x) : Mathf.Min(s.y, e.y);
            int to = alongX ? Mathf.Max(s.x, e.x) : Mathf.Max(s.y, e.y);
            int changed = 0, cost = 0, refund = 0;
            bool blocked = false;
            for (int i = from; i < to; i++)
            {
                int x = alongX ? i : s.x, z = alongX ? s.y : i;
                bool v = !alongX;
                if (!House.ValidEdge(x, z, v) || !MayEditLot(house.EdgeLot(x, z, v))) continue;
                var k = house.GetEdge(level, x, z, v);
                if (erase)
                {
                    if (k == EdgeKind.None) continue;
                    // стены, двери и окна возвращаются по полной цене — стройка без страха ошибиться
                    refund += (House.IsFenceLike(k) ? FencePrice : House.IsRailing(k) ? RailingPrice : WallPrice) + (k == EdgeKind.Door ? DoorPrice : k == EdgeKind.Window ? WindowPrice : k == EdgeKind.Arch ? ArchPrice : k == EdgeKind.Gate ? GatePrice : k == EdgeKind.RailingOpen ? RailingGapPrice : 0);
                    house.SetEdge(level, x, z, v, EdgeKind.None);
                    changed++;
                }
                else
                {
                    if (k != EdgeKind.None) continue;
                    if (EdgeCutsFurniture(level, x, z, v)) { blocked = true; continue; }
                    if (!freeBuild && money - cost < unit) { Log("Не хватило монет на всю длину."); break; }
                    cost += unit;
                    house.SetEdge(level, x, z, v, build);
                    changed++;
                }
            }
            if (changed == 0) { if (blocked) Log("Стена не может пройти сквозь мебель."); return; }
            if (!freeBuild && cost > 0) AddMoney(-cost, build == EdgeKind.Fence ? $"Забор ({changed} м)" : build == EdgeKind.Railing ? $"Перила ({changed} м)" : $"Стены ({changed} м)");
            if (!freeBuild && refund > 0) AddMoney(refund, "Снос стен");
            if (blocked) Log("Часть стены не прошла сквозь мебель.");
            var lotHere = house.LotById(house.EdgeLot(s.x, s.y, s.y != e.y));
            house.Rebuild(lotHere ?? house.PlayerLot);
        }

        // ================= ландшафт и сад =================

        public enum LandTool { None, Raise, Lower, Flatten, Pond, Fill, Flowers, Tree, Pine, Remove, Smooth, Road, Path, Lawn, Undergrowth, Stones, StonesX }
        public bool spray; // кисть-спрей для дорог, газона и подлеска

        /// <summary>Можно ли тут менять землю и сажать: в строительстве города — везде, в игре — только на своём участке и вокруг.</summary>
        bool InsideEditable(Vector3 q)
        {
            if (townBuilder) return true;
            int lot = Town.LotIdAt(q.x, q.z);
            return lot < 0 || lot == House.PlayerLotId;
        }
        public LandTool landTool;
        public float brushRadius = 1.5f;
        public const int FlowerPrice = 5, TreePrice = 40;
        float brushTimer, flattenH;

        static string LandToolName(LandTool t)
        {
            switch (t)
            {
                case LandTool.Raise: return "подъём земли";
                case LandTool.Lower: return "углубление земли";
                case LandTool.Flatten: return "выравнивание";
                case LandTool.Smooth: return "сглаживание";
                case LandTool.Pond: return "пруд";
                case LandTool.Fill: return "засыпка воды";
                case LandTool.Flowers: return "цветы";
                case LandTool.Tree: case LandTool.Pine: return "деревья";
                case LandTool.Remove: return "уборка растений";
                default: return "покраска земли";
            }
        }

        public void SetLandTool(LandTool t)
        {
            EndStroke();
            landTool = t;
            if (t != LandTool.None) SetWallTool(WallTool.None);
            HideWallGhost();
        }

        bool TerrainPoint(out Vector3 p)
        {
            p = default;
            var ray = camera3d.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 300f);
            float best = float.MaxValue;
            bool found = false;
            foreach (var h in hits)
            {
                bool ok = h.collider.GetComponent<TerrainChunk>() != null || (h.collider.GetComponent<StyleRef>() is StyleRef s && s.owner != null && s.owner.hidden && s.owner.kind == SurfaceKind.Outdoor);
                if (ok && h.distance < best) { best = h.distance; p = h.point; found = true; }
            }
            return found;
        }

        void UpdateLandTool()
        {
            if (ui.MouseOverUI) { HideWallGhost(); return; }
            if (!TerrainPoint(out var p)) { HideWallGhost(); return; }
            bool plant = landTool == LandTool.Flowers || landTool == LandTool.Tree || landTool == LandTool.Pine;
            float r = brushRadius;
            var col = landTool == LandTool.Lower || landTool == LandTool.Pond || landTool == LandTool.Remove ? new Color(0.95f, 0.45f, 0.35f)
                : landTool == LandTool.Raise || landTool == LandTool.Fill ? new Color(0.5f, 0.85f, 0.45f) : new Color(0.5f, 0.75f, 1f);
            ShowWallGhost(p + Vector3.up * 0.05f, new Vector3(r * 2f, 0.06f, r * 2f), col);

            if (Input.GetMouseButtonDown(0)) { brushTimer = 0; flattenH = p.y; BeginStroke(LandToolName(landTool)); }
            if (!Input.GetMouseButton(0)) { EndStroke(); return; }
            brushTimer -= Time.unscaledDeltaTime;
            if (brushTimer > 0) return;
            brushTimer = plant || landTool == LandTool.Remove ? 0.25f : landTool >= LandTool.Road ? 0.04f : 0.12f;
            var t = house.terrain;
            switch (landTool)
            {
                case LandTool.Raise: Brush(p, VoxelTerrain.Brush.Raise); break;
                case LandTool.Lower: Brush(p, VoxelTerrain.Brush.Lower); break;
                case LandTool.Flatten: Brush(p, VoxelTerrain.Brush.Flatten); break;
                case LandTool.Pond: Brush(p, VoxelTerrain.Brush.Pond); break;
                case LandTool.Fill: Brush(p, VoxelTerrain.Brush.Fill); break;
                case LandTool.Smooth: Brush(p, VoxelTerrain.Brush.Smooth); break;
                case LandTool.Road: case LandTool.Path: case LandTool.Lawn: case LandTool.Undergrowth: case LandTool.Stones: case LandTool.StonesX:
                {
                    var v = landTool == LandTool.Road ? VoxelTerrain.Paint.Road : landTool == LandTool.Path ? VoxelTerrain.Paint.Path
                        : landTool == LandTool.Lawn ? VoxelTerrain.Paint.Grass : landTool == LandTool.Stones ? VoxelTerrain.Paint.Stones : landTool == LandTool.StonesX ? VoxelTerrain.Paint.StonesX : VoxelTerrain.Paint.Forest;
                    if (t.PaintBrush(p, brushRadius, v, spray) && landTool == LandTool.Undergrowth && Random.value < (spray ? 0.12f : 0.35f))
                    {
                        // подлесок: папоротники и кустики там, где ещё пусто
                        var q = p + RandXZ(brushRadius);
                        if (!OnBuilding(q) && !t.IsWaterCell(GridMap.CellOf(q))) t.AddFlowers(new Vector3(q.x, 0, q.z), -1000 - Random.Range(0, 99999));
                    }
                    break;
                }
                case LandTool.Flowers:
                {
                    // кисть: чем больше, тем больше кустиков цветов за раз
                    int count = Mathf.Clamp(Mathf.RoundToInt(brushRadius * brushRadius * 0.6f), 1, 30);
                    for (int i = 0; i < count; i++)
                    {
                        var q = p + RandXZ(brushRadius);
                        if (OnBuilding(q) || !InsideEditable(q)) continue;
                        if (!freeBuild && money < FlowerPrice) { Log("Не хватает монет."); return; }
                        q.y = t.GroundAt(q.x, q.z);
                        t.AddFlowers(q, Random.Range(0, 100000));
                        if (!freeBuild) money -= FlowerPrice;
                    }
                    break;
                }
                case LandTool.Tree:
                case LandTool.Pine:
                {
                    if (!Input.GetMouseButtonDown(0)) return; // деревья — рощицей на клик
                    int count = Mathf.Clamp(Mathf.RoundToInt(brushRadius * brushRadius / 5f), 1, 12);
                    var placed = new List<Vector3>();
                    for (int i = 0; i < count * 4 && placed.Count < count; i++)
                    {
                        var q = count == 1 ? p : p + RandXZ(brushRadius);
                        if (OnBuilding(q) || !InsideEditable(q)) continue;
                        if (placed.Exists(o => (o - q).sqrMagnitude < 2.6f * 2.6f)) continue;
                        if (!freeBuild && money < TreePrice) { Log("Не хватает монет."); break; }
                        house.PlantTree(q, Random.Range(0.75f, 1.25f), landTool == LandTool.Pine);
                        placed.Add(q);
                        if (!freeBuild) AddMoney(-TreePrice, landTool == LandTool.Pine ? "Посадка ёлки" : "Посадка дерева");
                    }
                    if (placed.Count == 0) Log("Здесь дерево не посадить.");
                    break;
                }
                case LandTool.Remove:
                    int n = t.RemoveFlowers(p, brushRadius) + house.RemoveTrees(p, brushRadius * 0.6f);
                    if (n > 0) Log($"Убрано растений: {n}");
                    break;
            }
        }

        bool OnBuilding(Vector3 p)
        {
            var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
            return house.RoomIndexAt(0, c) >= 0;
        }

        void Brush(Vector3 p, VoxelTerrain.Brush b)
        {
            if (house.terrain.ApplyBrush(p, brushRadius, b, flattenH))
            {
                house.SettleTrees(p, brushRadius);
                int r = Mathf.CeilToInt(brushRadius) + 2;
                house.UpdateGrid(new RectInt(Mathf.FloorToInt(p.x) - r, Mathf.FloorToInt(p.z) - r, r * 2 + 1, r * 2 + 1));
            }
        }

        public bool CanAfford(FurnitureDef def) => freeBuild || money >= def.price;

        public void AddMoney(int amount, string reason)
        {
            if (amount == 0) return;
            money += amount;
            if (amount > 0 && started && sound != null) sound.Coin();
            Log($"{reason}: {(amount > 0 ? "+" : "")}{amount} мон.");
        }

        /// <summary>Сутки после покупки вещь сдаётся по полной цене, потом — за 75%.</summary>
        public const float RefundMinutes = 1440f;
        public bool FullRefund(Furniture f) => f.boughtAt >= 0 && minutes - f.boughtAt < RefundMinutes;
        public int SellValue(Furniture f) => FullRefund(f) ? f.def.price : Mathf.RoundToInt(f.def.price * 0.75f);

        public void SellFurniture(Furniture f)
        {
            var before = (mode == Mode.Build || mode == Mode.Style) ? Snap() : null;
            int refund = freeBuild ? 0 : SellValue(f);
            RemoveFurniture(f);
            if (refund > 0) AddMoney(refund, "Продано: " + f.def.name);
            if (before != null) PushBuild("продажа: " + f.def.name, before, Snap());
        }

        public void NotifyStyle()
        {
            if (!started || Time.unscaledTime - lastStyleNotify < 1f) return;
            lastStyleNotify = Time.unscaledTime;
            foreach (var f in foxes) f.OnEvent(new GameEvent { kind = "style" });
        }

        public void StartPlacing(FurnitureDef def)
        {
            if (!CanAfford(def)) { Log($"Не хватает монет: {def.name} стоит {def.price}."); return; }
            CancelPlacing();
            movingExisting = false;
            placeRot = 0;
            placeFine = 0;
            ghost = SpawnFurniture(def, Vector2Int.zero, 0, true, house.viewLevel);
        }

        void StartMoving(Furniture f)
        {
            CancelPlacing();
            pendingBefore = Snap();
            foreach (var fox in foxes) fox.CancelFor(f);
            Unregister(f);
            moveOrigAnchor = f.anchor;
            moveOrigLevel = f.level;
            moveOrigRot = f.rot;
            moveOrigFine = f.fineYaw;
            placeRot = f.rot;
            placeFine = f.fineYaw;
            movingExisting = true;
            f.isGhost = true;
            f.SetColliders(false);
            ghost = f;
        }

        float placeFine, moveOrigFine;

        /// <summary>Мебель — по 90°, постройки города (мосты, статуи…) — по 15°.</summary>
        public void RotateGhost()
        {
            if (ghost != null && ghost.FineRotate)
            {
                placeFine += 15f;
                if (placeFine > 45f) { placeFine -= 90f; placeRot = (placeRot + 1) % 4; }
            }
            else placeRot = (placeRot + 1) % 4;
        }

        static bool IsBridge(FurnitureDef d) => d.id == "p_bridge" || d.id == "p_bridge2";

        /// <summary>Точка под мышью на высоте пола там, куда она указывает (дом на холме — выше, в низине — ниже).</summary>
        bool FloorPoint(Ray ray, out Vector3 p)
        {
            p = default;
            float y = ViewPlaneY();
            for (int it = 0; it < 5; it++)
            {
                if (!new Plane(Vector3.up, Vector3.up * y).Raycast(ray, out float t)) return it > 0;
                p = ray.GetPoint(t);
                var c = GridMap.CellOf(p);
                if (!House.InTown(c)) return true;
                float ny = house.FloorY(house.viewLevel, c);
                if (Mathf.Abs(ny - y) < 0.02f) return true;
                y = ny;
            }
            return true;
        }

        public void CancelPlacing()
        {
            if (ghost == null) return;
            if (movingExisting)
            {
                ghost.level = moveOrigLevel;
                ghost.fineYaw = moveOrigFine;
                ghost.Place(moveOrigAnchor, moveOrigRot);
                pendingBefore = null;
                FinishGhost();
            }
            else Destroy(ghost.gameObject);
            ghost = null;
            movingExisting = false;
        }

        public void DeleteGhost()
        {
            if (ghost == null) return;
            if (movingExisting && !freeBuild && ghost.def.price > 0) AddMoney(SellValue(ghost), "Продано: " + ghost.def.name);
            bool wasMoving = movingExisting;
            Destroy(ghost.gameObject);
            ghost = null;
            movingExisting = false;
            if (wasMoving && pendingBefore != null) { var b = pendingBefore; pendingBefore = null; PushBuild("продажа предмета", b, Snap()); }
        }

        void FinishGhost()
        {
            ghost.isGhost = false;
            ghost.SetColliders(true);
            ghost.style.ClearHighlight();
            Register(ghost);
        }

        void UpdateBuild()
        {
            if (ghost == null) return;
            var ray = camera3d.ScreenPointToRay(Input.mousePosition);
            if (FloorPoint(ray, out var p))
            {
                ghost.fineYaw = placeFine;
                var e = Furniture.Size(ghost.def, placeRot);
                var a = new Vector2Int(Mathf.RoundToInt(p.x - e.x / 2f), Mathf.RoundToInt(p.z - e.y / 2f));
                ghost.level = house.viewLevel;
                ghost.Place(a, placeRot);
                placeValid = CanPlace(ghost.def, a, placeRot, null, house.viewLevel, mayorMode ? -1 : House.PlayerLotId) && (movingExisting || CanAfford(ghost.def));
                ghost.style.SetHighlight(-1, placeValid ? new Color(0.05f, 0.3f, 0.08f) : new Color(0.45f, 0.05f, 0.05f));
            }
            if (!ui.Typing && Input.GetKeyDown(KeyCode.R)) RotateGhost();
            if (Input.GetKeyDown(KeyCode.Delete) && movingExisting) { DeleteGhost(); return; }
            if (Input.GetMouseButtonDown(0) && !ui.MouseOverUI && placeValid)
            {
                var def = ghost.def;
                bool wasNew = !movingExisting;
                var undoBefore = wasNew ? Snap() : (pendingBefore ?? Snap());
                pendingBefore = null;
                if (wasNew)
                {
                    if (!freeBuild && def.price > 0) AddMoney(-def.price, "Покупка: " + def.name);
                    foreach (var fx in foxes) fx.OnEvent(new GameEvent { kind = "buy", arg = def.id });
                }
                if (wasNew) ghost.boughtAt = minutes;
                FinishGhost();
                ghost = null;
                movingExisting = false;
                PushBuild(wasNew ? "покупка: " + def.name : "перенос: " + def.name, undoBefore, Snap());
                if (wasNew && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) StartPlacing(def);
            }
        }

        // ================= ввод =================

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (ears != null && camera3d != null && cam != null)
            {
                var tp = cam.target;
                var dir = camera3d.transform.position - tp;
                ears.position = tp + dir.normalized * Mathf.Min(dir.magnitude, 4f) + Vector3.up * 1.2f;
                ears.rotation = camera3d.transform.rotation;
            }
            if (loading || house == null) return;
            house.UpdateDoors(foxes, dt);
            if (mode == Mode.Build) UpdateLotTool();
            if (ui.screen == GameUI.MenuScreen.None && mode == Mode.Town)
            {
                UpdateSky();
                foreach (var f in foxes) if (f != null) f.Tick(0, dt);
                TownClick();
                TownKeys();
                if (Input.GetKeyDown(KeyCode.Escape) && !ui.Typing) { if (ui.townEditing) ui.townEditing = false; else ui.screen = GameUI.MenuScreen.Pause; }
                return;
            }
            if (ui.screen != GameUI.MenuScreen.None)
            {
                // в меню время стоит, лисички только дышат, камера медленно облетает дом на титульном экране
                UpdateSky();
                foreach (var f in foxes) if (f != null) f.Tick(0, dt);
                if (titleScene && (ui.screen == GameUI.MenuScreen.Title || ui.screen == GameUI.MenuScreen.Settings)) UpdateTitleScene(dt);
                if (Input.GetKeyDown(KeyCode.Escape) && ui.screen == GameUI.MenuScreen.Pause && (mode == Mode.Town || Household.Count > 0)) ui.screen = GameUI.MenuScreen.None;
                else if (Input.GetKeyDown(KeyCode.Escape) && ui.screen == GameUI.MenuScreen.Settings) ui.screen = GameUI.MenuScreen.Pause;
                return;
            }

            float sp = SpeedMul;
            minutes += dt * sp;
            weather.Tick(dt * sp, Hour, camera3d, cam.target);
            UpdateSky();
            var list = new List<Fox>(foxes);
            foreach (var f in list) if (f != null) f.Tick(dt * sp, dt * sp);
            if (Day != lastAgeDay) NewDay();
            int hourNow = (int)(minutes / 60f);
            if (hourNow != lastSocialHour)
            {
                lastSocialHour = hourNow;
                foreach (var f in new List<Fox>(foxes)) if (f != null) f.MaybeDropBy();
                // утром — что сегодня в городе
                if (hourNow % 24 == 8)
                    foreach (var ev in TownEvents.Today(Day))
                    {
                        var lot = house.lots.Find(l => l.kind == ev.place);
                        Log($"Сегодня {TownEvents.Hours(ev)}: {ev.name} ({(lot != null ? lot.name : "")}). {ev.text}");
                    }
            }

            if (Day > 1 && (Day - 1) % 7 == 0 && Hour >= 9f && lastBillDay != Day)
            {
                lastBillDay = Day;
                float value = 0;
                foreach (var f in furniture) if (f.lot == House.PlayerLotId) value += f.def.price;
                int mouths = 0; foreach (var f in foxes) if (IsPlayer(f)) mouths++;
                int bill = Mathf.RoundToInt(50 + value * 0.03f + mouths * 20);
                AddMoney(-bill, "Счета за неделю");
                foreach (var f in foxes) if (f.pension > 0 && IsPlayer(f)) AddMoney(f.pension, $"{f.foxName}: пенсия");
            }

            if (started)
            {
                autosaveTimer -= Time.unscaledDeltaTime;
                if (autosaveTimer <= 0)
                {
                    autosaveTimer = AutosaveSeconds;
                    SaveSystem.Save(this);
                }
            }

            Hotkeys();

            if (mode == Mode.Build && ghost != null)
            {
                UpdateBuild();
                return;
            }
            if (mode == Mode.Build && wallTool != WallTool.None)
            {
                UpdateWallTool();
                return;
            }
            if (mode == Mode.Build && landTool != LandTool.None)
            {
                UpdateLandTool();
                return;
            }
            HideWallGhost();

            UpdateHover();
            UpdateWalkMarker();

            if (Input.GetMouseButtonDown(0))
            {
                if (ui.menuOpen && !ui.MouseOverUI) ui.CloseMenu();
                else if (!ui.MouseOverUI) WorldClick();
            }
            if (Input.GetMouseButtonDown(1) && ui.menuOpen) ui.CloseMenu();
        }

        void Hotkeys()
        {
            if (ui == null || ui.Typing) return;
            if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
            if (Input.GetKeyDown(KeyCode.PageUp)) SetViewLevel(house.viewLevel + 1);
            if (Input.GetKeyDown(KeyCode.PageDown)) SetViewLevel(house.viewLevel - 1);
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(2);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(3);
            UndoKeys();
            TownKeys();
            // в игре за семью строительство и стили открываются через мольберт: F2/F3 переключают их, пока план открыт
            if (!townBuilder && (mode == Mode.Build || mode == Mode.Style))
            {
                if (Input.GetKeyDown(KeyCode.F2)) SetMode(Mode.Build);
                if (Input.GetKeyDown(KeyCode.F3)) SetMode(Mode.Style);
                if (Input.GetKeyDown(KeyCode.F1)) SetMode(Mode.Live);
            }
            if (Input.GetKeyDown(KeyCode.F4)) ui.showViewTools = !ui.showViewTools;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (ui.menuOpen) ui.CloseMenu();
                else if (ghost != null) CancelPlacing();
                else if (wallTool != WallTool.None) SetWallTool(WallTool.None);
                else if (landTool != LandTool.None) SetLandTool(LandTool.None);
                else if (mode == Mode.Style && ui.tool != GameUI.StyleTool.Select) ui.tool = GameUI.StyleTool.Select;
                else if (mode == Mode.Style && styleTarget != null) styleTarget = null;
                else ui.screen = GameUI.MenuScreen.Pause;
            }
            if (mode == Mode.Build && ghost == null && Input.GetKeyDown(KeyCode.Delete) && hoverStyle != null)
            {
                var f = hoverStyle.GetComponent<Furniture>();
                if (f != null && f.lot == House.PlayerLotId) { SellFurniture(f); ClearHover(); }
            }
        }

        /// <summary>В меню города: F1 — семьи, F2 — участки и ландшафт, F3 — стили.</summary>
        void TownKeys()
        {
            if (ui.Typing || (mode != Mode.Town && !townBuilder)) return;
            if (Input.GetKeyDown(KeyCode.F1)) TownMode(0);
            if (Input.GetKeyDown(KeyCode.F2)) TownMode(1);
            if (Input.GetKeyDown(KeyCode.F3)) TownMode(2);
        }

        public void TownMode(int k)
        {
            if (k == 0) { if (townBuilder) ExitTownBuilder(); return; }
            if (!townBuilder) EnterTownBuilder();
            mode = k == 1 ? Mode.Build : Mode.Style;
            if (k == 2) ui.tool = GameUI.StyleTool.Select;
        }

        bool RaycastWorld(out RaycastHit hit, out StyleRef sr)
        {
            sr = null;
            var ray = camera3d.ScreenPointToRay(Input.mousePosition);
            hit = default;
            var hits = Physics.RaycastAll(ray, 300f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.GetComponent<TerrainChunk>() != null) continue; // земля не мешает кликать по участку
                sr = h.collider.GetComponent<StyleRef>();
                if (sr != null && sr.owner != null) { hit = h; return true; }
            }
            return false;
        }

        GameObject walkMarker;
        Transform markerArrow;

        /// <summary>Метка там, куда идёт выбранная лисичка.</summary>
        void UpdateWalkMarker()
        {
            Vector3? d = selected != null && mode == Mode.Live ? selected.Destination : null;
            if (walkMarker == null)
            {
                var mat = new Material(Shader.Find("FoxLife/Particle")) { color = new Color(0.17f, 0.6f, 0.84f, 0.75f) };
                walkMarker = new GameObject("walk marker");
                var ringGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(ringGo.GetComponent<Collider>());
                ringGo.transform.SetParent(walkMarker.transform, false);
                ringGo.transform.localScale = new Vector3(0.55f, 0.01f, 0.55f);
                ringGo.transform.localPosition = new Vector3(0, 0.03f, 0);
                ringGo.GetComponent<Renderer>().sharedMaterial = mat;
                var arrow = new GameObject("arrow");
                arrow.transform.SetParent(walkMarker.transform, false);
                arrow.AddComponent<MeshFilter>().sharedMesh = Shapes.Pyramid();
                arrow.AddComponent<MeshRenderer>().sharedMaterial = mat;
                arrow.transform.localScale = new Vector3(0.25f, 0.3f, 0.25f);
                arrow.transform.localRotation = Quaternion.Euler(180, 0, 0);
                markerArrow = arrow.transform;
                foreach (var r in walkMarker.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            walkMarker.SetActive(d != null);
            if (d == null) return;
            walkMarker.transform.position = d.Value;
            markerArrow.localPosition = new Vector3(0, 0.55f + Mathf.Sin(Time.unscaledTime * 4f) * 0.08f, 0);
            markerArrow.localRotation = Quaternion.Euler(180, Time.unscaledTime * 90f, 0);
        }

        void ClearHover()
        {
            if (hoverStyle != null) hoverStyle.ClearHighlight();
            hoverStyle = null;
            hoverPart = -1;
        }

        void UpdateHover()
        {
            Styleable st = null;
            int part = -1;
            Color col = Color.black;
            if (!ui.MouseOverUI && !ui.menuOpen && RaycastWorld(out _, out var sr))
            {
                var o = sr.owner;
                if (o.hidden) o = null;
                if (o == null) { ClearHover(); return; }
                var furn = o.GetComponent<Furniture>();
                var fox = mode == Mode.Style ? o.GetComponent<Fox>() : o.GetComponentInParent<Fox>();
                switch (mode)
                {
                    case Mode.Live:
                        if (fox != null || (furn != null && furn.def.actions.Count > 0)) { st = o; col = new Color(0.12f, 0.08f, 0.02f); }
                        break;
                    case Mode.Build:
                        if (furn != null && MayEditLot(furn.lot)) { st = o; col = new Color(0.1f, 0.12f, 0.02f); }
                        break;
                    case Mode.Style:
                        if (CanStyle(o)) { st = o; part = sr.part; col = new Color(0.16f, 0.12f, 0.04f); }
                        break;
                    case Mode.Create:
                        if (fox != null) { st = o; col = new Color(0.12f, 0.08f, 0.02f); }
                        break;
                }
            }
            if (st == hoverStyle && part == hoverPart && col == hoverColor) return;
            ClearHover();
            hoverStyle = st;
            hoverPart = part;
            hoverColor = col;
            if (st != null) st.SetHighlight(part, col);
        }

        void WorldClick()
        {
            if (!RaycastWorld(out var hit, out var sr)) return;
            var st = sr.owner;
            var fox = mode == Mode.Style ? st.GetComponent<Fox>() : st.GetComponentInParent<Fox>();
            var furn = st.GetComponent<Furniture>() ?? (mode == Mode.Build ? st.GetComponentInParent<Furniture>() : null);
            switch (mode)
            {
                case Mode.Live: LiveClick(st, fox, furn, hit); break;
                case Mode.Build:
                    if (lotTool) break;
                    if (furn == null && IsRemovableProp(st, out var propRoot) && (townBuilder || MayEditLot(Town.LotIdAt(propRoot.position.x, propRoot.position.z))))
                    {
                        string pn = propRoot.name;
                        ui.OpenMenu(Input.mousePosition, pn, new List<(string, System.Action)> { ("Убрать: " + pn.ToLower(), () => RemoveProp(propRoot)), ("Оставить", () => { }) });
                        break;
                    }
                    if (furn != null && MayEditLot(furn.lot)) StartMoving(furn);
                    else if (furn != null) Log("Это чужой участок: строить и переставлять можно только у себя (весь город — в «Строительстве города» на экране города).");
                    else if (sr.owner != null && house.RoofRoomOf(hit.collider.gameObject) is House.Room rr && MayEditLot(rr.lot)) ui.SelectRoof(rr);
                    break;
                case Mode.Style: StyleClick(st, sr.part); break;
                case Mode.Create:
                    if (fox != null && IsPlayer(fox)) { Select(fox); cam.Focus(fox.transform.position, 6f); }
                    break;
            }
        }

        void LiveClick(Styleable st, Fox fox, Furniture furn, RaycastHit hit)
        {
            if (fox != null)
            {
                if (!IsPlayer(fox)) { if (selected != null) OpenSocialMenu(fox); else Log("Это соседка или сосед: " + fox.foxName + ". Выбери свою лисичку, чтобы познакомиться."); return; }
                if (selected == null) Select(fox);
                else if (fox == selected) OpenSelfMenu(fox);
                else OpenSocialMenu(fox);
                return;
            }
            if (furn != null && furn.def.actions.Count > 0)
            {
                if (selected == null) { Log("Сначала выбери лисичку внизу экрана."); return; }
                OpenFurnitureMenu(furn);
                return;
            }
            if (selected != null && (st.kind == SurfaceKind.Floor || (st.hidden && st.kind == SurfaceKind.Outdoor) || furn != null))
            {
                var p = hit.point;
                selected.Command(new FoxAction { kind = ActKind.Move, moveTarget = p, label = "Идёт сюда" });
            }
        }

        void OpenFurnitureMenu(Furniture f)
        {
            var me = selected;
            var items = new List<PieItem>();
            foreach (var it in f.def.actions)
            {
                var inter = it;
                var pi = new PieItem(inter.name + (f.HasFreeSlot(me) ? "" : " (занято)"),
                    () => me.Command(new FoxAction { kind = ActKind.UseObject, furn = f, inter = inter, label = inter.verb ?? inter.name }));
                if (me.stage < inter.minStage) { pi.enabled = false; pi.reason = inter.minStage >= LifeStage.Adult ? "Только для взрослых" : "Пока мал" + (me.female ? "а" : "") + " для этого"; }
                else if (me.stage > inter.maxStage) { pi.enabled = false; pi.reason = "Это для лисят и подростков"; }
                else if (inter.blocked != null && inter.blocked(me, f) is string why) { pi.enabled = false; pi.reason = why; }
                items.Add(pi);
            }
            ui.OpenPie(Input.mousePosition, f.def.name, items);
        }

        static readonly string[] Friendly = { "Поболтать", "Пошутить", "Обсудить идеи", "Рассказать историю", "Сделать комплимент", "Обнять", "Бупнуть", "Утешить", "Пожаловаться на жизнь" };
        static readonly string[] Mischief = { "Подразнить", "Поспорить" };
        static readonly string[] Romance = { "Флиртовать", "Поцеловать", "Признаться в любви", "Сделать предложение", "Завести лисят" };

        PieItem SocialItem(Fox me, Fox target, SocialDef def)
        {
            var pi = new PieItem(def.name, () => me.Command(new FoxAction { kind = ActKind.Social, other = target, social = def, label = def.name + ": " + target.foxName }));
            if (target.sleeping)
            {
                pi.enabled = false;
                pi.reason = target.foxName + " спит — не стоит будить";
            }
            else if (!def.available(me, target))
            {
                pi.enabled = false;
                pi.reason = System.Array.IndexOf(Romance, def.name) >= 0 ? "Для этого нужны тёплые чувства, взрослый возраст и подходящий момент"
                    : def.name.Contains("лисёнка") || def.name.Contains("догонялки") ? "Это игра с лисятами" : "Сейчас не подходит";
            }
            return pi;
        }

        /// <summary>Клик по другой лисичке: подумать о ней, общение по разделам, позвать.</summary>
        void OpenSocialMenu(Fox target)
        {
            var me = selected;
            string he = target.female ? "ней" : "нём", his = target.female ? "её" : "его";
            var items = new List<PieItem>
            {
                new PieItem("Подумать") { sub = new List<PieItem>
                {
                    new PieItem("…о " + he, () => ui.ThinkAbout(target, 0)),
                    new PieItem("…о нас", () => ui.ThinkAbout(target, 1)),
                    new PieItem("…о " + his + " друзьях", () => ui.ThinkAbout(target, 2)),
                } },
            };
            var friendly = new List<PieItem>(); var mischief = new List<PieItem>(); var romance = new List<PieItem>(); var kids = new List<PieItem>();
            foreach (var sd in Social.All)
            {
                var it = SocialItem(me, target, sd);
                if (System.Array.IndexOf(Friendly, sd.name) >= 0) friendly.Add(it);
                else if (System.Array.IndexOf(Mischief, sd.name) >= 0) mischief.Add(it);
                else if (System.Array.IndexOf(Romance, sd.name) >= 0) romance.Add(it);
                else kids.Add(it);
            }
            items.Add(new PieItem("Дружба") { sub = friendly });
            items.Add(new PieItem("Озорство") { sub = mischief });
            var rItem = new PieItem("Романтика") { sub = romance };
            if (!romance.Exists(x => x.enabled)) { rItem.enabled = false; rItem.reason = Family.CanRomance(me, target) ? "Пока не те чувства" : "С этой лисичкой — никакой романтики"; }
            items.Add(rItem);
            if (kids.Exists(x => x.enabled)) items.Add(new PieItem("Игры") { sub = kids });
            if (IsPlayer(target)) items.Add(new PieItem("Выбрать: " + target.foxName, () => Select(target)));
            else
            {
                var invite = new List<PieItem>
                {
                    new PieItem("В гости к нам", () => { if (target.Accepts(me, 0.15f)) me.InviteOver(target); else { target.Say("Сейчас не могу..."); Log($"{target.foxName} сейчас не может прийти."); } }),
                };
                foreach (var lot in house.lots)
                {
                    if (lot.removed) continue;
                    if (!lot.Public || lot.kind == LotKind.School) continue;
                    var l = lot;
                    invite.Add(new PieItem(l.name, () => { if (target.Accepts(me, 0.2f)) me.GoOutTogether(target, l); else target.Say("Как-нибудь в другой раз..."); }));
                }
                items.Add(new PieItem("Позвать") { sub = invite });
            }
            var why = target.BusyReason(me);
            ui.OpenPie(Input.mousePosition, target.foxName + (why != null ? "\n(" + why + ")" : ""), items);
        }

        /// <summary>Клик по своей выбранной лисичке: подумать о себе, телефон, переодеться.</summary>
        void OpenSelfMenu(Fox me)
        {
            var items = new List<PieItem>
            {
                new PieItem("Подумать") { sub = new List<PieItem>
                {
                    new PieItem("…о себе", () => ui.ThinkAboutSelf(2)),
                    new PieItem("…о своих нуждах", () => ui.ThinkAboutSelf(0)),
                    new PieItem("…о чувствах", () => ui.ThinkAboutSelf(1)),
                    new PieItem("…о друзьях", () => ui.ThinkAboutSelf(6)),
                    new PieItem("…о семье", () => ui.ThinkAboutSelf(7)),
                    new PieItem("…о " + (me.IsChild ? "школе" : "работе"), () => ui.ThinkAboutSelf(4)),
                    new PieItem("…о мечтах", () => ui.ThinkAboutSelf(5)),
                    new PieItem("…об умениях", () => ui.ThinkAboutSelf(3)),
                    new PieItem("…о прожитом", () => ui.ThinkAboutSelf(8)),
                } },
                new PieItem("Телефон", () => ui.OpenPhone(me)),
            };
            ui.OpenPie(Input.mousePosition, me.foxName, items);
        }

        void StyleClick(Styleable st, int part)
        {
            if (st.hidden) return;
            if (!CanStyle(st)) { Log("Перекрашивать можно только своих лисичек, их одежду и свой дом (весь город — в «Режиме мэра»)."); return; }
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ui.tool == GameUI.StyleTool.Pipette || alt)
            {
                CopyStyle(st, part);
                if (ui.tool == GameUI.StyleTool.Pipette) ui.tool = GameUI.StyleTool.Brush;
                return;
            }
            if (ui.tool == GameUI.StyleTool.Brush || ctrl)
            {
                if (clipboard == null) { Log("Сначала возьми стиль пипеткой."); return; }
                if (shift) StyleEditAll(st, clipboard);
                else StyleEdit(st, part, clipboard);
                return;
            }
            styleTarget = st;
            stylePart = part;
        }

        public void CopyStyle(Styleable st, int part)
        {
            clipboard = st.Get(part).Clone();
            clipboardName = st.displayName + ": " + st.parts[part].name;
        }

        public void Log(string text)
        {
            int m = (int)(minutes % 1440f);
            log.Add(new LogEntry { time = $"{m / 60:00}:{m % 60:00}", text = text, born = Time.time });
            if (started && sound != null) sound.Chime();
            if (log.Count > 40) log.RemoveAt(0);
        }
    }
}
