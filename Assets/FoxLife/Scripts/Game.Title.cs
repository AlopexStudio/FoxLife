using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Главное меню — живая сцена: ночью у костра на лесной поляне или на закате у ручья.
    /// Там сидят лисички из семьи, за которую играли в последний раз. Нет сохранения — место просто пустое.
    /// </summary>
    public partial class Game
    {
        class TitleActor { public Fox fox; public Vector3 pos; public float yaw; public Pose rest; public float talkT, nextT; }

        readonly List<TitleActor> titleActors = new List<TitleActor>();
        bool titleScene;
        Vector3 titleCenter;
        float titleYaw, titleT, titleDist, titlePitch;
        public string titlePlace;

        public bool TitleSceneActive => titleScene;

        /// <summary>В меню горят только огни рядом со сценой (каждый фонарь — лишний проход по земле, меню тормозило).</summary>
        bool NearTitle(Vector3 p) => !titleScene || (p - titleCenter).sqrMagnitude < 26f * 26f;

        /// <param name="force">-1 — как выпадет, 0 — костёр, 1 — ручей (для автопроверки).</param>
        public void SetupTitleScene(int force = -1)
        {
            if (house == null || house.terrain == null) return;
            foreach (var a in titleActors) if (a.fox != null) { foxes.Remove(a.fox); Destroy(a.fox.gameObject); }
            // лисички по умолчанию из фонового города — прячем: в меню только своя семья
            foreach (var f in foxes) if (f != null) f.gameObject.SetActive(false);
            titleActors.Clear();
            weather.Set(WeatherKind.Clear, 1f); weather.Snap();

            var fire = furniture.Find(x => x != null && x.def.id == "campfire" && house.LotById(x.lot) is House.Lot l && l.kind == LotKind.Camp);
            bool camp = fire != null && (force == 0 || (force < 0 && (Random.value < 0.55f || !RiverSpot(out _, out _))));
            var fam = SaveSystem.LastFamily();
            var rest = new List<Pose>();

            if (camp)
            {
                titlePlace = "camp";
                minutes = 20.5f * 60f;
                titleCenter = fire.transform.position;
                var lot = house.LotById(fire.lot);
                var open = new Vector2(lot.rect.center.x - titleCenter.x, lot.rect.center.y - titleCenter.z);
                if (open.sqrMagnitude < 0.01f) open = Vector2.one;
                open.Normalize();
                titleYaw = Mathf.Atan2(-open.x, -open.y) * Mathf.Rad2Deg; // камера — со стороны поляны
                titleDist = 7.4f; titlePitch = 17f;
                float camAng = Mathf.Atan2(open.y, open.x);
                int n = fam.Count;
                for (int i = 0; i < n; i++)
                {
                    // по кругу у костра, со стороны камеры — просвет
                    float k = n == 1 ? 0.5f : i / (float)(n - 1);
                    float a = camAng + Mathf.Deg2Rad * (72f + k * 216f + Random.Range(-9f, 9f));
                    float r = 1.55f + Random.Range(-0.12f, 0.2f);
                    var p = titleCenter + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    p.y = house.terrain.GroundAt(p.x, p.z);
                    float yaw = Mathf.Atan2(titleCenter.x - p.x, titleCenter.z - p.z) * Mathf.Rad2Deg + Random.Range(-12f, 12f);
                    SpawnTitleActor(fam[i], p, yaw);
                }
            }
            else
            {
                titlePlace = "river";
                minutes = 19.75f * 60f;
                RiverSpot(out var bank, out var toWater);
                titleCenter = bank;
                // камера — над водой, лисички смотрят на неё (и на закат над ручьём)
                titleYaw = Mathf.Atan2(-toWater.x, -toWater.z) * Mathf.Rad2Deg;
                titleDist = 7.2f; titlePitch = 14f;
                var along = new Vector3(-toWater.z, 0, toWater.x);
                int n = fam.Count;
                // не в линейку: кто-то ближе к воде, кто-то дальше, по двое-трое, лёгкой дугой
                float run = 0;
                var offs = new float[n];
                for (int i = 0; i < n; i++) { offs[i] = run; run += Random.Range(0.7f, 1.35f) + (Random.value < 0.25f ? 0.6f : 0f); }
                for (int i = 0; i < n; i++)
                {
                    float off = offs[i] - (run - 1f) / 2f;
                    float depth = off * off * 0.05f + Random.Range(-0.35f, 0.55f) + (fam[i].stage == (int)LifeStage.Kit ? -0.3f : 0f);
                    var p = bank + along * off - toWater * depth;
                    p.y = house.terrain.GroundAt(p.x, p.z);
                    float yaw = Mathf.Atan2(toWater.x, toWater.z) * Mathf.Rad2Deg + Random.Range(-28f, 28f) - off * 6f;
                    SpawnTitleActor(fam[i], p, yaw);
                }
            }

            titleScene = true;
            titleT = 0;
            cam.minPitch = 8f;
            cam.Follow(null);
            cam.target = titleCenter;
            cam.yaw = titleYaw; cam.pitch = titlePitch; cam.dist = titleDist;
            cam.Snap();
        }

        void SpawnTitleActor(SaveFox sf, Vector3 p, float yaw)
        {
            var fox = SaveSystem.SpawnActor(this, sf, p);
            if (fox == null) return;
            fox.transform.position = p;
            fox.transform.rotation = Quaternion.Euler(0, yaw, 0);
            // взрослые сидят, лисята то сидят, то вскакивают поболтать
            var restPose = Pose.SitFloor;
            titleActors.Add(new TitleActor { fox = fox, pos = p, yaw = yaw, rest = restPose, nextT = Random.Range(3f, 12f) });
        }

        /// <summary>Место на берегу: трава, не дорога, не участок с домом, рядом нет моста и деревьев, что закроют вид.</summary>
        bool RiverSpot(out Vector3 bank, out Vector3 toWater)
        {
            bank = default; toWater = Vector3.forward;
            float best = float.MaxValue;
            var c = Town.Creek;
            var cands = new List<(Vector2 q, Vector2 w)>();
            for (float t = c.Length * 0.35f; t < c.Length * 0.97f; t += 1.5f)
            {
                var q = c.PointAt(t);
                var dir = (c.PointAt(Mathf.Min(c.Length, t + 0.5f)) - c.PointAt(Mathf.Max(0, t - 0.5f))).normalized;
                var nrm = new Vector2(dir.y, -dir.x);
                foreach (var s in new[] { -1f, 1f }) cands.Add((q + nrm * s * (c.half + 1.4f), -nrm * s));
            }
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) / 1.2f);
                cands.Add((Town.PondCenter + d * (Town.PondR + 1.4f), -d.normalized));
            }
            foreach (var (q, w) in cands)
            {
                var cell = new Vector2Int(Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y));
                if (!House.InTown(cell) || house.terrain.IsWaterCell(cell)) continue;
                if (Town.InForest(q.x, q.y) || Town.MountainMask(q.x, q.y) > 0.2f) continue;
                int lot = Town.LotIdAt(q.x, q.y);
                if (lot >= 0 && Town.Lots[lot].kind != LotKind.Park) continue;
                bool bad = false;
                for (int dx = -3; dx <= 3 && !bad; dx++)
                    for (int dz = -3; dz <= 3 && !bad; dz++)
                        if (Town.StreetKind(q.x + dx, q.y + dz) != 0 || Town.OnLotPath(q.x + dx, q.y + dz)) bad = true;
                foreach (var b in BridgeMark.All) if ((new Vector2(b.transform.position.x, b.transform.position.z) - q).magnitude < 8f) bad = true;
                if (bad) continue;
                float score = 0;
                foreach (var tr in house.trees)
                {
                    float dd = new Vector2(tr.p.x - q.x, tr.p.z - q.y).magnitude;
                    if (dd < 5f) score += (5f - dd) * 3f;
                }
                // камера над водой: деревья за водой тоже мешают
                var camP = q + w * 6f;
                foreach (var tr in house.trees)
                {
                    float dd = new Vector2(tr.p.x - camP.x, tr.p.z - camP.y).magnitude;
                    if (dd < 4f) score += (4f - dd) * 4f;
                }
                score += house.terrain.CellRoughness(cell) * 20f;
                score += Random.value * 2f; // каждый раз немножко разное место
                if (score < best)
                {
                    best = score;
                    bank = new Vector3(q.x, house.terrain.GroundAt(q.x, q.y), q.y);
                    toWater = new Vector3(w.x, 0, w.y).normalized;
                }
            }
            return best < float.MaxValue;
        }

        void UpdateTitleScene(float dt)
        {
            if (!titleScene) return;
            titleT += dt;
            // камера медленно покачивается, а не облетает (деревья не загораживают)
            cam.yaw = titleYaw + Mathf.Sin(titleT * 0.045f) * 14f;
            cam.pitch = titlePitch + Mathf.Sin(titleT * 0.031f) * 2f;
            cam.dist = titleDist + Mathf.Sin(titleT * 0.023f) * 0.6f;
            cam.target = titleCenter;
            foreach (var a in titleActors)
            {
                if (a.fox == null) continue;
                a.nextT -= dt;
                if (a.talkT > 0)
                {
                    a.talkT -= dt;
                    a.fox.pose = Pose.Talk;
                    if (a.talkT <= 0) a.nextT = Random.Range(6f, 16f);
                }
                else
                {
                    a.fox.pose = a.rest;
                    if (a.nextT <= 0) a.talkT = Random.Range(2.5f, 5f);
                }
                a.fox.walking = false;
                a.fox.transform.position = a.pos;
                a.fox.transform.rotation = Quaternion.Euler(0, a.yaw, 0);
            }
        }

        void EndTitleScene()
        {
            titleScene = false;
            titleActors.Clear();
            if (cam != null) cam.minPitch = 18f;
        }
    }
}
