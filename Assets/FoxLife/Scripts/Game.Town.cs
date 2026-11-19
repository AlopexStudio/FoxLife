using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Режим города (как в Sims): смотрим на весь городок, выбираем семью, за которую играть,
    /// редактируем, переселяем, выселяем в архив и создаём семьи. А ещё — экран загрузки.
    /// </summary>
    public partial class Game
    {
        // ================= загрузка =================

        public bool loading = true;
        public string loadingText = "Загрузка…";
        public float loadingProgress;

        /// <summary>Сделать тяжёлую работу, сначала показав экран загрузки.</summary>
        public void RunWithLoading(string text, System.Action work, System.Action after = null)
        {
            StartCoroutine(LoadingRoutine(text, work, after));
        }

        IEnumerator LoadingRoutine(string text, System.Action work, System.Action after)
        {
            loading = true;
            loadingText = text;
            loadingProgress = 0.3f;
            yield return null;
            yield return null;
            work();
            loadingProgress = 1f;
            yield return null;
            loading = false;
            after?.Invoke();
        }

        IEnumerator BootWorld()
        {
            Town.ResetLots();
            loading = true;
            loadingProgress = 0.03f;
            loadingText = "Рисуем долину, горы и лес…";
            yield return null; yield return null;
            yield return ResetWorldAsync(true);
            Log("Добро пожаловать в Лисью Долину!");
            SetupTitleScene();
            loading = false;
        }

        /// <summary>Пересоздать мир по частям (между частями крутится экран загрузки).</summary>
        public IEnumerator ResetWorldAsync(bool defaults)
        {
            ClearWorld();
            House.PlayerLotId = 0;
            loadingProgress = 0.05f;
            loadingText = "Рисуем долину, горы и лес…";
            house = new GameObject("House").AddComponent<House>();
            var hb = house.BuildAsync();
            while (hb.MoveNext()) yield return null;
            loadingProgress = 0.65f;
            loadingText = defaults ? "Заселяем городок…" : "Раскладываем вещи по местам…";
            yield return null;
            lanternsLit = false;
            minutes = 8 * 60;
            money = StartMoney;
            lastBillDay = 0;
            lastAgeDay = 1;
            archive.Clear();
            log.Clear();
            if (defaults)
            {
                BuildDefaultFurniture();
                CreateDefaultFoxes();
                yield return null;
                var pp = Town.PopulateAsync(this);
                while (pp.MoveNext()) yield return null;
                loadingText = "Зажигаем фонари…";
                var fm = house.terrain.FlushMeshAsync();
                while (fm.MoveNext()) yield return null;
                Select(foxes[0]);
            }
            loadingProgress = 0.95f;
        }

        public void NewGame()
        {
            ui.screen = GameUI.MenuScreen.None;
            StartCoroutine(NewGameRoutine());
        }

        IEnumerator NewGameRoutine()
        {
            Town.ResetLots();
            townName = "Лисья Долина";
            removedProps.Clear();
            loading = true;
            loadingText = "Создаём новый городок…";
            loadingProgress = 0.02f;
            yield return null; yield return null;
            yield return ResetWorldAsync(true);
            loading = false;
            started = false;
            Log("Добро пожаловать в Лисью Долину!");
            EnterTown();
        }

        public void ContinueGame()
        {
            ui.screen = GameUI.MenuScreen.None;
            StartCoroutine(LoadRoutine());
        }

        IEnumerator LoadRoutine()
        {
            loading = true;
            loadingText = "Загружаем сохранение…";
            loadingProgress = 0.02f;
            yield return null; yield return null;
            bool ok = false;
            yield return SaveSystem.LoadAsync(this, r => ok = r);
            loading = false;
            if (ok) StartPlaying();
            else { ui.screen = GameUI.MenuScreen.Title; ui.Note("Не получилось загрузить сохранение."); }
        }

        // ================= семьи =================

        /// <summary>Лисички без дома (выселены в архив). Хранятся выключенными, пока их не заселят.</summary>
        public readonly List<Fox> stored = new List<Fox>();
        /// <summary>Деньги каждой семьи (по участку): у кого играем — в <see cref="money"/>.</summary>
        public readonly Dictionary<int, float> lotMoney = new Dictionary<int, float>();
        public const float FamilyStartMoney = 2000f;
        public const int EmptyLotMoney = 5000, BuiltLotMoney = 2000;

        /// <summary>На участке ничего нет: ни комнат, ни мебели.</summary>
        public bool LotIsEmpty(int lot) => !house.rooms.Exists(r => r.lot == lot) && !furniture.Exists(f => f != null && !f.isGhost && f.lot == lot);

        /// <summary>Сколько денег получит новая семья на этом участке: задано для участка или 5000 на пустой, 2000 — если там что-то есть.</summary>
        public int NewFamilyMoney(int lot)
        {
            if (lot >= 0 && lot < Town.Lots.Length && Town.Lots[lot].customMoney) return Mathf.Max(0, Town.Lots[lot].startMoney);
            return LotIsEmpty(lot) ? EmptyLotMoney : BuiltLotMoney;
        }

        /// <summary>Задать деньги новой семьи для участка (null — считать само).</summary>
        public void SetLotMoney(int lot, int? v)
        {
            if (lot < 0 || lot >= Town.Lots.Length) return;
            Town.Lots[lot].customMoney = v.HasValue;
            Town.Lots[lot].startMoney = v.HasValue ? Mathf.Clamp(v.Value, 0, 10000000) : 0;
        }

        /// <summary>
        /// «Подъёмные» при заселении: на пустой участок — хватит на небольшой дом с мебелью,
        /// в пустой дом без обстановки — на мебель. В обжитой дом — ничего.
        /// </summary>
        public int MoveInGrant(int lot)
        {
            bool hasHouse = house.rooms.Exists(r => r.lot == lot);
            if (!hasHouse) return 12000;
            return LotFurnitureValue(lot) < 3000 ? 4000 : 0;
        }

        void GiveMoveInGrant(int lot, string who)
        {
            int g = MoveInGrant(lot);
            if (g <= 0) return;
            if (lot == House.PlayerLotId) AddMoney(g, "Подъёмные на обустройство");
            else lotMoney[lot] = MoneyOf(lot) + g;
            Log($"{who}: подъёмные на обустройство +{g} мон.");
        }

        public List<Fox> FamilyOf(int lot) => foxes.FindAll(f => f.home == lot);

        public List<string> StoredGroups()
        {
            var l = new List<string>();
            foreach (var f in stored) if (!l.Contains(f.storeGroup)) l.Add(f.storeGroup);
            return l;
        }

        public List<Fox> StoredFamily(string group) => stored.FindAll(f => f.storeGroup == group);

        public static string FamilyTitle(List<Fox> fam)
        {
            if (fam == null || fam.Count == 0) return "никого";
            var names = new List<string>();
            foreach (var f in fam) names.Add(f.foxName);
            return string.Join(", ", names);
        }

        public float MoneyOf(int lot) => lot == House.PlayerLotId ? money : lotMoney.TryGetValue(lot, out var m) ? m : FamilyStartMoney;

        public void EnterTown()
        {
            if (Net.Active) { Log("Пока играем по сети, город не редактируется: сначала закрой игру для друзей."); ui.screen = GameUI.MenuScreen.None; return; }
            CancelPlacing();
            SetWallTool(WallTool.None);
            SetLandTool(LandTool.None);
            ClearHover();
            mode = Mode.Town;
            SetSpeed(0);
            ui.screen = GameUI.MenuScreen.None;
            ui.townLot = -1;
            ui.townEditing = false;
            cam.Focus(new Vector3(20f, 0, -5f), 999f);
            cam.dist = 95f; cam.pitch = 58f;
        }

        /// <summary>Играть за семью с этого участка.</summary>
        public void PlayFamily(int lot)
        {
            if (FamilyOf(lot).Count == 0) return;
            if (lot != House.PlayerLotId)
            {
                lotMoney[House.PlayerLotId] = money;
                money = lotMoney.TryGetValue(lot, out var m) ? m : FamilyStartMoney;
                House.PlayerLotId = lot;
            }
            var l = house.LotById(lot);
            mode = Mode.Live;
            Select(Household.Count > 0 ? Household[0] : null);
            house.focusLot = -2;
            cam.Focus(GridMap.Center(l.entry), 18f);
            cam.dist = 18f; cam.pitch = 48f;
            if (!started) StartPlaying(); else SetSpeed(1);
            Log($"Играем за семью: {FamilyTitle(Household)} ({l.name}).");
        }

        static void Teleport(Fox f, House h, House.Lot lot, int i)
        {
            var c = lot.entry + new Vector2Int(i % 3 - 1, i / 3);
            if (!h.grid.IsFree(c)) c = lot.entry;
            f.transform.position = GridMap.Center(c) + Vector3.up * h.FloorY(0, c);
        }

        /// <summary>Переселить семью на свободный участок (без денег — это режим города).</summary>
        public bool MoveFamily(int from, int to)
        {
            var lot = house.LotById(to);
            if (lot == null || from == to || FamilyOf(to).Count > 0) return false;
            int i = 0;
            foreach (var f in FamilyOf(from))
            {
                f.home = to;
                f.CancelCurrentPublic();
                f.queue.Clear();
                Teleport(f, house, lot, i++);
                f.Remember("Переезд: " + lot.name);
            }
            if (lotMoney.TryGetValue(from, out var m)) { lotMoney.Remove(from); lotMoney[to] = m; }
            if (House.PlayerLotId == from) House.PlayerLotId = to;
            GiveMoveInGrant(to, FamilyTitle(FamilyOf(to)));
            house.focusLot = -2;
            return true;
        }

        /// <summary>Выселить семью в архив: дом освобождается, лисички ждут нового дома.</summary>
        public void EvictFamily(int lot)
        {
            var fam = FamilyOf(lot);
            if (fam.Count == 0) return;
            string group = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            foreach (var f in fam)
            {
                f.CancelCurrentPublic();
                f.queue.Clear();
                if (selected == f) Select(null);
                foreach (var o in foxes) if (o != f) o.CancelFor(f);
                f.home = -1;
                f.storeGroup = group;
                foxes.Remove(f);
                stored.Add(f);
                f.gameObject.SetActive(false);
            }
            if (lotMoney.TryGetValue(lot, out var m)) { lotMoney.Remove(lot); storedMoney[group] = m; }
            else if (lot == House.PlayerLotId) storedMoney[group] = money;
            Log($"Семья выселена в архив: {FamilyTitle(fam)}.");
        }

        public readonly Dictionary<string, float> storedMoney = new Dictionary<string, float>();

        /// <summary>Заселить семью из архива на свободный участок.</summary>
        public void SettleStored(string group, int lot)
        {
            var l = house.LotById(lot);
            if (l == null || FamilyOf(lot).Count > 0) return;
            int i = 0;
            foreach (var f in StoredFamily(group))
            {
                stored.Remove(f);
                f.home = lot;
                f.storeGroup = null;
                f.gameObject.SetActive(true);
                foxes.Add(f);
                Teleport(f, house, l, i++);
            }
            if (storedMoney.TryGetValue(group, out var m)) { storedMoney.Remove(group); if (lot == House.PlayerLotId) money = m; else lotMoney[lot] = m; }
            Log($"Семья заселилась: {l.name}.");
            GiveMoveInGrant(lot, FamilyTitle(FamilyOf(lot)));
        }

        /// <summary>Новая семья на свободном участке — одна случайная лисичка, остальное настраивается.</summary>
        public Fox CreateFamily(int lot)
        {
            var l = house.LotById(lot);
            if (l == null || FamilyOf(lot).Count > 0) return null;
            var f = AddRandomFox(lot);
            if (f != null) lotMoney[lot] = NewFamilyMoney(lot);
            return f;
        }

        void TownClick()
        {
            if (!Input.GetMouseButtonDown(0) || ui.MouseOverUI || ui.townEditing) return;
            var ray = camera3d.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 500f)) return;
            var lot = house.LotAt(GridMap.CellOf(hit.point));
            ui.townLot = lot != null ? lot.id : -1;
        }
    }
}
