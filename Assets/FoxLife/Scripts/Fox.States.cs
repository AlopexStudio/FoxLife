using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Состояния лисички: три физических (здоровье — главное, сытость, бодрость) и три психических
    /// (менталка — главное, комфорт, отдых).
    /// Сытость, бодрость и отдых лежат в старом массиве <see cref="needs"/> (0, 1, 3). Гигиена (2) выключена,
    /// но не удалена. Общение (4) — скрытое желание поболтать: его не показывают, но оно зовёт к друзьям
    /// и влияет на комфорт и отдых.
    /// </summary>
    public partial class Fox
    {
        /// <summary>Гигиена пока выключена: не падает, не показывается, не влияет на настроение.</summary>
        public const bool HygieneOn = false;

        public float health = -1f;   // общее состояние организма: падает от стресса, голода, недосыпа, с возрастом
        public float mental = 60f;   // «менталка» — удовлетворённость жизнью: растёт от исполненных желаний, падает от страхов
        public float comfort = 60f;  // насколько уютно здесь и сейчас: погода, место, компания
        public float sickMinutes;    // болеет ещё столько игровых минут
        public int likedSeason = -1; // любимое время года

        public List<string> interests = new List<string>();
        float interestTimer = 300f, thoughtTimer = 120f;

        public float lastParented = -99999f;
        public int parentedStreak;
        public IReadOnlyList<string> RecentActivities => history;

        public bool Sick => sickMinutes > 0;
        public bool Depressed => mental < 12f;

        public static readonly string[] StateNames = { "Здоровье", "Сытость", "Бодрость", "Менталка", "Комфорт", "Отдых" };
        public static readonly string[] StateTips =
        {
            "Общее состояние организма. Падает от голода, недосыпа, стресса и вредных привычек, с возрастом. Восстанавливается очень медленно. При низком здоровье лисичка часто болеет и быстрее стареет.",
            "Сытость: пора ли поесть.",
            "Бодрость: хватает ли сил или пора спать.",
            "Менталка — удовлетворённость жизнью. Растёт, когда сбываются желания и мечты, падает от страхов. Низкая — грустит, очень низкая — депрессия.",
            "Комфорт — насколько уютно вокруг: погода, время года, место, кто рядом и насколько они знакомы. Сильно зависит от характера.",
            "Отдых — снятие напряжения. Ненасытным помогает новое, косным — привычное, неугомонным — компания, нелюдимым — тишина и любимые дела в одиночестве.",
        };

        public float StateValue(int i)
        {
            switch (i)
            {
                case 0: return health;
                case 1: return needs[(int)Need.Hunger];
                case 2: return needs[(int)Need.Energy];
                case 3: return mental;
                case 4: return comfort;
                default: return needs[(int)Need.Fun];
            }
        }

        public void SetState(int i, float v)
        {
            v = Mathf.Clamp(v, 0, 100);
            switch (i)
            {
                case 0: health = v; break;
                case 1: needs[(int)Need.Hunger] = v; break;
                case 2: needs[(int)Need.Energy] = v; break;
                case 3: mental = v; break;
                case 4: comfort = v; break;
                default: needs[(int)Need.Fun] = v; break;
            }
        }

        void InitStates()
        {
            if (health < 0) health = Mathf.Clamp(88f + Random.Range(-8f, 8f) - (stage == LifeStage.Elder ? 18f : 0f), 30, 100);
            if (likedSeason < 0) likedSeason = Mathf.Abs((id ?? foxName ?? "").GetHashCode()) % 4;
            if (!HygieneOn) needs[(int)Need.Hygiene] = 100f;
            if (interests.Count == 0 && !Net.IsClient && !interestsSet) { interests = Interests.Random(pers); interestsSet = true; }
        }

        [System.NonSerialized] public bool interestsSet;

        public void AddInterest(string id, string why)
        {
            if (string.IsNullOrEmpty(id) || interests.Contains(id) || Interests.Get(id) == null) return;
            string dropped = null;
            if (interests.Count >= Interests.Max) { int k = Random.Range(0, interests.Count); dropped = interests[k]; interests.RemoveAt(k); }
            interests.Add(id);
            string n = Interests.Name(id).ToLower();
            Say(Pick("Как же это интересно!", "Кажется, у меня новое увлечение!", "Хочу узнать об этом больше!"));
            G.LogFox(this, dropped != null
                ? $"{foxName} увлекается: {n} (вместо: {Interests.Name(dropped).ToLower()}){why}."
                : $"{foxName} открывает новый интерес: {n}{why}.");
            Remember("Новое увлечение: " + n);
            AddMoodlet("Новое увлечение", 5, 360);
        }

        /// <summary>Интересы иногда открываются сами — от любимых занятий (редко), и лисички думают о своих увлечениях.</summary>
        void UpdateInterests(float dm)
        {
            interestTimer -= dm;
            if (interestTimer <= 0)
            {
                interestTimer = 720f;
                if (Random.value < 0.05f + (pers.O - 5f) * 0.01f)
                {
                    foreach (var d in Interests.All)
                    {
                        if (interests.Contains(d.id) || d.hooks == null) continue;
                        bool hit = false;
                        foreach (var h in history) foreach (var k in d.hooks) if (h != null && h.Contains(k)) hit = true;
                        if (!hit) continue;
                        if (interests.Count >= Interests.Max && Random.value > 0.3f) break; // сменить увлечение — редкость
                        AddInterest(d.id, " — понравилось этим заниматься");
                        break;
                    }
                }
            }
            thoughtTimer -= dm;
            if (thoughtTimer <= 0)
            {
                thoughtTimer = Random.Range(90f, 200f);
                if (interests.Count > 0 && !sleeping && side == null && (current == null || current.kind == ActKind.Wander || current.kind == ActKind.Move) && Random.value < 0.4f)
                {
                    var d = Interests.Get(interests[Random.Range(0, interests.Count)]);
                    if (d != null && d.thoughts.Length > 0) Say(Phrases.Fill(d.thoughts[Random.Range(0, d.thoughts.Length)], this, null));
                }
            }
        }

        // ---------- отдых: что снимает напряжение именно у этой лисички ----------

        /// <summary>
        /// Насколько занятие отдыхает эту лисичку: ненасытным (открытость высокая) — новое, косным — знакомое;
        /// нелюдимым — дела в одиночестве. 1 — как обычно.
        /// </summary>
        public float RestFit(Interaction it)
        {
            float k = 1f;
            bool known = history.Contains(it.name);
            if (pers.O >= 7) k *= known ? 0.65f : 1.4f;
            else if (pers.O <= 3) k *= known ? 1.4f : 0.7f;
            if ((it.tags & ActTag.Solitary) != 0) k *= pers.E <= 4 ? 1.35f : pers.E >= 7 ? 0.75f : 1f;
            if ((it.tags & ActTag.Creative) != 0 && pers.O >= 7) k *= 1.2f;
            if ((it.tags & ActTag.Routine) != 0 && pers.O <= 4) k *= 1.2f;
            if (Depressed) k *= 0.5f;
            return k;
        }

        /// <summary>Общение отдыхает неугомонных и утомляет нелюдимых (если долго).</summary>
        float SocialRest => (pers.E - 4.5f) * 0.035f;

        bool Socializing => side != null || (current != null && (current.kind == ActKind.Social || current.kind == ActKind.BeTalkedTo));

        // ---------- комфорт ----------

        /// <summary>Чего хочется по комфорту прямо сейчас (к этому значению комфорт плавно тянется).</summary>
        public float ComfortTarget()
        {
            float t = 58f;
            var w = G.weather;
            bool outside = Level == 0 && G.house.RoomAt(0, Cell) == null;
            if (w != null)
            {
                if ((int)w.kind == likedWeather) t += outside ? 14f : 7f;
                else if ((int)w.kind == dislikedWeather) t -= outside ? 14f : 6f;
                if (w.Raining && outside && !(IsChild && pers.E >= 7)) t -= 8f;
            }
            if (G.Season == likedSeason) t += 6f;
            // место: нелюдимым уютнее дома, неугомонным — где люди
            var lot = G.house.LotAt(Cell);
            bool home_ = lot != null && lot.id == home;
            if (home_) t += pers.E <= 4 ? 10f : 4f;
            else if (lot != null && lot.Public) t += (pers.E - 5f) * 1.2f;
            // кто рядом и насколько знакомы
            int strangers = 0; float friends = 0;
            foreach (var o in G.foxes)
            {
                if (o == null || o == this || !o.gameObject.activeInHierarchy) continue;
                var d = o.transform.position - transform.position; d.y = 0;
                if (d.sqrMagnitude > 25f) continue;
                float r = Social.Rel(this, o);
                if (r < 15f) strangers++;
                else friends += Mathf.Clamp01(r / 60f);
            }
            float crowd = pers.E >= 6 ? 1.5f : pers.E <= 3 ? -3.5f : -1f;
            t += strangers * crowd + friends * (pers.E >= 5 ? 3f : 1.5f);
            if (pers.E <= 3 && strangers + friends > 3) t -= 6f;
            // удобное место: диван, кровать, кресло
            if (current != null && current.inter != null && (current.inter.tags & ActTag.Comfort) != 0) t += 10f;
            if (sleeping && current != null && current.floorSleep) t -= 20f;
            if (Sick) t -= 10f;
            // тревожным всё ощущается острее
            if (t < 50f) t = 50f - (50f - t) * (0.7f + pers.N * 0.06f);
            return Mathf.Clamp(t, 0, 100);
        }

        float comfortTimer, sickTimer, habitTimer, sadTimer;
        float comfortGoal = 60f;

        /// <summary>Каждую игровую минуту (из Decay).</summary>
        void UpdateStates(float dm)
        {
            InitStates();
            UpdateInterests(dm);
            // ----- комфорт -----
            comfortTimer -= dm;
            if (comfortTimer <= 0) { comfortTimer = 5f; comfortGoal = ComfortTarget(); }
            comfort = Mathf.MoveTowards(comfort, comfortGoal, 0.35f * dm);

            // ----- отдых: напряжение копится, общение отдыхает неугомонных -----
            float stress = 0f;
            foreach (var m in moodlets) if (m.value < 0) stress -= m.value;
            if (!sleeping) needs[(int)Need.Fun] -= (0.012f * Mathf.Min(stress, 40f) / 10f) * dm;
            if (Socializing) needs[(int)Need.Fun] += SocialRest * dm;
            if (comfort < 30f) needs[(int)Need.Fun] -= 0.02f * dm;
            needs[(int)Need.Fun] = Mathf.Clamp(needs[(int)Need.Fun], 0, 100);

            // ----- здоровье: падает легко, растёт с трудом -----
            float dh = 0f;
            if (needs[(int)Need.Hunger] < 20f) dh -= 0.010f;
            if (needs[(int)Need.Energy] < 15f) dh -= 0.008f;
            if (needs[(int)Need.Fun] < 20f) dh -= 0.006f;   // стресс
            if (mental < 15f) dh -= 0.004f;
            if (Sick) dh -= 0.003f;
            // здоровье само не восстанавливается: только лечение и спорт (их пока нет). Это показатель на всю жизнь,
            // а сытость, бодрость, комфорт и отдых меняются в пределах дня
            dh -= stage == LifeStage.Elder ? 0.0006f : stage == LifeStage.Adult ? 0.0002f : 0f; // возраст
            health = Mathf.Clamp(health + dh * dm, 0, 100);

            // ----- болезни: при низком здоровье — часто -----
            if (Sick)
            {
                sickMinutes -= dm * (sleeping ? 1.6f : 1f);
                AddMoodlet("Болеет", -10, 5);
                if (sickMinutes <= 0) { sickMinutes = 0; AddMoodlet("Выздоровел" + (female ? "а" : ""), 6, 360); Say("Наконец-то здоров" + (female ? "а" : "") + "!"); }
                else if (Random.value < 0.004f * dm) Say(Random.value < 0.5f ? "Апчхи!" : "Кхе-кхе…");
            }
            sickTimer -= dm;
            if (sickTimer <= 0)
            {
                sickTimer = 60f;
                float chance = health < 65f ? (65f - health) / 65f * 0.03f : 0.001f;
                if (!Sick && Random.value < chance) FallSick();
            }

            // ----- менталка: медленно тянется к середине, низкая — грусть и депрессия -----
            // менталка — удовлетворённость жизнью: меняется от желаний, мечты и страхов, сама к середине не тянется
            if (Depressed) AddMoodlet("Депрессия", -25, 5);
            else if (mental < 30f) AddMoodlet("Грустно на душе", -7, 5);
            else if (mental > 80f) AddMoodlet("Жизнь удалась", 6, 5);
            sadTimer -= dm;
            if (sadTimer <= 0)
            {
                sadTimer = 90f;
                if (mental < 30f && !sleeping && Random.value < 0.35f)
                    Say(Depressed ? Pick("Ничего не хочется…", "Зачем это всё…", "Оставьте меня…") : Pick("Эх…", "Грустно как-то…", "Всё не то…"));
            }

            // ----- вредные привычки: в стрессе нервные срываются на то, что вредит здоровью -----
            habitTimer -= dm;
            if (habitTimer <= 0)
            {
                habitTimer = 45f;
                if (needs[(int)Need.Fun] < 20f && current == null && !sleeping && !atWork && !IsChild &&
                    Random.value < 0.15f + (pers.N - 5f) * 0.05f)
                    StartBadHabit();
            }
        }

        static string Pick(params string[] s) => s[Random.Range(0, s.Length)];

        /// <summary>Что повышает и понижает комфорт именно этой лисички (для подсказки в «Самочувствии»).</summary>
        public string ComfortHints()
        {
            var up = new List<string>(); var down = new List<string>();
            if (likedWeather >= 0) up.Add(WeatherName((WeatherKind)likedWeather).ToLower());
            if (dislikedWeather >= 0) down.Add(WeatherName((WeatherKind)dislikedWeather).ToLower());
            if (likedSeason >= 0) up.Add(Game.SeasonNames[likedSeason]);
            if (pers.E <= 4) { up.Add("свой дом"); down.Add("незнакомцы рядом"); down.Add("толпа"); }
            else if (pers.E >= 7) { up.Add("людные места"); up.Add("компания"); }
            else { up.Add("свой дом"); up.Add("друзья рядом"); }
            up.Add("диван, кресло, кровать");
            down.Add("дождь на улице");
            if (Sick) down.Add("болезнь");
            string s = "лучше — " + string.Join(", ", up) + "; хуже — " + string.Join(", ", down) + ".";
            if (pers.N >= 7) s += " Всё неуютное чувствует острее.";
            return s;
        }

        /// <summary>Что снимает напряжение именно у этой лисички и что утомляет.</summary>
        public string RestHints()
        {
            var up = new List<string>(); var down = new List<string>();
            if (pers.O >= 7) { up.Add("новые занятия"); up.Add("творчество"); down.Add("одно и то же по кругу"); }
            else if (pers.O <= 3) { up.Add("привычные дела"); down.Add("новое и непривычное"); }
            else up.Add("любимые дела");
            if (pers.E <= 4) { up.Add("дела в одиночестве"); down.Add("долгие разговоры"); }
            else if (pers.E >= 7) { up.Add("общение"); up.Add("компания"); }
            else up.Add("немного общения");
            if (interests.Count > 0) up.Add("разговоры об увлечениях");
            down.Add("плохое настроение и неприятности");
            if (comfort < 30f) down.Add("неуютная обстановка");
            return "помогает — " + string.Join(", ", up) + "; утомляет — " + string.Join(", ", down) + "." + (Depressed ? " В депрессии ничто не радует так, как раньше." : "");
        }

        public void FallSick()
        {
            sickMinutes = Random.Range(900f, 2600f);
            Say(Pick("Что-то мне нехорошо…", "Кажется, я заболел" + (female ? "а" : "") + "…", "Апчхи! Ой…"));
            G.LogFox(this, $"{foxName} заболевает. Нужно побольше спать и хорошо есть.");
            Remember("Болезнь", false);
        }

        /// <summary>Невротические «срывы» от стресса: немного снимают напряжение, но вредят здоровью.</summary>
        void StartBadHabit()
        {
            int k = Random.Range(0, 4);
            string label, say; float hp, rest, food = 0;
            switch (k)
            {
                case 0: label = "Грызёт коготки"; say = "Нервы…"; hp = 0.6f; rest = 4f; break;
                case 1: label = "Заедает стресс"; say = "Ещё печенька… и ещё…"; hp = 1.4f; rest = 6f; food = 18f; break;
                case 2: label = "Ходит из угла в угол"; say = "Не могу успокоиться!"; hp = 0.4f; rest = 3f; break;
                default: label = "Сидит в телефоне до ночи"; say = "Ещё одно видео…"; hp = 1.0f; rest = 5f; break;
            }
            Say(say);
            health = Mathf.Max(0, health - hp);
            needs[(int)Need.Fun] = Mathf.Min(100, needs[(int)Need.Fun] + rest);
            if (food > 0) needs[(int)Need.Hunger] = Mathf.Min(100, needs[(int)Need.Hunger] + food);
            current = new FoxAction { kind = ActKind.Idle, label = label, duration = 20, stage = 0 };
            G.LogFox(this, $"{foxName}: {label.ToLower()} — стресс. Это вредит здоровью.");
        }

        /// <summary>Сбылось желание или мечта — менталка растёт; сбылся страх — падает.</summary>
        public void MentalChange(float delta)
        {
            InitStates();
            mental = Mathf.Clamp(mental + delta, 0, 100);
        }

        /// <summary>Во сколько раз быстрее стареет: при низком здоровье — заметно быстрее.</summary>
        public float AgingMul => health < 50f && health >= 0f ? 1f + (50f - health) / 50f : 1f;

        /// <summary>Настроение из состояний: сытость, бодрость, отдых и комфорт плюс менталка и здоровье.</summary>
        float StatesMood()
        {
            InitStates();
            float avg = (needs[(int)Need.Hunger] + needs[(int)Need.Energy] + needs[(int)Need.Fun] + comfort) / 4f;
            return avg - 50f + (mental - 50f) * 0.35f + (health - 50f) * 0.15f;
        }
    }
}
