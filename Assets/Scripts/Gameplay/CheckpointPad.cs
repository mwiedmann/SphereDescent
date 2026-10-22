using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A large pad along a section (halfway, and on the outer spheres also a quarter and three quarters of the
    /// way). Dim white until the ball rolls over it; then it lights up green with a quick pulse and becomes where
    /// the ball comes back to if it's destroyed on this layer, unless it has already reached a pad further on.
    /// </summary>
    public class CheckpointPad : MonoBehaviour
    {
        public Renderer[] indicators;
        public Material inactive, active;
        public Transform pulseRing;
        [Tooltip("How far along the world it is; a pad only takes over from ones before it.")]
        public int order;

        public bool Reached { get; private set; }

        float pulse;

        void Update()
        {
            if (pulseRing == null) return;
            // Waiting: a slow, faint breathing. Reached: one big ring burst outward, then steady.
            if (!Reached)
            {
                float s = 1f + 0.04f * Mathf.Sin(Time.time * 2.2f);
                pulseRing.localScale = new Vector3(s, 1f, s);
            }
            else if (pulse < 1f)
            {
                pulse = Mathf.MoveTowards(pulse, 1f, Time.deltaTime * 1.6f);
                float s = 1f + 0.6f * Mathf.Sin(pulse * Mathf.PI);
                pulseRing.localScale = new Vector3(s, 1f, s);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (Reached) return;
            if (other.attachedRigidbody == null || !other.attachedRigidbody.TryGetComponent(out BallController ball) || ball.IsDestroyed) return;
            Reached = true;
            foreach (var r in indicators) if (r != null) r.sharedMaterial = active;
            ball.SetCheckpoint(transform.position + transform.up * 0.5f, order);
            if (GameManager.Instance != null) GameManager.Instance.ShowMessage("Checkpoint!", 1.6f);
        }
    }
}
