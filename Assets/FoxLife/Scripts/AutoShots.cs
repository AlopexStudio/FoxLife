using System.Collections;
using System.IO;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Отладка: запуск с аргументом «-foxshots папка» проходит по всем режимам,
    /// сохраняет скриншоты и журнал, затем закрывает игру. В обычной игре не активен.
    /// </summary>
    public partial class AutoShots : MonoBehaviour
    {
        string dir;
        StreamWriter log;
        bool wardrobeOnly, keysOnly;
        public static bool Active; // «-foxwardrobe папка»: только снимки одежды крупным планом (быстро)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-foxshots")
                {
                    var go = new GameObject("AutoShots");
                    go.AddComponent<AutoShots>().dir = args[i + 1];
                }
                else if (args[i] == "-foxkeys")
                {
                    var a = new GameObject("AutoShots").AddComponent<AutoShots>();
                    a.dir = args[i + 1];
                    a.keysOnly = true;
                }
                else if (args[i] == "-foxnethost" || args[i] == "-foxnetjoin")
                {
                    var a = new GameObject("AutoShots").AddComponent<AutoShots>();
                    a.dir = args[i + 1];
                    a.netHost = args[i] == "-foxnethost";
                    a.netJoin = !a.netHost;
                }
                else if (args[i] == "-foxwardrobe")
                {
                    var a = new GameObject("AutoShots").AddComponent<AutoShots>();
                    a.dir = args[i + 1];
                    a.wardrobeOnly = true;
                }
        }

        void Start()
        {
            Active = true;
            Directory.CreateDirectory(dir);
            Application.runInBackground = true; // чтобы прогон не вставал на паузу без фокуса окна
            SaveSystem.PathOverride = Path.Combine(dir, "test_save.json"); // не трогаем настоящее сохранение
            log = new StreamWriter(Path.Combine(dir, "run.log")) { AutoFlush = true };
            Application.logMessageReceived += (c, st, t) =>
            {
                if (t == LogType.Exception || t == LogType.Error) log.WriteLine($"[{t}] {c}\n{st}");
                else log.WriteLine($"[{t}] {c}");
            };
            if (wardrobeOnly) { StartCoroutine(WardrobeShots()); return; }
            if (keysOnly) { StartCoroutine(KeysOnly()); return; }
            if (netHost) { StartCoroutine(NetHostTest()); return; }
            if (netJoin) { StartCoroutine(NetJoinTest()); return; }
            StartCoroutine(Run());
            StartCoroutine(Fps());
        }

        IEnumerator Fps()
        {
            while (true)
            {
                int f0 = Time.frameCount;
                float t0 = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(5f);
                float dt = Time.realtimeSinceStartup - t0;
                log.WriteLine($"FPS {(Time.frameCount - f0) / dt:0.0} at {Time.realtimeSinceStartup:0}s, game {(Game.I != null ? Game.I.ClockText : "")}");
            }
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return new WaitForSeconds(0.5f);
        }

        void Dump()
        {
            var g = Game.I;
            log.WriteLine($"--- {g.ClockText}, тарелок: {g.PlateCount}, монет: {g.money:0}");
            foreach (var f in g.foxes)
                log.WriteLine($"{f.foxName} [{f.Summary}, {f.StageName}{(f.carried ? ", НЕСУТ" : "")}{(f.heldKind != null ? ", во рту " + f.heldKind : "")}]: {f.ActivityText} | настр {f.Mood:0} | нужды {string.Join(",", System.Array.ConvertAll(f.needs, n => n.ToString("0")))} | здоровье {f.health:0} менталка {f.mental:0} комфорт {f.comfort:0}{(f.Sick ? " БОЛЕЕТ" : "")} | увлечения {string.Join(",", f.interests)} | навыки {string.Join(",", f.skillLvl)} | работа {(f.career != null ? f.JobTitle + " " + f.performance.ToString("0") : "-")} | счастье {f.happiness} | желания: {string.Join("; ", f.wants.ConvertAll(w => w.DisplayText))} | страх {f.fearId} | мечта {(f.Dream != null ? f.Dream.name + " ур." + (f.dreamLevel + 1) + " " + Mathf.RoundToInt(Dreams.Progress(f, f.Dream) * 100) + "%" : "-")}{(f.running ? " | бежит" : "")}");
        }

        /// <summary>Кадр без постобработки: ищем NaN/бесконечность и сверхъяркие пиксели и под ними — объект.</summary>
        void FindBadPixels(string tag)
        {
            var cam = Camera.main;
            var cd = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            bool was = cd.renderPostProcessing;
            cd.renderPostProcessing = false;
            int w = 800, h = 450;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBFloat);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            PostFX.RenderTo(cam, rt);
            cam.targetTexture = old;
            cd.renderPostProcessing = was;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBAFloat, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            int nan = 0, hot = 0, neg = 0, logged = 0;
            var groups = new System.Collections.Generic.Dictionary<string, int>();
            string Path2(Transform t) { string r = t.name; for (int k = 0; k < 3 && t.parent != null; k++) { t = t.parent; r = t.name + "/" + r; } return r; }
            float maxL = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                bool bad = float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b);
                bool n = c.r < -0.001f || c.g < -0.001f || c.b < -0.001f;
                float l = bad ? 0 : Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (!bad) maxL = Mathf.Max(maxL, l);
                if (bad) nan++; else if (n) neg++; else if (l > 3f) hot++;
                if (l > 1.3f && (i % 3) == 0)
                {
                    float sx2 = (i % w) / (float)w * Screen.width, sy2 = (i / w) / (float)h * Screen.height;
                    var ray2 = cam.ScreenPointToRay(new Vector3(sx2, sy2, 0));
                    string what2 = Physics.Raycast(ray2, out var hit2, 500f) ? Path2(hit2.collider.transform) : "небо";
                    groups.TryGetValue(what2, out int cnt); groups[what2] = cnt + 1;
                }
                if ((bad || n || l > 3f) && logged < 6)
                {
                    logged++;
                    float sx = (i % w) / (float)w * Screen.width, sy = (i / w) / (float)h * Screen.height;
                    var ray = cam.ScreenPointToRay(new Vector3(sx, sy, 0));
                    string what = Physics.Raycast(ray, out var hit, 500f) ? hit.collider.name + " / " + (hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "-") + " / " + (hit.collider.transform.root.name) : "ничего";
                    log.WriteLine($"ПИКСЕЛЬ {tag}: ({i % w},{i / w}) = {c.r},{c.g},{c.b} — {what}");
                }
            }
            foreach (var kv in groups) if (kv.Value >= 3) log.WriteLine($"ЯРКО {tag}: {kv.Key} — {kv.Value}");
            log.WriteLine($"ПИКСЕЛИ {tag}: NaN/inf {nan}, отрицательных {neg}, ярче 3: {hot}, максимум {maxL:0.00}");
            Destroy(tex);
            rt.Release();
        }

        /// <summary>Вся одежда на лисичке крупным планом, спереди и сбоку.</summary>
        IEnumerator WardrobeShots()
        {
            var g = Game.I;
            while (g.loading) yield return null;
            g.NewGame();
            yield return new WaitForSeconds(0.3f);
            while (g.loading) yield return null;
            g.PlayFamily(0);
            yield return new WaitForSeconds(2f);
            g.SetSpeed(0);
            var f = g.Household[0];
            var sq = g.house.lots.Find(l => l.kind == LotKind.Square || l.kind == LotKind.Park);
            if (sq != null) { var c = GridMap.Center(sq.entry); f.transform.position = c + Vector3.up * g.house.FloorY(0, sq.entry); }
            f.queue.Clear();
            g.cam.minDist = 1.2f;
            var heads = Wardrobe.ForSlot(ClothSlot.Head); var necks = Wardrobe.ForSlot(ClothSlot.Neck);
            var bodies = Wardrobe.ForSlot(ClothSlot.Body); var eyes = Wardrobe.ForSlot(ClothSlot.Eyes);
            int n = Mathf.Max(Mathf.Max(heads.Count, necks.Count), Mathf.Max(bodies.Count, eyes.Count));
            for (int i = 0; i < n; i++)
            {
                f.SetItem(0, ClothSlot.Head, heads[i % heads.Count].id);
                f.SetItem(0, ClothSlot.Neck, necks[i % necks.Count].id);
                f.SetItem(0, ClothSlot.Body, bodies[i % bodies.Count].id);
                f.SetItem(0, ClothSlot.Eyes, eyes[i % eyes.Count].id);
                f.Wear(0);
                log.WriteLine($"ГАРДЕРОБ {i}: {heads[i % heads.Count].id}, {necks[i % necks.Count].id}, {bodies[i % bodies.Count].id}, {eyes[i % eyes.Count].id}");
                for (int k = 0; k < 3; k++)
                {
                    g.cam.target = f.transform.position + Vector3.up * 0.2f; g.cam.dist = k == 2 ? 1.3f : 2.6f; g.cam.pitch = k == 2 ? 18f : 22f;
                    g.cam.yaw = f.transform.eulerAngles.y + (k == 0 ? 150f : k == 1 ? 60f : 180f);
                    g.cam.Snap();
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"w{i:00}_{k}");
                }
            }
            Application.Quit();
        }

        IEnumerator KeysOnly()
        {
            var g = Game.I;
            while (g.loading) yield return null;
            g.NewGame();
            yield return new WaitForSeconds(0.3f);
            while (g.loading) yield return null;
            g.PlayFamily(0);
            yield return new WaitForSeconds(2f);
            yield return KeyTest();
            foreach (var e in g.log) log.WriteLine("ЖУРНАЛ " + e.time + " " + e.text);
            Application.Quit();
        }

        IEnumerator KeyTest()
        {
            var g = Game.I;
            var me = g.Household[0];
            var nb = g.foxes.Find(o => o != null && o.home != House.PlayerLotId && o.home >= 0 && Fox.LotLocked(o.home) && o.stage >= LifeStage.Teen && g.house.LotAt(o.Cell) != null && g.house.LotAt(o.Cell).id == o.home && !o.sleeping && !o.atWork);
            if (nb == null) { log.WriteLine("КЛЮЧИ: соседей дома нет"); yield break; }
            int lotId = nb.home;
            log.WriteLine($"КЛЮЧИ: {me.foxName} → {g.house.LotById(lotId).name}: войти можно {me.CanEnter(lotId)}, видно внутри {g.HouseholdCanEnter(lotId)}, входная дверь {(g.house.FrontDoor(lotId) != null)}");
            foreach (var m in g.foxes) if (m != null && m.home == lotId && m != me) Social.AddRel(me, m, 75f);
            me.CommandKnock(lotId, null);
            g.SetSpeed(3);
            float t0 = Time.realtimeSinceStartup;
            string last = null;
            while (Time.realtimeSinceStartup - t0 < 50f && me.KeyTo(lotId) == KeyKind.None)
            {
                if (me.ActivityText != last) { last = me.ActivityText; log.WriteLine($"КЛЮЧИ ход: {g.ClockText} {last}, клетка {me.Cell}"); }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            log.WriteLine($"КЛЮЧИ: получен ключ {me.KeyTo(lotId)}, {me.foxName}: {me.ActivityText}, видно внутри {g.HouseholdCanEnter(lotId)}");
            yield return new WaitForSecondsRealtime(4f);
            g.SetSpeed(1);
            g.cam.target = me.transform.position; g.cam.dist = 14f; g.cam.pitch = 45f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("26_keys");
            foreach (var m in g.foxes) if (m != null && m.home == lotId && m != me) Social.AddRel(me, m, -150f);
            g.SetSpeed(3);
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 40f && me.KeyTo(lotId) == KeyKind.Trusted) yield return new WaitForSecondsRealtime(0.5f);
            g.SetSpeed(1);
            log.WriteLine($"КЛЮЧИ: после ссоры ключ {me.KeyTo(lotId)} (через {Time.realtimeSinceStartup - t0:0} с), нужно для гостя {nb.GuestNeed:0}, для ключа {nb.TrustNeed:0}");
        }

        IEnumerator Run()
        {
            var g = Game.I;
            yield return new WaitForSeconds(1f);
            yield return Shot("00_loading");
            while (g.loading) yield return null;
            yield return new WaitForSeconds(2f);
            yield return Shot("0_title");
            g.NewGame();
            yield return new WaitForSeconds(0.3f);
            while (g.loading) yield return null;
            yield return new WaitForSeconds(2f);
            yield return Shot("0c_town");
            g.ui.townLot = 0;
            yield return new WaitForSeconds(1f);
            yield return Shot("0d_town_lot");
            // строительство города: границы участков, городские вещи в каталоге
            g.TownMode(1);
            g.ui.buildTab = 3;
            g.cam.target = new Vector3(10f, 0, -8f); g.cam.dist = 60f; g.cam.pitch = 55f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("0f_builder_lots");
            g.ui.lotsEdit = 0;
            yield return Shot("0fa_lot_cases");
            g.ui.lotsEdit = -1;
            g.ui.buildTab = 0;
            {
                var lampDef = Catalog.Get("p_ancient");
                if (lampDef != null && g.CanPlace(lampDef, new Vector2Int(-20, 30), 0, null, 0)) { g.SpawnFurniture(lampDef, new Vector2Int(-20, 30), 0, false); log.WriteLine("ГОРОД: древнее дерево поставлено"); }
                else log.WriteLine("ГОРОД: древнее дерево не встало");
                g.cam.target = new Vector3(-17f, 2f, 33f); g.cam.dist = 30f; g.cam.pitch = 30f;
            }
            yield return new WaitForSeconds(1.5f);
            yield return Shot("0g_builder_prop");
            {
                var bdef = Catalog.Get("p_bridge2");
                var cq = Town.Creek.PointAt(Town.Creek.Length * 0.62f);
                bool bridgeOk = false;
                for (int rot = 0; rot < 2 && !bridgeOk; rot++)
                {
                    var e = Furniture.Size(bdef, rot);
                    var a = new Vector2Int(Mathf.RoundToInt(cq.x - e.x / 2f), Mathf.RoundToInt(cq.y - e.y / 2f));
                    if (g.CanPlace(bdef, a, rot, null, 0)) { g.SpawnFurniture(bdef, a, rot, false); bridgeOk = true; log.WriteLine($"ГОРОД: мост через ручей в {a}, поворот {rot}, проход над водой {g.house.HasFloor(0, new Vector2Int(Mathf.FloorToInt(cq.x), Mathf.FloorToInt(cq.y)))}"); }
                }
                if (!bridgeOk) log.WriteLine("ГОРОД: мост не встал");
                for (int i = 0; i < 12; i++) g.house.terrain.PaintBrush(new Vector3(cq.x + 4f + i * 0.6f, 0, cq.y + 3f + Mathf.Sin(i * 0.5f)), 0.9f, VoxelTerrain.Paint.Path);
                for (int i = 0; i < 14; i++) g.house.terrain.PaintBrush(new Vector3(cq.x - 6f - i * 0.5f, 0, cq.y - 4f + i * 0.35f), 1.5f, VoxelTerrain.Paint.Road);
                for (int i = 0; i < 10; i++) g.house.terrain.PaintBrush(new Vector3(cq.x + 3f, 0, cq.y - 6f - i * 0.6f), 0.8f, VoxelTerrain.Paint.Stones);
                g.cam.target = new Vector3(cq.x - 2f, 0, cq.y - 3f); g.cam.dist = 22f; g.cam.pitch = 62f; g.cam.yaw = 0f;
                yield return new WaitForSeconds(1.5f);
                yield return Shot("0h_builder_bridge");
            }
            g.TownMode(0);
            yield return new WaitForSeconds(1f);
            g.ui.townEditing = true;
            g.ui.townEditFox = g.FamilyOf(0)[0];
            var nameFox = g.ui.townEditFox;
            string nameFoxId = nameFox.id;
            string cases = Phrases.Fill("{me}, {me:gen}, {me:dat}, {me:acc}, {me:ins}, {me:prep}", nameFox, null);
            if (cases != "Рыжик, Рыжика, Рыжику, Рыжика, Рыжиком, Рыжике")
                Debug.LogError("ПАДЕЖИ: неверные формы Рыжика: " + cases);
            log.WriteLine("ПАДЕЖИ: " + cases);
            if (Names.DefaultForm("Огонёк", NameCase.Genitive) != "Огонька" ||
                Names.DefaultForm("Корица", NameCase.Instrumental) != "Корицей" ||
                Names.DefaultForm("Лисичка 12", NameCase.Dative) != "Лисичке 12" ||
                Names.DefaultForm("Чэнь", NameCase.Dative) != "Чэнь")
                Debug.LogError("ПАДЕЖИ: ошибка в готовых формах или подстановке для своего имени.");
            g.cam.Focus(g.ui.townEditFox.transform.position, 5f); g.cam.pitch = 25f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("0e_family_edit");
            nameFox.nameOverrides = new[] { "Рыжи", null, null, null, null };
            g.ui.townEditing = false;
            g.PlayFamily(0);
            log.WriteLine($"ИГРАЕМ: режим {g.mode}, семья {Game.FamilyTitle(g.Household)}, started {g.started}");
            yield return new WaitForSeconds(2f);
            yield return Shot("1_live");
            int oldDisliked = nameFox.dislikedWeather;
            nameFox.dislikedWeather = (int)WeatherKind.Storm;
            if (Fox.WeatherName(WeatherKind.Storm) != "гроза" || Fox.WeatherName(WeatherKind.Storm, accusative: true) != "грозу")
                Debug.LogError("ПОГОДА: неверные формы слова «гроза».");
            yield return null;
            g.Select(nameFox);
            yield return null;
            g.ui.ThinkAboutSelf(2);
            yield return new WaitForSeconds(0.5f);
            g.ui.foxScroll = new Vector2(0, 10000);
            yield return Shot("1b_weather_thought");
            g.ui.infoOpen = false;
            nameFox.dislikedWeather = oldDisliked;
            var speaker = g.Household[1];
            string speakerId = speaker.id;
            var chat = Social.All.Find(d => d.name == "Поболтать");
            var compliment = Social.All.Find(d => d.name == "Сделать комплимент");
            var hug = Social.All.Find(d => d.name == "Обнять");
            if (g.SocialItem(speaker, nameFox, chat).label != "Поговорить" ||
                g.SocialItem(speaker, nameFox, hug).label != "Обнять" ||
                new PieItem("Подумать") { sub = new System.Collections.Generic.List<PieItem>() }.DisplayLabel != "Подумать…")
                Debug.LogError("МЕНЮ: нужна короткая команда без имени и многоточие без пробела.");
            var act = FoxAction.ForSocial(chat, nameFox);
            var queued = FoxAction.ForSocial(compliment, nameFox);
            var receive = new FoxAction { kind = ActKind.BeTalkedTo, social = chat, other = nameFox };
            if (act.DisplayText(true) != "Говорит с Рыжиком" || receive.DisplayText(true) != "Говорит с Рыжиком" ||
                queued.DisplayText() != "Сделать комплимент Рыжику" || hug.ActivityText(nameFox) != "Обнимает Рыжика" ||
                Social.TakeKey.ActivityText(nameFox) != "Забирает ключ у Рыжи" ||
                Fox.PhoneText(Fox.PhoneMode.Chat, nameFox, true) != "Говорит с Рыжиком по телефону")
                Debug.LogError("ОБЩЕНИЕ: неверные падежи в тексте действий.");
            foreach (var def in Social.All)
                if (def.CommandText(nameFox) == def.name || def.ActivityText(nameFox) == def.name)
                    Debug.LogError("ОБЩЕНИЕ: не задана форма действия «" + def.name + "».");
            var savedAction = speaker.current;
            int savedSpeed = g.speedIndex;
            g.SetSpeed(0);
            g.Select(speaker);
            act.stage = 2;
            speaker.current = act;
            speaker.queue.Insert(0, queued);
            log.WriteLine("ОБЩЕНИЕ: " + speaker.ActivityText + " · " + queued.DisplayText());
            yield return Shot("1c_social_cases");
            g.OpenSocialMenu(nameFox);
            yield return Shot("1d_social_menu");
            g.ui.CloseMenu();
            var oldCareer = speaker.career;
            int oldCareerLevel = speaker.careerLevel;
            speaker.career = Careers.Get("art"); speaker.careerLevel = 0;
            if (!speaker.WorkDescription.StartsWith("Работает уличным художником "))
                Debug.LogError("РАБОТА: неверная форма должности — " + speaker.WorkDescription);
            else log.WriteLine("РАБОТА: " + speaker.WorkDescription);
            speaker.career = oldCareer; speaker.careerLevel = oldCareerLevel;
            speaker.current = savedAction;
            speaker.queue.Remove(queued);
            g.Select(nameFox);
            g.SetSpeed(savedSpeed);

            // город: общий вид, кафе, парк, библиотека, соседи; поход в кафе по команде
            log.WriteLine($"ГОРОД: лисичек {g.foxes.Count}, наших {g.Household.Count}, мебели {g.furniture.Count}, участков {g.house.lots.Count}, комнат {g.house.rooms.Count}");
            foreach (var l in g.house.lots)
                log.WriteLine($"  участок {l.id} {l.name}: вход {l.entry}, мебели {g.furniture.FindAll(f => f.lot == l.id).Count}, жителей {g.HomeCount(l.id)}, комнат {g.house.rooms.FindAll(r => r.lot == l.id).Count}");
            var counter = g.furniture.Find(f => f.def.id == "cafecounter");
            var visitor = g.foxes[2];
            if (counter != null) visitor.Command(new FoxAction { kind = ActKind.UseObject, furn = counter, inter = counter.def.actions[0], label = "Какао" });
            (Vector3 t, float yaw, float pitch, float dist, string name)[] views =
            {
                (new Vector3(15f, 0, -8f), 0, 50, 48, "20_town"),
                (new Vector3(10f, 0, -16f), 20, 38, 20, "26_square"),
                (new Vector3(6f, 0, -28f), 15, 45, 20, "21_cafe"),
                (new Vector3(-28f, 0, -24f), -20, 40, 24, "22_park"),
                (new Vector3(35f, 0, -26f), 10, 42, 22, "23_library"),
                (new Vector3(-17f, 0, 8f), 200, 30, 22, "24_neighbors"),
                (new Vector3(44f, 0, 10f), 160, 28, 26, "27_turret"),
                (new Vector3(30f, 2f, 22f), 200, 18, 30, "28_mill"),
                (new Vector3(-32f, 0, -6f), 60, 30, 14, "29_bridge"),
                (new Vector3(10f, 0, -2f), 180, 20, 34, "30_street"),
                (new Vector3(-76f, 0, -22f), 20, 40, 28, "32_camp"),
                (new Vector3(-8f, 5f, -16f), 200, 18, 40, "32b_ancient"),
                (new Vector3(32f, 6f, 50f), 200, 25, 34, "33_mountains"),
                (new Vector3(99f, 0, -19f), 10, 45, 24, "34_school"),
                (new Vector3(99f, 0, 9f), 200, 40, 24, "35_market"),
                (new Vector3(-61f, 0, 8f), 200, 35, 22, "36_workshop"),
                (new Vector3(-60f, 0, 30f), 120, 22, 40, "37_forest"),
                (new Vector3(60f, 0, -10f), 340, 14, 60, "38_valley"),
            };
            foreach (var v in views)
            {
                g.cam.target = v.t; g.cam.yaw = v.yaw; g.cam.pitch = v.pitch; g.cam.dist = v.dist;
                yield return new WaitForSeconds(1.5f);
                yield return Shot(v.name);
                yield return new WaitForEndOfFrame();
                FindBadPixels(v.name);
            }
            {
                float keep = g.minutes;
                g.SetSpeed(0);
                g.minutes = Mathf.Floor(g.minutes / 1440f) * 1440f + 21.6f * 60f;
                g.cam.target = new Vector3(10f, 0, -14f); g.cam.yaw = 200; g.cam.pitch = 30; g.cam.dist = 26f;
                yield return new WaitForSeconds(1.5f);
                yield return Shot("31_night");
                yield return new WaitForEndOfFrame();
                FindBadPixels("31_night");
                g.minutes = keep;
                g.SetSpeed(1);
            }
            foreach (var wk in new[] { WeatherKind.Fog, WeatherKind.Rain, WeatherKind.Storm, WeatherKind.Cloudy })
            {
                g.weather.Set(wk, 3f); g.weather.Snap();
                g.cam.target = new Vector3(10f, 0, -8f); g.cam.yaw = 200; g.cam.pitch = 28; g.cam.dist = 34f;
                yield return new WaitForSeconds(wk == WeatherKind.Storm ? 1.2f : 2f);
                yield return Shot("40_weather_" + wk);
                yield return new WaitForEndOfFrame();
                FindBadPixels("40_weather_" + wk);
            }
            g.weather.Set(WeatherKind.Clear, 6f); g.weather.Snap();
            {
                var vac = g.VacantHomes();
                log.WriteLine($"ПЕРЕЕЗД: свободно {vac.Count}: {string.Join(", ", vac.ConvertAll(l => l.name + " " + g.BuyPrice(l.id)))}, продажа своего {g.SellPrice(House.PlayerLotId)}");
                foreach (var ev in TownEvents.All) log.WriteLine($"СОБЫТИЕ {ev.name}: дни {string.Join(",", ev.days)} {TownEvents.Hours(ev)}");
            }
            var guest = g.foxes.Find(x => x.home != 0 && x.stage >= LifeStage.Adult && x.career == null);
            if (guest != null) g.foxes[1].InviteOver(guest);
            g.SetSpeed(3);
            yield return new WaitForSeconds(6f);
            var vl = g.house.LotAt(visitor.Cell);
            log.WriteLine($"ГОСТИ: {(guest != null ? guest.foxName + " — " + guest.ActivityText + ", участок " + (g.house.LotAt(guest.Cell) != null ? g.house.LotAt(guest.Cell).name : "улица") + ", гость у дома " + guest.guestLot : "нет")}");
            log.WriteLine($"КАФЕ: {visitor.foxName} — {visitor.ActivityText}, участок {(vl != null ? vl.name : "улица")}, клетка {visitor.Cell}, монет {g.money:0}");
            g.cam.target = new Vector3(10f, 0, -15f); g.cam.yaw = 0; g.cam.pitch = 40; g.cam.dist = 26f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("25_outing");
            // транспорт: скейтборд и ролики — на улице быстрее, в доме убираются
            {
                var sk = g.Household.Count > 0 ? g.Household[0] : null;
                var rl = g.Household.Count > 1 ? g.Household[1] : null;
                int sp0 = g.speedIndex;
                g.SetSpeed(1);
                var start = GridMap.Center(g.house.PlayerLot.entry) + new Vector3(0, 0, -3f);
                var goal = start + new Vector3(14f, 0, -4f);
                foreach (var (fx, id, dx) in new[] { (sk, "skateboard", 0f), (rl, "rollers", 1.2f) })
                {
                    if (fx == null) continue;
                    fx.SetItem(0, ClothSlot.Ride, id);
                    fx.CancelCurrentPublic(); fx.queue.Clear();
                    fx.transform.position = start + new Vector3(dx, 0, 0);
                    fx.Command(new FoxAction { kind = ActKind.Move, moveTarget = goal + new Vector3(dx, 0, 0), label = "Катается" });
                }
                yield return new WaitForSeconds(0.6f);
                var p0 = sk != null ? sk.transform.position : Vector3.zero;
                float t0r = Time.time;
                yield return new WaitForSeconds(1.2f);
                g.cam.target = sk != null ? sk.transform.position : start; g.cam.yaw = 20; g.cam.pitch = 30; g.cam.dist = 7f;
                yield return new WaitForSeconds(0.5f);
                if (sk != null) log.WriteLine($"ТРАНСПОРТ: {sk.foxName} {sk.RideId} катится={sk.Riding}, скорость {Vector3.Distance(p0, sk.transform.position) / Mathf.Max(0.01f, Time.time - t0r):0.0} м/с; {(rl != null ? rl.foxName + " " + rl.RideId + " катится=" + rl.Riding : "")}");
                yield return Shot("25b_ride");
                foreach (var fx in new[] { sk, rl }) if (fx != null) fx.SetItem(0, ClothSlot.Ride, null);
                g.SetSpeed(sp0);
            }
            g.SetSpeed(1);

            yield return KeyTest();
            g.cam.target = new Vector3(7f, 0, 4f); g.cam.yaw = 0; g.cam.pitch = 48; g.cam.dist = 15f;

            // телевизор и мольберт: вокселы экрана и холста
            {
                var tv = g.furniture.Find(f => f.def.id == "tv" && f.lot == House.PlayerLotId);
                var easelM = g.furniture.Find(f => f.def.id == "easel" && f.lot == House.PlayerLotId);
                var fam = g.Household;
                if (tv != null && fam.Count > 0) fam[0].Command(new FoxAction { kind = ActKind.UseObject, furn = tv, inter = tv.def.actions[0], label = "тв", byPlayer = true });
                if (easelM != null && fam.Count > 1) { var pi = easelM.def.actions.Find(x => x.name == "Рисовать"); fam[1].Command(new FoxAction { kind = ActKind.UseObject, furn = easelM, inter = pi, label = "рисует", byPlayer = true }); }
                g.SetSpeed(2);
                yield return new WaitForSeconds(9f);
                g.SetSpeed(1);
                log.WriteLine($"МЕДИА: {(fam.Count > 0 ? fam[0].ActivityText : "-")} / {(fam.Count > 1 ? fam[1].ActivityText : "-")}, мольберт {(easelM != null)}");
                foreach (var mf in Object.FindObjectsByType<MediaFX>(FindObjectsSortMode.None)) log.WriteLine("МЕДИА-ВИД: " + mf.DebugState);
                for (int k = 0; k < 2; k++)
                {
                    if (tv != null) { g.cam.target = tv.transform.TransformPoint(new Vector3(0, 0.7f, -1.6f)); g.cam.dist = 1.4f; g.cam.yaw = tv.transform.eulerAngles.y + k * 180f; g.cam.pitch = 8f; }
                    yield return new WaitForSeconds(1f);
                    yield return Shot("41_tv" + k);
                    if (easelM != null) { g.cam.target = easelM.transform.TransformPoint(new Vector3(0, 0.9f, -1.4f)); g.cam.dist = 1.2f; g.cam.yaw = easelM.transform.eulerAngles.y + k * 180f; g.cam.pitch = 8f; }
                    yield return new WaitForSeconds(1f);
                    yield return Shot("42_easel" + k);
                }
                var comp = g.furniture.Find(f => f.def.id == "computer" && f.lot == House.PlayerLotId);
                if (comp != null && fam.Count > 0)
                {
                    if (fam[0].current != null) fam[0].Cancel(fam[0].current);
                    fam[0].Command(new FoxAction { kind = ActKind.UseObject, furn = comp, inter = comp.def.actions[0], label = "комп", byPlayer = true });
                    g.SetSpeed(2);
                    yield return new WaitForSeconds(8f);
                    g.SetSpeed(1);
                    foreach (var mf in Object.FindObjectsByType<MediaFX>(FindObjectsSortMode.None)) if (mf.furn == comp) log.WriteLine("КОМПЬЮТЕР: " + mf.DebugState + " / " + fam[0].ActivityText);
                    g.cam.target = comp.transform.TransformPoint(new Vector3(0, 0.95f, -1.4f)); g.cam.dist = 1.3f; g.cam.yaw = comp.transform.eulerAngles.y; g.cam.pitch = 8f;
                    yield return new WaitForSeconds(1f);
                    yield return Shot("43_computer");
                }
                g.cam.target = new Vector3(7f, 0, 4f); g.cam.yaw = 0; g.cam.pitch = 48; g.cam.dist = 15f;
            }

            g.SetMode(Mode.Style);
            var sofa = g.furniture.Find(f => f.def.id == "sofa");
            g.styleTarget = sofa.style;
            g.stylePart = 1;
            yield return new WaitForSeconds(1f);
            yield return Shot("2_style");

            g.SetMode(Mode.Create);
            yield return new WaitForSeconds(1.5f);
            g.ui.outfitTab = 1;
            yield return new WaitForSeconds(1f);
            yield return Shot("3b_create_pajama");
            g.ui.outfitTab = 0;
            yield return Shot("3_create");

            // дом снаружи с поднятыми стенами — проверка окон и двери
            g.SetMode(Mode.Live);
            g.cam.Focus(g.foxes[2].transform.position, 3.2f);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("3c_closeup");
            foreach (var fx in g.foxes) log.WriteLine($"НАРЯД {fx.foxName}: {string.Join(", ", fx.outfits[0].items.ConvertAll(it => it.id))} | пижама: {string.Join(", ", fx.outfits[1].items.ConvertAll(it => it.id))}");

            g.house.wallMode = House.WallMode.Up;
            g.cam.target = new Vector3(6f, 0, 3f); g.cam.yaw = 15f; g.cam.pitch = 22f; g.cam.dist = 15f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("13_exterior");
            {
                var nm = g.ui.NameMode; var pm = g.ui.PhraseMode;
                g.ui.TestLabels(GameUI.LabelMode.Visible, GameUI.LabelMode.Visible);
                yield return new WaitForSeconds(0.5f);
                var seen = g.foxes.FindAll(x => g.ui.FoxSeen(x)).ConvertAll(x => x.foxName);
                var hid = g.foxes.FindAll(x => !g.ui.FoxSeen(x) && x.home == House.PlayerLotId).ConvertAll(x => x.foxName);
                log.WriteLine($"ПОДПИСИ: заслоняющих {g.ui.OccluderCount}, видно {seen.Count}: {string.Join(", ", seen)}; своих не видно: {string.Join(", ", hid)}");
                // лисичка за передней стеной (не видна) и перед ней (видна)
                var tf = g.Household.Count > 0 ? g.Household[0] : null;
                var pl = g.house.PlayerLot;
                if (tf != null)
                {
                    int sp0 = g.speedIndex;
                    g.SetSpeed(0);
                    int cx = pl.entry.x + 3, cz = -999;
                    for (int z = pl.rect.yMin; z < pl.rect.yMax && cz == -999; z++) if (g.house.RoomIndexAt(0, new Vector2Int(cx, z)) >= 0) cz = z;
                    if (cz != -999)
                    {
                        var keep = tf.transform.position;
                        tf.transform.position = GridMap.Center(new Vector2Int(cx, cz + 1)) + Vector3.up * g.house.FloorY(0, new Vector2Int(cx, cz + 1));
                        g.ui.TestLabels(GameUI.LabelMode.Visible, GameUI.LabelMode.Visible);
                        yield return new WaitForSeconds(0.4f);
                        bool behind = g.ui.FoxSeen(tf);
                        yield return Shot("13c_label_behind_wall");
                        tf.transform.position = GridMap.Center(new Vector2Int(cx, cz - 2)) + Vector3.up * g.house.FloorY(0, new Vector2Int(cx, cz - 2));
                        g.ui.TestLabels(GameUI.LabelMode.Visible, GameUI.LabelMode.Visible);
                        yield return new WaitForSeconds(0.4f);
                        bool front = g.ui.FoxSeen(tf);
                        yield return Shot("13d_label_front");
                        log.WriteLine($"ПОДПИСИ: {tf.foxName} за стеной видна={behind} (надо False), перед домом видна={front} (надо True)");
                        tf.transform.position = keep;
                    }
                    g.SetSpeed(sp0);
                }
                yield return Shot("13b_exterior_labels");
                g.ui.TestLabels(nm, pm);
            }
            g.house.wallMode = House.WallMode.Auto;

            g.SetMode(Mode.Build);
            g.StartPlacing(Catalog.Get("doublebed"));
            yield return new WaitForSeconds(1f);
            yield return Shot("4_build");
            g.CancelPlacing();
            // отмена и повтор: продали предмет — вернули — снова продали; перекрасили — вернули цвет
            {
                var victim = g.furniture.Find(f => f.lot == House.PlayerLotId && f.def.id == "plant");
                int n0 = g.furniture.Count; float m0 = g.money;
                if (victim != null)
                {
                    g.SellFurniture(victim);
                    int n1 = g.furniture.Count;
                    g.Undo(); int n2 = g.furniture.Count; float m2 = g.money;
                    g.Redo(); int n3 = g.furniture.Count;
                    g.Undo();
                    log.WriteLine($"ОТМЕНА: мебели {n0} → продажа {n1} → отмена {n2} (монет {m0:0}→{m2:0}) → повтор {n3}");
                }
                var sofaSt = g.furniture.Find(f => f.def.id == "sofa")?.style;
                if (sofaSt != null)
                {
                    var c0 = sofaSt.Get(0).c1;
                    var ns = sofaSt.Get(0).Clone(); ns.c1 = Color.magenta;
                    g.StyleEdit(sofaSt, 0, ns);
                    g.Undo();
                    log.WriteLine($"ОТМЕНА СТИЛЯ: цвет вернулся {(sofaSt.Get(0).c1 == c0)}");
                }
            }

            // команды игрока и перекраска — проверяем, что ничего не падает
            var rf = g.foxes[0];
            var easel = g.furniture.Find(f => f.def.id == "easel");
            rf.Command(new FoxAction { kind = ActKind.UseObject, furn = easel, inter = easel.def.actions[0], label = "Рисует" });
            rf.Command(new FoxAction { kind = ActKind.Social, other = g.foxes[1], social = Social.All[1], label = "Пошутить" });
            rf.Command(new FoxAction { kind = ActKind.Move, moveTarget = new Vector3(11.5f, 0, 2.5f), label = "Идёт сюда" });
            g.CopyStyle(sofa.style, 1);
            var arm = g.furniture.Find(f => f.def.id == "armchair");
            arm.style.PasteAll(sofa.style);
            g.furniture.Find(f => f.def.id == "bed").style.SetStyle(1, g.clipboard);
            g.house.RoomAt(new Vector2Int(1, 1)).st.SetStyle(1, StyleData.Make("Кирпич", "#B5651D", "#EFE6DA"));
            // стройка: новая комната во дворе с дверью и окном
            for (int x = 15; x < 19; x++) { g.house.SetEdge(x, 1, false, EdgeKind.Wall); g.house.SetEdge(x, 5, false, EdgeKind.Wall); }
            for (int z = 1; z < 5; z++) { g.house.SetEdge(15, z, true, EdgeKind.Wall); g.house.SetEdge(19, z, true, EdgeKind.Wall); }
            g.house.SetEdge(16, 1, false, EdgeKind.Door);
            g.house.SetEdge(19, 3, true, EdgeKind.Window);
            g.house.SetEdge(17, 5, false, EdgeKind.Window);
            g.house.Rebuild();
            log.WriteLine("КОМНАТЫ: " + string.Join(", ", g.house.rooms.ConvertAll(r => r.name + " " + r.cells.Count)));
            g.house.wallMode = House.WallMode.Up;
            g.cam.target = new Vector3(13f, 0, 3f); g.cam.yaw = 25f; g.cam.pitch = 30f; g.cam.dist = 18f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("14_built");
            g.house.wallMode = House.WallMode.Auto;
            g.cam.yaw = 0; g.cam.pitch = 48; g.cam.dist = 15;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("15_cutaway");

            // второй этаж: комната над гостиной, лестница, лисичка поднимается наверх
            for (int x = 4; x < 9; x++) { g.house.SetEdge(1, x, 0, false, EdgeKind.Wall); g.house.SetEdge(1, x, 6, false, EdgeKind.Wall); }
            for (int z = 0; z < 6; z++) { g.house.SetEdge(1, 4, z, true, EdgeKind.Wall); g.house.SetEdge(1, 9, z, true, EdgeKind.Wall); }
            g.house.SetEdge(1, 6, 0, false, EdgeKind.Window);
            g.house.SetEdge(1, 9, 3, true, EdgeKind.Window);
            g.house.Rebuild();
            var stairs = Catalog.Get("stairs");
            bool canStairs = g.CanPlace(stairs, new Vector2Int(8, 1), 2, null, 0);
            if (canStairs) g.SpawnFurniture(stairs, new Vector2Int(8, 1), 2, false, 0);
            bool canPlant = g.CanPlace(Catalog.Get("plant"), new Vector2Int(5, 4), 0, null, 1);
            if (canPlant) g.SpawnFurniture(Catalog.Get("plant"), new Vector2Int(5, 4), 0, false, 1);
            log.WriteLine($"ЭТАЖ 2: комнат {g.house.rooms.FindAll(r => r.level == 1).Count}, лестница {canStairs}, растение {canPlant}");
            var climber = g.foxes[2];
            climber.Command(new FoxAction { kind = ActKind.Move, moveTarget = new Vector3(5.5f, House.WallH, 2.5f), label = "Наверх" });
            for (int k = 0; k < 20 && climber.Level == 0; k++) yield return new WaitForSeconds(0.5f);
            log.WriteLine($"ПОДЪЁМ: {climber.foxName} этаж {climber.Level + 1}, y={climber.transform.position.y:0.00}, {climber.ActivityText}");
            g.SetViewLevel(1);
            g.cam.target = new Vector3(6.5f, 2f, 3f); g.cam.yaw = 20f; g.cam.pitch = 45f; g.cam.dist = 13f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("16_upstairs");
            g.house.showRoof = true; g.house.ApplyVisibility();
            g.cam.target = new Vector3(8f, 0, 5f); g.cam.yaw = 30f; g.cam.pitch = 28f; g.cam.dist = 22f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("17_roof");
            log.WriteLine("КРЫШИ: " + g.house.RoofInfo());

            // веранда-беседка во дворе и разные крыши
            for (int x = 15; x < 19; x++) { g.house.SetEdge(0, x, 9, false, EdgeKind.Railing); g.house.SetEdge(0, x, 13, false, EdgeKind.Railing); }
            for (int z = 9; z < 13; z++) { g.house.SetEdge(0, 15, z, true, EdgeKind.Railing); g.house.SetEdge(0, 19, z, true, EdgeKind.None); g.house.SetEdge(0, 19, z, true, EdgeKind.Railing); }
            g.house.SetEdge(0, 16, 9, false, EdgeKind.None); // вход в беседку
            g.house.SetEdge(0, 16, 9, false, EdgeKind.Railing);
            g.house.SetEdge(0, 17, 9, false, EdgeKind.RailingOpen);
            g.house.Rebuild();
            var up = g.house.RoomAt(1, new Vector2Int(5, 2));
            if (up != null) { up.roofType = (int)House.RoofType.Shed; up.roofDir = 0; }
            var living2 = g.house.RoomAt(0, new Vector2Int(1, 1));
            if (living2 != null) { living2.roofType = (int)House.RoofType.Hip; living2.roofPitch = 0.5f; }
            var yardRoom = g.house.RoomAt(0, new Vector2Int(16, 2));
            if (yardRoom != null) { yardRoom.roofType = (int)House.RoofType.Flat; }
            g.house.RebuildRoofs();
            log.WriteLine("ВЕРАНДА: " + string.Join(", ", g.house.rooms.ConvertAll(r => r.name + " " + House.RoofTypeNames[r.roofType])));
            log.WriteLine("КРЫШИ2: " + g.house.RoofInfo());
            g.cam.target = new Vector3(11f, 1f, 7f); g.cam.yaw = 35f; g.cam.pitch = 32f; g.cam.dist = 24f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("19_roof_types");

            // ландшафт и сад
            var tr = g.house.terrain;
            for (int i = 0; i < 5; i++) tr.ApplyBrush(new Vector3(-6f, 0, 5f), 2.6f, VoxelTerrain.Brush.Raise, 0);
            tr.ApplyBrush(new Vector3(17.5f, 0, 15f), 1.2f, VoxelTerrain.Brush.Pond, 0);
            g.house.UpdateGrid();
            for (int i = 0; i < 6; i++) tr.AddFlowers(new Vector3(10f + i * 0.6f, 0, 14.5f), 500 + i);
            g.house.PlantTree(new Vector3(3f, 0, 14f), 1.1f, false);
            g.house.SettleTrees(new Vector3(-6f, 0, 5f), 3f);
            bool pondWater = tr.IsWaterCell(new Vector2Int(17, 15));
            int placed = 0;
            foreach (var (id, x, z) in new[] { ("bench", 11, 12), ("swing", 6, 12), ("gardenlamp", 9, 11), ("flowerbed", 14, 11), ("birdbath", 3, 12) })
            {
                var d = Catalog.Get(id);
                if (g.CanPlace(d, new Vector2Int(x, z), 0, null, 0)) { g.SpawnFurniture(d, new Vector2Int(x, z), 0, false, 0); placed++; }
            }
            log.WriteLine($"САД: пруд на участке {pondWater}, на воде можно ходить: {g.house.grids[0].IsFree(new Vector2Int(17, 15))}, садовых предметов {placed}, деревьев {g.house.trees.Count}, кустиков {tr.flowers.Count}");
            g.house.showRoof = true; g.house.ApplyVisibility();
            g.cam.target = new Vector3(5f, 0.5f, 10f); g.cam.yaw = 200f; g.cam.pitch = 32f; g.cam.dist = 26f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("20_garden");
            g.house.showRoof = false; g.house.ApplyVisibility();
            g.cam.target = new Vector3(7f, 2f, 5f); g.cam.yaw = 210f; g.cam.pitch = 35f; g.cam.dist = 20f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("18_roof_back");
            g.house.showRoof = false; g.SetViewLevel(0);
            g.cam.yaw = 0; g.cam.pitch = 48; g.cam.dist = 15;
            g.foxes[2].style.SetStyle(0, StyleData.Make("Полоска", "#D8B07A", "#A8743F", 1.5f));
            var nf = g.AddRandomFox();
            log.WriteLine("Новая лисичка: " + nf.foxName + " " + string.Join(",", nf.pers.v));
            g.foxes[0].TakeJob(Careers.Get("art"));
            g.foxes[1].TakeJob(Careers.Get("cook"));
            // семья: свадьба и скорые лисята
            var ry = g.foxes[0]; var sh = g.foxes[1];
            Social.AddRom(ry, sh, 85);
            g.MakeCouple(ry, sh);
            g.Marry(ry, sh);
            g.StartPregnancy(ry, sh);
            sh.pregnantMinutes = 2860f;
            g.AdoptKit(g.foxes[2]);

            g.SetMode(Mode.Live);
            g.cam.Focus(new Vector3(7, 0, 5), 20f);
            g.cam.dist = 17f;
            g.SetSpeed(3);
            Fox kit2() => g.foxes.Find(x => x.stage == LifeStage.Kit && x.motherId == sh.id);
            for (int i = 0; i < 24; i++)
            {
                yield return new WaitForSeconds(5f);
                if (i == 8)
                {
                    var kit = g.foxes.Find(x => x.stage == LifeStage.Kit && x.motherId == sh.id);
                    var carry = Social.All.Find(d => d.name == "Поносить лисёнка");
                    g.autonomy = false;
                    if (kit != null && kit.current != null) kit.Cancel(kit.current);
                    if (kit != null) { kit.queue.Clear(); kit.needs[1] = 100; }
                    if (kit != null) ry.Command(new FoxAction { kind = ActKind.Social, other = kit, social = carry, label = "Поносить лисёнка" });
                    log.WriteLine("КОМАНДА: поносить " + (kit != null ? kit.foxName : "НЕТ ЛИСЁНКА"));
                }
                if (i == 8)
                {
                    for (int k = 0; k < 6; k++)
                    {
                        yield return new WaitForSeconds(0.4f);
                        log.WriteLine($"CARRY {k}: {ry.ActivityText} | лисёнок: {(kit2() != null ? kit2().ActivityText + (kit2().carried ? " НЕСУТ" : "") : "-")}");
                        if (kit2() != null && kit2().carried) { g.cam.Focus(ry.transform.position, 5f); yield return new WaitForSeconds(0.3f); yield return Shot("12_carry"); break; }
                    }
                    g.autonomy = true;
                }
                Dump();
            }
            g.SetSpeed(1);
            yield return Shot("5_later");

            g.Select(g.foxes[0]);
            g.ui.infoOpen = true; g.ui.foxTab = 0; yield return new WaitForSeconds(0.5f); yield return Shot("7b_states");
            g.ui.foxTab = 3; yield return new WaitForSeconds(0.5f); yield return Shot("8_skills");
            g.ui.foxTab = 4; yield return new WaitForSeconds(0.5f); yield return Shot("9_work");
            g.ui.foxTab = 5; yield return new WaitForSeconds(0.5f); yield return Shot("10_wants");
            g.ui.foxTab = 7; yield return new WaitForSeconds(0.5f); yield return Shot("11_family");
            g.ui.foxTab = 8; yield return new WaitForSeconds(0.5f); yield return Shot("11_diary");
            // старение: короткая жизнь и пара прожитых дней
            g.lifespan = Lifespan.Short;
            for (int d = 0; d < 3; d++) { g.minutes += 1440f; yield return null; }
            foreach (var f in g.foxes) log.WriteLine($"ВОЗРАСТ {f.foxName}: {f.StageName}, {f.ageDays} дн., школа/работа: {(f.career != null ? f.career.name : "-")}");
            g.ui.foxTab = 0;

            foreach (var e in g.log) log.WriteLine($"LOG {e.time} {e.text}");
            log.WriteLine($"ПОСУДА: немытых тарелок сейчас {g.PlateCount}");
            // сохранение и загрузка
            string before = $"{g.money:0} мон, {g.foxes[0].JobTitle}, навыки {string.Join(",", g.foxes[0].skillLvl)}, желаний {g.foxes[0].wants.Count}, {g.foxes.Count} лис, {g.furniture.Count} предметов, стены {ColorUtility.ToHtmlStringRGB(g.house.RoomAt(new Vector2Int(1, 1)).st.Get(1).c1) + " комнат " + g.house.rooms.Count}, {g.ClockText}";
            speaker.wants.RemoveAll(w => w.id == "talk");
            speaker.wants.Add(new Want { id = "talk", arg = "Рыжик", text = "Поболтать: Рыжик", points = 120 });
            const int customLot = 3;
            g.RenameLot(customLot, "Дом у сада");
            Town.Lots[customLot].nameOverrides = "Дома у сада|Дому у сада|Дом у сада|Домом у сада|Доме у сада".Split('|');
            Town.Lots[customLot].preposition = LotPreposition.In;
            g.cam.Follow(null);
            g.cam.target = new Vector3(14f, g.house.GroundUnder(new Vector3(14, 0, 8)), 8f);
            g.cam.yaw = 123f; g.cam.pitch = 37f; g.cam.dist = 11f;
            g.cam.Snap();
            bool saved = g.SaveGame();
            g.ResetWorld(true);
            g.SetupTitleScene(0);
            g.ui.screen = GameUI.MenuScreen.Title;
            yield return null;
            g.ContinueGame();
            yield return new WaitForSeconds(0.2f);
            while (g.loading) yield return null;
            bool loaded = g.started;
            string after = $"{g.money:0} мон, {g.foxes[0].JobTitle}, навыки {string.Join(",", g.foxes[0].skillLvl)}, желаний {g.foxes[0].wants.Count}, {g.foxes.Count} лис, {g.furniture.Count} предметов, стены {ColorUtility.ToHtmlStringRGB(g.house.RoomAt(new Vector2Int(1, 1)).st.Get(1).c1) + " комнат " + g.house.rooms.Count}, {g.ClockText}";
            log.WriteLine($"SAVE {saved} LOAD {loaded}\n  до:    {before}\n  после: {after}");
            var lotWithForms = g.house.LotById(customLot);
            if (lotWithForms?.At != "в Доме у сада" || lotWithForms.Into != "в Дом у сада" ||
                lotWithForms.NameIn(NameCase.Genitive) != "Дома у сада")
                Debug.LogError("УЧАСТКИ: формы названия не восстановились.");
            else log.WriteLine($"УЧАСТКИ: восстановлены формы — {lotWithForms.At}, {lotWithForms.Into}.");
            if (Mathf.Abs(g.cam.target.x - 14) > 0.05f || Mathf.Abs(g.cam.target.z - 8) > 0.05f ||
                Mathf.Abs(Mathf.DeltaAngle(g.cam.yaw, 123)) > 0.05f || Mathf.Abs(g.cam.pitch - 37) > 0.05f || Mathf.Abs(g.cam.dist - 11) > 0.05f)
                Debug.LogError("КАМЕРА: после загрузки не вернулась к сохранённому ракурсу.");
            else log.WriteLine("КАМЕРА: положение, углы и масштаб восстановлены после титульной сцены.");
            var loadedFox = g.foxes.Find(f => f.id == nameFoxId);
            if (loadedFox?.NameIn(NameCase.Genitive) != "Рыжи")
                Debug.LogError("ПАДЕЖИ: ручная форма не восстановилась из сохранения.");
            else log.WriteLine("ПАДЕЖИ: ручная форма сохранена и загружена — Рыжи.");
            var loadedWant = g.foxes.Find(f => f.id == speakerId)?.wants.Find(w => w.id == "talk");
            if (loadedWant?.targetFoxId != nameFoxId || loadedWant.DisplayText != "Поговорить с Рыжиком")
                Debug.LogError("ОБЩЕНИЕ: желание из старого сохранения не обновилось.");
            else log.WriteLine("ОБЩЕНИЕ: старое желание восстановлено — " + loadedWant.DisplayText);
            yield return new WaitForSeconds(1f);
            yield return Shot("6_loaded");
            g.cam.Restore(g, null);
            if (g.selected == null || Vector2.Distance(new Vector2(g.cam.target.x, g.cam.target.z),
                    new Vector2(g.selected.transform.position.x, g.selected.transform.position.z)) > 0.05f)
                Debug.LogError("КАМЕРА: без ракурса в сейве не нашла выбранную лисичку.");
            else log.WriteLine("КАМЕРА: старый сейв открывается у выбранной лисички.");
            g.ui.screen = GameUI.MenuScreen.Pause;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("7_pause");
            g.ui.screen = GameUI.MenuScreen.None;
            // переезд на свободный участок
            {
                g.freeBuild = true;
                var vac = g.VacantHomes();
                var target = vac.Find(l => !g.house.rooms.Exists(r => r.lot == l.id)) ?? (vac.Count > 0 ? vac[0] : null);
                if (target != null && g.LotIsEmpty(target.id))
                {
                    int grant = g.MoveInGrant(target.id);
                    if (grant != 6000) Debug.LogError("ПОДЪЁМНЫЕ: за пустой участок начисляется " + grant);
                    else log.WriteLine("ПОДЪЁМНЫЕ: за пустой участок — 6000.");
                }
                bool moved = target != null && g.MoveTo(target.id);
                log.WriteLine($"ПЕРЕЕЗД: {moved}, теперь дом: {g.house.PlayerLot.name}, семья: {string.Join(", ", g.Household.ConvertAll(f => f.foxName + " " + g.house.LotAt(f.Cell)?.name))}");
                yield return new WaitForSeconds(2f);
                yield return Shot("8_moved");
                // и стройка на новом месте: маленький домик
                var pl = g.house.PlayerLot.rect;
                for (int x = pl.xMin + 3; x < pl.xMin + 9; x++) { g.house.SetEdge(x, pl.yMin + 3, false, EdgeKind.Wall); g.house.SetEdge(x, pl.yMin + 8, false, EdgeKind.Wall); }
                for (int z = pl.yMin + 3; z < pl.yMin + 8; z++) { g.house.SetEdge(pl.xMin + 3, z, true, EdgeKind.Wall); g.house.SetEdge(pl.xMin + 9, z, true, EdgeKind.Wall); }
                g.house.SetEdge(pl.xMin + 5, pl.yMin + 8, false, EdgeKind.Door);
                g.house.Rebuild();
                log.WriteLine($"НОВЫЙ ДОМ: комнат на участке {g.house.rooms.FindAll(r => r.lot == House.PlayerLotId).Count}, высота пола {(g.house.rooms.Find(r => r.lot == House.PlayerLotId)?.elev ?? -99):0.00}");
                int houseGrant = g.MoveInGrant(House.PlayerLotId);
                if (moved && houseGrant != 4000) Debug.LogError("ПОДЪЁМНЫЕ: за пустой дом начисляется " + houseGrant);
                else if (moved) log.WriteLine("ПОДЪЁМНЫЕ: за пустой дом — 4000.");
                g.cam.Focus(new Vector3(pl.xMin + 6, 0, pl.yMin + 5), 16f);
                yield return new WaitForSeconds(2f);
                yield return Shot("9_newhouse");
            }
            // город: выселить соседей в архив и заселить их на свободный участок
            {
                g.EnterTown();
                var fam = g.FamilyOf(1);
                g.EvictFamily(1);
                var groups = g.StoredGroups();
                log.WriteLine($"АРХИВ: выселено {fam.Count}, групп {groups.Count}, лис в городе {g.foxes.Count}");
                var free = g.house.lots.Find(l => l.kind == LotKind.Home && g.FamilyOf(l.id).Count == 0 && l.id != 1 && l.id != House.PlayerLotId);
                if (groups.Count > 0 && free != null) g.SettleStored(groups[0], free.id);
                log.WriteLine($"ЗАСЕЛЕНИЕ: {free?.name}: {Game.FamilyTitle(g.FamilyOf(free != null ? free.id : -1))}");
                var emptyLot = g.house.lots.Find(l => !l.removed && l.kind == LotKind.Home && g.LotIsEmpty(l.id));
                log.WriteLine($"ДЕНЬГИ НОВОЙ СЕМЬИ: на участке 1 {g.NewFamilyMoney(1)}, на пустом {(emptyLot != null ? emptyLot.name + " " + g.NewFamilyMoney(emptyLot.id) : "нет пустого")}");
                g.SetLotMoney(1, 3333);
                var newFam = g.CreateFamily(1);
                log.WriteLine($"НОВАЯ СЕМЬЯ: {(newFam != null ? newFam.foxName : "нет")} на участке 1, денег {g.MoneyOf(1):0} (задано 3333)");
                g.SetLotMoney(1, null);
                g.ui.townLot = 1;
                yield return new WaitForSeconds(1.5f);
                yield return Shot("9b_town_after");
            }
            // мечта всей жизни и кнопки этажей сбоку (в режиме жизни)
            {
                g.PlayFamily(House.PlayerLotId);
                g.SetMode(Mode.Live);
                g.ui.showViewTools = true;
                var me = g.Household.Find(x => x.stage >= LifeStage.Teen);
                if (me != null) { g.Select(me); yield return null; yield return null; g.ui.ThinkAboutSelf(5); }
                yield return new WaitForSeconds(1.5f);
                yield return Shot("98_dream");
            }
            // главное меню: семья из сохранения у костра и у ручья
            {
                g.PlayFamily(House.PlayerLotId);
                g.SaveGame();
                log.WriteLine($"МЕНЮ: в сохранении семья из {SaveSystem.LastFamily().Count}");
                g.ui.screen = GameUI.MenuScreen.Title;
                for (int k = 0; k < 2; k++)
                {
                    g.SetupTitleScene(k);
                    yield return new WaitForSeconds(3f);
                    yield return Shot("99_title_" + g.titlePlace);
                }
            }
            g.started = false; // чтобы выход не перезаписал тестовый файл
            foreach (var e in g.log) log.WriteLine($"LOG {e.time} {e.text}");
            log.Close();
            Application.Quit();
        }
    }
}
