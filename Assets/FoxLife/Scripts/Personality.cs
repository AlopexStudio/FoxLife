using UnityEngine;

namespace FoxLife
{
    public enum Trait { Openness = 0, Conscientiousness = 1, Extraversion = 2, Agreeableness = 3, Neuroticism = 4 }

    /// <summary>Пять шкал личности, у каждой значение от 1 до 10.</summary>
    [System.Serializable]
    public class Personality
    {
        public int[] v = { 5, 5, 5, 5, 5 };

        public int this[Trait t]
        {
            get => v[(int)t];
            set => v[(int)t] = Mathf.Clamp(value, 1, 10);
        }

        public int O => v[0];
        public int C => v[1];
        public int E => v[2];
        public int A => v[3];
        public int N => v[4];

        public Personality() { }
        public Personality(int o, int c, int e, int a, int n) { v = new[] { o, c, e, a, n }; }

        public Personality Clone() => new Personality { v = (int[])v.Clone() };

        public static Personality Random()
        {
            var p = new Personality();
            for (int i = 0; i < 5; i++) p.v[i] = UnityEngine.Random.Range(1, 11);
            return p;
        }
    }

    public static class PersonalityInfo
    {
        public static readonly string[] ScaleNames =
        {
            "Открытость новому опыту",
            "Добросовестность",
            "Общительность",
            "Доброжелательность",
            "Невротизм",
        };

        // Степени: 1 | 2–4 | 5–6 | 7–9 | 10
        public static readonly string[,] Tiers =
        {
            { "КОСНЫЙ", "ПРИЗЕМЛЁННЫЙ", "ОТКРЫТЫЙ", "ЛЮБОЗНАТЕЛЬНЫЙ", "НЕНАСЫТНЫЙ" },
            { "БЕЗОТВЕТСТВЕННЫЙ", "БЕСПЕЧНЫЙ", "ИСПОЛНИТЕЛЬНЫЙ", "ПЕДАНТИЧНЫЙ", "ДОТОШНЫЙ" },
            { "НЕЛЮДИМЫЙ", "ЗАМКНУТЫЙ", "ОБЩИТЕЛЬНЫЙ", "ОЖИВЛЁННЫЙ", "НЕУГОМОННЫЙ" },
            { "БЕССЕРДЕЧНЫЙ", "ЧЁРСТВЫЙ", "ОТЗЫВЧИВЫЙ", "ЧУТКИЙ", "СЕРДОБОЛЬНЫЙ" },
            { "ХЛАДНОКРОВНЫЙ", "НЕВОЗМУТИМЫЙ", "СТАБИЛЬНЫЙ", "ЭМОЦИОНАЛЬНЫЙ", "НЕУРАВНОВЕШЕННЫЙ" },
        };

        public static readonly string[,] TierDesc =
        {
            {
                "Любит то, что знает. Новое подозрительно, привычное — свято. Охотно повторяет любимые занятия.",
                "Практичен и предпочитает проверенное. Телевизор понятнее мольберта.",
                "Готов попробовать новое, но и от привычного не отказывается.",
                "Тянется к книгам, искусству и новым идеям. Быстро скучает от однообразия.",
                "Постоянно жаждет нового. Повторять одно и то же — настоящее мучение.",
            },
            {
                "Посуда подождёт. Всё подождёт. О себе вспоминает в последний момент.",
                "Не любит лишних хлопот и берётся за дела, только когда припечёт.",
                "Делает что нужно и вовремя, без фанатизма.",
                "Любит порядок, заранее заботится о себе и о доме.",
                "Не выносит ни малейшего беспорядка. Каждая тарелка должна быть вымыта.",
            },
            {
                "Лучшая компания — собственная. Почти не нуждается в общении.",
                "Общается охотно, но понемногу. Нужно время побыть одному.",
                "Приятный собеседник: хорошо и в компании, и наедине с собой.",
                "Заряжается от общения и быстро скучает в одиночестве.",
                "Ни часа без разговоров. Заговорит с кем угодно о чём угодно.",
            },
            {
                "Чужие чувства — не его забота. Подразнить и поспорить — любимое дело.",
                "Прямолинеен и не склонен к сантиментам.",
                "Дружелюбен и готов помочь, если попросят.",
                "Замечает, когда другим плохо, и спешит поддержать.",
                "Переживает за всех вокруг больше, чем за себя.",
            },
            {
                "Ничто не способно вывести из равновесия.",
                "Спокоен, неприятности переносит легко.",
                "Эмоции в норме: радуется и огорчается в меру.",
                "Всё принимает близко к сердцу — и хорошее, и плохое.",
                "Настроение скачет от восторга до отчаяния без видимой причины.",
            },
        };

        public static int TierIndex(int value)
        {
            if (value <= 1) return 0;
            if (value <= 4) return 1;
            if (value <= 6) return 2;
            if (value <= 9) return 3;
            return 4;
        }

        public static string TierName(int scale, int value) => Tiers[scale, TierIndex(value)];

        /// <summary>Степень с родом: любознательный / любознательная.</summary>
        public static string TierNameFor(int scale, int value, bool female) => female ? Feminine(TierName(scale, value)) : TierName(scale, value);

        public static string Feminine(string w)
        {
            if (string.IsNullOrEmpty(w) || w.Length < 3) return w;
            string end = w.Substring(w.Length - 2), stem = w.Substring(0, w.Length - 2);
            bool up = char.IsUpper(w[w.Length - 1]);
            string ee = end.ToLower();
            string res;
            if (ee == "ый" || ee == "ой") res = "ая";
            else if (ee == "ий") res = "гкхжшщч".IndexOf(char.ToLower(stem[stem.Length - 1])) >= 0 ? "ая" : "яя";
            else return w;
            return stem + (up ? res.ToUpper() : res);
        }
        public static string TierDescription(int scale, int value) => TierDesc[scale, TierIndex(value)];
    }
}
