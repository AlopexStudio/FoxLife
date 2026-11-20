using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public enum NameCase { Nominative, Genitive, Dative, Accusative, Instrumental, Prepositional }

    /// <summary>Имена лисичек по полу: мальчика не назовут Звёздочкой.</summary>
    public static class Names
    {
        public static readonly string[] Male =
        {
            "Рыжик", "Огонёк", "Хвостик", "Медок", "Каштан", "Уголёк", "Лучик", "Пряник", "Желудь", "Сухарик", "Фантик", "Орешек",
            "Ёжик", "Бублик", "Кедр", "Листик", "Шалфей", "Тмин", "Василёк", "Вереск", "Пушок", "Ветерок", "Дымок", "Коржик",
            "Боровик", "Тимошка", "Мох", "Кузнечик", "Пирожок", "Сухоцвет", "Лопушок", "Бочонок",
        };

        public static readonly string[] Female =
        {
            "Шишка", "Корица", "Тыковка", "Брусника", "Морошка", "Искорка", "Пушинка", "Рябинка", "Зефирка", "Кнопка", "Бусинка",
            "Пуговка", "Вишенка", "Ромашка", "Малинка", "Тучка", "Звёздочка", "Ириска", "Черника", "Ольха", "Клюква", "Дымка",
            "Сосенка", "Мята", "Облепиха", "Ежевика", "Калинка", "Белочка", "Капелька", "Снежинка", "Ягодка", "Мелиса",
        };

        static readonly Dictionary<string, string[]> Forms = BuildForms();

        static Dictionary<string, string[]> BuildForms()
        {
            var forms = new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string name in Male)
            {
                string stem = name;
                switch (name)
                {
                    case "Огонёк": stem = "Огоньк"; break;
                    case "Уголёк": stem = "Угольк"; break;
                    case "Василёк": stem = "Васильк"; break;
                    case "Орешек": stem = "Орешк"; break;
                    case "Медок": stem = "Медк"; break;
                    case "Ветерок": stem = "Ветерк"; break;
                    case "Пушок": stem = "Пушк"; break;
                    case "Пирожок": stem = "Пирожк"; break;
                    case "Лопушок": stem = "Лопушк"; break;
                    case "Бочонок": stem = "Бочонк"; break;
                    case "Мох": stem = "Мх"; break;
                }
                if (name == "Тимошка") forms[name] = AForms(name);
                else if (name.EndsWith("ь") || name.EndsWith("й"))
                {
                    stem = name.Substring(0, name.Length - 1);
                    forms[name] = new[] { stem + "я", stem + "ю", stem + "я", stem + "ем", stem + "е" };
                }
                else forms[name] = new[] { stem + "а", stem + "у", stem + "а", stem + "ом", stem + "е" };
            }
            forms["Жёлудь"] = new[] { "Жёлудя", "Жёлудю", "Жёлудя", "Жёлудем", "Жёлуде" };
            foreach (string name in Female) forms[name] = AForms(name);
            forms["Лисичка"] = AForms("Лисичка");
            return forms;
        }

        static string[] AForms(string name)
        {
            string stem = name.Substring(0, name.Length - 1);
            string gen = "гкхжчшщ".IndexOf(stem[stem.Length - 1]) >= 0 ? "и" : "ы";
            return new[] { stem + gen, stem + "е", stem + "у", stem + (name == "Корица" ? "ей" : "ой"), stem + "е" };
        }

        static readonly string[] FoxForms = { "Лиса", "Лису", "Лиса", "Лисом", "Лисе" };

        public static string DefaultForm(string name, NameCase nameCase)
        {
            if (nameCase == NameCase.Nominative || string.IsNullOrEmpty(name)) return name;
            int index = (int)nameCase - 1;
            // При исчерпании списка игра выдаёт имена вроде «Лис 12».
            int space = name.LastIndexOf(' ');
            string root = name;
            string suffix = "";
            if (space > 0 && int.TryParse(name.Substring(space + 1), out _))
            {
                root = name.Substring(0, space);
                suffix = name.Substring(space);
            }
            if (root.Equals("Лис", System.StringComparison.OrdinalIgnoreCase)) return FoxForms[index] + suffix;
            if (!Forms.TryGetValue(root, out var forms)) return name;
            return forms[index] + suffix;
        }

        public const int CaseCount = 5, MaxFormLength = 40;

        public static string[] CopyOverrides(string[] source)
        {
            if (source == null || source.Length != CaseCount) return null;
            var forms = new string[CaseCount];
            bool any = false;
            for (int i = 0; i < forms.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(source[i])) continue;
                forms[i] = source[i].Length > MaxFormLength ? source[i].Substring(0, MaxFormLength) : source[i];
                any = true;
            }
            return any ? forms : null;
        }

        public static string Pick(bool female, System.Func<string, bool> taken, System.Random rnd = null)
        {
            var pool = female ? Female : Male;
            int start = rnd != null ? rnd.Next(pool.Length) : Random.Range(0, pool.Length);
            for (int i = 0; i < pool.Length; i++)
            {
                var n = pool[(start + i) % pool.Length];
                if (!taken(n)) return n;
            }
            return (female ? "Лисичка " : "Лис ") + Random.Range(2, 99);
        }
    }
}
