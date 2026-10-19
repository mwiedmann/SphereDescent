using UnityEngine;

namespace SphereDescent
{
    /// <summary>Sits beneath a two-way hole. Jumping while on it launches the ball up to the layer above.</summary>
    public class BoostPad : MonoBehaviour
    {
        [HideInInspector] public Vector3 Axis;               // unit direction of the hole above
        [HideInInspector] public float TargetSurfaceRadius;  // outer surface of the shell above
        [HideInInspector] public Vector3 ExitTangent;        // which way to land after clearing the hole
        [HideInInspector] public float LandAngle;            // radians from the hole center to the landing spot
        public Transform pulseVisual;

        float pulse;

        void OnTriggerStay(Collider other)
        {
            if (other.attachedRigidbody != null && other.attachedRigidbody.TryGetComponent(out BallController ball))
                ball.SetPad(this);
        }

        public void OnLaunched() => pulse = 1f;

        void Update()
        {
            if (pulseVisual == null) return;
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 2f);
            float s = 1f + 0.08f * Mathf.Sin(Time.time * 4f) + pulse * 0.5f;
            pulseVisual.localScale = new Vector3(s, 1f, s);
        }
    }
}
