using UnityEngine;

namespace FoxLife
{
    /// <summary>Брызги фонтана и пена в ванне.</summary>
    public class WaterFX : MonoBehaviour
    {
        public Furniture furn;
        ParticleSystem foam, bubbles;
        static Material mat;

        public static void Attach(Furniture f)
        {
            if (f.def.id != "fountain" && f.def.id != "bath" && f.def.id != "birdbath") return;
            f.model.gameObject.AddComponent<WaterFX>().furn = f;
        }

        void Start()
        {
            if (mat == null) mat = new Material(Shader.Find("FoxLife/Particle"));
            switch (furn.def.id)
            {
                case "fountain":
                    // струйки с верхней чаши и брызги в нижней
                    Spray(new Vector3(0, 1.82f, 0), 1.5f, 0.06f, 70, 1.1f, 18f, new Color(0.85f, 0.95f, 1f, 0.85f));
                    Spray(new Vector3(0, 1.25f, 0), 0.6f, 0.05f, 40, 0.9f, 70f, new Color(0.8f, 0.92f, 1f, 0.7f), 0.38f);
                    Ring(new Vector3(0, 0.45f, 0), 0.6f, 14, new Color(1f, 1f, 1f, 0.55f));
                    break;
                case "birdbath":
                    Ring(new Vector3(0, 0.82f, 0), 0.15f, 3, new Color(1f, 1f, 1f, 0.5f));
                    break;
                case "bath":
                    foam = Blobs(new Vector3(0, 0.63f, 0), new Vector3(0.6f, 0.02f, 1.4f), 26, new Color(1f, 1f, 1f, 0.95f), 0.16f, 2.6f);
                    bubbles = Blobs(new Vector3(0, 0.66f, 0), new Vector3(0.55f, 0.02f, 1.3f), 10, new Color(0.85f, 0.95f, 1f, 0.7f), 0.07f, 1.4f, 0.35f);
                    foam.Stop(); bubbles.Stop();
                    break;
            }
        }

        ParticleSystem Base(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            return ps;
        }

        void Spray(Vector3 pos, float speed, float size, float rate, float life, float angle, Color c, float radius = 0.05f)
        {
            var ps = Base("брызги", pos);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.gravityModifier = 0.45f;
            main.startColor = c;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = angle; shape.radius = radius;
            shape.rotation = new Vector3(-90, 0, 0);
            var em = ps.emission; em.rateOverTime = rate;
            ps.Play();
        }

        void Ring(Vector3 pos, float radius, float rate, Color c)
        {
            var ps = Base("рябь", pos);
            var main = ps.main;
            main.startLifetime = 0.6f;
            main.startSpeed = 0.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = c;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = radius; shape.rotation = new Vector3(-90, 0, 0);
            var em = ps.emission; em.rateOverTime = rate;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(c.a, 0), new GradientAlphaKey(0, 1) });
            col.color = gr;
            ps.Play();
        }

        ParticleSystem Blobs(Vector3 pos, Vector3 box, float rate, Color c, float size, float life, float rise = 0.04f)
        {
            var ps = Base("пена", pos);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(rise * 0.3f, rise);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = c;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box; shape.rotation = new Vector3(-90, 0, 0);
            var em = ps.emission; em.rateOverTime = rate;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.3f), new Keyframe(0.2f, 1f), new Keyframe(1, 0.6f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(c.a, 0), new GradientAlphaKey(c.a, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            return ps;
        }

        void Update()
        {
            if (foam == null || furn == null) return;
            bool on = furn.inUse > 0;
            if (on && !foam.isEmitting) { foam.Play(); bubbles.Play(); }
            else if (!on && foam.isEmitting) { foam.Stop(); bubbles.Stop(); }
        }
    }
}
