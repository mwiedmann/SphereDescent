using System.Collections.Generic;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A blob of lava falling straight down onto the core, faster and faster, wobbling as it goes. Beneath it a
    /// dark shadow spreads over the ground, ringed in red, growing and pulsing quicker the nearer it gets. When
    /// it lands it splats: a burst of glow and a handful of droplets that fly out, fall back and shrink away.
    /// </summary>
    public class LavaBlob : MonoBehaviour
    {
        LavaRain rain;
        Vector3 to, up;
        float age, height;
        Transform shadow, rim;
        Mesh discMesh;
        Material shadowMat, rimMat;
        float rimIntensity;

        const float Size = 1.3f;

        public static LavaBlob Drop(LavaRain rain, Vector3 to, Vector3 ground)
        {
            var go = new GameObject("Lava Blob");
            go.transform.SetParent(rain.transform, true);
            var blob = go.AddComponent<LavaBlob>();
            blob.rain = rain;
            blob.to = to;
            blob.up = to.normalized;
            blob.height = 30f;
            var body = Piece(go.transform, "Body", rain.blobMesh, rain.blobMaterial);
            body.localScale = Vector3.one * Size;

            // The shadow and its rim lie on the ground, as wide as the splash.
            blob.discMesh = MortarBomb.DrapedDisc(rain.GroundHit, to, ground, rain.splashRadius + 0.4f);
            blob.shadowMat = new Material(rain.shadowMaterial);
            blob.shadow = Piece(rain.transform, "Blob Shadow", blob.discMesh, blob.shadowMat);
            blob.shadow.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            blob.rimMat = new Material(rain.rimMaterial);
            blob.rimIntensity = blob.rimMat.GetFloat("_Intensity");
            blob.rim = Piece(rain.transform, "Blob Warning", blob.discMesh, blob.rimMat);
            blob.rim.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            blob.Tick(0f);
            return blob;
        }

        static Transform Piece(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void Update() => Tick(Time.deltaTime);

        void Tick(float dt)
        {
            age += dt;
            float s = Mathf.Clamp01(age / Mathf.Max(0.1f, rain.fallTime));
            // Falling under gravity: slow at first, fast at the end.
            transform.position = to + up * (height * (1f - s * s));
            float wobble = Mathf.Sin(age * 18f) * 0.12f;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            transform.localScale = new Vector3(1f + wobble, 1f + s * 0.6f - wobble, 1f + wobble); // stretched by the fall

            // The shadow darkens as it nears; the red rim pulses faster.
            if (shadowMat != null)
            {
                Color c = shadowMat.color;
                c.a = Mathf.Lerp(0.15f, 0.8f, s);
                shadowMat.color = c;
            }
            if (rimMat != null)
            {
                float pulse = Mathf.Repeat(age * Mathf.Lerp(2f, 10f, s * s), 1f) < 0.5f ? 1f : 0.35f;
                rimMat.SetFloat("_Intensity", rimIntensity * Mathf.Lerp(0.4f, 1f, s) * pulse);
            }

            if (s >= 1f)
            {
                rain.Impact(to);
                Splat();
                Destroy(gameObject);
            }
        }

        /// <summary>Droplets flung out from the landing spot that arc back down and shrink away.</summary>
        void Splat()
        {
            int count = Random.Range(6, 9);
            for (int i = 0; i < count; i++)
            {
                var drop = Piece(rain.transform, "Lava Droplet", rain.blobMesh, rain.blobMaterial);
                drop.position = to + up * 0.3f;
                drop.localScale = Vector3.one * Random.Range(0.3f, 0.55f);
                Vector3 side = Vector3.ProjectOnPlane(Random.onUnitSphere, up).normalized;
                drop.gameObject.AddComponent<LavaDroplet>().velocity = side * Random.Range(2.5f, 5f) + up * Random.Range(3f, 6f);
            }
        }

        void OnDestroy()
        {
            if (shadow != null) Destroy(shadow.gameObject);
            if (rim != null) Destroy(rim.gameObject);
            if (shadowMat != null) Destroy(shadowMat);
            if (rimMat != null) Destroy(rimMat);
            if (discMesh != null) Destroy(discMesh);
        }
    }

    /// <summary>A harmless splash of lava: flies off, falls back toward the core and shrinks away to nothing.</summary>
    public class LavaDroplet : MonoBehaviour
    {
        public Vector3 velocity;
        public float life = 1.1f;

        float age;
        Vector3 startScale;

        void Start() => startScale = transform.localScale;

        void Update()
        {
            age += Time.deltaTime;
            velocity -= transform.position.normalized * (25f * Time.deltaTime);
            transform.position += velocity * Time.deltaTime;
            transform.localScale = startScale * Mathf.Clamp01(1f - age / life);
            if (age >= life) Destroy(gameObject);
        }
    }
}
