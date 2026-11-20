using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FoxLife
{
    /// <summary>То, что меняется в окне «Лисички»: друг правит своих лисичек у себя, правки уходят хосту.</summary>
    [Serializable]
    public class NetCas
    {
        public string name;
        public string[] nameOverrides;
        public bool female;
        public int stage;
        public int[] pers;
        public float ears, tail, size;
        public int element, liked, disliked;
        public float[] skillXp;
        public int[] skillLvl;
        public string career;
        public int careerLevel;
        public List<Want> wants = new List<Want>();
        public float[] needs;
        public float health = -1, mental = -1;
        public List<string> interests = new List<string>();
        public int likedSeason = -1;

        public static NetCas Of(Fox f) => new NetCas
        {
            name = f.foxName, female = f.female, stage = (int)f.stage, pers = (int[])f.pers.v.Clone(),
            nameOverrides = RussianCases.CopyOverrides(f.nameOverrides),
            ears = f.earSize, tail = f.tailFluff, size = f.size,
            element = f.elementId, liked = f.likedWeather, disliked = f.dislikedWeather,
            skillXp = (float[])f.skillXp.Clone(), skillLvl = (int[])f.skillLvl.Clone(),
            career = f.career != null ? f.career.id : null, careerLevel = f.careerLevel,
            wants = new List<Want>(f.wants),
            needs = new[] { Mathf.Round(f.needs[0]), Mathf.Round(f.needs[1]), Mathf.Round(f.needs[2]), Mathf.Round(f.needs[3]), Mathf.Round(f.needs[4]) },
            health = Mathf.Round(f.health), mental = Mathf.Round(f.mental),
            interests = new List<string>(f.interests), likedSeason = f.likedSeason,
        };

        public void ApplyTo(Fox f)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                string n = name.Trim();
                if (n.Length > 18) n = n.Substring(0, 18);
                f.Rename(n);
                f.nameOverrides = RussianCases.CopyOverrides(nameOverrides);
            }
            f.female = female;
            var st = (LifeStage)Mathf.Clamp(stage, 0, 3);
            if (st != f.stage) f.SetStage(st, false);
            if (pers != null && pers.Length == 5) for (int i = 0; i < 5; i++) f.pers.v[i] = Mathf.Clamp(pers[i], 1, 10);
            f.earSize = Mathf.Clamp(ears, 0.7f, 1.6f);
            f.tailFluff = Mathf.Clamp(tail, 0.7f, 1.5f);
            f.size = Mathf.Clamp(size, 0.8f, 1.15f);
            f.ApplyShape();
            f.elementId = Mathf.Clamp(element, -1, 3);
            f.likedWeather = Mathf.Clamp(liked, -1, 4);
            f.dislikedWeather = Mathf.Clamp(disliked, -1, 4);
            if (skillXp != null && skillXp.Length == Skills.Count) f.skillXp = (float[])skillXp.Clone();
            if (skillLvl != null && skillLvl.Length == Skills.Count) for (int i = 0; i < Skills.Count; i++) f.skillLvl[i] = Mathf.Clamp(skillLvl[i], 0, Skills.MaxLevel);
            var c = string.IsNullOrEmpty(career) ? null : Careers.Get(career);
            if (c != f.career && f.atWork) f.atWork = false;
            f.career = c;
            f.careerLevel = c != null ? Mathf.Clamp(careerLevel, 0, c.levels.Length - 1) : 0;
            if (wants != null)
            {
                f.wants.Clear();
                foreach (var w in wants) if (w != null && Wants.Get(w.id) != null && f.wants.Count < 3) f.wants.Add(w);
            }
            if (needs != null && needs.Length == 5) for (int i = 0; i < 5; i++) f.needs[i] = Mathf.Clamp(needs[i], 0, 100);
            if (health >= 0) f.health = Mathf.Clamp(health, 0, 100);
            if (interests != null) { f.interests.Clear(); foreach (var x in interests) if (Interests.Get(x) != null && f.interests.Count < Interests.Max && !f.interests.Contains(x)) f.interests.Add(x); f.interestsSet = true; }
            if (likedSeason >= 0) f.likedSeason = Mathf.Clamp(likedSeason, 0, 3);
            if (mental >= 0) f.mental = Mathf.Clamp(mental, 0, 100);
        }
    }

    public partial class Net
    {
        // у друга: что уже отправлено из окна «Лисички»
        readonly Dictionary<Fox, string> casSent = new Dictionary<Fox, string>();
        readonly Dictionary<Fox, float> casTime = new Dictionary<Fox, float>();
        float casTimer;
        /// <summary>Друг заводит новую семью: когда она появится — сразу открыть «Лисичек».</summary>
        bool claimCreate;

        /// <summary>Свою лисичку правят в «Лисичках» (или только что правили): данные хоста о ней пока не применяем.</summary>
        public bool CasEditing(Fox f) =>
            IsClient && f != null && Game.IsPlayer(f) &&
            (G.mode == Mode.Create || (casTime.TryGetValue(f, out var t) && Time.unscaledTime - t < 3f));

        /// <summary>У друга: раз в 0,3 с отправить хосту изменённых в «Лисичках» лисичек.</summary>
        void CasUpdate()
        {
            bool editing = G.mode == Mode.Create;
            if (!editing && casSent.Count == 0) return;
            if (editing)
            {
                casTimer -= Time.unscaledDeltaTime;
                if (casTimer > 0) return;
            }
            casTimer = 0.3f;
            foreach (var f in G.Household)
            {
                if (f == null) continue;
                string json = JsonUtility.ToJson(NetCas.Of(f));
                if (!casSent.TryGetValue(f, out var old)) { casSent[f] = json; continue; } // первый снимок — как было
                if (old == json) continue;
                casSent[f] = json;
                casTime[f] = Time.unscaledTime;
                var gz = Gzip(json);
                server.Send(Pack(Msg.FoxEdit, w => { WStr(w, f.id); w.Write(gz.Length); w.Write(gz); }));
            }
            if (!editing) casSent.Clear();
        }

        /// <summary>У хоста: друг поменял свою лисичку в «Лисичках».</summary>
        void ApplyFoxEdit(BinaryReader r, NetLink p)
        {
            var fox = FoxById(RStr(r));
            var json = Gunzip(r.ReadBytes(r.ReadInt32()));
            if (fox == null || p.lot < 0 || fox.home != p.lot) return;
            var d = JsonUtility.FromJson<NetCas>(json);
            if (d == null) return;
            Applying = true; From = p.id;
            try { d.ApplyTo(fox); }
            finally { Applying = false; From = -1; }
        }

        /// <summary>У друга: «+ Новая лисичка» и «Отпустить в лес» в своей семье делает хост.</summary>
        public void CasAddFox(Fox any) { SendOp(any, "addfox"); G.ui.Note("Новая лисичка сейчас появится…"); }
        public void CasRemoveFox(Fox f) => SendOp(f, "removefox");

        void HostCasOp(NetLink p, Fox fox, string op)
        {
            if (op == "addfox")
            {
                if (G.HomeCount(p.lot) >= Game.MaxFoxes) return;
                G.AddRandomFox(p.lot);

            }
            else if (op == "removefox") G.RemoveFox(fox);
        }
    }
}
