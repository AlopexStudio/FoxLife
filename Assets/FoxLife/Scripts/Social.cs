using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Социальное действие. Желание начать зависит от характера инициатора, успех — от характера собеседника.</summary>
    public class SocialDef
    {
        public string name;
        public string command, activity;
        public NameCase targetCase;
        public string say, yes, no;
        public float relYes, relNo;
        public string moodYesA, moodYesB, moodNoA, moodNoB;
        public float valYesA, valYesB, valNoA, valNoB;
        public Func<Fox, Fox, bool> available = (a, b) => true;
        public Func<Fox, Fox, float> want;
        public Func<Fox, Fox, float> accept;
        public float romYes, romNo, funYes;
        public bool carry;
        public bool chase;   // догонялки: бегают кругами
        public bool boop;    // бупнуть: лёгкое касание носиком
        public float duration = 22f;
        public Action<Fox, Fox> onSuccess;
        public bool sure;    // исход не случаен (ключи)

        public string CommandText(Fox target) => Format(command, target);
        public string ActivityText(Fox target) => Format(activity, target);

        string Format(string template, Fox target) =>
            template == null || target == null ? name : template.Replace("{0}", target.NameIn(targetCase));
    }

    public static class Social
    {
        static List<SocialDef> all;
        public static List<SocialDef> All { get { if (all == null) Build(); return all; } }

        public static float Rel(Fox a, Fox b) => a.rel.TryGetValue(b, out var v) ? v : 0f;

        public static void AddRel(Fox a, Fox b, float d)
        {
            float old = Rel(a, b);
            float v = Mathf.Clamp(old + d, -100, 100);
            a.rel[b] = v;
            b.rel[a] = v;
            if (Game.I == null || !Game.I.started) return;
            if (old < 40 && v >= 40)
            {
                a.OnEvent(new GameEvent { kind = "friend", other = b });
                b.OnEvent(new GameEvent { kind = "friend", other = a });
            }
            if (old < 70 && v >= 70)
            {
                a.Remember("Стали лучшими друзьями с " + b.NameIn(NameCase.Instrumental), about: b);
                b.Remember("Стали лучшими друзьями с " + a.NameIn(NameCase.Instrumental), about: a);
                Game.I.LogPair(a, b, $"{a.foxName} и {b.foxName} теперь лучшие друзья!");
            }
            if (old > -50 && v <= -50)
            {
                a.Remember("Вражда с " + b.NameIn(NameCase.Instrumental), false, b);
                b.Remember("Вражда с " + a.NameIn(NameCase.Instrumental), false, a);
                Game.I.LogPair(a, b, $"{a.foxName} и {b.foxName} теперь заклятые враги.");
            }
        }

        public static float Rom(Fox a, Fox b) => a.romance.TryGetValue(b, out var v) ? v : 0f;

        public static void AddRom(Fox a, Fox b, float d)
        {
            float v = Mathf.Clamp(Rom(a, b) + d, -100, 100);
            a.romance[b] = v;
            b.romance[a] = v;
        }

        public static string RomLabel(float v)
        {
            if (v <= -20) return "охлаждение";
            if (v < 15) return "—";
            if (v < 40) return "симпатия";
            if (v < 70) return "влюблённость";
            return "любовь";
        }

        static bool Free(Fox a, Fox b) => string.IsNullOrEmpty(a.partnerId) || a.partnerId == b.id;
        static float Taken(Fox a, Fox b) => !string.IsNullOrEmpty(b.partnerId) && b.partnerId != a.id ? -0.45f : 0f;

        public static string RelLabel(float v)
        {
            if (v <= -50) return "Враги";
            if (v <= -15) return "Неприязнь";
            if (v < 5) return "Едва знакомы";
            if (v < 15) return "Знакомые";
            if (v < 40) return "Приятели";
            if (v < 70) return "Друзья";
            return "Лучшие друзья";
        }

        /// <summary>Член семьи забирает постоянный ключ от дома (не в общем списке: по обстоятельствам).</summary>
        public static readonly SocialDef TakeKey = new SocialDef
        {
            command = "Забрать ключ у {0}", activity = "Забирает ключ у {0}", targetCase = NameCase.Genitive,
            name = "Забрать ключ от дома", say = "Верни, пожалуйста, ключ от нашего дома.", yes = "Ну… держи.", no = "Ну… держи.",
            relYes = -3, relNo = -3, sure = true, duration = 14f,
            want = (a, b) => 0f, accept = (a, b) => 1f,
            available = (a, b) => b.KeyTo(a.home) == KeyKind.Trusted && a.home != b.home,
            onSuccess = (a, b) => b.LoseKey(a.home, a),
        };

        /// <summary>Дать постоянный ключ от нашего дома — если вся семья согласна.</summary>
        public static readonly SocialDef GiveKey = new SocialDef
        {
            command = "Дать {0} ключ от дома", activity = "Даёт {0} ключ от дома", targetCase = NameCase.Dative,
            name = "Дать ключ от дома", say = "Держи ключ от нашего дома — заходи, когда захочешь!", yes = "Спасибо! Буду беречь.", no = "Спасибо!",
            relYes = 4, relNo = 0, sure = true, duration = 14f,
            moodYesA = "Доверили ключ другу", valYesA = 3, moodYesB = "Мне доверили ключ", valYesB = 8,
            want = (a, b) => 0f, accept = (a, b) => 1f,
            available = (a, b) => a.GiveKeyReason(b) == null,
            onSuccess = (a, b) => { b.GiveKey(a.home, KeyKind.Trusted, a); Game.I.LogPair(a, b, $"{a.foxName} даёт {b.NameIn(NameCase.Dative)} постоянный ключ от дома."); },
        };

        static void Build()
        {
            all = new List<SocialDef>
            {
                new SocialDef
                {
                    command = "Поговорить с {0}", activity = "Говорит с {0}", targetCase = NameCase.Instrumental,
                    name = "Поболтать", say = "Как дела?", yes = "Отлично!", no = "Не до тебя.",
                    relYes = 4, relNo = -2,
                    moodYesA = "Приятная беседа", valYesA = 5, moodYesB = "Приятная беседа", valYesB = 5,
                    moodNoA = "Неловкая пауза", valNoA = -4,
                    want = (a, b) => 0.25f,
                    accept = (a, b) => 0.72f + Rel(a, b) / 300f + b.Mood / 300f + (b.pers.E - 5) * 0.02f,
                },
                new SocialDef
                {
                    command = "Пошутить с {0}", activity = "Шутит с {0}", targetCase = NameCase.Instrumental,
                    name = "Пошутить", say = "Знаешь анекдот?", yes = "Ха-ха-ха!", no = "Не смешно.",
                    relYes = 6, relNo = -4,
                    moodYesA = "Смешная шутка", valYesA = 7, moodYesB = "Смешная шутка", valYesB = 7,
                    moodNoA = "Шутка не зашла", valNoA = -5,
                    want = (a, b) => 0.1f + (a.pers.E - 5) * 0.05f + (a.pers.O - 5) * 0.03f,
                    accept = (a, b) => 0.5f + (b.pers.O - 5) * 0.05f + (b.pers.E - 5) * 0.03f + Rel(a, b) / 300f + b.Mood / 400f,
                },
                new SocialDef
                {
                    command = "Обсудить идеи с {0}", activity = "Обсуждает идеи с {0}", targetCase = NameCase.Instrumental,
                    name = "Обсудить идеи", say = "А что, если…", yes = "Гениально!", no = "Чепуха какая-то.",
                    relYes = 5, relNo = -3,
                    moodYesA = "Интересный разговор", valYesA = 6, moodYesB = "Интересный разговор", valYesB = 6,
                    moodNoA = "Идею не оценили", valNoA = -4,
                    available = (a, b) => Rel(a, b) > -30,
                    want = (a, b) => (a.pers.O - 5) * 0.08f,
                    accept = (a, b) => 0.45f + (b.pers.O - 5) * 0.08f,
                },
                new SocialDef
                {
                    command = "Рассказать историю {0}", activity = "Рассказывает историю {0}", targetCase = NameCase.Dative,
                    name = "Рассказать историю", say = "Слушай, что было!", yes = "Вот это да!", no = "Скукота…",
                    relYes = 5, relNo = -3,
                    moodYesA = "Классная история", valYesA = 5, moodYesB = "Классная история", valYesB = 5,
                    moodNoA = "Не дослушали", valNoA = -4,
                    want = (a, b) => 0.05f + (a.pers.E - 5) * 0.06f,
                    accept = (a, b) => 0.6f + (b.pers.E - 5) * 0.03f + (b.pers.O - 5) * 0.02f + Rel(a, b) / 400f,
                },
                new SocialDef
                {
                    command = "Сделать комплимент {0}", activity = "Делает комплимент {0}", targetCase = NameCase.Dative,
                    name = "Сделать комплимент", say = "Классный хвост!", yes = "Спасибо!", no = "Подлиза…",
                    relYes = 6, relNo = -2,
                    moodYesA = "Доброе дело", valYesA = 4, moodYesB = "Похвалили", valYesB = 8,
                    moodNoA = "Комплимент не оценили", valNoA = -3,
                    want = (a, b) => 0.05f + (a.pers.A - 5) * 0.06f,
                    accept = (a, b) => 0.75f + (b.pers.A - 5) * 0.02f + Rel(a, b) / 300f,
                },
                new SocialDef
                {
                    command = "Обнять {0}", activity = "Обнимает {0}", targetCase = NameCase.Accusative,
                    name = "Обнять", say = "Иди сюда!", yes = "Тепло…", no = "Не трогай!",
                    relYes = 8, relNo = -6,
                    moodYesA = "Обнимашки", valYesA = 10, moodYesB = "Обнимашки", valYesB = 10,
                    moodNoA = "Отвергли", valNoA = -8,
                    available = (a, b) => Rel(a, b) >= 25,
                    want = (a, b) => (a.pers.A - 5) * 0.05f + Rel(a, b) / 250f + (a.pers.E - 5) * 0.02f,
                    accept = (a, b) => 0.35f + Rel(a, b) / 120f + (b.pers.A - 5) * 0.04f + (b.pers.E - 5) * 0.03f,
                },
                new SocialDef
                {
                    command = "Бупнуть {0}", activity = "Бупает {0}", targetCase = NameCase.Accusative,
                    name = "Бупнуть", say = "Буп!", yes = "Хи-хи! Буп в ответ!", no = "Эй, мой нос!",
                    relYes = 4, relNo = -2, funYes = 6, duration = 7f, boop = true,
                    moodYesA = "Буп носиком", valYesA = 5, moodYesB = "Меня бупнули", valYesB = 6,
                    moodNoB = "Не трогайте нос", valNoB = -2,
                    available = (a, b) => Rel(a, b) >= 5 || a.IsChild || b.IsChild,
                    want = (a, b) => 0.05f + (a.pers.E - 5) * 0.04f + (a.pers.A - 5) * 0.03f + Rel(a, b) / 300f + (a.IsChild ? 0.1f : 0f),
                    accept = (a, b) => 0.65f + Rel(a, b) / 200f + (b.pers.A - 5) * 0.03f - (b.pers.N - 5) * 0.03f,
                },
                new SocialDef
                {
                    command = "Утешить {0}", activity = "Утешает {0}", targetCase = NameCase.Accusative,
                    name = "Утешить", say = "Всё будет хорошо", yes = "Мне легче.", no = "Оставь меня.",
                    relYes = 8, relNo = -1,
                    moodYesA = "Доброе дело", valYesA = 5, moodYesB = "Поддержка друга", valYesB = 15,
                    available = (a, b) => b.Mood < -15,
                    want = (a, b) => 0.25f + (a.pers.A - 5) * 0.1f + (-b.Mood) / 200f,
                    accept = (a, b) => 0.8f + Rel(a, b) / 400f,
                },
                new SocialDef
                {
                    command = "Пожаловаться {0} на жизнь", activity = "Жалуется {0} на жизнь", targetCase = NameCase.Dative,
                    name = "Пожаловаться на жизнь", say = "Всё плохо…", yes = "Бедняжка…", no = "Опять ноешь?",
                    relYes = 3, relNo = -4,
                    moodYesA = "Удалось выговориться", valYesA = 8,
                    moodNoA = "Не выслушали", valNoA = -8,
                    want = (a, b) => (a.pers.N - 5) * 0.08f + (a.Mood < 0 ? -a.Mood / 300f : 0f),
                    accept = (a, b) => 0.35f + (b.pers.A - 5) * 0.08f + Rel(a, b) / 400f,
                },
                new SocialDef
                {
                    command = "Подразнить {0}", activity = "Дразнит {0}", targetCase = NameCase.Accusative,
                    name = "Подразнить", say = "Хи-хи, ушастик!", yes = "Ха, смешно!", no = "Обидно!",
                    relYes = 3, relNo = -8,
                    moodYesA = "Дружеская подколка", valYesA = 3, moodYesB = "Дружеская подколка", valYesB = 3,
                    moodNoB = "Обидели", valNoB = -10,
                    want = (a, b) => (5 - a.pers.A) * 0.07f,
                    accept = (a, b) => 0.3f + (5 - b.pers.N) * 0.07f + (5 - b.pers.A) * 0.03f + Rel(a, b) / 300f,
                },
                new SocialDef
                {
                    command = "Поспорить с {0}", activity = "Спорит с {0}", targetCase = NameCase.Instrumental,
                    name = "Поспорить", say = "Это неверно!", yes = "Интересный довод!", no = "Ни за что!",
                    relYes = 2, relNo = -10,
                    moodYesA = "Хорошая дискуссия", valYesA = 4, moodYesB = "Хорошая дискуссия", valYesB = 4,
                    moodNoA = "Ссора", valNoA = -8, moodNoB = "Ссора", valNoB = -8,
                    want = (a, b) => (5 - a.pers.A) * 0.05f + (a.pers.N - 5) * 0.03f,
                    accept = (a, b) => 0.25f + (b.pers.O - 5) * 0.04f + (b.pers.A - 5) * 0.02f,
                },

                // ----- лисята -----
                new SocialDef
                {
                    command = "Поиграть с {0} в догонялки", activity = "Играет с {0} в догонялки", targetCase = NameCase.Instrumental,
                    name = "Поиграть в догонялки", say = "Догоняй!", yes = "Не догонишь!", no = "Не хочу…",
                    relYes = 6, relNo = -2, funYes = 25, chase = true,
                    moodYesA = "Весёлая игра", valYesA = 8, moodYesB = "Весёлая игра", valYesB = 8,
                    available = (a, b) => a.IsChild || b.IsChild,
                    want = (a, b) => a.IsChild ? 0.45f : 0.1f + (a.pers.A - 5) * 0.05f,
                    accept = (a, b) => 0.75f + (b.IsChild ? 0.15f : (b.pers.A - 5) * 0.03f),
                },
                new SocialDef
                {
                    command = "Поносить {0} в зубах", activity = "Несёт {0} в зубах", targetCase = NameCase.Accusative,
                    name = "Поносить лисёнка", say = "Хоп — за шкирку!", yes = "Уии!", no = "Пусти!",
                    relYes = 6, relNo = -2, carry = true,
                    moodYesA = "Нянчиться — это мило", valYesA = 5, moodYesB = "Прокатили в зубах", valYesB = 10,
                    available = (a, b) => a.stage >= LifeStage.Adult && b.stage == LifeStage.Kit,
                    want = (a, b) => 0.15f + (a.pers.A - 5) * 0.05f,
                    accept = (a, b) => 0.9f,
                },

                // ----- романтика -----
                new SocialDef
                {
                    command = "Флиртовать с {0}", activity = "Флиртует с {0}", targetCase = NameCase.Instrumental,
                    name = "Флиртовать", say = "Какие у тебя глаза…", yes = "Ой, ну что ты!", no = "Не надо.",
                    relYes = 2, relNo = -3, romYes = 8, romNo = -4,
                    moodYesA = "Флирт", valYesA = 5, moodYesB = "Флирт", valYesB = 5, moodNoA = "Не взаимно", valNoA = -5,
                    available = (a, b) => Family.CanRomance(a, b) && Free(a, b) && Rel(a, b) >= 10,
                    want = (a, b) => (a.pers.E - 5) * 0.03f + Family.Chemistry(a, b) * 0.3f + Rom(a, b) / 400f - 0.12f,
                    accept = (a, b) => 0.3f + Rom(a, b) / 150f + Family.Chemistry(a, b) * 0.35f + (b.pers.E - 5) * 0.02f + Rel(a, b) / 300f + Taken(a, b),
                },
                new SocialDef
                {
                    command = "Поцеловать {0}", activity = "Целует {0}", targetCase = NameCase.Accusative,
                    name = "Поцеловать", say = "Иди ко мне…", yes = "Мур…", no = "Ты что?!",
                    relYes = 3, relNo = -6, romYes = 12, romNo = -10,
                    moodYesA = "Поцелуй", valYesA = 10, moodYesB = "Поцелуй", valYesB = 10, moodNoA = "Отказ", valNoA = -8,
                    available = (a, b) => Family.CanRomance(a, b) && Free(a, b) && Rom(a, b) >= 25,
                    want = (a, b) => Rom(a, b) / 250f + Family.Chemistry(a, b) * 0.15f - 0.05f,
                    accept = (a, b) => 0.25f + Rom(a, b) / 100f + Family.Chemistry(a, b) * 0.2f + Taken(a, b),
                },
                new SocialDef
                {
                    command = "Признаться {0} в любви", activity = "Признаётся {0} в любви", targetCase = NameCase.Dative,
                    name = "Признаться в любви", say = "Я тебя люблю!", yes = "И я тебя!", no = "Прости…",
                    relYes = 5, relNo = -8, romYes = 15, romNo = -15,
                    moodYesA = "Взаимная любовь", valYesA = 15, moodYesB = "Взаимная любовь", valYesB = 15,
                    moodNoA = "Разбитое сердце", valNoA = -15,
                    available = (a, b) => Family.CanRomance(a, b) && string.IsNullOrEmpty(a.partnerId) && string.IsNullOrEmpty(b.partnerId) && Rom(a, b) >= 50,
                    want = (a, b) => Rom(a, b) / 300f + (a.pers.E - 5) * 0.02f,
                    accept = (a, b) => 0.2f + Rom(a, b) / 90f + Family.Chemistry(a, b) * 0.2f,
                    onSuccess = (a, b) => Game.I.MakeCouple(a, b),
                },
                new SocialDef
                {
                    command = "Сделать предложение {0}", activity = "Делает предложение {0}", targetCase = NameCase.Dative,
                    name = "Сделать предложение", say = "Будь со мной навсегда!", yes = "Да! Да!", no = "Ещё рано…",
                    relYes = 8, relNo = -6, romYes = 10, romNo = -12,
                    moodNoA = "Отказ", valNoA = -12,
                    available = (a, b) => a.partnerId == b.id && !a.married && Rom(a, b) >= 70,
                    want = (a, b) => 0.05f + (a.pers.C - 5) * 0.02f + Rom(a, b) / 500f,
                    accept = (a, b) => 0.35f + Rom(a, b) / 120f + (b.pers.C - 5) * 0.03f,
                    onSuccess = (a, b) => Game.I.Marry(a, b),
                },
                new SocialDef
                {
                    command = "Завести лисят с {0}", activity = "Обсуждает лисят с {0}", targetCase = NameCase.Instrumental,
                    name = "Завести лисят", say = "Может, пора?..", yes = "Давай!", no = "Не сейчас.",
                    relYes = 5, relNo = -3, romYes = 6, romNo = -4,
                    available = (a, b) => a.partnerId == b.id && a.female != b.female && a.stage == LifeStage.Adult && b.stage == LifeStage.Adult
                        && !a.Pregnant && !b.Pregnant && Game.I.HomeCount(a.home) < Game.MaxFoxes && Rom(a, b) >= 60
                        && Game.I.KidsAt(a.home) < 3 && Game.I.foxes.Count < Game.TownCap,
                    want = (a, b) => -0.1f + (a.pers.A - 5) * 0.02f - Game.I.KidsAt(a.home) * 0.15f - (Game.I.foxes.Count > Game.TownCap - 8 ? 0.3f : 0f),
                    accept = (a, b) => 0.45f + Rom(a, b) / 200f + (b.pers.A - 5) * 0.03f,
                    onSuccess = (a, b) => Game.I.StartPregnancy(a, b),
                },
            };
            Parenting.Add(all);
        }
    }
}
