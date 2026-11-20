using UnityEngine;

namespace FoxLife
{
    public enum Skill { None = -1, Cooking = 0, Creativity = 1, Music = 2, Logic = 3, Charisma = 4, Housework = 5 }

    public static class Skills
    {
        public const int Count = 6;
        public const int MaxLevel = 10;

        public static readonly string[] Names = { "Кулинария", "Творчество", "Музыка", "Логика", "Харизма", "Хозяйство" };

        public static readonly string[] Hints =
        {
            "Растёт у плиты. Ужин сытнее, реже подгорает.",
            "Растёт у мольберта и за романом. Картины и книги дороже.",
            "Растёт от игры на гитаре и пианино. Слушателям нравится всё больше.",
            "Растёт от чтения и компьютера. Нужна для науки.",
            "Растёт в разговорах. Собеседники охотнее соглашаются.",
            "Растёт от уборки и ухода за цветами. Посуда моется быстрее.",
        };

        /// <summary>Опыт, нужный для перехода с уровня level на level+1.</summary>
        public static float XpForLevel(int level) => 60f + level * 45f;

        /// <summary>Скорость обучения зависит от открытости опыту.</summary>
        public static float LearnMul(Personality p) => 0.6f + p.O * 0.08f;
    }

    /// <summary>Карьера: пять ступеней, ключевой навык и черты характера, с которыми работа в радость.</summary>
    public class CareerDef
    {
        public string id, name;
        public string[] levels;
        public string[] instrumentalLevels;
        public float[] wage;          // монет в час на каждой ступени
        public int startHour, endHour;
        public bool[] days;           // Пн..Вс
        public Skill skill;
        public Trait[] likes;         // черты, при высоких значениях которых работа нравится

        public string DaysText
        {
            get
            {
                string[] d = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
                int first = -1, last = -1; bool contiguous = true;
                for (int i = 0; i < 7; i++)
                    if (days[i]) { if (first < 0) first = i; else if (last != i - 1) contiguous = false; last = i; }
                if (first < 0) return "";
                if (contiguous) return first == last ? d[first] : d[first] + "–" + d[last];
                var s = "";
                for (int i = 0; i < 7; i++) if (days[i]) s += (s.Length > 0 ? ", " : "") + d[i];
                return s;
            }
        }

        public string ScheduleText => $"{DaysText}, {startHour}:00–{endHour}:00";
        public int Hours => endHour - startHour;
        public static int SkillNeeded(int nextLevel) => nextLevel * 2; // для ступени 1..4 нужен навык 2,4,6,8

        public float Fit(Personality p)
        {
            float f = 0;
            foreach (var t in likes) f += p[t] - 5.5f;
            return likes.Length > 0 ? f / likes.Length : 0f;
        }
    }

    /// <summary>Где в городе работа и за чем работают.</summary>
    public static class Workplaces
    {
        public static LotKind Place(CareerDef c)
        {
            switch (c.id)
            {
                case "cook": return LotKind.Cafe;
                case "art": return LotKind.Workshop;
                case "science": return LotKind.Observatory;
                case "music": return LotKind.Park;
                case "sales": return LotKind.Square;
                case "teacher": return LotKind.School;
                case "librarian": return LotKind.Library;
                default: return LotKind.School;
            }
        }

        public static string[] Stations(CareerDef c)
        {
            switch (c.id)
            {
                case "cook": return new[] { "stove", "cafecounter", "counter" };
                case "art": return new[] { "easel" };
                case "science": return new[] { "telescope", "computer" };
                case "music": return new[] { "piano", "guitar" };
                case "sales": return new[] { "marketstall" };
                case "teacher": return new[] { "chalkboard" };
                case "librarian": return new[] { "bookshelf", "computer" };
                default: return new[] { "schooldesk" };
            }
        }

        public static string Verb(CareerDef c)
        {
            switch (c.id)
            {
                case "cook": return "Готовит в кафе";
                case "art": return "Рисует в мастерской";
                case "science": return "Изучает звёзды в обсерватории";
                case "music": return "Играет на эстраде";
                case "sales": return "Торгует на площади";
                case "teacher": return "Ведёт урок";
                case "librarian": return "Работает в библиотеке";
                default: return "На уроке";
            }
        }

        public static House.Lot Lot(CareerDef c)
        {
            return Game.I?.house?.lots.Find(l => l.kind == Place(c) && !l.removed);
        }
    }

    public static class Careers
    {
        static readonly bool[] Weekdays = { true, true, true, true, true, false, false };

