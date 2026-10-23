using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// The energy ball trapped in the middle of the core's walls: a white-hot sphere in a soft glow, bobbing
    /// and pulsing. Once every wall is down, rolling into it frees it; it thanks the player and then flies
    /// away, spiralling up and out of the world with a glowing trail.
    /// </summary>
    public class EnergyOrb : MonoBehaviour
    {
        public Transform body;
        [Tooltip("A flat glow that turns to face the camera.")]
        public Transform halo;
        [Tooltip("This orb's own material, so its pulse doesn't affect anything else.")]
        public Material material;
        public Color color = new Color(0.45f, 0.95f, 1f);
        public float intensity = 5f;
        public float size = 1.6f;
        public float haloSize = 7f;
        [Tooltip("For the trail it leaves as it flies away.")]
        public Material trailMaterial;
        public CoreArena arena;

        public bool Freed { get; private set; }

        Vector3 rest;
        bool flying;
        float glowBoost = 1f;

        void Start() => rest = body.localPosition;

        void Update()
        {
            float t = Time.time;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * 0.8f);
            if (!flying) body.localPosition = rest + Vector3.up * Mathf.Sin(t * 1.3f) * 0.2f;
            body.localScale = Vector3.one * size * Mathf.Lerp(0.92f, 1.08f, pulse);
            if (material != null) material.SetColor("_EmissionColor", color * intensity * glowBoost * Mathf.Lerp(0.75f, 1.3f, pulse));
            var cam = Camera.main;
            if (halo != null && cam != null)
            {
                halo.position = body.position;
                halo.rotation = Quaternion.FromToRotation(Vector3.up, cam.transform.position - halo.position);
                halo.localScale = Vector3.one * haloSize * Mathf.Lerp(0.85f, 1.15f, pulse) * Mathf.Sqrt(glowBoost);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (Freed || (arena != null && !arena.AllBroken)) return;
            if (other.attachedRigidbody == null || !other.attachedRigidbody.TryGetComponent(out BallController ball) || ball.IsDestroyed) return;
            Freed = true;
            glowBoost = 1.6f;
            if (GameManager.Instance != null) GameManager.Instance.EnergyFreed(this);
        }

        /// <summary>Spirals up and away over <paramref name="seconds"/>, faster and faster, leaving a trail.</summary>
        public IEnumerator FlyAway(float seconds)
        {
            flying = true;
            Vector3 start = body.position, up = start.normalized;
            Vector3 side = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 other = Vector3.Cross(up, side);
            var trail = body.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.9f;
            trail.widthCurve = AnimationCurve.Linear(0f, size * 0.7f, 1f, 0f);
            trail.sharedMaterial = trailMaterial;
            trail.startColor = Color.Lerp(color, Color.white, 0.5f);
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                float spin = k * k * 900f + k * 300f;
                float wide = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.4f)) * 2.5f;
                body.position = start + up * (k * 6f + k * k * k * 90f) +
                                (side * Mathf.Cos(spin * Mathf.Deg2Rad) + other * Mathf.Sin(spin * Mathf.Deg2Rad)) * wide;
                yield return null;
            }
            body.gameObject.SetActive(false);
            if (halo != null) halo.gameObject.SetActive(false);
        }
    }
}
