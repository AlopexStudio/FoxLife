using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Разговор «между делом»: две лисички рядом смотрят телевизор, едят или рисуют — и одна рассказывает
    /// другой шутку, не отрываясь от занятия. Не болтают во сне, в ванне и на работе.
    /// </summary>
    public partial class Fox
    {
        public class SideTalk
        {
            public Fox other;
            public SocialDef def;
            public float elapsed, duration;
            public bool lead, success, reacted;
        }

        public SideTalk side;
        float sideTimer = 20f;

        /// <summary>Разговоры, для которых не надо вставать с места (без объятий, догонялок и поцелуев).</summary>
        static readonly string[] SideNames = { "Поболтать", "Пошутить", "Обсудить идеи", "Рассказать историю", "Сделать комплимент", "Утешить", "Пожаловаться на жизнь", "Подразнить", "Поспорить", "Флиртовать" };

        public static bool IsSideTalkable(SocialDef d) => d != null && System.Array.IndexOf(SideNames, d.name) >= 0;

        /// <summary>Занят(а) делом, за которым можно и поболтать.</summary>
        public bool CanTalkWhileBusy
        {
            get
            {
                if (side != null || sleeping || atWork || carried || knock != null) return false;
                if (current == null || current.kind != ActKind.UseObject || current.stage != 2 || current.inter == null) return false;
                if (current.inter.sleep || current.inter.pose == Pose.Bath) return false;
                return true;
            }
        }

        bool CloseEnoughToChat(Fox o) => o.Level == Level && Vector3.Distance(o.transform.position, transform.position) < 3.6f;

        /// <summary>Начать разговор между делом (если оба заняты и рядом). true — получилось.</summary>
        public bool TryStartSideTalk(Fox o, SocialDef def)
        {
            if (o == null || o == this || !IsSideTalkable(def) || !def.available(this, o)) return false;
            if (!CanTalkWhileBusy || !o.CanTalkWhileBusy || !CloseEnoughToChat(o)) return false;
            float chance = Mathf.Clamp(def.accept(this, o) + SkillLevel(Skill.Charisma) * 0.015f + (HasPerk("charm") ? 0.1f : 0f), 0.05f, 0.95f);
            float dur = def.duration * 0.8f;
            side = new SideTalk { other = o, def = def, duration = dur, lead = true, success = Random.value < chance };
            o.side = new SideTalk { other = this, def = def, duration = dur, lead = false };
            Say(Phrases.Pick("say:" + def.name, this, o, def.say));
            return true;
        }

        void EndSideTalk()
        {
            if (side == null) return;
            var o = side.other;
            side = null;
            if (o != null && o.side != null && o.side.other == this) o.side = null;
        }

        void UpdateSideTalk(float dm)
        {
            if (side != null)
            {
                var o = side.other;
                // кто-то встал или ушёл — разговор сам собой затихает
                bool okMe = current != null && current.kind == ActKind.UseObject && current.stage == 2 && !sleeping;
                bool okO = o != null && G.foxes.Contains(o) && o.side != null && o.side.other == this && o.current != null && o.current.kind == ActKind.UseObject && !o.sleeping;
                if (!okMe || !okO || !CloseEnoughToChat(o)) { EndSideTalk(); return; }
                side.elapsed += dm;
                needs[(int)Need.Social] = Mathf.Min(100, needs[(int)Need.Social] + 1.2f * dm);
                if (side.lead)
                {
                    GainSkill(Skill.Charisma, 0.3f * dm);
                    if (!side.reacted && side.elapsed > side.duration * 0.45f)
                    {
                        side.reacted = true;
                        React(new FoxAction { kind = ActKind.Social, social = side.def, other = o, success = side.success });
                    }
                    if (side.elapsed >= side.duration) EndSideTalk();
                }
                return;
            }
            // сами заговаривают с соседом по занятию: общительные — чаще
            sideTimer -= dm;
            if (sideTimer > 0) return;
            sideTimer = Random.Range(25f, 55f) - pers.E * 2f;
            if (!CanTalkWhileBusy || (IsPlayer && !G.autonomy) || needs[(int)Need.Social] > 92f) return;
            if (Random.value > 0.35f + (pers.E - 5.5f) * 0.05f + (100f - needs[(int)Need.Social]) / 250f) return;
            Fox best = null; float bd = float.MaxValue;
            foreach (var f in G.foxes)
            {
                if (f == null || f == this || !f.CanTalkWhileBusy || !CloseEnoughToChat(f)) continue;
                float d = Vector3.Distance(f.transform.position, transform.position);
                if (d < bd) { bd = d; best = f; }
            }
            if (best == null) return;
            SocialDef pick = null; float ps = float.MinValue;
            foreach (var sd in Social.All)
            {
                if (!IsSideTalkable(sd) || !sd.available(this, best)) continue;
                float w = sd.want(this, best);
                if (w < -0.1f) continue;
                float sc = w + Random.value * 0.3f;
                if (sc > ps) { ps = sc; pick = sd; }
            }
            if (pick != null && TryStartSideTalk(best, pick))
                G.LogPair(this, best, $"{foxName} и {best.foxName} болтают, не отрываясь от дел: «{pick.name}».");
        }
    }
}