        public static readonly CareerDef[] All =
        {
            new CareerDef
            {
                id = "cook", name = "Кулинария", skill = Skill.Cooking, startHour = 10, endHour = 16, days = Weekdays,
                instrumentalLevels = "посудомойщиком|помощником повара|поваром|шеф-поваром|ресторатором".Split('|'),
                levels = new[] { "Посудомойщик", "Помощник повара", "Повар", "Шеф-повар", "Ресторатор" },
                wage = new[] { 14f, 20f, 28f, 40f, 58f }, likes = new[] { Trait.Conscientiousness },
            },
            new CareerDef
            {
                id = "art", name = "Искусство", skill = Skill.Creativity, startHour = 11, endHour = 16, days = Weekdays,
                instrumentalLevels = "уличным художником|иллюстратором|художником|известным художником|мастером кисти".Split('|'),
                levels = new[] { "Уличный художник", "Иллюстратор", "Художник", "Известный художник", "Мастер кисти" },
                wage = new[] { 12f, 19f, 29f, 44f, 66f }, likes = new[] { Trait.Openness },
            },
            new CareerDef
            {
                id = "science", name = "Наука", skill = Skill.Logic, startHour = 9, endHour = 17, days = Weekdays,
                instrumentalLevels = "лаборантом|младшим научным сотрудником|исследователем|профессором|академиком".Split('|'),
                levels = new[] { "Лаборант", "Младший научный сотрудник", "Исследователь", "Профессор", "Академик" },
                wage = new[] { 13f, 19f, 27f, 38f, 52f }, likes = new[] { Trait.Openness, Trait.Conscientiousness },
            },
            new CareerDef
            {
                id = "music", name = "Музыка", skill = Skill.Music, startHour = 15, endHour = 21,
                days = new[] { false, false, true, true, true, true, true },
                instrumentalLevels = "уличным музыкантом|музыкантом в кафе|солистом группы|звездой сцены|легендой".Split('|'),
                levels = new[] { "Уличный музыкант", "Музыкант в кафе", "Солист группы", "Звезда сцены", "Легенда" },
                wage = new[] { 12f, 19f, 30f, 46f, 70f }, likes = new[] { Trait.Extraversion, Trait.Openness },
            },
            new CareerDef
            {
                id = "sales", name = "Торговля", skill = Skill.Charisma, startHour = 9, endHour = 15, days = Weekdays,
                instrumentalLevels = "стажёром|продавцом|менеджером|директором магазина|владельцем сети".Split('|'),
                levels = new[] { "Стажёр", "Продавец", "Менеджер", "Директор магазина", "Владелец сети" },
                wage = new[] { 15f, 21f, 29f, 40f, 55f }, likes = new[] { Trait.Extraversion, Trait.Agreeableness },
            },
            new CareerDef
            {
                id = "teacher", name = "Школа", skill = Skill.Charisma, startHour = 8, endHour = 14, days = Weekdays,
                instrumentalLevels = "помощником учителя|учителем|классным руководителем|завучем|директором школы".Split('|'),
                levels = new[] { "Помощник учителя", "Учитель", "Классный руководитель", "Завуч", "Директор школы" },
                wage = new[] { 14f, 20f, 28f, 38f, 52f }, likes = new[] { Trait.Agreeableness, Trait.Conscientiousness },
            },

            new CareerDef
            {
                id = "librarian", name = "Библиотека", skill = Skill.Logic, startHour = 10, endHour = 17, days = Weekdays,
                instrumentalLevels = "помощником библиотекаря|библиотекарем|хранителем фонда|знатоком редких книг|главным библиотекарем".Split('|'),
                levels = new[] { "Помощник библиотекаря", "Библиотекарь", "Хранитель фонда", "Знаток редких книг", "Главный библиотекарь" },
                wage = new[] { 12f, 18f, 26f, 36f, 50f }, likes = new[] { Trait.Openness, Trait.Conscientiousness },
            },

        };

        /// <summary>Школа для лисят и подростков: ходят как на работу, вместо успехов — оценки.</summary>
        public static readonly CareerDef School = new CareerDef
        {
            id = "school", name = "Школа", skill = Skill.Logic, startHour = 8, endHour = 14, days = Weekdays,
            instrumentalLevels = "учеником".Split('|'),
            levels = new[] { "Ученик" }, wage = new[] { 0f }, likes = new[] { Trait.Conscientiousness },
        };

        public static string Grade(float performance) =>
            performance >= 60 ? "отлично" : performance >= 20 ? "хорошо" : performance >= -20 ? "удовлетворительно" : "плохо";

        public static CareerDef Get(string id)
        {
            if (id == "school") return School;
            foreach (var c in All) if (c.id == id) return c;
            return null;
        }
    }
}
