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
        List<PieItem> pieRoot, pieItems;
        Vector2 pieCenter;
        string pieTitle, pieSubTitle;

        public void OpenPie(Vector3 screenPos, string title, List<PieItem> items)
        {
            pieRoot = pieItems = items;
            pieTitle = title; pieSubTitle = null;
            pieCenter = new Vector2(screenPos.x / scale, (Screen.height - screenPos.y) / scale);
            float R = items.Count > 7 ? 150 : 125;
            pieCenter.x = Mathf.Clamp(pieCenter.x, R + 110, vw - R - 110);
            pieCenter.y = Mathf.Clamp(pieCenter.y, R + 40, vh - R - 40);
            menuItems = null;
            menuOpen = true;
        }

        void Pie()
        {
            if (!menuOpen || pieItems == null) return;
            EnsureSims();
            int n = pieItems.Count;
            float R = n > 7 ? 150 : 125;
            // середина: название; нажатие — назад
            var cr = new Rect(pieCenter.x - 58, pieCenter.y - 58, 116, 116);
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
            string ctitle = pieSubTitle != null ? "← " + pieSubTitle : pieTitle;
            var ts = Fit(new GUIStyle(sBold) { alignment = TextAnchor.MiddleCenter, fontSize = 13 }, ctitle, cr.width - 16, cr.height - 16);
            ts.normal.textColor = SimsInk;
            GUI.Label(new Rect(cr.x + 8, cr.y + 8, cr.width - 16, cr.height - 16), ctitle, ts);
            if (pieSubTitle != null && GUI.Button(cr, GUIContent.none, GUIStyle.none)) Defer(() => { pieItems = pieRoot; pieSubTitle = null; });

            for (int i = 0; i < n; i++)
            {
                var it = pieItems[i];
                float a = (i / (float)n) * Mathf.PI * 2f;
                var c = pieCenter + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * R;
                string text = it.DisplayLabel;
                float w = Mathf.Min(230, sBtn.CalcSize(new GUIContent(text)).x + 18);
                var bs = Fit(sBtn, text, w, 52);
                float bh = Mathf.Max(34, bs.CalcHeight(new GUIContent(text), w) + 4);
                var r = new Rect(c.x - w / 2, c.y - bh / 2, w, bh);
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
                    if (item.sub != null) Defer(() => { pieItems = item.sub; pieSubTitle = item.label; });
                    else Defer(() => { CloseMenu(); item.act?.Invoke(); });
                }
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
