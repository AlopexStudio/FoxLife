using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public class PieItem
    {
        public string label;
        public string DisplayLabel => sub == null ? label : label + "…";
        public System.Action act;
        public bool enabled = true;
        public string reason;
        public List<PieItem> sub;
        public PieItem(string label, System.Action act = null) { this.label = label; this.act = act; }
    }

    /// <summary>
    /// Круговое меню действий (как в Sims) с категориями. Недоступное — серым, с причиной в подсказке.
    /// «Подумать…» открывает боковую панель мыслей: о себе, о нуждах, о друзьях — или о другой лисичке.
    /// </summary>
    public partial class GameUI
    {
        const int PiePageSize = 11;
        const float PieCenterSize = 96f;
        List<PieItem> pieItems, laidOutPie;
        readonly Stack<(List<PieItem> items, string subtitle, int page)> pieParents = new Stack<(List<PieItem>, string, int)>();
        readonly List<(PieItem item, Rect rect, GUIStyle style)> pieButtons = new List<(PieItem, Rect, GUIStyle)>();
        Vector2 pieAnchor, pieCenter;
        Vector2 pieViewport;
        int piePage, laidOutPage;
        Rect piePager;
        string pieTitle, pieSubTitle;

        public void OpenPie(Vector3 screenPos, string title, List<PieItem> items)
        {
            pieItems = items;
            pieParents.Clear();
            laidOutPie = null;
            piePage = 0;
            pieTitle = title;
            pieSubTitle = null;
            pieAnchor = new Vector2(screenPos.x / scale, (Screen.height - screenPos.y) / scale);
            menuItems = null;
            menuRect = default;
            menuOpen = true;
        }

        internal void EnterPieCategory(PieItem item)
        {
            pieParents.Push((pieItems, pieSubTitle, piePage));
            pieItems = item.sub;
            pieSubTitle = item.label;
            piePage = 0;
            laidOutPie = null;
        }

        void ReturnToPieParent()
        {
            if (pieParents.Count == 0) return;
            (pieItems, pieSubTitle, piePage) = pieParents.Pop();
            laidOutPie = null;
        }

        void LayoutPie()
        {
            var viewport = new Vector2(vw, vh);
            if (laidOutPie == pieItems && laidOutPage == piePage && pieViewport == viewport) return;
            laidOutPie = pieItems;
            laidOutPage = piePage;
            pieViewport = viewport;
            pieButtons.Clear();
            float radius = PieCenterSize * 0.5f;
            var bounds = new Rect(-radius, -radius, PieCenterSize, PieCenterSize);
            int start = piePage * PiePageSize;
            int count = Mathf.Min(PiePageSize, pieItems.Count - start);
            bool crown = count % 2 == 1;
            int rightCount = (count - (crown ? 1 : 0)) / 2;
            int leftCount = count - (crown ? 1 : 0) - rightCount;
            float maxWidth = Mathf.Clamp((vw - PieCenterSize - 50f) / 2f, 92f, 226f);
            const float row = 43f;

            for (int i = 0; i < count; i++)
            {
                var item = pieItems[start + i];
                var label = new GUIContent(item.DisplayLabel);
                float width = Mathf.Clamp(sBtn.CalcSize(label).x + 12f, 92f, maxWidth);
                var style = Fit(sBtn, label.text, width, 38f);
                float height = Mathf.Clamp(style.CalcHeight(label, width), 32f, 38f);
                Rect rect;
                if (crown && i == 0)
                {
                    float y = -Mathf.Max(radius + 25f, (rightCount - 1) * row * 0.5f + row);
                    rect = new Rect(-width / 2f, y - height / 2f, width, height);
                }
                else
                {
                    int index = i - (crown ? 1 : 0);
                    bool right = index < rightCount;
                    int rowIndex = right ? index : leftCount - 1 - (index - rightCount);
                    int sideCount = right ? rightCount : leftCount;
                    float y = (rowIndex - (sideCount - 1) * 0.5f) * row;
                    float edge = radius + 13f - Mathf.Min(23f, Mathf.Abs(y) * 0.27f);
                    rect = new Rect(right ? edge : -edge - width, y - height / 2f, width, height);
                }
                pieButtons.Add((item, rect, style));
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin),
                    Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax));
            }

            if (pieItems.Count > PiePageSize)
            {
                piePager = new Rect(-53, bounds.yMax + 8, 106, 26);
                bounds.yMax = piePager.yMax;
            }
            else piePager = default;

            pieCenter = new Vector2(Mathf.Clamp(pieAnchor.x, 12 - bounds.xMin, vw - 12 - bounds.xMax),
                Mathf.Clamp(pieAnchor.y, 12 - bounds.yMin, vh - 12 - bounds.yMax));
        }

        void Pie()
        {
            if (!menuOpen || pieItems == null) return;
            EnsureSims();
            LayoutPie();
            var cr = new Rect(pieCenter.x - PieCenterSize / 2, pieCenter.y - PieCenterSize / 2, PieCenterSize, PieCenterSize);
            Reg(cr);
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.15f);
                GUI.DrawTexture(new Rect(cr.x + 2, cr.y + 4, cr.width, cr.height), texDot);
                GUI.color = Color.white;
                GUI.DrawTexture(cr, texDot);
                GUI.color = SimsBlue;
                GUI.DrawTexture(cr, texRing);
                GUI.color = old;
            }
            bool inSubmenu = pieParents.Count > 0;
            string ctitle = pieSubTitle ?? pieTitle;
            var titleRect = inSubmenu ? new Rect(cr.x + 8, cr.y + 22, cr.width - 16, 34) : new Rect(cr.x + 8, cr.y + 8, cr.width - 16, cr.height - 16);
            var ts = Fit(new GUIStyle(sBold) { alignment = TextAnchor.MiddleCenter, fontSize = 13 }, ctitle, titleRect.width, titleRect.height);
            ts.normal.textColor = SimsInk;
            GUI.Label(titleRect, ctitle, ts);
            if (inSubmenu)
            {
                GUI.Label(new Rect(cr.x, cr.y + 51, cr.width, 26), "←", ts);
                if (GUI.Button(cr, GUIContent.none, GUIStyle.none)) Defer(ReturnToPieParent);
            }

            foreach (var button in pieButtons)
            {
                var it = button.item;
                string text = it.DisplayLabel;
                var bs = button.style;
                var r = button.rect;
                r.position += pieCenter;
                Reg(r);
                if (!it.enabled)
                {
                    var old = GUI.color;
                    GUI.color = new Color(1, 1, 1, 0.55f);
                    GUI.Box(r, text, bs);
                    GUI.color = old;
                    if (r.Contains(Event.current.mousePosition)) tip = it.reason ?? "Сейчас недоступно";
                    continue;
                }
                if (r.Contains(Event.current.mousePosition) && it.reason != null) tip = it.reason;
                if (Btn(r, text, bs))
                {
                    var item = it;
                    if (item.sub != null) Defer(() => EnterPieCategory(item));
                    else Defer(() => { CloseMenu(); item.act?.Invoke(); });
                }
            }

            if (piePager.width > 0)
            {
                var pager = piePager;
                pager.position += pieCenter;
                Reg(pager);
                GUI.Box(pager, GUIContent.none, sPanel);
                int pages = Mathf.CeilToInt((float)pieItems.Count / PiePageSize);
                GUI.Label(new Rect(pager.x + 30, pager.y, 46, pager.height), $"{piePage + 1} / {pages}",
                    new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
                if (piePage > 0 && Btn(new Rect(pager.x, pager.y, 28, pager.height), "‹")) Defer(() => piePage--);
                if (piePage + 1 < pages && Btn(new Rect(pager.xMax - 28, pager.y, 28, pager.height), "›")) Defer(() => piePage++);
            }
        }

        bool Btn(Rect r, string text) => Clicked(GUI.Button(r, text, sBtn));
        bool Btn(Rect r, string text, GUIStyle st) => Clicked(GUI.Button(r, text, st));
        bool Btn(Rect r, GUIContent c, bool on) => Clicked(GUI.Button(r, c, on ? sBtnOn : sBtn));

        /// <summary>
        /// Стиль, в который текст точно влезает: перенос строк включён, а если даже одно слово
        /// не помещается по ширине или текст не влезает по высоте — шрифт уменьшается.
        /// </summary>
        public static GUIStyle Fit(GUIStyle s, string text, float w, float h = 9999f)
        {
            var st = new GUIStyle(s) { wordWrap = true, clipping = TextClipping.Overflow };
            int fs = s.fontSize > 0 ? s.fontSize : 14;
            float inner = w - st.padding.horizontal;
            for (; fs > 8; fs--)
            {
                st.fontSize = fs;
                bool ok = true;
                foreach (var word in (text ?? "").Split(' ', '\n'))
                    if (st.CalcSize(new GUIContent(word)).x - st.padding.horizontal > inner) { ok = false; break; }
                if (ok && st.CalcHeight(new GUIContent(text), w) <= h) break;
            }
            return st;
        }

        // ================= мысли: о себе и о других =================

        public Fox thinkFox;      // о ком думаем (null — о себе, панель вкладок)
        public int thinkTab;      // 0 — о ней/нём, 1 — о нас, 2 — о друзьях

        public void ThinkAboutSelf(int tab)
        {
            thinkFox = null;
            foxTab = tab;
            infoOpen = true;
            clothesOpen = false;
            foxScroll = Vector2.zero;
        }

        public void ThinkAbout(Fox other, int tab)
        {
            thinkFox = other;
            thinkTab = tab;
            infoOpen = true;
            clothesOpen = false;
            foxScroll = Vector2.zero;
        }

        /// <summary>Насколько хорошо мы знаем лисичку (0..1): по отношениям и родству.</summary>
        public static float Knowledge(Fox me, Fox o)
        {
            if (me == null || o == null) return 0;
            if (Family.Role(me, o) != null || o.home == me.home) return 1f;
            return Mathf.Clamp01(Mathf.Abs(Social.Rel(me, o)) / 60f);
        }

        void ThinkPanelContent(Fox me)
        {
            var o = thinkFox;
            if (o == null || !G.foxes.Contains(o)) { thinkFox = null; return; }
            bool she = o.female;
            string[] titles = { o.foxName, "Мы с " + o.NameIn(NameCase.Instrumental), "Друзья " + o.NameIn(NameCase.Genitive) };
            GUILayout.BeginHorizontal();
            GUILayout.Label(titles[Mathf.Clamp(thinkTab, 0, 2)], sTitle);
            GUILayout.FlexibleSpace();
            if (Btn(new GUIContent("×", "Перестать думать об этом"), false, W(30))) Defer(() => { thinkFox = null; infoOpen = false; });
            GUILayout.EndHorizontal();
            float k01 = Knowledge(me, o);
            foxScroll = GUILayout.BeginScrollView(foxScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical();
            switch (thinkTab)
            {
                case 0:
                {
                    GUILayout.Label(o.foxName, sTitle);
                    GUILayout.Label($"{(she ? "Лисица" : "Лис")}, {o.StageName}", sBold);
                    var homeLot = G.house.LotById(o.home);
                    GUILayout.Label(k01 >= 0.1f && homeLot != null ? $"Живёт {homeLot.At}." : "Пока не знаю, где живёт.", sWrap);
                    GUILayout.Label(k01 >= 0.25f ? o.WorkDescription : "Пока не знаю, чем занимается.", sWrap);
                    if (k01 >= 0.35f) ColorLabel(o.Summary, sBig, Accent); else GUILayout.Label("Пока плохо знаю характер.", sSmall);
                    int shown = Mathf.FloorToInt(k01 * 5.01f);
                    for (int i = 0; i < 5; i++)
                    {
                        if (i >= shown) break;
                        GUILayout.Label($"{PersonalityInfo.ScaleNames[i]} — {PersonalityInfo.TierNameFor(i, o.pers.v[i], o.female).ToLower()}", sSmall);
                    }
                    if (shown < 5) GUILayout.Label($"Ещё {5 - shown} черт характера — пока загадка.", sSmall);
                    if (k01 >= 0.45f) GUILayout.Label($"Стихия — {Elements.Names[(int)o.Element].ToLower()}. Любит {Elements.Music[(int)o.Element]} и {Elements.Tv[(int)o.Element]}.", sWrap);
                    else GUILayout.Label("Пока не знаю, какая стихия ближе.", sSmall);
                    if (k01 >= 0.55f) GUILayout.Label($"Любит {Fox.WeatherName(o.LikedWeather, accusative: true)}, а {Fox.WeatherName(o.DislikedWeather, accusative: true)} не любит.", sWrap);
                    if (k01 >= 0.7f && o.wants.Count > 0)
                    {
                        GUILayout.Label("Чего хочется", sBold);
                        foreach (var wish in o.wants) GUILayout.Label("• " + wish.DisplayText, sWrap);
                    }
                    if (k01 >= 0.6f && o.Dream != null) GUILayout.Label($"Мечта всей жизни — «{o.Dream.name}», уровень {Mathf.Min(o.dreamLevel, o.Dream.Levels - 1) + 1} из {o.Dream.Levels} ({Mathf.RoundToInt(Dreams.Total(o, o.Dream) * 100)}%).", sWrap);
                    float rel = Social.Rel(me, o);
                    GUILayout.Space(6);
                    if (Btn(new GUIContent($"Наши отношения — {Social.RelLabel(rel).ToLower()} ({Mathf.RoundToInt(rel)}) →", "Открыть мысли о нас"))) Defer(() => thinkTab = 1);
                    break;
                }
                case 1:
                {
                    float rel = Social.Rel(me, o);
                    GUILayout.Label($"{me.foxName} и {o.foxName}", sTitle);
                    string role = Family.Role(me, o);
                    if (role != null) GUILayout.Label($"{o.foxName} — {role}", sBold);
                    Bar("Дружба", (rel + 100f) / 200f, rel >= 0 ? new Color(0.4f, 0.7f, 0.9f) : new Color(0.85f, 0.4f, 0.35f), 90, 280);
                    GUILayout.Label(Social.RelLabel(rel), sSmall);
                    if (Family.CanRomance(me, o))
                    {
                        float rm = Social.Rom(me, o);
                        Bar("Романтика", (rm + 100f) / 200f, new Color(0.9f, 0.45f, 0.6f), 90, 280);
                        GUILayout.Label($"Чувства — {Social.RomLabel(rm)} · искра — {Family.ChemistryLabel(Family.Chemistry(me, o))}", sSmall);
                    }
                    GUILayout.Space(6);
                    GUILayout.Label("Что помнится:", sBold);
                    int m = 0;
                    for (int i = me.memories.Count - 1; i >= 0 && m < 6; i--)
                    {
                        var memory = me.memories[i];
                        if (string.IsNullOrEmpty(memory.aboutId) ? memory.text.Contains(o.foxName) : memory.aboutId == o.id)
                        {
                            GUILayout.Label("• " + memory.text, sSmall);
                            m++;
                        }
                    }
                    if (m == 0) GUILayout.Label("Пока ничего особенного.", sSmall);
                    break;
                }
                default:
                {
                    GUILayout.Label(she ? "С кем она дружит" : "С кем он дружит", sTitle);
                    int hidden = 0, shownF = 0;
                    foreach (var kv in o.rel)
                    {
                        var x = kv.Key;
                        if (x == null || Mathf.Abs(kv.Value) < 15) continue;
                        bool known = x == me || Knowledge(me, x) >= 0.2f || k01 >= 0.8f;
                        if (!known) { hidden++; continue; }
                        shownF++;
                        GUILayout.Label($"{(x == me ? "Я" : x.foxName)} — {Social.RelLabel(kv.Value).ToLower()}", sSmall);
                    }
                    if (shownF == 0) GUILayout.Label("Пока не знаю, с кем.", sSmall);
                    if (hidden > 0) GUILayout.Label($"И ещё {hidden} — о них ты пока не знаешь.", sSmall);
                    break;
                }
            }
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) infoContentH = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndScrollView();
        }
    }
}
