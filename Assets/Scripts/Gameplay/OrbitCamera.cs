using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// Third-person orbit camera whose "up" is always away from the world center. Its heading is carried
    /// along (parallel-transported) as the ball moves around the sphere, so the view never flips at the poles.
    /// Pulls in when walls or ceilings get in the way.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 8.5f;
        public float minDistance = 1.2f;
        public float focusHeight = 1f;
        public float pitch = 32f;
        public float minPitch = -5f;
        public float maxPitch = 75f;
        public float mouseSensitivity = 2.5f;
        [Tooltip("Degrees per second for Q/E and the gamepad right stick.")]
        public float keyTurnSpeed = 140f;
        public float collisionRadius = 0.3f;
        public LayerMask collisionMask = ~(1 << 2); // everything except Ignore Raycast (the ball)
        public float upSmoothing = 10f;

        [Header("Follow movement")]
        [Tooltip("Swing around behind the direction the ball is moving.")]
        public bool followMovement = true;
        [Tooltip("Below this speed the camera holds its heading, so it doesn't spin when the ball stops or wobbles.")]
        public float followMinSpeed = 1.5f;
        [Tooltip("How quickly the camera swings around behind the ball. Scales up with speed.")]
        public float followSharpness = 4f;

        Vector3 up = Vector3.up;
        Vector3 forward = Vector3.forward;
        float currentDistance;
        Rigidbody targetBody;

        public Vector3 Forward => forward;

        void Start() => Snap();

        /// <summary>Re-orients instantly behind the target, e.g. after a teleport.</summary>
        public void Snap()
        {
            if (target == null) return;
            up = target.position.normalized;
            forward = Vector3.ProjectOnPlane(transform.forward, up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(Vector3.right, up);
            forward.Normalize();
            currentDistance = distance;
            LateUpdate();
        }

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 targetPos = target.position;
            Vector3 targetUp = targetPos.sqrMagnitude > 1e-6f ? targetPos.normalized : Vector3.up;

            // Smoothly follow the local up and carry the heading along with it.
            Vector3 newUp = Vector3.Slerp(up, targetUp, 1f - Mathf.Exp(-upSmoothing * Time.deltaTime));
            forward = Quaternion.FromToRotation(up, newUp) * forward;
            up = newUp;
            forward = Vector3.ProjectOnPlane(forward, up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(transform.up, up);
            forward.Normalize();

            bool mouseActive = Cursor.lockState == CursorLockMode.Locked;
            Vector2 look = GameInput.Look(mouseActive, mouseSensitivity, keyTurnSpeed);
            forward = Quaternion.AngleAxis(look.x, up) * forward;
            if (followMovement) FollowMovement();
            pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);

            Vector3 right = Vector3.Cross(up, forward);
            Vector3 lookDir = Quaternion.AngleAxis(pitch, right) * forward;
            Vector3 focus = targetPos + up * focusHeight;

            float wanted = distance;
            if (Physics.SphereCast(focus, collisionRadius, -lookDir, out RaycastHit hit, distance,
                    collisionMask, QueryTriggerInteraction.Ignore))
            {
                wanted = Mathf.Max(minDistance, hit.distance - 0.1f);
            }
            // Pull in instantly, ease back out.
            currentDistance = wanted < currentDistance ? wanted : Mathf.Lerp(currentDistance, wanted, 1f - Mathf.Exp(-4f * Time.deltaTime));

            transform.SetPositionAndRotation(focus - lookDir * currentDistance, Quaternion.LookRotation(lookDir, up));
        }

        /// <summary>Turns the heading toward the ball's direction of travel along the surface.</summary>
        void FollowMovement()
        {
            if (targetBody == null || targetBody.transform != target) targetBody = target.GetComponent<Rigidbody>();
            if (targetBody == null) return;
            Vector3 moving = Vector3.ProjectOnPlane(targetBody.linearVelocity, up);
            float speed = moving.magnitude;
            if (speed < followMinSpeed) return;
            // Faster movement swings the camera around more decisively.
            float rate = followSharpness * Mathf.Clamp(speed / 6f, 0.5f, 2f);
            forward = Vector3.Slerp(forward, moving / speed, 1f - Mathf.Exp(-rate * Time.deltaTime));
            forward = Vector3.ProjectOnPlane(forward, up).normalized;
        }
    }
}
