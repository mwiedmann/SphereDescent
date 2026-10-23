using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// One of the round glowing walls around the core's trapped energy ball. It pulses slowly until its key
    /// pad's cable reaches it; then it flickers, bursts in a ring of sparks and small fireballs, and is gone.
    /// </summary>
    public class CoreBarrier : MonoBehaviour
    {
        [Tooltip("The wall itself (its look and its collider), switched off once it breaks.")]
        public GameObject wall;
        [Tooltip("This wall's own material, so its pulse doesn't affect the others.")]
        public Material material;
        public Color color = new Color(1f, 0.2f, 0.8f);
        public float intensity = 2.4f;
        [Tooltip("Where in the pulse it starts, so the rings ripple outward.")]
        public float phase;
        public float ringRadius = 3f;
        public float height = 3f;
        [Tooltip("An additive glow material for the fireballs (the light beam's).")]
        public Material glow;
        public Material sparkMaterial;

        public bool Broken { get; private set; }
        /// <summary>Breaking or broken: a cable has reached it.</summary>
        public bool Breaking => breaking;
        public event System.Action<CoreBarrier> Broke;

        bool breaking;

        void Update()
        {
            if (breaking || material == null) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f - phase);
            material.SetColor("_EmissionColor", color * intensity * Mathf.Lerp(0.7f, 1.25f, pulse));
        }

        public void Break()
        {
            if (breaking) return;
            breaking = true;
            StartCoroutine(Shatter());
        }

        IEnumerator Shatter()
        {
            // A moment of crackling flicker as the power surges in.
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                if (material != null) material.SetColor("_EmissionColor", color * (Random.value < 0.5f ? 0.3f : 6f));
                yield return null;
            }
            wall.SetActive(false);
            Vector3 up = transform.up;
            Vector3 side = transform.right;
            int bursts = Mathf.Max(6, Mathf.RoundToInt(ringRadius * 2.5f));
            if (glow != null)
                for (int i = 0; i < bursts; i++)
                {
                    Vector3 at = transform.position + Quaternion.AngleAxis(360f * i / bursts, up) * side * ringRadius
                                 + up * height * Random.Range(0.25f, 0.8f);
                    Explosion.Spawn(at, glow, Color.Lerp(color, Color.white, 0.35f), Random.Range(1.6f, 2.6f));
                }
            Sparks();
            Broken = true;
            Broke?.Invoke(this);
        }

        /// <summary>A burst of bright sparks flying out from all round the wall, slowing and fading.</summary>
        void Sparks()
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * height * 0.5f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, Color.Lerp(color, Color.white, 0.4f));
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Min(400, 50 + ringRadius * 40f)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = ringRadius;
            shape.radiusThickness = 0f; // from the wall itself
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.randomDirectionAmount = 0.6f;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1.5f;
            limit.dampen = 0.12f;
            var overLife = ps.colorOverLifetime;
            overLife.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            overLife.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.5f;
            renderer.sharedMaterial = sparkMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            Destroy(go, 2f);
        }
    }
}
