using UnityEngine;

namespace FoxLife
{
    /// <summary>Костёр: языки пламени, искры, дымок и мерцающий свет.</summary>
    public class CampfireFX : MonoBehaviour
    {
        public Light fireLight;
        float baseIntensity = 1.6f;

        void Start()
        {
            var mat = new Material(Shader.Find("FoxLife/Particle"));
            Make("flames", mat, 0.55f, 1.2f, 0.28f, 60, new Color(1f, 0.75f, 0.25f, 0.9f), new Color(1f, 0.3f, 0.05f, 0f), 0.15f, false);
            Make("sparks", mat, 1.4f, 2.4f, 0.05f, 14, new Color(1f, 0.9f, 0.5f, 1f), new Color(1f, 0.4f, 0.1f, 0f), 0.25f, true);
            Make("smoke", mat, 3.5f, 0.6f, 0.45f, 6, new Color(0.75f, 0.75f, 0.78f, 0.35f), new Color(0.85f, 0.85f, 0.9f, 0f), 0.2f, false, 1.8f);
            baseIntensity = 1.6f * Props.LightK; // как у костра в каталоге, с поправкой на URP
        }

        void Make(string name, Material mat, float life, float speed, float size, float rate, Color c0, Color c1, float radius, bool noisy, float grow = 0.2f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 0.25f, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = noisy ? 25f : 8f; shape.radius = radius;
            shape.rotation = new Vector3(-90, 0, 0);
            var em = ps.emission; em.rateOverTime = rate;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(c0, 0), new GradientColorKey(c1, 1) }, new[] { new GradientAlphaKey(c0.a, 0), new GradientAlphaKey(c1.a, 1) });
            col.color = gr;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, grow)));
            if (noisy)
            {
                var n = ps.noise; n.enabled = true; n.strength = 0.6f; n.frequency = 1.5f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        void Update()
        {
            if (fireLight == null) return;
            float t = Time.time;
            fireLight.intensity = baseIntensity * (0.8f + 0.15f * Mathf.Sin(t * 13f) + 0.1f * Mathf.Sin(t * 7.3f + 1f) + 0.08f * Mathf.PerlinNoise(t * 3f, 0));
        }
    }
}
