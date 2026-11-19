using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public enum LifeStage { Kit = 0, Teen = 1, Adult = 2, Elder = 3 }

    public enum Lifespan { Short = 0, Normal = 1, Long = 2, Endless = 3 }

    /// <summary>Запись о лисичке, которой больше нет в доме (для родословной).</summary>
    [Serializable]
    public class FoxRecord
    {
        public string id, name, mother, father, reason;
        public bool female;
        public int day;
    }

    public static class Family
    {
        static Game G => Game.I;

        // ---------- этапы жизни ----------

        static readonly int[] BaseDays = { 4, 6, 24, 12 };

        public static int StageDays(LifeStage s, Lifespan l)
        {
            float k = l == Lifespan.Short ? 0.5f : l == Lifespan.Long ? 2f : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(BaseDays[(int)s] * k));
        }

        public static string LifespanName(Lifespan l) =>
            l == Lifespan.Short ? "короткая" : l == Lifespan.Normal ? "обычная" : l == Lifespan.Long ? "долгая" : "без старения";

        public static string StageName(LifeStage s, bool female)
        {
            switch (s)
            {
                case LifeStage.Kit: return "лисёнок";
                case LifeStage.Teen: return female ? "юная лисичка" : "юный лис";
                case LifeStage.Adult: return female ? "взрослая лисица" : "взрослый лис";
                default: return female ? "пожилая лисица" : "пожилой лис";
            }
        }

        public static float StageScale(LifeStage s) => s == LifeStage.Kit ? 0.55f : s == LifeStage.Teen ? 0.82f : 1f;
        public static float HeadScale(LifeStage s) => s == LifeStage.Kit ? 1.3f : s == LifeStage.Teen ? 1.08f : 1f;

        // ---------- характер в двух словах ----------

        // [шкала, 0 = низко / 1 = высоко, 0 = м / 1 = ж]
        static readonly string[,,] Adj =
        {
            { { "практичный", "практичная" }, { "любопытный", "любопытная" } },
            { { "беспечный", "беспечная" }, { "аккуратный", "аккуратная" } },
            { { "застенчивый", "застенчивая" }, { "весёлый", "весёлая" } },
            { { "колючий", "колючая" }, { "добрый", "добрая" } },
            { { "спокойный", "спокойная" }, { "ранимый", "ранимая" } },
        };

        static readonly string[,,] Noun =
        {
            { { "домосед", "домоседка" }, { "выдумщик", "выдумщица" } },
            { { "растяпа", "растяпа" }, { "чистюля", "чистюля" } },
            { { "тихоня", "тихоня" }, { "непоседа", "непоседа" } },
            { { "вредина", "вредина" }, { "душка", "душка" } },
            { { "флегма", "флегма" }, { "неженка", "неженка" } },
        };

        /// <summary>Две самые яркие черты: сильнейшая — существительное, вторая — прилагательное. «Добрая непоседа».</summary>
        public static string Summary(Personality p, bool female)
        {
            int g = female ? 1 : 0;
            int first = -1, second = -1;
            float d1 = -1, d2 = -1;
            for (int i = 0; i < 5; i++)
            {
                float d = Mathf.Abs(p.v[i] - 5.5f);
                if (d > d1) { d2 = d1; second = first; d1 = d; first = i; }
                else if (d > d2) { d2 = d; second = i; }
            }
            if (d1 < 1.6f) return female ? "Уравновешенная лисица" : "Уравновешенный лис";
            string noun = Noun[first, p.v[first] > 5.5f ? 1 : 0, g];
            string adj = d2 < 1f ? (female ? "милая" : "милый") : Adj[second, p.v[second] > 5.5f ? 1 : 0, g];
            return char.ToUpper(adj[0]) + adj.Substring(1) + " " + noun;
        }

        // ---------- родство ----------

        static string MotherOf(string id)
        {
            var f = G.foxes.Find(x => x.id == id);
            if (f != null) return f.motherId;
            var r = G.archive.Find(x => x.id == id);
            return r?.mother;
        }

        static string FatherOf(string id)
        {
            var f = G.foxes.Find(x => x.id == id);
            if (f != null) return f.fatherId;
            var r = G.archive.Find(x => x.id == id);
            return r?.father;
        }

        public static IEnumerable<string> Parents(string id)
        {
            var m = MotherOf(id); if (!string.IsNullOrEmpty(m)) yield return m;
            var f = FatherOf(id); if (!string.IsNullOrEmpty(f)) yield return f;
        }

        public static bool IsParent(string parent, string child)
        {
            foreach (var p in Parents(child)) if (p == parent) return true;
            return false;
        }

        public static bool AreSiblings(string a, string b)
        {
            if (a == b) return false;
            foreach (var pa in Parents(a)) foreach (var pb in Parents(b)) if (pa == pb) return true;
            return false;
        }

        public static bool IsGrandparent(string g, string c)
        {
            foreach (var p in Parents(c)) if (IsParent(g, p)) return true;
            return false;
        }

        public static bool AreRelatives(Fox a, Fox b) =>
            IsParent(a.id, b.id) || IsParent(b.id, a.id) || AreSiblings(a.id, b.id) || IsGrandparent(a.id, b.id) || IsGrandparent(b.id, a.id);

        /// <summary>Кем приходится other для me: «мама», «сын», «сестра»...</summary>
        public static string Role(Fox me, Fox other)
        {
            if (me.partnerId == other.id) return me.married ? (other.female ? "супруга" : "супруг") : (other.female ? "любимая" : "любимый");
            if (IsParent(other.id, me.id)) return other.female ? "мама" : "папа";
            if (IsParent(me.id, other.id)) return other.female ? "дочь" : "сын";
            if (AreSiblings(me.id, other.id)) return other.female ? "сестра" : "брат";
            if (IsGrandparent(other.id, me.id)) return other.female ? "бабушка" : "дедушка";
            if (IsGrandparent(me.id, other.id)) return other.female ? "внучка" : "внук";
            return null;
        }

        public static string NameOf(string id, out bool gone)
        {
            gone = false;
            var f = G.foxes.Find(x => x.id == id);
            if (f != null) return f.foxName;
            var r = G.archive.Find(x => x.id == id);
            gone = true;
            return r != null ? r.name : "?";
        }

        /// <summary>«Искра» между двумя лисами: постоянна для пары, зависит от похожести открытости и доброжелательности.</summary>
        public static float Chemistry(Fox a, Fox b)
        {
            int h = 17;
            string k = string.CompareOrdinal(a.id, b.id) < 0 ? a.id + b.id : b.id + a.id;
            foreach (char c in k) h = h * 31 + c;
            float rnd = ((h & 0x7fffffff) % 1000) / 1000f;
            float sim = 1f - (Mathf.Abs(a.pers.O - b.pers.O) + Mathf.Abs(a.pers.A - b.pers.A)) / 18f;
            return Mathf.Clamp01(sim * 0.5f + rnd * 0.5f);
        }

        public static string ChemistryLabel(float c) => c > 0.7f ? "сильная" : c > 0.45f ? "есть" : c > 0.25f ? "слабая" : "нет";

        public static bool CanRomance(Fox a, Fox b) =>
            a.stage >= LifeStage.Adult && b.stage >= LifeStage.Adult && !AreRelatives(a, b);

        // ---------- лисята ----------

        public const float InheritChance = 0.55f;

        /// <summary>Характер ребёнка: каждая шкала с шансом наследуется от случайного родителя (±1), иначе случайна.</summary>
        public static Personality InheritPersonality(Fox a, Fox b)
        {
            var p = new Personality();
            for (int i = 0; i < 5; i++)
            {
                if (UnityEngine.Random.value < InheritChance)
                {
                    var src = (b != null && UnityEngine.Random.value < 0.5f) ? b : a;
                    p.v[i] = Mathf.Clamp(src.pers.v[i] + UnityEngine.Random.Range(-1, 2), 1, 10);
                }
                else p.v[i] = UnityEngine.Random.Range(1, 11);
            }
            return p;
        }

        static Color Jitter(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = (h + UnityEngine.Random.Range(-0.02f, 0.02f) + 1f) % 1f;
            s = Mathf.Clamp01(s + UnityEngine.Random.Range(-0.06f, 0.06f));
            v = Mathf.Clamp01(v + UnityEngine.Random.Range(-0.06f, 0.06f));
            return Color.HSVToRGB(h, s, v);
        }

        /// <summary>Окрас ребёнка: каждая часть шерсти — смесь родителей с небольшим отклонением.</summary>
        public static StyleData InheritStyle(Fox a, Fox b, int part)
        {
            var sa = a.style.Get(part);
            var sb = (b != null ? b : a).style.Get(part);
            var s = (UnityEngine.Random.value < 0.5f ? sa : sb).Clone();
            float t = UnityEngine.Random.value;
            s.c1 = Jitter(Color.Lerp(sa.c1, sb.c1, t));
            s.c2 = Jitter(Color.Lerp(sa.c2, sb.c2, t));
            return s;
        }

        public static readonly string[] KitNames =
        {
            "Кроха", "Пуговка", "Бусинка", "Кнопка", "Ириска", "Пряник", "Желудь", "Вишенка",
            "Сухарик", "Фантик", "Орешек", "Ромашка", "Малинка", "Тучка", "Звёздочка", "Ёжик",
        };
    }
}
