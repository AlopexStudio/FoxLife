using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FoxLife
{
    /// <summary>Ключ от дома: гостевой (пока не уйдёшь с участка), постоянный (у доверенных друзей), семейный (у жильцов).</summary>
    public enum KeyKind { None = 0, Guest = 1, Trusted = 2, Family = 3 }

    [Serializable]
    public class HouseKey
    {
        public int lot;
        public int kind;        // KeyKind
        public string giver;    // id лисички, которая дала ключ
        public bool entered;    // гостевой: уже заходил(а) на участок
        public float since;     // когда дали (игровые минуты)
    }

    /// <summary>
    /// Ключи и стук в дверь. Заходить в жилой чужой дом можно только с ключом; без ключа — постучаться.
    /// Открывает тот, у кого есть любой ключ от дома и кто рядом. Решение без случайностей: по отношениям
    /// и характеру, а семья решает сообща (постоянный ключ — только если согласны все жильцы).
    /// </summary>
    public partial class Fox
    {
        public List<HouseKey> keys = new List<HouseKey>();
        readonly Dictionary<int, float> refusedAt = new Dictionary<int, float>();

        class KnockState
        {
            public int lot;
            public House.DoorLeaf door;
            public Fox opener;
            public float waited;
            public FoxAction then;
            public bool pushy;
            public float lastTry;
            public bool forced; // открыть велел игрок — впускают без раздумий
        }
        KnockState knock;

        public bool Knocking => knock != null;

        /// <summary>В чью дверь стучится (-1 — не стучится). У друга по сети — по занятию и месту.</summary>
        public int KnockingAt => knock != null ? knock.lot
            : current != null && current.label == "Стучится в дверь" && G.house != null ? (G.house.LotAt(Cell)?.id ?? -1) : -1;

        /// <summary>Уже идут открывать?</summary>
        public bool DoorAnswered => knock != null && knock.opener != null;

        /// <summary>Игрок велел своей лисичке открыть дверь: она идёт к двери и впускает гостя.</summary>
        public void PlayerLetIn(Fox opener)
        {
            if (opener == null) return;
            if (Net.IsClient) { Net.I.SendOp(opener, "letin", id); return; }
            if (knock == null || opener.KeyTo(knock.lot) == KeyKind.None) return;
            if (knock.opener != null && knock.opener != opener) knock.opener.queue.RemoveAll(a => a.label != null && a.label.StartsWith("Идёт открывать дверь"));
            knock.opener = opener;
            knock.forced = true;
            knock.waited = Mathf.Min(knock.waited, 10f);
            opener.GoOpenDoor(this, knock.lot, knock.door);
        }

        // ---------- кто куда может войти ----------

        /// <summary>Жилой дом, в который без ключа не войти (общественные здания и пустые дома открыты).</summary>
        public static bool LotLocked(int lotId)
        {
            var lot = Game.I.house.LotById(lotId);
            if (lot == null || lot.kind != LotKind.Home || lot.removed) return false;
            // жилые участки считаются раз за кадр (двери спрашивают об этом очень часто)
            if (inhabitedFrame != Time.frameCount)
            {
                inhabitedFrame = Time.frameCount;
                inhabited.Clear();
                foreach (var o in Game.I.foxes) if (o != null && o.storeGroup == null) inhabited.Add(o.home);
            }
            return inhabited.Contains(lotId);
        }
        static readonly HashSet<int> inhabited = new HashSet<int>();
        static int inhabitedFrame = -1;

        public KeyKind KeyTo(int lotId)
        {
            if (lotId < 0) return KeyKind.None;
            if (lotId == home && storeGroup == null) return KeyKind.Family;
            var best = KeyKind.None;
            foreach (var k in keys)
                if (k.lot == lotId && KeyValid(k) && (KeyKind)k.kind > best) best = (KeyKind)k.kind;
            return best;
        }

        bool KeyValid(HouseKey k)
        {
            if ((KeyKind)k.kind != KeyKind.Trusted) return true;
            var giver = FoxById(k.giver);
            return giver != null && giver.home == k.lot; // семья переехала — старый ключ больше ни к чему
        }

        public bool CanEnter(int lotId) => !LotLocked(lotId) || KeyTo(lotId) != KeyKind.None;

        /// <summary>Ключ, который дала нам эта лисичка (для списка друзей).</summary>
        public HouseKey KeyFrom(Fox giver)
        {
            HouseKey best = null;
            foreach (var k in keys)
                if (k.giver == giver.id && KeyValid(k) && (best == null || k.kind > best.kind)) best = k;
            return best;
        }

        public static Fox FoxById(string id) => string.IsNullOrEmpty(id) ? null : Game.I.foxes.Find(o => o != null && o.id == id);

        public void GiveKey(int lotId, KeyKind kind, Fox giver)
        {
            keys.RemoveAll(k => k.lot == lotId && k.kind <= (int)kind);
            keys.Add(new HouseKey { lot = lotId, kind = (int)kind, giver = giver != null ? giver.id : null, since = G.minutes, entered = G.house.LotAt(Cell)?.id == lotId });
        }

        // ---------- характер: насколько легко пускает в дом ----------

        /// <summary>Чем доброжелательнее, общительнее и открытее — тем меньше нужно отношений; тревожным — больше.</summary>
        float Wariness => -(pers.A - 5.5f) * 4f - (pers.E - 5.5f) * 3f - (pers.O - 5.5f) * 3f + (pers.N - 5.5f) * 4f;
        /// <summary>Сколько нужно отношений, чтобы впустить в гости.</summary>
        public float GuestNeed => Mathf.Clamp(-5f + Wariness * 0.8f, -35f, 40f);
        /// <summary>Сколько нужно отношений, чтобы доверить постоянный ключ.</summary>
        public float TrustNeed => Mathf.Clamp(45f + Wariness, 20f, 85f);
        /// <summary>Ниже этого постоянный ключ отбирают (запас, чтобы ключ не прыгал туда-сюда).</summary>
        public float RevokeBelow => TrustNeed - 20f;

        /// <summary>Подойдёт ли к двери: не спит, не на работе, и настроение с характером не против гостей.</summary>
        bool WouldAnswerDoor => !sleeping && !atWork && !carried && stage >= LifeStage.Teen
            && Mood + (pers.E - 5.5f) * 8f + (pers.O - 5.5f) * 5f > -25f
            && (current == null || (current.kind != ActKind.Phone && current.kind != ActKind.Work && !(current.kind == ActKind.Idle && current.floorSleep)));

        static List<Fox> Residents(int lotId) => Game.I.foxes.FindAll(o => o != null && o.home == lotId && o.storeGroup == null && o.stage >= LifeStage.Teen);

        /// <summary>Кто из семьи против гостя (null — никто). Учитываются те, кто сейчас дома и не спит.</summary>
        Fox GuestObjector(int lotId, Fox except)
        {
            foreach (var m in Residents(lotId))
            {
                if (m == except || m.sleeping || m.atWork || G.house.LotAt(m.Cell)?.id != lotId) continue;
                if (Social.Rel(m, this) < m.GuestNeed - 15f) return m;
            }
            return null;
        }

        /// <summary>Доверит ли семья постоянный ключ: согласны должны быть все жильцы — тогда никто потом не передумает просто так.</summary>
        public Fox TrustObjector(int lotId)
        {
            foreach (var m in Residents(lotId))
                if (Social.Rel(m, this) < m.TrustNeed) return m;
            return null;
        }

        // ---------- стук в дверь ----------

        /// <summary>Подойти к двери дома и постучать; если впустят — заняться делом then (если есть).</summary>
        public void KnockAt(int lotId, House.DoorLeaf door = null, FoxAction then = null, bool byPlayer = true, Fox host = null)
        {
            var lot = G.house.LotById(lotId);
            if (lot == null) return;
            if (door == null) door = G.house.FrontDoor(lotId);
            Vector2Int outside = door != null ? G.house.DoorOutside(door) : lot.entry;
            var me = this;
            queue.Add(new FoxAction
            {
                kind = ActKind.Move, byPlayer = byPlayer, other = host, label = "Идёт к двери " + lot.NameIn(NameCase.Genitive),
                moveTarget = GridMap.Center(outside) + Vector3.up * G.house.FloorY(0, outside),
                onArrive = () => me.StartKnock(lotId, door, then),
            });
        }

        /// <summary>Приказ игрока «Постучаться»: прежние приказы идти куда-то отменяются.</summary>
        public void CommandKnock(int lotId, House.DoorLeaf door)
        {
            if (Net.IsClient) { Net.I.SendOp(this, "knock", lotId.ToString()); return; }
            queue.RemoveAll(q => q.kind == ActKind.Move && q.byPlayer);
            if (current != null && ((current.kind == ActKind.Move && current.byPlayer) || (!current.byPlayer && current.kind != ActKind.BeTalkedTo && current.kind != ActKind.Work))) CancelCurrent(false);
            KnockAt(lotId, door, null, true);
        }

        void StartKnock(int lotId, House.DoorLeaf door, FoxAction then)
        {
            if (CanEnter(lotId)) { EnterHouse(lotId, door, then); return; }
            knock = new KnockState { lot = lotId, door = door, then = then };
            if (door != null) Face(door.center);
            current = new FoxAction { kind = ActKind.Idle, label = "Стучится в дверь", duration = 60f, stage = 1, byPlayer = then != null && then.byPlayer };
            var lot = G.house.LotById(lotId);
            bool bell = lot != null && lot.id % 3 == 0;
            if (G.sound != null) { if (bell) G.sound.Doorbell(transform.position); else G.sound.Knock(transform.position); }
            Say(bell ? "Динь-дон!" : Random.value < 0.5f ? "Тук-тук!" : "Тук-тук! Есть кто дома?");
            knock.pushy = refusedAt.TryGetValue(lotId, out var t) && G.minutes - t < 120f;
            if (!FindOpener()) G.LogFox(this, lot != null
                ? $"{foxName} стучится в дверь {lot.NameIn(NameCase.Genitive)}."
                : $"{foxName} стучится в дверь.");
        }

        /// <summary>Открыть может тот, у кого есть ключ и кто недалеко от двери (через весь город на стук не бегут).</summary>
        bool FindOpener()
        {
            int lotId = knock.lot;
            Fox best = null; float bd = 16f;
            Vector3 dc = knock.door != null ? knock.door.center : transform.position;
            foreach (var o in G.foxes)
            {
                if (o == null || o == this || o.KeyTo(lotId) == KeyKind.None || !o.WouldAnswerDoor) continue;
                if (G.house.LotAt(o.Cell)?.id != lotId) continue;
                float d = Vector3.Distance(o.transform.position, dc);
                if (d < bd) { bd = d; best = o; }
            }
            if (best == null) return false;
            knock.opener = best;
            best.GoOpenDoor(this, lotId, knock.door);
            G.LogPair(this, best, $"{foxName} стучится в дверь, {best.foxName} идёт открывать.");
            return true;
        }

        void GoOpenDoor(Fox visitor, int lotId, House.DoorLeaf door)
        {
            if (current != null) CancelCurrent(true);
            Vector2Int inside = door != null ? G.house.DoorInside(door) : Cell;
            var me = this;
            Say(Random.value < 0.5f ? "Иду-иду!" : "Кто там?");
            queue.Insert(0, new FoxAction
            {
                kind = ActKind.Move, label = "Идёт открывать дверь " + visitor.NameIn(NameCase.Dative),
                moveTarget = GridMap.Center(inside) + Vector3.up * G.house.FloorY(0, inside),
                onArrive = () => visitor.DoorOpened(me),
            });
        }

        void UpdateKnock(float dm)
        {
            if (knock == null) return;
            if (current == null || current.label != "Стучится в дверь") { knock = null; return; }
            knock.waited += dm;
            current.elapsed = 0; // ждём, пока откроют
            // никто не подошёл — ещё раз постучать через пару минут (вдруг кто-то договорит или вернётся)
            if (knock.opener == null && knock.waited > 3f && knock.waited - knock.lastTry > 3f && knock.waited < 9f)
            {
                knock.lastTry = knock.waited;
                Say("Тук-тук-тук!");
                if (G.sound != null) G.sound.Knock(transform.position);
                FindOpener();
            }
            if (knock.opener == null && knock.waited > 10f)
            {
                Say(Random.value < 0.5f ? "Никого нет дома…" : "Никто не открывает…");
                AddMoodlet("Никто не открыл", -2, 90);
                knock = null;
                Finish(current);
            }
            else if (knock.opener != null && knock.waited > 45f)
            {
                Say("Так и не открыли…");
                knock = null;
                Finish(current);
            }
        }

        /// <summary>Дверь открыли: решение — гостевой ключ, постоянный или отказ (без бросков кубика).</summary>
        void DoorOpened(Fox opener)
        {
            if (knock == null || knock.opener != opener) return;
            var k = knock;
            knock = null;
            if (current != null && current.label == "Стучится в дверь") Finish(current);
            int lotId = k.lot;
            var lot = G.house.LotById(lotId);
            opener.Face(transform.position);
            Face(opener.transform.position);
            if (k.pushy)
            {
                // только что отказали — а она опять стучит
                Social.AddRel(opener, this, -5f);
                opener.AddMoodlet("Настырный гость", -3, 90);
            }
            bool ok = k.forced || Social.Rel(opener, this) >= opener.GuestNeed;
            var objector = ok && !k.forced ? GuestObjector(lotId, opener) : null;
            if (!ok || objector != null)
            {
                refusedAt[lotId] = G.minutes;
                string line = k.pushy ? "Опять ты? Я же сказал{|а}: не сейчас!"
                    : objector != null ? $"Прости, {objector.foxName} будет против."
                    : Social.Rel(opener, this) < -20f ? "Тебе здесь не рады." : "Извини, сейчас не до гостей.";
                opener.Say(line.Replace("{|а}", opener.female ? "а" : ""));
                AddMoodlet("Не впустили", -4, 120);
                G.LogPair(this, opener, $"{opener.foxName} не впускает {NameIn(NameCase.Accusative)}" + (objector != null ? $" ({objector.foxName} против)." : "."));
                return;
            }
            bool trust = opener.KeyTo(lotId) == KeyKind.Family && KeyTo(lotId) < KeyKind.Trusted && TrustObjector(lotId) == null;
            GiveKey(lotId, trust ? KeyKind.Trusted : KeyKind.Guest, opener);
            if (trust)
            {
                opener.Say("Заходи! И держи ключ — приходи, когда захочешь.");
                AddMoodlet("Мне доверили ключ", 8, 600);
                Remember("Ключ от " + opener.NameIn(NameCase.Genitive), about: opener);
                G.LogPair(this, opener, $"{opener.foxName} даёт {NameIn(NameCase.Dative)} постоянный ключ от дома.");
            }
            else
            {
                opener.Say(Phrases.Pick("host:welcome", opener, this));
                G.LogPair(this, opener, $"{opener.foxName} впускает {NameIn(NameCase.Accusative)} в гости.");
            }
            Social.AddRel(this, opener, 2f);
            opener.AddMoodlet("Пришли гости", 3, 180);
            EnterHouse(lotId, k.door, k.then);
        }

        /// <summary>Войти в дом (ключ уже есть): заняться делом, ради которого пришли, или просто зайти в гости.</summary>
        void EnterHouse(int lotId, House.DoorLeaf door, FoxAction then)
        {
            if (lotId != home)
            {
                BeGuest(lotId, 3f);
                var lot = G.house.LotById(lotId);
                if (lot != null) AddMoodlet("В гостях " + lot.At, 4, 180);
            }
            if (then != null) { then.stage = 0; queue.Insert(0, then); return; }
            if (door == null) return;
            var inside = G.house.DoorInside(door);
            // шаг-другой внутрь, чтобы не стоять в дверях
            var c = inside;
            for (int i = 0; i < 6; i++)
            {
                var t = inside + new Vector2Int(Random.Range(-2, 3), Random.Range(-2, 3));
                if (G.house.RoomAt(0, t) != null && G.house.grids[0].IsFree(t)) { c = t; break; }
            }
            queue.Insert(0, new FoxAction { kind = ActKind.Move, label = "Заходит в дом", moveTarget = GridMap.Center(c) + Vector3.up * G.house.FloorY(0, c) });
        }

        // ---------- ключи живут своей жизнью ----------

        float keyTimer;

        void UpdateKeys(float dm)
        {
            UpdateKnock(dm);
            if (keys.Count > 0)
            {
                int here = G.house.LotAt(Cell)?.id ?? -1;
                for (int i = keys.Count - 1; i >= 0; i--)
                {
                    var k = keys[i];
                    if ((KeyKind)k.kind == KeyKind.Guest)
                    {
                        if (here == k.lot) k.entered = true;
                        else if (k.entered || G.minutes - k.since > 120f)
                        {
                            keys.RemoveAt(i); // ушли с участка — гостевой ключ кончился
                            if (guestLot == k.lot) guestLot = -1;
                        }
                    }
                    else if (!KeyValid(k)) keys.RemoveAt(i);
                }
            }
            keyTimer -= dm;
            if (keyTimer > 0) return;
            keyTimer = 45f + Random.Range(0f, 30f);
            MaybeTakeKeyBack();
        }

        /// <summary>Отношения испортились — член семьи подходит и забирает постоянный ключ.</summary>
        void MaybeTakeKeyBack()
        {
            if (storeGroup != null || stage < LifeStage.Teen || sleeping || atWork || carried) return;
            if (current != null && (current.byPlayer || current.kind == ActKind.Social || current.kind == ActKind.BeTalkedTo || current.kind == ActKind.Work)) return;
            if (!G.AutonomyFor(home)) return;
            foreach (var o in G.foxes)
            {
                if (o == null || o == this || o.KeyTo(home) != KeyKind.Trusted || Social.Rel(this, o) >= RevokeBelow) continue;
                if (Vector3.Distance(o.transform.position, transform.position) > 25f || !o.CanBeApproached(this)) continue;
                if (current != null) CancelCurrent(true);
                queue.Insert(0, FoxAction.ForSocial(Social.TakeKey, o));
                return;
            }
        }

        /// <summary>Забрали постоянный ключ: если мы сейчас внутри — остаётся гостевой, чтобы спокойно выйти.</summary>
        public void LoseKey(int lotId, Fox by)
        {
            keys.RemoveAll(k => k.lot == lotId && k.kind == (int)KeyKind.Trusted);
            if (G.house.LotAt(Cell)?.id == lotId) GiveKey(lotId, KeyKind.Guest, by);
            AddMoodlet("Забрали ключ", -6, 300);
            G.LogPair(this, by, $"{by.foxName} забирает у {NameIn(NameCase.Genitive)} ключ от дома.");
        }

        /// <summary>Почему нельзя дать ключ от нашего дома (null — можно).</summary>
        public string GiveKeyReason(Fox to)
        {
            if (to.home == home) return "Живёт с нами — ключ и так есть";
            if (to.KeyTo(home) >= KeyKind.Trusted) return "Ключ уже есть";
            var obj = to.TrustObjector(home);
            if (obj == this) return "Пока не настолько доверяешь";
            if (obj != null) return obj.foxName + " будет против";
            return null;
        }

        void KeysForSave(List<HouseKey> into)
        {
            foreach (var k in keys) if ((KeyKind)k.kind == KeyKind.Trusted && KeyValid(k)) into.Add(new HouseKey { lot = k.lot, kind = k.kind, giver = k.giver, since = k.since });
        }

        public List<HouseKey> SavedKeys() { var l = new List<HouseKey>(); KeysForSave(l); return l; }
    }
}
