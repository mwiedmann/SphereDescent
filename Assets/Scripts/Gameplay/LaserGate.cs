using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A grid of lasers filling a doorway. Touching it blows the ball up like a mine does. Its switch pad
    /// turns it off for good. Each laser is a white-hot core inside a soft colored glow, and the whole grid
    /// slowly pulses.
    /// </summary>
    public class LaserGate : MonoBehaviour
    {
        public GameObject lasers;
        public Collider field;
        [Tooltip("This gate's own materials, so its pulse doesn't affect other gates.")]
        public Material coreMaterial, glowMaterial;
        public Color coreColor = Color.white;
        [Tooltip("Seconds per pulse.")]
        public float pulsePeriod = 2.5f;
        public Vector2 coreGlow = new Vector2(3f, 4.5f);
        public Vector2 haloIntensity = new Vector2(1.2f, 1.9f);

        public bool Active { get; private set; } = true;

        void Update()
        {
            if (!Active) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / pulsePeriod);
            if (coreMaterial != null) coreMaterial.SetColor("_EmissionColor", coreColor * Mathf.Lerp(coreGlow.x, coreGlow.y, pulse));
            if (glowMaterial != null) glowMaterial.SetFloat("_Intensity", Mathf.Lerp(haloIntensity.x, haloIntensity.y, pulse));
        }

        public void Deactivate()
        {
            if (!Active) return;
            Active = false;
            if (lasers != null) lasers.SetActive(false);
            if (field != null) field.enabled = false;
        }

        void OnTriggerEnter(Collider other)
        {
            if (Active && other.attachedRigidbody != null && other.attachedRigidbody.TryGetComponent(out BallController ball))
                ball.HitHazard();
        }
    }
}
