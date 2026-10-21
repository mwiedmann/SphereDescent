using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// The pad that turns a laser gate off. Pulses a red warning until the ball rolls over it, then turns
    /// green and a glowing cable lights up along the floor from the pad to the gate. The lasers go off when
    /// the cable reaches the walls they come out of.
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
        [Tooltip("The hidden cable to the gate: its mesh, its triangles in order from the pad to the gate (six per " +
                 "piece), and how far along the cable each piece ends.")]
        public Mesh cable;
        public int[] cableTriangles;
        public float[] cableDistances;
        [Tooltip("How fast the cable lights up along the floor, in units per second (the ball's top speed is 11).")]
        public float cableSpeed = 9f;

        bool pressed;

        void Update()
        {
            if (gate == null || pressed || armed == null) return;
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseRate * Mathf.PI * 2f);
            armed.SetColor("_EmissionColor", warningColor * Mathf.Lerp(minGlow, maxGlow, t * t));
        }

        void OnTriggerEnter(Collider other)
        {
            if (gate == null || pressed) return;
            if (other.attachedRigidbody == null || !other.attachedRigidbody.TryGetComponent(out BallController _)) return;
            pressed = true;
            foreach (var r in indicators) if (r != null) r.sharedMaterial = disarmed;
            // Run on the level, which is always on: the pad's chunk may be switched off as the ball follows the cable.
            MonoBehaviour host = GetComponentInParent<LevelInstance>();
            if (host == null) host = this;
            if (cable != null && cableTriangles != null && cableTriangles.Length > 0) host.StartCoroutine(LightCable());
            else gate.Deactivate();
        }

        /// <summary>
        /// Draws the cable from the pad outward at a steady speed; once it has reached the walls the lasers
        /// come out of, switches the gate off.
        /// </summary>
        IEnumerator LightCable()
        {
            int pieces = cableTriangles.Length / 6, shown = 0;
            for (float reach = 0f; shown < pieces; reach += cableSpeed * Time.deltaTime)
            {
                while (shown < pieces && cableDistances[shown] <= reach) shown++;
                cable.SetTriangles(cableTriangles, 0, shown * 6, 0, false);
                yield return null;
            }
            gate.Deactivate();
        }
    }
}
