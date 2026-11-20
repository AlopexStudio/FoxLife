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

    /// <summary>Уровень мечты: своё звание и шаги (у каждого следующего уровня требования выше).</summary>
    public class DreamLevel
    {
        public string title;
        public DreamStep[] steps;
    }

    public class DreamDef
    {
        public string id, name, desc;
        public DreamLevel[] levels;
        public int Levels => levels.Length;
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
        public const int Reward = 4200; // за всю мечту (600 + 1200 + 2400)

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

        static DreamLevel L(string title, params DreamStep[] steps) => new DreamLevel { title = title, steps = steps };
        static DreamStep S(string t, Func<Fox, float> p) => new DreamStep(t, p);

        public static readonly List<DreamDef> All = new List<DreamDef>
        {
            new DreamDef { id = "artist", name = "Великий художник", desc = "Картины, которые захотят повесить у себя все в долине.",
                weight = f => 0.4f + (f.pers.O - 5) * 0.25f,
                levels = new[] {
                    L("Начинающий художник", S("Продать 2 картины", f => Cnt(f, "painting", 2)), S("Творчество 3", f => Lvl(f, Skill.Creativity, 3))),
                    L("Известный художник", S("Продать 10 картин", f => Cnt(f, "painting", 10)), S("Творчество 7", f => Lvl(f, Skill.Creativity, 7))),
                    L("Великий художник", S("Продать 25 картин", f => Cnt(f, "painting", 25)), S("Творчество 10", f => Lvl(f, Skill.Creativity, 10))) } },
            new DreamDef { id = "writer", name = "Писатель", desc = "Издать несколько романов — и чтобы их читали у камина.",
                weight = f => 0.3f + (f.pers.O - 5) * 0.15f + (f.pers.C - 5) * 0.12f + (5 - f.pers.E) * 0.06f,
                levels = new[] {
                    L("Автор рассказов", S("Написать 5 глав", f => Cnt(f, "chapter", 5)), S("Творчество 3", f => Lvl(f, Skill.Creativity, 3))),
                    L("Писатель", S("Издать первый роман", f => Cnt(f, "novel", 1)), S("Творчество 7", f => Lvl(f, Skill.Creativity, 7))),
                    L("Классик", S("Издать 4 романа", f => Cnt(f, "novel", 4)), S("Творчество 10", f => Lvl(f, Skill.Creativity, 10))) } },
            new DreamDef { id = "virtuoso", name = "Виртуоз", desc = "Играть так, чтобы соседи открывали окна и слушали.",
                weight = f => 0.3f + (f.pers.O - 5) * 0.12f + (f.pers.E - 5) * 0.1f,
                levels = new[] {
                    L("Любитель музыки", S("Сыграть 8 раз", f => Cnt(f, "music", 8)), S("Музыка 3", f => Lvl(f, Skill.Music, 3))),
                    L("Музыкант", S("Сыграть 30 раз", f => Cnt(f, "music", 30)), S("Музыка 7", f => Lvl(f, Skill.Music, 7))),
                    L("Виртуоз", S("Сыграть 80 раз", f => Cnt(f, "music", 80)), S("Музыка 10", f => Lvl(f, Skill.Music, 10))) } },
            new DreamDef { id = "chef", name = "Шеф-повар", desc = "Чтобы за стол садились всей семьёй и просили добавки.",
                minStage = LifeStage.Adult,
                weight = f => 0.3f + (f.pers.C - 5) * 0.1f + (f.pers.A - 5) * 0.12f,
                levels = new[] {
                    L("Домашний повар", S("Приготовить 8 блюд", f => Cnt(f, "cook", 8)), S("Кулинария 3", f => Lvl(f, Skill.Cooking, 3))),
                    L("Повар", S("Приготовить 30 блюд", f => Cnt(f, "cook", 30)), S("Кулинария 7", f => Lvl(f, Skill.Cooking, 7))),
                    L("Шеф-повар", S("Приготовить 80 блюд", f => Cnt(f, "cook", 80)), S("Кулинария 10", f => Lvl(f, Skill.Cooking, 10))) } },
            new DreamDef { id = "career", name = "Вершина карьеры", desc = "Дорасти до самой верхней ступеньки в своём деле.",
                minStage = LifeStage.Adult,
                weight = f => 0.3f + (f.pers.C - 5) * 0.2f,
                levels = new[] {
                    L("Своё дело", S("Устроиться на работу", f => f.career != null && f.career.id != "school" || f.Tally("career_top") > 0 ? 1 : 0)),
                    L("Ценный работник", S("Подняться до 3-й ступени", f => f.Tally("career_lvl") >= 2 ? 1 : f.Tally("career_lvl") / 2f)),
                    L("Вершина карьеры", S("Достичь высшей ступени", f => f.Tally("career_top") > 0 ? 1 : f.Tally("career_lvl") / 4f)) } },
            new DreamDef { id = "family", name = "Большая семья", desc = "Полный дом лисят и любовь на всю жизнь.",
                minStage = LifeStage.Adult,
                weight = f => 0.35f + (f.pers.A - 5) * 0.12f + (f.pers.E - 5) * 0.06f,
                levels = new[] {
                    L("Любовь", S("Найти любовь", f => f.married ? 1 : Romance(f)), S("Пожениться", f => f.married ? 1 : 0)),
                    L("Своя семья", S("Вырастить лисёнка до подростка", f => Kids(f, 1))),
                    L("Большая семья", S("Вырастить 3 лисят", f => Kids(f, 3))) } },
            new DreamDef { id = "social", name = "Душа компании", desc = "Друзья в каждом доме долины.",
                weight = f => 0.3f + (f.pers.E - 5) * 0.25f,
                levels = new[] {
                    L("Приятный собеседник", S("2 друга", f => Friends(f, 2)), S("Харизма 3", f => Lvl(f, Skill.Charisma, 3))),
                    L("Всеобщий друг", S("5 друзей", f => Friends(f, 5)), S("Харизма 7", f => Lvl(f, Skill.Charisma, 7))),
                    L("Душа компании", S("10 друзей", f => Friends(f, 10)), S("Харизма 10", f => Lvl(f, Skill.Charisma, 10))) } },
            new DreamDef { id = "home", name = "Дом мечты", desc = "Обставить дом так уютно, чтобы из него не хотелось уходить.",
                minStage = LifeStage.Adult,
                weight = f => 0.3f + (f.pers.C - 5) * 0.08f + (5 - f.pers.O) * 0.06f,
                levels = new[] {
                    L("Уютный уголок", S("Вещей в доме на 6 000", f => HomeValue(f, 6000))),
                    L("Красивый дом", S("Вещей в доме на 14 000", f => HomeValue(f, 14000))),
                    L("Дом мечты", S("Вещей в доме на 25 000", f => HomeValue(f, 25000))) } },
            new DreamDef { id = "sage", name = "Мудрец", desc = "Знать ответ почти на любой вопрос.",
                weight = f => 0.25f + (f.pers.O - 5) * 0.1f + (f.pers.C - 5) * 0.1f + (5 - f.pers.E) * 0.05f,
                levels = new[] {
                    L("Любознательный", S("Учиться и читать 10 раз", f => Cnt(f, "study", 10)), S("Логика 3", f => Lvl(f, Skill.Logic, 3))),
                    L("Знаток", S("Учиться и читать 30 раз", f => Cnt(f, "study", 30)), S("Логика 7", f => Lvl(f, Skill.Logic, 7))),
                    L("Мудрец", S("Учиться и читать 70 раз", f => Cnt(f, "study", 70)), S("Логика 10", f => Lvl(f, Skill.Logic, 10))) } },
            new DreamDef { id = "tidy", name = "Образцовое хозяйство", desc = "Чистота, порядок и цветы на каждом подоконнике.",
                weight = f => 0.25f + (f.pers.C - 5) * 0.2f,
                levels = new[] {
                    L("Хозяюшка", S("Навести порядок 10 раз", f => Cnt(f, "tidy", 10)), S("Хозяйство 3", f => Lvl(f, Skill.Housework, 3))),
                    L("Мастер на все лапы", S("Навести порядок 35 раз", f => Cnt(f, "tidy", 35)), S("Хозяйство 7", f => Lvl(f, Skill.Housework, 7))),
                    L("Образцовое хозяйство", S("Навести порядок 80 раз", f => Cnt(f, "tidy", 80)), S("Хозяйство 10", f => Lvl(f, Skill.Housework, 10))) } },
        };

        /// <summary>Награда за уровень мечты (очки счастья): чем выше, тем щедрее.</summary>
        public static readonly int[] LevelReward = { 600, 1200, 2400 };

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

        /// <summary>Сколько сделано на текущем уровне мечты (0..1).</summary>
        public static float Progress(Fox f, DreamDef d) => LevelProgress(f, d, Mathf.Clamp(f.dreamLevel, 0, d.Levels - 1));

        public static float LevelProgress(Fox f, DreamDef d, int lvl)
        {
            var st = d.levels[lvl].steps;
            float s = 0;
            foreach (var x in st) s += Mathf.Clamp01(x.progress(f));
            return s / st.Length;
        }

        /// <summary>Вся мечта целиком (0..1): пройденные уровни плюс текущий.</summary>
        public static float Total(Fox f, DreamDef d) => (Mathf.Clamp(f.dreamLevel, 0, d.Levels) + (f.dreamLevel < d.Levels ? Progress(f, d) : 0)) / d.Levels;
    }
}
