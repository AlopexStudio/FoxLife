using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace FoxLife
{
    [Serializable] public class StyleList { public List<StyleData> s = new List<StyleData>(); }

    /// <summary>Всё о лисичке, кроме того, что меняется каждый кадр (положение, нужды — они идут в «тике»).</summary>
    [Serializable]
    public class NetFox
    {
        public SaveFox f;
        public int guestLot = -1;
    }

    [Serializable]
    public class NetFoxStyle
    {
        public List<StyleData> fur = new List<StyleData>();
        public List<Outfit> outfits = new List<Outfit>();
    }

    /// <summary>Постройка участка: стены, двери, окна, заборы, комнаты с крышами и цвета фасада.</summary>
    [Serializable]
    public class NetLot
    {
        public int lot;
        public List<SaveEdge> edges = new List<SaveEdge>();
        public List<SaveRoom> rooms = new List<SaveRoom>();
        public List<StyleList> surf = new List<StyleList>();
    }

    public partial class Net
    {
        // ================= общее состояние синхронизации =================

        readonly Dictionary<int, Furniture> byNid = new Dictionary<int, Furniture>();
        readonly Dictionary<Furniture, int> dirtyFurn = new Dictionary<Furniture, int>();
        readonly Dictionary<int, int> delFurn = new Dictionary<int, int>();
        readonly HashSet<Fox> dirtyFoxStyle = new HashSet<Fox>();
        readonly Dictionary<Fox, float> foxStyleTime = new Dictionary<Fox, float>();
        readonly Dictionary<int, string> lastLot = new Dictionary<int, string>();
        int tempCounter;
        float tickTimer, fullTimer, lotTimer;
        int fullCursor;
        bool wasBuilding;

        void ResetSync()
        {
            byNid.Clear(); dirtyFurn.Clear(); delFurn.Clear(); dirtyFoxStyle.Clear(); foxStyleTime.Clear(); lastLot.Clear();
            roster.Clear(); unacked.Clear();
            tempCounter = 0; moneySeqOut = 0; pendingOpen = null; waitingPlan = null; byeReason = null; pickerNote = null;
            takenLots.Clear(); takenNames.Clear();
            sentAuto = null;
        }

        static Fox FoxById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var f in G.foxes) if (f != null && f.id == id) return f;
            return null;
        }

        Furniture FindFurn(int nid, NetLink from)
        {
            if (nid == 0) return null;
            if (from != null && nid < 0 && !from.tempNid.TryGetValue(nid, out nid)) return null;
            if (byNid.TryGetValue(nid, out var f) && f != null && f.nid == nid) return f;
            byNid.Clear();
            foreach (var x in G.furniture) if (x != null && x.nid != 0) byNid[x.nid] = x;
            return byNid.TryGetValue(nid, out f) ? f : null;
        }

        static Dictionary<string, Interaction> staticInters;

        static Interaction FindInter(Furniture f, string name)
        {
            if (name == null) return null;
            if (f != null) foreach (var it in f.def.actions) if (it.name == name) return it;
            if (staticInters == null)
            {
                staticInters = new Dictionary<string, Interaction>();
                foreach (var fi in typeof(Catalog).GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (fi.FieldType == typeof(Interaction) && fi.GetValue(null) is Interaction it && it.name != null) staticInters[it.name] = it;
            }
            return staticInters.TryGetValue(name, out var s) ? s : null;
        }

        static SocialDef FindSocial(string name)
        {
            if (name == null) return null;
            if (name == Social.TakeKey.name) return Social.TakeKey;
            if (name == Social.GiveKey.name) return Social.GiveKey;
            return Social.All.Find(d => d.name == name);
        }

        static void WriteAction(BinaryWriter w, FoxAction a)
        {
            w.Write((byte)a.kind);
            WStr(w, a.label);
            w.Write(a.furn != null ? a.furn.nid : 0);
            WStr(w, a.inter != null ? a.inter.name : null);
            w.Write(a.seat != null ? a.seat.nid : 0);
            w.Write(a.seatSlot);
            WStr(w, a.other != null ? a.other.id : null);
            WStr(w, a.social != null ? a.social.name : null);
            w.Write((byte)Mathf.Clamp(a.stage, 0, 255));
            WVec(w, a.moveTarget);
            w.Write((byte)a.phoneMode);
            w.Write(a.placeLot);
        }

        FoxAction ReadAction(BinaryReader r, NetLink from)
        {
            var a = new FoxAction
            {
                kind = (ActKind)r.ReadByte(),
                label = RStr(r) ?? "",
            };
            a.furn = FindFurn(r.ReadInt32(), from);
            string inter = RStr(r);
            a.inter = FindInter(a.furn, inter);
            a.seat = FindFurn(r.ReadInt32(), from);
            a.seatSlot = r.ReadInt32();
            a.other = FoxById(RStr(r));
            a.social = FindSocial(RStr(r));
            a.stage = r.ReadByte();
            a.moveTarget = RVec(r);
            a.phoneMode = (Fox.PhoneMode)r.ReadByte();
            a.placeLot = r.ReadInt32();
            if (a.inter == null && inter != null && from == null) a.inter = new Interaction { name = inter, verb = a.label };
            return a;
        }

        /// <summary>У клиента — только то, что можно показать без ошибок: нет предмета или собеседника — просто «занят(а)».</summary>
        static FoxAction Sanitize(FoxAction a)
        {
            if (a == null) return null;
            if ((a.kind == ActKind.Social || a.kind == ActKind.BeTalkedTo || a.kind == ActKind.Phone) && a.other == null) a.kind = ActKind.Idle;
            if (a.kind == ActKind.Social && a.social == null) a.kind = ActKind.Idle;
            if (a.kind == ActKind.UseObject && (a.furn == null || a.inter == null)) a.kind = ActKind.Idle;
            return a;
        }

        // ================= хост: цикл =================

        void HostUpdate()
        {
            while (accepted.TryDequeue(out var c))
            {
                var p = new NetLink(c) { id = nextPeerId++ };
                peers.Add(p);
                SetStatus("Подключается: " + p.Address);
            }
            for (int i = peers.Count - 1; i >= 0; i--)
            {
                var p = peers[i];
                int n = 0;
                while (!p.closed && n++ < 500 && p.inbox.TryDequeue(out var m))
                {
                    try { HostHandle(p, m); }
                    catch (Exception e) { Debug.LogError("FoxLife: сеть: сообщение от " + p.name + ": " + e); }
                    finally { Applying = false; From = -1; }
                }
                if (p.closed && p.inbox.IsEmpty)
                {
                    peers.RemoveAt(i);
                    p.Close();
                    if (p.lot >= 0) G.Log($"{p.name} выходит из игры. Семья живёт дальше сама.");
                    SetStatus(p.name + " отключается" + (p.error != null ? ": " + p.error : ""));
                }
            }
            // скорость: самая медленная из выбранных, пауза у одного — пауза у всех
            int eff = HostVote();
            foreach (var p in peers) if (p.lot >= 0 && !p.closed) eff = Mathf.Min(eff, p.vote);
            if (G.ui.screen == GameUI.MenuScreen.None && G.mode != Mode.Town) G.speedIndex = eff;

            if (G.loading || G.house == null) return;
            tickTimer -= Time.unscaledDeltaTime;
            if (tickTimer <= 0f)
            {
                tickTimer = 0.1f;
                SendTicks();
            }
            fullTimer -= Time.unscaledDeltaTime;
            if (fullTimer <= 0f)
            {
                fullTimer = 0.5f;
                FullPass();
            }
        }

        int HostVote() => G.ui.screen != GameUI.MenuScreen.None || G.mode == Mode.Town || G.mode == Mode.Create || G.loading ? 0 : G.wantSpeed;

        void HostHandle(NetLink p, byte[] m)
        {
            var r = new BinaryReader(new MemoryStream(m));
            var t = (Msg)r.ReadByte();
            switch (t)
            {
                case Msg.Hello:
                {
                    int proto = r.ReadInt32();
                    p.name = RStr(r) ?? "Друг";
                    if (proto != Protocol) { Refuse(p, "Разные версии игры. Возьмите одну и ту же сборку."); return; }
                    if (G.loading || G.house == null || G.Household.Count == 0) { Refuse(p, "Хозяин ещё не в игре. Попробуй чуть позже."); return; }
                    p.Send(Pack(Msg.Welcome, w => { w.Write(p.id); WStr(w, myName); WStr(w, G.townName); }));
                    SendWorld(p);
                    G.Log($"{p.name} заходит в гости по сети.");
                    break;
                }
                case Msg.Ready: break;
                case Msg.Claim: HandleClaim(p, r.ReadInt32(), r.ReadBoolean()); break;
                case Msg.Cmd:
                {
                    var fox = FoxById(RStr(r));
                    var a = ReadAction(r, p);
                    if (fox == null || fox.home != p.lot || p.lot < 0) return;
                    if (a.kind == ActKind.UseObject && (a.furn == null || a.inter == null)) return;
                    if ((a.kind == ActKind.Social || a.kind == ActKind.Phone) && a.other == null) return;
                    fox.Command(a);
                    break;
                }
                case Msg.Op: HandleOp(p, RStr(r), RStr(r), RStr(r), RStr(r), RStr(r)); break;
                case Msg.Settings:
                    p.autonomy = r.ReadBoolean();
                    p.autoOutfits = r.ReadBoolean();
                    p.vote = r.ReadByte();
                    break;
                case Msg.Money:
                {
                    float d = r.ReadSingle();
                    int seq = r.ReadInt32();
                    if (p.lot >= 0) G.lotMoney[p.lot] = G.MoneyOf(p.lot) + d;
                    p.moneySeq = seq;
                    break;
                }
                case Msg.Furn: ApplyFurn(r, p); break;
                case Msg.FurnDel: ApplyFurnDel(r, p); break;
                case Msg.LotState: ApplyLot(r, p, m); break;
                case Msg.Terrain: ApplyTerrain(r); Broadcast(m, p.id); break;
                case Msg.Prop: ApplyProp(r); Broadcast(m, p.id); break;
                case Msg.FoxStyle: ApplyFoxStyle(r, p); break;
                case Msg.FoxEdit: ApplyFoxEdit(r, p); break;
                case Msg.Chat: HostChat(p, RStr(r)); break;
                case Msg.Bye: p.closed = true; break;
            }
        }

        void Refuse(NetLink p, string why)
        {
            p.Send(Pack(Msg.Refused, w => WStr(w, why)));
            p.Close();
        }

        void SendWorld(NetLink p)
        {
            string json = SaveSystem.ToJson(G, false);
            var gz = Gzip(json);
            p.Send(Pack(Msg.World, w => { w.Write(gz.Length); w.Write(gz); }));
            p.worldSent = true;
            p.sentRoster = null;
            p.sentFull.Clear(); p.sentAct.Clear(); p.sentBubble.Clear();
            SetStatus($"{p.name}: отправлен город ({gz.Length / 1024} КБ)");
        }

        void HandleClaim(NetLink p, int lot, bool create)
        {
            if (p.lot >= 0) return;
            var l = G.house.LotById(lot);
            string err = null;
            if (l == null || l.removed || l.kind != LotKind.Home) err = "Этот участок не для жилья.";
            else if (TakenLots().Contains(lot)) err = "За эту семью уже играют.";
            else if (create)
            {
                if (G.FamilyOf(lot).Count > 0) err = "Участок уже занят.";
                else if (G.CreateFamily(lot) == null) err = "Не получилось создать семью.";
            }
            else if (G.FamilyOf(lot).Count == 0) err = "Здесь никто не живёт.";
            if (err != null) { p.Send(Pack(Msg.Claimed, w => { w.Write(-1); WStr(w, err); })); return; }
            p.lot = lot;
            p.Send(Pack(Msg.Claimed, w => { w.Write(lot); WStr(w, ""); w.Write(G.MoneyOf(lot)); }));
            G.Log($"{p.name} играет за семью: {Game.FamilyTitle(G.FamilyOf(lot))} ({l.name}).");
        }

        void HandleOp(NetLink p, string fid, string op, string a1, string a2, string a3)
        {
            var fox = FoxById(fid);
            if (fox == null || p.lot < 0 || fox.home != p.lot) return;
            int I1() { int.TryParse(a1, out int v); return v; }
            switch (op)
            {
                case "cancel":
                {
                    int idx = I1();
                    var target = idx < 0 ? fox.current : idx < fox.queue.Count ? fox.queue[idx] : null;
                    if (target != null && (a2 == null || target.label == a2)) fox.Cancel(target);
                    break;
                }
                case "takejob": { var c = Careers.Get(a1); if (c != null) fox.TakeJob(c); break; }
                case "quitjob": fox.QuitJob(a1 != "0"); break;
                case "perk": { var pk = Array.Find(Wants.Perks, x => x.id == a1); if (pk != null) fox.BuyPerk(pk); break; }
                case "item":
                {
                    int.TryParse(a2, out int slot);
                    fox.SetItem(I1(), (ClothSlot)slot, a3);
                    break;
                }
                case "knock": fox.CommandKnock(I1(), null); break;
                case "invite": { var t = FoxById(a1); if (t != null) fox.InviteOver(t); break; }
                case "goout":
                {
                    var t = FoxById(a1);
                    int.TryParse(a2, out int li);
                    var l = G.house.LotById(li);
                    if (t != null && l != null) fox.GoOutTogether(t, l);
                    break;
                }
                case "plandone": if (fox.current != null) fox.current.netDone = true; break;
                case "addfox": case "removefox": HostCasOp(p, fox, op); break;
            }
        }

        // ================= хост: лисички =================

        static ulong Hash(byte[] b)
        {
            ulong h = 14695981039346656037UL;
            foreach (var x in b) { h ^= x; h *= 1099511628211UL; }
            return h;
        }

        static byte[] ActBytes(Fox f)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(f.current != null);
            if (f.current != null) WriteAction(w, f.current);
            WStr(w, f.heldKind);
            w.Write(f.atWork);
            WStr(w, f.side != null && f.side.other != null ? f.side.other.id : null);
            w.Write((sbyte)Mathf.Clamp(f.wornOutfit, -1, 100));
            int qn = Mathf.Min(f.queue.Count, 8);
            w.Write((byte)qn);
            for (int i = 0; i < qn; i++) WriteAction(w, f.queue[i]);
            w.Flush();
            return ms.ToArray();
        }

        void SendTicks()
        {
            var list = new List<Fox>();
            foreach (var f in G.foxes) if (f != null) list.Add(f);
            var ids = new List<string>(list.Count);
            foreach (var f in list) ids.Add(f.id);
            var acts = new byte[list.Count][];
            var hashes = new ulong[list.Count];
            bool any = false;
            foreach (var p in peers) if (p.worldSent && !p.closed) any = true;
            if (!any) return;
            for (int i = 0; i < list.Count; i++) { acts[i] = ActBytes(list[i]); hashes[i] = Hash(acts[i]); }
            var taken = TakenLots();
            foreach (var p in peers)
            {
                if (!p.worldSent || p.closed) continue;
                if (p.Pending > 400) continue; // друг не успевает принимать — пропустим кадр
                if (p.sentRoster == null || !SameList(p.sentRoster, ids))
                {
                    foreach (var f in list) if (!p.sentFull.ContainsKey(f.id)) SendFull(p, f, NetFoxJson(f));
                    p.Send(Pack(Msg.Roster, w => { w.Write(ids.Count); foreach (var id in ids) WStr(w, id); }));
                    p.sentRoster = new List<string>(ids);
                    var gone = new List<Fox>();
                    foreach (var k in p.sentAct.Keys) if (k == null || !list.Contains(k)) gone.Add(k);
                    foreach (var k in gone) { p.sentAct.Remove(k); p.sentBubble.Remove(k); }
                }
                var pp = p;
                p.Send(Pack(Msg.Tick, w =>
                {
                    w.Write(G.minutes);
                    w.Write((byte)G.speedIndex);
                    w.Write((byte)G.weather.kind);
                    w.Write(pp.lot >= 0 ? G.MoneyOf(pp.lot) : 0f);
                    w.Write(pp.moneySeq);
                    w.Write((byte)taken.Count);
                    foreach (var lot in taken) { w.Write(lot); WStr(w, OwnerName(lot)); }
                    w.Write((ushort)list.Count);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var f = list[i];
                        var tr = f.transform;
                        WVec(w, tr.position);
                        w.Write((ushort)(Mathf.Repeat(tr.eulerAngles.y, 360f) / 360f * 65535f));
                        byte fl = (byte)((f.walking ? 1 : 0) | (f.running ? 2 : 0) | (f.sleeping ? 4 : 0) | (f.carried ? 8 : 0) | (f.atWork ? 16 : 0));
                        w.Write(fl);
                        w.Write((byte)f.pose);
                        for (int k = 0; k < 5; k++) w.Write((byte)Mathf.RoundToInt(Mathf.Clamp(f.needs[k], 0, 100) * 2.55f));
                        if (!pp.sentAct.TryGetValue(f, out var old) || old != hashes[i])
                        {
                            w.Write(true);
                            w.Write(acts[i].Length);
                            w.Write(acts[i]);
                            pp.sentAct[f] = hashes[i];
                        }
                        else w.Write(false);
                        bool nb = f.bubbleTimer > 0.05f && !string.IsNullOrEmpty(f.bubble) && (!pp.sentBubble.TryGetValue(f, out var ob) || ob != f.sayCount.ToString());
                        w.Write(nb);
                        if (nb) { WStr(w, f.bubble); w.Write(f.bubbleTimer); pp.sentBubble[f] = f.sayCount.ToString(); }
                    }
                }));
            }
        }

        static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Описание лисички без быстро меняющихся мелочей: если ничего важного не изменилось — строка та же.</summary>
        static string NetFoxJson(Fox f)
        {
            var sf = SaveSystem.FoxToSave(f, true);
            sf.needs = null;
            sf.x = sf.y = sf.z = sf.yaw = 0;
            foreach (var m in sf.moodlets) m.minutesLeft = Mathf.Ceil(m.minutesLeft / 30f) * 30f;
            if (sf.skillXp != null) for (int i = 0; i < sf.skillXp.Length; i++) sf.skillXp[i] = Mathf.Floor(sf.skillXp[i]);
            sf.performance = Mathf.Round(sf.performance);
            if (sf.pregnantMinutes >= 0) sf.pregnantMinutes = Mathf.Ceil(sf.pregnantMinutes / 30f) * 30f;
            sf.fearCooldown = Mathf.Ceil(sf.fearCooldown / 30f) * 30f;
            return JsonUtility.ToJson(new NetFox { f = sf, guestLot = f.guestLot });
        }

        void SendFull(NetLink p, Fox f, string json)
        {
            var gz = Gzip(json);
            p.Send(Pack(Msg.FoxFull, w => { WStr(w, f.id); w.Write(gz.Length); w.Write(gz); }));
            p.sentFull[f.id] = json;
        }

        void FullPass()
        {
            bool any = false;
            foreach (var p in peers) if (p.worldSent && !p.closed && p.sentRoster != null) any = true;
            if (!any || G.foxes.Count == 0) return;
            var set = new HashSet<Fox>();
            int n = Mathf.Min(6, G.foxes.Count);
            for (int i = 0; i < n; i++) set.Add(G.foxes[(fullCursor++) % G.foxes.Count]);
            foreach (var f in G.foxes) if (f != null && (f.home == House.PlayerLotId || PeerOf(f.home) != null)) set.Add(f);
            foreach (var f in set)
            {
                if (f == null) continue;
                string json = NetFoxJson(f);
                foreach (var p in peers)
                {
                    if (!p.worldSent || p.closed || p.sentRoster == null) continue;
                    if (!p.sentFull.TryGetValue(f.id, out var old) || old != json) SendFull(p, f, json);
                }
            }
        }

        void ApplyFoxStyle(BinaryReader r, NetLink p)
        {
            var fox = FoxById(RStr(r));
            var json = Gunzip(r.ReadBytes(r.ReadInt32()));
            if (fox == null || fox.home != p.lot) return;
            var d = JsonUtility.FromJson<NetFoxStyle>(json);
            Applying = true; From = p.id;
            try { SaveSystem.ApplyNetStyle(fox, d, true); }
            finally { Applying = false; From = -1; }
        }

        // ================= хост: письма друзьям =================

        public void LogTo(int lot, string text, bool coin = false)
        {
            var p = PeerOf(lot);
            if (p == null || !p.worldSent) return;
            p.Send(Pack(Msg.Log, w => { WStr(w, text); w.Write(coin); }));
        }

        public void LogAll(string text)
        {
            if (role != Role.Host) return;
            var m = Pack(Msg.Log, w => { WStr(w, text); w.Write(false); });
            foreach (var p in peers) if (p.lot >= 0) p.Send(m);
        }

        /// <summary>Деньги семьи друга (у хоста они лежат в lotMoney): заработок, траты, счета.</summary>
        public void RemoteMoney(int lot, int amount, string reason)
        {
            if (amount == 0 || PeerOf(lot) == null) return;
            G.lotMoney[lot] = G.MoneyOf(lot) + amount;
            LogTo(lot, $"{reason}: {(amount > 0 ? "+" : "")}{amount} мон.", amount > 0);
        }

        public void SendOpen(Fox f, int kind)
        {
            var p = PeerOf(f.home);
            if (p == null) return;
            p.Send(Pack(Msg.Open, w => { WStr(w, f.id); w.Write((byte)kind); }));
        }

        public static void Sfx(int kind, Vector3 pos)
        {
            if (!IsHost || Applying) return;
            I.Broadcast(Pack(Msg.Sfx, w => { w.Write((byte)kind); WVec(w, pos); }));
        }

        // ================= клиент: цикл =================

        readonly List<Fox> roster = new List<Fox>();
        readonly List<(int seq, float d)> unacked = new List<(int, float)>();
        int moneySeqOut;
        float lastMoney;
        string byeReason;
        public string pickerNote;
        bool selectPending;
        (bool a, bool o, int v)? sentAuto;
        class OpenReq { public Fox fox; public int kind; public float time; }
        OpenReq pendingOpen, waitingPlan;

        void ClientUpdate()
        {
            if (connecting)
            {
                if (connectError != null)
                {
                    var e = connectError;
                    connectError = null;
                    connecting = false;
                    role = Role.None;
                    SetStatus(e);
                    return;
                }
                if (connected != null)
                {
                    server = new NetLink(connected);
                    connected = null;
                    connecting = false;
                    server.Send(Pack(Msg.Hello, w => { w.Write(Protocol); WStr(w, myName); }));
                    SetStatus("Подключились! Ждём город…");
                }
                return;
            }
            if (server == null) return;
            FlushMoney();
            int n = 0;
            while (!loadingWorld && n++ < 3000 && server != null && server.inbox.TryDequeue(out var m))
            {
                try { ClientHandle(m); }
                catch (Exception e) { Debug.LogError("FoxLife: сеть: сообщение от хоста: " + e); }
                finally { Applying = false; }
            }
            if (server == null) return;
            if (server.closed && server.inbox.IsEmpty && !loadingWorld)
            {
                string why = byeReason ?? (server.error != null ? "Связь с хозяином потеряна: " + server.error : "Связь с хозяином потеряна.");
                server = null;
                Stop(why);
                return;
            }
            if (!worldReady || G.loading) return;

            if (myLot >= 0)
            {
                FlushMoney();
                if (selectPending && G.Household.Count > 0)
                {
                    selectPending = false;
                    var f = G.Household[0];
                    G.Select(f);
                    var l = G.house.LotById(myLot);
                    if (l != null) { G.cam.Focus(GridMap.Center(l.entry), 18f); G.cam.dist = 18f; G.cam.pitch = 48f; }
                    lastLot[myLot] = LotJson(myLot);
                    if (claimCreate)
                    {
                        claimCreate = false;
                        G.SetMode(Mode.Create);
                        G.ui.Note("Новая семья! Настрой лисичку: имя, характер, окрас — и добавь ещё, если хочешь.");
                    }
                }
            }
            int vote = G.ui.screen != GameUI.MenuScreen.None || G.mode == Mode.Create ? 0 : G.wantSpeed;
            var now = (G.autonomy, G.autoOutfits, vote);
            if (myLot >= 0 && (sentAuto == null || sentAuto.Value != now))
            {
                sentAuto = now;
                server.Send(Pack(Msg.Settings, w => { w.Write(now.autonomy); w.Write(now.autoOutfits); w.Write((byte)now.vote); }));
            }
            UpdatePlans();
            if (myLot >= 0) CasUpdate();
        }

        /// <summary>Шкаф и мольберт: хост ждёт, пока игрок выберет одежду или закончит план дома.</summary>
        void UpdatePlans()
        {
            if (pendingOpen != null)
            {
                var o = pendingOpen;
                var it = o.fox != null && o.fox.current != null ? o.fox.current.inter : null;
                bool ready = it != null && (o.kind == 0 ? it.openWardrobe : it.openBuild);
                if (o.fox == null || G.selected != o.fox || Time.unscaledTime - o.time > 3f)
                {
                    pendingOpen = null;
                    if (o.fox != null) SendOp(o.fox, "plandone");
                }
                else if (ready)
                {
                    pendingOpen = null;
                    if (o.kind == 0) { G.ui.clothesOpen = true; G.ui.outfitTab = 0; }
                    else G.SetMode(Mode.Build);
                    waitingPlan = o;
                }
            }
            if (waitingPlan != null)
            {
                var o = waitingPlan;
                bool open = o.fox != null && G.selected == o.fox && (o.kind == 0 ? G.ui.clothesOpen && G.mode == Mode.Live : G.mode == Mode.Build || G.mode == Mode.Style);
                if (!open)
                {
                    waitingPlan = null;
                    if (o.fox != null) SendOp(o.fox, "plandone");
                }
            }
        }

        void ClientHandle(byte[] m)
        {
            var r = new BinaryReader(new MemoryStream(m));
            var t = (Msg)r.ReadByte();
            switch (t)
            {
                case Msg.Welcome:
                    myId = r.ReadInt32();
                    hostName = RStr(r) ?? "";
                    SetStatus($"Загружаем город «{RStr(r)}»…");
                    break;
                case Msg.Refused:
                    byeReason = RStr(r) ?? "Хозяин не пустил в игру.";
                    server.closed = true;
                    break;
                case Msg.Bye:
                    byeReason = RStr(r) ?? "Хозяин закрыл игру.";
                    break;
                case Msg.World:
                {
                    int len = r.ReadInt32();
                    var gz = r.ReadBytes(len);
                    worldBytes = len;
                    string json = Gunzip(gz);
                    loadingWorld = true;
                    G.StartCoroutine(LoadWorld(json));
                    break;
                }
                case Msg.Tick: ApplyTick(r); break;
                case Msg.Chat: ClientChat(RStr(r), RStr(r)); break;
                case Msg.Roster: ApplyRoster(r); break;
                case Msg.FoxFull: ApplyFoxFull(r); break;
                case Msg.Claimed:
                {
                    int lot = r.ReadInt32();
                    string err = RStr(r);
                    if (lot < 0) { pickerNote = err; break; }
                    float money = r.ReadSingle();
                    myLot = lot;
                    House.PlayerLotId = lot;
                    G.money = money; lastMoney = money; unacked.Clear();
                    G.started = true;
                    G.house.focusLot = -2;
                    selectPending = true;
                    sentAuto = null;
                    casSent.Clear();
                    var l = G.house.LotById(lot);
                    G.Log($"Играем вместе с {hostName}! Наш дом: {(l != null ? l.name : "")}.");
                    break;
                }
                case Msg.Furn: ApplyFurn(r, null); break;
                case Msg.FurnDel: ApplyFurnDel(r, null); break;
                case Msg.NidMap:
                {
                    int temp = r.ReadInt32(), real = r.ReadInt32();
                    foreach (var f in G.furniture) if (f != null && f.nid == temp) { f.nid = real; byNid[real] = f; break; }
                    break;
                }
                case Msg.LotState: ApplyLot(r, null, m); break;
                case Msg.Terrain: ApplyTerrain(r); break;
                case Msg.Prop: ApplyProp(r); break;
                case Msg.Log:
                {
                    string text = RStr(r);
                    bool coin = r.ReadBoolean();
                    if (text != null) G.Log(text);
                    if (coin && G.sound != null) G.sound.Coin();
                    break;
                }
                case Msg.Open:
                {
                    var fox = FoxById(RStr(r));
                    int kind = r.ReadByte();
                    if (fox != null) pendingOpen = new OpenReq { fox = fox, kind = kind, time = Time.unscaledTime };
                    break;
                }
                case Msg.Sfx:
                {
                    int k = r.ReadByte();
                    var pos = RVec(r);
                    if (G.sound != null) { if (k == 0) G.sound.Knock(pos); else G.sound.Doorbell(pos); }
                    break;
                }
            }
        }

        IEnumerator LoadWorld(string json)
        {
            G.ui.screen = GameUI.MenuScreen.None;
            G.loading = true;
            G.loadingText = "Загружаем город друга…";
            G.loadingProgress = 0.02f;
            yield return null;
            Applying = true;
            bool ok = false;
            var it = SaveSystem.LoadAsync(G, res => ok = res, null, json);
            while (true)
            {
                if (role != Role.Client) { Applying = false; yield break; } // отключились посреди загрузки
                bool more;
                try { more = it.MoveNext(); }
                catch (Exception e) { Debug.LogError("FoxLife: сеть: загрузка города: " + e); ok = false; break; }
                if (!more) break;
                yield return it.Current;
            }
            Applying = false;
            G.loading = false;
            loadingWorld = false;
            if (!ok || role != Role.Client) { if (role == Role.Client) Stop("Не получилось загрузить город."); yield break; }
            // пока не выбрали семью — смотрим на город со стороны
            int view = G.house.lots.FindIndex(l => l.kind != LotKind.Home && !l.removed);
            House.PlayerLotId = view >= 0 ? view : 0;
            G.Select(null);
            G.mode = Mode.Live;
            G.started = false;
            G.house.focusLot = -2;
            G.cam.Focus(new Vector3(20f, 0, -5f), 999f);
            G.cam.dist = 70f; G.cam.pitch = 55f;
            roster.Clear();
            worldReady = true;
            server?.Send(Pack(Msg.Ready, null));
            SetStatus("Город загружен. Выбери семью!");
        }

        public void Claim(int lot, bool create)
        {
            pickerNote = null;
            claimCreate = create;
            server?.Send(Pack(Msg.Claim, w => { w.Write(lot); w.Write(create); }));
        }

        /// <summary>Свои траты и находки (стройка, хитрости) — хосту, пока их не затёр снимок от него.</summary>
        void FlushMoney()
        {
            if (myLot < 0 || server == null || !worldReady || Mathf.Abs(G.money - lastMoney) < 0.001f) return;
            float d = G.money - lastMoney;
            int seq = ++moneySeqOut;
            unacked.Add((seq, d));
            server.Send(Pack(Msg.Money, w => { w.Write(d); w.Write(seq); }));
            lastMoney = G.money;
        }

        void ApplyMoney(float money, int ack)
        {
            if (myLot < 0) return;
            FlushMoney();
            unacked.RemoveAll(u => u.seq <= ack);
            float pend = 0; foreach (var u in unacked) pend += u.d;
            G.money = money + pend;
            lastMoney = G.money;
        }

        void ApplyTick(BinaryReader r)
        {
            float minutes = r.ReadSingle();
            int speed = r.ReadByte();
            var wk = (WeatherKind)r.ReadByte();
            float money = r.ReadSingle();
            int ack = r.ReadInt32();
            int tn = r.ReadByte();
            takenLots.Clear(); takenNames.Clear();
            for (int i = 0; i < tn; i++) { takenLots.Add(r.ReadInt32()); takenNames.Add(RStr(r) ?? "?"); }
            G.minutes = minutes;
            G.speedIndex = Mathf.Clamp(speed, 0, Game.Speeds.Length - 1);
            if (G.weather != null) { G.weather.kind = wk; G.weather.hoursLeft = 99f; }
            ApplyMoney(money, ack);
            int n = r.ReadUInt16();
            var needs = new float[5];
            for (int i = 0; i < n; i++)
            {
                var f = i < roster.Count ? roster[i] : null;
                var pos = RVec(r);
                float yaw = r.ReadUInt16() / 65535f * 360f;
                int fl = r.ReadByte();
                var pose = (Pose)r.ReadByte();
                for (int k = 0; k < 5; k++) needs[k] = r.ReadByte() / 2.55f;
                byte[] act = null;
                if (r.ReadBoolean()) act = r.ReadBytes(r.ReadInt32());
                string bubble = null; float bt = 0;
                if (r.ReadBoolean()) { bubble = RStr(r); bt = r.ReadSingle(); }
                if (f == null) continue;
                f.NetSnap(pos, yaw, fl, pose, needs);
                if (act != null) ApplyAct(f, act);
                if (bubble != null) f.Say(bubble, bt);
            }
        }

        void ApplyAct(Fox f, byte[] b)
        {
            var r = new BinaryReader(new MemoryStream(b));
            var cur = r.ReadBoolean() ? Sanitize(ReadAction(r, null)) : null;
            string held = RStr(r);
            bool atWork = r.ReadBoolean();
            var side = FoxById(RStr(r));
            int worn = r.ReadSByte();
            int qn = r.ReadByte();
            var q = new List<FoxAction>();
            for (int i = 0; i < qn; i++) { var a = Sanitize(ReadAction(r, null)); if (a != null) q.Add(a); }
            f.NetAction(cur, q, held, atWork, side, worn);
        }

        void ApplyRoster(BinaryReader r)
        {
            int n = r.ReadInt32();
            var ids = new List<string>(n);
            for (int i = 0; i < n; i++) ids.Add(RStr(r));
            roster.Clear();
            var keep = new HashSet<Fox>();
            foreach (var id in ids) { var f = FoxById(id); roster.Add(f); if (f != null) keep.Add(f); }
            foreach (var f in new List<Fox>(G.foxes)) if (f != null && !keep.Contains(f)) G.NetDropFox(f);
        }

        void ApplyFoxFull(BinaryReader r)
        {
            string id = RStr(r);
            var json = Gunzip(r.ReadBytes(r.ReadInt32()));
            var nf = JsonUtility.FromJson<NetFox>(json);
            if (nf == null || nf.f == null) return;
            var fox = FoxById(id);
            Applying = true;
            try
            {
                if (fox == null) fox = SaveSystem.SpawnNetFox(G, nf.f);
                else if (CasEditing(fox)) return; // друг как раз правит её в «Лисичках»
                bool recentEdit = foxStyleTime.TryGetValue(fox, out var te) && Time.unscaledTime - te < 4f;
                SaveSystem.ApplyNetFox(fox, nf.f, !recentEdit);
                fox.guestLot = nf.guestLot;
            }
            finally { Applying = false; }
        }

        // ================= мебель =================

        public static void FurnRegistered(Furniture f)
        {
            if (!Active || f == null || f.isGhost) return;
            if (IsClient && Applying) return;
            if (IsHost && f.nid == 0) f.nid = ++I.nidCounter;
            if (IsClient && f.nid == 0) f.nid = --I.tempCounter;
            I.delFurn.Remove(f.nid);
            I.dirtyFurn[f] = IsHost && Applying ? From : -1;
            I.byNid[f.nid] = f;
        }

        public static void FurnUnregistered(Furniture f)
        {
            if (!Active || f == null || f.nid == 0 || f.isGhost) return;
            if (IsClient && Applying) return;
            I.dirtyFurn.Remove(f);
            I.delFurn[f.nid] = IsHost && Applying ? From : -1;
        }

        public static void StyleChanged(Styleable st)
        {
            if (I == null || I.role == Role.None || st == null) return;
            if (IsClient && Applying) return;
            var f = st.GetComponentInParent<Furniture>();
            if (f != null)
            {
                if (!f.isGhost && f.nid != 0) I.dirtyFurn[f] = IsHost && Applying ? From : -1;
                return;
            }
            if (IsClient && I.worldReady)
            {
                var fox = st.GetComponentInParent<Fox>();
                if (fox != null && Game.IsPlayer(fox)) { I.dirtyFoxStyle.Add(fox); I.foxStyleTime[fox] = Time.unscaledTime; }
            }
        }

        byte[] FurnMsg(Furniture f) => Pack(Msg.Furn, w =>
        {
            w.Write(f.nid);
            WStr(w, f.def.id);
            w.Write(f.anchor.x); w.Write(f.anchor.y);
            w.Write(f.rot); w.Write(f.level);
            w.Write(f.fineYaw);
            w.Write(f.lightLevel);
            w.Write(f.lightColor.r); w.Write(f.lightColor.g); w.Write(f.lightColor.b); w.Write(f.lightColor.a);
            w.Write(f.boughtAt);
            WStr(w, JsonUtility.ToJson(new StyleList { s = SaveSystem.AllStylesOf(f) }));
        });

        void ApplyFurn(BinaryReader r, NetLink from)
        {
            int nid = r.ReadInt32();
            var def = Catalog.Get(RStr(r));
            var a = new Vector2Int(r.ReadInt32(), r.ReadInt32());
            int rot = r.ReadInt32(), level = r.ReadInt32();
            float yaw = r.ReadSingle(), light = r.ReadSingle();
            var col = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float bought = r.ReadSingle();
            var styles = JsonUtility.FromJson<StyleList>(RStr(r) ?? "{}");
            if (def == null || level < 0 || level >= House.Levels) return;
            var f = FindFurn(nid, from);
            if (from != null)
            {
                // друг строит только у себя
                var lt = G.house.LotAt(a);
                if ((lt == null || lt.id != from.lot) || (f != null && f.lot != from.lot)) return;
            }
            Applying = true; From = from != null ? from.id : -1;
            try
            {
                f = G.NetUpsertFurniture(f, def, a, rot, level, yaw);
                if (f == null) return;
                if (from == null) { f.nid = nid; byNid[nid] = f; }
                else if (nid < 0 && !from.tempNid.ContainsKey(nid))
                {
                    from.tempNid[nid] = f.nid;
                    int real = f.nid;
                    from.Send(Pack(Msg.NidMap, w => { w.Write(nid); w.Write(real); }));
                }
                if (styles != null && styles.s != null) SaveSystem.ApplyAllStylesTo(f, styles.s);
                f.lightLevel = light; f.lightColor = col; f.boughtAt = bought;
                f.ApplyLight();
            }
            finally { Applying = false; From = -1; }
        }

        void ApplyFurnDel(BinaryReader r, NetLink from)
        {
            int nid = r.ReadInt32();
            var f = FindFurn(nid, from);
            if (f == null) return;
            if (from != null && f.lot != from.lot && f.def.id != "plate") return;
            Applying = true; From = from != null ? from.id : -1;
            try { G.RemoveFurniture(f); }
            finally { Applying = false; From = -1; }
        }

        // ================= участки: стены, комнаты, крыши, фасад =================

        public string LotJson(int id)
        {
            var d = G.NetLotData(id);
            return d != null ? JsonUtility.ToJson(d) : null;
        }

        void ApplyLot(BinaryReader r, NetLink from, byte[] raw)
        {
            int id = r.ReadInt32();
            var json = Gunzip(r.ReadBytes(r.ReadInt32()));
            if (from != null && id != from.lot) return;
            var d = JsonUtility.FromJson<NetLot>(json);
            if (d == null) return;
            Applying = true; From = from != null ? from.id : -1;
            try { G.NetApplyLot(d); }
            finally { Applying = false; From = -1; }
            Debug.Log($"FoxLife: сеть: участок {d.lot} перестроен по сети, стен и заборов {d.edges.Count}");
            if (from != null) Broadcast(raw, from.id);
        }

        byte[] LotMsg(int id, string json)
        {
            var gz = Gzip(json);
            return Pack(Msg.LotState, w => { w.Write(id); w.Write(gz.Length); w.Write(gz); });
        }

        // ================= земля и декорации =================

        public static void SendTerrain(List<(int idx, Vector2 v)> cols, List<(Vector3 p, int seed)> remF, List<(Vector3 p, int seed)> addF,
            List<(Vector3 p, float s, bool pine)> remT, List<(Vector3 p, float s, bool pine)> addT)
        {
            if (!Active || Applying) return;
            if (IsClient && (!I.worldReady || I.myLot < 0)) return;
            var m = Pack(Msg.Terrain, w =>
            {
                w.Write(cols.Count);
                foreach (var c in cols) { w.Write(c.idx); w.Write(c.v.x); w.Write(c.v.y); }
                void Fl(List<(Vector3 p, int seed)> l) { w.Write(l.Count); foreach (var x in l) { WVec(w, x.p); w.Write(x.seed); } }
                void Tr(List<(Vector3 p, float s, bool pine)> l) { w.Write(l.Count); foreach (var x in l) { WVec(w, x.p); w.Write(x.s); w.Write(x.pine); } }
                Fl(remF); Fl(addF); Tr(remT); Tr(addT);
            });
            I.SendOut(m, -1);
        }

        void ApplyTerrain(BinaryReader r)
        {
            int n = r.ReadInt32();
            var cols = new List<(int, Vector2)>(n);
            for (int i = 0; i < n; i++) cols.Add((r.ReadInt32(), new Vector2(r.ReadSingle(), r.ReadSingle())));
            List<(Vector3, int)> Fl() { int k = r.ReadInt32(); var l = new List<(Vector3, int)>(k); for (int i = 0; i < k; i++) l.Add((RVec(r), r.ReadInt32())); return l; }
            List<(Vector3, float, bool)> Tr() { int k = r.ReadInt32(); var l = new List<(Vector3, float, bool)>(k); for (int i = 0; i < k; i++) l.Add((RVec(r), r.ReadSingle(), r.ReadBoolean())); return l; }
            var remF = Fl(); var addF = Fl(); var remT = Tr(); var addT = Tr();
            Applying = true;
            try { G.NetApplyTerrain(cols, remF, addF, remT, addT); }
            finally { Applying = false; }
        }

        public static void PropRemoved(bool tree, string key, Vector3 pos)
        {
            if (!Active || Applying) return;
            if (IsClient && (!I.worldReady || I.myLot < 0)) return;
            I.SendOut(Pack(Msg.Prop, w => { w.Write(tree); WStr(w, key); WVec(w, pos); }), -1);
        }

        void ApplyProp(BinaryReader r)
        {
            bool tree = r.ReadBoolean();
            string key = RStr(r);
            var pos = RVec(r);
            Applying = true;
            try { G.NetRemoveProp(tree, key, pos); }
            finally { Applying = false; }
        }

        // ================= клиент: приказы =================

        public void SendCommand(Fox f, FoxAction a)
        {
            if (server == null || f == null || a == null) return;
            server.Send(Pack(Msg.Cmd, w => { WStr(w, f.id); WriteAction(w, a); }));
        }

        public void SendOp(Fox f, string op, string a1 = null, string a2 = null, string a3 = null)
        {
            if (server == null || f == null) return;
            server.Send(Pack(Msg.Op, w => { WStr(w, f.id); WStr(w, op); WStr(w, a1); WStr(w, a2); WStr(w, a3); }));
        }

        // ================= отправка изменений (раз в кадр) =================

        void FlushChanges()
        {
            if (role == Role.Client && (!worldReady || server == null || G.loading)) { dirtyFurn.Clear(); delFurn.Clear(); dirtyFoxStyle.Clear(); return; }
            if (G.loading || G.house == null) return;
            foreach (var kv in delFurn)
            {
                int nid = kv.Key;
                SendOut(Pack(Msg.FurnDel, w => w.Write(nid)), kv.Value);
            }
            delFurn.Clear();
            if (dirtyFurn.Count > 0)
            {
                var list = new List<KeyValuePair<Furniture, int>>(dirtyFurn);
                dirtyFurn.Clear();
                foreach (var kv in list)
                {
                    var f = kv.Key;
                    if (f == null || f.isGhost || !G.furniture.Contains(f)) continue;
                    if (role == Role.Client && f.lot != myLot) continue;
                    SendOut(FurnMsg(f), kv.Value);
                }
            }
            if (role == Role.Client && dirtyFoxStyle.Count > 0)
            {
                foreach (var fox in dirtyFoxStyle)
                {
                    if (fox == null) continue;
                    fox.SyncWorn();
                    var d = new NetFoxStyle();
                    foreach (var p in fox.style.parts) d.fur.Add(p.style.Clone());
                    if (fox.outfits != null) d.outfits.AddRange(fox.outfits);
                    var gz = Gzip(JsonUtility.ToJson(d));
                    var id = fox.id;
                    server.Send(Pack(Msg.FoxStyle, w => { WStr(w, id); w.Write(gz.Length); w.Write(gz); }));
                }
                dirtyFoxStyle.Clear();
            }
            // постройка своего участка: пока открыт план (и сразу после) сравниваем с тем, что уже отправили
            bool building = G.mode == Mode.Build || G.mode == Mode.Style;
            lotTimer -= Time.unscaledDeltaTime;
            if ((building || wasBuilding) && lotTimer <= 0f)
            {
                lotTimer = 0.5f;
                wasBuilding = building;
                int lot = role == Role.Client ? myLot : House.PlayerLotId;
                if (lot >= 0)
                {
                    var json = LotJson(lot);
                    if (json != null && (!lastLot.TryGetValue(lot, out var old) || old != json))
                    {
                        if (old != null) SendOut(LotMsg(lot, json), -1);
                        lastLot[lot] = json;
                    }
                }
            }
            if (!building && !wasBuilding && role == Role.Host && !lastLot.ContainsKey(House.PlayerLotId)) lastLot[House.PlayerLotId] = LotJson(House.PlayerLotId);
        }
    }
}
