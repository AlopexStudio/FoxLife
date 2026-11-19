using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Шаг мечты: что сделать и насколько уже сделано (0..1).</summary>
    public class DreamStep
    {
        public string text;
        public Func<Fox, float> progress;
        public DreamStep(string t, Func<Fox, float> p) { text = t; progress = p; }
    }

    public class DreamDef
    {
        public string id, name, desc;
        public DreamStep[] steps;
        public Func<Fox, float> weight;   // насколько мечта подходит характеру
        public LifeStage minStage = LifeStage.Teen;
    }

    /// <summary>
    /// Мечта всей жизни: большая цель лисички из нескольких шагов (стать великим художником, вырастить большую семью,
    /// обустроить дом мечты…). Даёт смысл остальному: навыкам, работе, деньгам, отношениям. Исполнилась —
    /// много очков счастья, долгая радость, запись в дневнике, и можно мечтать о новом.
    /// </summary>
    public static class Dreams
    {
        public const int Reward = 3000;

        static float Lvl(Fox f, Skill s, int need) => Mathf.Clamp01(f.SkillLevel(s) / (float)need);
        static float Cnt(Fox f, string key, int need) => Mathf.Clamp01(f.Tally(key) / (float)need);
        static float Friends(Fox f, int need) { int n = 0; foreach (var kv in f.rel) if (kv.Key != null && kv.Value >= 40f) n++; return Mathf.Clamp01(n / (float)need); }
        static float Kids(Fox f, int need)
        {
            int n = 0;
            foreach (var o in Game.I.foxes) if (o != null && (o.motherId == f.id || o.fatherId == f.id) && o.stage >= LifeStage.Teen) n++;
            return Mathf.Clamp01(n / (float)need);
        }
        static float HomeValue(Fox f, int need) => Mathf.Clamp01(Game.I.LotFurnitureValue(f.home) / (float)need);
        static float Romance(Fox f) { float best = 0; foreach (var kv in f.romance) best = Mathf.Max(best, kv.Value); return Mathf.Clamp01(best / 60f); }

        public static readonly List<DreamDef> All = new List<DreamDef>
        {
            new DreamDef { id = "artist", name = "Великий художник", desc = "Картины, которые захотят повесить у себя все в долине.",
                weight = f => 0.4f + (f.pers.O - 5) * 0.25f,
                steps = new[] {
                    new DreamStep("Продать 3 картины", f => Cnt(f, "painting", 3)),
                    new DreamStep("Творчество 6", f => Lvl(f, Skill.Creativity, 6)),
                    new DreamStep("Продать 15 картин", f => Cnt(f, "painting", 15)),
                    new DreamStep("Творчество 10", f => Lvl(f, Skill.Creativity, 10)) } },
            new DreamDef { id = "writer", name = "Писатель", desc = "Издать несколько романов — и чтобы их читали у камина.",
                weight = f => 0.3f + (f.pers.O - 5) * 0.15f + (f.pers.C - 5) * 0.12f + (5 - f.pers.E) * 0.06f,
                steps = new[] {
                    new DreamStep("Написать 5 глав", f => Cnt(f, "chapter", 5)),
                    new DreamStep("Издать первый роман", f => Cnt(f, "novel", 1)),
                    new DreamStep("Творчество 8", f => Lvl(f, Skill.Creativity, 8)),
                    new DreamStep("Издать 3 романа", f => Cnt(f, "novel", 3)) } },
            new DreamDef { id = "virtuoso", name = "Виртуоз", desc = "Играть так, чтобы соседи открывали окна и слушали.",
                weight = f => 0.3f + (f.pers.O - 5) * 0.12f + (f.pers.E - 5) * 0.1f,
                steps = new[] {
                    new DreamStep("Сыграть 10 раз", f => Cnt(f, "music", 10)),
                    new DreamStep("Музыка 6", f => Lvl(f, Skill.Music, 6)),
                    new DreamStep("Сыграть 40 раз", f => Cnt(f, "music", 40)),
                    new DreamStep("Музыка 10", f => Lvl(f, Skill.Music, 10)) } },
            new DreamDef { id = "chef", name = "Шеф-повар", desc = "Чтобы за стол садились всей семьёй и просили добавки.",
                minStage = LifeStage.Adult,
                weight = f => 0.3f + (f.pers.C - 5) * 0.1f + (f.pers.A - 5) * 0.12f,
                steps = new[] {
                    new DreamStep("Приготовить 10 блюд", f => Cnt(f, "cook", 10)),
                    new DreamStep("Кулинария 6", f => Lvl(f, Skill.Cooking, 6)),
                    new DreamStep("Приготовить 50 блюд", f => Cnt(f, "cook", 50)),
                    new DreamStep("Кулинария 10", f => Lvl(f, Skill.Cooking, 10)) } },
            new DreamDef { id = "career", name = "Вершина карьеры", desc = "Дорасти до самой верхней ступеньки в своём деле.",
                minStage = LifeStage.Adult,
                weight = f => 0.3f + (f.pers.C - 5) * 0.2f,
                steps = new[] {
                    new DreamStep("Устроиться на работу", f => f.career != null && f.career.id != "school" || f.Tally("career_top") > 0 ? 1 : 0),
                    new DreamStep("Подняться до 3-й ступени", f => f.Tally("career_lvl") >= 2 ? 1 : f.Tally("career_lvl") / 2f),
                    new DreamStep("Достичь высшей ступени", f => f.Tally("career_top") > 0 ? 1 : f.Tally("career_lvl") / 4f) } },
            new DreamDef { id = "family", name = "Большая семья", desc = "Полный дом лисят и любовь на всю жизнь.",
                minStage = LifeStage.Adult,
                weight = f => 0.35f + (f.pers.A - 5) * 0.12f + (f.pers.E - 5) * 0.06f,
                steps = new[] {
                    new DreamStep("Найти любовь", f => f.married ? 1 : Romance(f)),
                    new DreamStep("Пожениться", f => f.married ? 1 : 0),
                    new DreamStep("Вырастить 2 лисят до подростков", f => Kids(f, 2)),
                    new DreamStep("Вырастить 3 лисят", f => Kids(f, 3)) } },
            new DreamDef { id = "social", name = "Душа компании", desc = "Друзья в каждом доме долины.",
                weight = f => 0.3f + (f.pers.E - 5) * 0.25f,
                steps = new[] {
                    new DreamStep("3 друга", f => Friends(f, 3)),
                    new DreamStep("Харизма 6", f => Lvl(f, Skill.Charisma, 6)),
                    new DreamStep("8 друзей", f => Friends(f, 8)),
                    new DreamStep("Харизма 10", f => Lvl(f, Skill.Charisma, 10)) } },
            new DreamDef { id = "home", name = "Дом мечты", desc = "Самый уютный дом в долине: обставить его так, чтобы не хотелось уходить.",
                minStage = LifeStage.Adult,
                weight = f => 0.3f + (f.pers.C - 5) * 0.08f + (5 - f.pers.O) * 0.06f,
                steps = new[] {
                    new DreamStep("Вещей в доме на 6 000", f => HomeValue(f, 6000)),
                    new DreamStep("Вещей в доме на 14 000", f => HomeValue(f, 14000)),
                    new DreamStep("Вещей в доме на 25 000", f => HomeValue(f, 25000)) } },
            new DreamDef { id = "sage", name = "Мудрец", desc = "Знать ответ почти на любой вопрос.",
                weight = f => 0.25f + (f.pers.O - 5) * 0.1f + (f.pers.C - 5) * 0.1f + (5 - f.pers.E) * 0.05f,
                steps = new[] {
                    new DreamStep("Логика 5", f => Lvl(f, Skill.Logic, 5)),
                    new DreamStep("Учиться и читать 30 раз", f => Cnt(f, "study", 30)),
                    new DreamStep("Логика 10", f => Lvl(f, Skill.Logic, 10)) } },
            new DreamDef { id = "tidy", name = "Образцовое хозяйство", desc = "Чистота, порядок и цветы на каждом подоконнике.",
                weight = f => 0.25f + (f.pers.C - 5) * 0.2f,
                steps = new[] {
                    new DreamStep("Хозяйство 5", f => Lvl(f, Skill.Housework, 5)),
                    new DreamStep("Навести порядок 40 раз", f => Cnt(f, "tidy", 40)),
                    new DreamStep("Хозяйство 10", f => Lvl(f, Skill.Housework, 10)) } },
        };

        public static DreamDef Get(string id) => id == null ? null : All.Find(d => d.id == id);

        /// <summary>Мечта по характеру (уже исполненные не повторяются, если есть другие).</summary>
        public static DreamDef Pick(Fox f, System.Random rnd = null)
        {
            var opts = All.FindAll(d => f.stage >= d.minStage && !f.dreamsDone.Contains(d.id));
            if (opts.Count == 0) opts = All.FindAll(d => f.stage >= d.minStage);
            if (opts.Count == 0) return null;
            float sum = 0;
            var w = new float[opts.Count];
            for (int i = 0; i < opts.Count; i++) { w[i] = Mathf.Max(0.05f, opts[i].weight(f)); sum += w[i]; }
            float r = (rnd != null ? (float)rnd.NextDouble() : UnityEngine.Random.value) * sum;
            for (int i = 0; i < opts.Count; i++) { r -= w[i]; if (r <= 0) return opts[i]; }
            return opts[opts.Count - 1];
        }

        public static float Progress(Fox f, DreamDef d)
        {
            float s = 0;
            foreach (var st in d.steps) s += Mathf.Clamp01(st.progress(f));
            return s / d.steps.Length;
        }
    }
}
