using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Весь интерфейс на IMGUI: без префабов и ассетов, масштабируется под экран.</summary>
    public partial class GameUI : MonoBehaviour
    {
        public enum StyleTool { Select, Pipette, Brush }
        public StyleTool tool = StyleTool.Select;

        public enum MenuScreen { None, Title, Pause, Settings }
        public MenuScreen screen = MenuScreen.Title;
        bool confirmNew;
        string menuNote;
        float menuNoteTime;

        Game G => Game.I;
        float scale = 1f, vw = 1600f, vh = 900f;
        readonly List<Rect> rects = new List<Rect>();
        System.Action deferred;
        string tip;
        public bool Typing { get; private set; }

        // всплывающее меню действий
        public bool menuOpen;
        Rect menuRect;
        string menuTitle;
        List<(string label, System.Action act)> menuItems;

        public int foxTab;
        public int outfitTab;
        int buildCat;
        internal Vector2 foxScroll;
        Vector2 buildScroll, styleScroll, createScroll;

        // редактор цвета
        int colorSlot;
        float hue, sat, val = 1f;
        Styleable syncTarget;
        int syncPart = -1, syncSlot = -1;
        string hexText = "";
        public readonly List<StyleData> housePalette = new List<StyleData>();

        static readonly string[] StandardPalette =
        {
            "#F3E5C8", "#E2C08D", "#C8A27A", "#A0703C", "#7A5230", "#4E3422",
            "#FFFFFF", "#EDEAE4", "#C9C5BE", "#8E8A84", "#4A4744", "#22201F",
            "#F2C14E", "#E8742A", "#C65B3A", "#B33A3A", "#E46A8C", "#8C3B5E",
            "#9DB7C9", "#4F7CAC", "#2E5E8C", "#7FAE55", "#4E8A3E", "#2F5D50",
        };
        Color[] stdColors;

        static readonly Color[] TierColors =
        {
            new Color(0.33f, 0.47f, 0.8f), new Color(0.52f, 0.68f, 0.9f), new Color(0.55f, 0.72f, 0.45f),
            new Color(0.95f, 0.62f, 0.28f), new Color(0.86f, 0.3f, 0.24f),
        };

        static readonly Color Ink = new Color(0.17f, 0.24f, 0.31f);
        static readonly Color Accent = new Color(0.13f, 0.52f, 0.76f);

        Texture2D wheel, texPanel, texBtn, texBtnHover, texBtnOn, texBubble;
        GUIStyle sPanel, sBtn, sBtnOn, sLabel, sBold, sWrap, sSmall, sTitle, sBig, sBubble, sName, sTip, sField, sTier, sHelp;
        bool ready;

        void Start()
        {
            housePalette.Add(StyleData.Make("Доски", "#A0703C"));
            housePalette.Add(StyleData.Make("Ткань", "#EDEAE4"));
            housePalette.Add(StyleData.Make("Вельвет", "#2F5D50"));
            housePalette.Add(StyleData.Make("Клетка", "#C65B3A", "#F3E5C8"));
            stdColors = new Color[StandardPalette.Length];
            for (int i = 0; i < StandardPalette.Length; i++) stdColors[i] = StyleData.Hex(StandardPalette[i]);
        }

        // ================= служебное =================

        Vector2 MouseGui()
        {
            var m = Input.mousePosition;
            return new Vector2(m.x / scale, (Screen.height - m.y) / scale);
        }

        public bool MouseOverUI
        {
            get
            {
                if (screen != MenuScreen.None) return true;
                var p = MouseGui();
                if (menuOpen && menuRect.Contains(p)) return true;
                foreach (var r in rects) if (r.Contains(p)) return true;
                return false;
            }
        }

        public void OpenMenu(Vector3 screenPos, string title, List<(string, System.Action)> items, float width = 250)
        {
            menuTitle = title;
            menuItems = items;
            pieItems = null;
            float w = width, h = 58 + items.Count * 34;
            float x = screenPos.x / scale + 10, y = (Screen.height - screenPos.y) / scale - 10;
            x = Mathf.Min(x, vw - w - 10);
            y = Mathf.Clamp(y, 70, vh - h - 10);
            menuRect = new Rect(x, y, w, h);
            menuOpen = true;
        }

        public void CloseMenu() { menuOpen = false; pieItems = null; pieRoot = null; }

        void Defer(System.Action a) => deferred += a;

        Rect Reg(Rect r)
        {
            if (Event.current.type == EventType.Layout) rects.Add(r);
            return r;
        }

        readonly Stack<Rect> areaStack = new Stack<Rect>();

        void Begin(Rect r)
        {
            GUILayout.BeginArea(Reg(r), sPanel);
            areaStack.Push(r);
            // GUI.tooltip в IMGUI «залипает» между кадрами — сбрасываем его в начале каждой панели
            if (Event.current.type == EventType.Repaint) GUI.tooltip = null;
        }

        void End()
        {
            var r = areaStack.Count > 0 ? areaStack.Pop() : Rect.zero;
            CaptureTip(r);
            GUILayout.EndArea();
        }

        void CaptureTip(Rect area)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(GUI.tooltip)) return;
            if (area.Contains(MouseGui())) tip = GUI.tooltip;
            GUI.tooltip = null;
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static GUILayoutOption W(float w) => GUILayout.Width(w);
        static GUILayoutOption Hh(float h) => GUILayout.Height(h);

        static void ColorLabel(string text, GUIStyle s, Color c, params GUILayoutOption[] o)
        {
            var old = s.normal.textColor;
            s.normal.textColor = c;
            GUILayout.Label(text, s, o);
            s.normal.textColor = old;
        }

        bool Btn(string text, bool on = false, params GUILayoutOption[] opt) => Clicked(GUILayout.Button(text, on ? sBtnOn : sBtn, opt));
        bool Btn(GUIContent c, bool on = false, params GUILayoutOption[] opt) => Clicked(GUILayout.Button(c, on ? sBtnOn : sBtn, opt));
        bool Clicked(bool b) { if (b && G != null && G.sound != null) G.sound.Click(); return b; }

        // ================= стили =================

        static Texture2D Rounded(Color fill, Color border, int r)
        {
            int s = r * 2 + 4;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Mathf.Clamp(fx, r, s - r), cy = Mathf.Clamp(fy, r, s - r);
                    float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    float b = Mathf.Clamp01(d - (r - 2f));
                    var c = Color.Lerp(fill, border, b);
                    c.a *= a;
                    px[y * s + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        void EnsureStyles()
        {
            if (ready) return;
            ready = true;
            // в духе Sims 4: белые скруглённые панели, голубые акценты
            texPanel = Rounded(new Color(1f, 1f, 1f, 0.95f), new Color(0.78f, 0.86f, 0.92f, 1f), 14);
            texBtn = Rounded(new Color(0.97f, 0.98f, 1f), new Color(0.8f, 0.87f, 0.93f), 12);
            texBtnHover = Rounded(new Color(0.88f, 0.95f, 1f), new Color(0.45f, 0.72f, 0.9f), 12);
            texBtnOn = Rounded(new Color(0.17f, 0.6f, 0.84f), new Color(0.1f, 0.45f, 0.68f), 12);
            texBubble = Rounded(new Color(1f, 1f, 1f, 0.97f), new Color(0.6f, 0.72f, 0.82f), 12);

            sPanel = new GUIStyle { border = new RectOffset(16, 16, 16, 16), padding = new RectOffset(14, 14, 12, 12) };
            sPanel.normal.background = texPanel;

            sBtn = new GUIStyle(GUI.skin.button)
            {
                border = new RectOffset(9, 9, 9, 9),
                padding = new RectOffset(10, 10, 6, 6),
                margin = new RectOffset(3, 3, 3, 3),
                fontSize = 14,
                wordWrap = false,
            };
            sBtn.normal.background = texBtn; sBtn.normal.textColor = Ink;
            sBtn.hover.background = texBtnHover; sBtn.hover.textColor = Ink;
            sBtn.active.background = texBtnOn; sBtn.active.textColor = Color.white;
            sBtn.focused.background = texBtn; sBtn.focused.textColor = Ink;

            sBtnOn = new GUIStyle(sBtn);
            sBtnOn.normal.background = texBtnOn; sBtnOn.normal.textColor = Color.white;
            sBtnOn.hover.background = texBtnOn; sBtnOn.hover.textColor = Color.white;
            sBtnOn.focused.background = texBtnOn; sBtnOn.focused.textColor = Color.white;

            sLabel = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false };
            sLabel.normal.textColor = Ink;
            sBold = new GUIStyle(sLabel) { fontStyle = FontStyle.Bold };
            sWrap = new GUIStyle(sLabel) { wordWrap = true, fontSize = 13 };
            sSmall = new GUIStyle(sLabel) { fontSize = 12, wordWrap = true };
            sSmall.normal.textColor = new Color(0.38f, 0.47f, 0.55f);
            sTitle = new GUIStyle(sLabel) { fontSize = 20, fontStyle = FontStyle.Bold };
            sBig = new GUIStyle(sLabel) { fontSize = 16, fontStyle = FontStyle.Bold };
            sBig.normal.textColor = Accent;
            sTier = new GUIStyle(sLabel) { fontSize = 15, fontStyle = FontStyle.Bold };
            sBubble = new GUIStyle { border = new RectOffset(11, 11, 11, 11), padding = new RectOffset(11, 11, 6, 6), fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            sBubble.normal.background = texBubble; sBubble.normal.textColor = Ink;
            sName = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sName.normal.textColor = Color.white;
            sTip = new GUIStyle(sBubble) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = 13 };
            sField = new GUIStyle(GUI.skin.textField) { fontSize = 15, padding = new RectOffset(8, 8, 5, 5), border = new RectOffset(9, 9, 9, 9) };
            var texField = Rounded(Color.white, new Color(0.7f, 0.8f, 0.88f), 8);
            var texFieldOn = Rounded(Color.white, Accent, 8);
            sField.normal.background = texField; sField.normal.textColor = Ink;
            sField.hover.background = texField; sField.hover.textColor = Ink;
            sField.focused.background = texFieldOn; sField.focused.textColor = Ink;
            sField.active.background = texFieldOn; sField.active.textColor = Ink;
            sHelp = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight };
            sHelp.normal.textColor = Color.white;

            const int ws = 128;
            wheel = new Texture2D(ws, ws, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[ws * ws];
            for (int y = 0; y < ws; y++)
                for (int x = 0; x < ws; x++)
                {
                    float dx = (x + 0.5f) / ws * 2 - 1, dy = (y + 0.5f) / ws * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float h = (Mathf.Atan2(dy, dx) / (2 * Mathf.PI) + 1f) % 1f;
                    var c = Color.HSVToRGB(h, Mathf.Clamp01(r), 1f);
                    c.a = Mathf.Clamp01((1f - r) * ws * 0.5f);
                    px[y * ws + x] = c;
                }
            wheel.SetPixels(px);
            wheel.Apply();
        }

        // ================= OnGUI =================

        void OnGUI()
        {
            if (G == null) return;
            EnsureStyles();
            scale = Mathf.Clamp(Screen.height / 900f, 0.75f, 3f);
            vw = Screen.width / scale;
            vh = Screen.height / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            var e = Event.current;
            if (e.type == EventType.Layout) rects.Clear();
            if (e.type == EventType.Repaint) tip = null;
            // любой клик снимает фокус с полей ввода (поле под мышью тут же заберёт его обратно) — иначе фокус
            // «залипал» в имени или поиске, и не работали Enter (чат), Ctrl+Z и горячие клавиши
            if (e.type == EventType.MouseDown && !chatOpen) GUI.FocusControl(null);
            string focused = GUI.GetNameOfFocusedControl();
            Typing = !string.IsNullOrEmpty(focused) || chatOpen; // в поле ввода — горячие клавиши не срабатывают
            // Enter или Esc в обычном поле — закончить ввод
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) && Typing && !chatOpen && focused != "chat")
            { GUI.FocusControl(null); Typing = false; e.Use(); }

            if (G.loading || G.house == null)
            {
                LoadingScreen();
            }
            else if (screen != MenuScreen.None)
            {
                ScreenMenu();
            }
            else if (Net.I != null && Net.I.NeedsPicker)
            {
                WorldLabels();
                NetPicker();
                NetBadge();
                Tooltip();
            }
            else if (G.mode == Mode.Town)
            {
                UpdatePortraits();
                TownScreen();
                ModeBar();
                Menu();
                Tooltip();
            }
            else
            {
                WorldLabels();
                UpdatePortraits();
                bool live = G.mode == Mode.Live;
                TimePanel(live && G.selected != null ? new Rect(10, vh - 256, 420, 66) : live ? new Rect(10, vh - 78, 420, 66) : new Rect((vw - 420) / 2, vh - 78, 420, 66));
                ModeBar();
                if (G.mode != Mode.Create) HouseholdStrip();
                switch (G.mode)
                {
                    case Mode.Live: PortraitCard(); InfoPanel(); Notifications(); KnockNotice(); break;
                    case Mode.Build: BuildPanel(); break;
                    case Mode.Style: StylePanel(); break;
                    case Mode.Create: CreatePanel(); break;
                }
                NetBadge();
                ChatBox();
                Menu();
                Tooltip();
            }

            if (deferred != null)
            {
                var d = deferred;
                deferred = null;
                d();
            }
        }

        // ---------- титульный экран и пауза ----------

        public void Note(string text)
        {
            menuNote = text;
            menuNoteTime = Time.unscaledTime;
        }

        void Quit()
        {
            // сохранение делает Game.OnApplicationQuit
#if UNITY_EDITOR
            if (G.started) SaveSystem.Save(G);
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- подписи над лисичками ----------

        readonly List<Rect> bubbleRects = new List<Rect>();
        readonly Dictionary<Fox, float> bubbleLift = new Dictionary<Fox, float>();

        /// <summary>Имена и фразы над лисичками: всегда (хоть через весь город), только видимые (не сквозь стены, крыши,
        /// вещи и холмы) или никогда.</summary>
        public enum LabelMode { Always = 0, Visible = 1, Never = 2 }
        public static readonly string[] LabelModeNames = { "всегда", "только видимых", "никогда" };
        LabelMode nameMode = (LabelMode)PlayerPrefs.GetInt("fox_names", 0), phraseMode = (LabelMode)PlayerPrefs.GetInt("fox_phrases", 0);
        public LabelMode NameMode { get => nameMode; set { nameMode = value; PlayerPrefs.SetInt("fox_names", (int)value); } }
        public LabelMode PhraseMode { get => phraseMode; set { phraseMode = value; PlayerPrefs.SetInt("fox_phrases", (int)value); } }

        // видно ли лисичку с камеры: считается в LateUpdate, а не в OnGUI (тот вызывается несколько раз за кадр)
        readonly Dictionary<Fox, bool> foxSeen = new Dictionary<Fox, bool>();
        readonly RaycastHit[] seenHits = new RaycastHit[32];
        float seenTimer;

        void UpdateFoxSeen()
        {
            if (nameMode != LabelMode.Visible && phraseMode != LabelMode.Visible) return;
            seenTimer -= Time.unscaledDeltaTime;
            if (seenTimer > 0) return;
            seenTimer = 0.1f;
            var cam = Camera.main;
            if (cam == null || G == null) return;
            var from = cam.transform.position;
            GatherOccluders(G.cam.target);
            foreach (var f in G.foxes)
            {
                if (f == null) continue;
                var p = f.transform.position;
                // видна хотя бы голова или туловище — подписываем
                foxSeen[f] = Clear(from, p + Vector3.up * (0.45f * f.size), p, f.size) || Clear(from, p + Vector3.up * (1.0f * f.size), p, f.size);
            }
        }

        bool Clear(Vector3 from, Vector3 to, Vector3 feet, float size)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            var dir = d / len;
            // земля и крыши — по коллайдерам
            int n = Physics.RaycastNonAlloc(from, dir, seenHits, len - 0.2f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = seenHits[i].collider;
                if (c == null || c.GetComponentInParent<Fox>() != null) continue; // сами лисички (и их одежда) не заслоняют
                var r = c.GetComponent<Renderer>();
                if (r != null && (!r.enabled || r.forceRenderingOff)) continue;
                return false;
            }
            // стены, мебель, деревья и постройки — по габаритам (коллайдеров у них нет)
            var ray = new Ray(from, dir);
            var body = new Bounds(feet + Vector3.up * (0.5f * size), new Vector3(0.5f, size + 0.2f, 0.5f));
            foreach (var b in occNow)
            {
                if (b.Intersects(body)) continue; // то, в чём лисичка сидит или стоит (кровать, диван, дверной проём)
                if (b.IntersectRay(ray, out float t) && t < len - 0.3f) return false;
            }
            return true;
        }

        // заслоняющие предметы: список рендереров обновляется раз в 1,5 с, их габариты и видимость — при каждой проверке
        readonly List<Renderer> occAll = new List<Renderer>();
        readonly List<Bounds> occNow = new List<Bounds>();
        readonly List<MeshRenderer> occTmp = new List<MeshRenderer>();
        float occRefresh;

        void GatherOccluders(Vector3 center)
        {
            occRefresh -= 0.1f;
            if (occRefresh <= 0)
            {
                occRefresh = 1.5f;
                occAll.Clear();
                foreach (var fu in G.furniture)
                    if (fu != null && !fu.isGhost) AddOcc(fu.transform);
                if (G.house != null) { AddOcc(G.house.WallsRoot); AddOcc(G.house.TreesRoot); AddOcc(G.house.propsRoot); }
            }
            occNow.Clear();
            const float R = 45f;
            foreach (var r in occAll)
            {
                if (r == null || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue; // спрятанные и опущенные стены
                var b = r.bounds;
                if (b.size.y < 0.45f) continue; // ковры, пол, опущенные стены — не заслоняют
                if (b.size.x * b.size.z > 400f) continue; // большие плоские вещи вроде улиц
                if (Mathf.Abs(b.center.x - center.x) > R + b.extents.x || Mathf.Abs(b.center.z - center.z) > R + b.extents.z) continue;
                b.Expand(-0.06f); // чуть меньше габаритов: краешек предмета не прячет лисичку
                occNow.Add(b);
            }
        }

        void AddOcc(Transform root)
        {
            if (root == null) return;
            root.GetComponentsInChildren(true, occTmp);
            foreach (var r in occTmp)
            {
                var m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name.Contains("Glass")) continue; // через стекло видно
                if (r.GetComponentInParent<Fox>() != null) continue;
                occAll.Add(r);
            }
        }

        public bool FoxSeen(Fox f) => !foxSeen.TryGetValue(f, out var s) || s;

        /// <summary>Для автотеста: режим подписей без записи в настройки игрока.</summary>
        public void TestLabels(LabelMode names, LabelMode phrases) { nameMode = names; phraseMode = phrases; seenTimer = 0; occRefresh = 0; }
        public int OccluderCount => occNow.Count;

        bool LabelOn(LabelMode m, Fox f) => m == LabelMode.Always || (m == LabelMode.Visible && FoxSeen(f));

        void WorldLabels()
        {
            var cam = Camera.main;
            if (cam == null) return;
            bubbleRects.Clear();
            var bubbles = new List<(Rect r, GUIContent c, Fox f)>();
            foreach (var f in G.foxes)
            {
                if (G.house.HiddenAt(f.Level, f.Cell)) continue;
                bool mine = Game.IsPlayer(f);
                string chatB = ChatBubbleOf(f);
                Color pc = Color.white; bool ctrl = false;
                bool other = Net.Active && Net.I.OtherPlayer(f, out pc, out ctrl, out _);
                // соседей подписываем, только когда они рядом (лисичку другого игрока и того, кто пишет в чат, — всегда)
                if (!mine && !(other && ctrl) && chatB == null && Vector3.Distance(f.transform.position, G.cam.target) > 22f) continue;
                var wp = f.transform.position + Vector3.up * (1.05f * f.size);
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z < 0) continue;
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
                if (LabelOn(nameMode, f))
                {
                    var nameRect = new Rect(x - 70, y - 4, 140, 20);
                    var old = GUI.color;
                    GUI.color = new Color(0, 0, 0, 0.6f);
                    GUI.Label(new Rect(nameRect.x + 1, nameRect.y + 1, nameRect.width, nameRect.height), f.foxName, sName);
                    // свои — белым (выбранная — золотом), семьи других игроков — их цветом, за кого они играют — ярко и со звёздочкой
                    GUI.color = f == G.selected ? new Color(1f, 0.85f, 0.55f) : mine ? Color.white
                        : other ? (ctrl ? pc : Color.Lerp(pc, Color.white, 0.45f)) : new Color(0.78f, 0.9f, 1f);
                    GUI.Label(nameRect, other && ctrl ? "★ " + f.foxName : f.foxName, sName);
                    GUI.color = old;
                }
                if (chatB == null && !LabelOn(phraseMode, f)) continue;

                string b = chatB ?? (f.bubbleTimer > 0 ? f.bubble : (f.sleeping ? "Хр-р-р…" : null));
                if (string.IsNullOrEmpty(b)) continue;
                var c = new GUIContent(BubbleText(b));
                var size = BubbleSize(c);
                bubbles.Add((new Rect(x - size.x / 2, y - size.y - 6, size.x, size.y), c, f));
            }
            // облачка, которые налезают друг на друга, расталкиваем вверх
            // порядок устойчивый (иначе два облачка на одной высоте каждый кадр меняются местами и прыгают),
            // а сдвиг вверх догоняется плавно
            bubbles.Sort((p, q) =>
            {
                int k = Mathf.RoundToInt(q.r.y / 12f).CompareTo(Mathf.RoundToInt(p.r.y / 12f));
                return k != 0 ? k : G.foxes.IndexOf(p.f).CompareTo(G.foxes.IndexOf(q.f));
            });
            bool repaint = Event.current.type == EventType.Repaint;
            foreach (var bb in bubbles)
            {
                var r = bb.r;
                float baseY = r.y;
                bool moved = true;
                int guard = 0;
                while (moved && guard++ < 20)
                {
                    moved = false;
                    foreach (var o in bubbleRects)
                        if (o.Overlaps(new Rect(r.x - 4, r.y - 3, r.width + 8, r.height + 6))) { r.y = o.y - r.height - 4; moved = true; }
                }
                float lift = baseY - r.y;
                bubbleLift.TryGetValue(bb.f, out float cur);
                if (repaint) { cur = Mathf.Lerp(cur, lift, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime)); bubbleLift[bb.f] = cur; }
                bubbleRects.Add(r);
                GUI.Label(new Rect(r.x, baseY - cur, r.width, r.height), bb.c, sBubble);
            }
        }

        /// <summary>Знаки препинания и короткое последнее слово не отрываются от предыдущего слова.</summary>
        static string BubbleText(string b)
        {
            b = b.Replace("...", "…");
            foreach (var p in new[] { " …", " !", " ?", " ,", " ." }) b = b.Replace(p, p.Substring(1));
            foreach (var p in new[] { " —", " :)", " :(" }) b = b.Replace(p, " " + p.Substring(1));
            int last = b.LastIndexOf(' ');
            if (last > 0 && b.Length - last <= 5) b = b.Substring(0, last) + " " + b.Substring(last + 1);
            return b;
        }

        /// <summary>
        /// Размер облачка: ширина с запасом (иначе последний символ при отрисовке переносится на новую строку,
        /// а облачко остаётся высотой в одну строку). Длинные фразы — в несколько строк, и облачко сужается,
        /// чтобы строки были примерно одинаковой длины.
        /// </summary>
        Vector2 BubbleSize(GUIContent c)
        {
            const float max = 240f;
            var one = sBubble.CalcSize(c);
            float w = Mathf.Ceil(one.x) + 6f;
            if (w <= max) return new Vector2(w, sBubble.CalcHeight(c, w));
            float h = sBubble.CalcHeight(c, max);
            float lo = max * 0.5f, hi = max;
            for (int i = 0; i < 7; i++)
            {
                float mid = (lo + hi) / 2f;
                if (sBubble.CalcHeight(c, mid) > h + 0.5f) lo = mid; else hi = mid;
            }
            w = Mathf.Ceil(hi) + 4f;
            return new Vector2(w, sBubble.CalcHeight(c, w) + 2f);
        }

        // ---------- верхняя панель ----------

        void TopBar()
        {
            Begin(new Rect(10, 10, vw - 20, 56));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Лисья Жизнь", sTitle, W(150));
            ModeBtn("Жизнь", Mode.Live, "F1");
            ModeBtn("Обустройство", Mode.Build, "F2");
            ModeBtn("Создание стиля", Mode.Style, "F3");
            ModeBtn("Лисички", Mode.Create, "F4");
            GUILayout.FlexibleSpace();
            ColorLabel($"{Mathf.RoundToInt(G.money):N0} мон.".Replace(",", " ").Replace("\u00A0", " "), sBold, G.money < 0 ? new Color(0.75f, 0.2f, 0.15f) : new Color(0.25f, 0.45f, 0.2f), W(110), Hh(32));
            GUILayout.Label(G.ClockText, sBold, W(175), Hh(32));
            GUILayout.Label(new GUIContent(G.weather.Label, "Погода меняется сама несколько раз в день"), sBold, W(78), Hh(32));
            string[] sp = { "||", ">", ">>", ">>>" };
            string[] spTip = { "Пауза (пробел)", "Обычная скорость (1)", "Быстро (2)", "Очень быстро (3)" };
            for (int i = 0; i < sp.Length; i++)
            {
                int k = i;
                if (Btn(new GUIContent(sp[i], spTip[i]), G.speedIndex == i, W(44))) Defer(() => G.SetSpeed(k));
            }
            GUILayout.Space(8);
            if (Btn(new GUIContent("Свобода воли — " + (G.autonomy ? "вкл" : "выкл"), "Лисички сами решают, чем заняться, в зависимости от характера"), G.autonomy))
                Defer(() => G.autonomy = !G.autonomy);
            for (int lv = 0; lv < House.Levels; lv++)
            {
                int k = lv;
                if (Btn(new GUIContent((lv + 1) + " эт.", "Показать этаж (PageUp / PageDown)"), G.house.viewLevel == lv, W(52))) Defer(() => G.SetViewLevel(k));
            }
            if (Btn(new GUIContent("Крыша", "Показать или спрятать крыши"), G.house.showRoof)) Defer(() => { G.house.showRoof = !G.house.showRoof; G.house.ApplyVisibility(); });
            if (Btn(new GUIContent("Стены — " + G.house.WallModeName, "Авто — стены со стороны камеры опускаются")))
                Defer(() => G.house.CycleWallMode());
            if (Btn(new GUIContent("Меню", "Сохранить, загрузить, настройки (Esc)"))) Defer(() => screen = MenuScreen.Pause);
            GUILayout.EndHorizontal();
            End();
        }

        void ModeBtn(string text, Mode m, string key)
        {
            if (Btn(new GUIContent(text, key), G.mode == m)) Defer(() => G.SetMode(m));
        }

        // ---------- лисички внизу ----------

        void Household()
        {
            var family = G.Household;
            int n = family.Count;
            float w = n * 126 + 26;
            Begin(new Rect((vw - w) / 2, vh - 92, w, 56));
            GUILayout.BeginHorizontal();
            foreach (var f in family)
            {
                var fox = f;
                if (Btn(new GUIContent("   " + f.foxName, "Настроение — " + Fox.MoodLabel(f.Mood).ToLower()), f == G.selected, W(120), Hh(34)))
                {
                    Defer(() =>
                    {
                        if (G.selected == fox) { G.cam.Focus(fox.transform.position, 10f); G.SetViewLevel(fox.Level); }
                        G.Select(fox);
                        if (G.mode == Mode.Create) G.cam.Focus(fox.transform.position, 6f);
                    });
                }
                if (Event.current.type == EventType.Repaint)
                {
                    var r = GUILayoutUtility.GetLastRect();
                    Fill(new Rect(r.x + 9, r.y + r.height / 2 - 5, 10, 10), Fox.MoodColor(f.Mood));
                }
            }
            GUILayout.EndHorizontal();
            End();
        }

        // ---------- панель выбранной лисички ----------

        static Color NeedColor(float t) => t > 0.5f
            ? Color.Lerp(new Color(0.95f, 0.78f, 0.25f), new Color(0.38f, 0.76f, 0.36f), (t - 0.5f) * 2f)
            : Color.Lerp(new Color(0.88f, 0.3f, 0.26f), new Color(0.95f, 0.78f, 0.25f), t * 2f);

        /// <summary>Состояния: слева физические (главное — здоровье), справа психические (главное — менталка).</summary>
        void StatesBlock(Fox f)
        {
            GUILayout.BeginHorizontal();
            for (int col = 0; col < 2; col++)
            {
                GUILayout.BeginVertical(W(200));
                ColorLabel(col == 0 ? "Тело" : "Душа", sBold, new Color(0.45f, 0.4f, 0.35f));
                for (int k = 0; k < 3; k++)
                {
                    int i = col * 3 + k;
                    float v = f.StateValue(i) / 100f;
                    bool main = k == 0;
                    var lr = GUILayoutUtility.GetRect(196, main ? 24 : 22, W(196));
                    var tipR = lr;
                    var st = main ? sBold : sLabel;
                    GUI.Label(new Rect(lr.x, lr.y, 100, lr.height), new GUIContent(Fox.StateNames[i], Fox.StateTips[i]), st);
                    if (tipR.Contains(Event.current.mousePosition)) tip = Fox.StateTips[i];
                    var br = new Rect(lr.x + 92, lr.y + (main ? 6 : 6), 100, main ? 12 : 10);
                    if (Event.current.type == EventType.Repaint)
                    {
                        Fill(br, new Color(0.3f, 0.2f, 0.1f, 0.13f));
                        var fr = br; fr.width *= Mathf.Clamp01(v);
                        Fill(fr, NeedColor(v));
                    }
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            var notes = new List<string>();
            if (f.Sick) notes.Add("болеет");
            if (f.Depressed) notes.Add("в депрессии");
            else if (f.mental < 30) notes.Add("грустит");
            if (f.needs[(int)Need.Social] < 25) notes.Add("скучает по общению");
            if (notes.Count > 0) GUILayout.Label("Сейчас: " + string.Join(", ", notes) + ".", sSmall);
            GUILayout.Space(6);
            GUILayout.Label("Что важно именно " + f.NameIn(NameCase.Dative), sBold);
            GUILayout.Label("Комфорт: " + f.ComfortHints(), sWrap);
            GUILayout.Label("Отдых: " + f.RestHints(), sWrap);
        }

        void Bar(string label, float t, Color c, float labelW = 100, float barW = 210)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, sLabel, W(labelW));
            var r = GUILayoutUtility.GetRect(barW, 18, W(barW), Hh(18));
            if (Event.current.type == EventType.Repaint)
            {
                r.y += 2; r.height = 14;
                Fill(r, new Color(0.3f, 0.2f, 0.1f, 0.13f));
                var f = r; f.width *= Mathf.Clamp01(t);
                Fill(f, c);
            }
            GUILayout.EndHorizontal();
        }

        void FoxPanel()
        {
            var f = G.selected;
            if (f == null) return;
            Begin(new Rect(10, vh - 100 - 470, 390, 470));
            GUILayout.BeginHorizontal();
            GUILayout.Label(f.foxName, sTitle);
            GUILayout.FlexibleSpace();
            float mood = f.Mood;
            ColorLabel("Настроение — " + Fox.MoodLabel(mood).ToLower(), sBold, Fox.MoodColor(mood) * 0.8f + Color.black * 0.2f);
            GUILayout.EndHorizontal();
            ColorLabel(f.Summary + "  ·  " + f.StageName + (f.Pregnant ? "  ·  ждёт лисят" : ""), sBold, Accent);

            GUILayout.BeginHorizontal();
            GUILayout.Label(f.ActivityText, sWrap, W(310));
            if (f.current != null && f.current.kind != ActKind.BeTalkedTo && Btn(new GUIContent("×", "Отменить"), false, W(30)))
            {
                var c = f.current;
                Defer(() => f.Cancel(c));
            }
            GUILayout.EndHorizontal();
            foreach (var q in f.queue)
            {
                var qq = q;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Затем — " + q.DisplayText(), sWrap, W(310));
                if (Btn("×", false, W(30))) Defer(() => f.Cancel(qq));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            string[] tabs = { "Нужды", "Эмоции", "Характер", "Навыки", f.IsChild ? "Школа" : "Работа", "Желания", "Друзья", "Семья", "Дневник" };
            for (int row = 0; row < tabs.Length; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < row + 3; i++)
                {
                    int k = i;
                    if (Btn(tabs[i], foxTab == i, W(113))) Defer(() => foxTab = k);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);

            foxScroll = GUILayout.BeginScrollView(foxScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            FoxTabContent(f);
            GUILayout.EndScrollView();
            End();
        }

        void FoxTabContent(Fox f)
        {
            switch (foxTab)
            {
                case 0:
                    StatesBlock(f);

                    break;
                case 1:
                    var list = new List<Moodlet>(f.moodlets);
                    list.Sort((a, b) => b.value.CompareTo(a.value));
                    if (list.Count == 0) GUILayout.Label("Ничего особенного не чувствует.", sWrap);
                    float mul = f.MoodletMul;
                    foreach (var m in list)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(m.name, Fit(sLabel, m.name, 360), W(360));
                        GUILayout.FlexibleSpace();
                        float v = m.value * mul;
                        ColorLabel((v >= 0 ? "+" : "") + Mathf.RoundToInt(v), sBold, v >= 0 ? new Color(0.2f, 0.55f, 0.2f) : new Color(0.75f, 0.2f, 0.15f), W(40));
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.Space(6);
                    GUILayout.Label($"Сила эмоций ×{mul:0.0} — зависит от невротизма ({PersonalityInfo.TierNameFor(4, f.pers.N, f.female).ToLower()}).", sSmall);
                    break;
                case 2:
                    for (int i = 0; i < 5; i++)
                    {
                        int v = f.pers.v[i];
                        GUILayout.Label(PersonalityInfo.ScaleNames[i], sSmall);
                        GUILayout.BeginHorizontal();
                        ColorLabel(PersonalityInfo.TierNameFor(i, v, f.female), sTier, TierColors[PersonalityInfo.TierIndex(v)] * 0.85f + Color.black * 0.15f);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(v + " / 10", sBold);
                        GUILayout.EndHorizontal();
                        GUILayout.Label(PersonalityInfo.TierDescription(i, v), sWrap);
                        GUILayout.Space(4);
                    }
                    GUILayout.Label("Вкусы", sBold);
                    GUILayout.Label($"Стихия — {Elements.Names[(int)f.Element].ToLower()}. Любит {Elements.Music[(int)f.Element]}, {Elements.Tv[(int)f.Element]}, {Elements.Books[(int)f.Element]}. Рисует {Elements.Paint[(int)f.Element]}.", sWrap);
                    GUILayout.Label($"Любимая погода — {Fox.WeatherName(f.LikedWeather)}, нелюбимая — {Fox.WeatherName(f.DislikedWeather)}." +
                        (f.likedSeason >= 0 ? $" Любимое время года — {Game.SeasonNames[f.likedSeason]}." : ""), sWrap);
                    if (f.interests.Count > 0)
                    {
                        GUILayout.Label("Увлечения", sBold);
                        var inames = new List<string>();
                        foreach (var x in f.interests) inames.Add(Interests.Name(x).ToLower());
                        GUILayout.Label(string.Join(", ", inames) + ". С теми, кто увлечён тем же, " + f.NameIn(NameCase.Dative) + " легче подружиться.", sWrap);
                    }
                    float att = f.HomeAttachment(out var why);
                    GUILayout.Label((att >= 2.5f ? "Дом стал родным" : att >= 1.2f ? "Дома уютно" : "Дом пока кажется чужим") + (why != null ? $" ({why})" : "") + ".", sWrap);
                    break;
                case 3: SkillsTab(f); break;
                case 4: WorkTab(f); break;
                case 5: WantsTab(f); break;
                case 7: FamilyTab(f); break;
                case 8: DiaryTab(f); break;
                case 6:
                    foreach (var o in G.foxes)
                    {
                        if (o == f || (o.home != f.home && !f.rel.ContainsKey(o))) continue;
                        float r = Social.Rel(f, o);
                        GUILayout.BeginHorizontal();
                        string role = Family.Role(f, o);
                        string where = o.home != f.home && role == null ? " · сосед" + (o.female ? "ка" : "") : "";
                        GUILayout.Label(o.foxName + (role != null ? $" ({role})" : "") + where, sBold, W(190));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(Social.RelLabel(r) + $" ({Mathf.RoundToInt(r)})", sLabel);
                        GUILayout.EndHorizontal();
                        Bar("Дружба", (r + 100f) / 200f, r >= 0 ? new Color(0.4f, 0.7f, 0.9f) : new Color(0.85f, 0.4f, 0.35f), 80, 260);
                        // ключ, который нам дала эта лисичка (семейный не пишем — он и так у всех жильцов)
                        var key = f.KeyFrom(o);
                        if (key != null && o.home != f.home)
                        {
                            var kl = G.house.LotById(key.lot);
                            bool trusted = key.kind == (int)KeyKind.Trusted;
                            string verb = trusted ? (o.female ? "Дала" : "Дал") + " постоянный ключ" : (o.female ? "Впустила" : "Впустил") + " в гости и оставил" + (o.female ? "а" : "") + " ключ";
                            ColorLabel(verb + (kl != null ? " (" + kl.name + ")" : ""), sSmall, trusted ? new Color(0.62f, 0.45f, 0.1f) : new Color(0.35f, 0.45f, 0.55f));
                        }
                        // романтика — только если между ними уже что-то было
                        if (Mathf.Abs(Social.Rom(f, o)) > 0.01f)
                        {
                            float rm = Social.Rom(f, o);
                            Bar("Романтика", (rm + 100f) / 200f, new Color(0.9f, 0.45f, 0.6f), 80, 260);
                            GUILayout.Label($"Чувства — {Social.RomLabel(rm)} · искра — {Family.ChemistryLabel(Family.Chemistry(f, o))}", sSmall);
                        }
                        GUILayout.Space(4);
                    }
                    break;
            }
        }

        // ---------- навыки, работа, желания, дневник ----------

        void SkillsTab(Fox f)
        {
            GUILayout.Label($"Скорость обучения ×{Skills.LearnMul(f.pers) * (f.HasPerk("learner") ? 1.3f : 1f):0.0} — зависит от открытости.", sSmall);
            for (int i = 0; i < Skills.Count; i++)
            {
                var s = (Skill)i;
                int lvl = f.SkillLevel(s);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent(Skills.Names[i], Skills.Hints[i]), sLabel, W(100));
                var r = GUILayoutUtility.GetRect(200, 18, W(200), Hh(18));
                if (Event.current.type == EventType.Repaint)
                {
                    r.y += 3; r.height = 12;
                    float seg = r.width / Skills.MaxLevel;
                    for (int k = 0; k < Skills.MaxLevel; k++)
                    {
                        var c = new Rect(r.x + k * seg + 1, r.y, seg - 2, r.height);
                        Fill(c, new Color(0.3f, 0.2f, 0.1f, 0.13f));
                        float t = k < lvl ? 1f : k == lvl ? f.SkillProgress(s) : 0f;
                        if (t > 0) Fill(new Rect(c.x, c.y, c.width * t, c.height), new Color(0.9f, 0.55f, 0.2f));
                    }
                }
                GUILayout.Label(lvl + "/10", sBold, W(40));
                GUILayout.EndHorizontal();
            }
        }

        void FamilyTab(Fox f)
        {
            GUILayout.Label($"{char.ToUpper(f.StageName[0])}{f.StageName.Substring(1)}, возраст {f.ageDays} дн.", sBold);
            int left = f.DaysToNextStage(G.lifespan);
            if (left >= 0)
                GUILayout.Label(f.stage < LifeStage.Elder ? $"До следующего этапа {left} дн." : "Почтенный возраст.", sSmall);
            if (f.pension > 0) GUILayout.Label($"Пенсия — {f.pension} мон. в неделю", sSmall);
            var partner = f.Partner;
            GUILayout.Space(4);
            GUILayout.Label(partner != null ? (f.married ? "В браке с " : "В паре с ") + partner.NameIn(NameCase.Instrumental) : "Пары нет.", sLabel);
            if (f.Pregnant) GUILayout.Label($"Лисята появятся через {Mathf.CeilToInt((2880f - f.pregnantMinutes) / 60f)} ч.", sSmall);

            FamilyLine("Родители", new List<string>(Family.Parents(f.id)));
            var grand = new List<string>();
            foreach (var p in Family.Parents(f.id)) grand.AddRange(Family.Parents(p));
            FamilyLine("Бабушки и дедушки", grand);
            FamilyLine("Братья и сёстры", AllIds().FindAll(id => Family.AreSiblings(f.id, id)));
            FamilyLine("Дети", AllIds().FindAll(id => Family.IsParent(f.id, id)));
            FamilyLine("Внуки", AllIds().FindAll(id => Family.IsGrandparent(f.id, id)));
            GUILayout.Space(6);
        }

        List<string> AllIds()
        {
            var l = new List<string>();
            foreach (var x in G.foxes) l.Add(x.id);
            foreach (var r in G.archive) if (!l.Contains(r.id)) l.Add(r.id);
            return l;
        }

        void FamilyLine(string title, List<string> ids)
        {
            if (ids.Count == 0) return;
            var names = new List<string>();
            foreach (var id in ids)
            {
                string n = Family.NameOf(id, out bool gone);
                var rec = gone ? G.archive.Find(r => r.id == id) : null;
                names.Add(gone ? $"{n} ({rec?.reason ?? "нет в доме"})" : n);
            }
            GUILayout.Label(title + " — " + string.Join(", ", names), sWrap);
        }

        void WorkTab(Fox f)
        {
            if (f.IsChild)
            {
                GUILayout.Label("Школа", sBig);
                GUILayout.Label($"{Careers.School.ScheduleText}", sLabel);
                GUILayout.Label("Учится " + Careers.Grade(f.performance).ToLower(), sBold);
                Bar("Учёба", (f.performance + 100f) / 200f, new Color(0.4f, 0.65f, 0.9f), 70, 250);
                GUILayout.Label("В школе растут логика и творчество. Хорошие оценки — повод для гордости при выпуске.", sSmall);
                return;
            }
            if (f.career != null)
            {
                var c = f.career;
                GUILayout.Label(c.name, sBig);
                GUILayout.Label(f.JobTitle + $"  ·  {f.Wage:0} мон./ч", sBold);
                GUILayout.Label(c.ScheduleText, sLabel);
                float fit = c.Fit(f.pers);
                GUILayout.Label(fit > 1.5f ? "Работа по душе — характер подходит." : fit < -1.5f ? "Работа не по душе — характер не подходит." : "К работе относится спокойно.", sSmall);
                Bar("Успехи", (f.performance + 100f) / 200f, f.performance >= 0 ? new Color(0.4f, 0.72f, 0.35f) : new Color(0.85f, 0.4f, 0.35f), 70, 250);
                if (f.careerLevel < c.levels.Length - 1)
                {
                    int need = CareerDef.SkillNeeded(f.careerLevel + 1);
                    int have = f.SkillLevel(c.skill);
                    GUILayout.Label($"Можно стать {c.instrumentalLevels[f.careerLevel + 1]}. Нужен навык «{Skills.Names[(int)c.skill]}» {need} (сейчас {have}).", sWrap);
                }
                else GUILayout.Label("Вершина карьеры!", sWrap);
                GUILayout.Label("Успехи растут от хорошего настроения, навыка и добросовестности. Опоздания и прогулы их снижают.", sSmall);
                if (Btn("Уволиться")) Defer(() => f.QuitJob());
                return;
            }
            GUILayout.Label("Пока без работы. Выбери карьеру:", sWrap);
            foreach (var cd in Careers.All)
            {
                var c = cd;
                float fit = c.Fit(f.pers);
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label(c.name + (fit > 1.5f ? "  (по душе)" : fit < -1.5f ? "  (не по душе)" : ""), sBold);
                GUILayout.Label($"{c.levels[0]} · {c.wage[0]:0} мон./ч · {c.ScheduleText}\nНужный навык — {Skills.Names[(int)c.skill].ToLower()}", sSmall);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (Btn("Устроиться", false, W(100))) Defer(() => f.TakeJob(c));
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
        }

        void WantsTab(Fox f)
        {
            ColorLabel($"Очки счастья — {f.happiness}", sBig, Accent);
            GUILayout.Label("Мечта всей жизни", sBold);
            var dd = f.Dream;
            if (dd == null) GUILayout.Label(f.stage < LifeStage.Teen ? "Лисята пока мечтают обо всём сразу." : "Мечта скоро появится…", sSmall);
            else
            {
                ColorLabel(dd.name, sBig, new Color(0.75f, 0.45f, 0.15f));
                GUILayout.Label(dd.desc, sWrap);
                // три уровня: пройденные — галочкой, текущий — с шагами, будущие — названием
                int lv = Mathf.Clamp(f.dreamLevel, 0, dd.Levels - 1);
                for (int i = 0; i < dd.Levels; i++)
                {
                    var L = dd.levels[i];
                    if (i < lv) { ColorLabel($"✓ Уровень {i + 1}: {L.title}", sBold, new Color(0.35f, 0.6f, 0.3f)); continue; }
                    if (i > lv) { ColorLabel($"Уровень {i + 1}: {L.title}  (+{Dreams.LevelReward[i]})", sSmall, new Color(0.55f, 0.5f, 0.45f)); continue; }
                    ColorLabel($"Уровень {i + 1}: {L.title}  (+{Dreams.LevelReward[i]} очков счастья)", sBold, new Color(0.85f, 0.5f, 0.15f));
                    foreach (var st in L.steps)
                    {
                        float p = Mathf.Clamp01(st.progress(f));
                        Bar("   " + st.text + (p >= 0.999f ? " — готово" : ""), p, p >= 0.999f ? new Color(0.45f, 0.75f, 0.4f) : new Color(0.95f, 0.7f, 0.35f), 210, 140);
                    }
                }
                GUILayout.Label($"Вся мечта — {Dreams.Reward} очков счастья, рост менталки и долгая радость.", sSmall);
            }
            if (f.dreamsDone.Count > 0) GUILayout.Label("Исполнившиеся мечты — " + string.Join(", ", f.dreamsDone.ConvertAll(id => Dreams.Get(id)?.name ?? id)), sSmall);
            if (f.stage >= LifeStage.Teen && Btn(new GUIContent("Выбрать другую мечту…", "Мечта по душе — любая из подходящих по возрасту"), false))
            {
                var fx = f;
                var items = new List<(string, System.Action)>();
                foreach (var d0 in Dreams.All)
                {
                    var d = d0;
                    if (fx.stage < d.minStage || d.id == fx.dream) continue;
                    items.Add((d.name + (fx.dreamsDone.Contains(d.id) ? " (уже сбылась)" : ""), () => fx.ChooseDream(d.id)));
                }
                Defer(() => OpenMenu(Input.mousePosition, "О чём мечтать?", items));
            }
            GUILayout.Space(6);
            GUILayout.Label("Желания", sBold);
            if (f.wants.Count == 0) GUILayout.Label("Скоро что-нибудь захочется…", sSmall);
            foreach (var w0 in new List<Want>(f.wants))
            {
                var w = w0;
                GUILayout.BeginHorizontal();
                GUILayout.Label("• " + w.DisplayText, sWrap, W(250));
                GUILayout.FlexibleSpace();
                GUILayout.Label("+" + w.points, sBold, W(42));
                if (Btn(new GUIContent("×", "Отказаться от желания"), false, W(28))) Defer(() => f.DismissWant(w));
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);
            GUILayout.Label("Страх", sBold);
            var fd = f.fearId != null ? Wants.GetFear(f.fearId) : null;
            GUILayout.Label(fd != null ? "• " + fd.text : "Сейчас ничего не боится.", sLabel);
            GUILayout.Space(4);
            GUILayout.Label("Награды за очки счастья", sBold);
            foreach (var pd in Wants.Perks)
            {
                var p = pd;
                bool has = f.HasPerk(p.id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent(p.name + (has ? "  (есть)" : ""), p.desc), has ? sBold : sLabel, W(200));
                GUILayout.FlexibleSpace();
                if (!has)
                {
                    GUI.enabled = f.happiness >= p.cost;
                    if (Btn(new GUIContent(p.cost.ToString(), p.desc), false, W(70))) Defer(() => f.BuyPerk(p));
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
        }

        void DiaryTab(Fox f)
        {
            if (f.memories.Count == 0) GUILayout.Label("Дневник пока пуст.", sSmall);
            for (int i = f.memories.Count - 1; i >= 0; i--)
            {
                var m = f.memories[i];
                ColorLabel($"День {m.day} · {m.text}", sWrap, m.good ? Ink : new Color(0.65f, 0.22f, 0.18f));
            }
        }

        void LogPanel()
        {
            int n = Mathf.Min(6, G.log.Count);
            if (n == 0) return;
            float h = 20 + n * 21;
            var r = new Rect(10, 74, 420, h);
            GUILayout.BeginArea(r, sPanel);
            for (int i = G.log.Count - n; i < G.log.Count; i++)
            {
                var e = G.log[i];
                float age = Time.time - e.born;
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, Mathf.Lerp(1f, 0.45f, Mathf.Clamp01((age - 20f) / 20f)));
                GUILayout.Label($"{e.time}  {e.text}", sSmall);
                GUI.color = old;
            }
            GUILayout.EndArea();
        }

        // ---------- обустройство ----------

        /// <summary>Отменить / повторить (Ctrl+Z, Ctrl+Shift+Z или Ctrl+Y).</summary>
        void UndoButtons()
        {
            GUI.enabled = G.CanUndo;
            if (Btn(new GUIContent("←", "Отменить (Ctrl+Z)"), false, W(32))) Defer(() => G.Undo());
            GUI.enabled = G.CanRedo;
            if (Btn(new GUIContent("→", "Повторить (Ctrl+Shift+Z или Ctrl+Y)"), false, W(32))) Defer(() => G.Redo());
            GUI.enabled = true;
        }

        void BuildPanel()
        {
            float panelWidth = G.townBuilder && buildTab == 3 ? 460 : 376;
            buildPanelInner = panelWidth - 44;
            Begin(new Rect(vw - panelWidth - 10, 130, panelWidth, vh - 130 - 110));
            GUILayout.BeginHorizontal();
            GUILayout.Label(G.townBuilder ? "Город" : "Обустройство", sTitle);
            GUILayout.FlexibleSpace();
            UndoButtons();
            if (G.townBuilder) { if (Btn(new GUIContent("Готово", "Закончить строительство и вернуться к семьям"), true, W(100))) Defer(() => G.ExitTownBuilder()); }
            else if (Btn(new GUIContent("Переезд…", "Продать свой дом, купить свободный дом или участок и переехать всей семьёй"))) Defer(() => MoveMenu());
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            float tw = G.townBuilder ? 74 : 96;
            var tabSt = G.townBuilder ? Fit(sBtn, "Ландшафт", tw) : sBtn;
            if (GUILayout.Button("Мебель", buildTab == 0 ? Fit(sBtnOn, "Ландшафт", tw) : tabSt, W(tw))) Defer(() => { buildTab = 0; G.lotTool = false; G.SetWallTool(Game.WallTool.None); G.SetLandTool(Game.LandTool.None); });
            if (GUILayout.Button("Стены", buildTab == 1 ? Fit(sBtnOn, "Ландшафт", tw) : tabSt, W(tw))) Defer(() => { buildTab = 1; G.lotTool = false; G.CancelPlacing(); G.SetLandTool(Game.LandTool.None); });
            if (GUILayout.Button("Ландшафт", buildTab == 2 ? Fit(sBtnOn, "Ландшафт", tw) : tabSt, W(tw))) Defer(() => { buildTab = 2; G.lotTool = false; G.CancelPlacing(); G.SetWallTool(Game.WallTool.None); });
            if (G.townBuilder && GUILayout.Button("Участки", buildTab == 3 ? Fit(sBtnOn, "Ландшафт", tw) : tabSt, W(tw))) Defer(() => { buildTab = 3; G.CancelPlacing(); G.SetWallTool(Game.WallTool.None); G.SetLandTool(Game.LandTool.None); });
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            if (buildTab == 3 && !G.townBuilder) buildTab = 0;
            if (buildTab == 3)
            {
                buildScroll = GUILayout.BeginScrollView(buildScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
                LotsTab();
                GUILayout.EndScrollView();
                End();
                return;
            }
            if (buildTab == 1)
            {
                buildScroll = GUILayout.BeginScrollView(buildScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
                WallTools();
                GUILayout.EndScrollView();
                End();
                return;
            }
            if (buildTab == 2)
            {
                LandTools();
                End();
                return;
            }
            if (G.ghost != null)
            {
                GUILayout.Label((G.movingExisting ? "Переставляем " : "Ставим ") + "«" + G.ghost.def.name + "»", sBig);
                GUILayout.Label(G.placeValid ? "Сюда можно поставить." : "Здесь не помещается.", sWrap);
                GUILayout.BeginHorizontal();
                if (Btn("Повернуть (R)")) Defer(() => G.RotateGhost());
                if (Btn("Отмена (Esc)")) Defer(() => G.CancelPlacing());
                GUILayout.EndHorizontal();
                if (G.movingExisting && Btn("Удалить предмет (Del)")) Defer(() => G.DeleteGhost());
                GUILayout.Space(8);
            }

            var cats = G.townBuilder ? Catalog.TownCategories : Catalog.Categories;
            if (buildCat >= cats.Length) buildCat = 0;
            // поиск по названию — сразу по всем разделам
            GUILayout.BeginHorizontal();
            GUILayout.Label("Найти", sBold, W(54));
            GUI.SetNextControlName("buildsearch");
            buildSearch = GUILayout.TextField(buildSearch ?? "", 30, sField, W(230));
            if (!string.IsNullOrEmpty(buildSearch) && Btn(new GUIContent("×", "Очистить поиск"), false, W(30))) Defer(() => { buildSearch = ""; GUI.FocusControl(null); });
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            for (int row = 0; row < cats.Length; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + 3, cats.Length); i++)
                {
                    int k = i;
                    if (Btn(cats[i], buildCat == i && string.IsNullOrEmpty(buildSearch), W(110))) Defer(() => { buildCat = k; buildSearch = ""; buildScroll = Vector2.zero; });
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            buildScroll = GUILayout.BeginScrollView(buildScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            CatalogGrid(cats, panelWidth - 40);
            GUILayout.EndScrollView();
            GUILayout.Label("Клик по вещи в доме — переставить · Shift+клик — поставить ещё · Del — продать. Купленное сегодня сдаётся по полной цене, потом — за 75%.", sSmall);
            if (G.ghost != null && G.movingExisting && !G.freeBuild)
                if (!G.freeBuild) ColorLabel($"Продать за {G.SellValue(G.ghost)} мон. (Del)" + (G.FullRefund(G.ghost) ? " — полная цена, куплено недавно" : ""), sBold, new Color(0.2f, 0.5f, 0.25f));
            End();
        }

        public int buildTab;

        void WallTool(string text, Game.WallTool t, string hint)
        {
            if (Btn(new GUIContent(text, hint), G.wallTool == t)) Defer(() => G.SetWallTool(G.wallTool == t ? Game.WallTool.None : t));
        }

        int roofRoom;

        void LandTool(string text, Game.LandTool t, string hint)
        {
            if (Btn(new GUIContent(text, hint), G.landTool == t)) Defer(() => G.SetLandTool(G.landTool == t ? Game.LandTool.None : t));
        }

        string Pr(int price) => G.freeBuild ? "" : $"  — {price} мон.";

        /// <summary>Настройка крыши над выбранной комнатой: тип, направление, наклон.</summary>
        public void SelectRoof(House.Room r)
        {
            var list = RoofRooms();
            int i = list.IndexOf(r);
            if (i < 0) return;
            roofRoom = i;
            buildTab = 1;
            G.house.showRoof = true;
            G.house.ApplyVisibility();
        }

        List<House.Room> RoofRooms() => G.house.rooms.FindAll(x => G.mayorMode ? x.lot == (G.house.focusLot >= 0 ? G.house.focusLot : House.PlayerLotId) : x.lot == House.PlayerLotId);

        // ---------- создание стиля ----------

        static readonly (string name, Color c)[] LightColors =
        {
            ("Тёплый", new Color(1f, 0.82f, 0.58f)), ("Свеча", new Color(1f, 0.66f, 0.36f)), ("Дневной", new Color(1f, 0.97f, 0.9f)),
            ("Лунный", new Color(0.7f, 0.82f, 1f)), ("Розовый", new Color(1f, 0.68f, 0.8f)), ("Мятный", new Color(0.7f, 1f, 0.82f)),
            ("Сиреневый", new Color(0.8f, 0.7f, 1f)), ("Янтарный", new Color(1f, 0.75f, 0.3f)),
        };

        /// <summary>Яркость и цвет света у ламп, торшеров, фонарей и свечей.</summary>
        void LightSettings(Styleable t)
        {
            var f = t.GetComponent<Furniture>();
            if (f == null || f.lamp == null) return;
            GUILayout.Space(4);
            GUILayout.Label("Яркость света — " + Mathf.RoundToInt(f.lightLevel * 100) + "%", sBold);
            float v = GUILayout.HorizontalSlider(f.lightLevel, 0f, 1f);
            if (Mathf.Abs(v - f.lightLevel) > 0.001f) { f.lightLevel = v; f.ApplyLight(); }
            if (f.def.id == "campfire") return;
            for (int row = 0; row < LightColors.Length; row += 4)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + 4, LightColors.Length); i++)
                {
                    var lc = LightColors[i];
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = lc.c;
                    bool sel = (f.lightColor - lc.c).maxColorComponent < 0.02f && (lc.c - f.lightColor).maxColorComponent < 0.02f;
                    if (Btn(lc.name, sel, W(88))) { var cc = lc.c; Defer(() => { f.lightColor = cc; f.ApplyLight(); }); }
                    GUI.backgroundColor = old;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);
        }

        void StylePanel()
        {
            var spr = new Rect(vw - 420, 74, 410, vh - 74 - 110);
            Begin(spr);
            if (Event.current.type == EventType.Repaint) Fill(new Rect(6, 6, spr.width - 12, 44), new Color(0.55f, 0.4f, 0.85f, 0.14f));
            styleScroll = GUILayout.BeginScrollView(styleScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Создание стиля", sTitle);
            GUILayout.FlexibleSpace();
            UndoButtons();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            ToolBtn("Выбор", StyleTool.Select, "Клик по детали — открыть её стиль");
            ToolBtn("Пипетка", StyleTool.Pipette, "Клик по любой детали — скопировать её стиль (Alt+клик)");
            ToolBtn("Кисть", StyleTool.Brush, "Клик — вставить скопированный стиль (Ctrl+клик), с Shift — на весь предмет");
            GUILayout.EndHorizontal();
            string hint = tool == StyleTool.Select ? "Кликни по любому предмету, стене, полу, дереву или лисичке."
                : tool == StyleTool.Pipette ? "Кликни по детали, чей стиль хочешь скопировать."
                : "Кликай по деталям, чтобы покрасить их. Shift+клик — весь предмет. Esc — выход.";
            GUILayout.Label(hint, sSmall);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Буфер:", sBold, W(60));
            if (G.clipboard != null)
            {
                Swatch(G.clipboard, 26);
                GUILayout.Label(G.clipboardName, sSmall);
            }
            else GUILayout.Label("пусто", sSmall);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            var t = G.styleTarget;
            if (t == null || t.parts.Count == 0)
            {
                GUILayout.Label("Ничего не выбрано.", sWrap);
                HousePalette(null, 0);
                GUILayout.EndScrollView();
                End();
                return;
            }
            int part = Mathf.Clamp(G.stylePart, 0, t.parts.Count - 1);
            GUILayout.Label(t.displayName, sBig);
            LightSettings(t);
            for (int row = 0; row < t.parts.Count; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + 3, t.parts.Count); i++)
                {
                    int k = i;
                    if (Btn(t.parts[i].name, part == i, W(110))) Defer(() => G.stylePart = k);
                }
                GUILayout.EndHorizontal();
            }

            var s = t.Get(part);
            if (t != syncTarget || part != syncPart || colorSlot != syncSlot) SyncColor(t, part);

            GUILayout.Space(4);
            GUILayout.Label("Узор «" + Patterns.Get(s.pattern).name + "»", sBold);
            var pats = Patterns.All;
            const int perRow = 6;
            for (int row = 0; row < pats.Count; row += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + perRow, pats.Count); i++)
                {
                    var r = GUILayoutUtility.GetRect(54, 54, W(54), Hh(54));
                    if (GUI.Button(r, new GUIContent("", pats[i].name), GUIStyle.none))
                    {
                        int k = i;
                        Defer(() => { var ns = t.Get(part).Clone(); ns.pattern = k; G.StyleEdit(t, part, ns); });
                    }
                    if (Event.current.type == EventType.Repaint)
                    {
                        if (i == s.pattern) Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Accent);
                        var old = GUI.color;
                        GUI.color = s.c1;
                        GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), pats[i].preview);
                        GUI.color = old;
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (Btn("Цвет 1", colorSlot == 0, W(90))) Defer(() => colorSlot = 0);
            SwatchColor(s.c1, 26);
            GUILayout.Space(10);
            if (Btn("Цвет 2", colorSlot == 1, W(90))) Defer(() => colorSlot = 1);
            SwatchColor(s.c2, 26);
            GUILayout.EndHorizontal();
            if (!Patterns.Get(s.pattern).usesColor2) GUILayout.Label("У этого узора один цвет — второй не используется.", sSmall);

            GUILayout.BeginHorizontal();
            ColorWheel(t, part, 150);
            GUILayout.BeginVertical();
            GUILayout.Label("Яркость", sLabel);
            float nv = GUILayout.HorizontalSlider(val, 0f, 1f, W(170));
            if (Mathf.Abs(nv - val) > 0.0001f) { val = nv; ApplyHsv(t, part); }
            GUILayout.Space(10);
            GUILayout.Label("Код цвета", sLabel);
            GUI.SetNextControlName("hex");
            string nh = GUILayout.TextField(hexText, 7, sField, W(110));
            if (nh != hexText)
            {
                hexText = nh;
                string clean = nh.TrimStart('#');
                if (clean.Length == 6 && ColorUtility.TryParseHtmlString("#" + clean, out var hc))
                {
                    Color.RGBToHSV(hc, out hue, out sat, out val);
                    SetSlotColor(t, part, hc, false);
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            RecentColors(t, part);
            GUILayout.Label("Стандартная палитра — эти цвета сочетаются между собой", sSmall);
            for (int row = 0; row < stdColors.Length; row += 12)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < row + 12 && i < stdColors.Length; i++)
                {
                    var r = GUILayoutUtility.GetRect(26, 22, W(26), Hh(22));
                    if (GUI.Button(r, new GUIContent("", "#" + StandardPalette[i].TrimStart('#')), GUIStyle.none))
                    {
                        var c = stdColors[i];
                        Defer(() => { Color.RGBToHSV(c, out hue, out sat, out val); SetSlotColor(t, part, c, true); });
                    }
                    if (Event.current.type == EventType.Repaint)
                    {
                        Fill(r, new Color(0.3f, 0.2f, 0.1f, 0.5f));
                        Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), stdColors[i]);
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Масштаб узора", sLabel, W(120));
            float sc = GUILayout.HorizontalSlider(s.scale, 0.25f, 4f, W(200));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Блеск", sLabel, W(120));
            float gl = GUILayout.HorizontalSlider(s.gloss, 0f, 1f, W(200));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(sc - s.scale) > 0.0001f || Mathf.Abs(gl - s.gloss) > 0.0001f)
            {
                var ns = s.Clone(); ns.scale = sc; ns.gloss = gl;
                G.StyleEdit(t, part, ns);
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (Btn(new GUIContent("Копировать", "Взять стиль этой детали в буфер"))) Defer(() => G.CopyStyle(t, part));
            if (Btn(new GUIContent("Вставить", "Покрасить эту деталь стилем из буфера")) && G.clipboard != null)
                Defer(() => G.StyleEdit(t, part, G.clipboard));
            if (Btn(new GUIContent("Всем деталям", "Сделать все детали предмета как эта"))) Defer(() =>
            {
                var cur = t.Get(part).Clone();
                G.StyleEditAll(t, cur);
            });
            GUILayout.EndHorizontal();

            HousePalette(t, part);
            GUILayout.EndScrollView();
            End();
        }

        void HousePalette(Styleable t, int part)
        {
            GUILayout.Space(8);
            GUILayout.Label("Палитра дома", sBig);
            GUILayout.Label(t != null ? "Клик — применить сохранённый стиль к выбранной детали." : "Выбери деталь, чтобы применять сохранённые стили.", sSmall);
            for (int row = 0; row < housePalette.Count; row += 8)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + 8, housePalette.Count); i++)
                {
                    var st = housePalette[i];
                    var r = GUILayoutUtility.GetRect(40, 40, W(40), Hh(40));
                    if (GUI.Button(r, new GUIContent("", Patterns.Get(st.pattern).name), GUIStyle.none))
                    {
                        if (t != null) Defer(() => { G.StyleEdit(t, part, st); syncTarget = null; });
                        Defer(() => { G.clipboard = st.Clone(); G.clipboardName = "Палитра дома"; });
                    }
                    if (Event.current.type == EventType.Repaint) DrawStyleThumb(r, st);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (t != null && housePalette.Count < 24 && Btn("+ Запомнить стиль детали"))
                Defer(() => housePalette.Add(t.Get(part).Clone()));
            if (housePalette.Count > 0 && Btn("Убрать последний")) Defer(() => housePalette.RemoveAt(housePalette.Count - 1));
            GUILayout.EndHorizontal();
        }

        void ToolBtn(string text, StyleTool tl, string hintText)
        {
            if (Btn(new GUIContent(text, hintText), tool == tl, W(120))) Defer(() => tool = tl);
        }

        void DrawStyleThumb(Rect r, StyleData st)
        {
            Fill(r, new Color(0.3f, 0.2f, 0.1f, 0.5f));
            var inner = new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2);
            var old = GUI.color;
            GUI.color = st.c1;
            GUI.DrawTexture(inner, Patterns.Get(st.pattern).preview);
            GUI.color = old;
            if (Patterns.Get(st.pattern).usesColor2)
                Fill(new Rect(r.xMax - 12, r.yMax - 12, 10, 10), st.c2);
        }

        void Swatch(StyleData st, float size)
        {
            var r = GUILayoutUtility.GetRect(size, size, W(size), Hh(size));
            if (Event.current.type == EventType.Repaint) DrawStyleThumb(r, st);
        }

        void SwatchColor(Color c, float size)
        {
            var r = GUILayoutUtility.GetRect(size, size, W(size), Hh(size));
            if (Event.current.type == EventType.Repaint)
            {
                Fill(r, new Color(0.3f, 0.2f, 0.1f, 0.6f));
                Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), c);
            }
        }

        void SyncColor(Styleable t, int part)
        {
            var s = t.Get(part);
            var c = colorSlot == 0 ? s.c1 : s.c2;
            Color.RGBToHSV(c, out hue, out sat, out val);
            hexText = ColorUtility.ToHtmlStringRGB(c);
            syncTarget = t;
            syncPart = part;
            syncSlot = colorSlot;
        }

        void ApplyHsv(Styleable t, int part) => SetSlotColor(t, part, Color.HSVToRGB(hue, sat, val), true);

        // недавние цвета: последние 12 выбранных (без повторов) — чтобы быстро красить в «тот же оттенок»
        readonly List<Color> recentColors = new List<Color>();
        float recentTimer;
        Color recentPending; bool recentHas;

        void RememberColor(Color c)
        {
            recentColors.RemoveAll(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.03f);
            recentColors.Insert(0, c);
            if (recentColors.Count > 12) recentColors.RemoveAt(recentColors.Count - 1);
        }

        void RecentColors(Styleable t, int part)
        {
            // цвет запоминается, когда его перестали крутить на круге
            if (recentHas && Time.unscaledTime - recentTimer > 0.8f) { recentHas = false; RememberColor(recentPending); }
            if (recentColors.Count == 0) return;
            GUILayout.Label("Недавние цвета", sSmall);
            GUILayout.BeginHorizontal();
            foreach (var c0 in recentColors)
            {
                var c = c0;
                var r = GUILayoutUtility.GetRect(26, 26, W(26), Hh(26));
                if (Event.current.type == EventType.Repaint)
                {
                    Fill(r, new Color(0.3f, 0.2f, 0.1f, 0.5f));
                    Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), c);
                }
                if (GUI.Button(r, new GUIContent("", "#" + ColorUtility.ToHtmlStringRGB(c)), GUIStyle.none))
                    Defer(() => { Color.RGBToHSV(c, out hue, out sat, out val); SetSlotColor(t, part, c, true); });
            }
            GUILayout.EndHorizontal();
        }

        void SetSlotColor(Styleable t, int part, Color c, bool updateHex)
        {
            recentPending = c; recentHas = true; recentTimer = Time.unscaledTime;
            var ns = t.Get(part).Clone();
            if (colorSlot == 0) ns.c1 = c; else ns.c2 = c;
            G.StyleEdit(t, part, ns);
            if (updateHex) hexText = ColorUtility.ToHtmlStringRGB(c);
        }

        void ColorWheel(Styleable t, int part, float size)
        {
            var r = GUILayoutUtility.GetRect(size, size, W(size), Hh(size));
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(Mathf.Lerp(0.25f, 1f, val), Mathf.Lerp(0.25f, 1f, val), Mathf.Lerp(0.25f, 1f, val), 1f);
                GUI.DrawTexture(r, wheel);
                GUI.color = old;
                float a = hue * Mathf.PI * 2f;
                var c = r.center + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * sat * size * 0.5f;
                Fill(new Rect(c.x - 6, c.y - 6, 12, 12), Color.black);
                Fill(new Rect(c.x - 4, c.y - 4, 8, 8), Color.HSVToRGB(hue, sat, val));
            }
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && r.Contains(e.mousePosition))
            {
                var d = (e.mousePosition - r.center) / (size * 0.5f);
                if (d.magnitude <= 1.08f)
                {
                    hue = (Mathf.Atan2(-d.y, d.x) / (2f * Mathf.PI) + 1f) % 1f;
                    sat = Mathf.Clamp01(d.magnitude);
                    if (val < 0.05f) val = 1f;
                    ApplyHsv(t, part);
                    e.Use();
                }
            }
        }

        // ---------- лисички: характер и внешность ----------

        // ================= создание и редактирование лисичек =================

        public int casTab;
        static readonly (string name, string icon, string tip)[] CasTabs =
        {
            ("Кто", "fox", "Имя, пол, возраст"),
            ("Характер", "mood", "Пять черт характера"),
            ("Вид", "style", "Уши, хвост, рост, окрас"),
            ("Одежда", "create", "Наряды и транспорт"),
            ("Вкусы", "wants", "Стихия, погода, время года, увлечения"),
            ("Умения", "skills", "Навыки и работа"),
            ("Мечты", "needs", "Желания и состояния"),
        };

        static readonly Color CasHeader = new Color(0.98f, 0.62f, 0.32f);

        /// <summary>
        /// Редактор лисичек: слева — семья портретами, ниже — вкладки (кто, характер, вид, одежда, вкусы, умения, мечты).
        /// Внизу — отмена/повтор (Ctrl+Z), «Случайная лисичка», «Готово». Лисичка видна справа крупно.
        /// </summary>
        void CreatePanel()
        {
            var f = CasFox;
            CasUndoTrack(f);
            float w = 470, x = 10, y = 80, h = vh - 80 - 20;
            var outer = new Rect(x, y, w, h);
            Reg(outer); // над панелью колёсико листает список, а не приближает камеру
            GUI.Box(outer, GUIContent.none, sPanel);
            // шапка
            if (Event.current.type == EventType.Repaint) Fill(new Rect(outer.x + 6, outer.y + 6, outer.width - 12, 46), new Color(CasHeader.r, CasHeader.g, CasHeader.b, 0.18f));
            var title = new GUIStyle(sTitle) { fontSize = 22 };
            GUI.Label(new Rect(x + 18, y + 12, 200, 34), G.mode == Mode.Town ? "Семья" : "Лисички", title);
            GUI.enabled = G.CanUndo;
            if (Btn(new Rect(x + w - 236, y + 14, 34, 30), new GUIContent("←", "Отменить (Ctrl+Z)"), false)) Defer(() => G.Undo());
            GUI.enabled = G.CanRedo;
            if (Btn(new Rect(x + w - 198, y + 14, 34, 30), new GUIContent("→", "Повторить (Ctrl+Y)"), false)) Defer(() => G.Redo());
            GUI.enabled = true;
            GUI.enabled = !CasNameEmpty;
            if (Btn(new Rect(x + w - 158, y + 14, 140, 30), new GUIContent("Готово", CasNameEmpty ? "Сначала придумай лисичке имя" : "Вернуться"), true)) Defer(() =>
            {
                if (CasNameEmpty) return;
                G.ClearUndo();
                if (G.mode == Mode.Create) { G.SetMode(Mode.Live); return; }
                townEditing = false;
                G.cam.Focus(G.cam.target, 999f);
                G.cam.dist = 60f;
                G.cam.pitch = 55f;
            });
            GUI.enabled = true;

            // семья — портретами
            var family = CasFamily;
            float py = y + 60, px = x + 14;
            int lotNow = CasLot;
            for (int i = 0; i < family.Count; i++)
            {
                var fox = family[i];
                var r = new Rect(px + i * 54, py, 48, 48);
                if (fox == f && Event.current.type == EventType.Repaint) Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), CasHeader);
                DrawPortrait(r, fox);
                if (r.Contains(Event.current.mousePosition)) tip = fox.foxName + " — " + fox.Summary.ToLower() + ", " + fox.StageName;
                var nm = new GUIStyle(sSmall) { alignment = TextAnchor.UpperCenter, fontSize = 11, clipping = TextClipping.Clip };
                GUI.Label(new Rect(r.x - 4, r.yMax + 1, r.width + 8, 16), fox.foxName, nm);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { var fx = fox; Defer(() => CasSelect(fx)); }
            }
            if (G.HomeCount(lotNow) < Game.MaxFoxes)
            {
                var r = new Rect(px + family.Count * 54, py, 48, 48);
                if (Btn(r, new GUIContent("+", "Новая лисичка в семью"), false))
                    Defer(() => { if (Net.IsClient) { if (f != null) Net.I.CasAddFox(f); return; } var nf = G.AddRandomFox(lotNow); if (nf != null) CasSelect(nf); });
            }
            if (f == null) return;

            // вкладки
            float ty = py + 70;
            float tw = (w - 28) / CasTabs.Length;
            for (int i = 0; i < CasTabs.Length; i++)
            {
                var r = new Rect(x + 14 + i * tw, ty, tw - 4, 52);
                bool on = casTab == i;
                if (Event.current.type == EventType.Repaint)
                {
                    Fill(r, on ? CasHeader : new Color(0.93f, 0.9f, 0.86f));
                    var old = GUI.color;
                    GUI.color = on ? Color.white : SimsInk;
                    GUI.DrawTexture(new Rect(r.center.x - 11, r.y + 6, 22, 22), Icon(CasTabs[i].icon));
                    GUI.color = old;
                }
                var ts = new GUIStyle(sSmall) { alignment = TextAnchor.LowerCenter, fontSize = 11, fontStyle = on ? FontStyle.Bold : FontStyle.Normal };
                ts.normal.textColor = on ? Color.white : SimsInk;
                GUI.Label(new Rect(r.x, r.y, r.width, r.height - 4), CasTabs[i].name, ts);
                if (r.Contains(Event.current.mousePosition)) tip = CasTabs[i].tip;
                int k = i;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Defer(() => { casTab = k; createScroll = Vector2.zero; });
            }

            // содержимое вкладки
            var body = new Rect(x + 14, ty + 60, w - 22, y + h - (ty + 60) - 52);
            GUILayout.BeginArea(body);
            createScroll = GUILayout.BeginScrollView(createScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            switch (casTab)
            {
                case 0: CasWho(f); break;
                case 1: CasCharacter(f); break;
                case 2: CasLook(f); break;
                case 3: ClothesSection(f); break;
                case 4: CasMore(f); break;
                case 5: CasSkills(f); break;
                default: CasWantsStates(f); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // низ: случайная, отпустить
            float by = y + h - 44;
            if (Btn(new Rect(x + 14, by, 200, 32), new GUIContent("Случайная лисичка", "Новые имя, характер, вид и вкусы — можно отменить"), false))
                Defer(() => CasRandomize(f));
            if (G.HomeCount(lotNow) > 1 && Btn(new Rect(x + w - 200, by, 186, 32), new GUIContent("Отпустить в лес", "Убрать эту лисичку из семьи"), false))
                Defer(() => { if (Net.IsClient) { Net.I.CasRemoveFox(f); return; } G.RemoveFox(f); CasSelect(G.FamilyOf(lotNow).Count > 0 ? G.FamilyOf(lotNow)[0] : null); });
        }

        void CasWho(Fox f)
        {
            FoxNameFields(f);
            GUILayout.Space(6);
            ColorLabel(f.Summary, sBig, Accent);
            GUILayout.Space(4);
            GUILayout.Label("Пол", sBold);
            GUILayout.BeginHorizontal();
            if (Btn("Лис", !f.female, W(140))) Defer(() => f.female = false);
            if (Btn("Лисица", f.female, W(140))) Defer(() => f.female = true);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.Label("Возраст", sBold);
            GUILayout.BeginHorizontal();
            string[] stages = { "Лисёнок", "Подросток", "Взрослый", "Пожилой" };
            for (int i = 0; i < 4; i++)
            {
                var st = (LifeStage)i;
                if (Btn(stages[i], f.stage == st, W(101))) Defer(() => { if (f.stage != st) f.SetStage(st, false); });
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("Подсказка: Ctrl+Z отменяет любую правку лисички, Ctrl+Y — возвращает.", sSmall);
        }

        void CasCharacter(Fox f)
        {
            GUILayout.BeginHorizontal();
            ColorLabel(f.Summary, sBig, Accent);
            GUILayout.FlexibleSpace();
            if (Btn("Случайный", false, W(120))) Defer(() => f.pers = Personality.Random());
            GUILayout.EndHorizontal();
            for (int i = 0; i < 5; i++)
            {
                int v = f.pers.v[i];
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label(PersonalityInfo.ScaleNames[i], sBold);
                GUILayout.FlexibleSpace();
                GUILayout.Label(v + " / 10", sLabel);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                ScaleCells(f, i);
                GUILayout.Space(8);
                ColorLabel(PersonalityInfo.TierNameFor(i, v, f.female), sTier, TierColors[PersonalityInfo.TierIndex(v)] * 0.85f + Color.black * 0.15f);
                GUILayout.EndHorizontal();
                GUILayout.Label(PersonalityInfo.TierDescription(i, v), sWrap);
            }
        }

        void CasLook(Fox f)
        {
            GUILayout.Label("Фигура", sBig);
            ShapeSlider("Уши", ref f.earSize, 0.7f, 1.6f, f);
            ShapeSlider("Хвост", ref f.tailFluff, 0.7f, 1.5f, f);
            ShapeSlider("Рост", ref f.size, 0.8f, 1.15f, f);
            GUILayout.Space(8);
            GUILayout.Label("Окрас", sBig);
            // образцы окраса: три полоски цвета (основной, светлый, тёмный) и название
            const int cols = 3;
            for (int row = 0; row < Game.FurPresets.Length; row += cols)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + cols, Game.FurPresets.Length); i++)
                {
                    var p = Game.FurPresets[i];
                    var r = GUILayoutUtility.GetRect(140, 54, W(140), Hh(54));
                    bool cur = f.style.Get(0).c1 == StyleData.Hex(p.fur);
                    if (Event.current.type == EventType.Repaint)
                    {
                        Fill(r, cur ? CasHeader : new Color(0.93f, 0.9f, 0.86f));
                        var sw = new Rect(r.x + 6, r.y + 6, r.width - 12, 22);
                        Fill(new Rect(sw.x, sw.y, sw.width * 0.5f, sw.height), StyleData.Hex(p.fur));
                        Fill(new Rect(sw.x + sw.width * 0.5f, sw.y, sw.width * 0.25f, sw.height), StyleData.Hex(p.light));
                        Fill(new Rect(sw.x + sw.width * 0.75f, sw.y, sw.width * 0.25f, sw.height), StyleData.Hex(p.dark));
                    }
                    var ns = new GUIStyle(sSmall) { alignment = TextAnchor.LowerCenter, fontSize = 12 };
                    ns.normal.textColor = cur ? Color.white : SimsInk;
                    GUI.Label(new Rect(r.x, r.y, r.width, r.height - 4), p.name, ns);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                        Defer(() =>
                        {
                            f.style.SetStyle(0, StyleData.Make("Гладкий", p.fur));
                            f.style.SetStyle(1, StyleData.Make("Гладкий", p.light));
                            f.style.SetStyle(2, StyleData.Make("Гладкий", p.dark));
                        });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.Label("Свой окрас и узоры — в «Создании стиля»: кликни по лисичке.", sSmall);
        }

        /// <summary>Случайная лисичка: имя, характер, фигура, окрас и вкусы (пол и возраст остаются).</summary>
        void CasRandomize(Fox f)
        {
            f.pers = Personality.Random();
            string nn = Names.Pick(f.female, n => G.foxes.Exists(o => o != f && o.foxName == n));
            if (!string.IsNullOrEmpty(nn)) f.Rename(nn);
            f.earSize = Random.Range(0.85f, 1.4f); f.tailFluff = Random.Range(0.85f, 1.3f); f.size = Random.Range(0.88f, 1.08f);
            f.ApplyShape();
            var p = Game.FurPresets[Random.Range(0, Game.FurPresets.Length)];
            f.style.SetStyle(0, StyleData.Make("Гладкий", p.fur));
            f.style.SetStyle(1, StyleData.Make("Гладкий", p.light));
            f.style.SetStyle(2, StyleData.Make("Гладкий", p.dark));
            f.elementId = -1; f.likedWeather = -1; f.dislikedWeather = -1;
            f.likedSeason = Random.Range(0, 4);
            f.interests = Interests.Random(f.pers); f.interestsSet = true;
            f.NewLook();
        }

        // ---------- отмена в редакторе лисичек: снимок правок раз в 0,3 с ----------

        Fox casSnapFox;
        string casSnap;
        float casSnapTimer;

        [System.Serializable] class CasSnapshot { public NetCas cas; public NetFoxStyle look; }

        static string CasState(Fox f)
        {
            f.SyncWorn();
            var look = new NetFoxStyle();
            foreach (var p in f.style.parts) look.fur.Add(p.style.Clone());
            if (f.outfits != null) look.outfits.AddRange(f.outfits);
            var cas = NetCas.Of(f);
            cas.needs = null; cas.health = -1; cas.mental = -1; cas.skillXp = null; // то, что меняется само, — не правка
            return JsonUtility.ToJson(new CasSnapshot { cas = cas, look = look });
        }

        static void CasRestore(Fox f, string json)
        {
            if (f == null || string.IsNullOrEmpty(json)) return;
            var s = JsonUtility.FromJson<CasSnapshot>(json);
            if (s == null) return;
            s.cas?.ApplyTo(f);
            if (s.look != null)
            {
                if (s.look.fur != null) for (int i = 0; i < s.look.fur.Count && i < f.style.parts.Count; i++) f.style.SetStyle(i, s.look.fur[i]);
                if (s.look.outfits != null && s.look.outfits.Count == 3)
                {
                    // глубокая копия нарядов через JSON, чтобы отмена не делила вещи со снимком
                    var copy = JsonUtility.FromJson<NetFoxStyle>(JsonUtility.ToJson(s.look));
                    f.outfits = copy.outfits.ToArray();
                    f.wornOutfit = -1;
                }
            }
        }

        void CasUndoTrack(Fox f)
        {
            if (f == null) { casSnapFox = null; return; }
            if (f != casSnapFox) { casSnapFox = f; casSnap = CasState(f); casSnapTimer = 0.3f; return; }
            casSnapTimer -= Time.unscaledDeltaTime;
            if (casSnapTimer > 0 || Event.current.type != EventType.Repaint) return;
            casSnapTimer = 0.3f;
            string now = CasState(f);
            if (now == casSnap) return;
            string before = casSnap, after = now;
            var fox = f;
            casSnap = now;
            G.PushCasUndo(fox.foxName, () => { CasRestore(fox, before); casSnap = before; }, () => { CasRestore(fox, after); casSnap = after; }, fox);
        }

        /// <summary>Всё остальное о лисичке: вкусы, погода, умения, работа, мечты, настроение — чтобы сразу создать такую, как хочешь.</summary>
        void CasMore(Fox f)
        {
            GUILayout.Space(10);
            GUILayout.Label("Вкусы", sBig);
            GUILayout.Label("Любимая стихия", sBold);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                if (Btn(new GUIContent(Elements.Names[i], $"Любит {Elements.Music[i]}, {Elements.Tv[i]} и {Elements.Books[i]}. Рисует {Elements.Paint[i]}."), f.Element == (Element)i, W(98)))
                    Defer(() => f.elementId = k);
            }
            GUILayout.EndHorizontal();
            string[] wn = { "Солнце", "Облака", "Туман", "Дождь", "Гроза" };
            GUILayout.Label("Любимая погода", sBold);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                if (Btn(wn[i], (int)f.LikedWeather == i, W(78))) Defer(() => { f.likedWeather = k; if (f.dislikedWeather == k) f.dislikedWeather = (k + 2) % 5; });
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Нелюбимая погода", sBold);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                if (Btn(wn[i], (int)f.DislikedWeather == i, W(78))) Defer(() => { f.dislikedWeather = k; if (f.likedWeather == k) f.likedWeather = (k + 2) % 5; });
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Любимое время года", sBold);
            GUILayout.BeginHorizontal();
            string[] sn = { "Весна", "Лето", "Осень", "Зима" };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                if (Btn(sn[i], f.likedSeason == i, W(98))) Defer(() => f.likedSeason = k);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Увлечения", "До трёх тем: о них лисичка шутит и рассказывает. С теми, кто увлечён тем же, легче подружиться."), sBig);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{f.interests.Count} из {Interests.Max}", sSmall);
            GUILayout.EndHorizontal();
            for (int row = 0; row < Interests.All.Count; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + 3, Interests.All.Count); i++)
                {
                    var d = Interests.All[i];
                    bool on = f.interests.Contains(d.id);
                    if (Btn(new GUIContent(d.name, on ? "Убрать увлечение" : f.interests.Count >= Interests.Max ? "Уже три увлечения — сначала убери одно" : "Добавить увлечение"), on, W(130)))
                        Defer(() => { if (on) f.interests.Remove(d.id); else if (f.interests.Count < Interests.Max) f.interests.Add(d.id); f.interestsSet = true; });
                }
                GUILayout.EndHorizontal();
            }

        }

        void CasSkills(Fox f)
        {
            GUILayout.Label("Умения", sBig);
            for (int s = 0; s < Skills.Count; s++)
            {
                int si = s;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Skills.Names[s], sLabel, W(104));
                for (int lv = 0; lv <= Skills.MaxLevel; lv++)
                {
                    int l = lv;
                    bool on = f.skillLvl[s] >= lv && lv > 0;
                    var r = GUILayoutUtility.GetRect(24, 22, W(24), Hh(22));
                    if (Event.current.type == EventType.Repaint) Fill(new Rect(r.x + 1, r.y + 3, r.width - 2, r.height - 6), lv == 0 ? new Color(0.93f, 0.9f, 0.86f) : on ? SimsBlue : new Color(0.9f, 0.92f, 0.95f));
                    if (lv == 0 && Event.current.type == EventType.Repaint) GUI.Label(r, "×", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
                    if (lv == 0 && r.Contains(Event.current.mousePosition)) tip = "Сбросить умение до нуля";
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Defer(() => { f.skillLvl[si] = l; f.skillXp[si] = 0; });
                }
                GUILayout.Label(f.skillLvl[s].ToString(), sBold, W(26));
                GUILayout.EndHorizontal();
            }

            if (f.stage >= LifeStage.Adult)
            {
                GUILayout.Space(10);
                GUILayout.Label("Работа", sBig);
                GUILayout.BeginHorizontal();
                if (Btn("Без работы", f.career == null, W(130))) Defer(() => f.QuitJob(false));
                GUILayout.EndHorizontal();
                int col = 0;
                GUILayout.BeginHorizontal();
                foreach (var c in Careers.All)
                {
                    if (c.id == "school") continue;
                    var cc = c;
                    if (Btn(c.name, f.career == c, W(130))) Defer(() => { if (f.career != cc) f.TakeJob(cc); });
                    if (++col % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                }
                GUILayout.EndHorizontal();
                if (f.career != null)
                {
                    GUILayout.Label("Должность", sBold);
                    for (int i = 0; i < f.career.levels.Length; i++)
                    {
                        int k = i;
                        if (Btn(f.career.levels[i] + $" — {f.career.wage[i]:0} мон./час", f.careerLevel == i)) Defer(() => f.careerLevel = k);
                    }
                }
            }

        }

        void CasWantsStates(Fox f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Желания", sBig);
            GUILayout.FlexibleSpace();
            if (Btn(new GUIContent("Другие желания", "Загадать новые желания"))) Defer(() => { f.wants.Clear(); f.RefreshWants(); });
            GUILayout.EndHorizontal();
            foreach (var w in f.wants) GUILayout.Label("• " + w.DisplayText, sWrap);

            GUILayout.Space(10);
            GUILayout.Label("Состояния", sBig);
            for (int i = 0; i < 6; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent(Fox.StateNames[i], Fox.StateTips[i]), sLabel, W(110));
                float cur = f.StateValue(i);
                float v = GUILayout.HorizontalSlider(cur, 0, 100, W(250));
                if (Mathf.Abs(v - cur) > 0.01f) f.SetState(i, v);
                GUILayout.EndHorizontal();
            }
        }

        static readonly (LotKind k, string n)[] LotKinds =
        {
            (LotKind.Home, "Жилой"), (LotKind.Park, "Парк"), (LotKind.Cafe, "Кафе"), (LotKind.Library, "Библиотека"), (LotKind.Square, "Площадь"),
            (LotKind.School, "Школа"), (LotKind.Workshop, "Мастерская"), (LotKind.Observatory, "Обсерватория"), (LotKind.Camp, "Поляна"),
        };

        internal int lotsEdit = -1;
        int lotMoneyFor = -1;
        string lotMoneyText = "";

        /// <summary>Сколько денег получит семья, которую создадут на участке: само (5000 на пустом, 2000 на обжитом) или вручную.</summary>
        void LotMoneyRow(int id)
        {
            if (id < 0 || id >= Town.Lots.Length || Town.Lots[id].kind != LotKind.Home) return;
            var d = Town.Lots[id];
            if (lotMoneyFor != id) { lotMoneyFor = id; lotMoneyText = G.NewFamilyMoney(id).ToString(); }
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Деньги новой семьи", "Столько монет получит семья, которую создадут на этом участке"), sSmall, W(150));
            GUI.SetNextControlName("lotmoney" + id);
            string t = GUILayout.TextField(lotMoneyText, 8, sField, W(90));
            if (t != lotMoneyText)
            {
                lotMoneyText = t;
                string digits = "";
                foreach (char ch in t) if (char.IsDigit(ch)) digits += ch;
                if (digits.Length > 0 && int.TryParse(digits, out int v)) Defer(() => G.SetLotMoney(id, v));
            }
            if (d.customMoney && Btn(new GUIContent("Само", $"По умолчанию семья получает {Game.EmptyLotMoney} на пустом участке и {Game.BuiltLotMoney} на обустроенном."), false, W(70)))
                Defer(() => { G.SetLotMoney(id, null); lotMoneyFor = -1; });
            GUILayout.EndHorizontal();
            if (!d.customMoney) GUILayout.Label(G.LotIsEmpty(id) ? "Участок пустой — хватит на домик." : "На участке уже что-то есть.", sSmall);
        }

        /// <summary>Строительство города: участки — создать, переименовать, сменить назначение, снести.</summary>
        void LotsTab()
        {
            if (G.lotTool)
            {
                GUILayout.Label("Новый участок", sBig);
                GUILayout.Label(G.lotToolHint ?? "", sWrap);
                if (Btn("Отмена")) Defer(() => G.lotTool = false);
                return;
            }
            if (Btn(new GUIContent("+ Новый участок", "Протяни прямоугольник по земле"), true, Hh(38))) Defer(() => G.lotTool = true);
            if (G.lotsDirty) GUILayout.Label("Участки изменены — город перестроится, когда нажмёшь «Готово».", sSmall);
            GUILayout.Space(6);
            for (int i = 0; i < Town.Lots.Length; i++)
            {
                var d = Town.Lots[i];
                if (d.removed) continue;
                int id = i;
                GUILayout.BeginHorizontal();
                if (Btn(new GUIContent(d.name, "Показать участок и изменить"), lotsEdit == id, W(250)))
                    Defer(() => { lotsEdit = lotsEdit == id ? -1 : id; G.CancelResize(); G.cam.Focus(new Vector3(d.rect.center.x, 0, d.rect.center.y), 30f); });
                GUILayout.EndHorizontal();
                if (lotsEdit != id) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Название", sSmall, W(70));
                GUI.SetNextControlName("lotname" + id);
                string nn = GUILayout.TextField(d.name, 40, sField, W(220));
                if (nn != d.name) G.RenameLot(id, nn);
                GUILayout.EndHorizontal();
                GUILayout.Label("Падежи названия участка", sBold);
                var forms = DrawCaseFields("lotcase" + id + "_", Town.Lots[id].nameOverrides,
                    c => Town.LotNameIn(id, Town.Lots[id].name, c), LotCaseExamples);
                Town.Lots[id].nameOverrides = forms;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Предлог места", sSmall, W(110));
                if (Btn("Авто", d.preposition == LotPreposition.Automatic, W(70))) Defer(() => Town.Lots[id].preposition = LotPreposition.Automatic);
                if (Btn("В", d.preposition == LotPreposition.In, W(42))) Defer(() => Town.Lots[id].preposition = LotPreposition.In);
                if (Btn("На", d.preposition == LotPreposition.On, W(42))) Defer(() => Town.Lots[id].preposition = LotPreposition.On);
                GUILayout.EndHorizontal();
                GUILayout.Label("У стандартных названий уже есть формы. Для своего названия можно вписать их вручную.", sWrap);
                LotMoneyRow(id);
                GUILayout.Label("Назначение", sSmall);
                for (int row = 0; row < LotKinds.Length; row += 3)
                {
                    GUILayout.BeginHorizontal();
                    for (int k = row; k < Mathf.Min(row + 3, LotKinds.Length); k++)
                    {
                        var lk = LotKinds[k];
                        if (Btn(lk.n, d.kind == lk.k, W(120))) Defer(() => G.SetLotKind(id, lk.k));
                    }
                    GUILayout.EndHorizontal();
                }
                // размер: по клеточке с каждой стороны, превью на земле, пересборка — по «Применить»
                bool resizing = G.resizeLot == id;
                var rr = resizing ? G.resizeRect : d.rect;
                GUILayout.Label($"Размер — {rr.width} × {rr.height}", sSmall);
                if (!resizing) { if (Btn(new GUIContent("Изменить размер…", "Расширить или сузить участок с любой стороны"))) Defer(() => G.BeginResize(id)); }
                else
                {
                    string[] sides = { "Север", "Юг", "Запад", "Восток" };
                    for (int sd = 0; sd < 4; sd++)
                    {
                        int k = sd;
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(sides[sd], sSmall, W(70));
                        if (Btn("−", false, W(44))) Defer(() => G.NudgeLot(k, -1));
                        if (Btn("+", false, W(44))) Defer(() => G.NudgeLot(k, 1));
                        GUILayout.EndHorizontal();
                    }
                    string rw = G.CheckResize();
                    if (rw != null) GUILayout.Label(rw + ".", sSmall);
                    GUILayout.BeginHorizontal();
                    if (Btn(new GUIContent("Применить", "Город перестроится один раз"), rw == null, W(130)) && rw == null) Defer(() => G.ApplyResize());
                    if (Btn("Отмена", false, W(100))) Defer(() => G.CancelResize());
                    GUILayout.EndHorizontal();
                }
                string why = G.CanRemoveLot(id);
                if (why == null) { if (Btn(new GUIContent("Снести участок", "Всё на участке исчезнет, жильцы переедут в архив"))) Defer(() => { G.RemoveLot(id); lotsEdit = -1; }); }
                else GUILayout.Label("Участок нельзя снести. " + why + ".", sSmall);
                GUILayout.Space(6);
            }
            GUILayout.Space(6);
            GUILayout.Label("Клик по изгороди, арке, кусту, камню, фонарю или дереву — убрать его.", sSmall);
        }

        static readonly string[] NameCaseLabels =
        {
            "Именительный", "Родительный", "Дательный", "Винительный", "Творительный", "Предложный"
        };
        static readonly string[] NameCaseExamples =
        {
            "Рыжик", "нет Рыжика", "к Рыжику", "вижу Рыжика", "с Рыжиком", "о Рыжике"
        };
        static readonly string[] LotCaseExamples =
        {
            "Лисий дом", "нет Лисьего дома", "к Лисьему дому", "в Лисий дом", "за Лисьим домом", "в Лисьем доме"
        };

        public bool nameFormsOpen;
        Fox nameBufFox;
        string nameBuf;
        /// <summary>В редакторе стёрли имя целиком: пока имени нет, «Готово» не нажать.</summary>
        bool CasNameEmpty => nameBufFox != null && nameBufFox == CasFox && string.IsNullOrWhiteSpace(nameBuf);

        /// <summary>
        /// Имя и его формы: маленькая история с пропусками («Без ___ в долине было бы скучно»).
        /// В пустом пропуске серым — как скажет игра сама; можно вписать свою форму.
        /// </summary>
        void FoxNameFields(Fox f)
        {
            // имя можно стереть целиком (буфер поля), но лисичка без имени не остаётся — «Готово» ждёт имени
            if (nameBufFox != f || (GUI.GetNameOfFocusedControl() != "foxname" && !string.IsNullOrWhiteSpace(nameBuf) && nameBuf != f.foxName))
            { nameBufFox = f; nameBuf = f.foxName; }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Имя", sBold, W(50));
            GUI.SetNextControlName("foxname");
            string next = GUILayout.TextField(nameBuf ?? "", 18, sField, W(200));
            if (next != nameBuf)
            {
                nameBuf = next;
                if (!string.IsNullOrWhiteSpace(next)) f.Rename(next.Trim());
            }
            GUILayout.FlexibleSpace();
            if (Btn(new GUIContent(nameFormsOpen ? "Как звучит ▾" : "Как звучит ▸", "Как имя меняется в разговорах лисичек — можно поправить"), nameFormsOpen, W(130)))
                Defer(() => nameFormsOpen = !nameFormsOpen);
            GUILayout.EndHorizontal();
            if (CasNameEmpty) ColorLabel("Придумай имя — без имени лисичку не сохранить.", sSmall, new Color(0.8f, 0.3f, 0.2f));
            if (!nameFormsOpen) return;
            var box = new GUIStyle(sLabel) { fontSize = 14, wordWrap = false };
            GUILayout.Space(2);
            GUILayout.Label("Так о лисичке будут говорить в долине. Если звучит неправильно — впиши свою форму в пропуск.", sSmall);
            for (int i = 1; i < 6; i++)
            {
                var c = (NameCase)i;
                var line = NameStory.For(f, c);
                string control = "foxcase" + i;
                string current = f.nameOverrides?[i - 1] ?? "";
                string shown = current.Length > 0 ? current : Names.DefaultForm(f.foxName, c);
                // фраза целиком: если не помещается в строку — окончание переносится на следующую (слова не теряются)
                string pre = NameStory.Fill(line.pre, shown);
                float fw = Mathf.Clamp(sField.CalcSize(new GUIContent(shown)).x + 18, 70, 170);
                float preW = box.CalcSize(new GUIContent(pre)).x, postW = box.CalcSize(new GUIContent(line.post)).x;
                const float avail = 420f;
                bool preAlone = preW + fw > avail;
                bool postBelow = !preAlone && preW + fw + postW > avail;
                if (preAlone) GUILayout.Label(new GUIContent(pre, NameCaseLabels[i] + " падеж"), box);
                GUILayout.BeginHorizontal();
                if (!preAlone) GUILayout.Label(new GUIContent(pre, NameCaseLabels[i] + " падеж"), box, W(preW));
                GUI.SetNextControlName(control);
                string val = GUILayout.TextField(current, RussianCases.MaxFormLength, sField, W(fw));
                if (val != current)
                {
                    if (f.nameOverrides == null) f.nameOverrides = new string[RussianCases.CaseCount];
                    f.nameOverrides[i - 1] = val;
                }
                if (Event.current.type == EventType.Repaint && val.Length == 0 && GUI.GetNameOfFocusedControl() != control)
                {
                    var r = GUILayoutUtility.GetLastRect(); r.xMin += 6;
                    var old = GUI.color; GUI.color = new Color(1, 1, 1, 0.55f);
                    GUI.Label(r, shown, sLabel);
                    GUI.color = old;
                }
                if (!postBelow) GUILayout.Label(line.post, box, W(postW));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                if (postBelow) GUILayout.Label("    " + line.post.TrimStart(), box);
                GUILayout.Space(2);
            }
        }

        string[] DrawCaseFields(string key, string[] forms, System.Func<NameCase, string> defaultForm, string[] examples)
        {
            for (int i = 1; i < NameCaseLabels.Length; i++)
            {
                string control = key + i;
                string current = forms?[i - 1] ?? "";
                GUILayout.BeginHorizontal();
                GUILayout.Label(NameCaseLabels[i], sBold, W(138));
                GUI.SetNextControlName(control);
                string next = GUILayout.TextField(current, RussianCases.MaxFormLength, sField, W(130));
                if (next != current)
                {
                    if (forms == null) forms = new string[RussianCases.CaseCount];
                    forms[i - 1] = next;
                }
                if (Event.current.type == EventType.Repaint && next.Length == 0 && GUI.GetNameOfFocusedControl() != control)
                {
                    Rect field = GUILayoutUtility.GetLastRect();
                    field.xMin += 5;
                    GUI.Label(field, defaultForm((NameCase)i), sSmall);
                }
                GUILayout.Label(examples[i], sSmall);
                GUILayout.EndHorizontal();
            }
            return forms;
        }

        void ClothesSection(Fox f)
        {
            // ---------- одежда ----------
            GUILayout.Space(8);
            GUILayout.Label("Одежда", sTitle);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Wardrobe.OutfitNames.Length; i++)
            {
                int k = i;
                if (Btn(Wardrobe.OutfitNames[i], outfitTab == i, W(136))) Defer(() => outfitTab = k);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(outfitTab == 0 ? "В этом лисичка ходит обычно." : outfitTab == 1 ? "Переодевается в это ко сну." : "Надевает под дождём на улице.", sSmall);
            if (f.outfits != null)
                for (int si = 0; si < Wardrobe.SlotNames.Length; si++)
                {
                    var slot = (ClothSlot)si;
                    var cur = f.outfits[outfitTab].Get(slot);
                    GUILayout.Label(Wardrobe.SlotNames[si], sBold);
                    var opts = Wardrobe.ForSlot(slot);
                    for (int row = -1; row < opts.Count; row += 2)
                    {
                        GUILayout.BeginHorizontal();
                        for (int c = row; c < row + 2 && c < opts.Count; c++)
                        {
                            if (c < 0)
                            {
                                if (Btn("Ничего", cur == null, W(200))) Defer(() => f.SetItem(outfitTab, slot, null));
                                continue;
                            }
                            var d = opts[c];
                            if (Btn(d.name, cur != null && cur.id == d.id, W(200))) Defer(() => f.SetItem(outfitTab, slot, d.id));
                        }
                        GUILayout.EndHorizontal();
                    }
                }
            GUILayout.Label("Чтобы перекрасить вещь, кликни по ней на лисичке в режиме «Создание стиля».", sSmall);
        }

        void ScaleCells(Fox f, int scaleIdx)
        {
            int v = f.pers.v[scaleIdx];
            for (int i = 1; i <= 10; i++)
            {
                if (i == 2 || i == 5 || i == 7 || i == 10) GUILayout.Space(5);
                var r = GUILayoutUtility.GetRect(20, 22, W(20), Hh(22));
                int tier = PersonalityInfo.TierIndex(i);
                if (GUI.Button(r, new GUIContent("", PersonalityInfo.TierName(scaleIdx, i) + " (" + i + ")"), GUIStyle.none))
                {
                    int k = i;
                    Defer(() => f.pers.v[scaleIdx] = k);
                }
                if (Event.current.type == EventType.Repaint)
                {
                    var c = TierColors[tier];
                    Fill(r, i <= v ? c : new Color(c.r, c.g, c.b, 0.18f));
                    if (i == v) Fill(new Rect(r.x, r.yMax - 3, r.width, 3), Ink);
                }
            }
        }

        void ShapeSlider(string label, ref float value, float min, float max, Fox f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, sLabel, W(70));
            float nv = GUILayout.HorizontalSlider(value, min, max, W(260));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(nv - value) > 0.0001f)
            {
                value = nv;
                f.ApplyShape();
            }
        }

        // ---------- меню, подсказки ----------

        void Menu()
        {
            if (menuOpen && pieItems != null) { Pie(); return; }
            if (!menuOpen || menuItems == null) return;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { Event.current.Use(); Defer(CloseMenu); }
            GUILayout.BeginArea(menuRect, sPanel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(menuTitle, Fit(sBold, menuTitle, menuRect.width - 60));
            GUILayout.FlexibleSpace();
            if (Btn(new GUIContent("×", "Закрыть"), false, W(28), Hh(26))) Defer(CloseMenu);
            GUILayout.EndHorizontal();
            foreach (var it in menuItems)
            {
                var item = it;
                if (Btn(item.label)) Defer(() => { CloseMenu(); item.act(); });
            }
            GUILayout.EndArea();
        }

        void Help()
        {
            string h;
            switch (G.mode)
            {
                case Mode.Build: h = buildTab == 1 ? "Тяни по сетке · ПКМ — отменить · Esc — выйти из инструмента" : "R — повернуть · Esc — отмена · Del — удалить"; break;
                case Mode.Style: h = "Alt+клик — пипетка · Ctrl+клик — кисть · Ctrl+Shift+клик — на весь предмет"; break;
                default: h = "ЛКМ — выбрать/действие · ПКМ — вращать камеру · колесо — зум · WASD — перемещение · пробел — пауза"; break;
            }
            var r = new Rect(10, vh - 24, vw - 20, 20);
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), h, sHelp);
            GUI.color = old;
            GUI.Label(r, h, sHelp);
        }

        void Tooltip()
        {
            if (string.IsNullOrEmpty(tip)) return;
            var c = new GUIContent(tip);
            float w = Mathf.Min(300, sTip.CalcSize(c).x + 4);
            float h = sTip.CalcHeight(c, w);
            var m = Event.current.mousePosition;
            var r = new Rect(m.x + 16, m.y + 18, w, h);
            if (r.xMax > vw - 4) r.x = vw - 4 - w;
            if (r.yMax > vh - 4) r.y = m.y - h - 6;
            GUI.Label(r, c, sTip);
        }
    }
}
