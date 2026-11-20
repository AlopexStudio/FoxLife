using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Похвалить или отругать лисёнка за то, что он сейчас делает (или только что сделал). Иногда это чуть-чуть
    /// меняет характер: похвала закрепляет черту, ругань — гасит. Слишком часто нельзя: перехваленный лисёнок
    /// перестаёт ценить похвалу, а затюканный — грустит, обижается и становится тревожнее.
    /// </summary>
    public static class Parenting
    {
        const float Cooldown = 240f; // игровых минут между воспитательными моментами

        /// <summary>Какая черта связана с занятием (0..4 — O, C, E, A, N) и в какую сторону (+1 — черта проявилась).</summary>
        public static (int trait, int sign, string what) TraitOf(Fox kid)
        {
            string act = kid.current != null ? (kid.current.inter != null ? kid.current.inter.name : kid.current.social != null ? kid.current.social.name : kid.current.label) : null;
            var list = new List<string>();
            if (act != null) list.Add(act);
            list.AddRange(kid.RecentActivities);
            foreach (var a in list)
            {
                if (a == null) continue;
                if (a.Contains("Подразнить")) return (3, -1, "дразнилку");
                if (a.Contains("Рис") || a.Contains("гитар") || a.Contains("пианино") || a.Contains("Сочин") || a.Contains("Ставить опыт") || a.Contains("Пофантаз")) return (0, 1, "творчество");
                if (a.Contains("Убра") || a.Contains("Помыть") || a.Contains("урок") || a.Contains("Учить") || a.Contains("Полить") || a.Contains("Почитать")) return (1, 1, "старание");
                if (a.Contains("Поболтать") || a.Contains("Пошутить") || a.Contains("Рассказать") || a.Contains("Поиграть") || a.Contains("догонялки")) return (2, 1, "общительность");
                if (a.Contains("Обнять") || a.Contains("комплимент") || a.Contains("Утешить") || a.Contains("Поделиться")) return (3, 1, "доброту");
            }
            return (-1, 0, "просто так");
        }

        public static void Add(List<SocialDef> all)
        {
            all.Add(new SocialDef
            {
                command = "Похвалить {0}", activity = "Хвалит {0}", targetCase = NameCase.Accusative,
                name = "Похвалить", say = "Какой ты молодец!", yes = "Правда?! Ура!", no = "Ну и ладно…",
                relYes = 4, sure = true, duration = 12f,
                available = (a, b) => b.IsChild && !a.IsChild && a.home == b.home,
                want = (a, b) => -0.05f + (a.pers.A - 5) * 0.03f,
                accept = (a, b) => 1f,
                onSuccess = (a, b) => Praise(a, b, true),
            });
            all.Add(new SocialDef
            {
                command = "Отругать {0}", activity = "Ругает {0}", targetCase = NameCase.Accusative,
                name = "Отругать", say = "Ну-ка, что это такое?!", yes = "Я больше не буду…", no = "Я больше не буду…",
                relYes = -3, sure = true, duration = 12f,
                available = (a, b) => b.IsChild && !a.IsChild && a.home == b.home,
                want = (a, b) => -0.3f + (5 - a.pers.A) * 0.03f + (a.pers.C - 5) * 0.02f,
                accept = (a, b) => 1f,
                onSuccess = (a, b) => Praise(a, b, false),
            });
        }

        static void Praise(Fox parent, Fox kid, bool praise)
        {
            var G = Game.I;
            var t = TraitOf(kid);
            bool tooOften = G.minutes - kid.lastParented < Cooldown;
            kid.lastParented = G.minutes;
            string kidName = kid.NameIn(NameCase.Accusative);
            if (tooOften)
            {
                kid.parentedStreak++;
                if (praise)
                {
                    kid.AddMoodlet("Хвалят за всё подряд", 0, 240);
                    kid.Say("Ты всем так говоришь…");
                    G.LogPair(parent, kid, $"{parent.foxName} опять хвалит {kidName}. Похвала, когда её слишком много, уже не радует.");
                }
                else
                {
                    kid.AddMoodlet("Ругают постоянно", -10, 360);
                    kid.MentalChange(-5f);
                    Social.AddRel(kid, parent, -6f);
                    kid.Say("Вечно я виноват" + (kid.female ? "а" : "") + "!");
                    if (Random.value < 0.25f) Shift(kid, 4, 1, "стал" + (kid.female ? "а" : "") + " тревожнее");
                    G.LogPair(parent, kid, $"{parent.foxName} снова ругает {kidName}. Слишком часто: лисёнок обижается и тревожится.");
                }
                return;
            }
            kid.parentedStreak = 0;
            if (praise)
            {
                kid.AddMoodlet("Меня похвалили", 7, 240);
                kid.MentalChange(2f);
                if (t.trait >= 0 && Random.value < 0.22f) Shift(kid, t.trait, t.sign, null);
                else if (Random.value < 0.08f) Shift(kid, 4, -1, "стал" + (kid.female ? "а" : "") + " спокойнее");
                G.LogPair(parent, kid, $"{parent.foxName} хвалит {kidName} за {t.what}.");
            }
            else
            {
                kid.AddMoodlet("Отругали", -6, 180);
                if (t.trait >= 0 && Random.value < 0.22f) Shift(kid, t.trait, -t.sign, null);
                if (Random.value < 0.1f) Shift(kid, 4, 1, "стал" + (kid.female ? "а" : "") + " тревожнее");
                G.LogPair(parent, kid, $"{parent.foxName} ругает {kidName} за {t.what}.");
            }
        }

        static readonly string[] Up = { "любопытнее", "старательнее", "общительнее", "добрее", "тревожнее" };
        static readonly string[] Down = { "осторожнее с новым", "беспечнее", "тише", "колючее", "спокойнее" };

        static void Shift(Fox kid, int trait, int sign, string text)
        {
            int before = kid.pers.v[trait];
            kid.pers.v[trait] = Mathf.Clamp(before + sign, 1, 10);
            if (kid.pers.v[trait] == before) return;
            string how = text ?? ("стал" + (kid.female ? "а" : "") + " " + (sign > 0 ? Up[trait] : Down[trait]));
            Game.I.LogFox(kid, $"{kid.foxName} {how}: характер чуть-чуть изменился.");
            kid.Remember("Характер изменился: " + how);
        }
    }
}
