using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public class Moodlet
    {
        public string name;
        public float value;
        public float minutesLeft;
    }

    public enum ActKind { UseObject, Social, BeTalkedTo, Move, Wander, Idle, Work, Phone }

    public class FoxAction
    {
        public ActKind kind;
        public string label;
        public bool byPlayer;
        public Furniture furn;
        public Interaction inter;
        public Fox other;
        public Furniture seat;               // смотрит телевизор с дивана
        public int seatSlot = -1;
        public SocialDef social;
        public Vector3 moveTarget;
        public System.Action onArrive;       // что сделать, когда дошли
        public Fox.PhoneMode phoneMode;
        public int placeLot = -1;

        // состояние выполнения
        public int stage;
        public int slot = -1;
        public Vector2Int stand;
        public float elapsed, duration;
        public float funMul = 1f;
        public bool success, reacted, floorSleep, busyTalk;
        public Vector3 chaseCenter;
        public float chaseAngle, chaseDir = 1f, chaseSwap;
        public bool chasing, booped, together;
        public int repaths;
        public Vector3 targetSeen;          // где был собеседник, когда прокладывали путь
        public float repathTimer;
        public bool netDone;                // друг закончил выбирать одежду или план дома

        public static FoxAction ForSocial(SocialDef def, Fox target) =>
            new FoxAction { kind = ActKind.Social, other = target, social = def, label = def.CommandText(target) };

        public string DisplayText(bool active = false)
        {
            if (other == null) return label;
            switch (kind)
            {
                case ActKind.Social when social != null:
                    return active ? social.ActivityText(other) : social.CommandText(other);
                case ActKind.BeTalkedTo:
                    string name = other.NameIn(NameCase.Instrumental);
                    if (social != null && social.carry) return "Катается в зубах у " + other.NameIn(NameCase.Genitive);
                    if (social != null && social.chase) return $"Играет с {name} в догонялки";
                    return "Говорит с " + name;
                case ActKind.Phone:
                    return Fox.PhoneText(phoneMode, other, active);
                case ActKind.Move when placeLot < 0:
                    return "Идёт в гости к " + other.NameIn(NameCase.Dative);
                case ActKind.Move:
                    var place = Game.I?.house?.LotById(placeLot);
                    return "Идёт гулять с " + other.NameIn(NameCase.Instrumental) + (place != null ? " " + place.Into : "");
                default:
                    return label;
            }
        }
    }

    /// <summary>Лисичка: внешность, нужды, настроение, отношения и свобода воли, зависящая от характера.</summary>
    public partial class Fox : MonoBehaviour
    {
        public static readonly string[] NeedNames = { "Сытость", "Бодрость", "Гигиена", "Веселье", "Общение" };
        const float WalkSpeed = 1.7f;

        public string foxName = "Лисичка";
        public string[] nameOverrides;

        public string NameIn(NameCase nameCase) => RussianCases.Override(nameOverrides, nameCase) ?? Names.DefaultForm(foxName, nameCase);

        public void Rename(string name)
        {
            if (foxName != name) nameOverrides = null;
            foxName = name;
            gameObject.name = name;
            if (style != null) style.displayName = name;
        }
        public Personality pers = new Personality();
        public float earSize = 1f, tailFluff = 1f, size = 1f;
        public float[] needs = { 80, 80, 80, 80, 80 };
        public readonly List<Moodlet> moodlets = new List<Moodlet>();
        public readonly List<string> recentPhrases = new List<string>();
        float chatterTimer = 30f;
        WeatherKind lastWeatherSeen;

        /// <summary>Мысли вслух: по нуждам, характеру, погоде и времени суток.</summary>
        void Chatter(float dm)
        {
            chatterTimer -= dm;
            var w = G.weather;
            if (w != null && w.kind != lastWeatherSeen)
            {
                lastWeatherSeen = w.kind;
                if (Level == 0 && G.house.RoomAt(0, Cell) == null && Random.value < 0.5f)
                {
                    string key = w.kind == WeatherKind.Storm ? "weather:storm" : w.kind == WeatherKind.Rain ? "weather:rain" : w.kind == WeatherKind.Fog ? "weather:fog" : w.kind == WeatherKind.Clear ? "weather:clear" : null;
                    if (key != null) { Say(Phrases.Pick(key, this, null)); return; }
                }
            }
            if (chatterTimer > 0) return;
            chatterTimer = Random.Range(60f, 160f) - pers.E * 6f;
            if (sleeping || atWork || bubbleTimer > 0 || (current != null && (current.kind == ActKind.Social || current.kind == ActKind.BeTalkedTo || current.kind == ActKind.Phone))) return;
            if (Random.value < 0.55f && NoticeSurroundings()) return;
            // мечты и страхи вслух
            if (wants.Count > 0 && Random.value < 0.3f)
            {
                var wish = wants[Random.Range(0, wants.Count)];
                Fox about = wish.Target;
                var line = Phrases.Pick("want:" + wish.id, this, about);
                if (!string.IsNullOrEmpty(line) && (about != null || (wish.id != "talk" && wish.id != "hug" && wish.id != "kiss"))) { Say(line); return; }
            }
            if (!string.IsNullOrEmpty(fearId) && Random.value < 0.15f)
            {
                var line = Phrases.Pick("fear:" + fearId, this, null);
                if (!string.IsNullOrEmpty(line)) { Say(line); return; }
            }
            if (Random.value < 0.35f + pers.E * 0.04f) Say(Phrases.Pick("idle", this, null));
        }

        /// <summary>
        /// Реакция на то, что происходит рядом (в той же комнате или поблизости на улице, в пределах видимости):
        /// музыка, рисование, еда, сон, грусть, запах. Что скажет — зависит от характера.
        /// </summary>
        bool NoticeSurroundings()
        {
            var myRoom = G.house.RoomAt(Level, Cell);
            Fox best = null; float bd = 6.5f;
            foreach (var o in G.foxes)
            {
                if (o == null || o == this || !o.gameObject.activeInHierarchy || o.atWork) continue;
                float d = Vector3.Distance(o.transform.position, transform.position);
                if (d > bd || o.Level != Level || G.house.RoomAt(o.Level, o.Cell) != myRoom) continue;
                bd = d; best = o;
            }
            if (best == null) return false;
            var b = best;
            string n = b.foxName;
            string fid = b.current != null && b.current.kind == ActKind.UseObject && b.current.stage == 2 && b.current.furn != null ? b.current.furn.def.id : null;
            bool soft = pers.A >= 6;
            string say = null;
            if (b.sleeping) say = pers.C >= 6 || soft ? "Тсс… " + n + " спит." : null;
            else if (fid == "guitar" || fid == "piano")
            {
                bool likes = b.Element == Element || pers.O >= 6;
                say = likes ? (Random.value < 0.5f ? "Красиво играет!" : "Ещё, ещё!") : pers.N >= 7 ? "Можно потише?.." : soft ? "Хм, интересная мелодия." : "Ну и шум…";
                AddMoodlet(likes ? "Слушаю музыку" : "Шумно", likes ? 3 : -2, 60);
            }
            else if (fid == "easel") say = b.Element == Element ? "Ой, как красиво! Прямо как я люблю." : pers.O >= 6 ? "Что рисуешь, " + n + "?" : null;
            else if (fid == "tv") say = b.Element == Element ? "О, моя любимая передача!" : Random.value < 0.5f ? "Опять это смотрят…" : null;
            else if (b.current != null && b.current.inter == Catalog.EatAtTable) say = soft ? "Приятного аппетита!" : needs[0] < 40 ? "Пахнет вкусно… а мне?" : null;
            else if (b.Mood < -30) say = soft ? "Эй, " + n + ", что случилось?" : pers.E >= 6 ? "Не грусти!" : null;
            else if (b.needs[2] < 18)
            {
                say = pers.A <= 3 ? "Фу! " + n + ", от тебя пахнет!" : pers.A <= 6 ? "Кто-то давно не мылся…" : null;
                if (say != null) Social.AddRel(b, this, -1);
            }
            else if (b.walking && b.IsChild && stage >= LifeStage.Adult) say = pers.N >= 6 ? "Осторожно, не упади!" : "Беги-беги!";
            if (say == null) return false;
            if (current == null || current.kind == ActKind.Idle) Face(b.transform.position, 0.5f);
            Say(say);
            return true;
        }
        public readonly Dictionary<Fox, float> rel = new Dictionary<Fox, float>();
        public Styleable style;

        public FoxAction current;
        public readonly List<FoxAction> queue = new List<FoxAction>();
        public Pose pose = Pose.Stand;
        public bool walking, sleeping;

        public string bubble;
        public float bubbleTimer;

        readonly List<string> history = new List<string>();
        float thinkCooldown, neuroTimer = 30f, messTimer = 10f;
        List<Vector3> path;
        int pathIdx;

        // визуал
        Transform model, head, tail, earL, earR, eyeL, eyeR, legFL, legFR, legBL, legBR;
        GameObject ring;
        float animT, blinkTimer = 2f, blinkLeft;

        Game G => Game.I;
        /// <summary>Куда сейчас идёт лисичка (последняя точка пути).</summary>
        public Vector3? Destination => walking && path != null && path.Count > 0 && pathIdx < path.Count ? path[path.Count - 1] : (Vector3?)null;
        public Vector3 HeadPosition => head != null ? head.position : transform.position + Vector3.up * 0.6f;
        /// <summary>Этаж, на котором сейчас лисичка (по высоте).</summary>
        public int Level => Game.I != null && Game.I.house != null ? Game.I.house.LevelOf(transform.position) : 0;
        GridMap Grid => Game.I.house.grids[Level];
        bool hiddenByLevel;

        // ---------- одна клетка — одна лисичка (проходить друг сквозь друга можно) ----------
        int resLevel = -1;
        Vector2Int resCell;

        void Reserve(int level, Vector2Int c) { resLevel = level; resCell = c; }

        /// <summary>Занята ли клетка другой лисичкой (стоит там или идёт туда, чтобы стоять).</summary>
        public static bool Taken(Fox me, int level, Vector2Int c)
        {
            foreach (var o in Game.I.foxes)
            {
                if (o == me || o == null || o.carried) continue;
                if (o.resLevel >= 0) { if (o.resLevel == level && o.resCell == c) return true; }
                else if (!o.walking && o.Level == level && o.Cell == c) return true;
            }
            return false;
        }

        /// <summary>Ближайшая к цели свободная клетка (если сама цель занята).</summary>
        Vector2Int FreeNear(int level, Vector2Int target)
        {
            var g = G.house.grids[level];
            if (!Taken(this, level, target)) return target;
            for (int r = 1; r <= 3; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        var c = target + new Vector2Int(dx, dz);
                        if (g.IsFree(c) && !Taken(this, level, c)) return c;
                    }
            return target;
        }
        public Vector2Int Cell => Grid.ClampCell(GridMap.CellOf(transform.position));

        // ================= внешность =================

        public void Build(StyleData fur, StyleData light, StyleData dark)
        {
            style = gameObject.AddComponent<Styleable>();
            style.kind = SurfaceKind.Fox;
            style.displayName = foxName;
            style.AddPart("Мех", fur);
            style.AddPart("Светлый мех", light);
            style.AddPart("Лапки и ушки", dark);
            style.AddPart("Глаза и нос", StyleData.Make("Гладкий", "#1E1612", null, 1, 0.85f));
            const int FUR = 0, LIGHT = 1, DARK = 2, EYES = 3;

            model = new GameObject("model").transform;
            model.SetParent(transform, false);
            model.localPosition = new Vector3(0, 0.4f, 0);

            // простая блочная лисичка: корпус, голова с мордочкой, уши с тёмными кончиками, хвост с белым кончиком
            Shapes.Box(style, FUR, model, new Vector3(0, 0.06f, 0), new Vector3(0.46f, 0.38f, 0.78f));
            Shapes.Box(style, LIGHT, model, new Vector3(0, 0.0f, 0.37f), new Vector3(0.36f, 0.26f, 0.08f));

            head = Pivot(model, new Vector3(0, 0.34f, 0.48f));
            Shapes.Box(style, FUR, head, Vector3.zero, new Vector3(0.44f, 0.36f, 0.36f));
            Shapes.Box(style, LIGHT, head, new Vector3(0, -0.118f, 0.03f), new Vector3(0.46f, 0.128f, 0.32f));
            Shapes.Box(style, LIGHT, head, new Vector3(0, -0.07f, 0.25f), new Vector3(0.2f, 0.14f, 0.16f));
            Shapes.Box(style, EYES, head, new Vector3(0, -0.02f, 0.335f), new Vector3(0.08f, 0.06f, 0.02f));
            eyeL = Pivot(head, new Vector3(-0.11f, 0.05f, 0.185f));
            eyeR = Pivot(head, new Vector3(0.11f, 0.05f, 0.185f));

            earL = Ear(head, -1, FUR, DARK);
            earR = Ear(head, 1, FUR, DARK);

            legFL = Leg(model, new Vector3(-0.13f, -0.13f, 0.27f), DARK);
            legFR = Leg(model, new Vector3(0.13f, -0.13f, 0.27f), DARK);
            legBL = Leg(model, new Vector3(-0.13f, -0.13f, -0.27f), DARK);
            legBR = Leg(model, new Vector3(0.13f, -0.13f, -0.27f), DARK);

            tail = Pivot(model, new Vector3(0, 0.12f, -0.38f));
            tail.localRotation = Quaternion.Euler(25, 0, 0);
            Shapes.Box(style, FUR, tail, new Vector3(0, 0, -0.18f), new Vector3(0.22f, 0.22f, 0.36f));
            Shapes.Box(style, FUR, tail, new Vector3(0, 0.02f, -0.44f), new Vector3(0.26f, 0.26f, 0.24f));
            Shapes.Box(style, LIGHT, tail, new Vector3(0, 0.025f, -0.6f), new Vector3(0.24f, 0.235f, 0.12f));

            // лёгкая «пиксельная» зернистость шерсти без воксельного шума
            foreach (var p in style.parts) p.mat.SetFloat("_Voxel", 0.04f);

            // кольцо выделения
            var ringSt = new GameObject("ring").AddComponent<Styleable>();
            ringSt.transform.SetParent(transform, false);
            ringSt.displayName = "ring";
            ringSt.AddPart("ring", StyleData.Make("Гладкий", "#FFB347"));
            ring = Shapes.Cyl(ringSt, 0, ringSt.transform, new Vector3(0, 0.01f, 0), 0.85f, 0.01f);
            Destroy(ring.GetComponent<Collider>());
            Destroy(ring.GetComponent<StyleRef>());
            ringSt.SetHighlight(-1, new Color(0.9f, 0.5f, 0.1f));
            ring.SetActive(false);

            ApplyShape();
            BuildEyes(); // глаза — маленькие воксельные фигурки, по форме которых видно эмоцию
            var fx = new GameObject("нужды").AddComponent<NeedFX>();
            fx.transform.SetParent(model, false);
            fx.fox = this;
        }

        Transform Pivot(Transform parent, Vector3 pos)
        {
            var t = new GameObject("pivot").transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        Transform Ear(Transform h, int side, int fur, int dark)
        {
            var p = Pivot(h, new Vector3(0.13f * side, 0.18f, -0.04f));
            p.localRotation = Quaternion.Euler(0, 0, -15 * side);
            Shapes.Box(style, fur, p, new Vector3(0, 0.08f, 0), new Vector3(0.13f, 0.16f, 0.06f));
            Shapes.Box(style, dark, p, new Vector3(0, 0.155f, 0), new Vector3(0.131f, 0.05f, 0.062f));
            return p;
        }

        Transform Leg(Transform parent, Vector3 hip, int part)
        {
            var p = Pivot(parent, hip);
            Shapes.Box(style, part, p, new Vector3(0, -0.135f, 0), new Vector3(0.12f, 0.27f, 0.12f));
            return p;
        }

        public void ApplyShape()
        {
            transform.localScale = Vector3.one * size * Family.StageScale(stage);
            if (head != null) head.localScale = Vector3.one * Family.HeadScale(stage);
            if (earL != null) { earL.localScale = Vector3.one * earSize; earR.localScale = Vector3.one * earSize; }
            if (tail != null) tail.localScale = new Vector3(tailFluff, tailFluff, 0.85f + 0.15f * tailFluff);
            if (style != null) style.displayName = foxName;
        }

        public void SetSelected(bool on) { if (ring != null) ring.SetActive(on); }

        // ================= настроение =================

        public float MoodletMul => 0.4f + pers.N * 0.12f;

        public float Mood
        {
            get
            {
                float avg = 0;
                foreach (var n in needs) avg += n;
                avg /= needs.Length;
                float m = avg - 50f;
                float mul = MoodletMul;
                foreach (var ml in moodlets) m += ml.value * mul * (ml.value < 0 && HasPerk("calm") ? 0.6f : 1f);
                return Mathf.Clamp(m, -100, 100);
            }
        }

        public static string MoodLabel(float m)
        {
            if (m > 40) return "прекрасное";
            if (m > 15) return "хорошее";
            if (m > -15) return "обычное";
            if (m > -40) return "плохое";
            return "ужасное";
        }

        public static Color MoodColor(float m)
        {
            if (m > 15) return new Color(0.35f, 0.75f, 0.35f);
            if (m > -15) return new Color(0.9f, 0.75f, 0.25f);
            return new Color(0.88f, 0.35f, 0.3f);
        }

        public void AddMoodlet(string name, float value, float minutes)
        {
            foreach (var m in moodlets)
                if (m.name == name) { m.value = value; m.minutesLeft = Mathf.Max(m.minutesLeft, minutes); return; }
            moodlets.Add(new Moodlet { name = name, value = value, minutesLeft = minutes });
        }

        void Condition(string name, bool on, float value)
        {
            if (on) AddMoodlet(name, value, 3);
        }

        public void Say(string text, float seconds = 2.8f)
        {
            bubble = text;
            bubbleTimer = seconds;
            sayCount++;
        }

        // ================= главный цикл =================

        public void Tick(float dm, float dr)
        {
            if (Net.IsClient)
            {
                // у второго игрока лисички повторяют то, что присылает хост
                NetStep();
                UpdateClothes();
                Animate(dr);
                if (bubbleTimer > 0) bubbleTimer -= Time.deltaTime;
                return;
            }
            if (dm > 0)
            {
                Decay(dm);
                UpdateMoodlets(dm);
                PersonalityEvents(dm);
                UpdateLife(dm);
                UpdateFamily(dm);
                UpdateKeys(dm);
                UpdateSideTalk(dm);
                if (needs[(int)Need.Energy] <= 0.5f && !sleeping && !atWork) PassOut();

                if (current == null)
                {
                    if (queue.Count > 0)
                    {
                        current = queue[0];
                        queue.RemoveAt(0);
                        current.stage = 0;
                    }
                    else
                    {
                        thinkCooldown -= dm;
                        if (thinkCooldown <= 0)
                        {
                            thinkCooldown = 3f;
                            if (G.AutonomyFor(home)) current = ChooseAutonomous();
                        }
                    }
                }
                if (current != null) Run(current, dm, dr);
            }
            UpdateClothes();
            Animate(dr);
            if (bubbleTimer > 0) bubbleTimer -= Time.deltaTime;
        }

        float wetTimer;

        /// <summary>Под дождём на улице лисички мокнут: большинству неприятно, а непоседливым лисятам — весело.</summary>
        void WeatherEffects(float dm)
        {
            var w = G.weather;
            if (w == null || !w.Raining || carried) return;
            wetTimer -= dm;
            if (wetTimer > 0) return;
            wetTimer = 20f;
            if (Level != 0 || G.house.RoomAt(0, Cell) != null) return; // под крышей сухо
            if (IsChild && pers.E >= 7) AddMoodlet("Прыжки по лужам!", 5, 120);
            else AddMoodlet(w.kind == WeatherKind.Storm ? "Гроза! Страшновато" : "Промок под дождём", w.kind == WeatherKind.Storm && pers.N >= 7 ? -9 : -5, 120);
        }

        void Decay(float dm)
        {
            WeatherEffects(dm);
            TasteEffects(dm);
            Chatter(dm);
            EventEffects(dm);
            float sm = sleeping ? 0.45f : 1f;
            needs[(int)Need.Hunger] -= 0.11f * dm * sm * (HasPerk("stomach") ? 0.75f : 1f);
            if (!sleeping) needs[(int)Need.Energy] -= 0.075f * dm;
            needs[(int)Need.Hygiene] -= 0.06f * dm * sm * (HasPerk("clean") ? 0.67f : 1f);
            if (!sleeping) needs[(int)Need.Fun] -= 0.09f * dm;
            needs[(int)Need.Social] -= 0.08f * (0.4f + pers.E * 0.12f) * dm * (sleeping ? 0.3f : 1f);
            for (int i = 0; i < needs.Length; i++) needs[i] = Mathf.Clamp(needs[i], 0, 100);

            Condition("Голодно", needs[0] < 20, -12);
            Condition("Клонит в сон", needs[1] < 20, -10);
            Condition("Пора помыться", needs[2] < 20, -8);
            Condition("Скучно", needs[3] < 20, -6 - Mathf.Max(0, pers.O - 5));
            Condition("Одиноко", needs[4] < 25, -(3 + pers.E));
        }

        void UpdateMoodlets(float dm)
        {
            for (int i = moodlets.Count - 1; i >= 0; i--)
            {
                moodlets[i].minutesLeft -= dm;
                if (moodlets[i].minutesLeft <= 0) moodlets.RemoveAt(i);
            }
        }

        void PersonalityEvents(float dm)
        {
            neuroTimer -= dm;
            if (neuroTimer <= 0)
            {
                neuroTimer = 60f;
                if (pers.N >= 6 && Random.value < (pers.N - 5) * 0.07f)
                {
                    if (Random.value < 0.5f) { AddMoodlet("Беспричинная тревога", -12, Random.Range(60, 150)); Say("Что-то мне неспокойно…"); }
                    else { AddMoodlet("Внезапная радость", 10, Random.Range(60, 120)); Say("Как же хорошо!"); }
                }
                else if (pers.N <= 2 && Random.value < 0.2f) AddMoodlet("Душевное равновесие", 4, 120);
            }

            messTimer -= dm;
            if (messTimer <= 0)
            {
                messTimer = 30f;
                int plates = G.PlatesOn(home);
                var hereL = G.house.LotAt(Cell);
                if (plates > 0 && pers.C >= 7 && hereL != null && hereL.id == home && !atWork)
                {
                    AddMoodlet("Беспорядок в доме", -(pers.C - 6) * 2.5f * Mathf.Min(plates, 3), 40);
                    if (Random.value < 0.3f) Say(Phrases.Pick("plates", this, null, "Кто бросил тарелку?!"));
                }
            }
        }

        void PassOut()
        {
            CancelCurrent(false);
            queue.Clear();
            current = new FoxAction { kind = ActKind.Idle, label = "Уснуть прямо на полу", floorSleep = true, duration = 240, stage = 0 };
            Say("Не могу больше…");
            G.LogFox(this, $"{foxName} засыпает прямо на полу.");
            AddMoodlet("Сон на полу", -8, 300);
        }

        // ================= команды =================

        /// <summary>Клетка — внутри чужого дома, от которого у нас нет ключа? Тогда номер участка, иначе −1.</summary>
        public int ForeignHomeAt(Vector2Int c, int level = 0)
        {
            var lot = G.house.LotAt(c);
            if (lot == null || CanEnter(lot.id)) return -1;                   // общественное, пустое или есть ключ
            if (G.house.RoomAt(level, c) == null) return -1;                 // во дворе — можно
            return lot.id;
        }

        public void Command(FoxAction a)
        {
            if (Net.IsClient) { if (IsPlayer) Net.I.SendCommand(this, a); return; }
            a.byPlayer = true;
            // в чужой дом — только с разрешения жильцов: сначала постучаться
            if (a.kind != ActKind.Work)
            {
                int foreign = -1;
                if (a.kind == ActKind.Move) foreign = ForeignHomeAt(GridMap.CellOf(a.moveTarget), G.house.LevelOf(a.moveTarget));
                else if (a.kind == ActKind.UseObject && a.furn != null) foreign = ForeignHomeAt(a.furn.anchor, a.furn.level);
                else if (a.kind == ActKind.Social && a.other != null) foreign = ForeignHomeAt(a.other.Cell, a.other.Level);
                if (foreign >= 0)
                {
                    // без ключа — сначала постучаться; впустят — займётся тем, что велели
                    queue.RemoveAll(q => q.kind == ActKind.Move && q.byPlayer);
                    if (current != null && current.kind == ActKind.Move && current.byPlayer) CancelCurrent(false);
                    if (current != null && !current.byPlayer && current.kind != ActKind.BeTalkedTo && current.kind != ActKind.Work) CancelCurrent(false);
                    if (queue.Count < 5) KnockAt(foreign, null, a.kind == ActKind.Move ? null : a);
                    return;
                }
                // поболтать, не отрываясь от дела: оба заняты и сидят рядом
                if (a.kind == ActKind.Social && a.other != null && TryStartSideTalk(a.other, a.social)) return;
            }
            // «иди сюда» имеет смысл, только если после него ничего нет: любой новый приказ его отменяет
            queue.RemoveAll(q => q.kind == ActKind.Move && q.byPlayer && q.onArrive == null);
            if (current != null && current.kind == ActKind.Move && current.byPlayer && current.onArrive == null) CancelCurrent(false);
            if (current != null && !current.byPlayer && current.kind != ActKind.BeTalkedTo && current.kind != ActKind.Work) CancelCurrent(false);
            if (queue.Count >= 5) return;
            queue.Add(a);
        }

        public void Cancel(FoxAction a)
        {
            if (Net.IsClient)
            {
                int idx = a == current ? -1 : queue.IndexOf(a);
                if (a == current || idx >= 0) Net.I.SendOp(this, "cancel", idx.ToString(), a.label);
                return;
            }
            if (a == current && a.kind == ActKind.Work)
            {
                if (atWork) EndShift(a, true);
                else
                {
                    performance -= 15f;
                    G.LogFox(this, $"{foxName}: прогул работы");
                    CancelCurrent(false);
                }
                return;
            }
            if (a == current) CancelCurrent(false);
            else queue.Remove(a);
        }

        public void CancelFor(Furniture f)
        {
            if (current != null && current.furn == f) CancelCurrent(false);
            queue.RemoveAll(q => q.furn == f);
        }

        public void CancelFor(Fox other)
        {
            if (current != null && current.other == other) CancelCurrent(false);
            queue.RemoveAll(q => q.other == other);
        }

        void CancelCurrent(bool keepForLater)
        {
            var a = current;
            if (a == null) return;
            if (a.kind == ActKind.UseObject && a.stage >= 3 && heldKind == "plate") G.SpawnPlate(this);
            ReleaseUse(a);
            Drop();
            PutDown();
            if (a.kind == ActKind.Social && a.other != null && a.other.current != null &&
                a.other.current.kind == ActKind.BeTalkedTo && a.other.current.other == this)
                a.other.EndTalk();
            if (a.kind == ActKind.BeTalkedTo && a.other != null && a.other.current != null && a.other.current.other == this)
                a.other.Finish(a.other.current);
            current = null;
            path = null;
            walking = false;
            sleeping = false;
            pose = Pose.Stand;
            if (keepForLater && ((a.byPlayer && a.kind == ActKind.UseObject) || (a.kind == ActKind.Move && a.onArrive != null)))
            {
                a.stage = 0;
                queue.Insert(0, a);
            }
        }

        /// <summary>Диван или кресло напротив телевизора — смотреть оттуда.</summary>
        bool FindSeatFor(FoxAction a)
        {
            var tv = a.furn;
            Furniture best = null; int bestSlot = -1; Vector2Int bestStand = default; List<Vector3> bestPath = null;
            float bestD = float.MaxValue;
            var tvFront = tv.transform.TransformDirection(Vector3.back);
            foreach (var f in G.furniture)
            {
                if (f.level != tv.level || f.lot != tv.lot) continue;
                if (f.def.id != "sofa" && f.def.id != "armchair" && f.def.id != "pouf") continue;
                var to = tv.transform.position - f.transform.position; to.y = 0;
                float d = to.magnitude;
                if (d < 1.4f || d > 6.5f) continue;
                if (Vector3.Dot(tvFront, -to / d) < 0.55f) continue;               // диван — перед экраном
                if (Vector3.Dot(f.transform.TransformDirection(Vector3.back), to / d) < 0.55f) continue; // и смотрит на него
                if (!f.FindSlot(this, G.house, out int s, out var st, out var p)) continue;
                if (f.def.slots[s].pose == Vector3.zero) continue;
                if (d < bestD) { bestD = d; best = f; bestSlot = s; bestStand = st; bestPath = p; }
            }
            if (best == null) return false;
            a.seat = best; a.seatSlot = bestSlot; a.stand = bestStand; a.slot = -1;
            best.occupants[bestSlot] = this;
            Reserve(best.level, bestStand);
            StartPath(bestPath);
            return true;
        }

        void ReleaseUse(FoxAction a)
        {
            if (a.kind == ActKind.UseObject && a.seat != null)
            {
                if (a.seatSlot >= 0 && a.seatSlot < a.seat.occupants.Length && a.seat.occupants[a.seatSlot] == this) a.seat.occupants[a.seatSlot] = null;
                if (a.stage == 2)
                {
                    if (a.furn != null) a.furn.inUse = Mathf.Max(0, a.furn.inUse - 1);
                    transform.position = GridMap.Center(a.stand) + Vector3.up * G.house.grids[a.seat.level].HeightAt(a.stand);
                }
                a.seat = null; a.seatSlot = -1;
                return;
            }
            if (a.kind != ActKind.UseObject || a.furn == null || a.slot < 0) return;
            if (a.slot < a.furn.occupants.Length && a.furn.occupants[a.slot] == this) a.furn.occupants[a.slot] = null;
            if (a.stage == 2 || a.stage == 5)
            {
                a.furn.inUse = Mathf.Max(0, a.furn.inUse - 1);
                if (a.inter.onObject)
                {
                    transform.position = GridMap.Center(a.stand) + Vector3.up * G.house.grids[a.furn.level].HeightAt(a.stand);
                }
            }
            a.slot = -1;
        }

        void Finish(FoxAction a)
        {
            Drop();
            PutDown();
            Reserve(Level, Cell); // стоим здесь
            if (a.kind == ActKind.UseObject || a.kind == ActKind.Social)
            {
                history.Add(a.kind == ActKind.UseObject ? a.inter.name : a.social.name);
                if (history.Count > 4) history.RemoveAt(0);
            }
            current = null;
            walking = false;
            sleeping = false;
            pose = Pose.Stand;
            thinkCooldown = Random.Range(0.5f, 2f);
            // с нами говорили, пока мы были заняты — дело закончено, теперь можно поговорить как следует
            if (a.kind == ActKind.UseObject && queue.Count == 0 || a.kind == ActKind.UseObject && !queue[0].byPlayer)
                foreach (var o in G.foxes)
                {
                    var oc = o != null ? o.current : null;
                    if (oc == null || oc.kind != ActKind.Social || oc.other != this || !oc.busyTalk || oc.stage != 2) continue;
                    oc.busyTalk = false;
                    oc.duration = Mathf.Max(oc.duration, oc.elapsed + Random.Range(8f, 14f));
                    BeginTalk(o, oc.duration - oc.elapsed, oc.social);
                    Face(o.transform.position);
                    o.Face(transform.position);
                    break;
                }
        }

        void Fail(FoxAction a, string msg)
        {
            ReleaseUse(a);
            Drop();
            if (a.byPlayer)
            {
                Say(msg);
                G.LogFox(this, $"{foxName}: «{a.DisplayText()}» — {msg}");
            }
            current = null;
            walking = false;
            sleeping = false;
            pose = Pose.Stand;
            thinkCooldown = 2f;
        }

        // ================= выполнение =================

        void Run(FoxAction a, float dm, float dr)
        {
            switch (a.kind)
            {
                case ActKind.UseObject: RunUse(a, dm, dr); break;
                case ActKind.Social: RunSocial(a, dm, dr); break;
                case ActKind.BeTalkedTo: RunTalked(a, dm); break;
                case ActKind.Move:
                case ActKind.Wander: RunMove(a, dr); break;
                case ActKind.Idle: RunIdle(a, dm); break;
                case ActKind.Work: RunWork(a, dm, dr); break;
                case ActKind.Phone: RunPhone(a, dm); break;
            }
        }

        void RunUse(FoxAction a, float dm, float dr)
        {
            if (a.stage >= 3) { RunCarry(a, dm, dr); return; }
            if (a.furn == null || !G.furniture.Contains(a.furn)) { Fail(a, "Предмета больше нет"); return; }
            if (a.stage == 0 && a.inter != null && a.inter.fromSeat && FindSeatFor(a))
            {
                a.stage = 1;
            }
            if (a.stage == 0)
            {
                if (!a.furn.FindSlot(this, G.house, out a.slot, out a.stand, out var p, a.inter != null ? a.inter.slotOnly : -1)) { Fail(a, "Не подойти"); return; }
                a.furn.occupants[a.slot] = this;
                Reserve(a.furn.level, a.stand);
                StartPath(p);
                a.stage = 1;
            }
            if (a.stage == 1)
            {
                if (!FollowPath(dr)) return;
                if (a.seat != null)
                {
                    transform.position = a.seat.PosePos(a.seatSlot);
                    transform.rotation = a.seat.PoseRot(a.seatSlot);
                }
                else if (a.inter.onObject)
                {
                    transform.position = a.furn.PosePos(a.slot);
                    transform.rotation = a.furn.PoseRot(a.slot);
                }
                else Face(a.furn.transform.position);
                pose = a.seat != null ? Pose.Sit : a.inter.pose;
                sleeping = a.inter.sleep;
                a.funMul = FunMultiplier(a.inter);
                a.elapsed = 0;
                a.stage = 2;
                a.furn.inUse++;
                if (!string.IsNullOrEmpty(a.inter.bubble)) Say(a.inter.bubble);
                if (!string.IsNullOrEmpty(a.inter.hold) && a.inter.carryTo == null) Hold(a.inter.hold);
            }
            if (a.stage == 2)
            {
                a.elapsed += dm;
                var it = a.inter;
                if (it.openWardrobe)
                {
                    // стоит у шкафа, пока игрок выбирает одежду
                    pose = Pose.Use;
                    if (Net.RemoteOwned(this))
                    {
                        // лисичка друга: ждём, пока друг выберет одежду у себя
                        if (!a.reacted) { a.reacted = true; Net.I.SendOpen(this, 0); }
                        if (a.netDone) Finish(a);
                        return;
                    }
                    if (!a.reacted) { a.reacted = true; if (G.selected == this) { G.ui.clothesOpen = true; G.ui.outfitTab = 0; } }
                    if (!G.ui.clothesOpen || G.selected != this) { G.ui.clothesOpen = false; Finish(a); }
                    return;
                }
                if (it.openBuild)
                {
                    // стоит у мольберта, пока игрок перестраивает дом
                    pose = Pose.Use;
                    if (Net.RemoteOwned(this))
                    {
                        if (!a.reacted) { a.reacted = true; Net.I.SendOpen(this, 1); }
                        if (a.netDone) Finish(a);
                        return;
                    }
                    if (!a.reacted) { a.reacted = true; if (G.selected == this) G.SetMode(Mode.Build); }
                    if ((G.mode != Mode.Build && G.mode != Mode.Style) || G.selected != this) Finish(a);
                    return;
                }
                if (it.carryTo != null)
                {
                    if (a.elapsed >= 1.5f) BeginCarry(a);
                    return;
                }
                float hungerMul = it.skill == Skill.Cooking ? 0.8f + SkillLevel(Skill.Cooking) * 0.05f : 1f;
                float energyMul = it.sleep && it.energy > 0 && HasPerk("sleep") ? 1.3f : 1f;
                needs[0] += it.hunger * dm * hungerMul;
                needs[1] += it.energy * dm * energyMul;
                if (it.hygiene <= 0 || needs[2] < it.hygieneCap) needs[2] = it.hygiene > 0 ? Mathf.Min(it.hygieneCap, needs[2] + it.hygiene * dm) : needs[2] + it.hygiene * dm;
                needs[3] += it.fun * dm * a.funMul;
                needs[4] += it.social * dm;
                // вместе за одним устройством: смотрят, листают, пишут вдвоём — веселее и почти как разговор
                int mates = Together(a);
                if (mates > 0)
                {
                    needs[4] += 0.12f * dm;
                    needs[3] += it.fun * dm * 0.15f;
                    if (!a.together)
                    {
                        a.together = true;
                        string what = it.name.Contains("телевизор") ? "Смотрим вместе" : it.name.Contains("роман") ? "Пишем роман вместе" : it.name.Contains("интернет") ? "Вместе в интернете" : "Вместе веселее";
                        AddMoodlet(what, pers.E >= 4 ? 5 : 2, 120);
                        foreach (var o in a.furn.occupants)
                            if (o != null && o != this && o.current != null && o.current.furn == a.furn) Social.AddRel(this, o, 2f);
                    }
                }
                for (int i = 0; i < needs.Length; i++) needs[i] = Mathf.Clamp(needs[i], 0, 100);
                if (it.skill != Skill.None) GainSkill(it.skill, it.skillRate * dm);
                bool full = false;
                if (it.endWhenFull)
                {
                    full = true;
                    for (int i = 0; i < 5; i++) if (it.Gain((Need)i) > 0 && needs[i] < (i == 2 ? it.hygieneCap : 100f) - 1f) full = false;
                    if (it.sleep && G.Hour >= 22 || it.sleep && G.Hour < 6) full = full && needs[1] >= 100;
                }
                if (a.elapsed >= it.duration * DurationMul(it) || full) Complete(a);
            }
        }

        /// <summary>Сколько ещё лисичек сейчас заняты тем же самым за этим же предметом.</summary>
        int Together(FoxAction a)
        {
            if (a.furn == null || a.furn.occupants == null) return 0;
            int n = 0;
            foreach (var o in a.furn.occupants)
                if (o != null && o != this && o.current != null && o.current.furn == a.furn && o.current.inter == a.inter && o.current.stage == 2) n++;
            return n;
        }

        void Complete(FoxAction a)
        {
            var it = a.inter;
            var furn = a.furn;
            furn.lastFinished = G.minutes;
            ReleaseUse(a);
            // счётчики для мечты всей жизни
            if (it.skill == Skill.Cooking && it.makesPlate) Count("cook");
            else if (it.skill == Skill.Music) Count("music");
            else if (it.skill == Skill.Logic) Count("study");
            else if (it.skill == Skill.Housework) Count("tidy");
            it.onFinish?.Invoke(this);
            OnEvent(new GameEvent { kind = "act", arg = it.name });
            Furniture plate = null;
            if (it.makesPlate)
            {
                var chair = DiningChair();
                if (chair != null)
                    queue.Insert(0, new FoxAction { kind = ActKind.UseObject, furn = chair, inter = Catalog.EatAtTable, label = Catalog.EatAtTable.verb });
                else plate = G.SpawnPlate(this);
            }
            else if (it == Catalog.EatAtTable) plate = G.SpawnPlateOnTable(this, furn);
            Finish(a);
            if (plate != null && WantsToWashNow()) WashLater(plate, true);
            if (it.removesObject) G.RemoveFurniture(furn);
        }

        float FunMultiplier(Interaction it)
        {
            float m = 1f;
            if ((it.tags & ActTag.Creative) != 0) m *= 1f + (pers.O - 5.5f) * 0.08f;
            if ((it.tags & ActTag.Routine) != 0) m *= 1f + (5.5f - pers.O) * 0.08f;
            if ((it.tags & ActTag.Solitary) != 0) m *= 1f + (5.5f - pers.E) * 0.04f;
            if (history.Contains(it.name)) m *= Mathf.Clamp(1f - (pers.O - 3) * 0.07f, 0.4f, 1.15f);
            return Mathf.Max(0.3f, m);
        }

        void RunMove(FoxAction a, float dr)
        {
            if (a.stage == 0)
            {
                Vector2Int target;
                int tl = Level;
                if (a.kind == ActKind.Wander)
                {
                    target = Cell;
                    for (int i = 0; i < 12; i++)
                    {
                        var c = Cell + new Vector2Int(Random.Range(-4, 5), Random.Range(-4, 5));
                        if (Grid.IsFree(c) && !Taken(this, Level, c)) { target = c; break; }
                    }
                }
                else
                {
                    tl = G.house.LevelOf(a.moveTarget);
                    target = FreeNear(tl, G.house.grids[tl].ClampCell(GridMap.CellOf(a.moveTarget)));
                }
                var p = G.house.FindPath(Level, Cell, tl, target);
                Reserve(tl, target);
                if (p == null) { Fail(a, "Туда не пройти"); return; }
                StartPath(p);
                a.stage = 1;
            }
            if (a.stage == 1 && FollowPath(dr))
            {
                if (a.kind == ActKind.Wander)
                {
                    a.kind = ActKind.Idle;
                    a.label = "Осматривается";
                    a.duration = Random.Range(8f, 20f);
                    a.elapsed = 0;
                    a.stage = 1;
                }
                else { var cb = a.onArrive; Finish(a); cb?.Invoke(); }
            }
        }

        void RunIdle(FoxAction a, float dm)
        {
            a.elapsed += dm;
            if (a.floorSleep)
            {
                pose = Pose.Lie;
                sleeping = true;
                needs[1] = Mathf.Min(100, needs[1] + 0.2f * dm);
                if (a.elapsed >= a.duration || needs[1] >= 70) Finish(a);
                return;
            }
            pose = Pose.Stand;
            if (a.elapsed >= a.duration) Finish(a);
        }

        // ---------- общение ----------

        public bool CanBeApproached(Fox by) => BusyReason(by) == null;

        /// <summary>Почему сейчас нельзя заговорить (null — можно). Поболтать можно и за делом — кроме сна и ванны.</summary>
        public string BusyReason(Fox by)
        {
            if (by == this) return "это ты сам" + (female ? "а" : "");
            if (atWork) return career != null && career.id == "school" ? "сейчас на уроке" : "сейчас на работе";
            if (carried) return "сейчас на руках";
            if (sleeping) return "спит";
            if (side != null) return "болтает с " + (side.other != null ? side.other.NameIn(NameCase.Instrumental) : "кем-то");
            if (knock != null) return "стучится в дверь";
            if (current == null) return null;
            switch (current.kind)
            {
                case ActKind.Move: return current.onArrive == null ? null : "спешит по делам";
                case ActKind.Idle: return current.floorSleep ? "спит" : null;
                case ActKind.UseObject:
                    if (current.inter.sleep) return "спит";
                    if (current.inter.pose == Pose.Bath) return "принимает ванну";
                    return null;
                case ActKind.Social:
                case ActKind.BeTalkedTo: return "уже с кем-то разговаривает";
                case ActKind.Phone: return "говорит по телефону";
                case ActKind.Work: return "собирается на работу";
                default: return null;
            }
        }

        /// <summary>Занят(а) делом, но может поболтать, не отрываясь.</summary>
        bool ChatsWhileBusy => current != null && current.kind == ActKind.UseObject && current.stage == 2;

        void RunSocial(FoxAction a, float dm, float dr)
        {
            var o = a.other;
            if (o == null || !G.foxes.Contains(o)) { Fail(a, "Не с кем говорить"); return; }
            if (a.stage == 0)
            {
                var why = o.BusyReason(this);
                if (why != null) { Fail(a, o.foxName + " " + why); return; }
                if (!PathNear(o)) { Fail(a, "Не подойти"); return; }
                a.targetSeen = o.transform.position;
                a.repathTimer = 0.5f;
                a.stage = 1;
            }
            if (a.stage == 1)
            {
                // собеседник ушёл с места — идём за ним, а не туда, где он был
                a.repathTimer -= dr;
                if (a.repathTimer <= 0)
                {
                    a.repathTimer = 0.5f;
                    if (Vector3.Distance(Flat(o.transform.position), Flat(a.targetSeen)) > 0.9f)
                    {
                        a.targetSeen = o.transform.position;
                        PathNear(o);
                    }
                }
                bool arrived = FollowPath(dr);
                float dist = Vector3.Distance(Flat(transform.position), Flat(o.transform.position));
                if (dist < 1.25f) arrived = true;
                if (!arrived) return;
                if (dist > 1.9f)
                {
                    if (++a.repaths > 12 || !PathNear(o)) Fail(a, "Не догнать");
                    else a.targetSeen = o.transform.position;
                    return;
                }
                var why2 = o.BusyReason(this);
                if (why2 != null) { Fail(a, o.foxName + " " + why2); return; }
                walking = false;
                path = null;
                a.duration = a.social.duration;
                a.chaseCenter = transform.position;
                a.busyTalk = o.ChatsWhileBusy && !a.social.carry;
                if (!a.busyTalk) { o.BeginTalk(this, a.duration, a.social); o.Face(transform.position); }
                Face(o.transform.position);
                pose = Pose.Talk;
                float chance = Mathf.Clamp(a.social.accept(this, o) + SkillLevel(Skill.Charisma) * 0.015f + (HasPerk("charm") ? 0.1f : 0f), 0.05f, 0.95f);
                a.success = a.social.sure || Random.value < chance;
                a.elapsed = 0;
                a.stage = 2;
                Say(Phrases.Pick("say:" + a.social.name, this, o, a.social.say));
            }
            if (a.stage == 2)
            {
                a.elapsed += dm;
                if (a.social.chase && a.success && !a.busyTalk && a.elapsed > a.duration * 0.12f && ChaseRun(a, dr)) { }
                else if (a.social.carry) CarryWalk(dr);
                else if (a.social.boop)
                {
                    // тянется носиком к носику — «буп!» — и обратно
                    pose = Pose.Talk;
                    float k = a.elapsed / a.duration;
                    float lean = Mathf.Sin(Mathf.Clamp01((k - 0.15f) / 0.3f) * Mathf.PI);
                    var to = Flat(o.transform.position) - Flat(a.chaseCenter);
                    float reach = Mathf.Max(0f, to.magnitude - 0.55f * (size + o.size));
                    transform.position = a.chaseCenter + to.normalized * reach * lean;
                    if (!a.booped && k > 0.3f)
                    {
                        a.booped = true;
                        if (G.sound != null && IsPlayer) G.sound.Click(); // тихий «буп»
                    }
                }
                else pose = Pose.Talk;
                needs[4] = Mathf.Min(100, needs[4] + 1.7f * dm);
                GainSkill(Skill.Charisma, 0.5f * dm);
                if (pers.E <= 3) needs[1] = Mathf.Max(0, needs[1] - 0.04f * dm);
                if (!a.reacted && a.elapsed > a.duration * 0.45f)
                {
                    a.reacted = true;
                    React(a);
                }
                if (a.elapsed >= a.duration)
                {
                    if (!a.busyTalk) o.EndTalk();
                    if (pers.E <= 3 && needs[4] > 85) AddMoodlet("Хочется тишины", -4, 60);
                    Finish(a);
                }
            }
        }

        /// <summary>Догонялки: двое бегают по кругу, время от времени меняясь ролями.</summary>
        bool ChaseRun(FoxAction a, float dr)
        {
            var o = a.other;
            const float R = 1.4f;
            if (!a.chasing)
            {
                var mid = (transform.position + o.transform.position) * 0.5f;
                var g = G.house.grids[Level];
                var cc = GridMap.CellOf(mid);
                float h0 = g.HeightAt(cc);
                for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        if (dx * dx + dz * dz > 5) continue;
                        var c = cc + new Vector2Int(dx, dz);
                        if (!g.IsFree(c) || Mathf.Abs(g.HeightAt(c) - h0) > 0.3f) return false;
                    }
                a.chasing = true;
                a.chaseCenter = GridMap.Center(cc) + Vector3.up * h0;
                var d0 = transform.position - a.chaseCenter;
                a.chaseAngle = Mathf.Atan2(d0.z, d0.x);
                a.chaseSwap = Random.Range(3f, 6f);
            }
            float sp = dr * 2.2f / R;
            a.chaseAngle += sp * a.chaseDir;
            a.chaseSwap -= dr;
            if (a.chaseSwap <= 0)
            {
                a.chaseDir = -a.chaseDir;              // поймал — теперь водит другой
                a.chaseSwap = Random.Range(3f, 6f);
                (Random.value < 0.5f ? this : o).Say(Random.value < 0.5f ? "Осалил{|а}!".Replace("{|а}", female ? "а" : "") : "Ха-ха!");
            }
            void Place(Fox f, float ang, float lead)
            {
                float r = R + Mathf.Sin(ang * 2f) * 0.25f;
                var p = a.chaseCenter + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
                var tangent = new Vector3(-Mathf.Sin(ang), 0, Mathf.Cos(ang)) * a.chaseDir;
                f.transform.position = p;
                f.transform.rotation = Quaternion.LookRotation(tangent);
                f.walking = true;
                f.pose = Pose.Stand;
            }
            Place(o, a.chaseAngle, 0);
            Place(this, a.chaseAngle - 0.9f * a.chaseDir, 0);
            return true;
        }

        void React(FoxAction a)
        {
            var s = a.social;
            var o = a.other;
            o.Say(Phrases.Pick((a.success ? "yes:" : "no:") + s.name, o, this, a.success ? s.yes : s.no));
            Social.AddRel(this, o, a.success ? s.relYes : s.relNo);
            if (s.romYes != 0 || s.romNo != 0) Social.AddRom(this, o, a.success ? s.romYes : s.romNo);
            if (a.success && s.funYes > 0)
            {
                needs[3] = Mathf.Min(100, needs[3] + s.funYes);
                o.needs[3] = Mathf.Min(100, o.needs[3] + s.funYes);
            }
            if (a.success) s.onSuccess?.Invoke(this, o);
            if (a.success)
            {
                if (s.moodYesA != null) AddMoodlet(s.moodYesA, s.valYesA, 120);
                if (s.moodYesB != null) o.AddMoodlet(s.moodYesB, s.valYesB, 120);
            }
            else
            {
                if (s.moodNoA != null) AddMoodlet(s.moodNoA, s.valNoA, 90);
                if (s.moodNoB != null) o.AddMoodlet(s.moodNoB, s.valNoB, 120);
                if (s.name == "Подразнить" && pers.A <= 3) AddMoodlet("Пакость удалась", 3, 60);
            }
            G.LogPair(this, o, $"{foxName} — {s.ActivityText(o)}. {(a.success ? "Удачно" : "Неудачно")} ({(a.success ? s.relYes : s.relNo):+0;-0;0})");
            OnEvent(new GameEvent { kind = "social", arg = s.name, other = o, success = a.success });
            o.OnEvent(new GameEvent { kind = "social_target", arg = s.name, other = this, success = a.success });
        }

        bool PathNear(Fox o)
        {
            var oc = o.Cell;
            var og = G.house.grids[o.Level];
            List<Vector3> best = null;
            Vector2Int bestCell = default;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var c = oc + new Vector2Int(dx, dz);
                    if (!og.IsFree(c) || og.EdgeBlocked(oc, c) || Taken(this, o.Level, c)) continue;
                    var p = G.house.FindPath(Level, Cell, o.Level, c);
                    if (p != null && (best == null || p.Count < best.Count)) { best = p; bestCell = c; }
                }
            if (best == null) return false;
            Reserve(o.Level, bestCell);
            StartPath(best);
            return true;
        }

        public void BeginTalk(Fox with, float duration, SocialDef def = null)
        {
            if (current != null) CancelCurrent(true);
            current = new FoxAction { kind = ActKind.BeTalkedTo, other = with, social = def, duration = duration, stage = 2 };
            current.label = current.DisplayText(true);
            pose = Pose.Talk;
        }

        public void EndTalk()
        {
            if (current != null && current.kind == ActKind.BeTalkedTo) Finish(current);
        }

        void RunTalked(FoxAction a, float dm)
        {
            a.elapsed += dm;
            bool chased = a.other != null && a.other.current != null && a.other.current.chasing;
            if (!chased) pose = Pose.Talk;
            needs[4] = Mathf.Min(100, needs[4] + 1.5f * dm);
            GainSkill(Skill.Charisma, 0.15f * dm);
            if (pers.E <= 3) needs[1] = Mathf.Max(0, needs[1] - 0.04f * dm);
            if (a.social != null && a.social.carry && a.other != null) FollowCarrier(a.other);
            else if (a.other != null && !chased) Face(a.other.transform.position, 0.2f);
            bool partnerGone = a.other == null || a.other.current == null || a.other.current.kind != ActKind.Social || a.other.current.other != this;
            if (a.elapsed >= a.duration + 1f || partnerGone) Finish(a);
        }

        // ---------- свобода воли ----------

        float Urgency(Need n)
        {
            float T = 40f + pers.C * 4f; // аккуратные заботятся о себе заранее, беспечные — в последний момент
            float u = Mathf.Clamp01((T - needs[(int)n]) / T);
            return 0.05f + u * u * 3f;
        }

        public float ScoreInteraction(Interaction it, Furniture f)
        {
            float s = 0;
            for (int i = 0; i < 5; i++)
            {
                float g = it.Gain((Need)i);
                if (g <= 0) continue;
                float total = Mathf.Min(g * it.duration, 100f - needs[i]);
                s += total / 100f * Urgency((Need)i) * 2f;
            }
            if ((it.tags & ActTag.Creative) != 0) s += (pers.O - 5.5f) * 0.06f;
            if ((it.tags & ActTag.Routine) != 0) s += (5.5f - pers.O) * 0.05f;
            if ((it.tags & ActTag.Tidy) != 0) s += (pers.C - 5.5f) * 0.09f * (Mood < -20 ? 0.5f : 1f);
            if ((it.tags & ActTag.Solitary) != 0) s += (5.5f - pers.E) * 0.04f;
            if (history.Contains(it.name)) s -= (pers.O - 3) * 0.035f;
            if (f.occupants != null && f.occupants.Length > 1)
                foreach (var o in f.occupants)
                    if (o != null && o != this && o.current != null && o.current.furn == f && o.current.inter == it)
                        s += 0.12f + Social.Rel(this, o) / 350f + (pers.E - 5.5f) * 0.03f + Urgency(Need.Social) * 0.25f;
            if (it.cooldown > 0 && G.minutes - f.lastFinished < it.cooldown) s -= 1f;
            if (it.sleep)
            {
                float h = G.Hour;
                bool night = h >= 21.5f || h < 7f;
                bool longSleep = it.duration >= 100;
                if (longSleep)
                {
                    if (night && needs[1] < 90) s += 0.3f + 0.7f * (1f - needs[1] / 100f);
                    else if (needs[1] > 20) s -= 0.8f; // днём на всю ночь не ложатся
                }
                else
                {
                    if (night) s -= 0.25f;       // ночью — спать, а не дремать
                    else if (needs[1] > 60) s -= 0.3f;
                }
            }
            s -= Vector3.Distance(transform.position, f.transform.position) * 0.01f;
            return s;
        }

        FoxAction ChooseAutonomous()
        {
            var cands = new List<(float score, FoxAction act)>();
            var hereLot = G.house.LotAt(Cell);
            bool outside = hereLot == null || (hereLot.id != home && !IsGuestAt(hereLot.id));
            if (guestLot >= 0 && !IsGuestAt(guestLot)) guestLot = -1;
            // в чужом доме без приглашения не задерживаются
            if (hereLot != null && hereLot.kind == LotKind.Home && hereLot.id != home && !IsGuestAt(hereLot.id))
                return new FoxAction { kind = ActKind.Move, moveTarget = GridMap.Center(HomeEntry) + Vector3.up * G.house.FloorY(0, HomeEntry), label = "Идёт домой" };
            bool open = Town.IsOpen(G.Hour);
            // желание выбраться в город: общительным и любопытным — сильнее, скучающим — тоже
            float outing = (pers.E - 5.5f) * 0.03f + (pers.O - 5.5f) * 0.025f + (100f - needs[(int)Need.Fun]) / 100f * 0.15f
                + (100f - needs[(int)Need.Social]) / 100f * 0.1f - 0.1f;
            // одно и то же занятие (гитара, книга, телевизор…) делают там, где ближе: своя гитара дома
            // лучше чужой на другом конце города. Из одинаковых занятий остаётся одно — самое удобное.
            var bestSame = new Dictionary<string, (float s, float pick, FoxAction a)>();
            foreach (var f in G.furniture)
            {
                if (f.def.actions.Count == 0 || !f.HasFreeSlot(this)) continue;
                var lot = G.house.LotById(f.lot);
                bool mine = f.lot == home || IsGuestAt(f.lot);
                bool pub = lot != null && lot.Public;
                var ev = pub ? TownEvents.Active(lot.kind, G.Day, G.Hour) : null;
                if (!mine && !(pub && (open || ev != null))) continue;   // в чужие дома без приглашения не ходят
                float pubBonus = 0;
                if (pub)
                {
                    bool there = hereLot != null && hereLot.id == f.lot;
                    pubBonus = there ? 0.08f : outing;
                    pubBonus += Vector3.Distance(transform.position, f.transform.position) * 0.007f; // по городу идти не так «дорого»
                }
                // праздник в городе манит всех (общительных — сильнее)
                if (ev != null) pubBonus += 0.3f + (pers.E - 5.5f) * 0.03f;
                // в гостях — остаются у хозяев, а не убегают по делам
                if (guestLot >= 0 && IsGuestAt(guestLot)) pubBonus += f.lot == guestLot ? 0.2f : -0.6f;
                foreach (var it in f.def.actions)
                {
                    if (it.playerOnly || !CanUse(it) || (it.blocked != null && it.blocked(this, f) != null)) continue;
                    float s = ScoreInteraction(it, f) + pubBonus + Random.value * 0.08f;
                    var act = new FoxAction { kind = ActKind.UseObject, furn = f, inter = it, label = it.verb ?? it.name };
                    bool here = hereLot != null && hereLot.id == f.lot;
                    float pick = s - Vector3.Distance(transform.position, f.transform.position) * 0.03f - (here ? 0f : 0.15f) - (ev != null ? -0.3f : 0f);
                    if (!bestSame.TryGetValue(it.name, out var cur) || pick > cur.pick) bestSame[it.name] = (s, pick, act);
                }
            }
            foreach (var b in bestSame.Values) cands.Add((b.s, b.a));
            // прогулка в парк днём
            if (open && stage >= LifeStage.Kit)
            {
                var park = G.house.lots.Find(l => l.kind == LotKind.Park);
                if (park != null && (hereLot == null || hereLot.id != park.id))
                {
                    var c = new Vector2Int(Random.Range(park.rect.xMin + 2, park.rect.xMax - 2), Random.Range(park.rect.yMin + 2, park.rect.yMax - 2));
                    if (G.house.grid.IsFree(c))
                        cands.Add((outing + 0.02f + Random.value * 0.05f, new FoxAction { kind = ActKind.Move, moveTarget = GridMap.Center(c), label = "Гуляет в парке" }));
                }
            }
            // в дождь гулять не хочется
            if (G.weather != null && G.weather.Raining) outing -= 0.45f;
            // вечером, ночью и в дождь — домой
            var hereEv = hereLot != null && hereLot.Public ? TownEvents.Active(hereLot.kind, G.Day, G.Hour) : null;
            if (outside && ((!open && hereEv == null) || needs[(int)Need.Energy] < 25f || (G.weather != null && G.weather.Raining)))
                cands.Add((0.7f, new FoxAction { kind = ActKind.Move, moveTarget = GridMap.Center(HomeEntry), label = "Идёт домой" }));
            float socialNeed = Urgency(Need.Social);
            float extra = (pers.E - 5.5f) * 0.02f;
            foreach (var o in G.foxes)
            {
                if (o == this || !o.CanBeApproached(this)) continue;
                float meters = Vector3.Distance(transform.position, o.transform.position);
                if (meters > (o.home == home ? 30f : 14f)) continue;   // с соседями — когда они рядом
                float relBonus = Social.Rel(this, o) / 600f + (guestLot >= 0 && IsGuestAt(guestLot) && o.home == guestLot ? 0.25f : 0f);
                float dist = meters * 0.01f;
                foreach (var sd in Social.All)
                {
                    if (!sd.available(this, o)) continue;
                    float want = sd.want(this, o);
                    if (want < -0.1f) continue;
                    float s = socialNeed * 0.6f * (0.4f + want) + extra + relBonus - dist + Random.value * 0.05f;
                    cands.Add((s, FoxAction.ForSocial(sd, o)));
                }
            }
            cands.Add((0.08f + Random.value * 0.06f, new FoxAction { kind = ActKind.Wander, label = "Бродит по дому" }));

            float best = float.MinValue;
            foreach (var c in cands) best = Mathf.Max(best, c.score);
            var top = cands.FindAll(c => c.score >= best - 0.12f);
            float sum = 0;
            foreach (var c in top) sum += Mathf.Exp((c.score - best) / 0.05f);
            float r = Random.value * sum;
            foreach (var c in top)
            {
                r -= Mathf.Exp((c.score - best) / 0.05f);
                if (r <= 0) return c.act;
            }
            return top[top.Count - 1].act;
        }

        // ================= движение и анимация =================

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void StartPath(List<Vector3> p)
        {
            path = p;
            pathIdx = 0;
            runCheck = 0;
        }

        // бег: лисичка сама решает — чем бодрее и быстрее она обычно ходит, тем чаще срывается на бег
        public bool running;
        float runCheck;
        const float RunMul = 1.9f;

        void DecideRun()
        {
            float s = WalkSpeedNow;
            int left = path != null ? path.Count - pathIdx : 0;
            float p = Mathf.Clamp01((s - 1.45f) / 0.85f) * 0.8f;
            if (left < 6) p *= 0.25f;                                   // пару шагов — пешком
            if (G.house.RoomAt(Level, Cell) != null) p *= 0.35f;       // дома тесно
            if (stage == LifeStage.Elder) p *= 0.15f;
            if (needs[1] < 25) p *= 0.3f;                               // устала
            if (running) p = Mathf.Max(p, 0.55f * p + 0.35f);          // бежит — не сразу переходит на шаг
            running = Random.value < p;
        }

        bool FollowPath(float dr)
        {
            if (path == null || pathIdx >= path.Count) { walking = false; running = false; return true; }
            walking = true;
            pose = Pose.Stand;
            sleeping = false;
            runCheck -= dr;
            if (runCheck <= 0) { runCheck = Random.Range(1.5f, 3.5f); DecideRun(); }
            var pos = transform.position;
            var tgt = path[pathIdx];
            var d3 = tgt - pos;                  // по лестнице — и вверх, и вперёд
            var d = d3; d.y = 0;
            float step = WalkSpeedNow * (running ? RunMul : 1f) * dr;
            if (d3.magnitude <= step)
            {
                transform.position = tgt;
                pathIdx++;
            }
            else transform.position = pos + d3.normalized * step;
            if (d.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 720f * dr);
            if (pathIdx >= path.Count) { walking = false; running = false; return true; }
            return false;
        }

        public void Face(Vector3 p, float t = 1f)
        {
            var d = p - transform.position; d.y = 0;
            if (d.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), t);
        }

        void Animate(float dr)
        {
            bool hide = G.house.HiddenAt(Level, Cell);
            if (hide != hiddenByLevel)
            {
                hiddenByLevel = hide;
                SetVisible(!hide);
            }
            float dt = Time.deltaTime;
            float ad = Mathf.Min(dr, dt * 2.5f);
            animT += ad;

            Vector3 basePos = new Vector3(0, 0.4f, 0);
            Vector3 tPos = Vector3.zero;
            Quaternion tRot = Quaternion.identity;
            float swing = 0, frontComp = 0, hindFold = 0, headPitch = 0, headYaw = 0;
            float tailYaw = Mathf.Sin(animT * 1.7f) * 12f, tailPitch = 0;
            bool eyesClosed = sleeping;

            if (walking && running)
            {
                // галоп: длинные махи лап, корпус качается, хвост вытянут
                float g = animT * 16f;
                tPos.y = Mathf.Abs(Mathf.Sin(g)) * 0.075f;
                tRot = Quaternion.Euler(Mathf.Sin(g) * 7f, 0, 0);
                swing = Mathf.Sin(g) * 58f;
                tailPitch = 18f;
                tailYaw = Mathf.Sin(g * 0.5f) * 9f;
                headPitch = -5f + Mathf.Sin(g * 2f) * 3f;
            }
            else if (walking)
            {
                tPos.y = Mathf.Abs(Mathf.Sin(animT * 11f)) * 0.035f;
                swing = Mathf.Sin(animT * 11f) * 32f;
                tailYaw = Mathf.Sin(animT * 5.5f) * 18f;
                headPitch = Mathf.Sin(animT * 22f) * 2f;
            }
            else
            {
                switch (pose)
                {
                    case Pose.Use:
                    {
                        // у каждого занятия свои движения; бодрые и весёлые двигаются живее
                        string fid = current != null && current.furn != null ? current.furn.def.id : null;
                        float moodK = Mathf.Clamp(Mood / 40f, -1f, 1f);
                        float tempo = Mathf.Max(0.5f, 1f + moodK * 0.25f + (pers.E - 5.5f) * 0.03f - (needs[1] < 30 ? 0.25f : 0f));
                        float t = animT * tempo;
                        switch (fid)
                        {
                            case "stove":
                            case "cafecounter":
                            case "campfire":
                                headPitch = 22f + Mathf.Sin(t * 3f) * 4f;
                                headYaw = Mathf.Sin(t * 3f) * 14f;
                                tPos.x = Mathf.Sin(t * 3f) * 0.015f;
                                frontComp = -8f + Mathf.Sin(t * 6f) * 14f;
                                tailYaw = Mathf.Sin(t * 3f) * 22f;
                                break;
                            case "fridge":
                                tPos.z = 0.06f;
                                headPitch = 8f + Mathf.Sin(t * 1.5f) * 8f;
                                headYaw = Mathf.Sin(t * 0.9f) * 25f;
                                tailYaw = Mathf.Sin(t * (needs[0] < 30 ? 7f : 3f)) * 18f;
                                break;
                            case "sink":
                            case "counter":
                                headPitch = 28f;
                                tPos.y = Mathf.Abs(Mathf.Sin(t * 9f)) * 0.012f;
                                frontComp = Mathf.Sin(t * 9f) * 20f;
                                break;
                            case "easel":
                                headYaw = Mathf.Sin(t * 0.7f) * 18f;
                                headPitch = Mathf.Sin(t * 0.5f) * 6f - 4f;
                                tRot = Quaternion.Euler(0, 0, Mathf.Sin(t * 0.7f) * 4f);
                                frontComp = Mathf.Max(0, Mathf.Sin(t * 2.3f)) * 35f;
                                break;
                            case "computer":
                                headPitch = 8f;
                                tPos.y = Mathf.Abs(Mathf.Sin(t * 14f)) * 0.004f;
                                frontComp = Mathf.Sin(t * 16f) * 8f;
                                break;
                            case "piano":
                            case "guitar":
                                headYaw = Mathf.Sin(t * 2f) * 12f;
                                headPitch = 12f + Mathf.Sin(t * 4f) * 6f;
                                frontComp = Mathf.Abs(Mathf.Sin(t * 5f)) * 25f;
                                tailYaw = Mathf.Sin(t * 4f) * 30f;
                                break;
                            default:
                                headPitch = 18f + Mathf.Sin(t * 5f) * 6f;
                                tPos.y = Mathf.Abs(Mathf.Sin(t * 4f)) * 0.01f;
                                break;
                        }
                        if (moodK < -0.4f) { headPitch += 10f; tailPitch = -15f; }
                        break;
                    }
                    case Pose.Talk:
                        headPitch = Mathf.Sin(animT * 6f) * 9f - 6f;
                        headYaw = Mathf.Sin(animT * 2.1f) * 10f;
                        tailYaw = Mathf.Sin(animT * 5f) * 28f;
                        break;
                    case Pose.Sit:
                    case Pose.SitFloor:
                    case Pose.Bath:
                        tRot = Quaternion.Euler(-30, 0, 0);
                        tPos = new Vector3(0, -0.03f, -0.05f);
                        frontComp = 30f;
                        hindFold = -75f;
                        headPitch = 22f;
                        tailPitch = -20f;
                        if (pose == Pose.Bath) tPos.y -= 0.2f;
                        if (current != null && current.inter == Catalog.EatAtTable) headPitch = 25f + Mathf.Abs(Mathf.Sin(animT * 7f)) * 8f;
                        else if (current != null && current.seat != null)
                        {
                            headPitch = 8f + Mathf.Max(0, Mathf.Sin(animT * 0.4f) - 0.9f) * 60f; // смеётся над передачей
                            tailYaw = Mathf.Sin(animT * 1.2f) * 20f;
                        }
                        break;
                    case Pose.Lie:
                        tRot = Quaternion.Euler(0, 0, 78);
                        tPos = new Vector3(0.05f, -0.19f, 0);
                        frontComp = 25f;
                        hindFold = -25f;
                        tailYaw = 55f + Mathf.Sin(animT * 0.8f) * 4f;
                        headPitch = 10f;
                        break;
                    default:
                        tPos.y = Mathf.Sin(animT * 2f) * 0.004f;
                        headYaw = Mathf.Sin(animT * 0.6f) * 14f;
                        break;
                }
            }

            if (stage == LifeStage.Elder) headPitch += 8f;
            BathUndress(pose == Pose.Bath && !walking);
            if (carried) { swing = Mathf.Sin(animT * 3f) * 12f; frontComp = 15f; hindFold = -10f; tailYaw = Mathf.Sin(animT * 4f) * 25f; }
            float k = 1f - Mathf.Exp(-12f * dt);
            model.localPosition = Vector3.Lerp(model.localPosition, basePos + tPos, k);
            model.localRotation = Quaternion.Slerp(model.localRotation, tRot, k);
            legFL.localRotation = Quaternion.Euler(swing + frontComp, 0, 0);
            legBR.localRotation = Quaternion.Euler(swing + hindFold, 0, 0);
            legFR.localRotation = Quaternion.Euler(-swing + frontComp, 0, 0);
            legBL.localRotation = Quaternion.Euler(-swing + hindFold, 0, 0);
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(headPitch, headYaw, 0), k);
            tail.localRotation = Quaternion.Slerp(tail.localRotation, Quaternion.Euler(25 + tailPitch, tailYaw, 0), k);

            // моргание
            blinkTimer -= dt;
            if (blinkTimer <= 0) { blinkLeft = 0.12f; blinkTimer = Random.Range(2.5f, 5.5f); }
            if (blinkLeft > 0) blinkLeft -= dt;
            SetEyes(eyesClosed || blinkLeft > 0 ? EyeState.Closed : Emotion());

            // уши иногда дёргаются
            float tw = Mathf.Max(0, Mathf.Sin(animT * 0.9f + foxName.Length) - 0.97f) * 400f;
            earL.localRotation = Quaternion.Euler(-tw * 0.3f, 0, 15 + tw * 0.2f);
            earR.localRotation = Quaternion.Euler(0, 0, -15);
        }

        public bool CanUse(Interaction it) => stage >= it.minStage && stage <= it.maxStage;

        /// <summary>Свободный стул у стола на этом же участке.</summary>
        Furniture DiningChair()
        {
            var here = G.house.LotAt(Cell);
            if (here == null) return null;
            Furniture best = null; float bd = 16f;
            foreach (var f in G.furniture)
            {
                if (f.def.id != "chair" || f.lot != here.id || f.level != Level || !f.HasFreeSlot(this)) continue;
                var tbl = G.TableInFront(f);
                if (tbl == null) continue;
                float d = Vector3.Distance(f.transform.position, transform.position);
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        /// <summary>
        /// Помыть за собой сразу после еды? Аккуратные почти всегда моют, неряхи — редко.
        /// В плохом настроении, усталые или голодные чаще бросают тарелку.
        /// </summary>
        bool WantsToWashNow()
        {
            float p = 0.1f + (pers.C - 1) * 0.1f;           // 1 → 10%, 10 → 100%
            if (Mood < -15) p *= 0.5f;
            if (needs[(int)Need.Energy] < 25 || needs[(int)Need.Hygiene] < 15) p *= 0.6f;
            if (IsChild) p *= 0.7f;
            return Random.value < Mathf.Clamp01(p);
        }

        void WashLater(Furniture plate, bool first)
        {
            var wash = plate.def.actions[0];
            var act = new FoxAction { kind = ActKind.UseObject, furn = plate, inter = wash, label = wash.verb ?? wash.name };
            if (first) queue.Insert(0, act); else queue.Add(act);
        }

        void BeginCarry(FoxAction a)
        {
            var it = a.inter;
            var plate = a.furn;
            Hold(it.hold ?? "plate");
            if (a.slot >= 0 && a.slot < plate.occupants.Length && plate.occupants[a.slot] == this) plate.occupants[a.slot] = null;
            plate.inUse = Mathf.Max(0, plate.inUse - 1);
            Furniture target = null;
            float best = float.MaxValue;
            foreach (var f in G.furniture)
            {
                if (f.def.id != it.carryTo || !f.HasFreeSlot(this)) continue;
                float d = Vector3.Distance(f.transform.position, transform.position);
                if (d < best) { best = d; target = f; }
            }
            a.furn = target;
            a.slot = -1;
            G.RemoveFurniture(plate);
            a.stage = 3;
            a.elapsed = 0;
        }

        /// <summary>Несём предмет во рту к другому предмету (тарелку — к раковине) и доделываем действие там.</summary>
        void RunCarry(FoxAction a, float dm, float dr)
        {
            var it = a.inter;
            if (a.stage == 3)
            {
                if (a.furn != null && G.furniture.Contains(a.furn) && a.furn.FindSlot(this, G.house, out a.slot, out a.stand, out var p))
                {
                    a.furn.occupants[a.slot] = this;
                    StartPath(p);
                    a.stage = 4;
                }
                else
                {
                    a.furn = null;
                    a.stage = 5;
                    a.elapsed = 0;
                    pose = Pose.Use;
                    return;
                }
            }
            if (a.stage == 4)
            {
                if (a.furn == null || !G.furniture.Contains(a.furn)) { a.furn = null; a.stage = 5; a.elapsed = 0; return; }
                if (!FollowPath(dr)) return;
                Face(a.furn.transform.position);
                pose = Pose.Use;
                a.furn.inUse++;
                a.elapsed = 0;
                a.stage = 5;
                if (!string.IsNullOrEmpty(it.bubble)) Say(it.bubble);
                return;
            }
            a.elapsed += dm;
            pose = Pose.Use;
            needs[3] = Mathf.Min(100, needs[3] + it.fun * dm);
            if (it.skill != Skill.None) GainSkill(it.skill, it.skillRate * dm);
            if (a.elapsed >= it.duration * DurationMul(it))
            {
                ReleaseUse(a);
                Drop();
                it.onFinish?.Invoke(this);
                OnEvent(new GameEvent { kind = "act", arg = it.name });
                Finish(a);
            }
        }

        public string ActivityText
        {
            get
            {
                if (atWork && career != null) return $"На работе ({JobTitle}) до {career.endHour}:00";
                if (current == null) return "Ничего не делает";
                if (current.kind == ActKind.UseObject && current.stage < 2)
                    return "Идёт " + RussianCases.LowerFirst(current.inter.name);
                if (current.kind == ActKind.Social && current.stage < 2 && current.other != null) return "Идёт к " + current.other.NameIn(NameCase.Dative);
                string activity = current.DisplayText(true);
                if (side != null && side.other != null) return activity + " · говорит с " + side.other.NameIn(NameCase.Instrumental);
                return activity;
            }
        }
    }
}
