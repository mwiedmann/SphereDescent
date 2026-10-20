using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A low toothed disc on the ground with a blinking red light that throws a red glow around it. Touching
    /// it blows the ball up and sends it back to its checkpoint (where it last arrived on this layer). Jump
    /// over it or roll around it.
    /// </summary>
    public class Mine : MonoBehaviour
    {
        public Renderer lightRenderer;
        public Material lightOn, lightOff;
        public Renderer glowRenderer;
        public Material glowOn, glowOff;
        [Tooltip("Seconds between blinks.")]
        public float blinkPeriod = 0.9f;
        [Tooltip("How long each blink stays lit.")]
        public float blinkLength = 0.12f;

        float phase;
        bool lit;

        void Awake() => phase = Random.value * blinkPeriod;

        void Update()
        {
            if (lightRenderer == null) return;
            // Swaps materials rather than toggling the renderer, which layer hiding owns.
            bool on = Mathf.Repeat(Time.time + phase, blinkPeriod) < blinkLength;
            if (on == lit) return;
            lit = on;
            lightRenderer.sharedMaterial = on ? lightOn : lightOff;
            if (glowRenderer != null) glowRenderer.sharedMaterial = on ? glowOn : glowOff;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.attachedRigidbody != null && other.attachedRigidbody.TryGetComponent(out BallController ball))
                ball.HitMine(transform.position);
        }
    }
}
