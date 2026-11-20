using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Окна игры по сети: подключиться к другу, открыть игру для друзей, выбрать семью.</summary>
    public partial class GameUI
    {
        /// <summary>Открыто окно «Играть вместе» (на титульном экране или в паузе).</summary>
        public bool netPanel;
        string netAddr, netName, netPort;
        Vector2 netScroll;

        void NetPrefs()
        {
            if (netAddr != null) return;
            netAddr = PlayerPrefs.GetString("fox_net_addr", "");
            netName = PlayerPrefs.GetString("fox_net_name", "");
            netPort = PlayerPrefs.GetInt("fox_net_port", Net.DefaultPort).ToString();
        }

        void SaveNetPrefs()
        {
            PlayerPrefs.SetString("fox_net_addr", netAddr ?? "");
            PlayerPrefs.SetString("fox_net_name", netName ?? "");
            if (int.TryParse(netPort, out int p)) PlayerPrefs.SetInt("fox_net_port", p);
            PlayerPrefs.Save();
        }

        int NetPortValue => int.TryParse(netPort, out int p) && p > 0 && p < 65536 ? p : Net.DefaultPort;

        void NetStatusLine()
        {
            var n = Net.I;
            if (n == null || string.IsNullOrEmpty(n.status)) return;
            GUILayout.Space(6);
            GUILayout.Label(n.status, new GUIStyle(sWrap) { fontSize = 14, alignment = TextAnchor.MiddleCenter });
        }

        /// <summary>Окно в меню: на титульном экране — подключиться к другу, в игре — открыть игру для друзей.</summary>
        void NetPanel()
        {
            NetPrefs();
            bool title = screen == MenuScreen.Title;
            float w = 560, h = title ? 470 : 560;
            GUILayout.BeginArea(new Rect((vw - w) / 2, Mathf.Max(10, (vh - h) / 2), w, Mathf.Min(h, vh - 20)), sPanel);
            netScroll = GUILayout.BeginScrollView(netScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Label("Играть вместе", new GUIStyle(sTitle) { fontSize = 26 });
            if (title) NetJoin(); else NetHost();
            NetStatusLine();
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (Btn("Назад", false, Hh(38))) Defer(() => netPanel = false);
            GUILayout.EndArea();
        }

        bool netManual;

        string MyNetName => string.IsNullOrEmpty(netName) ? "Друг" : netName.Trim();

        void NetJoin()
        {
            var n = Net.Ensure();
            bool busy = n.role == Net.Role.Client;
            GUILayout.Label("Чтобы зайти в город друга, попроси его выбрать в меню «Играть вместе», а затем «Открыть игру для друзей».", sWrap);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Твоё имя", sBold, W(150));
            netName = GUILayout.TextField(netName ?? "", 20, sField, W(330));
            GUILayout.EndHorizontal();
            if (busy) { if (Btn("Отмена", false, Hh(40))) Defer(() => Net.I.Stop(null)); return; }

            // открытые игры в локальной сети и Radmin VPN находятся сами
            n.searchUntil = Time.unscaledTime + 1f;
            GUILayout.Space(6);
            GUILayout.Label("Открытые игры", sBig);
            if (n.found.Count == 0)
                GUILayout.Label("Ищем игры в домашней сети и Radmin VPN" + new string('.', 1 + (int)(Time.unscaledTime * 2) % 3), sSmall, Hh(30));
            foreach (var g in n.found)
            {
                var gg = g;
                string who = g.players > 1 ? $"играют {g.players}" : "ждёт друзей";
                if (Btn(new GUIContent($"«{g.town}» у {g.host} · {who}", $"{g.address}:{g.port} — подключиться"), true, Hh(40)))
                    Defer(() =>
                    {
                        netAddr = gg.address; netPort = gg.port.ToString();
                        SaveNetPrefs();
                        Net.Ensure().StartClient(gg.address, gg.port, MyNetName);
                    });
            }
            GUILayout.Space(6);
            if (Btn(new GUIContent((netManual ? "▾ " : "▸ ") + "Ввести адрес вручную", "Если игра друга не находится сама — например, через интернет"), false, Hh(32))) Defer(() => netManual = !netManual);
            if (!netManual) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Адрес друга", sBold, W(150));
            netAddr = GUILayout.TextField(netAddr ?? "", 64, sField, W(330));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Порт", sBold, W(150));
            netPort = GUILayout.TextField(netPort ?? "", 5, sField, W(100));
            GUILayout.EndHorizontal();
            GUILayout.Label("В Radmin VPN адрес друга начинается с 26. В домашней сети — обычно с 192.168.", sSmall);
            GUILayout.Space(8);
            if (Btn("Подключиться", false, Hh(40)))
                Defer(() =>
                {
                    string addr = (netAddr ?? "").Trim();
                    if (addr.Length == 0) { Note("Впиши адрес друга."); return; }
                    SaveNetPrefs();
                    Net.Ensure().StartClient(addr, NetPortValue, MyNetName);
                });
        }

        // ================= чат =================

        public bool chatOpen;
        string chatText = "";

        /// <summary>Чат по сети: Enter — написать, Enter — отправить, Esc — закрыть. Старые строчки тают через полминуты.</summary>
        void ChatBox()
        {
            var n = Net.I;
            if (n == null || !Net.Active || (Net.IsClient && !n.worldReady)) { chatOpen = false; return; }
            var e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (!chatOpen && enter && !Typing) { chatOpen = true; chatText = ""; e.Use(); }
            else if (chatOpen && enter)
            {
                string t = chatText;
                Defer(() => n.SendChat(t));
                chatOpen = false; chatText = ""; GUI.FocusControl(null); e.Use();
            }
            else if (chatOpen && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { chatOpen = false; GUI.FocusControl(null); e.Use(); }

            const float w = 400;
            float x = vw - w - 14, y = vh - 14;
            var st = new GUIStyle(sWrap) { fontSize = 14 };
            if (chatOpen)
            {
                y -= 34;
                Fill(new Rect(x - 4, y - 2, w + 8, 34), new Color(0.08f, 0.12f, 0.16f, 0.75f));
                GUI.SetNextControlName("chat");
                chatText = GUI.TextField(new Rect(x, y + 2, w, 28), chatText ?? "", 200, sField);
                if (GUI.GetNameOfFocusedControl() != "chat") GUI.FocusControl("chat");
                y -= 4;
            }
            int shown = 0;
            for (int i = n.chat.Count - 1; i >= 0 && shown < (chatOpen ? 10 : 6); i--)
            {
                var c = n.chat[i];
                float age = Time.unscaledTime - c.time;
                float a = chatOpen ? 1f : Mathf.Clamp01((35f - age) / 5f);
                if (a <= 0) break;
                string line = c.who + ": " + c.text;
                float h = st.CalcHeight(new GUIContent(line), w - 12) + 6;
                y -= h + 2;
                Fill(new Rect(x - 4, y, w + 8, h), new Color(0.08f, 0.12f, 0.16f, 0.55f * a));
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                st.normal.textColor = c.mine ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 0.96f, 0.88f);
                GUI.Label(new Rect(x + 2, y + 3, w - 8, h), line, st);
                GUI.color = old;
                shown++;
            }
            if (!chatOpen && shown == 0) return;
        }

        void NetHost()
        {
            var n = Net.I;
            if (Net.IsClient)
            {
                GUILayout.Label("Ты в гостях у " + n.hostName + ".", sWrap);
                if (Btn("Покинуть игру друга", false, Hh(40))) Defer(() => { netPanel = false; Net.I.Stop("Ты вышел(ла) из игры друга."); });
                return;
            }
            if (!Net.IsHost)
            {
                GUILayout.Label("Друг зайдёт в этот город и будет играть за свою семью. Время, деньги и жизнь города считаются у тебя; " +
                                "сохраняется тоже у тебя. Пока друзья в игре, город не редактируется.", sWrap);
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Твоё имя", sBold, W(150));
                netName = GUILayout.TextField(netName ?? "", 20, sField, W(330));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Порт", sBold, W(150));
                netPort = GUILayout.TextField(netPort ?? "", 5, sField, W(100));
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
                if (Btn("Открыть игру для друзей", true, Hh(44)))
                    Defer(() =>
                    {
                        SaveNetPrefs();
                        var net = Net.Ensure();
                        net.myName = string.IsNullOrEmpty(netName) ? "Хозяин" : netName.Trim();
                        if (!net.StartHost(NetPortValue, out var err)) Note(err);
                    });
                GUILayout.Label("Windows может спросить, разрешить ли игре доступ к сети, — разреши (в частных сетях).", sSmall);
                return;
            }
            GUILayout.Label("Игра открыта. Скажи другу свой адрес и порт " + n.port + ":", sWrap);
            foreach (var a in Net.LocalAddresses())
                GUILayout.Label("•  " + a + (a.StartsWith("26.") ? "   (Radmin VPN)" : a.StartsWith("192.168.") || a.StartsWith("10.") ? "   (домашняя сеть)" : ""), sBold);
            GUILayout.Space(8);
            GUILayout.Label("Игроки", sBig);
            GUILayout.Label("• " + n.myName + " (ты) — " + Game.FamilyTitle(G.Household), sSmall);
            int shown = 0;
            foreach (var p in n.peers)
            {
                if (p.closed) continue;
                shown++;
                string fam = p.lot >= 0 ? Game.FamilyTitle(G.FamilyOf(p.lot)) : p.worldSent ? "выбирает семью…" : "подключается…";
                GUILayout.Label($"• {p.name} ({p.Address}): {fam}", sSmall);
            }
            if (shown == 0) GUILayout.Label("Пока никого. Ждём друзей!", sSmall);
            GUILayout.Space(8);
            if (Btn("Закрыть игру для друзей", false, Hh(40))) Defer(() => Net.I.Stop("Игра закрыта для друзей."));
        }

        /// <summary>Друг загрузил город: за какую семью играть.</summary>
        void NetPicker()
        {
            var n = Net.I;
            var homes = new List<House.Lot>();
            foreach (var l in G.house.lots) if (!l.removed && l.kind == LotKind.Home) homes.Add(l);
            float w = 560, h = Mathf.Min(vh - 40, 190 + homes.Count * 40);
            Begin(new Rect((vw - w) / 2, (vh - h) / 2, w, h));
            GUILayout.Label("За какую семью играем?", new GUIStyle(sTitle) { fontSize = 24 });
            GUILayout.Label("Город друга «" + G.townName + "». У каждого — своя семья.", sSmall);
            netScroll = GUILayout.BeginScrollView(netScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            var taken = n.TakenLots();
            foreach (var l in homes)
            {
                var fam = G.FamilyOf(l.id);
                var lot = l;
                int ti = taken.IndexOf(l.id);
                if (ti >= 0)
                {
                    GUILayout.Label($"{l.name} · {Game.FamilyTitle(fam)} — играет {n.OwnerName(l.id) ?? "друг"}", sSmall, Hh(34));
                    continue;
                }
                if (fam.Count > 0)
                {
                    if (Btn(new GUIContent($"{l.name} · {Game.FamilyTitle(fam)}", "Играть за эту семью"), false, Hh(34))) Defer(() => n.Claim(lot.id, false));
                }
                else if (Btn(new GUIContent($"{l.name} · новая семья, {G.NewFamilyMoney(l.id)} мон.", "Поселить сюда новую лисичку"), false, Hh(34))) Defer(() => n.Claim(lot.id, true));
            }
            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(n.pickerNote)) GUILayout.Label(n.pickerNote, sBold);
            if (Btn("Отключиться", false, Hh(34))) Defer(() => Net.I.Stop("Ты вышел(ла) из игры друга."));
            End();
        }

        /// <summary>Плашка «по сети» вверху экрана: кто ещё играет.</summary>
        void NetBadge()
        {
            var n = Net.I;
            if (n == null || n.role == Net.Role.None) return;
            string text;
            if (n.role == Net.Role.Host)
            {
                var names = new List<string>();
                foreach (var p in n.peers) if (!p.closed) names.Add(p.name + (p.lot < 0 ? " (выбирает)" : ""));
                text = names.Count == 0 ? "Игра открыта для друзей · ждём" : "Вместе играют " + string.Join(", ", names);
            }
            else text = "В гостях у " + n.hostName;
            if (!chatOpen) text += " · Enter — чат";
            if (G.mode == Mode.Create) text += " · пауза, пока настраиваешь лисичек";
            else if (G.speedIndex == 0 && G.wantSpeed != 0) text += " · пауза у друга";
            else if (G.speedIndex < G.wantSpeed) text += " · друг выбрал скорость помедленнее";
            var st = new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter };
            var size = st.CalcSize(new GUIContent(text));
            var r = new Rect((vw - size.x) / 2 - 12, 6, size.x + 24, 24);
            Fill(r, new Color(0.08f, 0.12f, 0.16f, 0.55f));
            var old = st.normal.textColor;
            st.normal.textColor = new Color(1f, 0.96f, 0.88f);
            GUI.Label(r, text, st);
            st.normal.textColor = old;
        }
    }
}
