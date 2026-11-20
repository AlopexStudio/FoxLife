using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Экран загрузки, меню и настройки, экран города (выбор и редактирование семей), панель одежды.</summary>
    public partial class GameUI
    {
        public int townLot = -1;
        public bool townEditing;
        public Fox townEditFox;
        public bool showViewTools = true;
        public bool clothesOpen;
        string moneyText = "";

        // ---------- кого редактируем в «создании лисички» ----------

        Fox CasFox => G.mode == Mode.Town ? townEditFox : G.selected;
        int CasLot => G.mode == Mode.Town ? townLot : House.PlayerLotId;
        List<Fox> CasFamily => G.FamilyOf(CasLot);

        void CasSelect(Fox f)
        {
            if (G.mode == Mode.Town) townEditFox = f; else G.Select(f);
            if (f != null) { G.cam.Focus(f.transform.position, 5f); G.cam.pitch = 25f; }
        }

        // ================= экран загрузки =================

        void LoadingScreen()
        {
            Fill(new Rect(0, 0, vw, vh), new Color(0.86f, 0.93f, 0.98f, 1f));
            var big = new GUIStyle(sTitle) { fontSize = 44, alignment = TextAnchor.MiddleCenter };
            big.normal.textColor = SimsInk;
            GUI.Label(new Rect(0, vh / 2 - 120, vw, 60), "Лисья жизнь", big);
            var sub = new GUIStyle(sBold) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            sub.normal.textColor = SimsInk;
            GUI.Label(new Rect(0, vh / 2 - 40, vw, 30), G.loadingText, sub);
            // полоска и бегущие лапки — видно, что игра не зависла
            var bar = new Rect(vw / 2 - 220, vh / 2 + 10, 440, 14);
            Fill(bar, new Color(1, 1, 1, 0.9f));
            var fill = bar; fill.width *= Mathf.Clamp01(G.loadingProgress);
            Fill(fill, SimsBlue);
            EnsureSims();
            float t = Time.unscaledTime;
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Clamp01(Mathf.Sin(t * 4f - i * 0.8f) * 0.5f + 0.5f);
                var old = GUI.color;
                GUI.color = new Color(SimsBlue.r, SimsBlue.g, SimsBlue.b, 0.25f + a * 0.75f);
                GUI.DrawTexture(new Rect(vw / 2 - 90 + i * 40, vh / 2 + 50 + (i % 2) * 8, 26, 26), Icon("live"));
                GUI.color = old;
            }
        }

        // ================= главное меню, пауза, настройки =================

        public void ShowNote(string text) => Note(text);

        Texture2D titleShade;

        /// <summary>Главное меню поверх живой сцены: слева мягкая тень, крупное название, кнопки; внизу — где мы.</summary>
        void TitleBackdrop()
        {
            if (titleShade == null)
            {
                titleShade = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 128; x++)
                {
                    float k = 1f - x / 127f;
                    titleShade.SetPixel(x, 0, new Color(0.03f, 0.05f, 0.09f, 0.62f * k * k));
                }
                titleShade.Apply();
            }
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(0, 0, vw * 0.55f, vh), titleShade);
            float x0 = 64, y0 = vh * 0.5f - 250;
            var big = new GUIStyle(sTitle) { fontSize = 60, alignment = TextAnchor.MiddleLeft };
            big.normal.textColor = Color.white;
            var subSt = new GUIStyle(sBold) { fontSize = 18 };
            subSt.normal.textColor = Color.white;
            var smallSt = new GUIStyle(sSmall) { fontSize = 14 };
            smallSt.normal.textColor = Color.white;
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.Label(new Rect(x0 + 3, y0 + 4, 600, 80), "Лисья жизнь", big);
            GUI.color = new Color(1f, 0.86f, 0.62f);
            GUI.Label(new Rect(x0, y0, 600, 80), "Лисья жизнь", big);
            GUI.color = new Color(1f, 0.95f, 0.88f, 0.85f);
            GUI.Label(new Rect(x0 + 4, y0 + 74, 600, 30), "уютная жизнь в Лисьей Долине", subSt);
            if (G.TitleSceneActive)
            {
                string place = G.titlePlace == "camp" ? "Вечер у костра на Лесной поляне" : "Закат на берегу ручья";
                GUI.color = new Color(1f, 0.95f, 0.88f, 0.7f);
                GUI.Label(new Rect(x0 + 4, vh - 52, 700, 26), place, smallSt);
            }
            GUI.color = old;
        }

        void ScreenMenu()
        {
            bool title = screen == MenuScreen.Title;
            if (title) TitleBackdrop();
            else Fill(new Rect(0, 0, vw, vh), new Color(0.05f, 0.1f, 0.15f, 0.45f));
            if (screen == MenuScreen.Settings) { SettingsScreen(); return; }
            if (netPanel || (title && Net.IsClient)) { NetPanel(); return; }
            if (townChoices != null) { TownPicker(); return; }
            bool hasSave = SaveSystem.HasSave;
            float w = title ? 360 : 420, h = confirmNew ? 330 : title ? (hasSave ? 284 : 220) : Net.IsClient ? 300 : (hasSave ? 608 : 558) - (Net.Active ? 48 : 0);
            var r = title ? new Rect(64, vh * 0.5f - 130, w, h) : new Rect((vw - w) / 2, (vh - h) / 2, w, h);
            var oldBg = GUI.backgroundColor;
            if (title) GUI.backgroundColor = new Color(1f, 1f, 1f, 0.82f);
            GUILayout.BeginArea(r, sPanel);
            GUI.backgroundColor = oldBg;
            var sub = new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            if (!title)
            {
                var big = new GUIStyle(sTitle) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
                GUILayout.Space(4);
                GUILayout.Label("Лисья жизнь", big);
                GUILayout.Label("пауза", sub);
                GUILayout.Space(16);
            }
            else GUILayout.Space(8);

            if (confirmNew)
            {
                GUILayout.Label("Начать заново? Нынешний город пропадёт, если его не сохранить.", new GUIStyle(sWrap) { alignment = TextAnchor.MiddleCenter, fontSize = 15 });
                GUILayout.Space(10);
                if (Btn("Да, начать новую игру", false, Hh(40))) Defer(() => { confirmNew = false; StartNew(); });
                if (Btn("Нет", false, Hh(40))) Defer(() => confirmNew = false);
                GUILayout.EndArea();
                return;
            }

            if (title)
            {
                if (hasSave)
                {
                    if (Btn("Продолжить", true, Hh(44))) Defer(() => G.ContinueGame());
                    GUILayout.Label(SaveSystem.SaveInfo ?? "", sub);
                }
                if (Btn("Новая игра", !hasSave, Hh(44))) Defer(() => { if (hasSave) confirmNew = true; else StartNew(); });
                if (Btn(new GUIContent("Играть вместе", "Зайти в город к другу по сети (Radmin VPN или домашняя сеть)"), false, Hh(40))) Defer(() => netPanel = true);
                if (Btn("Настройки", false, Hh(40))) Defer(() => { settingsFrom = MenuScreen.Title; screen = MenuScreen.Settings; });
                if (Btn("Выйти", false, Hh(40))) Defer(Quit);
            }
            else if (Net.IsClient)
            {
                // в гостях у друга: сохраняет и меняет город хозяин
                if (Btn("Вернуться в игру", true, Hh(42))) Defer(() => screen = MenuScreen.None);
                if (Btn("Настройки", false, Hh(40))) Defer(() => { settingsFrom = MenuScreen.Pause; screen = MenuScreen.Settings; });
                if (Btn(new GUIContent("Играть вместе", "Кто в игре, покинуть игру друга"), false, Hh(40))) Defer(() => netPanel = true);
                if (Btn("Покинуть игру друга", false, Hh(40))) Defer(() => Net.I.Stop("Ты вышел(ла) из игры друга."));
                GUILayout.Label("Пока меню открыто, у всех пауза.", sub);
            }
            else
            {
                bool canReturn = G.mode == Mode.Town || G.Household.Count > 0;
                if (canReturn && Btn("Вернуться в игру", true, Hh(42))) Defer(() => screen = MenuScreen.None);
                if (Btn("Сохранить", false, Hh(40))) Defer(() => Note(G.SaveGame() ? "Сохранено!" : "Не получилось сохранить."));
                if (G.mode != Mode.Town && Btn(new GUIContent(Net.IsHost ? "Играть вместе (игра открыта)" : "Играть вместе", "Открыть этот город для друга по сети"), Net.IsHost, Hh(40))) Defer(() => netPanel = true);
                if (hasSave && Btn(new GUIContent("Загрузить сохранение  (" + SaveSystem.SaveInfo + ")"), false, Hh(40))) Defer(() => G.ContinueGame());
                if (!Net.Active && Btn(new GUIContent("Редактирование города", "Выбирай, создавай и переселяй семьи. Меняй участки, ландшафт и оформление города."), false, Hh(40))) Defer(() => G.EnterTown());
                if (Btn("Настройки", false, Hh(40))) Defer(() => { settingsFrom = MenuScreen.Pause; screen = MenuScreen.Settings; });
                if (Btn("Новая игра", false, Hh(40))) Defer(() => confirmNew = true);
                if (G.mode == Mode.Town && !Net.Active && Btn(new GUIContent("Строить город", "Перестроить весь город"), false, Hh(40))) Defer(() => G.EnterTownBuilder());
                if (Btn("Сохранить и выйти", false, Hh(40))) Defer(Quit);
            }
            if (!string.IsNullOrEmpty(menuNote) && Time.unscaledTime - menuNoteTime < 3f)
            {
                GUILayout.Space(6);
                GUILayout.Label(menuNote, new GUIStyle(sBold) { alignment = TextAnchor.MiddleCenter });
            }
            GUILayout.EndArea();
        }

        MenuScreen settingsFrom = MenuScreen.Pause;
        List<(string name, string path, string info)> townChoices;

        /// <summary>Новая игра: если сохранены свои города — сначала выбрать город.</summary>
        void StartNew()
        {
            var towns = SaveSystem.ListTowns();
            if (towns.Count == 0) { G.NewGame(); return; }
            townChoices = towns;
        }

        void TownPicker()
        {
            float w = 560, h = Mathf.Min(vh - 40, 170 + (townChoices.Count + 1) * 52);
            GUILayout.BeginArea(new Rect((vw - w) / 2, (vh - h) / 2, w, h), sPanel);
            GUILayout.Label("Где начнём?", new GUIStyle(sTitle) { fontSize = 26 });
            townScroll = GUILayout.BeginScrollView(townScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (Btn(new GUIContent("Лисья Долина (стандартный город)"), true, Hh(44))) Defer(() => { townChoices = null; G.NewGame(); });
            foreach (var t in townChoices)
            {
                var tt = t;
                if (Btn(new GUIContent(t.name + "   ·   " + t.info, t.path), false, Hh(44))) Defer(() => { townChoices = null; G.NewGameFromTown(tt.path); });
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (Btn("Открыть папку городов", false, W(220))) Defer(() => Application.OpenURL("file:///" + SaveSystem.TownsDir.Replace("\\", "/")));
            if (Btn("Назад", false, W(120))) Defer(() => townChoices = null);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void SettingsScreen()
        {
            bool inGame = settingsFrom == MenuScreen.Pause;
            float w = 560, h = inGame ? 760 : 420;
            var r = new Rect((vw - w) / 2, Mathf.Max(10, (vh - h) / 2), w, Mathf.Min(h, vh - 20));
            GUILayout.BeginArea(r, sPanel);
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Label("Настройки", new GUIStyle(sTitle) { fontSize = 26 });
            GUILayout.Space(6);

            GUILayout.Label("Экран и звук", sBig);
            bool full = Screen.fullScreenMode != FullScreenMode.Windowed;
            if (Btn(full ? "Играть в окне" : "Во весь экран", false, Hh(36)))
                Defer(() =>
                {
                    if (full) Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
                    else Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
                });
            if (Btn("Прокрутка у края экрана — " + (G.cam.edgeScroll ? "вкл" : "выкл"), false, Hh(36))) Defer(() => G.cam.edgeScroll = !G.cam.edgeScroll);
            if (Btn(new GUIContent("Красивая картинка — " + (PostFX.enabledFx ? "вкл" : "выкл"), "Свечение фонарей, мягкие цвета, виньетка"), false, Hh(36))) Defer(() => { PostFX.enabledFx = !PostFX.enabledFx; PostFX.SaveSettings(); });
            if (Btn(new GUIContent("Эффект миниатюры — " + (PostFX.tiltShift ? "вкл" : "выкл"), "Края кадра чуть размыты, будто смотришь на игрушечный городок"), false, Hh(36))) Defer(() => { PostFX.tiltShift = !PostFX.tiltShift; PostFX.SaveSettings(); });
            if (G.sound != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Музыка", sBold, W(90));
                float mv = GUILayout.HorizontalSlider(G.sound.musicVolume, 0f, 1f, Hh(24));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Звуки", sBold, W(90));
                float sv = GUILayout.HorizontalSlider(G.sound.soundVolume, 0f, 1f, Hh(24));
                GUILayout.EndHorizontal();
                if (mv != G.sound.musicVolume || sv != G.sound.soundVolume) { G.sound.musicVolume = mv; G.sound.soundVolume = sv; G.sound.SaveVolumes(); }
            }

            if (inGame)
            {
                GUILayout.Space(8);
                GUILayout.Label("Игра", sBig);
                if (Net.IsClient) GUILayout.Label("По сети погоду, время суток и правила города меняет хозяин игры.", sWrap);
                else if (Btn(new GUIContent("Жизнь лисичек — " + Family.LifespanName(G.lifespan), "Как быстро лисички растут и стареют"), false, Hh(36)))
                    Defer(() => G.lifespan = (Lifespan)(((int)G.lifespan + 1) % 4));
                if (Btn("Свобода воли — " + (G.autonomy ? "вкл" : "выкл"), false, Hh(36))) Defer(() => G.autonomy = !G.autonomy);
                if (Btn(new GUIContent("Наши лисички сами переодеваются — " + (G.autoOutfits ? "да" : "нет"), "Ко сну — в пижаму, под дождём — в дождевик"), false, Hh(36))) Defer(() => G.autoOutfits = !G.autoOutfits);

                GUILayout.Space(8);
                GUILayout.Label("Погода", sBig);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < Weather.Names.Length; i++)
                {
                    var k = (WeatherKind)i;
                    if (Btn(Weather.Names[i], G.weather.kind == k, W(98))) Defer(() => { G.weather.Set(k, 6f); });
                }
                GUILayout.EndHorizontal();
                if (Btn(new GUIContent("Погода меняется сама — " + (G.weather.locked ? "нет" : "да"), "Если выключить — выбранная погода останется, пока её не сменить"), false, Hh(34)))
                    Defer(() => G.weather.locked = !G.weather.locked);

                GUILayout.Space(8);
                GUILayout.Label("Время суток", sBig);
                GUILayout.BeginHorizontal();
                (string n, float hr)[] times = { ("Утро", 7.5f), ("День", 13f), ("Вечер", 19f), ("Ночь", 23f) };
                foreach (var tm in times)
                {
                    var tt = tm;
                    if (Btn(tt.n, false, W(122))) Defer(() => G.SetHour(tt.hr));
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.Label("Хитрости", sBig);
                if (Btn(new GUIContent("Бесплатное строительство — " + (G.freeBuild ? "вкл" : "выкл"), "Мебель и стены ничего не стоят"), false, Hh(34)))
                    Defer(() => G.freeBuild = !G.freeBuild);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Деньги семьи", sBold, W(120));
                GUI.SetNextControlName("money");
                if (string.IsNullOrEmpty(moneyText)) moneyText = Mathf.RoundToInt(G.money).ToString();
                moneyText = GUILayout.TextField(moneyText, 9, sField, W(140));
                if (Btn("Установить", false, W(120))) Defer(() => { if (int.TryParse(moneyText, out int v)) { G.money = Mathf.Max(0, v); Note($"У семьи теперь {G.money:0} мон."); } });
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                foreach (var add in new[] { 1000, 10000, 50000 })
                {
                    int a = add;
                    if (Btn("+" + a, false, W(120))) Defer(() => { G.money += a; moneyText = Mathf.RoundToInt(G.money).ToString(); });
                }
                GUILayout.EndHorizontal();
                if (Btn(new GUIContent("Все нужды семьи — полные", "Сытость, бодрость, гигиена, веселье и общение — до максимума"), false, Hh(34)))
                    Defer(() => { foreach (var f in G.Household) for (int i = 0; i < f.needs.Length; i++) f.needs[i] = 100f; });
            }
            GUILayout.Space(10);
            if (Btn("Назад", true, Hh(40))) Defer(() => { moneyText = ""; screen = settingsFrom; });
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        Vector2 settingsScroll, townScroll;

        // ================= экран города =================

        void TownScreen()
        {
            TownLabels();
            // плашка в левом нижнем углу: название города и «Сохранить город»
            var titleSt = new GUIStyle(sTitle) { wordWrap = false };
            float tw = Mathf.Clamp(titleSt.CalcSize(new GUIContent(G.townName + " — семьи")).x + 200, 380, 620);
            Begin(new Rect(10, vh - 68, tw, 58));
            GUILayout.BeginHorizontal();
            GUILayout.Label(G.townName + " — семьи", titleSt);
            GUILayout.FlexibleSpace();
            if (Btn(new GUIContent("Сохранить город…", "Сохранить город в файл, чтобы начинать в нём новые игры или поделиться с друзьями"), false, W(170))) Defer(() => { townSaveName = G.townName; townSaveOpen = true; });
            GUILayout.EndHorizontal();
            End();
            if (townSaveOpen) { TownSaveDialog(); return; }
            if (townEditing) { CreatePanel(); return; }
            StoredPanel();
            LotPanel();
        }

        bool townSaveOpen;
        string townSaveName = "";

        void TownSaveDialog()
        {
            float w = 520, h = 250;
            Begin(new Rect((vw - w) / 2, (vh - h) / 2, w, h));
            GUILayout.Label("Сохранить город", sTitle);
            GUILayout.Label("Город сохранится в файл. Выбери его при новой игре или отправь друзьям, чтобы они добавили его в свою папку городов.", sWrap);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Название", sBold, W(90));
            GUI.SetNextControlName("townname");
            townSaveName = GUILayout.TextField(townSaveName, 40, sField, W(300));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (Btn("Сохранить", true, W(150))) Defer(() => { townSaveOpen = false; Note(G.SaveTown(townSaveName) ? "Город «" + townSaveName + "» сохранён." : "Не получилось сохранить город."); });
            if (Btn("Открыть папку городов", false, W(210))) Defer(() => Application.OpenURL("file:///" + SaveSystem.TownsDir.Replace("\\", "/")));
            if (Btn("Отмена", false, W(100))) Defer(() => townSaveOpen = false);
            GUILayout.EndHorizontal();
            End();
        }

        void TownLabels()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var lot in G.house.lots)
            {
                if (lot.removed) continue;
                var c = lot.rect.center;
                var wp = new Vector3(c.x, Town.LotElev(lot.id) + 3.5f, c.y);
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z < 0) continue;
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
                var fam = G.FamilyOf(lot.id);
                string text = lot.kind == LotKind.Home ? (fam.Count > 0 ? (lot.id == House.PlayerLotId ? "★ " : "") + Game.FamilyTitle(fam) : "Свободно") : lot.name;
                var st = new GUIStyle(sBold) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false };
                var content = new GUIContent(text);
                float w = st.CalcSize(content).x + 22;
                var r = new Rect(x - w / 2, y - 14, w, 28);
                bool sel = townLot == lot.id;
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.Box(r, GUIContent.none, sel ? sBtnOn : sBtn);
                    st.normal.textColor = sel ? Color.white : lot.kind == LotKind.Home ? (fam.Count > 0 ? SimsInk : new Color(0.25f, 0.55f, 0.3f)) : new Color(0.4f, 0.48f, 0.56f);
                    GUI.Label(r, content, st);
                }
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { int id = lot.id; Defer(() => { townLot = id; G.cam.Focus(new Vector3(lot.rect.center.x, 0, lot.rect.center.y), 999f); }); }
                Reg(r);
            }
        }

        float lotPanelH = 300;

        void LotPanel()
        {
            var lot = G.house.LotById(townLot);
            float w = 440;
            // в правом нижнем углу, высота — по содержимому (больше участков — выше панель)
            float h = Mathf.Clamp(lotPanelH + 34, 120, vh - 110);
            Begin(new Rect(vw - w - 10, vh - h - 10, w, h));
            townScroll = GUILayout.BeginScrollView(townScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical();
            if (lot == null)
            {
                GUILayout.Label("Городок", sTitle);
                GUILayout.Label("Нажми на участок на карте (или на табличку над ним), чтобы выбрать семью или свободный дом.", sWrap);
                GUILayout.Space(6);
                foreach (var l in G.house.lots)
                {
                    if (l.removed) continue;
                    if (l.kind != LotKind.Home) continue;
                    var fam = G.FamilyOf(l.id);
                    var ll = l;
                    if (Btn((l.id == House.PlayerLotId ? "★ " : "") + l.name + " — " + (fam.Count > 0 ? Game.FamilyTitle(fam) : "свободно"), false))
                        Defer(() => { townLot = ll.id; G.cam.Focus(new Vector3(ll.rect.center.x, 0, ll.rect.center.y), 999f); });
                }
            }
            else
            {
                GUILayout.Label(lot.name, sTitle);
                if (lot.kind != LotKind.Home)
                {
                    GUILayout.Label("Сюда могут приходить все жители городка.", sWrap);
                }
                else
                {
                    var fam = G.FamilyOf(lot.id);
                    if (fam.Count > 0)
                    {
                        ColorLabel(lot.id == House.PlayerLotId ? "Сейчас вы играете за эту семью" : "Здесь живёт семья", sBold, SimsBlue);
                        GUILayout.Label($"У семьи {Mathf.RoundToInt(G.MoneyOf(lot.id))} мон.", sSmall);
                        var row = GUILayoutUtility.GetRect(w - 30, 92);
                        for (int i = 0; i < fam.Count; i++)
                        {
                            var pr = new Rect(row.x + i * 66, row.y + 4, 58, 58);
                            if (pr.xMax > row.xMax) break;
                            DrawPortrait(pr, fam[i]);
                            var lbl = new GUIStyle(sSmall) { alignment = TextAnchor.UpperCenter, fontSize = 11, wordWrap = false };
                            GUI.Label(new Rect(pr.x - 4, pr.yMax + 2, 66, 16), fam[i].foxName, lbl);
                            if (pr.Contains(Event.current.mousePosition)) tip = fam[i].foxName + ": " + fam[i].Summary.ToLower() + ", " + fam[i].StageName;
                        }
                        foreach (var f in fam) GUILayout.Label($"{f.foxName} — {f.Summary.ToLower()}, {f.StageName}", sSmall);
                        GUILayout.Space(6);
                        int id = lot.id;
                        if (Btn("Играть за эту семью", true, Hh(42))) Defer(() => G.PlayFamily(id));
                        if (Btn(new GUIContent("Редактировать семью", "Имена, пол, возраст, характер, окрас, одежда; добавить или убрать лисичек"), false, Hh(38)))
                            Defer(() => { townEditing = true; CasSelect(G.FamilyOf(id)[0]); });
                        if (Btn("Переселить…", false, Hh(38))) Defer(() => MoveFamilyMenu(id));
                        if (Btn(new GUIContent("Выселить в архив", "Семья съедет и будет ждать нового дома в архиве"), false, Hh(38))) Defer(() => G.EvictFamily(id));
                    }
                    else
                    {
                        ColorLabel(G.house.rooms.Exists(r => r.lot == lot.id) ? "Свободный дом — можно заселить" : "Пустой участок — можно заселить и построить дом", sBold, new Color(0.25f, 0.55f, 0.3f));
                        int id = lot.id;
                        LotMoneyRow(id);
                        if (Btn("Создать здесь новую семью", true, Hh(42))) Defer(() => { var f = G.CreateFamily(id); if (f != null) { townEditing = true; CasSelect(f); } });
                        var groups = G.StoredGroups();
                        foreach (var g in groups)
                        {
                            var gg = g;
                            if (Btn("Заселить семью из архива («" + Game.FamilyTitle(G.StoredFamily(g)) + "»)", false, Hh(36))) Defer(() => G.SettleStored(gg, id));
                        }
                    }
                }
                GUILayout.Space(8);
                if (Btn("К списку участков")) Defer(() => townLot = -1);
            }
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) lotPanelH = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndScrollView();
            End();
        }

        void MoveFamilyMenu(int from)
        {
            var items = new List<(string, System.Action)>();
            foreach (var l in G.house.lots)
            {
                if (l.removed) continue;
                if (l.kind != LotKind.Home || l.id == from || G.FamilyOf(l.id).Count > 0) continue;
                var ll = l;
                bool house = G.house.rooms.Exists(r => r.lot == l.id);
                items.Add((l.name + (house ? " (дом)" : " (пустой участок)"), () => { if (G.MoveFamily(from, ll.id)) townLot = ll.id; }));
            }
            if (items.Count == 0) items.Add(("Свободных участков нет", () => { }));
            OpenMenu(Input.mousePosition, "Куда переселить?", items);
        }

        void StoredPanel()
        {
            var groups = G.StoredGroups();
            if (groups.Count == 0) return;
            Begin(new Rect(10, 80, 360, 70 + groups.Count * 30));
            GUILayout.Label("Семьи без дома", sBold);
            foreach (var g in groups) GUILayout.Label("• " + Game.FamilyTitle(G.StoredFamily(g)), sSmall);
            GUILayout.Label("Выбери свободный участок, чтобы заселить.", sSmall);
            End();
        }

        // ================= одежда в режиме жизни =================

        void ClothesPanel()
        {
            var f = G.selected;
            if (f == null || !clothesOpen) return;
            // переодеваться можно только у шкафа: отошли — панель закрылась
            if (f.current == null || f.current.inter == null || !f.current.inter.openWardrobe) { clothesOpen = false; return; }
            Begin(InfoRect(Mathf.Min(560, vh - 100)));
            createScroll = GUILayout.BeginScrollView(createScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Одежда " + f.NameIn(NameCase.Genitive), sTitle);
            GUILayout.FlexibleSpace();
            if (Btn("×", false, W(30))) Defer(() => clothesOpen = false);
            GUILayout.EndHorizontal();
            ClothesSection(f);
            GUILayout.EndScrollView();
            End();
        }
    }
}
