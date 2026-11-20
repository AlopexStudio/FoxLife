using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Четыре стихии — любимый «жанр» лисички: музыка, передачи, книги и картины.</summary>
    public enum Element { Fire, Water, Earth, Air }

    public static class Elements
    {
        public static readonly string[] Names = { "Огонь", "Вода", "Земля", "Воздух" };
        public static readonly string[] Music = { "задорные огненные пляски", "тягучие морские баллады", "народные песни", "лёгкий воздушный джаз" };
        public static readonly string[] Tv = { "приключения и погони", "мелодрамы", "передачи о природе", "фантастику" };
        public static readonly string[] Books = { "приключенческие романы", "романтические истории", "сказания и летописи", "стихи и мечтательные повести" };
        public static readonly string[] Paint = { "закаты и костры", "море и реки", "холмы и леса", "облака и небо" };

        public static Element Opposite(Element e) => e == Element.Fire ? Element.Water : e == Element.Water ? Element.Fire : e == Element.Earth ? Element.Air : Element.Earth;

        /// <summary>Стихия по характеру: общительные — огонь, добрые — вода, собранные — земля, мечтатели — воздух.</summary>
        public static Element From(Personality p, int seed)
        {
            var rnd = new System.Random(seed);
            float[] w =
            {
                1f + Mathf.Max(0, p.E - 4) * 0.6f,
                1f + Mathf.Max(0, p.A - 4) * 0.6f,
                1f + Mathf.Max(0, p.C - 4) * 0.6f,
                1f + Mathf.Max(0, p.O - 4) * 0.6f,
            };
            float sum = 0; foreach (var x in w) sum += x;
            float r = (float)rnd.NextDouble() * sum;
            for (int i = 0; i < 4; i++) { r -= w[i]; if (r <= 0) return (Element)i; }
            return Element.Air;
        }
    }

    public partial class Fox
    {
        public int elementId = -1, likedWeather = -1, dislikedWeather = -1;
        float tasteTimer = 30f, homeTimer = 200f;

        int TasteSeed { get { int h = 17; foreach (var c in foxName ?? "") h = h * 31 + c; return h; } }

        public Element Element
        {
            get
            {
                if (elementId < 0) elementId = (int)Elements.From(pers, TasteSeed);
                return (Element)elementId;
            }
        }

        public WeatherKind LikedWeather { get { InitWeatherTaste(); return (WeatherKind)likedWeather; } }
        public WeatherKind DislikedWeather { get { InitWeatherTaste(); return (WeatherKind)dislikedWeather; } }

        void InitWeatherTaste()
        {
            if (likedWeather >= 0 && dislikedWeather >= 0) return;
            var rnd = new System.Random(TasteSeed * 7 + 3);
            // мечтатели любят туман, смелые — грозу, тревожные её боятся; солнце любят многие
            var like = new List<WeatherKind> { WeatherKind.Clear, WeatherKind.Clear, WeatherKind.Cloudy, WeatherKind.Rain };
            if (pers.O >= 6) like.Add(WeatherKind.Fog);
            if (pers.N <= 4 && pers.E >= 6) like.Add(WeatherKind.Storm);
            if (Element == Element.Water) { like.Add(WeatherKind.Rain); like.Add(WeatherKind.Rain); }
            if (Element == Element.Air) like.Add(WeatherKind.Cloudy);
            likedWeather = (int)like[rnd.Next(like.Count)];
            var dis = new List<WeatherKind> { WeatherKind.Rain, WeatherKind.Fog, WeatherKind.Cloudy };
            if (pers.N >= 6) { dis.Add(WeatherKind.Storm); dis.Add(WeatherKind.Storm); }
            if (Element == Element.Fire) dis.Add(WeatherKind.Rain);
            if (pers.E <= 4) dis.Add(WeatherKind.Clear);
            dis.RemoveAll(k => (int)k == likedWeather);
            dislikedWeather = (int)dis[rnd.Next(dis.Count)];
        }

        public static string WeatherName(WeatherKind k, bool accusative = false) =>
            k == WeatherKind.Clear ? "солнце" : k == WeatherKind.Cloudy ? "облака" : k == WeatherKind.Fog ? "туман" : k == WeatherKind.Rain ? "дождь" : accusative ? "грозу" : "гроза";

        /// <summary>Погода по вкусу и привязанность к дому — раз в какое-то время.</summary>
        void TasteEffects(float dm)
        {
            if (sleeping || atWork || carried) return;
            tasteTimer -= dm;
            if (tasteTimer <= 0 && G.weather != null)
            {
                tasteTimer = 90f;
                var k = G.weather.kind;
                if (k == LikedWeather) AddMoodlet("Люблю " + WeatherName(k, accusative: true), 4, 150);
                else if (k == DislikedWeather) AddMoodlet("Не люблю " + WeatherName(k, accusative: true), pers.N >= 7 ? -5 : -3, 150);
            }
            homeTimer -= dm;
            if (homeTimer <= 0)
            {
                homeTimer = 360f;
                var here = G.house.LotAt(Cell);
                if (here == null || here.id != home) return;
                float a = HomeAttachment(out string why);
                if (a >= 2.5f) AddMoodlet("Мой уютный дом" + (why != null ? " (" + why + ")" : ""), Mathf.Min(8, 2 + a), 360);
                else if (a <= 0.5f && stage >= LifeStage.Teen) AddMoodlet("Дома чего-то не хватает" + (why != null ? " (" + why + ")" : ""), -3, 360);
            }
        }

        /// <summary>Насколько дом «свой»: любимые вещи по характеру и стихии.</summary>
        public float HomeAttachment(out string why)
        {
            why = null;
            float score = 0;
            bool easel = false, music = false, books = false, plants = false, fire = false, water = false, sky = false, tv = false, bed = false;
            foreach (var f in G.furniture)
            {
                if (f.lot != home) continue;
                switch (f.def.id)
                {
                    case "easel": easel = true; break;
                    case "guitar": case "piano": music = true; break;
                    case "bookshelf": books = true; break;
                    case "plant": case "vase": case "flowerbed": case "berrybush": plants = true; break;
                    case "campfire": case "lamp": fire = true; break;
                    case "bath": case "fountain": case "birdbath": water = true; break;
                    case "telescope": case "swing": sky = true; break;
                    case "tv": case "computer": tv = true; break;
                    case "bed": case "doublebed": case "foxbed": bed = true; break;
                }
            }
            if (bed) score += 1;
            if (pers.O >= 7) { if (easel || music) { score += 1.5f; why = easel ? "мольберт" : "музыка"; } else why = "негде творить"; }
            if (pers.O <= 4 && tv) score += 1;
            if (pers.C >= 7 && books) { score += 1; why ??= "книги"; }
            switch (Element)
            {
                case Element.Fire: if (fire) score += 1; break;
                case Element.Water: if (water) score += 1; break;
                case Element.Earth: if (plants) { score += 1; why ??= "цветы"; } break;
                case Element.Air: if (sky) score += 1; break;
            }
            if (G.PlatesOn(home) > 2) score -= 1;
            int years = Mathf.Max(0, ageDays) / 7;
            score += Mathf.Min(1.5f, years * 0.3f);
            return score;
        }
    }

    /// <summary>
    /// Живые экраны и инструменты: телевизор показывает передачи по стихиям, на компьютере печатается роман,
    /// на мольберте постепенно появляется картина, гитара и пианино звучат.
    /// </summary>
    public class MediaFX : MonoBehaviour
    {
        public Furniture furn;
        Texture2D tex;
        GameObject quad;
        AudioSource src;
        float frameT, noteT, paint, seed;
        Element channel;
        Fox lastUser;
        int noteIdx;
        static Shader shader;
        static AudioClip[] guitarNotes, pianoNotes;
        Color32[] target;
        int[] order;

        public static void Attach(Furniture f)
        {
            string id = f.def.id;
            if (id != "tv" && id != "computer" && id != "easel" && id != "guitar" && id != "piano") return;
            var m = f.gameObject.AddComponent<MediaFX>();
            m.furn = f;
        }

        void Start()
        {
            string id = furn.def.id;
            if (id == "tv") MakeQuad(new Vector3(0, 0.7f, FrontZ(0.7f, 0.18f) - 0.003f), new Vector2(0.74f, 0.4f), Vector3.zero, 37, 20);
            else if (id == "computer") MakeQuad(new Vector3(0, 0.95f, FrontZ(0.95f, 0.15f) - 0.003f), new Vector2(0.64f, 0.34f), Vector3.zero, 32, 17);
            else if (id == "easel") MakeQuad(new Vector3(0, 0.92f, 0.015f), new Vector2(0.6f, 0.5f), new Vector3(8, 0, 0), 30, 25);
            else
            {
                src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 1.5f; src.maxDistance = 13f; src.dopplerLevel = 0; src.playOnAwake = false;
            }
            if (quad != null && furn.def.id != "easel") quad.SetActive(false);
        }

        PixelGrid grid, backGrid;
        public Fox paintOwner;
        public bool scribble;

        static MediaFX Of(Furniture e) => e != null ? e.GetComponent<MediaFX>() : null;

        /// <summary>Недорисованная чужая картина — сначала её надо выкинуть (или дорисует хозяин).</summary>
        public static string PaintBlocked(Fox f, Furniture e)
        {
            var m = Of(e);
            if (m == null || m.paintOwner == null || m.paintOwner == f || m.paint <= 0f || m.paint >= 1f) return null;
            if (!Game.I.foxes.Contains(m.paintOwner)) return null;
            return "На мольберте недорисованная картина " + m.paintOwner.NameIn(NameCase.Genitive);
        }

        public static string DiscardBlocked(Furniture e)
        {
            var m = Of(e);
            return m == null || m.paint <= 0f ? "Холст и так чистый" : null;
        }

        /// <summary>Картина готова: хорошая — продаётся и уходит с мольберта, каракули — просто в мусор (зато опыт).</summary>
        public static void FinishPainting(Fox f, Furniture e)
        {
            var m = Of(e);
            bool scr = m != null ? m.scribble : Random.value < ScribbleChance(f);
            if (scr)
            {
                f.AddMoodlet("Одни каракули…", f.pers.N >= 6 ? -3 : -1, 90);
                f.Say(Random.value < 0.5f ? "Ну и каракули… В следующий раз выйдет лучше." : "Хм. Это… абстракция!");
                Game.I.LogFox(f, $"{f.foxName} нарисовал{(f.female ? "а" : "")} каракули и выбросил{(f.female ? "а" : "")} их (зато опыт!)");
            }
            else
            {
                // цена растёт с мастерством, но если продавать картины одну за другой — галерея платит всё меньше
                int day = Game.I.Day;
                if (salesDay != day) { salesDay = day; salesToday.Clear(); }
                salesToday.TryGetValue(f.home, out int sold);
                salesToday[f.home] = sold + 1;
                float k = Mathf.Pow(0.72f, sold);
                int price = Mathf.Max(3, Mathf.RoundToInt((6 + Mathf.Pow(f.SkillLevel(Skill.Creativity), 1.6f) * 2.4f) * k));
                Game.I.AddMoneyFor(f, price, $"{f.foxName} продаёт картину" + (sold > 0 ? " (сегодня картин уже много — дешевле)" : ""));
                f.Count("painting");
            }
            if (m != null) m.ClearCanvas();
        }

        static int salesDay = -1;
        static readonly Dictionary<int, int> salesToday = new Dictionary<int, int>();

        public static float ScribbleChance(Fox f) => Mathf.Clamp(0.75f - f.SkillLevel(Skill.Creativity) * 0.08f, 0.02f, 0.75f);

        public static void Discard(Fox by, Furniture e)
        {
            var m = Of(e);
            if (m == null) return;
            var owner = m.paintOwner;
            if (owner != null && owner != by && Game.I.foxes.Contains(owner))
            {
                owner.AddMoodlet($"Мой рисунок выбросили ({by.foxName})", -5, 360);
                Social.AddRel(owner, by, -4);
                if (by.pers.A >= 6) by.AddMoodlet("Жалко выбрасывать чужой рисунок", -3, 180);
                owner.Remember($"{by.foxName} выбросил{(by.female ? "а" : "")} мой рисунок");
            }
            m.ClearCanvas();
        }

        public void ClearCanvas()
        {
            paint = 0; paintOwner = null; target = null; scribble = false;
            if (tex == null || grid == null) return;
            var blank = new Color32[tex.width * tex.height];
            for (int i = 0; i < blank.Length; i++) blank[i] = C(0.98f, 0.97f, 0.94f);
            tex.SetPixels32(blank);
            grid.Flatten();
            Push();
        }

        public string DebugState => $"{furn.def.id}: экран {(quad != null ? quad.activeSelf.ToString() : "нет")}, пользователь {(lastUser != null ? lastUser.foxName : "-")}, картина {paint:0.00}";

        /// <summary>Где на самом деле лицевая грань корпуса (после вокселизации она сдвигается на шаг сетки).</summary>
        float FrontZ(float cy, float halfH)
        {
            // ближайшая к зрителю грань, которая закрывает несколько точек экрана (вершины большой грани — только по углам,
            // поэтому смотрим именно на треугольники, а не на вершины)
            float best = 1f;
            var probes = new[] { new Vector2(0, cy), new Vector2(-0.12f, cy), new Vector2(0.12f, cy), new Vector2(0, cy + halfH * 0.5f), new Vector2(0, cy - halfH * 0.5f) };
            foreach (var mf in furn.model.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || mr.forceRenderingOff || mf.GetComponent<PixelGrid>() != null) continue;
                var vs = mf.sharedMesh.vertices;
                var tris = mf.sharedMesh.triangles;
                var lp = new Vector3[vs.Length];
                for (int i = 0; i < vs.Length; i++) lp[i] = furn.model.InverseTransformPoint(mf.transform.TransformPoint(vs[i]));
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    Vector3 a = lp[tris[t]], b = lp[tris[t + 1]], c = lp[tris[t + 2]];
                    if (Mathf.Abs(a.z - b.z) > 0.002f || Mathf.Abs(a.z - c.z) > 0.002f) continue; // только грани, обращённые вперёд/назад
                    if (a.z >= best) continue;
                    foreach (var q in probes)
                        if (InTri(q, a, b, c)) { best = a.z; break; }
                }
            }
            return best < 1f ? best : 0.05f;
        }

        static bool InTri(Vector2 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        void MakeQuad(Vector3 pos, Vector2 size, Vector3 euler, int w, int h)
        {
            bool canvas = furn.def.id == "easel";
            quad = new GameObject(canvas ? "холст" : "экран");
            quad.transform.SetParent(furn.model, false);
            quad.transform.localPosition = pos;
            quad.transform.localEulerAngles = euler;
            grid = quad.AddComponent<PixelGrid>();
            grid.Build(w, h, size, canvas ? 0.03f : 0.004f, canvas ? 0f : 0.0015f, canvas ? 0f : 0.85f, canvas ? 0.012f : 0f);
            if (canvas) grid.backColor = new Color32(214, 204, 184, 255); // обратная сторона холста — льняная ткань
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false); // только как холст для рисования в памяти
            if (canvas)
            {
                var blank = new Color32[w * h];
                for (int i = 0; i < blank.Length; i++) blank[i] = C(0.98f, 0.97f, 0.94f);
                tex.SetPixels32(blank);
                grid.SetColors(blank);
            }
        }

        void Push()
        {
            if (grid == null) return;
            var px = tex.GetPixels32();
            grid.SetColors(px);
            if (backGrid != null)
            {
                // с обратной стороны — зеркально, чтобы картина читалась так же
                int w = tex.width, h = tex.height;
                var m = new Color32[px.Length];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = px[y * w + (w - 1 - x)];
                backGrid.SetColors(m);
            }
        }

        Fox User()
        {
            foreach (var f in Game.I.foxes)
                if (f != null && f.current != null && f.current.kind == ActKind.UseObject && f.current.furn == furn && f.current.stage == 2) return f;
            return null;
        }

        void Update()
        {
            if (furn == null || furn.isGhost) return;
            var u = User();
            if (u != null && u.current != null && u.current.inter != null && u.current.inter.openBuild) u = null; // план дома — не картина
            if (u != lastUser)
            {
                if (u != null) OnStart(u);
                lastUser = u;
            }
            string id = furn.def.id;
            if (id == "tv" || id == "computer") Screen(u);
            else if (id == "easel") Easel(u);
            else Instrument(u);
        }

        void OnStart(Fox u)
        {
            seed = Random.value * 100f;
            channel = Random.value < 0.65f ? u.Element : (Element)Random.Range(0, 4);
            if (furn.def.id == "tv")
            {
                if (channel == u.Element) u.AddMoodlet("Смотрю любимую передачу", 4, 180);
                else if (channel == Elements.Opposite(u.Element)) u.AddMoodlet("Передача не по вкусу", -2, 120);
            }
            if (furn.def.id == "easel")
            {
                if (paint >= 1f || target == null || paintOwner != u) NewPainting(u);
            }
            if (furn.def.id == "guitar" || furn.def.id == "piano")
                if (u.Element == channel) u.AddMoodlet("Играю любимую музыку", 3, 150);
        }

        // ---------- экраны ----------

        void Screen(Fox u)
        {
            if (quad == null) return;
            bool on = u != null;
            if (quad.activeSelf != on) quad.SetActive(on);
            if (!on) return;
            frameT -= Time.deltaTime;
            if (frameT > 0) return;
            frameT = 0.18f;
            float t = Time.time + seed;
            bool novel = u.current != null && u.current.inter != null && u.current.inter.name.Contains("роман");
            if (furn.def.id == "computer" && novel) DrawNovel(u);
            else if (furn.def.id == "computer" && channel == Element.Air) DrawWeb(t);
            else DrawScene(tex, channel, t, true);
            Push();
        }

        static Color32 C(float r, float g, float b) => new Color(r, g, b, 1);

        /// <summary>Пиксельная сцена по стихии; animated — с движением.</summary>
        static void DrawScene(Texture2D tex, Element e, float t, bool animated)
        {
            int w = tex.width, h = tex.height;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fy = y / (float)h, fx = x / (float)w;
                    Color c;
                    switch (e)
                    {
                        case Element.Fire:
                        {
                            c = Color.Lerp(new Color(0.12f, 0.05f, 0.1f), new Color(0.35f, 0.12f, 0.2f), fy);
                            float flame = 0.35f + 0.25f * Mathf.PerlinNoise(fx * 4f, t * 2.5f) + 0.15f * Mathf.Sin(fx * 9f + t * 5f);
                            if (fy < flame) c = Color.Lerp(new Color(1f, 0.9f, 0.4f), new Color(0.95f, 0.3f, 0.1f), fy / flame);
                            if (fy < 0.12f) c = new Color(0.25f, 0.15f, 0.1f);
                            break;
                        }
                        case Element.Water:
                        {
                            c = Color.Lerp(new Color(0.75f, 0.88f, 1f), new Color(0.45f, 0.7f, 0.95f), fy);
                            float wave = 0.45f + 0.05f * Mathf.Sin(fx * 12f + t * 2f) + 0.03f * Mathf.Sin(fx * 23f - t * 3f);
                            if (fy < wave) c = Color.Lerp(new Color(0.1f, 0.3f, 0.6f), new Color(0.25f, 0.55f, 0.85f), fy / wave);
                            float bx = Mathf.Repeat(t * 0.05f, 1.2f) - 0.1f;
                            if (Mathf.Abs(fx - bx) < 0.08f && fy > wave - 0.03f && fy < wave + 0.05f) c = new Color(0.65f, 0.4f, 0.25f);
                            if (Mathf.Abs(fx - bx) < 0.01f && fy > wave && fy < wave + 0.22f) c = new Color(0.9f, 0.9f, 0.85f);
                            if (Mathf.Abs(fx - bx - 0.03f) < 0.03f && fy > wave + 0.06f && fy < wave + 0.2f) c = new Color(1f, 1f, 0.95f);
                            break;
                        }
                        case Element.Earth:
                        {
                            c = Color.Lerp(new Color(1f, 0.92f, 0.75f), new Color(0.6f, 0.82f, 0.95f), fy);
                            float sx = fx + (animated ? t * 0.04f : 0);
                            float hill = 0.35f + 0.12f * Mathf.Sin(sx * 7f) + 0.08f * Mathf.Sin(sx * 15f + 1f);
                            float hill2 = 0.22f + 0.08f * Mathf.Sin(sx * 11f + 2f);
                            if (fy < hill) c = new Color(0.45f, 0.65f, 0.35f);
                            if (fy < hill2) c = new Color(0.32f, 0.52f, 0.27f);
                            if (Mathf.Repeat(sx * 6f, 1f) < 0.08f && fy < hill + 0.12f && fy > hill - 0.02f) c = new Color(0.2f, 0.4f, 0.2f);
                            float dx = fx - 0.78f, dy = fy - 0.78f;
                            if (dx * dx * 2.5f + dy * dy < 0.012f) c = new Color(1f, 0.85f, 0.4f);
                            break;
                        }
                        default:
                        {
                            c = Color.Lerp(new Color(0.85f, 0.92f, 1f), new Color(0.45f, 0.65f, 0.95f), fy);
                            for (int k = 0; k < 3; k++)
                            {
                                float cx = Mathf.Repeat(k * 0.37f + (animated ? t * (0.02f + k * 0.01f) : 0), 1.3f) - 0.15f;
                                float cy = 0.3f + k * 0.22f;
                                float ddx = (fx - cx) * 1.2f, ddy = (fy - cy) * 2.2f;
                                if (ddx * ddx + ddy * ddy < 0.012f + 0.01f * Mathf.PerlinNoise(fx * 10f, k)) c = Color.white;
                            }
                            float bx = Mathf.Repeat(t * 0.08f, 1.2f) - 0.1f, by = 0.6f + Mathf.Sin(t * 2f) * 0.05f;
                            if (Mathf.Abs(fx - bx) < 0.04f && Mathf.Abs(fy - by - Mathf.Abs(fx - bx) * 1.2f) < 0.025f) c = new Color(0.2f, 0.2f, 0.3f);
                            break;
                        }
                    }
                    if (animated && (y & 1) == 0) c *= 0.93f; // строчки кинескопа
                    c.a = 1;
                    px[y * w + x] = c;
                }
            tex.SetPixels32(px);
        }

        void DrawWeb(float t)
        {
            int w = tex.width, h = tex.height;
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = C(0.97f, 0.97f, 0.98f);
            for (int x = 0; x < w; x++) for (int y = h - 4; y < h; y++) px[y * w + x] = C(0.35f, 0.55f, 0.85f);
            int scroll = (int)(t * 3f) % 12;
            for (int b = 0; b < 4; b++)
            {
                int y0 = h - 7 - b * 6 + scroll;
                for (int y = y0 - 4; y < y0; y++)
                {
                    if (y < 0 || y >= h - 4) continue;
                    for (int x = 2; x < 12; x++) px[y * w + x] = (b % 2 == 0) ? C(0.95f, 0.65f, 0.35f) : C(0.6f, 0.8f, 0.5f);
                    for (int x = 14; x < w - 2; x += 1) if (((x + b) % 5) != 0 && y == y0 - 2) px[y * w + x] = C(0.55f, 0.55f, 0.6f);
                }
            }
            tex.SetPixels32(px);
        }

        void DrawNovel(Fox u)
        {
            int w = tex.width, h = tex.height;
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = C(1f, 0.99f, 0.95f);
            float prog = u.current.inter != null && u.current.inter.duration > 0 ? u.current.elapsed / u.current.inter.duration : 0;
            int chars = Mathf.FloorToInt(prog * 220f);
            var rnd = new System.Random((int)(seed * 100));
            int lines = 0;
            for (int y = h - 3; y > 1 && chars > 0; y -= 2, lines++)
            {
                for (int x = 2; x < w - 2 && chars > 0; x++, chars--)
                    if (rnd.Next(6) != 0) px[y * w + x] = C(0.25f, 0.25f, 0.3f);
            }
            if (Mathf.Repeat(Time.time, 1f) < 0.5f)
            {
                int cy = h - 3 - lines * 2 + 2;
                if (cy > 0 && cy < h) px[cy * w + Mathf.Min(w - 3, 2 + (int)(prog * 220f) % (w - 4))] = C(0.1f, 0.1f, 0.1f);
            }
            tex.SetPixels32(px);
        }

        // ---------- мольберт ----------

        void NewPainting(Fox u)
        {
            if (tex == null) return;
            paint = 0;
            paintOwner = u;
            scribble = Random.value < ScribbleChance(u);
            var tmp = new Texture2D(tex.width, tex.height);
            DrawScene(tmp, u.Element, Random.value * 50f, false);
            target = tmp.GetPixels32();
            Destroy(tmp);
            if (scribble) target = Scribble(tex.width, tex.height);
            // кривизна от навыка: новичок рисует неровно
            int skill = u.SkillLevel(Skill.Creativity);
            var r = new System.Random((int)(seed * 1000));
            for (int i = 0; i < target.Length; i++)
                if (r.NextDouble() > 0.55 + skill * 0.05)
                {
                    int j = Mathf.Clamp(i + r.Next(-2, 3) + r.Next(-1, 2) * tex.width, 0, target.Length - 1);
                    target[i] = target[j];
                }
            order = new int[target.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            // снизу вверх, мазками
            var keys = new float[order.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = (i / tex.width) * 3 + (float)r.NextDouble() * 5f;
            System.Array.Sort(keys, order);
            var blank = new Color32[target.Length];
            for (int i = 0; i < blank.Length; i++) blank[i] = C(0.98f, 0.97f, 0.94f);
            tex.SetPixels32(blank);
            Push();
            grid?.Flatten();
            backGrid?.Flatten();
        }

        /// <summary>Каракули: пара кривых линий и кляксы на белом.</summary>
        static Color32[] Scribble(int w, int h)
        {
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = C(0.98f, 0.97f, 0.94f);
            var cols = new[] { C(0.9f, 0.3f, 0.3f), C(0.3f, 0.5f, 0.9f), C(0.95f, 0.75f, 0.2f), C(0.3f, 0.7f, 0.35f), C(0.35f, 0.3f, 0.35f) };
            int lines = Random.Range(2, 5);
            for (int l = 0; l < lines; l++)
            {
                var c = cols[Random.Range(0, cols.Length)];
                float x = Random.Range(2, w - 2), y = Random.Range(2, h - 2), a = Random.value * 6.3f;
                for (int k = 0; k < 40; k++)
                {
                    a += Random.Range(-0.8f, 0.8f);
                    x = Mathf.Clamp(x + Mathf.Cos(a), 1, w - 2); y = Mathf.Clamp(y + Mathf.Sin(a), 1, h - 2);
                    px[(int)y * w + (int)x] = c;
                    if (Random.value < 0.3f) px[Mathf.Min(h - 1, (int)y + 1) * w + (int)x] = c;
                }
            }
            for (int b = 0; b < 3; b++)
            {
                var c = cols[Random.Range(0, cols.Length)];
                int cx = Random.Range(3, w - 3), cy = Random.Range(3, h - 3), r = Random.Range(1, 3);
                for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) if (dx * dx + dy * dy <= r * r + 1) px[(cy + dy) * w + cx + dx] = c;
            }
            return px;
        }

        void Easel(Fox u)
        {
            if (quad == null) return;

            if (u == null || target == null || u.current == null) return;
            float p = u.current.inter != null && u.current.inter.duration > 0 ? Mathf.Clamp01(u.current.elapsed / u.current.inter.duration) : 0;
            if (p <= paint + 0.01f) return;
            var px = tex.GetPixels32();
            int from = Mathf.FloorToInt(paint * order.Length), to = Mathf.FloorToInt(p * order.Length);
            for (int i = from; i < to && i < order.Length; i++)
            {
                px[order[i]] = target[order[i]];
                float hh = 0.35f + 0.65f * Mathf.PerlinNoise(order[i] * 0.37f, seed);
                grid?.Pop(order[i], hh);
                if (backGrid != null) { int w = tex.width, ix = order[i]; backGrid.Pop((ix / w) * w + (w - 1 - ix % w), hh); }
            }
            tex.SetPixels32(px);
            Push();
            paint = p >= 0.98f ? 1f : p;
        }

        // ---------- инструменты ----------

        // ноты — хроматически, от 12 полутонов ниже до 24 выше «до» первой октавы
        const int Low = 12, Span = 37;
        static readonly int[] Penta = { 0, 2, 4, 7, 9 };
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        readonly List<(float at, int semi, float vol)> pending = new List<(float, int, float)>();
        float clock;

        static AudioClip[] Notes(bool piano)
        {
            const int rate = 44100;
            var list = new AudioClip[Span];
            for (int k = 0; k < Span; k++)
            {
                int semis = k - Low;
                float freq = 261.63f * Mathf.Pow(2f, (semis - (piano ? 0 : 12)) / 12f);
                float len = piano ? 1.5f : 1.0f;
                int n = (int)(len * rate);
                var d = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float env = piano ? Mathf.Exp(-t * 3.0f) * Mathf.Min(1, t * 400f) : Mathf.Exp(-t * 4.5f) * Mathf.Min(1, t * 900f);
                    float s = Mathf.Sin(2 * Mathf.PI * freq * t) + (piano ? 0.35f : 0.5f) * Mathf.Sin(4 * Mathf.PI * freq * t) * Mathf.Exp(-t * 4f)
                              + (piano ? 0.15f : 0.3f) * Mathf.Sin(6 * Mathf.PI * freq * t) * Mathf.Exp(-t * 7f)
                              + (piano ? 0.06f : 0.12f) * Mathf.Sin(8 * Mathf.PI * freq * t) * Mathf.Exp(-t * 10f);
                    d[i] = s * env * 0.2f;
                }
                list[k] = AudioClip.Create((piano ? "piano" : "guitar") + k, n, 1, rate, false);
                list[k].SetData(d, 0);
            }
            return list;
        }

        /// <summary>Ступень лада → полутоны (лад шире у тех, кто умеет больше).</summary>
        static int Deg(int d, int[] scale)
        {
            int n = scale.Length;
            int o = Mathf.FloorToInt(d / (float)n);
            return scale[((d % n) + n) % n] + 12 * o;
        }

        void Note(int semi, float vol, float delay = 0f) => pending.Add((clock + delay, semi, vol));

        void Instrument(Fox u)
        {
            float dtm = Time.deltaTime * Mathf.Clamp(Game.I.SpeedMul, 0.5f, 3f);
            clock += dtm;
            bool piano = furn.def.id == "piano";
            if (piano && pianoNotes == null) pianoNotes = Notes(true);
            if (!piano && guitarNotes == null) guitarNotes = Notes(false);
            var notes = piano ? pianoNotes : guitarNotes;
            float vol = Game.I.sound != null ? Game.I.sound.soundVolume : 0.7f;
            // отложенные ноты (пассажи, арпеджио) — точно по времени
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].at > clock) continue;
                int k = Mathf.Clamp(pending[i].semi + Low, 0, Span - 1);
                src.PlayOneShot(notes[k], vol * pending[i].vol);
                pending.RemoveAt(i);
            }
            if (src == null || u == null) { pending.Clear(); return; }
            noteT -= dtm;
            if (noteT > 0) return;
            int skill = u.SkillLevel(Skill.Music);
            var el = u.Element;
            float beat = el == Element.Fire ? 0.2f : el == Element.Air ? 0.3f : el == Element.Earth ? 0.34f : 0.45f;
            var scale = skill >= 5 ? Major : Penta;
            int top = scale.Length * 2;

            if (skill >= 9)
            {
                // виртуоз: пассажи, арпеджио, аккорды с басом, трели — без единой фальшивой ноты
                float r = Random.value;
                int d0 = noteIdx;
                if (r < 0.3f)
                {
                    int len = Random.Range(5, 9), dir = d0 > top / 2 ? -1 : 1;
                    float step = Mathf.Lerp(0.055f, 0.085f, beat / 0.45f);
                    for (int i = 0; i < len; i++) Note(Deg(d0 + dir * i, scale), 0.75f, i * step);
                    noteIdx = Mathf.Clamp(d0 + dir * (len - 1), 0, top);
                    noteT = len * step + beat * 0.5f;
                }
                else if (r < 0.55f)
                {
                    int[] arp = { 0, 2, 4, 7, 9, 7, 4, 2 };
                    float step = 0.075f;
                    for (int i = 0; i < arp.Length; i++) Note(Deg(d0 + arp[i], scale) - 12, 0.65f, i * step);
                    noteT = arp.Length * step + beat * 0.3f;
                }
                else if (r < 0.75f)
                {
                    Note(Deg(d0, scale), 0.8f); Note(Deg(d0 + 2, scale), 0.6f, 0.01f); Note(Deg(d0 + 4, scale), 0.6f, 0.02f);
                    if (piano) Note(Deg(d0, scale) - 24, 0.7f);
                    noteIdx = Mathf.Clamp(d0 + Random.Range(-2, 3), 0, top);
                    noteT = beat * 1.5f;
                }
                else if (r < 0.85f)
                {
                    for (int i = 0; i < 8; i++) Note(Deg(d0 + (i % 2), scale), 0.55f, i * 0.05f);
                    noteT = 0.4f + beat * 0.3f;
                }
                else
                {
                    // певучая мелодия с подголоском
                    noteIdx = Mathf.Clamp(d0 + Random.Range(-2, 3), 0, top);
                    Note(Deg(noteIdx, scale), 0.85f);
                    Note(Deg(noteIdx - 2, scale) - 12, 0.45f, beat * 0.5f);
                    noteT = beat;
                }
                return;
            }

            noteT = beat * (Random.value < 0.25f ? 2f : 1f) * (1f + Random.Range(-0.3f, 0.3f) * (1f - skill / 10f));
            if (Random.value < 0.12f - skill * 0.008f) return; // пауза
            // мелодия ходит по ступенькам, вода — плавно вниз, огонь — прыжками
            int stepMax = el == Element.Fire ? 3 : 2;
            noteIdx = Mathf.Clamp(noteIdx + Random.Range(-stepMax, stepMax + 1) - (el == Element.Water && Random.value < 0.3f ? 1 : 0), 0, top);
            int semi = Deg(noteIdx, scale);
            if (Random.value < 0.25f * Mathf.Max(0f, 1f - skill / 6f)) semi += Random.value < 0.5f ? 1 : -1; // новичок фальшивит
            Note(semi, 0.9f);
            // с опытом — аккомпанемент: бас, а потом и аккорды
            if (skill >= 3 && Random.value < 0.3f) Note(Deg(noteIdx - 4, scale) - 12, 0.5f);
            if (skill >= 6 && Random.value < 0.25f) { Note(Deg(noteIdx + 2, scale), 0.5f, 0.02f); Note(Deg(noteIdx + 4, scale), 0.45f, 0.04f); }
        }
    }
}
