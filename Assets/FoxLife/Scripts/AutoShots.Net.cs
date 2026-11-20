using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Автотест игры по сети на одном компьютере: «-foxnethost папка» открывает город, «-foxnetjoin папка»
    /// заходит к нему (127.0.0.1), выбирает семью, отдаёт приказы и строит. Оба пишут журнал и снимки.
    /// </summary>
    public partial class AutoShots
    {
        bool netHost, netJoin;
        const int TestPort = 7790;

        string FoxLine(Fox f) => $"{f.foxName} [{f.home}] {f.transform.position.x:0.0},{f.transform.position.z:0.0} «{f.ActivityText}» нужды {f.needs[0]:0}/{f.needs[1]:0}";

        int FurnOn(int lot) { int n = 0; foreach (var f in Game.I.furniture) if (f != null && f.lot == lot) n++; return n; }

        IEnumerator NetHostTest()
        {
            var g = Game.I;
            while (g.loading) yield return null;
            g.NewGame();
            yield return new WaitForSeconds(0.3f);
            while (g.loading) yield return null;
            g.PlayFamily(0);
            yield return new WaitForSeconds(1f);
            var net = Net.Ensure();
            net.myName = "Хост";
            bool ok = net.StartHost(TestPort, out var err);
            log.WriteLine($"СЕТЬ хост: открыта {ok} {err}, лисичек {g.foxes.Count}, мебели {g.furniture.Count}");
            float t0 = Time.realtimeSinceStartup;
            NetLink peer = null;
            while (Time.realtimeSinceStartup - t0 < 150f)
            {
                peer = net.peers.Find(p => p.lot >= 0);
                if (peer != null) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (peer == null) { log.WriteLine("СЕТЬ хост: никто не пришёл"); Application.Quit(); yield break; }
            int lot = peer.lot;
            log.WriteLine($"СЕТЬ хост: {peer.name} играет за участок {lot}, мебели там {FurnOn(lot)}, денег {g.MoneyOf(lot):0}");
            yield return new WaitForSecondsRealtime(6f);
            // хост ставит у себя предмет — друг должен его увидеть
            var def = Catalog.Get("plant");
            var home = g.house.LotById(House.PlayerLotId);
            bool placed = false;
            for (int x = home.rect.xMin + 1; x < home.rect.xMax - 1 && !placed; x++)
                for (int z = home.rect.yMin + 1; z < home.rect.yMax - 1 && !placed; z++)
                    if (g.CanPlace(def, new Vector2Int(x, z), 0, null)) { g.SpawnFurniture(def, new Vector2Int(x, z), 0, false); placed = true; log.WriteLine($"СЕТЬ хост: поставил цветок в {x},{z}, мебели у себя {FurnOn(House.PlayerLotId)}"); }
            g.SetSpeed(3);
            int lastFurn = FurnOn(lot);
            int shot = 0;
            bool saidHi = false;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 120f && net.peers.Count > 0)
            {
                foreach (var f in g.FamilyOf(lot)) log.WriteLine($"СЕТЬ хост {Time.realtimeSinceStartup:0}: друг: {FoxLine(f)}");
                log.WriteLine($"СЕТЬ хост: скорость {g.speedIndex} (моя {g.wantSpeed}, у друга {peer.vote}), денег у друга {g.MoneyOf(lot):0}, мебели у друга {FurnOn(lot)}, свобода воли друга {peer.autonomy}, стен у друга {g.NetLotData(lot).edges.Count}");
                if (shot >= 1 && net.chat.Count > 0 && !saidHi) { saidHi = true; net.SendChat("Привет, заходи на чай!"); }
                if (FurnOn(lot) != lastFurn) { log.WriteLine($"СЕТЬ хост: у друга теперь мебели {FurnOn(lot)} (было {lastFurn})"); lastFurn = FurnOn(lot); }
                if (shot < 2 && Time.realtimeSinceStartup - t0 > 20f + shot * 25f)
                {
                    var ff = g.FamilyOf(lot);
                    if (ff.Count > 0) { g.cam.target = ff[0].transform.position; g.cam.dist = 14f; g.cam.pitch = 45f; }
                    yield return new WaitForSeconds(1f);
                    yield return Shot("net_host_" + shot++);
                }
                yield return new WaitForSecondsRealtime(3f);
            }
            log.WriteLine($"СЕТЬ хост: друг ушёл, игроков {net.peers.Count}");
            log.WriteLine($"СЕТЬ хост: семья друга: {Game.FamilyTitle(g.FamilyOf(lot))}");
            var renamed = g.FamilyOf(lot).Find(f => f.foxName == "Тестик");
            if (renamed?.NameIn(NameCase.Genitive) != "Тестика")
                Debug.LogError("СЕТЬ хост: ручная форма имени не пришла из «Лисичек».");
            else log.WriteLine("СЕТЬ хост: ручная форма имени — Тестика.");
            foreach (var f in g.FamilyOf(lot)) log.WriteLine($"СЕТЬ хост: {f.foxName}: характер {string.Join(",", f.pers.v)}, рост {f.size:0.00}, навык0 {f.skillLvl[0]}");
            log.WriteLine($"СЕТЬ хост: чат {net.chat.Count}: {string.Join(" / ", net.chat.ConvertAll(c => c.who + ": " + c.text))}");
            foreach (var e in g.log) log.WriteLine("ЖУРНАЛ " + e.time + " " + e.text);
            Application.Quit();
        }

        IEnumerator NetJoinTest()
        {
            var g = Game.I;
            while (g.loading) yield return null;
            yield return new WaitForSecondsRealtime(4f);
            var net = Net.Ensure();
            // ищем игру в сети, как это делает окно «Играть вместе»
            float t0 = Time.realtimeSinceStartup;
            LanGame lan = null;
            while (lan == null && Time.realtimeSinceStartup - t0 < 40f)
            {
                net.searchUntil = Time.unscaledTime + 1f;
                lan = net.found.Find(x => x.port == TestPort);
                yield return null;
            }
            log.WriteLine(lan != null ? $"СЕТЬ гость: найдена игра «{lan.town}» у {lan.host} ({lan.address}:{lan.port}), найдено всего {net.found.Count}" : "СЕТЬ гость: поиск игр НЕ нашёл хоста");
            net.searchUntil = 0;
            string addr = lan != null ? lan.address : "127.0.0.1";
            t0 = Time.realtimeSinceStartup;
            while (!net.worldReady && Time.realtimeSinceStartup - t0 < 120f)
            {
                if (net.role == Net.Role.None) { yield return new WaitForSecondsRealtime(2f); net.StartClient(addr, TestPort, "Гость"); }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (!net.worldReady) { log.WriteLine("СЕТЬ гость: город не пришёл: " + net.status); Application.Quit(); yield break; }
            log.WriteLine($"СЕТЬ гость: город загружен ({net.worldBytes / 1024:0} КБ), лисичек {g.foxes.Count}, мебели {g.furniture.Count}");
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("net_join_0_picker");
            var taken = net.TakenLots();
            var pick = g.house.lots.Find(l => !l.removed && l.kind == LotKind.Home && g.FamilyOf(l.id).Count > 0 && !taken.Contains(l.id));
            if (pick == null) { log.WriteLine("СЕТЬ гость: нет свободной семьи"); net.Stop(null); Application.Quit(); yield break; }
            net.Claim(pick.id, false);
            t0 = Time.realtimeSinceStartup;
            while (g.selected == null && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            log.WriteLine($"СЕТЬ гость: играем за {pick.name}, семья {Game.FamilyTitle(g.Household)}, денег {g.money:0}, выбрана {(g.selected != null ? g.selected.foxName : "-")}");
            if (g.selected == null) { net.Stop(null); Application.Quit(); yield break; }
            var me = g.selected;
            g.SetSpeed(2);
            yield return new WaitForSecondsRealtime(2f);
            // приказ: поесть из холодильника (если есть), иначе — пройтись
            var fridge = g.furniture.Find(f => f != null && f.lot == pick.id && f.def.id == "fridge");
            if (fridge != null) me.Command(new FoxAction { kind = ActKind.UseObject, furn = fridge, inter = fridge.def.actions[0], label = fridge.def.actions[0].verb ?? fridge.def.actions[0].name });
            else me.Command(new FoxAction { kind = ActKind.Move, moveTarget = GridMap.Center(pick.entry), label = "Идёт сюда" });
            log.WriteLine($"СЕТЬ гость: приказ {(fridge != null ? "холодильник" : "идти")} → {FoxLine(me)}");
            string last = null;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 15f)
            {
                if (me.ActivityText != last) { last = me.ActivityText; log.WriteLine($"СЕТЬ гость {Time.realtimeSinceStartup:0}: {FoxLine(me)}, очередь {me.queue.Count}"); }
                yield return new WaitForSecondsRealtime(0.2f);
            }
            g.cam.target = me.transform.position; g.cam.dist = 14f; g.cam.pitch = 45f;
            yield return new WaitForSeconds(1f);
            yield return Shot("net_join_1_fox");
            // шкаф: у гостя открывается выбор одежды; надеваем кепку, закрываем — лисичка отходит
            var wd = g.furniture.Find(f => f != null && f.lot == pick.id && f.def.id == "wardrobe");
            if (wd != null)
            {
                var wi = wd.def.actions.Find(x => x.openWardrobe);
                me.Command(new FoxAction { kind = ActKind.UseObject, furn = wd, inter = wi, label = wi.verb ?? wi.name });
                t0 = Time.realtimeSinceStartup;
                while (!g.ui.clothesOpen && Time.realtimeSinceStartup - t0 < 30f) yield return null;
                log.WriteLine($"СЕТЬ гость: шкаф — окно одежды {(g.ui.clothesOpen ? "открылось" : "НЕ открылось")}, {FoxLine(me)}");
                if (g.ui.clothesOpen)
                {
                    me.SetItem(0, ClothSlot.Head, "cap");
                    yield return new WaitForSecondsRealtime(1.5f);
                    g.cam.target = me.transform.position; g.cam.dist = 6f;
                    yield return Shot("net_join_1b_wardrobe");
                    g.ui.clothesOpen = false;
                }
                yield return new WaitForSecondsRealtime(3f);
                log.WriteLine($"СЕТЬ гость: после шкафа {FoxLine(me)}, наряд 0: {me.outfits[0].items.Count} вещей");
            }
            else log.WriteLine("СЕТЬ гость: шкафа нет");
            // работа и отмена действия
            var noJob = g.Household.Find(f => f.career == null && f.stage >= LifeStage.Adult);
            if (noJob != null)
            {
                noJob.TakeJob(Careers.All[0]);
                yield return new WaitForSecondsRealtime(3f);
                log.WriteLine($"СЕТЬ гость: {noJob.foxName} работа: {(noJob.career != null ? noJob.career.name : "нет")}");
            }
            if (me.current != null)
            {
                string was = me.current.label;
                me.Cancel(me.current);
                yield return new WaitForSecondsRealtime(1.5f);
                log.WriteLine($"СЕТЬ гость: отмена «{was}» → «{me.ActivityText}»");
            }
            // стройка: открываем план, ставим предмет и стену
            g.SetMode(Mode.Build);
            float money0 = g.money;
            var def = Catalog.Get("plant");
            bool placed = false;
            for (int x = pick.rect.xMin + 1; x < pick.rect.xMax - 1 && !placed; x++)
                for (int z = pick.rect.yMin + 1; z < pick.rect.yMax - 1 && !placed; z++)
                    if (g.CanPlace(def, new Vector2Int(x, z), 0, null))
                    {
                        g.SpawnFurniture(def, new Vector2Int(x, z), 0, false);
                        g.AddMoney(-def.price, "Тест: цветок");
                        placed = true;
                        log.WriteLine($"СЕТЬ гость: поставил цветок в {x},{z}, мебели у себя {FurnOn(pick.id)}, денег {money0:0}→{g.money:0}");
                    }
            var e0 = pick.rect.min + new Vector2Int(2, pick.rect.height - 2);
            g.house.SetEdge(0, e0.x, e0.y, false, EdgeKind.Fence);
            g.house.Rebuild(pick);
            log.WriteLine($"СЕТЬ гость: забор в {e0}");
            yield return new WaitForSecondsRealtime(2f);
            g.SetMode(Mode.Live);
            yield return new WaitForSecondsRealtime(3f);
            int hostFurn = FurnOn(net.TakenLots().Count > 0 ? net.TakenLots()[0] : 0);
            log.WriteLine($"СЕТЬ гость: мебели у хоста (вижу) {hostFurn}, время {g.ClockText}, скорость {g.speedIndex}");
            // разговор с соседом
            var other = g.foxes.Find(o => o != null && o.home != g.selected.home && o.stage >= LifeStage.Teen && !o.sleeping && !o.atWork && Vector3.Distance(o.transform.position, me.transform.position) < 40f);
            if (other != null)
            {
                var chat = Social.All.Find(d => d.name == "Поболтать");
                me.Command(FoxAction.ForSocial(chat, other));
                log.WriteLine("СЕТЬ гость: " + chat.CommandText(other));
            }
            t0 = Time.realtimeSinceStartup;
            last = null;
            while (Time.realtimeSinceStartup - t0 < 25f)
            {
                if (me.ActivityText != last) { last = me.ActivityText; log.WriteLine($"СЕТЬ гость {Time.realtimeSinceStartup:0}: {FoxLine(me)}"); }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            g.cam.target = me.transform.position;
            yield return new WaitForSeconds(1f);
            yield return Shot("net_join_2_social");
            // чат и «Лисички»: переименовать свою лисичку, поменять характер, добавить лисичку
            net.SendChat("Привет из гостей!");
            g.SetMode(Mode.Create);
            yield return new WaitForSecondsRealtime(1f);
            int fam0 = g.Household.Count;
            me.Rename("Тестик"); me.pers.v[0] = 10; me.size = 1.1f;
            me.skillLvl[0] = 3;
            yield return new WaitForSecondsRealtime(1f);
            if (fam0 < Game.MaxFoxes) net.CasAddFox(me);
            yield return new WaitForSecondsRealtime(3f);
            log.WriteLine($"СЕТЬ гость: в «Лисичках» скорость {g.speedIndex}, семья {fam0}→{g.Household.Count}: {Game.FamilyTitle(g.Household)}");
            g.cam.Focus(me.transform.position, 6f);
            yield return Shot("net_join_2b_create");
            me.nameOverrides = new[] { "Тестика", null, null, null, null };
            g.SetMode(Mode.Live);
            yield return new WaitForSecondsRealtime(5f);
            log.WriteLine($"СЕТЬ гость: после «Лисичек» {me.foxName}, общительность {me.pers.v[0]}, рост {me.size:0.00}, скорость {g.speedIndex}");
            g.ui.chatOpen = true;
            yield return Shot("net_join_2c_chat");
            g.ui.chatOpen = false;
            log.WriteLine($"СЕТЬ гость: чат {net.chat.Count}: {string.Join(" / ", net.chat.ConvertAll(c => c.who + ": " + c.text))}");
            g.cam.Focus(new Vector3(20f, 0, -5f), 999f); g.cam.dist = 70f; g.cam.pitch = 55f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("net_join_3_town");
            foreach (var f in g.Household) log.WriteLine("СЕТЬ гость итог: " + FoxLine(f));
            foreach (var e in g.log) log.WriteLine("ЖУРНАЛ " + e.time + " " + e.text);
            net.Stop(null);
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
