using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Вкладки «Стены» и «Ландшафт»: инструменты — карточками с картинками (как каталог мебели), разделами,
    /// с ценой и подсказкой. Крыши — карточками с силуэтом крыши; кисть — тремя кружками разного размера.
    /// </summary>
    public partial class GameUI
    {
        static readonly Color CardBg = new Color(1f, 0.99f, 0.97f), CardOn = new Color(0.98f, 0.62f, 0.32f), CardHover = new Color(0.86f, 0.92f, 0.97f);
        static readonly Color PWall = new Color(0.86f, 0.72f, 0.55f), PWallDark = new Color(0.64f, 0.48f, 0.34f), PWood = new Color(0.62f, 0.42f, 0.26f),
            PGlass = new Color(0.62f, 0.82f, 0.95f), PGreen = new Color(0.45f, 0.7f, 0.35f), PDarkGreen = new Color(0.25f, 0.45f, 0.28f),
            PEarth = new Color(0.62f, 0.48f, 0.32f), PWater = new Color(0.4f, 0.65f, 0.9f), PStone = new Color(0.68f, 0.66f, 0.62f), PRed = new Color(0.85f, 0.32f, 0.25f);

        struct ToolCardDef
        {
            public string name, sub, tip, pict;
            public bool on;
            public System.Action click;
        }

        /// <summary>Сетка карточек-инструментов в 3 столбца.</summary>
        void ToolCards(List<ToolCardDef> cards, float width)
        {
            const float gap = 6, ch = 92;
            int cols = 3;
            float cw = Mathf.Floor((width - gap * (cols - 1)) / cols);
            var nameSt = new GUIStyle(sSmall) { alignment = TextAnchor.UpperCenter, fontSize = 12, wordWrap = true };
            var subSt = new GUIStyle(sSmall) { alignment = TextAnchor.LowerCenter, fontSize = 11 };
            for (int i = 0; i < cards.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(i + cols, cards.Count); k++)
                {
                    var c = cards[k];
                    var r = GUILayoutUtility.GetRect(cw, ch, W(cw), Hh(ch));
                    bool hover = r.Contains(Event.current.mousePosition);
                    if (Event.current.type == EventType.Repaint)
                    {
                        Fill(r, c.on ? CardOn : hover ? CardHover : CardBg);
                        var pr = new Rect(r.center.x - 26, r.y + 6, 52, 40);
                        Fill(pr, c.on ? new Color(1f, 0.86f, 0.72f) : new Color(0.95f, 0.92f, 0.87f));
                        Pict(pr, c.pict);
                    }
                    nameSt.normal.textColor = c.on ? Color.white : SimsInk;
                    subSt.normal.textColor = c.on ? Color.white : new Color(0.25f, 0.45f, 0.2f);
                    GUI.Label(new Rect(r.x + 2, r.y + 49, r.width - 4, 30), c.name, nameSt);
                    if (!string.IsNullOrEmpty(c.sub)) GUI.Label(new Rect(r.x, r.yMax - 17, r.width, 16), c.sub, subSt);
                    if (hover && !string.IsNullOrEmpty(c.tip)) tip = c.name + "\n" + c.tip;
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none) && c.click != null) { var cl = c.click; Defer(cl); }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
        }

        void Section(string title)
        {
            GUILayout.Space(4);
            ColorLabel(title, sBig, new Color(0.55f, 0.35f, 0.2f));
        }

        // ---------- пиктограммы из прямоугольников ----------

        void Pict(Rect r, string k)
        {
            float x = r.x, y = r.y, w = r.width, h = r.height;
            Rect R(float a, float b, float c, float d) => new Rect(x + a * w, y + b * h, c * w, d * h);
            void Wall() { Fill(R(0.08f, 0.15f, 0.84f, 0.75f), PWall); for (int i = 0; i < 4; i++) Fill(R(0.08f, 0.15f + i * 0.19f, 0.84f, 0.02f), PWallDark); }
            void Pickets(bool gapMid) { Fill(R(0.05f, 0.5f, 0.9f, 0.06f), PWood); for (int i = 0; i < 6; i++) { if (gapMid && (i == 2 || i == 3)) continue; Fill(R(0.08f + i * 0.15f, 0.3f, 0.07f, 0.6f), PWood); } }
            void Mound(float sign) { for (int i = 0; i < 4; i++) { float wd = 0.85f - i * 0.18f; Fill(R(0.5f - wd / 2, sign > 0 ? 0.8f - i * 0.14f : 0.35f + i * 0.14f, wd, 0.14f), i == 3 ? PGreen : PEarth); } }
            void Glyph(string g, Color c) { var st = new GUIStyle(sBold) { fontSize = 22, alignment = TextAnchor.MiddleCenter }; st.normal.textColor = c; GUI.Label(r, g, st); }
            switch (k)
            {
                case "wall": Wall(); break;
                case "erase": Wall(); Glyph("✕", PRed); break;
                case "door": Wall(); Fill(R(0.38f, 0.38f, 0.24f, 0.52f), PWood); Fill(R(0.55f, 0.62f, 0.04f, 0.06f), new Color(0.95f, 0.8f, 0.3f)); break;
                case "window": Wall(); Fill(R(0.3f, 0.3f, 0.4f, 0.32f), PWood); Fill(R(0.33f, 0.34f, 0.34f, 0.24f), PGlass); Fill(R(0.49f, 0.34f, 0.02f, 0.24f), PWood); break;
                case "arch": Wall(); Fill(R(0.36f, 0.4f, 0.28f, 0.5f), new Color(0.95f, 0.92f, 0.87f)); Fill(R(0.4f, 0.33f, 0.2f, 0.07f), new Color(0.95f, 0.92f, 0.87f)); break;
                case "fence": Pickets(false); break;
                case "gate": Pickets(true); Fill(R(0.38f, 0.42f, 0.24f, 0.4f), new Color(0.75f, 0.55f, 0.35f)); break;
                case "railing": Fill(R(0.05f, 0.3f, 0.9f, 0.07f), PWallDark); for (int i = 0; i < 3; i++) Fill(R(0.1f + i * 0.37f, 0.3f, 0.1f, 0.6f), PWallDark); for (int i = 0; i < 8; i++) Fill(R(0.08f + i * 0.11f, 0.42f, 0.03f, 0.45f), PWall); break;
                case "railgap": Fill(R(0.05f, 0.3f, 0.3f, 0.07f), PWallDark); Fill(R(0.65f, 0.3f, 0.3f, 0.07f), PWallDark); Fill(R(0.05f, 0.3f, 0.08f, 0.6f), PWallDark); Fill(R(0.87f, 0.3f, 0.08f, 0.6f), PWallDark); for (int i = 0; i < 2; i++) { Fill(R(0.2f + i * 0.08f, 0.42f, 0.03f, 0.45f), PWall); Fill(R(0.7f + i * 0.08f, 0.42f, 0.03f, 0.45f), PWall); } break;
                case "raise": Mound(1); Glyph("▲", new Color(0.3f, 0.55f, 0.3f)); break;
                case "lower": Mound(-1); Glyph("▼", new Color(0.55f, 0.35f, 0.2f)); break;
                case "flatten": Fill(R(0.05f, 0.55f, 0.9f, 0.35f), PEarth); Fill(R(0.05f, 0.5f, 0.9f, 0.08f), PGreen); break;
                case "smooth": for (int i = 0; i < 9; i++) { float hh = 0.2f + Mathf.Sin(i * 0.7f) * 0.12f + 0.12f; Fill(R(0.05f + i * 0.1f, 0.9f - hh - 0.25f, 0.1f, hh + 0.25f), i % 2 == 0 ? PGreen : new Color(0.5f, 0.75f, 0.4f)); } break;
                case "road": Fill(R(0f, 0.25f, 1f, 0.5f), PStone); for (int i = 0; i < 4; i++) Fill(R(0.05f + i * 0.25f, 0.48f, 0.12f, 0.04f), Color.white * 0.92f); break;
                case "path": Fill(R(0f, 0.3f, 1f, 0.4f), PGreen); for (int i = 0; i < 10; i++) Fill(R(0.04f + (i * 0.37f % 0.9f), 0.35f + (i % 3) * 0.1f, 0.08f, 0.07f), new Color(0.85f, 0.78f, 0.62f)); break;
                case "stones": case "stonesx": Fill(R(0f, 0f, 1f, 1f), PGreen); for (int i = 0; i < 3; i++) Fill(k == "stones" ? R(0.35f, 0.08f + i * 0.31f, 0.3f, 0.24f) : R(0.06f + i * 0.31f, 0.35f, 0.24f, 0.3f), PStone); break;
                case "under": Fill(R(0f, 0.55f, 1f, 0.45f), PDarkGreen); for (int i = 0; i < 6; i++) Fill(R(0.06f + i * 0.15f, 0.3f + (i % 2) * 0.1f, 0.08f, 0.3f), i % 2 == 0 ? new Color(0.35f, 0.55f, 0.3f) : new Color(0.55f, 0.65f, 0.35f)); break;
                case "lawn": Fill(R(0f, 0.45f, 1f, 0.55f), PGreen); for (int i = 0; i < 12; i++) Fill(R(0.04f + i * 0.08f, 0.33f + (i % 3) * 0.04f, 0.03f, 0.14f), new Color(0.5f, 0.78f, 0.4f)); break;
                case "pond": Fill(R(0.05f, 0.3f, 0.9f, 0.5f), PEarth); Fill(R(0.12f, 0.36f, 0.76f, 0.38f), PWater); Fill(R(0.25f, 0.45f, 0.2f, 0.04f), Color.white * 0.9f); break;
                case "fill": Fill(R(0.05f, 0.3f, 0.9f, 0.5f), PEarth); Fill(R(0.3f, 0.45f, 0.4f, 0.15f), new Color(0.72f, 0.58f, 0.4f)); break;
                case "flowers":
                    Fill(R(0f, 0.6f, 1f, 0.4f), PGreen);
                    Color[] fc = { PRed, new Color(0.98f, 0.82f, 0.3f), new Color(0.95f, 0.6f, 0.8f), new Color(0.6f, 0.55f, 0.95f) };
                    for (int i = 0; i < 7; i++) { Fill(R(0.06f + i * 0.13f, 0.42f + (i % 2) * 0.1f, 0.03f, 0.2f), PDarkGreen); Fill(R(0.03f + i * 0.13f, 0.34f + (i % 2) * 0.1f, 0.09f, 0.1f), fc[i % 4]); }
                    break;
                case "tree": Fill(R(0.44f, 0.55f, 0.12f, 0.4f), PWood); Fill(R(0.2f, 0.12f, 0.6f, 0.48f), PGreen); Fill(R(0.3f, 0.06f, 0.4f, 0.1f), PGreen); break;
                case "pine": Fill(R(0.45f, 0.75f, 0.1f, 0.2f), PWood); for (int i = 0; i < 4; i++) { float wd = 0.75f - i * 0.17f; Fill(R(0.5f - wd / 2, 0.62f - i * 0.17f, wd, 0.17f), PDarkGreen); } break;
                case "remove": Fill(R(0.44f, 0.55f, 0.12f, 0.4f), PWood); Fill(R(0.2f, 0.12f, 0.6f, 0.48f), new Color(0.55f, 0.7f, 0.5f)); Glyph("✕", PRed); break;
                case "spray": for (int i = 0; i < 14; i++) { float a = i * 2.4f, d = 0.1f + (i % 4) * 0.08f; Fill(R(0.47f + Mathf.Cos(a) * d * 1.2f, 0.47f + Mathf.Sin(a) * d, 0.06f, 0.08f), PGreen); } break;
                // крыши: силуэт
                case "roof0": for (int i = 0; i < 5; i++) { float wd = 0.9f - i * 0.18f; Fill(R(0.5f - wd / 2, 0.7f - i * 0.13f, wd, 0.13f), PRed); } Fill(R(0.15f, 0.83f, 0.7f, 0.12f), PWall); break;
                case "roof1": for (int i = 0; i < 3; i++) { float wd = 0.9f - i * 0.2f; Fill(R(0.5f - wd / 2, 0.66f - i * 0.15f, wd, 0.15f), PRed); } Fill(R(0.15f, 0.83f, 0.7f, 0.12f), PWall); break;
                case "roof2": for (int i = 0; i < 5; i++) Fill(R(0.05f, 0.7f - i * 0.12f, 0.9f - i * 0.17f, 0.12f), PRed); Fill(R(0.15f, 0.83f, 0.7f, 0.12f), PWall); break;
                case "roof3": Fill(R(0.05f, 0.62f, 0.9f, 0.1f), PRed); Fill(R(0.15f, 0.72f, 0.7f, 0.23f), PWall); break;
            }
        }

        // ---------- стены ----------

        void WallCard(List<ToolCardDef> l, string name, string sub, string pict, Game.WallTool t, string hint) =>
            l.Add(new ToolCardDef { name = name, sub = sub, pict = pict, tip = hint, on = G.wallTool == t, click = () => G.SetWallTool(G.wallTool == t ? Game.WallTool.None : t) });

        string PerM(int p) => G.freeBuild ? "" : p + " мон./м";
        string Each(int p) => G.freeBuild ? "" : p + " мон.";

        void WallTools()
        {
            float wdt = buildPanelInner;
            Section("Стены и проёмы");
            var a = new List<ToolCardDef>();
            WallCard(a, "Стена", PerM(Game.WallPrice), "wall", Game.WallTool.Wall, "Зажми мышь на углу сетки и протяни. Замкнутые стенами места сами становятся комнатами.");
            WallCard(a, "Снести", "Возврат цены", "erase", Game.WallTool.Erase, "Протяни вдоль стен, которые нужно убрать.");
            WallCard(a, "Дверь", Each(Game.DoorPrice), "door", Game.WallTool.Door, "Кликни по стене или по стыку двух секций.");
            WallCard(a, "Окно", Each(Game.WindowPrice), "window", Game.WallTool.Window, "Кликни по стене или по стыку двух секций.");
            WallCard(a, "Проём", Each(Game.ArchPrice), "arch", Game.WallTool.Arch, "Проход с косяком. Кликни по стене или по стыку двух секций.");
            ToolCards(a, wdt);
            Section("Двор и веранды");
            var b = new List<ToolCardDef>();
            WallCard(b, "Забор", PerM(Game.FencePrice), "fence", Game.WallTool.Fence, "Во дворе, на первом этаже. Тяни по сетке.");
            WallCard(b, "Калитка", Each(Game.GatePrice), "gate", Game.WallTool.Gate, "Кликни по забору.");
            WallCard(b, "Перила", PerM(Game.RailingPrice), "railing", Game.WallTool.Railing, "Веранды, беседки, на втором этаже — балконы. Место за перилами — тоже комната, с полом и крышей.");
            WallCard(b, "Вход в перилах", Each(Game.RailingGapPrice), "railgap", Game.WallTool.RailingGap, "Кликни по перилам, чтобы сделать проход.");
            ToolCards(b, wdt);
            string hint = G.wallTool == Game.WallTool.Wall ? "Зажми мышь на углу сетки и протяни стену. ПКМ — отменить."
                : G.wallTool == Game.WallTool.Sledgehammer ? "ЛКМ — убрать подсвеченную стену, мебель или проём."
                : G.wallTool == Game.WallTool.Erase ? "Протяни вдоль стен, которые нужно снести."
                : G.wallTool == Game.WallTool.MoveRoom ? "Зажми ЛКМ на полу комнаты, перетащи и отпусти. ПКМ — отменить."
                : G.wallTool == Game.WallTool.ResizeRoom ? "Зажми ЛКМ у стены прямоугольной комнаты и потяни поперёк стены."
                : G.wallTool == Game.WallTool.None ? "Page Up / Page Down — этажи. Home / End — стены."
                : G.wallTool == Game.WallTool.Door || G.wallTool == Game.WallTool.Window || G.wallTool == Game.WallTool.Arch ? "Кликни по середине секции стены или по стыку двух секций."
                : "Кликни по стене или забору, чтобы поставить. Esc — выйти из инструмента.";
            ColorLabel(G.RoomToolHint ?? hint, sSmall, new Color(0.35f, 0.4f, 0.5f));
            Section("Планировка");
            GUILayout.BeginHorizontal();
            WallTool("Перенести комнату", Game.WallTool.MoveRoom, "Перетащи комнату вместе со стенами и мебелью.");
            WallTool("Растянуть стену", Game.WallTool.ResizeRoom, "Потяни сторону прямоугольной комнаты наружу или внутрь.");
            GUILayout.EndHorizontal();
            RoofSettings();
            var rooms = G.house.rooms.FindAll(r => r.lot == House.PlayerLotId);
            if (rooms.Count > 0)
            {
                Section("Комнаты");
                var parts = new List<string>();
                foreach (var r in rooms) parts.Add($"{r.name} · {r.cells.Count} м²" + (r.level > 0 ? " (2 эт.)" : ""));
                GUILayout.Label(string.Join("   ", parts), sWrap);
                GUILayout.Label("Пол и обои красятся в «Создании стиля»: у стены каждая сторона — отдельно.", sSmall);
            }
        }

        // ---------- крыши ----------

        void RoofSettings()
        {
            var rooms = RoofRooms();
            if (rooms.Count == 0) return;
            Section("Крыши");
            GUILayout.Label("Выбери комнату или просто нажми на крышу дома.", sSmall);
            roofRoom = Mathf.Clamp(roofRoom, 0, rooms.Count - 1);
            for (int row = 0; row < rooms.Count; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + 3, rooms.Count); i++)
                {
                    int k = i;
                    if (Btn(rooms[i].name + (rooms[i].level > 0 ? " ²" : ""), roofRoom == i, W((buildPanelInner - 12) / 3))) Defer(() => roofRoom = k);
                }
                GUILayout.EndHorizontal();
            }
            var r = rooms[roofRoom];
            void Apply(System.Action change) => Defer(() => { change(); G.house.showRoof = true; G.house.RebuildRoofs(); });
            var cards = new List<ToolCardDef>();
            int[] order = { 0, 1, 2, 3 };
            foreach (int t in order)
            {
                int k = t;
                cards.Add(new ToolCardDef { name = House.RoofTypeNames[t], pict = "roof" + t, on = r.roofType == t, click = () => Apply(() => { r.roofType = k; r.roofDir = 0; }) });
            }
            ToolCards(cards, buildPanelInner);
            if (r.roofType == (int)House.RoofType.Gable)
            {
                GUILayout.BeginHorizontal();
                string[] d = { "Конёк сам", "Вдоль X", "Вдоль Z" };
                for (int i = 0; i < 3; i++) { int k = i; if (Btn(d[i], r.roofDir == i, W((buildPanelInner - 12) / 3))) Apply(() => r.roofDir = k); }
                GUILayout.EndHorizontal();
            }
            else if (r.roofType == (int)House.RoofType.Shed)
            {
                GUILayout.Label("Выше всего — с какой стороны:", sSmall);
                GUILayout.BeginHorizontal();
                string[] d = { "Север", "Восток", "Юг", "Запад" };
                for (int i = 0; i < 4; i++) { int k = i; if (Btn(d[i], r.roofDir == i, W((buildPanelInner - 18) / 4))) Apply(() => r.roofDir = k); }
                GUILayout.EndHorizontal();
            }
            if (r.roofType != (int)House.RoofType.Flat)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Наклон", sLabel, W(70));
                float np = GUILayout.HorizontalSlider(r.roofPitch, 0.2f, 1.4f, W(buildPanelInner - 130));
                GUILayout.Label(Mathf.RoundToInt(Mathf.Atan(r.roofPitch) * Mathf.Rad2Deg) + "°", sBold, W(44));
                GUILayout.EndHorizontal();
                if (Mathf.Abs(np - r.roofPitch) > 0.04f) { float v = Mathf.Round(np * 10f) / 10f; Apply(() => r.roofPitch = v); }
            }
            GUILayout.Label("Цвет черепицы — в «Создании стиля».", sSmall);
        }

        // ---------- ландшафт ----------

        void LandCard(List<ToolCardDef> l, string name, string sub, string pict, Game.LandTool t, string hint) =>
            l.Add(new ToolCardDef { name = name, sub = sub, pict = pict, tip = hint, on = G.landTool == t, click = () => G.SetLandTool(G.landTool == t ? Game.LandTool.None : t) });

        void LandTools()
        {
            float wdt = buildPanelInner;
            buildScroll = GUILayout.BeginScrollView(buildScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            Section(G.townBuilder ? "Рельеф" : "Рельеф вокруг участка");
            var a = new List<ToolCardDef>();
            LandCard(a, "Поднять", "", "raise", Game.LandTool.Raise, "Зажми мышь — холм растёт.");
            LandCard(a, "Опустить", "", "lower", Game.LandTool.Lower, "Зажми мышь — земля опускается.");
            LandCard(a, "Выровнять", "", "flatten", Game.LandTool.Flatten, "Выравнивает по высоте места, где нажал.");
            LandCard(a, "Сгладить", "", "smooth", Game.LandTool.Smooth, "Склоны оплывают, ступеньки и бугры разглаживаются.");
            ToolCards(a, wdt);
            Section("Дорожки");
            var paths = new List<ToolCardDef>();
            LandCard(paths, "Плитки ↕", "", "stones", Game.LandTool.Stones, "Каменные плитки, как у входов в дома. Веди кистью с севера на юг.");
            LandCard(paths, "Плитки ↔", "", "stonesx", Game.LandTool.StonesX, "Каменные плитки, как у входов в дома. Веди кистью с запада на восток.");
            LandCard(paths, "Убрать плитки", "", "lawn", Game.LandTool.EraseStones, "Убирает уложенные плитки и возвращает исходное покрытие.");
            ToolCards(paths, wdt);
            if (G.townBuilder)
            {
                Section("Дороги и лес");
                var t = new List<ToolCardDef>();
                LandCard(t, "Дорога", "", "road", Game.LandTool.Road, "Веди мышью — мощёная улица.");
                LandCard(t, "Тропинка", "", "path", Game.LandTool.Path, "Веди мышью — тропинка из гравия.");
                LandCard(t, "Подлесок", "", "under", Game.LandTool.Undergrowth, "Хвоя, мох и папоротники, как в лесу.");
                LandCard(t, "Газон", "", "lawn", Game.LandTool.Lawn, "Стирает дороги, тропинки и подлесок.");
                t.Add(new ToolCardDef { name = G.spray ? "Спрей: вкл" : "Спрей: выкл", pict = "spray", on = G.spray, tip = "Кисть брызгает редкими точками, гуще к середине — мягкий переход.", click = () => G.spray = !G.spray });
                ToolCards(t, wdt);
            }
            Section("Вода");
            var w = new List<ToolCardDef>();
            LandCard(w, "Вырыть пруд", "", "pond", Game.LandTool.Pond, "Вода заполнит ямку сама. Можно и на участке.");
            LandCard(w, "Засыпать", "", "fill", Game.LandTool.Fill, "Убрать воду.");
            ToolCards(w, wdt);
            Section("Сад");
            var g = new List<ToolCardDef>();
            LandCard(g, "Цветы", Each(Game.FlowerPrice), "flowers", Game.LandTool.Flowers, "Зажми мышь и веди — получится клумба.");
            LandCard(g, "Дерево", Each(Game.TreePrice), "tree", Game.LandTool.Tree, "Клик — посадить.");
            LandCard(g, "Ёлка", Each(Game.TreePrice), "pine", Game.LandTool.Pine, "Клик — посадить.");
            LandCard(g, "Убрать растения", "", "remove", Game.LandTool.Remove, "Убирает цветы и деревья под кистью.");
            ToolCards(g, wdt);
            Section("Кисть");
            GUILayout.BeginHorizontal();
            float[] sizes = { 0.8f, 1.5f, 2.6f };
            string[] names = { "Малая", "Средняя", "Большая" };
            float cw = (wdt - 12) / 3;
            for (int i = 0; i < 3; i++)
            {
                float sz = sizes[i];
                bool on = Mathf.Approximately(G.brushRadius, sz);
                var r = GUILayoutUtility.GetRect(cw, 62, W(cw), Hh(62));
                bool hover = r.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint)
                {
                    Fill(r, on ? CardOn : hover ? CardHover : CardBg);
                    float d = 10 + i * 9;
                    var old = GUI.color; GUI.color = on ? Color.white : new Color(0.55f, 0.45f, 0.35f);
                    GUI.DrawTexture(new Rect(r.center.x - d / 2, r.y + 22 - d / 2 + 4, d, d), texDot);
                    GUI.color = old;
                }
                var ns = new GUIStyle(sSmall) { alignment = TextAnchor.LowerCenter, fontSize = 12 };
                ns.normal.textColor = on ? Color.white : SimsInk;
                GUI.Label(new Rect(r.x, r.y, r.width, r.height - 4), names[i], ns);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Defer(() => G.brushRadius = sz);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            ColorLabel(G.townBuilder ? "Кистью можно сажать цветы и деревья: большой — клумбу или рощицу." :
                "Участок остаётся ровным — на нём строят; холмы и долины — вокруг. Esc — выйти из инструмента.", sSmall, new Color(0.35f, 0.4f, 0.5f));
            GUILayout.EndScrollView();
        }

        float buildPanelInner = 340f;
    }
}
