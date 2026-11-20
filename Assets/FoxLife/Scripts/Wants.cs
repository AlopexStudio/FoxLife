using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Событие из жизни лисички: по ним исполняются желания и сбываются страхи.</summary>
    public class GameEvent
    {
        public string kind;   // act, social, social_target, skill, promo, job, fired, buy, style, friend
        public string arg;
        public Fox other;
        public bool success = true;
        public int level;
    }

    [Serializable]
    public class Want
    {
        public string id;
        public string arg;     // имя лисички или навыка
        public string text;
        public int points;
    }

    [Serializable]
    public class Memory
    {
        public int day;
        public string text;
        public string aboutId;
        public bool good = true;
    }

    public class WantDef
    {
        public string id;
        public int points = 150;
        public Func<Fox, float> weight;                 // <= 0 — недоступно
        public Func<Fox, string> pickArg;               // для желаний с параметром
        public Func<string, string> text;
        public Func<GameEvent, string, bool> match;
    }

    public class FearDef
    {
        public string id, text;
        public Func<Fox, float> weight;
        public Func<Fox, bool> check;                   // периодическая проверка
        public Func<Fox, GameEvent, bool> match;        // проверка по событию
    }

    public class PerkDef
    {
        public string id, name, desc;
        public int cost;
    }

    public static class Wants
    {
        static Game G => Game.I;
        static bool Has(string id) => G.furniture.Exists(f => f.def.id == id);
        static bool Act(GameEvent e, string name) => e.kind == "act" && e.arg == name;
        static bool Soc(GameEvent e, string name) => e.kind == "social" && e.arg == name && e.success;

        public static readonly List<WantDef> All = new List<WantDef>
        {
            new WantDef
            {
                id = "talk", points = 120,
                weight = f => G.foxes.Count > 1 ? 0.8f + (f.pers.E - 5) * 0.15f : 0,
                pickArg = f =>
                {
                    var others = G.foxes.FindAll(o => o != f);
                    return others.Count > 0 ? others[UnityEngine.Random.Range(0, others.Count)].foxName : null;
                },
                text = a => "Поболтать: " + a,
                match = (e, a) => (e.kind == "social" || e.kind == "social_target") && e.success && e.other != null && e.other.foxName == a,
            },
            new WantDef
            {
                id = "hug", points = 180,
                weight = f => f.pers.A >= 6 && G.foxes.Exists(o => o != f && Social.Rel(f, o) >= 25) ? 0.4f + (f.pers.A - 5) * 0.12f : 0,
                text = a => "Обнять кого-нибудь", match = (e, a) => Soc(e, "Обнять"),
            },
            new WantDef
            {
                id = "joke", points = 150,
                weight = f => G.foxes.Count > 1 && f.pers.E >= 5 ? 0.4f + (f.pers.E - 5) * 0.1f : 0,
                text = a => "Рассмешить кого-нибудь", match = (e, a) => Soc(e, "Пошутить"),
            },
            new WantDef
            {
                id = "tease", points = 130,
                weight = f => G.foxes.Count > 1 && f.pers.A <= 4 ? 0.4f + (5 - f.pers.A) * 0.12f : 0,
                text = a => "Подразнить кого-нибудь", match = (e, a) => e.kind == "social" && e.arg == "Подразнить",
            },
            new WantDef
            {
                id = "argue", points = 200,
                weight = f => G.foxes.Count > 1 && f.pers.A <= 4 ? 0.3f + (5 - f.pers.A) * 0.08f : 0,
                text = a => "Победить в споре", match = (e, a) => Soc(e, "Поспорить"),
            },
            new WantDef
            {
                id = "comfort", points = 220,
                weight = f => f.pers.A >= 7 && G.foxes.Exists(o => o != f && o.Mood < -15) ? 1.2f : 0,
                text = a => "Поддержать того, кому грустно", match = (e, a) => Soc(e, "Утешить"),
            },
            new WantDef
            {
                id = "paint", points = 150,
                weight = f => Has("easel") && f.pers.O >= 5 ? 0.4f + (f.pers.O - 5) * 0.12f : 0,
                text = a => "Нарисовать картину", match = (e, a) => Act(e, "Рисовать"),
            },
            new WantDef
            {
                id = "read", points = 100,
                weight = f => Has("bookshelf") ? 0.3f + (f.pers.O - 5) * 0.05f : 0,
                text = a => "Почитать книгу", match = (e, a) => Act(e, "Почитать"),
            },
            new WantDef
            {
                id = "tv", points = 90,
                weight = f => Has("tv") && f.pers.O <= 5 ? 0.4f + (5 - f.pers.O) * 0.1f : 0,
                text = a => "Посмотреть любимую передачу", match = (e, a) => Act(e, "Смотреть телевизор"),
            },
            new WantDef
            {
                id = "cook", points = 140,
                weight = f => Has("stove") && f.pers.C >= 5 ? 0.3f + (f.pers.C - 5) * 0.1f : 0,
                text = a => "Приготовить ужин", match = (e, a) => Act(e, "Приготовить ужин"),
            },
            new WantDef
            {
                id = "dishes", points = 110,
                weight = f => f.pers.C >= 6 && G.PlateCount > 0 ? 0.6f + (f.pers.C - 5) * 0.12f : 0,
                text = a => "Перемыть посуду", match = (e, a) => Act(e, "Помыть посуду"),
            },
            new WantDef
            {
                id = "water", points = 80,
                weight = f => Has("plant") && f.pers.C >= 6 ? 0.3f : 0,
                text = a => "Полить цветы", match = (e, a) => Act(e, "Полить"),
            },
            new WantDef
            {
                id = "guitar", points = 140,
                weight = f => Has("guitar") ? 0.3f + (f.pers.O - 5) * 0.05f + (f.pers.E - 5) * 0.05f : 0,
                text = a => "Поиграть на гитаре", match = (e, a) => Act(e, "Играть на гитаре"),
            },
            new WantDef
            {
                id = "novel", points = 180,
                weight = f => Has("computer") && f.pers.O >= 6 ? 0.3f + (f.pers.O - 5) * 0.08f : 0,
                text = a => "Написать главу романа", match = (e, a) => Act(e, "Писать роман"),
            },
            new WantDef
            {
                id = "bath", points = 90,
                weight = f => Has("bath") ? 0.25f : 0,
                text = a => "Понежиться в ванне", match = (e, a) => Act(e, "Принять ванну"),
            },
            new WantDef
            {
                id = "ball", points = 90,
                weight = f => Has("ball") && f.pers.E >= 6 ? 0.3f : 0,
                text = a => "Погонять мячик", match = (e, a) => Act(e, "Поиграть с мячиком"),
            },
            new WantDef
            {
                id = "skill", points = 250,
                weight = f => 0.5f + (f.pers.O - 5) * 0.08f + (f.pers.C - 5) * 0.05f,
                pickArg = f =>
                {
                    // любимые навыки зависят от характера
                    var w = new float[Skills.Count];
                    w[(int)Skill.Cooking] = 1 + Mathf.Max(0, f.pers.C - 5) * 0.3f;
                    w[(int)Skill.Creativity] = 1 + Mathf.Max(0, f.pers.O - 5) * 0.4f;
                    w[(int)Skill.Music] = 1 + Mathf.Max(0, f.pers.E - 5) * 0.2f + Mathf.Max(0, f.pers.O - 5) * 0.2f;
                    w[(int)Skill.Logic] = 1 + Mathf.Max(0, f.pers.O - 5) * 0.2f + Mathf.Max(0, f.pers.C - 5) * 0.2f;
                    w[(int)Skill.Charisma] = 1 + Mathf.Max(0, f.pers.E - 5) * 0.4f;
                    w[(int)Skill.Housework] = 1 + Mathf.Max(0, f.pers.C - 5) * 0.4f;
                    for (int i = 0; i < w.Length; i++) if (f.SkillLevel((Skill)i) >= Skills.MaxLevel) w[i] = 0;
                    float sum = 0; foreach (var x in w) sum += x;
                    if (sum <= 0) return null;
                    float r = UnityEngine.Random.value * sum;
                    for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0) return Skills.Names[i]; }
                    return Skills.Names[0];
                },
                text = a => $"Поднять навык «{a}»",
                match = (e, a) => e.kind == "skill" && e.arg == a,
            },
            new WantDef
            {
                id = "job", points = 200,
                weight = f => f.career == null && f.stage == LifeStage.Adult ? 0.4f + (f.pers.C - 5) * 0.12f : 0,
                text = a => "Устроиться на работу", match = (e, a) => e.kind == "job",
            },
            new WantDef
            {
                id = "promo", points = 400,
                weight = f => f.career != null && f.career.id != "school" && f.careerLevel < 4 ? 0.5f + (f.pers.C - 5) * 0.08f : 0,
                text = a => "Получить повышение", match = (e, a) => e.kind == "promo",
            },
            new WantDef
            {
                id = "buy", points = 160,
                weight = f => 0.25f + (f.pers.O - 5) * 0.1f,
                text = a => "Купить что-нибудь новое в дом", match = (e, a) => e.kind == "buy",
            },
            new WantDef
            {
                id = "style", points = 160,
                weight = f => f.pers.O >= 5 ? 0.3f + (f.pers.O - 5) * 0.12f : 0,
                text = a => "Перекрасить что-нибудь в доме", match = (e, a) => e.kind == "style",
            },
            new WantDef
            {
                id = "love", points = 350,
                weight = f => f.stage >= LifeStage.Adult && string.IsNullOrEmpty(f.partnerId) && G.foxes.Exists(o => o != f && Family.CanRomance(f, o) && string.IsNullOrEmpty(o.partnerId))
                    ? 0.35f + (f.pers.E - 5) * 0.05f + (f.pers.A - 5) * 0.03f : 0,
                text = a => "Найти любовь", match = (e, a) => e.kind == "partner",
            },
            new WantDef
            {
                id = "kiss", points = 160,
                weight = f => f.Partner != null ? 0.5f : 0,
                pickArg = f => f.Partner != null ? f.Partner.foxName : null,
                text = a => "Поцеловать: " + a,
                match = (e, a) => e.kind == "social" && e.arg == "Поцеловать" && e.success && e.other != null && e.other.foxName == a,
            },
            new WantDef
            {
                id = "wed", points = 500,
                weight = f => f.Partner != null && !f.married ? 0.4f + (f.pers.C - 5) * 0.05f : 0,
                text = a => "Сыграть свадьбу", match = (e, a) => e.kind == "married",
            },
            new WantDef
            {
                id = "baby", points = 500,
                weight = f => f.Partner != null && f.stage == LifeStage.Adult && G.HomeCount(f.home) < Game.MaxFoxes && G.KidsAt(f.home) < 3 && G.foxes.Count < Game.TownCap
                    ? (0.2f + (f.pers.A - 5) * 0.06f) * (1f - G.KidsAt(f.home) / 3f) : 0,
                text = a => "Завести лисят", match = (e, a) => e.kind == "baby",
            },
            new WantDef
            {
                id = "playkit", points = 140,
                weight = f => G.foxes.Exists(o => o != f && o.home == f.home && o.stage == LifeStage.Kit) && f.stage >= LifeStage.Teen ? 0.4f + (f.pers.A - 5) * 0.05f : 0,
                text = a => "Поиграть с лисёнком",
                match = (e, a) => e.kind == "social" && e.success && (e.arg == "Поиграть в догонялки" || e.arg == "Поносить лисёнка"),
            },
            new WantDef
            {
                id = "friend", points = 300,
                weight = f => G.foxes.Exists(o => o != f && Social.Rel(f, o) < 40) ? 0.3f + (f.pers.E - 5) * 0.06f + (f.pers.A - 5) * 0.04f : 0,
                text = a => "Подружиться с кем-нибудь", match = (e, a) => e.kind == "friend",
            },
        };

        public static readonly List<FearDef> Fears = new List<FearDef>
        {
            new FearDef
            {
                id = "mess", text = "Беспорядок в доме", weight = f => f.pers.C >= 7 ? f.pers.C - 6 : 0,
                check = f => G.PlateCount >= 3,
            },
            new FearDef
            {
                id = "lonely", text = "Одиночество", weight = f => f.pers.E >= 7 ? f.pers.E - 6 : 0,
                check = f => f.needs[(int)Need.Social] < 15,
            },
            new FearDef
            {
                id = "quarrel", text = "Ссора", weight = f => (f.pers.A >= 7 ? f.pers.A - 6 : 0) + (f.pers.N >= 7 ? f.pers.N - 6 : 0),
                match = (f, e) => (e.kind == "social" || e.kind == "social_target") && !e.success && (e.arg == "Поспорить" || e.arg == "Подразнить"),
            },
            new FearDef
            {
                id = "boredom", text = "Скука", weight = f => f.pers.O >= 7 ? f.pers.O - 6 : 0,
                check = f => f.needs[(int)Need.Fun] < 15,
            },
            new FearDef
            {
                id = "broke", text = "Остаться без денег", weight = f => (f.pers.N >= 7 ? f.pers.N - 6 : 0) + (f.pers.C >= 8 ? 1 : 0),
                check = f => G.money < 100,
            },
            new FearDef
            {
                id = "fired", text = "Потерять работу", weight = f => f.career != null && f.career.id != "school" && f.pers.N >= 6 ? f.pers.N - 5 : 0,
                match = (f, e) => e.kind == "fired",
            },
        };

        public static readonly PerkDef[] Perks =
        {
            new PerkDef { id = "stomach", name = "Крепкий желудок", desc = "Голод растёт на четверть медленнее.", cost = 800 },
            new PerkDef { id = "clean", name = "Чистюля", desc = "Гигиена падает на треть медленнее.", cost = 800 },
            new PerkDef { id = "sleep", name = "Крепкий сон", desc = "Сон восстанавливает бодрость на 30% быстрее.", cost = 1000 },
            new PerkDef { id = "learner", name = "Быстрый ученик", desc = "Навыки растут на 30% быстрее.", cost = 1500 },
            new PerkDef { id = "charm", name = "Душа компании", desc = "Собеседники на 10% охотнее соглашаются.", cost = 1500 },
            new PerkDef { id = "worker", name = "Трудоголик", desc = "Успехи на работе растут быстрее.", cost = 1500 },
            new PerkDef { id = "calm", name = "Толстая шкурка", desc = "Плохие эмоции слабее на 40%.", cost = 2000 },
        };

        public static readonly HashSet<string> AdultOnly = new HashSet<string> { "job", "promo", "cook", "novel", "buy", "style", "love", "wed", "baby", "argue", "kiss", "comfort", "dishes" };
        public static readonly HashSet<string> NotForTeens = new HashSet<string> { "job", "promo", "love", "wed", "baby", "kiss" };

        public static WantDef Get(string id) => All.Find(w => w.id == id);
        public static FearDef GetFear(string id) => Fears.Find(w => w.id == id);
    }
}
