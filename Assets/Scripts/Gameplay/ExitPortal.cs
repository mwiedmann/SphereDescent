using UnityEngine;

namespace SphereDescent
{
    /// <summary>The goal on the core. Touching it completes the world.</summary>
    public class ExitPortal : MonoBehaviour
    {
        public Transform spinner;

        void Update()
        {
            if (spinner == null) return;
            spinner.Rotate(0f, 90f * Time.deltaTime, 0f, Space.Self);
            spinner.localPosition = new Vector3(0f, 1.8f + Mathf.Sin(Time.time * 2f) * 0.25f, 0f);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.attachedRigidbody != null && other.attachedRigidbody.GetComponent<BallController>() != null)
                GameManager.Instance?.OnExitReached();
        }
    }
}
