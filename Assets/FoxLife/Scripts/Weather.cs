using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public enum WeatherKind { Clear, Cloudy, Fog, Rain, Storm }

    /// <summary>
    /// Погода: облака из вокселов плывут по небу, туман стелется по долине, идёт дождь, гремит гроза.
    /// Меняется сама несколько раз в день; лисички мокнут под дождём и спешат домой.
    /// </summary>
    public class Weather : MonoBehaviour
    {
        public WeatherKind kind = WeatherKind.Clear, next = WeatherKind.Clear;
        public float hoursLeft = 5f;
        public bool locked;   // погода не меняется сама (настройки)
        // плавные величины 0..1
        public float cloud, fog, rain, storm;

        public static readonly string[] Names = { "Ясно", "Облачно", "Туман", "Дождь", "Гроза" };
        static readonly string[] WinterNames = { "Ясно", "Облачно", "Туман", "Снег", "Метель" };
        public static bool Winter => Game.I != null && Game.SeasonIndex(Game.I.Day) == 3;
        public string Label => (Winter ? WinterNames : Names)[(int)kind];
        public bool Raining => rain > 0.3f && !Winter;
        public bool Snowing => rain > 0.3f && Winter;
        bool snowMode;

        readonly List<(Transform t, Material m, float speed, float h)> clouds = new List<(Transform, Material, float, float)>();
        ParticleSystem rainPs;
        Material particleMat;
        LineRenderer bolt;
        float flash, nextBolt = 8f, boltShow;
        float lastHour;

        public void Build()
        {
            var sh = Shader.Find("FoxLife/Particle");
            particleMat = new Material(sh);
            // облака
            var rnd = new System.Random(5);
            for (int i = 0; i < 26; i++)
            {
                var go = new GameObject("cloud");
                go.transform.SetParent(transform, false);
                var st = go.AddComponent<Styleable>();
                st.kind = SurfaceKind.Outdoor; st.hidden = true; st.displayName = null;
                st.AddPart("Облако", StyleData.Make("Гладкий", "#FFFFFF", null, 1, 0.1f));
                int n = 3 + rnd.Next(4);
                float w = 6f + (float)rnd.NextDouble() * 8f;
                for (int k = 0; k < n; k++)
                {
                    float x = ((float)rnd.NextDouble() - 0.5f) * w, z = ((float)rnd.NextDouble() - 0.5f) * w * 0.5f;
                    float s = 3f + (float)rnd.NextDouble() * 4f;
                    Shapes.Ball(st, 0, go.transform, new Vector3(x, s * 0.2f, z), new Vector3(s * 1.4f, s * 0.7f, s));
                }
                Shapes.Box(st, 0, go.transform, new Vector3(0, 0, 0), new Vector3(w, 1.2f, w * 0.45f));
                Voxelizer.Bake(go.transform, 0.5f);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float h = 26f + (float)rnd.NextDouble() * 10f;
                go.transform.position = new Vector3(Town.MinX + (float)rnd.NextDouble() * Town.W, h, Town.MinZ + (float)rnd.NextDouble() * Town.H);
                clouds.Add((go.transform, st.parts[0].mat, 0.6f + (float)rnd.NextDouble() * 0.8f, h));
                go.SetActive(false);
            }
            // дождь
            var rg = new GameObject("rain");
            rg.transform.SetParent(transform, false);
            rainPs = rg.AddComponent<ParticleSystem>();
            rainPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = rainPs.main;
            main.loop = true; main.startLifetime = 1.1f; main.startSpeed = 0f; main.startSize = 0.022f;
            main.maxParticles = 6000; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.8f, 0.88f, 1f, 0.35f);
            var shape = rainPs.shape;
            shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(50f, 1f, 50f);
            var vel = rainPs.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-1.5f); vel.y = new ParticleSystem.MinMaxCurve(-22f); vel.z = new ParticleSystem.MinMaxCurve(-0.5f);
            var em = rainPs.emission; em.rateOverTime = 0f;
            // капли разбиваются о крыши, навесы, деревья и землю
            var col = rainPs.collision;
            col.enabled = true; col.type = ParticleSystemCollisionType.World; col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Medium; col.voxelSize = 0.5f; col.lifetimeLoss = 1f; col.bounce = 0f;
            col.maxCollisionShapes = 512; col.radiusScale = 0.5f;
            var pr = rg.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.025f; pr.lengthScale = 1f;
            pr.sharedMaterial = particleMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rainPs.Play();
            // молния
            var bg = new GameObject("bolt");
            bg.transform.SetParent(transform, false);
            bolt = bg.AddComponent<LineRenderer>();
            bolt.sharedMaterial = new Material(sh) { color = new Color(1f, 1f, 0.92f, 1f) };
            bolt.sharedMaterial.SetFloat("_Unlit", 1f);
            bolt.widthMultiplier = 0.35f; bolt.positionCount = 0;
            bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        float Target(WeatherKind k, int what)
        {
            // what: 0 облака, 1 туман, 2 дождь, 3 гроза
            switch (k)
            {
                case WeatherKind.Cloudy: return new[] { 0.65f, 0.1f, 0f, 0f }[what];
                case WeatherKind.Fog: return new[] { 0.45f, 1f, 0f, 0f }[what];
                case WeatherKind.Rain: return new[] { 0.9f, 0.25f, 1f, 0f }[what];
                case WeatherKind.Storm: return new[] { 1f, 0.3f, 1f, 1f }[what];
                default: return new[] { 0.18f, 0f, 0f, 0f }[what];
            }
        }

        WeatherKind Roll(float hour)
        {
            float r = Random.value;
            bool morning = hour < 10f;
            if (morning && r < 0.2f) return WeatherKind.Fog;
            if (r < 0.45f) return WeatherKind.Clear;
            if (r < 0.7f) return WeatherKind.Cloudy;
            if (r < 0.88f) return WeatherKind.Rain;
            if (r < 0.95f) return WeatherKind.Storm;
            return WeatherKind.Fog;
        }

        public void Set(WeatherKind k, float hours = 5f) { kind = k; hoursLeft = hours; }

        /// <summary>Сразу перейти к погоде без плавного перехода (для проверок).</summary>
        public void Snap() { cloud = Target(kind, 0); fog = Target(kind, 1); rain = Target(kind, 2); storm = Target(kind, 3); if (kind == WeatherKind.Storm) nextBolt = 0.3f; }

        /// <summary>Шаг погоды: dm — игровые минуты. Вызывается из игры каждый кадр.</summary>
        public void Tick(float dm, float hour, Camera cam, Vector3 focus)
        {
            if (!locked) hoursLeft -= dm / 60f;
            if (hoursLeft <= 0)
            {
                kind = Roll(hour);
                hoursLeft = 2.5f + Random.value * 5f;
            }
            float k = 1f - Mathf.Exp(-Mathf.Max(dm, Time.unscaledDeltaTime * 0.3f) * 0.04f);
            cloud = Mathf.Lerp(cloud, Target(kind, 0), k);
            fog = Mathf.Lerp(fog, Target(kind, 1), k);
            rain = Mathf.Lerp(rain, Target(kind, 2), k);
            storm = Mathf.Lerp(storm, Target(kind, 3), k);

            // облака плывут; чем пасмурнее, тем их больше и тем они темнее
            int shown = Mathf.RoundToInt(clouds.Count * Mathf.Clamp01(cloud * 1.1f));
            var grey = Color.Lerp(Color.white, new Color(0.45f, 0.48f, 0.55f), Mathf.Clamp01(rain * 0.6f + storm * 0.4f));
            for (int i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                bool on = i < shown;
                if (c.t.gameObject.activeSelf != on) c.t.gameObject.SetActive(on);
                if (!on) continue;
                var p = c.t.position;
                p.x += c.speed * (1f + storm * 2f) * Time.deltaTime * Mathf.Max(0.2f, Game.I.SpeedMul);
                if (p.x > Town.MinX + Town.W + 20) p.x = Town.MinX - 20;
                p.y = Mathf.Lerp(c.h, c.h - 6f, rain);
                c.t.position = p;
                c.m.SetColor("_Color", grey);
                c.m.SetColor("_Color2", grey);
            }

            // дождь (зимой — снег крупными хлопьями) вокруг камеры
            bool winter = Winter;
            if (winter != snowMode) SetSnowMode(winter);
            var em = rainPs.emission;
            em.rateOverTime = winter ? rain * (420f + storm * 700f) : rain * (2500f + storm * 2000f);
            rainPs.transform.position = new Vector3(focus.x, focus.y + 18f, focus.z);
            if (rain > 0.02f) KillIndoorDrops();

            // молнии (зимой гроз не бывает — метель)
            if (storm > 0.6f && !winter)
            {
                nextBolt -= Time.deltaTime;
                if (nextBolt <= 0)
                {
                    nextBolt = 4f + Random.value * 9f;
                    flash = 1f;
                    if (Game.I.sound != null) Game.I.sound.Thunder(0.3f + Random.value * 1.6f);
                    boltShow = 0.18f;
                    var start = focus + new Vector3(Random.Range(-30f, 30f), 32f, Random.Range(10f, 40f));
                    var pts = new List<Vector3>();
                    var q = start;
                    float ground = Game.I.house.terrain.GroundAt(q.x, q.z);
                    while (q.y > ground)
                    {
                        pts.Add(q);
                        q += new Vector3(Random.Range(-1.6f, 1.6f), -Random.Range(1.5f, 3f), Random.Range(-1.6f, 1.6f));
                    }
                    pts.Add(new Vector3(q.x, ground, q.z));
                    bolt.positionCount = pts.Count;
                    bolt.SetPositions(pts.ToArray());
                }
            }
            if (boltShow > 0) { boltShow -= Time.deltaTime; if (boltShow <= 0) bolt.positionCount = 0; }
            flash = Mathf.Max(0, flash - Time.deltaTime * 5f);
        }

        /// <summary>Переключить частицы: капли дождя или неспешные снежинки, которых несёт ветер.</summary>
        void SetSnowMode(bool snow)
        {
            snowMode = snow;
            var main = rainPs.main;
            main.startLifetime = snow ? 9f : 1.1f;
            main.startSize = snow ? new ParticleSystem.MinMaxCurve(0.07f, 0.16f) : new ParticleSystem.MinMaxCurve(0.022f);
            main.startColor = snow ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.8f, 0.88f, 1f, 0.35f);
            var vel = rainPs.velocityOverLifetime;
            vel.x = snow ? new ParticleSystem.MinMaxCurve(-1.2f, 0.6f) : new ParticleSystem.MinMaxCurve(-1.5f);
            vel.y = snow ? new ParticleSystem.MinMaxCurve(-2.6f, -1.6f) : new ParticleSystem.MinMaxCurve(-22f);
            vel.z = snow ? new ParticleSystem.MinMaxCurve(-0.6f, 0.6f) : new ParticleSystem.MinMaxCurve(-0.5f);
            var noise = rainPs.noise;
            noise.enabled = snow; noise.strength = 0.6f; noise.frequency = 0.4f;
            var pr = rainPs.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = snow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            rainPs.Clear();
        }

        ParticleSystem.Particle[] drops;

        /// <summary>Капли над комнатами исчезают у крыши — внутрь дома дождь не попадает, даже когда крыша спрятана.</summary>
        void KillIndoorDrops()
        {
            var h = Game.I.house;
            if (drops == null || drops.Length < rainPs.main.maxParticles) drops = new ParticleSystem.Particle[rainPs.main.maxParticles];
            int n = rainPs.GetParticles(drops);
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                var p = drops[i].position;
                var c = GridMap.CellOf(p);
                var r = h.RoomAt(0, c);
                if (r == null) continue;
                float roofY = r.elev + (h.RoomIndexAt(1, c) >= 0 ? 2f * House.WallH : House.WallH) + 0.2f;
                if (p.y < roofY) { drops[i].remainingLifetime = 0; changed = true; }
            }
            if (changed) rainPs.SetParticles(drops, n);
        }

        /// <summary>Свет и туман поверх дневного неба.</summary>
        public void ApplySky(Light sun, Camera cam, ref Color sky)
        {
            float dim = cloud * 0.45f + rain * 0.25f + storm * 0.2f;
            sun.intensity *= 1f - dim;
            sun.shadowStrength = Mathf.Lerp(0.7f, 0.25f, Mathf.Clamp01(cloud + rain * 0.5f));
            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, RenderSettings.ambientLight * 0.75f + new Color(0.05f, 0.06f, 0.09f), Mathf.Clamp01(rain + storm * 0.5f));
            var grey = new Color(0.62f, 0.66f, 0.72f) * Mathf.Max(0.25f, sky.grayscale * 1.2f);
            sky = Color.Lerp(sky, grey, Mathf.Clamp01(cloud * 0.5f + rain * 0.5f + fog * 0.4f));
            if (flash > 0)
            {
                float f = flash * (Mathf.Sin(flash * 40f) > 0 ? 1f : 0.55f);
                sun.intensity += f * 2.5f;
                RenderSettings.ambientLight += new Color(0.6f, 0.62f, 0.7f) * f;
                sky = Color.Lerp(sky, new Color(0.9f, 0.92f, 1f), f * 0.7f);
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.004f + fog * 0.021f + rain * 0.006f; // туман молочный, но город сквозь него виден
            RenderSettings.fogColor = Color.Lerp(sky, new Color(0.85f, 0.87f, 0.9f) * Mathf.Max(0.3f, sky.grayscale * 1.3f), fog * 0.6f);
        }
    }
}
