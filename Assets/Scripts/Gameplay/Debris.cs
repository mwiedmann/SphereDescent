using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A harmless bit of something that burst (a splash of lava, a piece of the ball): flies off, falls back
    /// toward the world's center, tumbles, and shrinks away to nothing.
    /// </summary>
    public class Debris : MonoBehaviour
    {
        public Vector3 velocity;
        public Vector3 spin;      // degrees per second about each axis
        public float life = 1.1f;
        public float gravity = 25f;

        float age;
        Vector3 startScale;

        void Start() => startScale = transform.localScale;

        void Update()
        {
            age += Time.deltaTime;
            velocity -= transform.position.normalized * (gravity * Time.deltaTime);
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(spin * Time.deltaTime, Space.Self);
            transform.localScale = startScale * Mathf.Clamp01(1f - age / life);
            if (age >= life) Destroy(gameObject);
        }
    }
}
