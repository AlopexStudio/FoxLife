using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>События в городе по дням недели: ярмарка, концерты, пикник, вечер у костра, звёздная ночь.</summary>
    public static class TownEvents
    {
        public class Ev
        {
            public string name, mood, text;
            public LotKind place;
            public int[] days;   // 0 — понедельник … 6 — воскресенье
            public float from, to;
        }

        public static readonly Ev[] All =
        {
            new Ev { name = "Ярмарка", place = LotKind.Square, days = new[] { 5 }, from = 10, to = 19, mood = "Весёлая ярмарка", text = "Будут фрукты, мёд, пряники, музыка и танцы!" },
            new Ev { name = "Концерт на эстраде", place = LotKind.Park, days = new[] { 4, 6 }, from = 18, to = 21, mood = "Чудесный концерт", text = "Приходи послушать музыкантов." },
            new Ev { name = "Пикник", place = LotKind.Park, days = new[] { 6 }, from = 11, to = 16, mood = "Пикник с друзьями", text = "Зови друзей, захвати что-нибудь вкусное." },
            new Ev { name = "Вечер у костра", place = LotKind.Camp, days = new[] { 5 }, from = 19, to = 23.5f, mood = "Песни у костра", text = "Расскажем истории, споём песни и пожарим зефир." },
            new Ev { name = "Звёздная ночь", place = LotKind.Observatory, days = new[] { 2 }, from = 20.5f, to = 24, mood = "Звёздная ночь", text = "Гостям покажут звёзды и планеты." },
            new Ev { name = "Читательский клуб", place = LotKind.Library, days = new[] { 3 }, from = 16, to = 19, mood = "Книжный клуб", text = "Почитаем вслух и поспорим о книгах." },
        };

        public static int DayOfWeek(int day) => (day - 1) % 7;

        public static Ev Active(LotKind place, int day, float hour)
        {
            int dw = DayOfWeek(day);
            foreach (var e in All)
                if (e.place == place && System.Array.IndexOf(e.days, dw) >= 0 && hour >= e.from && hour < e.to) return e;
            return null;
        }

        public static List<Ev> Today(int day)
        {
            int dw = DayOfWeek(day);
            return new List<Ev>(System.Array.FindAll(All, e => System.Array.IndexOf(e.days, dw) >= 0));
        }

        public static string Hours(Ev e) => $"{(int)e.from}:{(e.from % 1 > 0 ? "30" : "00")}–{(int)e.to % 24}:{(e.to % 1 > 0 ? "30" : "00")}";
    }
}
