using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// The pad that turns a laser gate off (or, on the core, breaks one of the walls around the trapped energy
    /// ball). Pulses a red warning until the ball rolls over it, then turns green and a glowing cable lights up
    /// along the floor from the pad to its target. The lasers go off when the cable reaches them. On the core
    /// the cable runs on to whichever wall is outermost of those still standing, and breaks it.
    /// </summary>
    public class LaserSwitch : MonoBehaviour
    {
        public LaserGate gate;
        [Tooltip("On the core: its walls, and how far along the cable each one is (innermost first).")]
        public CoreArena arena;
        public float[] wallDistances;
        [Tooltip("Points for rolling over it (key pads on the core score; laser switches don't).")]
        public int points;
        [Tooltip("The section (or the core) that gets those points.")]
        public int section;
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

        bool HasTarget => gate != null || arena != null;

        void Update()
        {
            if (!HasTarget || pressed || armed == null) return;
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseRate * Mathf.PI * 2f);
            armed.SetColor("_EmissionColor", warningColor * Mathf.Lerp(minGlow, maxGlow, t * t));
        }

        void OnTriggerEnter(Collider other)
        {
            if (!HasTarget || pressed) return;
            if (other.attachedRigidbody == null || !other.attachedRigidbody.TryGetComponent(out BallController ball) || ball.IsDestroyed) return;
            pressed = true;
            if (points > 0 && GameManager.Instance != null) GameManager.Instance.KeyPadPressed(this);
            foreach (var r in indicators) if (r != null) r.sharedMaterial = disarmed;
            // Run on the level, which is always on: the pad's chunk may be switched off as the ball follows the cable.
            MonoBehaviour host = GetComponentInParent<LevelInstance>();
            if (host == null) host = this;
            if (cable != null && cableTriangles != null && cableTriangles.Length > 0) host.StartCoroutine(LightCable());
            else Reached();
        }

        /// <summary>
        /// Draws the cable from the pad outward at a steady speed; once it has reached the walls the lasers
        /// come out of (or the wall it breaks), switches the gate off (or breaks the wall).
        /// </summary>
        IEnumerator LightCable()
        {
            int pieces = cableTriangles.Length / 6, shown = 0;
            for (float reach = 0f; ; reach += cableSpeed * Time.deltaTime)
            {
                // On the core it stops at the outermost wall still standing (another cable may break that one
                // first, and then it carries on in to the next).
                int wall = arena != null ? arena.OutermostStanding() : -1;
                float end = wall >= 0 ? wallDistances[wall] : float.MaxValue;
                while (shown < pieces && cableDistances[shown] <= Mathf.Min(reach, end)) shown++;
                cable.SetTriangles(cableTriangles, 0, shown * 6, 0, false);
                if (arena != null && wall < 0) yield break; // every wall is already down
                if (reach >= end) { arena.barriers[wall].Break(); yield break; }
                if (arena == null && shown == pieces) break;
                yield return null;
            }
            Reached();
        }

        void Reached()
        {
            if (gate != null) gate.Deactivate();
            if (arena != null)
            {
                int wall = arena.OutermostStanding();
                if (wall >= 0) arena.barriers[wall].Break();
            }
        }
    }
}
