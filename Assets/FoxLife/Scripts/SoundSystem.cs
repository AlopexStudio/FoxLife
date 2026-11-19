using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Звук без файлов: всё синтезируется при запуске. Спокойная мелодия (ксилофон и мягкий фон),
    /// днём — птицы и ветер, ночью — сверчки, дождь и гром по погоде, щелчки кнопок, монетки, колокольчик уведомлений.
    /// </summary>
    public class SoundSystem : MonoBehaviour
    {
        const int Rate = 44100;
        AudioSource music, birds, crickets, wind, rain, sfx;
        AudioClip click, coin, chime, thunder, knock, bell;
        public float musicVolume = 0.5f, soundVolume = 0.7f;
        float lastChime;

        public void Build()
        {
            musicVolume = PlayerPrefs.GetFloat("fox_music", 0.5f);
            soundVolume = PlayerPrefs.GetFloat("fox_sound", 0.7f);
            music = Src(Music(), true);
            birds = Src(Birds(), true);
            crickets = Src(Crickets(), true);
            wind = Src(Wind(), true);
            rain = Src(Rain(), true);
            sfx = gameObject.AddComponent<AudioSource>();
            click = Clip("click", 0.05f, (t, i) => Mathf.Sin(2 * Mathf.PI * 1400 * t) * Mathf.Exp(-t * 90f) * 0.4f);
            coin = Clip("coin", 0.5f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 1318.5f * t) * (t < 0.08f ? 1 : 0) + Mathf.Sin(2 * Mathf.PI * 1568f * t) * (t >= 0.08f ? 1 : 0)) * Mathf.Exp(-(t < 0.08f ? t : t - 0.08f) * 9f) * 0.3f);
            chime = Clip("chime", 1.2f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 1046.5f * t) + 0.4f * Mathf.Sin(2 * Mathf.PI * 2093f * t) + 0.2f * Mathf.Sin(2 * Mathf.PI * 3136f * t)) * Mathf.Exp(-t * 4f) * 0.18f);
            // стук в дверь: три глухих удара по дереву (низкий тон + короткий шорох)
            var kr = new System.Random(11);
            knock = Clip("knock", 0.75f, (t, i) =>
            {
                float s = 0;
                foreach (var t0 in new[] { 0f, 0.2f, 0.43f })
                {
                    float u = t - t0;
                    if (u < 0 || u > 0.18f) continue;
                    float env = Mathf.Exp(-u * 38f);
                    float n = (float)kr.NextDouble() * 2f - 1f;
                    s += (Mathf.Sin(2 * Mathf.PI * 170f * u) * 0.8f + Mathf.Sin(2 * Mathf.PI * 95f * u) * 0.6f + Mathf.Sin(2 * Mathf.PI * 410f * u) * 0.25f) * env
                        + n * Mathf.Exp(-u * 160f) * 0.5f;
                }
                return s * 0.45f;
            });
            // дверной звонок «динь-дон»
            bell = Clip("bell", 1.6f, (t, i) =>
            {
                float a = t < 0.5f ? (Mathf.Sin(2 * Mathf.PI * 880f * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * 1760f * t)) * Mathf.Exp(-t * 5f) : 0f;
                float u = t - 0.45f;
                float b = u > 0 ? (Mathf.Sin(2 * Mathf.PI * 698.5f * u) + 0.3f * Mathf.Sin(2 * Mathf.PI * 1397f * u)) * Mathf.Exp(-u * 3f) : 0f;
                return (a + b) * 0.2f;
            });
            var rnd = new System.Random(3);
            float lp = 0, lp2 = 0;
            thunder = Clip("thunder", 4.5f, (t, i) =>
            {
                float n = (float)rnd.NextDouble() * 2f - 1f;
                lp += (n - lp) * 0.02f; lp2 += (lp - lp2) * 0.05f;
                float crack = t < 0.25f ? n * (0.25f - t) * 1.5f : 0;
                return (lp2 * 9f * Mathf.Exp(-t * 0.9f) * (1f + 0.5f * Mathf.Sin(t * 7f)) + crack) * 0.8f;
            });
            foreach (var s in new[] { music, birds, crickets, wind, rain }) s.Play();
        }

        AudioSource Src(AudioClip c, bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = c; s.loop = loop; s.volume = 0; s.playOnAwake = false; s.spatialBlend = 0;
            return s;
        }

        delegate float Gen(float t, int i);

        static AudioClip Clip(string name, float seconds, Gen g)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(g(i / (float)Rate, i), -1f, 1f);
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        // ---------- музыка ----------

        static float Midi(int n) => 440f * Mathf.Pow(2f, (n - 69) / 12f);

        /// <summary>Тихая мелодия: аккорды C–Am–F–G, ксилофон по пентатонике и мягкий фон. 16 тактов.</summary>
        static AudioClip Music()
        {
            const float bpm = 92f;
            float beat = 60f / bpm;
            int bars = 16;
            float len = bars * 4 * beat;
            int n = Mathf.CeilToInt(len * Rate);
            var d = new float[n];
            int[][] chords = { new[] { 48, 52, 55 }, new[] { 45, 48, 52 }, new[] { 41, 45, 48 }, new[] { 43, 47, 50 } };
            int[] scale = { 60, 62, 64, 67, 69, 72, 74, 76 };
            var rnd = new System.Random(11);
            // фон: мягкие аккорды
            for (int bar = 0; bar < bars; bar++)
            {
                var ch = chords[bar % 4];
                int s0 = Mathf.RoundToInt(bar * 4 * beat * Rate), s1 = Mathf.RoundToInt((bar + 1) * 4 * beat * Rate);
                for (int i = s0; i < s1 && i < n; i++)
                {
                    float t = (i - s0) / (float)Rate, dur = (s1 - s0) / (float)Rate;
                    float env = Mathf.Min(1f, t / 0.6f) * Mathf.Min(1f, (dur - t) / 0.6f);
                    float v = 0;
                    foreach (var note in ch) { float f = Midi(note); v += Mathf.Sin(2 * Mathf.PI * f * i / Rate) * 0.5f + Mathf.Sin(2 * Mathf.PI * f * 2.003f * i / Rate) * 0.12f; }
                    d[i] += v * env * 0.05f;
                }
                // бас на первую долю
                AddNote(d, bar * 4 * beat, Midi(ch[0] - 12), 1.6f, 0.13f, 2.5f, 1f);
            }
            // ксилофон
            int prev = 3;
            for (int step = 0; step < bars * 8; step++)
            {
                if (rnd.NextDouble() < (step % 2 == 0 ? 0.25 : 0.55)) continue;
                int bar = step / 8;
                prev = Mathf.Clamp(prev + rnd.Next(-2, 3), 0, scale.Length - 1);
                int note = scale[prev];
                if (step % 8 == 0) { var ch = chords[bar % 4]; note = ch[rnd.Next(ch.Length)] + 24; }
                AddNote(d, step * beat / 2f, Midi(note), 1.1f, 0.16f, 5f, 4f);
            }
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i], -1, 1);
            var c = AudioClip.Create("music", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static void AddNote(float[] d, float start, float freq, float dur, float amp, float decay, float harm)
        {
            int s0 = Mathf.RoundToInt(start * Rate), cnt = Mathf.RoundToInt(dur * Rate);
            for (int k = 0; k < cnt; k++)
            {
                int i = (s0 + k) % d.Length;   // хвост последних нот переходит в начало — петля без щелчка
                float t = k / (float)Rate;
                float env = Mathf.Min(1f, t * 400f) * Mathf.Exp(-t * decay);
                d[i] += (Mathf.Sin(2 * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * freq * harm * t) * Mathf.Exp(-t * decay * 2f)) * env * amp;
            }
        }

        // ---------- окружение ----------

        static AudioClip Birds()
        {
            const float len = 9f;
            int n = Mathf.CeilToInt(len * Rate);
            var d = new float[n];
            var r = new System.Random(5);
            for (int k = 0; k < 14; k++)
            {
                float st = (float)r.NextDouble() * (len - 0.6f);
                float f0 = 2400f + (float)r.NextDouble() * 2200f, f1 = f0 * (0.7f + (float)r.NextDouble() * 0.7f);
                int notes = 1 + r.Next(4);
                float amp = 0.05f + (float)r.NextDouble() * 0.07f;
                for (int m = 0; m < notes; m++)
                {
                    float s = st + m * 0.11f, dur = 0.07f + (float)r.NextDouble() * 0.06f;
                    int s0 = Mathf.RoundToInt(s * Rate), cnt = Mathf.RoundToInt(dur * Rate);
                    float ph = 0;
                    for (int i = 0; i < cnt && s0 + i < n; i++)
                    {
                        float t = i / (float)cnt;
                        ph += 2 * Mathf.PI * Mathf.Lerp(f0, f1, t) / Rate;
                        d[s0 + i] += Mathf.Sin(ph) * Mathf.Sin(Mathf.PI * t) * amp;
                    }
                }
            }
            return Make("birds", d);
        }

        static AudioClip Crickets()
        {
            const float len = 4f;
            int n = Mathf.CeilToInt(len * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float chirp = Mathf.Repeat(t, 0.5f) < 0.18f ? Mathf.Max(0, Mathf.Sin(2 * Mathf.PI * 30f * t)) : 0;
                float chirp2 = Mathf.Repeat(t + 0.23f, 0.67f) < 0.15f ? Mathf.Max(0, Mathf.Sin(2 * Mathf.PI * 26f * t)) : 0;
                d[i] = Mathf.Sin(2 * Mathf.PI * 4400f * t) * chirp * 0.05f + Mathf.Sin(2 * Mathf.PI * 3900f * t) * chirp2 * 0.035f;
            }
            return Make("crickets", d);
        }

        static AudioClip Wind()
        {
            const float len = 8f;
            int n = Mathf.CeilToInt(len * Rate);
            var d = new float[n];
            var r = new System.Random(9);
            float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float w = (float)r.NextDouble() * 2 - 1;
                lp += (w - lp) * 0.03f; lp2 += (lp - lp2) * 0.03f;
                float t = i / (float)Rate;
                d[i] = lp2 * 3.5f * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * t / len));
            }
            Crossfade(d);
            return Make("wind", d);
        }

        static AudioClip Rain()
        {
            const float len = 5f;
            int n = Mathf.CeilToInt(len * Rate);
            var d = new float[n];
            var r = new System.Random(13);
            float lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float w = (float)r.NextDouble() * 2 - 1;
                lp += (w - lp) * 0.35f;
                hp = lp - prev; prev = lp;
                float drop = r.NextDouble() < 0.0008 ? ((float)r.NextDouble() * 0.6f) : 0;
                d[i] = lp * 0.22f + hp * 0.25f + drop;
            }
            Crossfade(d);
            return Make("rain", d);
        }

        static void Crossfade(float[] d)
        {
            int f = Rate / 4;
            for (int i = 0; i < f; i++)
            {
                float k = i / (float)f;
                d[i] = d[i] * k + d[d.Length - f + i] * (1 - k);
            }
        }

        static AudioClip Make(string name, float[] d)
        {
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        // ---------- смешивание ----------

        void Update()
        {
            var g = Game.I;
            if (g == null) return;
            float h = g.Hour;
            float day = Mathf.Clamp01(Mathf.InverseLerp(5.5f, 7f, h)) * (1f - Mathf.Clamp01(Mathf.InverseLerp(19.5f, 21f, h)));
            var w = g.weather;
            float rainK = w != null ? w.rain : 0, storm = w != null ? w.storm : 0;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2f);
            bool menu = g.ui != null && g.ui.screen != GameUI.MenuScreen.None;
            Fade(music, musicVolume * 0.55f * (g.speedIndex == 0 && !menu ? 0.6f : 1f), k);
            Fade(birds, soundVolume * day * (1f - rainK) * 0.45f, k);
            Fade(crickets, soundVolume * (1f - day) * (1f - rainK) * 0.3f, k);
            Fade(wind, soundVolume * (0.12f + storm * 0.35f + rainK * 0.1f), k);
            Fade(rain, soundVolume * rainK * (0.5f + storm * 0.4f), k);
        }

        static void Fade(AudioSource s, float target, float k) { if (s != null) s.volume = Mathf.Lerp(s.volume, target, k); }

        public void Click() { if (sfx != null) sfx.PlayOneShot(click, soundVolume * 0.6f); }
        public void Coin() { if (sfx != null) sfx.PlayOneShot(coin, soundVolume * 0.7f); }

        public void Chime()
        {
            if (sfx == null || Time.unscaledTime - lastChime < 1.5f) return;
            lastChime = Time.unscaledTime;
            sfx.PlayOneShot(chime, soundVolume * 0.6f);
        }

        /// <summary>Громкость звука в мире: чем дальше от камеры, тем тише.</summary>
        float Near(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null || Game.I == null || Game.I.cam == null) return 1f;
            float d = Vector3.Distance(p, Game.I.cam.target);
            return Mathf.Clamp01(1f - (d - 6f) / 30f);
        }

        public void Knock(Vector3 p) { float v = Near(p); if (sfx != null && v > 0.02f) sfx.PlayOneShot(knock, soundVolume * 0.9f * v); }
        public void Doorbell(Vector3 p) { float v = Near(p); if (sfx != null && v > 0.02f) sfx.PlayOneShot(bell, soundVolume * 0.7f * v); }

        public void Thunder(float delay) { if (sfx != null) StartCoroutine(Later(delay)); }

        System.Collections.IEnumerator Later(float d)
        {
            yield return new WaitForSecondsRealtime(d);
            sfx.PlayOneShot(thunder, soundVolume);
        }

        public void SaveVolumes()
        {
            PlayerPrefs.SetFloat("fox_music", musicVolume);
            PlayerPrefs.SetFloat("fox_sound", soundVolume);
        }
    }
}
