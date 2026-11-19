using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Навыки, работа, желания, страхи, награды и дневник лисички.</summary>
    public partial class Fox
    {
        // ---------- навыки ----------
        public float[] skillXp = new float[Skills.Count];
        public int[] skillLvl = new int[Skills.Count];

        // ---------- работа ----------
        public CareerDef career;
        public int careerLevel;
        public float performance;
        public int lastWorkDay = -1;
        public bool atWork;
        float shiftStartHour;

        // ---------- желания, страхи, награды ----------
        public readonly List<Want> wants = new List<Want>();
        public string fearId;
        public float fearCooldown;
        public int happiness;
        public readonly List<string> perks = new List<string>();
        public readonly List<Memory> memories = new List<Memory>();
        float wantTimer = 1f, hourTimer;

        /// <summary>
        /// Навыки новой лисички: лисята почти ничего не умеют, подростки — чуть-чуть, взрослые — кто что,
        /// у пожилых высокие навыки встречаются чаще. Характер подталкивает к своему (открытость — к творчеству и музыке и т.д.).
        /// </summary>
        public void RandomSkills(System.Random rnd = null)
        {
            float R() => rnd != null ? (float)rnd.NextDouble() : Random.value;
            int cap = stage == LifeStage.Kit ? 2 : stage == LifeStage.Teen ? 4 : stage == LifeStage.Adult ? 8 : 10;
            float expo = stage == LifeStage.Kit ? 2.2f : stage == LifeStage.Teen ? 1.8f : stage == LifeStage.Adult ? 1.5f : 1.05f;
            for (int i = 0; i < Skills.Count; i++)
            {
                var sk = (Skill)i;
                float bias = sk == Skill.Creativity || sk == Skill.Music ? (pers.O - 5) * 0.04f
                    : sk == Skill.Housework || sk == Skill.Cooking ? (pers.C - 5) * 0.04f
                    : sk == Skill.Charisma ? (pers.E - 5) * 0.04f
                    : sk == Skill.Logic ? (pers.C + pers.O - 10) * 0.02f : 0f;
                float roll = Mathf.Clamp01(Mathf.Pow(R(), expo) + bias);
                int lvl = Mathf.Clamp(Mathf.FloorToInt(roll * (cap + 0.99f)), 0, Mathf.Min(cap, Skills.MaxLevel));
                skillLvl[i] = lvl;
                skillXp[i] = lvl >= Skills.MaxLevel ? 0 : R() * 0.8f * Skills.XpForLevel(lvl);
            }
        }

        public int SkillLevel(Skill s) => s == Skill.None ? 0 : skillLvl[(int)s];
        public float SkillProgress(Skill s) => s == Skill.None || skillLvl[(int)s] >= Skills.MaxLevel ? 1f : skillXp[(int)s] / Skills.XpForLevel(skillLvl[(int)s]);
        public bool HasPerk(string id) => perks.Contains(id);

        public void GainSkill(Skill s, float amount)
        {
            if (s == Skill.None) return;
            int i = (int)s;
            if (skillLvl[i] >= Skills.MaxLevel) return;
            skillXp[i] += amount * Skills.LearnMul(pers) * (HasPerk("learner") ? 1.3f : 1f);
            while (skillLvl[i] < Skills.MaxLevel && skillXp[i] >= Skills.XpForLevel(skillLvl[i]))
            {
                skillXp[i] -= Skills.XpForLevel(skillLvl[i]);
                skillLvl[i]++;
                int lvl = skillLvl[i];
                Say($"{Skills.Names[i]}: уровень {lvl}!");
                G.LogFox(this, $"{foxName}: навык «{Skills.Names[i]}» — уровень {lvl}");
                AddMoodlet("Новый уровень навыка", 6, 180);
                if (lvl == 5) Remember($"Навык «{Skills.Names[i]}» достиг 5-го уровня");
                if (lvl == Skills.MaxLevel) Remember($"Мастерство: навык «{Skills.Names[i]}» на максимуме!");
                OnEvent(new GameEvent { kind = "skill", arg = Skills.Names[i], level = lvl });
            }
            if (skillLvl[i] >= Skills.MaxLevel) skillXp[i] = 0;
        }

        /// <summary>Множитель длительности действия (хозяйственные дела с навыком идут быстрее).</summary>
        float DurationMul(Interaction it) => it.skill == Skill.Housework ? 1f - SkillLevel(Skill.Housework) * 0.05f : 1f;

        // ---------- работа ----------

        public string JobTitle => career != null ? career.levels[careerLevel] : "";
        public float Wage => career != null ? career.wage[careerLevel] : 0;

        bool IsWorkday(int day) => career != null && career.days[(day - 1) % 7];

        public void TakeJob(CareerDef c)
        {
            if (Net.IsClient) { Net.I.SendOp(this, "takejob", c.id); return; }
            if (career != null) QuitJob(false);
            career = c;
            careerLevel = 0;
            performance = 0;
            float h = G.Hour;
            lastWorkDay = h >= c.startHour - 0.75f ? G.Day : -1; // сегодняшняя смена уже началась — выходим с завтра
            AddMoodlet("Новая работа", 6, 300);
            Say("Ура, меня взяли!");
            G.LogFox(this, $"{foxName} устраивается: {c.name}, {JobTitle}");
            Remember($"Новая работа: {c.name}, {JobTitle}");
            OnEvent(new GameEvent { kind = "job", arg = c.id });
        }

        public void QuitJob(bool log = true)
        {
            if (Net.IsClient) { Net.I.SendOp(this, "quitjob", log ? "1" : "0"); return; }
            if (career == null) return;
            if (atWork && current != null && current.kind == ActKind.Work) EndShift(current, true);
            if (log)
            {
                G.LogFox(this, $"{foxName} увольняется: {career.name}");
                Remember($"Уход с работы: {career.name}");
            }
            career = null;
            careerLevel = 0;
            performance = 0;
            queue.RemoveAll(q => q.kind == ActKind.Work);
            if (current != null && current.kind == ActKind.Work) CancelCurrent(false);
        }

        void CheckWork()
        {
            if (career == null || atWork) return;
            if (current != null && current.kind == ActKind.Work) return;
            int day = G.Day;
            float h = G.Hour;
            if (lastWorkDay == day || !IsWorkday(day)) return;
            if (h >= career.startHour - 1.25f && h < career.endHour - 1f)
            {
                lastWorkDay = day;
                CancelCurrent(true);
                queue.RemoveAll(q => q.kind == ActKind.Work);
                queue.Insert(0, new FoxAction { kind = ActKind.Work, label = career.id == "school" ? "Идёт в школу" : "Идёт на работу" });
                Say(Phrases.Pick(career.id == "school" ? "school:go" : "work:go", this, null));
            }
        }

        void SetVisible(bool on)
        {
            if (on && hiddenByLevel) on = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
            if (on) SetSelected(G.selected == this);
        }

        /// <summary>
        /// Работа и школа — в зданиях города: лисичка идёт туда пешком и работает на виду
        /// (повар у плиты в кафе, художник за мольбертом, учёный у телескопа, музыкант на эстраде, продавец за прилавком).
        /// </summary>
        void RunWork(FoxAction a, float dm, float dr)
        {
            if (career == null) { Finish(a); return; }
            if (a.stage == 0)
            {
                var lot = G.house.lots.Find(l => l.kind == Workplaces.Place(career));
                var stations = Workplaces.Stations(career);
                int only = career.id == "sales" ? 1 : -1;
                a.furn = null;
                List<Vector3> best = null;
                float bestScore = float.MaxValue;
                // в школе — сначала первый ряд (ближе к доске), потом второй, потом третий
                Furniture board = career.id == "school" && lot != null ? G.furniture.Find(x => x.lot == lot.id && x.def.id == "chalkboard") : null;
                if (lot != null)
                    foreach (var f in G.furniture)
                    {
                        if (f.lot != lot.id || System.Array.IndexOf(stations, f.def.id) < 0) continue;
                        if (!f.FindSlot(this, G.house, out int sl, out var st, out var p, only)) continue;
                        float score = p.Count;
                        if (board != null)
                        {
                            var d = f.transform.position - board.transform.position;
                            float row = Mathf.Abs(Vector3.Dot(d, board.transform.forward));
                            float side = Mathf.Abs(Vector3.Dot(d, board.transform.right));
                            score = Mathf.Round(row / 1.5f) * 1000f + side * 10f + p.Count;
                        }
                        if (score < bestScore) { bestScore = score; best = p; a.furn = f; a.slot = sl; a.stand = st; }
                    }
                if (a.furn != null)
                {
                    a.furn.occupants[a.slot] = this;
                    Reserve(a.furn.level, a.stand);
                    StartPath(best);
                }
                else StartPath(G.house.FindPath(Level, Cell, 0, lot != null ? lot.entry : HomeEntry) ?? new List<Vector3>());
                a.label = career.id == "school" ? "Идёт в школу" : "Идёт на работу";
                a.stage = 1;
            }
            if (a.stage == 1)
            {
                if (!FollowPath(dr)) return;
                atWork = true;
                shiftStartHour = Mathf.Max(G.Hour, career.startHour);
                a.success = G.Hour <= career.startHour + 0.25f; // не опоздал(а)
                a.label = $"{Workplaces.Verb(career)} до {career.endHour}:00";
                if (a.furn != null) { Face(a.furn.transform.position); a.furn.inUse++; }
                a.stage = 2;
                G.LogFox(this, career.id == "school" ? $"{foxName} пришёл(ла) в школу" : $"{foxName} на работе: {Workplaces.PlaceName(career)}");
                return;
            }
            pose = career.id == "school" ? Pose.Sit : Pose.Use;
            if (a.furn != null) Face(a.furn.transform.position, 0.1f);
            // на работе: нужды меняются как на смене, растёт ключевой навык
            float fit = career.Fit(pers);
            needs[(int)Need.Fun] = Mathf.Max(0, needs[(int)Need.Fun] - (fit < 0 ? 0.05f : 0.01f) * dm);
            needs[(int)Need.Social] = Mathf.Min(100, needs[(int)Need.Social] + (career.id == "sales" || career.id == "music" ? 0.25f : 0.12f) * dm);
            needs[(int)Need.Energy] = Mathf.Max(5, needs[(int)Need.Energy]);
            if (career.id == "school") SchoolLearn(dm);
            else GainSkill(career.skill, 0.25f * dm);
            if (G.Day != lastWorkDay || G.Hour >= career.endHour) EndShift(a, false);
        }

        public float lastTeacherQ = 1f;

        /// <summary>
        /// Учитель на уроке: прирост умения ученика ≈ (1 + умение учителя + 1 − умение ученика) × харизма учителя × оценки,
        /// с поправкой на настроение и отношения. Без учителя дети занимаются сами — тогда всё решает их ответственность.
        /// </summary>
        void SchoolLearn(float dm)
        {
            Fox teacher = null; float best = -1f;
            foreach (var o in G.foxes)
            {
                if (o == null || !o.atWork || o.career == null || o.career.id != "teacher") continue;
                float q = o.SkillLevel(Skill.Charisma) + o.SkillLevel(Skill.Logic) + o.SkillLevel(Skill.Creativity);
                if (q > best) { best = q; teacher = o; }
            }
            float grades = Mathf.Clamp(1f + performance / 200f, 0.5f, 1.5f);
            float mood = Mathf.Clamp(1f + Mood / 150f, 0.5f, 1.4f);
            void Learn(Skill s, float k)
            {
                float rate;
                if (teacher != null)
                {
                    float diff = Mathf.Max(0.3f, 1f + teacher.SkillLevel(s) + 1f - SkillLevel(s));
                    float cha = 0.4f + teacher.SkillLevel(Skill.Charisma) * 0.12f;          // харизма 0 → 0.4, 10 → 1.6
                    float rel = Mathf.Clamp(1f + Social.Rel(this, teacher) / 200f, 0.6f, 1.4f);
                    float tMood = Mathf.Clamp(1f + teacher.Mood / 200f, 0.7f, 1.3f);
                    rate = 0.025f * diff * cha * grades * mood * rel * tMood;
                }
                else rate = 0.06f * (0.15f + pers.C * 0.12f) * mood;                         // сами: всё решает ответственность
                GainSkill(s, rate * k * dm);
            }
            Learn(Skill.Logic, 1f);
            Learn(Skill.Creativity, 0.75f);
            lastTeacherQ = teacher != null ? Mathf.Clamp((0.4f + teacher.SkillLevel(Skill.Charisma) * 0.12f) * (1f + teacher.SkillLevel(Skill.Logic) * 0.08f), 0.4f, 2.5f) : 0.3f + pers.C * 0.06f;
        }

        void EndShift(FoxAction a, bool early)
        {
            if (!atWork) { Finish(a); return; }
            atWork = false;
            if (a.furn != null)
            {
                if (a.slot >= 0 && a.slot < a.furn.occupants.Length && a.furn.occupants[a.slot] == this) a.furn.occupants[a.slot] = null;
                a.furn.inUse = Mathf.Max(0, a.furn.inUse - 1);
            }
            pose = Pose.Stand;
            queue.Add(new FoxAction { kind = ActKind.Move, moveTarget = GridMap.Center(HomeEntry) + Vector3.up * G.house.FloorY(0, HomeEntry), label = "Идёт домой" });
            float end = (G.Day != lastWorkDay) ? career.endHour : Mathf.Min(G.Hour, career.endHour);
            float hours = Mathf.Max(0, end - shiftStartHour);
            int pay = Mathf.RoundToInt(hours * Wage);
            if (pay > 0) G.AddMoneyFor(this, pay, $"{foxName}: зарплата");

            float fit = career.Fit(pers);
            float gain = 6f + SkillLevel(career.skill) * 1f + Mood / 25f + (pers.C - 5.5f) * 0.8f + fit + (HasPerk("worker") ? 4f : 0f);
            if (!a.success) { gain -= 8f; AddMoodlet("Опоздание", -4, 120); }
            if (early) gain = -12f;
            performance += gain;
            if (fit > 1.5f) AddMoodlet("Любимая работа", 6, 240);
            else if (fit < -1.5f) AddMoodlet("Нелюбимая работа", -6, 240);

            if (careerLevel < career.levels.Length - 1)
            {
                int need = CareerDef.SkillNeeded(careerLevel + 1);
                if (SkillLevel(career.skill) < need) performance = Mathf.Min(performance, 90f);
                if (performance >= 100f) Promote();
            }
            else performance = Mathf.Min(performance, 100f);

            if (career.id == "school")
            {
                performance += (lastTeacherQ - 1f) * 4f; // хороший учитель — оценки лучше
                performance = Mathf.Clamp(performance, -100f, 100f);
            }
            else if (performance <= -60f)
            {
                var c = career;
                AddMoodlet("Уволили", -15, 600);
                G.LogFox(this, $"{foxName}: уволили с работы ({c.name})");
                Remember($"Увольнение: {c.name}", false);
                career = null; careerLevel = 0; performance = 0;
                OnEvent(new GameEvent { kind = "fired" });
            }
            Say(early ? "Сегодня домой пораньше..." : career != null && career.id == "school" ? "Уроки кончились!" : "Я дома!");
            Finish(a);
        }

        void Promote()
        {
            careerLevel++;
            performance = 10f;
            int bonus = Mathf.RoundToInt(Wage * career.Hours);
            G.AddMoneyFor(this, bonus, $"{foxName}: премия за повышение");
            AddMoodlet("Повышение!", 12, 480);
            Say("Меня повысили!");
            G.LogFox(this, $"{foxName}: повышение — {JobTitle}");
            Remember($"Повышение: {JobTitle}");
            OnEvent(new GameEvent { kind = "promo", level = careerLevel });
        }

        // ---------- желания и страхи ----------

        // ---------- мечта всей жизни ----------
        public string dream;
        public readonly List<string> dreamsDone = new List<string>();
        public readonly Dictionary<string, int> tally = new Dictionary<string, int>();
        float dreamTimer;

        public int Tally(string key) => tally.TryGetValue(key, out int v) ? v : 0;
        public void Count(string key, int n = 1) => tally[key] = Tally(key) + n;
        public DreamDef Dream => Dreams.Get(dream);

        public void ChooseDream(string id)
        {
            if (Dreams.Get(id) == null) return;
            dream = id;
            AddMoodlet("Новая мечта", 4, 240);
            Remember("Мечта всей жизни: " + Dreams.Get(id).name);
        }

        void UpdateDream(float dm)
        {
            dreamTimer -= dm;
            if (dreamTimer > 0) return;
            dreamTimer = 10f;
            if (career != null && career.id != "school")
            {
                if (careerLevel > Tally("career_lvl")) tally["career_lvl"] = careerLevel;
                if (careerLevel >= career.levels.Length - 1) tally["career_top"] = 1;
            }
            if (stage < LifeStage.Teen) return;
            var d = Dream;
            if (d == null || stage < d.minStage)
            {
                var p = Dreams.Pick(this);
                if (p != null && p.id != dream) { dream = p.id; if (G.started) Remember("Мечта всей жизни: " + p.name); }
                return;
            }
            if (Dreams.Progress(this, d) < 0.999f) return;
            // мечта сбылась!
            dreamsDone.Add(d.id);
            happiness += Dreams.Reward;
            AddMoodlet("Мечта сбылась!", 25, 4320);
            Say("Неужели… моя мечта сбылась!");
            Remember("Сбылась мечта всей жизни: " + d.name);
            G.LogFox(this, $"{foxName}: сбылась мечта всей жизни «{d.name}»! (+{Dreams.Reward} очков счастья)");
            foreach (var o in G.foxes)
                if (o != this && o.home == home && Social.Rel(o, this) >= 30) o.AddMoodlet($"Мечта {foxName} сбылась", 8, 720);
            dream = null; // новая мечта — по характеру, или игрок выберет сам
        }

        void UpdateLife(float dm)
        {
            CheckWork();
            UpdateDream(dm);

            wantTimer -= dm;
            fearCooldown -= dm;
            if (wantTimer <= 0)
            {
                wantTimer = 30f;
                int guard = 0;
                while (wants.Count < 3 && guard++ < 5) if (!TryAddWant()) break;
                if (fearId == null && fearCooldown <= 0) PickFear();
            }
            if (fearId != null)
            {
                var fd = Wants.GetFear(fearId);
                if (fd == null) fearId = null;
                else if (fd.check != null && fd.check(this)) RealizeFear(fd);
            }

            hourTimer -= dm;
            if (hourTimer <= 0)
            {
                hourTimer = 60f;
                if (G.money < 0 && !IsChild) AddMoodlet("Долги", -6, 70);
                // по утрам часть старых желаний сменяется новыми
                int h = Mathf.FloorToInt(G.Hour);
                if (h == 7 && wants.Count > 0 && Random.value < 0.5f) wants.RemoveAt(Random.Range(0, wants.Count));
            }
        }

        public void RefreshWants() { for (int i = 0; i < 3 && wants.Count < 3; i++) TryAddWant(); }

        bool TryAddWant()
        {
            var cands = new List<(WantDef def, float w)>();
            float sum = 0;
            foreach (var d in Wants.All)
            {
                if (wants.Exists(x => x.id == d.id)) continue;
                if (stage == LifeStage.Kit && Wants.AdultOnly.Contains(d.id)) continue;
                if (stage == LifeStage.Teen && Wants.NotForTeens.Contains(d.id)) continue;
                float w = d.weight(this);
                if (w <= 0) continue;
                cands.Add((d, w));
                sum += w;
            }
            if (cands.Count == 0) return false;
            float r = Random.value * sum;
            foreach (var c in cands)
            {
                r -= c.w;
                if (r > 0) continue;
                string arg = c.def.pickArg != null ? c.def.pickArg(this) : null;
                if (c.def.pickArg != null && arg == null) return false;
                wants.Add(new Want { id = c.def.id, arg = arg, text = c.def.text(arg), points = c.def.points });
                return true;
            }
            return false;
        }

        void PickFear()
        {
            float sum = 0;
            foreach (var f in Wants.Fears) sum += Mathf.Max(0, f.weight(this));
            if (sum <= 0) { fearId = null; fearCooldown = 720; return; }
            float r = Random.value * sum;
            foreach (var f in Wants.Fears)
            {
                r -= Mathf.Max(0, f.weight(this));
                if (r <= 0) { fearId = f.id; return; }
            }
        }

        void RealizeFear(FearDef fd)
        {
            AddMoodlet("Сбылся страх: " + fd.text.ToLower(), -12, 300);
            Say("Только не это!");
            G.LogFox(this, $"{foxName}: сбылся страх — {fd.text.ToLower()}");
            Remember($"Сбылся страх: {fd.text.ToLower()}", false);
            fearId = null;
            fearCooldown = 720f;
        }

        public void OnEvent(GameEvent e)
        {
            for (int i = wants.Count - 1; i >= 0; i--)
            {
                var w = wants[i];
                var d = Wants.Get(w.id);
                if (d != null && d.match(e, w.arg)) Fulfill(w);
            }
            if (fearId != null)
            {
                var fd = Wants.GetFear(fearId);
                if (fd != null && fd.match != null && fd.match(this, e)) RealizeFear(fd);
            }
        }

        void Fulfill(Want w)
        {
            wants.Remove(w);
            happiness += w.points;
            AddMoodlet("Желание исполнилось", 8, 240);
            Say("Ура!");
            G.LogFox(this, $"{foxName}: исполнилось желание «{w.text}» (+{w.points})");
            wantTimer = Mathf.Min(wantTimer, 20f);
        }

        public void DismissWant(Want w)
        {
            wants.Remove(w);
            TryAddWant();
        }

        public bool BuyPerk(PerkDef p)
        {
            if (Net.IsClient) { Net.I.SendOp(this, "perk", p.id); return true; }
            if (HasPerk(p.id) || happiness < p.cost) return false;
            happiness -= p.cost;
            perks.Add(p.id);
            AddMoodlet("Новая награда", 8, 240);
            G.LogFox(this, $"{foxName} получает награду «{p.name}»");
            Remember($"Награда: {p.name}");
            return true;
        }

        public void Remember(string text, bool good = true)
        {
            memories.Add(new Memory { day = G.Day, text = text, good = good });
            if (memories.Count > 50) memories.RemoveAt(0);
        }
    }
}
