using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Падежи имени — не таблицей, а маленькой историей с пропусками: «Без ___ в долине было бы скучно».
    /// Игрок сразу видит, какая форма нужна и как она будет звучать. Фразы зависят от характера и увлечений.
    /// </summary>
    public static class NameStory
    {
        public struct Line
        {
            public string pre, post;
            public Line(string a, string b) { pre = a; post = b; }
        }

        // для каждого падежа — общие фразы и фразы по увлечениям/характеру (ключ: id увлечения или черта)
        static readonly Dictionary<NameCase, List<(string key, Line line)>> Lines = new Dictionary<NameCase, List<(string, Line)>>
        {
            [NameCase.Genitive] = new List<(string, Line)>
            {
                ("", new Line("Без ", " в долине было бы скучно.")),
                ("", new Line("У ", " сегодня отличное настроение.")),
                ("", new Line("Это любимая кружка ", ".")),
                ("garden", new Line("На грядках ", " выросла огромная тыква.")),
                ("games", new Line("Рекорд ", " до сих пор никто не побил.")),
                ("books", new Line("Книжная полка ", " ломится от книг.")),
                ("football", new Line("Гол ", " вошёл в историю долины!")),
                ("cook", new Line("Пироги ", " — лучшие во всей округе.")),
                ("art", new Line("Картины ", " висят в каждом доме.")),
                ("fashion", new Line("Шарфик ", " — последний писк моды.")),
                ("C+", new Line("В комнате ", " всегда идеальный порядок.")),
                ("C-", new Line("Носки ", " снова нашлись под диваном.")),
            },
            [NameCase.Dative] = new List<(string, Line)>
            {
                ("", new Line("Друзья пришли в гости к ", ".")),
                ("", new Line("Соседи машут лапой ", ".")),
                ("A+", new Line("Все несут свои секреты ", ".")),
                ("E-", new Line("Иногда ", " хочется тишины.")),
                ("E+", new Line("Без компании ", " скучно.")),
                ("rock", new Line("Аплодисменты — ", "!")),
                ("classic", new Line("Оркестр играет вальс ", ".")),
                ("tea", new Line("Заварим чай ", "?")),
                ("travel", new Line("Дорога сама зовёт ", " в путь.")),
            },
            [NameCase.Accusative] = new List<(string, Line)>
            {
                ("", new Line("Все в долине любят ", ".")),
                ("", new Line("Я позову ", " на чай.")),
                ("O+", new Line("Любопытство снова завело ", " в приключения.")),
                ("N+", new Line("Гроза немножко пугает ", ".")),
                ("space", new Line("Звёзды манят ", " всё сильнее.")),
                ("bugs", new Line("Стрекоза выбрала ", " своим другом.")),
                ("detective", new Line("Загадка не отпускает ", " третий день.")),
                ("photo", new Line("Фотограф поймал ", " в лучшем ракурсе.")),
            },
            [NameCase.Instrumental] = new List<(string, Line)>
            {
                ("", new Line("Как здорово дружить с ", "!")),
                ("", new Line("Мы гордимся ", ".")),
                ("C+", new Line("Порядок в доме наведён ", ".")),
                ("dance", new Line("Весь вечер танцевали с ", ".")),
                ("boardgames", new Line("Никто не хочет играть в шахматы с ", " — всегда выигрывает!")),
                ("fishing", new Line("На рассвете на рыбалку — с ", ".")),
                ("knit", new Line("Тёплый шарф связан ", ".")),
            },
            [NameCase.Prepositional] = new List<(string, Line)>
            {
                ("", new Line("Мы часто вспоминаем {о} ", ".")),
                ("", new Line("Все в долине говорят {о} ", ".")),
                ("A+", new Line("Каждый думает {о} ", " с теплом.")),
                ("history", new Line("Когда-нибудь {о} ", " напишут в летописи.")),
                ("politics", new Line("{О} ", " пишут в городской газете.")),
                ("movies", new Line("Снимут фильм {о} ", "!")),
                ("science", new Line("Учёные спорят {о} ", ".")),
            },
        };

        static bool Fits(Fox f, string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            if (key.Length == 2 && (key[1] == '+' || key[1] == '-'))
            {
                int v = key[0] switch { 'O' => f.pers.O, 'C' => f.pers.C, 'E' => f.pers.E, 'A' => f.pers.A, _ => f.pers.N };
                return key[1] == '+' ? v >= 7 : v <= 3;
            }
            return f.interests.Contains(key);
        }

        /// <summary>Фраза для падежа: по увлечениям и характеру, если есть подходящая, иначе общая. Не мигает: выбор по лисичке.</summary>
        public static Line For(Fox f, NameCase c)
        {
            var all = Lines[c];
            var special = all.FindAll(x => x.key != "" && Fits(f, x.key));
            var pool = special.Count > 0 ? special : all.FindAll(x => x.key == "");
            int h = Mathf.Abs(((f.id ?? f.foxName ?? "").GetHashCode() * 31 + (int)c * 7) % 9973);
            return pool[h % pool.Count].line;
        }

        /// <summary>«о»/«об» перед именем (об Огоньке, о Рыжике).</summary>
        public static string About(string form, bool cap)
        {
            string p = !string.IsNullOrEmpty(form) && "аоиуэыАОИУЭЫ".IndexOf(form[0]) >= 0 ? "об" : "о";
            return cap ? char.ToUpper(p[0]) + p.Substring(1) : p;
        }

        public static string Fill(string t, string form) => t.Replace("{о}", About(form, false)).Replace("{О}", About(form, true));
    }
}
