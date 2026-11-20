using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Пол, возраст, этапы жизни, пары, лисята, предметы во рту.</summary>
    public partial class Fox
    {
        public string id = System.Guid.NewGuid().ToString("N").Substring(0, 12);
        public bool female;
        public int home;   // участок, где живёт (0 — лисий дом игрока)
        public bool IsPlayer => home == House.PlayerLotId;
        public Vector2Int HomeEntry => Game.I.house.LotById(home) != null ? Game.I.house.LotById(home).entry : Game.I.house.EntryCell;
        public LifeStage stage = LifeStage.Adult;
        public int ageDays, stageDays;
        public string motherId, fatherId, partnerId, babyFatherId;
        public bool married;
        public readonly Dictionary<Fox, float> romance = new Dictionary<Fox, float>();
        public float pregnantMinutes = -1f;
        public int pension;
        public bool carried;

        GameObject held;
        public string heldKind;

        public string Summary => Family.Summary(pers, female);
        public string StageName => Family.StageName(stage, female);
        public bool IsChild => stage <= LifeStage.Teen;
        public Fox Partner => string.IsNullOrEmpty(partnerId) ? null : G.foxes.Find(f => f.id == partnerId);

        float speedQuirk = Random.Range(0.92f, 1.08f);

        /// <summary>
        /// Скорость шага: лисята и подростки шустрые, старички неспешные; любопытные и общительные — бегом;
        /// хорошее настроение ускоряет, усталость и одиночество замедляют; опаздывая, под дождём или когда хочется помыться — спешат.
        /// </summary>
        public float WalkSpeedNow
        {
            get
            {
                float s = stage == LifeStage.Kit ? 1.8f : stage == LifeStage.Teen ? 1.9f : stage == LifeStage.Elder ? 1.1f : 1.6f;
                s *= 1f + (pers.E - 5.5f) * 0.03f + (pers.O - 5.5f) * 0.02f;
                s *= 1f + Mathf.Clamp(Mood, -60f, 60f) / 300f;
                if (needs[1] < 35) s *= 0.75f + needs[1] / 140f;
                if (needs[4] < 25) s *= 0.9f;
                if (needs[2] < 20) s *= 1.12f;
                if (current != null && current.kind == ActKind.Work && career != null && G.Hour > career.startHour - 0.3f) s *= 1.4f;
                if (G.weather != null && G.weather.Raining && Level == 0 && G.house.RoomAt(0, Cell) == null) s *= 1.3f;
                return s * speedQuirk;
            }
        }

        /// <summary>С возрастом мех светлеет и теряет яркость.</summary>
        List<StyleData> youngFur;

        /// <summary>Седина ушла (лисичку вернули из пожилых): прежние цвета, а если их не помним — обратный пересчёт.</summary>
        public void UnfadeFur()
        {
            for (int p = 0; p < 3 && p < style.parts.Count; p++)
            {
                if (youngFur != null && p < youngFur.Count) { style.SetStyle(p, youngFur[p]); continue; }
                var sd = style.Get(p).Clone();
                var c = (sd.c1 - new Color(0.8f, 0.8f, 0.8f) * 0.15f) / 0.85f;
                Color.RGBToHSV(c, out float h, out float sat, out float v);
                sd.c1 = Color.HSVToRGB(h, Mathf.Clamp01(sat / 0.55f), Mathf.Clamp01((v - 0.2125f) / 0.75f));
                style.SetStyle(p, sd);
            }
            youngFur = null;
        }

        public void FadeFur()
        {
            youngFur = new List<StyleData>();
            for (int p = 0; p < 3 && p < style.parts.Count; p++) youngFur.Add(style.Get(p).Clone());
            for (int p = 0; p < 3 && p < style.parts.Count; p++)
            {
                var sd = style.Get(p).Clone();
                Color.RGBToHSV(sd.c1, out float h, out float sat, out float v);
                sd.c1 = Color.Lerp(Color.HSVToRGB(h, sat * 0.55f, Mathf.Lerp(v, 0.85f, 0.25f)), new Color(0.8f, 0.8f, 0.8f), 0.15f);
                style.SetStyle(p, sd);
            }
        }

        // ---------- этапы жизни ----------

        public void SetStage(LifeStage s, bool announce)
        {
            var old = stage;
            stage = s;
            stageDays = 0;
            ApplyShape();
            if (s <= LifeStage.Teen)
            {
                if (career == null || career.id != "school")
                {
                    career = Careers.School;
                    careerLevel = 0;
                    performance = 0;
                    lastWorkDay = G != null && G.Hour >= career.startHour - 0.75f ? G.Day : -1;
                }
            }
            else if (career != null && career.id == "school")
            {
                bool honors = performance >= 50f;
                career = null;
                performance = 0;
                if (announce)
                {
                    AddMoodlet(honors ? "Школа окончена с отличием" : "Школа окончена", honors ? 10 : 5, 720);
                    Remember(honors ? "Окончание школы с отличием" : "Окончание школы");
                }
            }
            if (s == LifeStage.Elder && old != LifeStage.Elder) FadeFur();
            if (s != LifeStage.Elder && old == LifeStage.Elder) UnfadeFur();
            if (s == LifeStage.Elder && career != null && career.id != "school")
            {
                pension = Mathf.RoundToInt(Wage * career.Hours * 2f);
                var c = career;
                QuitJob(false);
                Remember($"Выход на пенсию ({c.name})");
                G.LogFox(this, $"{foxName}: выход на пенсию, {pension} мон. в неделю");
            }
            if (announce && old != s)
            {
                AddMoodlet("День рождения", 10, 720);
                Say(female ? "Я стала старше!" : "Я стал старше!");
                G.LogFox(this, $"{foxName}: теперь {StageName}!");
                Remember($"Начался новый этап жизни — {StageName.ToLower()}");
            }
        }

        /// <summary>Вызывается в полночь. Возвращает true, если пришло время уйти в звёздный лес.</summary>
        float agingDebt;

        public bool AgeOneDay(Lifespan l)
        {
            ageDays++;
            if (l == Lifespan.Endless) return false;
            stageDays++;
            // при плохом здоровье стареют быстрее: «долг» старения копится и иногда добавляет день
            agingDebt += AgingMul - 1f;
            if (agingDebt >= 1f) { agingDebt -= 1f; stageDays++; }
            int len = Family.StageDays(stage, l);
            if (stage < LifeStage.Elder)
            {
                if (stageDays >= len) SetStage(stage + 1, true);
                return false;
            }
            return stageDays >= len && Random.value < 0.35f;
        }

        public int DaysToNextStage(Lifespan l) => l == Lifespan.Endless ? -1 : Mathf.Max(0, Family.StageDays(stage, l) - stageDays);

        // ---------- романтика и лисята ----------

        void UpdateFamily(float dm)
        {
            if (pregnantMinutes >= 0)
            {
                pregnantMinutes += dm;
                AddMoodlet("Скоро будут лисята", 6, 5);
                if (pregnantMinutes >= 2880f)
                {
                    pregnantMinutes = -1f;
                    G.BirthKits(this);
                    ApplyShape();
                }
            }
        }

        public bool Pregnant => pregnantMinutes >= 0;

        // ---------- предметы во рту ----------

        public Vector3 MouthPos => head != null ? head.TransformPoint(new Vector3(0, -0.07f, 0.34f)) : transform.position;

        public void Hold(string kind)
        {
            Drop();
            if (string.IsNullOrEmpty(kind) || head == null) return;
            var st = new GameObject("held").AddComponent<Styleable>();
            st.kind = SurfaceKind.Fox;
            st.displayName = kind;
            st.transform.SetParent(head, false);
            st.transform.localPosition = new Vector3(0, -0.08f, 0.36f);
            var t = st.transform;
            switch (kind)
            {
                case "plate":
                    st.AddPart("Тарелка", StyleData.Make("Гладкий", "#F4F1EA", null, 1, 0.6f));
                    Shapes.Cyl(st, 0, t, new Vector3(0, -0.04f, 0.05f), 0.26f, 0.015f, new Vector3(80, 0, 0));
                    break;
                case "book":
                    st.AddPart("Книга", StyleData.Make("Ткань", Random.value < 0.5f ? "#B33A3A" : "#2E5E8C"));
                    st.AddPart("Страницы", StyleData.Make("Гладкий", "#F5EFE0"));
                    Shapes.Box(st, 0, t, new Vector3(0, -0.06f, 0.03f), new Vector3(0.2f, 0.15f, 0.04f));
                    Shapes.Box(st, 1, t, new Vector3(0, -0.06f, 0.03f), new Vector3(0.19f, 0.14f, 0.045f));
                    break;
                case "food":
                    st.AddPart("Еда", StyleData.Make("Ворс", "#C98B5B"));
                    st.AddPart("Зелень", StyleData.Make("Гладкий", "#6FA34A"));
                    Shapes.Ball(st, 0, t, new Vector3(0, -0.02f, 0.03f), new Vector3(0.14f, 0.07f, 0.1f));
                    Shapes.Ball(st, 1, t, new Vector3(0.02f, 0.01f, 0.03f), new Vector3(0.06f, 0.03f, 0.05f));
                    break;
                case "ball":
                    st.AddPart("Мяч", StyleData.Make("Звёздочки", "#E5484D", "#F5E6C8"));
                    Shapes.Ball(st, 0, t, new Vector3(0, -0.04f, 0.06f), new Vector3(0.2f, 0.2f, 0.2f));
                    break;
                default:
                    Destroy(st.gameObject);
                    return;
            }
            foreach (var c in st.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in st.GetComponentsInChildren<StyleRef>()) Destroy(r);
            if (atWork) foreach (var r in st.GetComponentsInChildren<Renderer>()) r.enabled = false;
            held = st.gameObject;
            heldKind = kind;
        }

        public void Drop()
        {
            if (held != null) Destroy(held);
            held = null;
            heldKind = null;
        }

        // ---------- лисёнка носят в зубах ----------

        /// <summary>Держать лисёнка за шкирку у рта несущего.</summary>
        void FollowCarrier(Fox carrier)
        {
            carried = true;
            transform.rotation = carrier.transform.rotation * Quaternion.Euler(0, 90, 0);
            float s = transform.localScale.y;
            var scruff = transform.rotation * new Vector3(0, 0.66f, 0.12f) * s;
            transform.position = carrier.MouthPos - scruff;
        }

        void PutDown()
        {
            if (!carried) return;
            carried = false;
            var p = transform.position;
            int lvl = G.house.LevelOf(p - Vector3.up * 0.5f);
            var g = G.house.grids[lvl];
            var c = g.ClampCell(GridMap.CellOf(p));
            if (!g.IsFree(c)) p = GridMap.Center(c);
            transform.position = new Vector3(p.x, g.HeightAt(GridMap.CellOf(p)), p.z);
            transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        }

        /// <summary>Несущий бродит по дому, пока длится «Поносить лисёнка».</summary>
        void CarryWalk(float dr)
        {
            if (path == null || FollowPath(dr))
            {
                for (int i = 0; i < 8; i++)
                {
                    var c = Cell + new Vector2Int(Random.Range(-3, 4), Random.Range(-3, 4));
                    if (!Grid.IsFree(c)) continue;
                    var p = Grid.FindPath(Cell, c);
                    if (p != null && p.Count > 0) { StartPath(p); break; }
                }
            }
        }
    }
}
