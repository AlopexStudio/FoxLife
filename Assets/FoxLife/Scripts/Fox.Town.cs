using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Жизнь в городе: телефон, приглашения, походы в гости. Гость может пользоваться вещами хозяев,
    /// пока визит не закончится; потом идёт домой.
    /// </summary>
    public partial class Fox
    {
        public int guestLot = -1;
        public string storeGroup;   // семья в архиве (без дома)
        public float guestUntil;

        public bool IsGuestAt(int lot) => lot >= 0 && guestLot == lot && G.minutes < guestUntil;

        public void BeGuest(int lot, float hours)
        {
            guestLot = lot;
            guestUntil = G.minutes + hours * 60f;
        }

        /// <summary>Согласится ли лисичка на предложение (по отношениям, общительности и занятости).</summary>
        public bool Accepts(Fox from, float bonus = 0)
        {
            if (atWork || sleeping || carried || stage == LifeStage.Kit) return false;
            if (needs[(int)Need.Energy] < 15f) return false;
            float rel = Social.Rel(this, from);
            if (rel < -10f) return false;
            float p = 0.35f + rel / 120f + (pers.E - 5.5f) * 0.05f + bonus;
            return Random.value < p;
        }

        public enum PhoneMode { Chat, InviteOver, InviteOut }

        public static string PhoneText(PhoneMode mode, Fox target, bool active = false)
        {
            if (mode == PhoneMode.Chat)
                return (active ? "Говорит с " : "Поговорить с ") + target.NameIn(NameCase.Instrumental) + " по телефону";
            string name = target.NameIn(NameCase.Accusative);
            return mode == PhoneMode.InviteOver
                ? (active ? "Приглашает " : "Пригласить ") + name + " в гости"
                : (active ? "Зовёт " : "Позвать ") + name + " гулять";
        }

        public void Call(Fox target, PhoneMode mode, int lot = -1) =>
            Command(new FoxAction { kind = ActKind.Phone, other = target, phoneMode = mode, placeLot = lot, label = PhoneText(mode, target) });

        /// <summary>Звонок: болтает по телефону, а в конце — приглашение (если это приглашение).</summary>
        void RunPhone(FoxAction a, float dm)
        {
            var o = a.other;
            if (o == null || !G.foxes.Contains(o)) { Fail(a, "Не дозвониться"); return; }
            if (a.stage == 0)
            {
                Say("Алло! Это " + foxName + "!");
                a.duration = a.phoneMode == PhoneMode.Chat ? 25f : 8f;
                a.elapsed = 0;
                a.stage = 1;
            }
            pose = Pose.Use;
            a.elapsed += dm;
            needs[(int)Need.Social] = Mathf.Min(100, needs[(int)Need.Social] + 0.9f * dm);
            if (a.elapsed < a.duration) return;
            if (a.phoneMode == PhoneMode.Chat)
            {
                bool ok = Social.Rel(o, this) > -20f && Random.value < 0.85f;
                float d = ok ? 3f + SkillLevel(Skill.Charisma) * 0.6f : -1f;
                Social.AddRel(this, o, d);
                o.needs[(int)Need.Social] = Mathf.Min(100, o.needs[(int)Need.Social] + 12f);
                AddMoodlet("Поболтали по телефону", 3, 120);
                G.LogPair(this, o, $"{foxName} и {o.foxName} болтают по телефону ({(d >= 0 ? "+" : "")}{d:0})");
            }
            else if (a.phoneMode == PhoneMode.InviteOver)
            {
                if (o.Accepts(this, 0.1f)) InviteOver(o);
                else { Say("Не может прийти..."); G.LogFox(this, $"{o.foxName} сейчас не может прийти."); }
            }
            else
            {
                var lot = G.house.LotById(a.placeLot);
                if (lot != null && o.Accepts(this, 0.15f))
                {
                    GoOutTogether(o, lot);
                }
                else { Say("Не получилось договориться..."); G.LogFox(this, $"{o.foxName} не может пойти гулять."); }
            }
            Finish(a);
        }

        /// <summary>Пригласить к себе: гость идёт к нашей двери и остаётся на пару часов.</summary>
        public void InviteOver(Fox guest)
        {
            if (Net.IsClient) { Net.I.SendOp(this, "invite", guest.id); return; }
            int myHome = home;
            var host = this;
            var entry = HomeEntry;
            guest.CancelCurrentPublic();
            guest.queue.Clear();
            guest.queue.Add(new FoxAction
            {
                kind = ActKind.Move, other = host, label = "Идёт в гости к " + host.NameIn(NameCase.Dative),
                moveTarget = GridMap.Center(entry) + Vector3.up * G.house.FloorY(0, entry),
                onArrive = () =>
                {
                    guest.BeGuest(myHome, 3f);
                    guest.GiveKey(myHome, KeyKind.Guest, host); // пригласили — дверь открыта
                    guest.Say(Phrases.Pick("guest:arrive", guest, host));
                    guest.AddMoodlet("В гостях у " + host.NameIn(NameCase.Genitive), 5, 240);
                    host.AddMoodlet("Пришли гости", 4, 240);
                    Social.AddRel(guest, host, 3f);
                    G.LogPair(guest, host, $"{guest.foxName} {(guest.female ? "пришла" : "пришёл")} в гости к {host.NameIn(NameCase.Dative)}.");
                },
            });
            Say("Приходи, буду ждать!");
            G.LogPair(this, guest, $"{guest.foxName} идёт в гости к {NameIn(NameCase.Dative)}.");
        }

        /// <summary>Пойти вместе в кафе, парк или куда-то ещё.</summary>
        public void GoOutTogether(Fox friend, House.Lot lot)
        {
            if (Net.IsClient) { Net.I.SendOp(this, "goout", friend.id, lot.id.ToString()); return; }
            var target = lot.entry;
            var pos = GridMap.Center(target) + Vector3.up * G.house.FloorY(0, target);
            friend.CancelCurrentPublic();
            friend.queue.Clear();
            friend.queue.Add(new FoxAction { kind = ActKind.Move, other = this, placeLot = lot.id, moveTarget = pos, label = "Идёт: " + lot.name });
            queue.Add(new FoxAction { kind = ActKind.Move, other = friend, placeLot = lot.id, moveTarget = pos + new Vector3(0.6f, 0, 0), label = "Идёт: " + lot.name, byPlayer = true });
            Say("Встретимся там!");
            G.LogPair(this, friend, $"{foxName} и {friend.foxName} идут вместе: {lot.name}.");
        }

        /// <summary>Сходить в гости: с ключом — просто войти, без ключа — постучать в дверь, а там уж как встретят.</summary>
        public void VisitHome(Fox host)
        {
            if (Net.IsClient) { Net.I.SendOp(this, "visit", host.id); return; }
            int lotId = host.home;
            var lot = G.house.LotById(lotId);
            if (lot == null || lot.removed || lotId == home) return;
            KnockAt(lotId, host: host);
        }

        float eventTimer;

        /// <summary>На празднике — хорошее настроение и воспоминание.</summary>
        void EventEffects(float dm)
        {
            eventTimer -= dm;
            if (eventTimer > 0) return;
            eventTimer = 30f;
            var lot = G.house.LotAt(Cell);
            if (lot == null || !lot.Public) return;
            var ev = TownEvents.Active(lot.kind, G.Day, G.Hour);
            if (ev == null) return;
            bool first = !moodlets.Exists(m => m.name == ev.mood);
            AddMoodlet(ev.mood, 6 + (pers.E - 5) * 0.6f, 180);
            if (first) { Remember(ev.name + ": " + lot.name); G.LogFox(this, $"{foxName}: {ev.name.ToLower()} — {lot.name}."); }
        }

        public void CancelCurrentPublic()
        {
            if (current != null && current.kind != ActKind.Work && current.kind != ActKind.BeTalkedTo) CancelCurrent(false);
        }

        /// <summary>Соседи иногда сами заглядывают к друзьям (раз в игровой час — небольшой шанс).</summary>
        public void MaybeDropBy()
        {
            if (!G.AutonomyFor(home)) return;
            if (atWork || sleeping || stage == LifeStage.Kit || current != null && current.byPlayer) return;
            float h = G.Hour;
            if (h < 10f || h > 19f || (G.weather != null && G.weather.Raining)) return;
            if (Random.value > 0.03f + (pers.E - 5.5f) * 0.006f) return;
            Fox friend = null; float best = 25f;
            foreach (var o in G.foxes)
                if (o.home != home && Social.Rel(this, o) > best && !o.atWork) { best = Social.Rel(this, o); friend = o; }
            if (friend == null) return;
            var lot = G.house.LotById(friend.home);
            if (lot == null) return;
            if (refusedAt.TryGetValue(friend.home, out var rt) && G.minutes - rt < 240f) return; // недавно не пустили — не навязываемся
            // с ключом заходят сами, без ключа — стучат
            KnockAt(friend.home, byPlayer: false, host: friend);
            G.LogFox(friend, $"{foxName} идёт к вам в гости.");
        }
    }
}
