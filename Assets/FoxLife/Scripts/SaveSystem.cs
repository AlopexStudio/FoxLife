using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FoxLife
{
    [Serializable] public class SaveRel { public string other; public float value; }
    [Serializable] public class SaveMoodlet { public string name; public float value, minutesLeft; }

    [Serializable]
    public class SaveFox
    {
        public string name;
        public int[] pers;
        public float ears, tail, size;
        public float[] needs;
        public float x, y, z, yaw;
        public int home;
        public List<Outfit> outfits;
        public bool stored;
        public string group;
        public List<StyleData> styles = new List<StyleData>();
        public List<SaveRel> rels = new List<SaveRel>();
        public List<SaveMoodlet> moodlets = new List<SaveMoodlet>();
        // версия 2
        public float[] skillXp;
        public int[] skillLvl;
        public string career;
        public int careerLevel, lastWorkDay = -1;
        public float performance;
        public List<Want> wants = new List<Want>();
        public string fearId;
        public float fearCooldown;
        public int happiness;
        public List<string> perks = new List<string>();
        public string dream;
        public List<string> dreamsDone = new List<string>();
        public List<string> tallyK = new List<string>();
        public List<int> tallyV = new List<int>();
        public List<Memory> memories = new List<Memory>();
        // версия 3
        public string id, mother, father, partner, babyFather;
        public bool female, married;
        public int stage = (int)LifeStage.Adult, ageDays, stageDays, pension;
        public float pregnantMinutes = -1f;
        public int element = -1, likedWeather = -1, dislikedWeather = -1;
        public List<SaveRel> romance = new List<SaveRel>();
        // ключи от чужих домов (постоянные)
        public List<HouseKey> keys = new List<HouseKey>();
    }

    [Serializable]
    public class SaveFurniture
    {
        public string id;
        public int x, z, rot, level;
        public float bought = -1f;
        public float light = 0.5f;
        public Color lightColor = new Color(1f, 0.82f, 0.58f);
        public List<StyleData> styles = new List<StyleData>();
        public float yaw;
        public int nid;   // сетевой номер (только для игры по сети)
    }

    [Serializable]
    public class SaveSurface
    {
        public string name;
        public List<StyleData> styles = new List<StyleData>();
    }

    [Serializable] public class SaveFlower { public float x, z; public int seed; }

    [Serializable]
    public class SaveTree
    {
        public float x, z, s;
        public bool pine;
        public List<StyleData> styles = new List<StyleData>();
    }

    [Serializable] public class SaveEdge { public int x, z, kind, level; public bool v; }

    [Serializable]
    public class SaveRoom
    {
        public int x, z, level, roofType, roofDir;
        public float roofPitch = 0.62f;
        public string name;
        public StyleData floor, wall;
    }

    [Serializable]
    public class SaveLot
    {
        public string name;
        public int x, z, w, h, kind, template;
        public bool south, removed;
        public float pathX;
    }

    [Serializable]
    public class SaveGame
    {
        public string townName;
        public List<SaveLot> lots = new List<SaveLot>();
        public List<string> removedProps = new List<string>();
        public List<SaveEdge> edges = new List<SaveEdge>();
        public List<Vector3> terrain = new List<Vector3>();
        public List<Vector3> paint = new List<Vector3>();
        public List<SaveFlower> flowers = new List<SaveFlower>();
        public List<SaveTree> trees = new List<SaveTree>();
        public List<SaveRoom> rooms = new List<SaveRoom>();
        public int version = 10;
        public int weather;
        public int playerLot;
        public List<int> moneyLots = new List<int>();
        public List<float> moneyValues = new List<float>();
        public List<string> moneyGroups = new List<string>();
        public List<float> moneyGroupValues = new List<float>();
        public float weatherHours = 5f;
        public int lifespan = (int)Lifespan.Normal;
        public int lastAgeDay = 1;
        public List<FoxRecord> archive = new List<FoxRecord>();
        public float money = Game.StartMoney;
        public bool freeBuild;
        public bool mayorMode, autoOutfits = true;
        public int lastBillDay;
        public string savedAt;
        public float minutes;
        public bool autonomy = true;
        public string selected;
        public List<SaveFox> foxes = new List<SaveFox>();
        public List<SaveFurniture> furniture = new List<SaveFurniture>();
        public List<SaveSurface> surfaces = new List<SaveSurface>();
        public List<StyleData> palette = new List<StyleData>();
    }

    /// <summary>Сохранение в JSON в папке данных игры (Application.persistentDataPath).</summary>
    public static class SaveSystem
    {
        public static string PathOverride;
        public static string PathToSave => PathOverride ?? Path.Combine(Application.persistentDataPath, "save.json");
        // меню рисуется несколько раз за кадр: проверять файл и разбирать сохранение каждый раз — очень дорого.
        // Поэтому существование файла проверяем не чаще раза в секунду, а сводку перечитываем, только когда файл изменился.
        static float hasSaveAt = -10f;
        static bool hasSaveCached;
        static string infoCached, infoPath;
        static System.DateTime infoTime;
        static float infoCheckAt = -10f;

        public static bool HasSave
        {
            get
            {
                if (Time.unscaledTime - hasSaveAt > 1f || hasSaveAt > Time.unscaledTime) { hasSaveAt = Time.unscaledTime; hasSaveCached = File.Exists(PathToSave); }
                return hasSaveCached;
            }
        }

        /// <summary>После сохранения или удаления файла — сразу перепроверить.</summary>
        public static void Touch() { hasSaveAt = -10f; infoPath = null; }

        public static string SaveInfo
        {
            get
            {
                if (!HasSave) return null;
                try
                {
                    if (infoPath == PathToSave && Time.unscaledTime - infoCheckAt < 1f && infoCheckAt <= Time.unscaledTime) return infoCached;
                    infoCheckAt = Time.unscaledTime;
                    var t = File.GetLastWriteTimeUtc(PathToSave);
                    if (infoPath == PathToSave && t == infoTime) return infoCached;
                    var s = JsonUtility.FromJson<SaveGame>(File.ReadAllText(PathToSave));
                    int m = (int)(s.minutes % 1440f);
                    infoCached = $"День {(int)(s.minutes / 1440f) + 1}, {m / 60:00}:{m % 60:00} · лисичек: {s.foxes.Count}";
                    infoPath = PathToSave; infoTime = t;
                    return infoCached;
                }
                catch { return null; }
            }
        }

        /// <summary>Семья из сохранения (для сцены в главном меню). Нет сохранения — пусто.</summary>
        public static List<SaveFox> LastFamily()
        {
            var l = new List<SaveFox>();
            try
            {
                if (!HasSave) return l;
                var s = JsonUtility.FromJson<SaveGame>(File.ReadAllText(PathToSave));
                if (s == null || s.foxes == null) return l;
                int home = s.version < 10 ? 0 : s.playerLot;
                foreach (var sf in s.foxes) if (!sf.stored && sf.home == home) l.Add(sf);
            }
            catch (Exception e) { Debug.LogWarning("FoxLife: не прочитать семью для меню: " + e.Message); }
            return l;
        }

        /// <summary>Лисичка-«актёр» для меню: та же внешность и одежда, но она не живёт в городе.</summary>
        public static Fox SpawnActor(Game g, SaveFox sf, Vector3 pos)
        {
            try
            {
                var p = new Personality();
                if (sf.pers != null && sf.pers.Length == 5) p.v = (int[])sf.pers.Clone();
                var fox = g.CreateFox(sf.name, p, 0, pos, sf.ears, sf.tail, sf.size, sf.female);
                fox.home = -77;
                if (sf.outfits != null && sf.outfits.Count == 3) { fox.outfits = sf.outfits.ToArray(); fox.wornOutfit = -1; }
                fox.stage = (LifeStage)Mathf.Clamp(sf.stage, 0, 3);
                fox.ApplyShape();
                ApplyStyles(fox.style, sf.styles);
                if (sf.needs != null && sf.needs.Length == fox.needs.Length) fox.needs = (float[])sf.needs.Clone();
                return fox;
            }
            catch (Exception e) { Debug.LogWarning("FoxLife: актёр для меню: " + e.Message); return null; }
        }

        static List<StyleData> Styles(Styleable st)
        {
            var l = new List<StyleData>();
            foreach (var p in st.parts) l.Add(p.style.Clone());
            return l;
        }

        /// <summary>Цвета всех частей предмета (у построек города — и вложенных: башня, циферблат, крылья мельницы…).</summary>
        static List<StyleData> AllStyles(Furniture f)
        {
            var l = new List<StyleData>();
            foreach (var st in f.GetComponentsInChildren<Styleable>(true))
                foreach (var p in st.parts) l.Add(p.style.Clone());
            return l;
        }

        static void ApplyAllStyles(Furniture f, List<StyleData> styles)
        {
            if (styles == null) return;
            int i = 0;
            foreach (var st in f.GetComponentsInChildren<Styleable>(true))
                for (int k = 0; k < st.parts.Count && i < styles.Count; k++, i++)
                {
                    var s = styles[i];
                    s.pattern = Mathf.Clamp(s.pattern, 0, Patterns.All.Count - 1);
                    st.SetStyle(k, s);
                }
        }

        static void ApplyStyles(Styleable st, List<StyleData> styles)
        {
            if (styles == null) return;
            for (int i = 0; i < st.parts.Count && i < styles.Count; i++)
            {
                var s = styles[i];
                s.pattern = Mathf.Clamp(s.pattern, 0, Patterns.All.Count - 1);
                st.SetStyle(i, s);
            }
        }

        public static bool Save(Game g, string path = null)
        {
            try
            {
                File.WriteAllText(path ?? PathToSave, ToJson(g, true));
                Touch();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("FoxLife: не удалось сохранить: " + e);
                return false;
            }
        }

        /// <summary>Весь мир одной строкой JSON: для файла сохранения и для друга по сети.</summary>
        public static string ToJson(Game g, bool pretty)
        {
            {
                var s = new SaveGame
                {
                    savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    minutes = g.minutes,
                    money = g.money,
                    freeBuild = g.freeBuild,
                    mayorMode = g.mayorMode,
                    autoOutfits = g.autoOutfits,
                    lastBillDay = g.lastBillDay,
                    lifespan = (int)g.lifespan,
                    lastAgeDay = g.lastAgeDay,
                    archive = new List<FoxRecord>(g.archive),
                    autonomy = g.autonomy,
                    selected = g.selected != null ? g.selected.foxName : null,
                    weather = (int)g.weather.kind, weatherHours = g.weather.hoursLeft,
                    playerLot = House.PlayerLotId,
                    townName = g.townName,
                    removedProps = new List<string>(g.removedProps),
                };
                foreach (var l in Town.Lots)
                    s.lots.Add(new SaveLot { name = l.name, x = l.rect.x, z = l.rect.y, w = l.rect.width, h = l.rect.height, kind = (int)l.kind, template = l.template, south = l.frontSouth, removed = l.removed, pathX = l.pathX });
                foreach (var kv in g.lotMoney) { s.moneyLots.Add(kv.Key); s.moneyValues.Add(kv.Value); }
                foreach (var kv in g.storedMoney) { s.moneyGroups.Add(kv.Key); s.moneyGroupValues.Add(kv.Value); }
                var everyone = new List<Fox>(g.foxes);
                everyone.AddRange(g.stored);
                foreach (var f in everyone) s.foxes.Add(FoxToSave(f, false));
                foreach (var fu in g.furniture)
                    s.furniture.Add(new SaveFurniture { id = fu.def.id, x = fu.anchor.x, z = fu.anchor.y, rot = fu.rot, level = fu.level, bought = fu.boughtAt, light = fu.lightLevel, lightColor = fu.lightColor, styles = AllStyles(fu), yaw = fu.fineYaw, nid = fu.nid });
                s.terrain = g.house.terrain.OffsetData();
                s.paint = g.house.terrain.PaintData();
                foreach (var fl in g.house.terrain.flowers) s.flowers.Add(new SaveFlower { x = fl.p.x, z = fl.p.z, seed = fl.seed });
                foreach (var tr in g.house.trees) s.trees.Add(new SaveTree { x = tr.p.x, z = tr.p.z, s = tr.s, pine = tr.pine, styles = tr.st != null ? Styles(tr.st) : null });
                foreach (var t in g.house.Layout()) s.edges.Add(new SaveEdge { level = t.level, x = t.x, z = t.z, v = t.v, kind = (int)t.k });
                foreach (var r in g.house.rooms)
                    if (r.cells.Count > 0)
                        s.rooms.Add(new SaveRoom { level = r.level, x = r.cells[0].x, z = r.cells[0].y, name = r.name, roofType = r.roofType, roofDir = r.roofDir, roofPitch = r.roofPitch, floor = r.st.Get(0).Clone(), wall = r.st.Get(1).Clone() });
                foreach (var st in g.house.Surfaces())
                    s.surfaces.Add(new SaveSurface { name = st.displayName, styles = Styles(st) });
                foreach (var p in g.ui.housePalette) s.palette.Add(p.Clone());
                return JsonUtility.ToJson(s, pretty);
            }
        }

        /// <summary>Лисичка для сохранения. allKeys — со всеми ключами (по сети), иначе только постоянные.</summary>
        public static SaveFox FoxToSave(Fox f, bool allKeys)
        {
            f.SyncWorn();
            var sf = new SaveFox
            {
                name = f.foxName,
                pers = (int[])f.pers.v.Clone(),
                ears = f.earSize, tail = f.tailFluff, size = f.size,
                needs = (float[])f.needs.Clone(),
                x = f.transform.position.x, y = f.transform.position.y, z = f.transform.position.z,
                yaw = f.transform.eulerAngles.y,
                styles = Styles(f.style),
                skillXp = (float[])f.skillXp.Clone(),
                skillLvl = (int[])f.skillLvl.Clone(),
                career = f.career != null ? f.career.id : null,
                careerLevel = f.careerLevel,
                lastWorkDay = f.lastWorkDay,
                performance = f.performance,
                wants = new List<Want>(f.wants),
                fearId = f.fearId,
                fearCooldown = f.fearCooldown,
                happiness = f.happiness,
                perks = new List<string>(f.perks),
                dream = f.dream,
                dreamsDone = new List<string>(f.dreamsDone),
                tallyK = new List<string>(f.tally.Keys),
                tallyV = new List<int>(f.tally.Values),
                memories = new List<Memory>(f.memories),
                id = f.id, mother = f.motherId, father = f.fatherId, partner = f.partnerId, babyFather = f.babyFatherId,
                female = f.female, married = f.married, stage = (int)f.stage, ageDays = f.ageDays, stageDays = f.stageDays,
                pension = f.pension, pregnantMinutes = f.pregnantMinutes,
                element = f.elementId, likedWeather = f.likedWeather, dislikedWeather = f.dislikedWeather,
                home = f.home,
                stored = f.storeGroup != null,
                group = f.storeGroup,
                outfits = f.outfits != null ? new List<Outfit>(f.outfits) : null,
            };
            foreach (var kv in f.romance) sf.romance.Add(new SaveRel { other = kv.Key.id, value = kv.Value });
            sf.keys = allKeys ? new List<HouseKey>(f.keys) : f.SavedKeys();

            foreach (var kv in f.rel) sf.rels.Add(new SaveRel { other = kv.Key.id, value = kv.Value });
            foreach (var m in f.moodlets) sf.moodlets.Add(new SaveMoodlet { name = m.name, value = m.value, minutesLeft = m.minutesLeft });
            return sf;
        }

        public static System.Collections.IEnumerator LoadAsync(Game g, Action<bool> done, string path = null, string json = null)
        {
            SaveGame s = null;
            try { s = JsonUtility.FromJson<SaveGame>(json ?? File.ReadAllText(path ?? PathToSave)); }
            catch (Exception e) { Debug.LogError("FoxLife: не удалось прочитать сохранение: " + e); }
            if (s == null || s.foxes == null) { done(false); yield break; }
            Styleable.notify = false;
            // участки города — до постройки мира: от них зависят рельеф, дорожки и дома
            if (s.lots != null && s.lots.Count > 0)
            {
                var defs = new Town.LotDef[s.lots.Count];
                for (int i = 0; i < defs.Length; i++)
                {
                    var l = s.lots[i];
                    defs[i] = new Town.LotDef { name = l.name, rect = l.removed ? Town.Nowhere : new RectInt(l.x, l.z, l.w, l.h), kind = (LotKind)l.kind, template = l.template, frontSouth = l.south, removed = l.removed, pathX = l.pathX };
                }
                Town.SetLots(defs);
            }
            else Town.ResetLots();
            g.townName = string.IsNullOrEmpty(s.townName) ? "Лисья Долина" : s.townName;
            yield return g.ResetWorldAsync(false);
            bool ok = true;
            var it = Apply(g, s);
            while (true)
            {
                bool more;
                try { more = it.MoveNext(); }
                catch (Exception e) { Debug.LogError("FoxLife: ошибка загрузки: " + e); ok = false; break; }
                if (!more) break;
                yield return it.Current;
            }
            Styleable.notify = true;
            done(ok);
        }

        static System.Collections.IEnumerator Apply(Game g, SaveGame s)
        {
            g.loadingText = "Раскладываем вещи по местам…";
            g.removedProps.Clear();
            if (s.removedProps != null) foreach (var key in s.removedProps) g.RemovePropByKey(key);
            g.minutes = s.minutes;
            g.autonomy = s.autonomy;
            g.weather.Set((WeatherKind)Mathf.Clamp(s.weather, 0, 4), s.weatherHours > 0 ? s.weatherHours : 5f);
            g.money = s.money;
            g.freeBuild = s.freeBuild;
            g.mayorMode = s.mayorMode;
            g.autoOutfits = s.autoOutfits;
            g.lastBillDay = s.lastBillDay;
            g.lifespan = (Lifespan)Mathf.Clamp(s.lifespan, 0, 3);
            g.lastAgeDay = s.lastAgeDay > 0 ? s.lastAgeDay : g.Day;
            if (s.archive != null) g.archive.AddRange(s.archive);

            // до версии 7 мир был одним участком: рельеф, цветы и деревья города берём новые
            bool oldWorld = s.version < 10;
            House.PlayerLotId = oldWorld ? 0 : Mathf.Clamp(s.playerLot, 0, Town.Lots.Length - 1); // до версии 8 города (или этого города) не было: берём только лисий дом
            if (!oldWorld)
            {
                var t = g.house.terrain;
                t.LoadOffsets(s.terrain);
                t.LoadPaint(s.paint);
                if (s.flowers != null) { t.ClearFlowers(); foreach (var fl in s.flowers) t.AddFlowers(new Vector3(fl.x, 0, fl.z), fl.seed); }
                if (s.trees != null)
                {
                    g.house.ClearTrees();
                    foreach (var tr in s.trees)
                    {
                        var rec = g.house.PlantTree(new Vector3(tr.x, 0, tr.z), tr.s, tr.pine);
                        ApplyStyles(rec.st, tr.styles);
                    }
                }
                g.house.UpdateGrid();
            }
            if (s.edges != null && s.edges.Count > 0)
            {
                var layout = new List<(int, int, int, bool, EdgeKind)>();
                foreach (var e in s.edges)
                    if (!oldWorld || g.house.EdgeLot(e.x, e.z, e.v) == 0) layout.Add((e.level, e.x, e.z, e.v, (EdgeKind)e.kind));
                yield return g.house.SetLayoutAsync(layout);
            }
            if (s.rooms != null)
                foreach (var sr in s.rooms)
                {
                    var room = g.house.RoomAt(sr.level, new Vector2Int(sr.x, sr.z));
                    if (room == null) continue;
                    if (!string.IsNullOrEmpty(sr.name)) { room.name = sr.name; room.st.displayName = sr.name; }
                    if (sr.floor != null) room.st.SetStyle(0, sr.floor);
                    if (sr.wall != null) room.st.SetStyle(1, sr.wall);
                    room.roofType = Mathf.Clamp(sr.roofType, 0, 3);
                    room.roofDir = sr.roofDir;
                    room.roofPitch = sr.roofPitch > 0 ? sr.roofPitch : 0.62f;
                }
            foreach (var lot in g.house.lots) g.house.RebuildRoofs(lot);
            var surfaces = g.house.Surfaces();
            // по порядку и по имени: если поверхностей стало больше (город), лишние пропускаются
            int j = 0;
            foreach (var ss in s.surfaces)
            {
                int k = j;
                while (k < surfaces.Count && surfaces[k].displayName != ss.name) k++;
                if (k >= surfaces.Count) continue;
                ApplyStyles(surfaces[k], ss.styles);
                j = k + 1;
            }

            yield return null;
            g.loadingText = "Расставляем мебель…";
            int nf = 0;
            foreach (var sf in s.furniture)
            {
                if (++nf % 40 == 0) yield return null;
                var def = Catalog.Get(sf.id);
                if (def == null) continue;
                var a = new Vector2Int(sf.x, sf.z);
                if (oldWorld && !g.house.PlayerLot.rect.Contains(a)) continue;
                if (!g.CanPlace(def, a, sf.rot, null, sf.level)) { Debug.LogWarning($"FoxLife: при загрузке не встал {sf.id} в {a} эт.{sf.level}"); continue; }
                var f = g.SpawnFurniture(def, a, sf.rot, false, sf.level);
                if (Mathf.Abs(sf.yaw) > 0.01f) { f.fineYaw = sf.yaw; f.Place(f.anchor, f.rot); }
                ApplyAllStyles(f, sf.styles);
                f.boughtAt = sf.bought;
                f.lightLevel = sf.light; f.lightColor = sf.lightColor;
                if (Net.IsClient && sf.nid != 0) f.nid = sf.nid;
            }

            g.loadingText = "Будим лисичек…";
            yield return null;
            var byName = new Dictionary<string, Fox>();
            var byId = new Dictionary<string, Fox>();
            foreach (var sf in s.foxes)
            {
                if (oldWorld && s.version >= 7 && sf.home != 0) continue;
                var p = new Personality();
                if (sf.pers != null && sf.pers.Length == 5) p.v = (int[])sf.pers.Clone();
                var fox = g.CreateFox(sf.name, p, 0, new Vector3(sf.x, sf.y, sf.z), sf.ears, sf.tail, sf.size, sf.female);
                if (!string.IsNullOrEmpty(sf.id)) fox.id = sf.id;
                fox.home = oldWorld ? 0 : sf.home;
                if (sf.outfits != null && sf.outfits.Count == 3) { fox.outfits = sf.outfits.ToArray(); fox.wornOutfit = -1; }
                fox.motherId = sf.mother; fox.fatherId = sf.father; fox.partnerId = sf.partner; fox.babyFatherId = sf.babyFather;
                fox.married = sf.married;
                fox.stage = (LifeStage)Mathf.Clamp(sf.stage, 0, 3);
                fox.ageDays = sf.ageDays; fox.stageDays = sf.stageDays; fox.pension = sf.pension;
                fox.pregnantMinutes = sf.pregnantMinutes;
                fox.elementId = sf.element; fox.likedWeather = sf.likedWeather; fox.dislikedWeather = sf.dislikedWeather;
                fox.ApplyShape();
                fox.transform.rotation = Quaternion.Euler(0, sf.yaw, 0);
                if (sf.needs != null && sf.needs.Length == fox.needs.Length) fox.needs = (float[])sf.needs.Clone();
                ApplyStyles(fox.style, sf.styles);
                foreach (var m in sf.moodlets) fox.AddMoodlet(m.name, m.value, m.minutesLeft);
                if (sf.skillXp != null && sf.skillXp.Length == Skills.Count) fox.skillXp = (float[])sf.skillXp.Clone();
                if (sf.skillLvl != null && sf.skillLvl.Length == Skills.Count) fox.skillLvl = (int[])sf.skillLvl.Clone();
                fox.career = string.IsNullOrEmpty(sf.career) ? null : Careers.Get(sf.career);
                fox.careerLevel = fox.career != null ? Mathf.Clamp(sf.careerLevel, 0, fox.career.levels.Length - 1) : 0;
                fox.lastWorkDay = sf.lastWorkDay;
                fox.performance = sf.performance;
                if (sf.wants != null) foreach (var w in sf.wants) if (Wants.Get(w.id) != null) fox.wants.Add(w);
                fox.fearId = string.IsNullOrEmpty(sf.fearId) ? null : sf.fearId;
                fox.fearCooldown = sf.fearCooldown;
                fox.happiness = sf.happiness;
                if (sf.perks != null) fox.perks.AddRange(sf.perks);
                fox.dream = string.IsNullOrEmpty(sf.dream) ? null : sf.dream;
                if (sf.dreamsDone != null) fox.dreamsDone.AddRange(sf.dreamsDone);
                if (sf.tallyK != null && sf.tallyV != null) for (int ti = 0; ti < sf.tallyK.Count && ti < sf.tallyV.Count; ti++) fox.tally[sf.tallyK[ti]] = sf.tallyV[ti];
                if (sf.memories != null) { fox.memories.Clear(); fox.memories.AddRange(sf.memories); }
                fox.keys.Clear();
                if (sf.keys != null) foreach (var k in sf.keys) if (k != null && k.kind == (int)KeyKind.Trusted) fox.keys.Add(k);
                byName[sf.name] = fox;
                byId[fox.id] = fox;
            }
            foreach (var sf in s.foxes)
            {
                Fox a = null;
                if (!string.IsNullOrEmpty(sf.id)) byId.TryGetValue(sf.id, out a);
                if (a == null && !byName.TryGetValue(sf.name, out a)) continue;
                foreach (var r in sf.rels)
                    if ((byId.TryGetValue(r.other, out var b) || byName.TryGetValue(r.other, out b)) && a != b) a.rel[b] = r.value;
                if (sf.romance != null)
                    foreach (var r in sf.romance)
                        if (byId.TryGetValue(r.other, out var b) && a != b) a.romance[b] = r.value;
            }

            // семьи в архиве
            foreach (var sf in s.foxes)
            {
                if (!sf.stored || oldWorld) continue;
                Fox f = null;
                if (!string.IsNullOrEmpty(sf.id)) byId.TryGetValue(sf.id, out f);
                if (f == null) continue;
                f.storeGroup = string.IsNullOrEmpty(sf.group) ? "archive" : sf.group;
                f.home = -1;
                g.foxes.Remove(f);
                g.stored.Add(f);
                f.gameObject.SetActive(false);
            }
            for (int i = 0; i < s.moneyLots.Count && i < s.moneyValues.Count; i++) g.lotMoney[s.moneyLots[i]] = s.moneyValues[i];
            for (int i = 0; i < s.moneyGroups.Count && i < s.moneyGroupValues.Count; i++) g.storedMoney[s.moneyGroups[i]] = s.moneyGroupValues[i];

            g.ui.housePalette.Clear();
            if (s.palette != null) foreach (var p in s.palette) g.ui.housePalette.Add(p);

            if (oldWorld) Town.Populate(g);
            var fm = g.house.terrain.FlushMeshAsync();
            while (fm.MoveNext()) yield return null; // старое сохранение: вокруг лисьего дома вырастает городок

            Fox sel = null;
            if (s.selected != null) byName.TryGetValue(s.selected, out sel);
            if (sel == null || !Game.IsPlayer(sel)) sel = g.Household.Count > 0 ? g.Household[0] : null;
            g.Select(sel);
            g.Log("Сохранение загружено.");
        }

        // ================= игра по сети =================

        public static void ApplyStylesTo(Styleable st, List<StyleData> styles) => ApplyStyles(st, styles);
        public static void ApplyAllStylesTo(Furniture f, List<StyleData> styles) => ApplyAllStyles(f, styles);
        public static List<StyleData> AllStylesOf(Furniture f) => AllStyles(f);

        /// <summary>Новая лисичка, о которой рассказал хост (родилась, переехала в город).</summary>
        public static Fox SpawnNetFox(Game g, SaveFox sf)
        {
            var p = new Personality();
            if (sf.pers != null && sf.pers.Length == 5) p.v = (int[])sf.pers.Clone();
            var fox = g.CreateFox(sf.name, p, 0, new Vector3(sf.x, sf.y, sf.z), sf.ears, sf.tail, sf.size, sf.female, (LifeStage)Mathf.Clamp(sf.stage, 0, 3));
            if (!string.IsNullOrEmpty(sf.id)) fox.id = sf.id;
            fox.home = sf.home;
            return fox;
        }

        /// <summary>Обновить лисичку по описанию от хоста (всё, кроме положения и нужд — они идут отдельно).</summary>
        public static void ApplyNetFox(Fox fox, SaveFox sf, bool styles)
        {
            fox.foxName = sf.name;
            if (sf.pers != null && sf.pers.Length == 5) fox.pers.v = (int[])sf.pers.Clone();
            fox.earSize = sf.ears; fox.tailFluff = sf.tail; fox.size = sf.size;
            fox.home = sf.home;
            fox.female = sf.female;
            var st = (LifeStage)Mathf.Clamp(sf.stage, 0, 3);
            if (st != fox.stage) fox.SetStage(st, false);
            fox.ApplyShape();
            fox.motherId = sf.mother; fox.fatherId = sf.father; fox.partnerId = sf.partner; fox.babyFatherId = sf.babyFather;
            fox.married = sf.married;
            fox.ageDays = sf.ageDays; fox.stageDays = sf.stageDays; fox.pension = sf.pension;
            fox.pregnantMinutes = sf.pregnantMinutes;
            fox.elementId = sf.element; fox.likedWeather = sf.likedWeather; fox.dislikedWeather = sf.dislikedWeather;
            fox.moodlets.Clear();
            if (sf.moodlets != null) foreach (var m in sf.moodlets) fox.AddMoodlet(m.name, m.value, m.minutesLeft);
            if (sf.skillXp != null && sf.skillXp.Length == Skills.Count) fox.skillXp = (float[])sf.skillXp.Clone();
            if (sf.skillLvl != null && sf.skillLvl.Length == Skills.Count) fox.skillLvl = (int[])sf.skillLvl.Clone();
            fox.career = string.IsNullOrEmpty(sf.career) ? null : Careers.Get(sf.career);
            fox.careerLevel = fox.career != null ? Mathf.Clamp(sf.careerLevel, 0, fox.career.levels.Length - 1) : 0;
            fox.lastWorkDay = sf.lastWorkDay;
            fox.performance = sf.performance;
            fox.wants.Clear();
            if (sf.wants != null) foreach (var w in sf.wants) if (Wants.Get(w.id) != null) fox.wants.Add(w);
            fox.fearId = string.IsNullOrEmpty(sf.fearId) ? null : sf.fearId;
            fox.fearCooldown = sf.fearCooldown;
            fox.happiness = sf.happiness;
            fox.perks.Clear(); if (sf.perks != null) fox.perks.AddRange(sf.perks);
            fox.dream = string.IsNullOrEmpty(sf.dream) ? null : sf.dream;
            fox.dreamsDone.Clear(); if (sf.dreamsDone != null) fox.dreamsDone.AddRange(sf.dreamsDone);
            fox.tally.Clear();
            if (sf.tallyK != null && sf.tallyV != null) for (int i = 0; i < sf.tallyK.Count && i < sf.tallyV.Count; i++) fox.tally[sf.tallyK[i]] = sf.tallyV[i];
            fox.memories.Clear(); if (sf.memories != null) fox.memories.AddRange(sf.memories);
            fox.keys.Clear(); if (sf.keys != null) foreach (var k in sf.keys) if (k != null) fox.keys.Add(k);
            var byId = new Dictionary<string, Fox>();
            foreach (var o in Game.I.foxes) if (o != null && o.id != null) byId[o.id] = o;
            fox.rel.Clear();
            if (sf.rels != null) foreach (var r in sf.rels) if (r.other != null && byId.TryGetValue(r.other, out var b) && b != fox) fox.rel[b] = r.value;
            fox.romance.Clear();
            if (sf.romance != null) foreach (var r in sf.romance) if (r.other != null && byId.TryGetValue(r.other, out var b) && b != fox) fox.romance[b] = r.value;
            if (!styles) return;
            string sj = JsonUtility.ToJson(new StyleList { s = sf.styles ?? new List<StyleData>() });
            if (sj != fox.netStyleJson) { fox.netStyleJson = sj; ApplyStyles(fox.style, sf.styles); }
            string oj = JsonUtility.ToJson(new NetFoxStyle { outfits = sf.outfits ?? new List<Outfit>() });
            if (oj != fox.netOutfitJson && sf.outfits != null && sf.outfits.Count == 3)
            {
                fox.netOutfitJson = oj;
                fox.outfits = sf.outfits.ToArray();
                fox.wornOutfit = -1; // переоденется в следующем кадре уже в новые вещи
            }
        }

        /// <summary>Друг перекрасил свою лисичку или её одежду.</summary>
        public static void ApplyNetStyle(Fox fox, NetFoxStyle d, bool clothes)
        {
            if (d.fur != null && d.fur.Count > 0) ApplyStyles(fox.style, d.fur);
            if (clothes && d.outfits != null && d.outfits.Count == 3)
            {
                fox.outfits = d.outfits.ToArray();
                fox.wornOutfit = -1;
            }
        }

        // ================= города-файлы =================

        /// <summary>Папка с городами: файлы *.foxtown можно отправить друзьям — им достаточно положить файл сюда.</summary>
        public static string TownsDir
        {
            get
            {
                var d = Path.Combine(Application.persistentDataPath, "Towns");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string TownPath(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(TownsDir, name.Trim() + ".foxtown");
        }

        public static List<(string name, string path, string info)> ListTowns()
        {
            var l = new List<(string, string, string)>();
            try
            {
                foreach (var f in Directory.GetFiles(TownsDir, "*.foxtown"))
                {
                    string name = Path.GetFileNameWithoutExtension(f), info = "";
                    try
                    {
                        var s = JsonUtility.FromJson<SaveGame>(File.ReadAllText(f));
                        if (!string.IsNullOrEmpty(s.townName)) name = s.townName;
                        int homes = 0; foreach (var lot in s.lots) if (!lot.removed) homes++;
                        info = $"участков: {homes}, лисичек: {s.foxes.Count}" + (string.IsNullOrEmpty(s.savedAt) ? "" : ", " + s.savedAt);
                    }
                    catch { info = "файл повреждён?"; }
                    l.Add((name, f, info));
                }
            }
            catch (Exception e) { Debug.LogWarning("FoxLife: города не прочитаны: " + e.Message); }
            return l;
        }
    }
}
