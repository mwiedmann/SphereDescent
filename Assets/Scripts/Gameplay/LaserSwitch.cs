using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// The pad that turns a laser gate off. Pulses a red warning while the gate is on and turns green once
    /// the ball has rolled over it.
    /// </summary>
    public class LaserSwitch : MonoBehaviour
    {
        public LaserGate gate;
        public Renderer[] indicators;
        [Tooltip("This pad's own warning material; its glow is pulsed.")]
        public Material armed;
        public Material disarmed;
        public Color warningColor = new Color(1f, 0.04f, 0.02f);
        [Tooltip("Pulses per second.")]
        public float pulseRate = 1.4f;
        public float minGlow = 0.4f, maxGlow = 3f;

        void Update()
        {
            if (gate == null || !gate.Active || armed == null) return;
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseRate * Mathf.PI * 2f);
            armed.SetColor("_EmissionColor", warningColor * Mathf.Lerp(minGlow, maxGlow, t * t));
        }

        void OnTriggerEnter(Collider other)
        {
            if (gate == null || !gate.Active) return;
            if (other.attachedRigidbody == null || !other.attachedRigidbody.TryGetComponent(out BallController _)) return;
            gate.Deactivate();
            foreach (var r in indicators) if (r != null) r.sharedMaterial = disarmed;
        }
    }
}
