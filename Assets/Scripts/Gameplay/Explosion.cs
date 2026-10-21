using UnityEngine;

namespace SphereDescent
{
    /// <summary>A quick glowing fireball that swells and fades, then removes itself.</summary>
    public class Explosion : MonoBehaviour
    {
        public float duration = 0.6f;
        public float maxSize = 5f;

        Material material;
        float intensity;
        float age;

        /// <param name="glow">An additive glow material (the light beam's); a copy is tinted and faded.</param>
        /// <param name="size">How wide it swells.</param>
        public static void Spawn(Vector3 position, Material glow, Color color, float size = 5f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Explosion";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = position;
            var e = go.AddComponent<Explosion>();
            e.maxSize = size;
            e.material = new Material(glow);
            e.material.SetColor("_Color", color);
            e.material.SetFloat("_FadeStart", 1f);
            e.intensity = e.material.GetFloat("_Intensity") * 3f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = e.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            e.Tick(0f);
        }

        void Update() => Tick(Time.deltaTime);

        void Tick(float dt)
        {
            age += dt;
            float t = Mathf.Clamp01(age / duration);
            // Bursts out fast, then slows; brightness falls away as it grows.
            transform.localScale = Vector3.one * Mathf.Lerp(0.5f, maxSize, 1f - (1f - t) * (1f - t));
            material.SetFloat("_Intensity", intensity * (1f - t) * (1f - t));
            if (t >= 1f)
            {
                Destroy(material);
                Destroy(gameObject);
            }
        }
    }
}
