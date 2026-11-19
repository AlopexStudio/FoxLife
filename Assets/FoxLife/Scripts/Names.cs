using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
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
