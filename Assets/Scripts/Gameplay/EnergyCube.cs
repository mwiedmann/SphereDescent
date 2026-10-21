using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A glowing cube floating above the ground, bobbing and slowly spinning. Rolling through it collects it
    /// for points; it's gone for the rest of the world.
    /// </summary>
    public class EnergyCube : MonoBehaviour
    {
        public Transform visual;
        [Tooltip("A flat glow that turns to face the camera.")]
        public Transform halo;
        public int points = 1;
        public bool isSuper;
        public float spinSpeed = 45f;
        public float bobHeight = 0.12f;
        public float bobSpeed = 1.6f;

        float phase;
        Vector3 restPosition;

        void Start()
        {
            phase = Random.value * 10f;
            restPosition = visual.localPosition;
        }

        void Update()
        {
            float t = Time.time + phase;
            visual.localPosition = restPosition + Vector3.up * (Mathf.Sin(t * bobSpeed) * bobHeight);
            // Tumbling slowly on a tilted axis looks livelier than a flat turn.
            visual.localRotation = Quaternion.Euler(35f, t * spinSpeed, 20f + t * spinSpeed * 0.3f);
            FaceCamera(Camera.main);
        }

        /// <summary>Turns the glow to face the camera (the disc faces its local up).</summary>
        public void FaceCamera(Camera cam)
        {
            if (halo == null || cam == null) return;
            halo.position = visual.position;
            halo.rotation = Quaternion.FromToRotation(Vector3.up, cam.transform.position - halo.position);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.attachedRigidbody == null || !other.attachedRigidbody.TryGetComponent(out BallController ball) || ball.IsDestroyed) return;
            gameObject.SetActive(false);
            if (GameManager.Instance != null) GameManager.Instance.CollectCube(this);
        }
    }
}
