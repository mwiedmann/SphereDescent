using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// Physics-driven rolling ball on spherical worlds. Gravity always points at the world center, so the
    /// ball sticks to whichever shell surface it is on and falls inward through holes. Movement is relative
    /// to the camera; the ball rolls via torque and gets limited steering in the air (it can't jump, but it
    /// can be in the air after a drop, a launch or rolling fast over a hill). Touching a boost pad
    /// launches it straight up through the hole above and sets it down beside the hole.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BallController : MonoBehaviour
    {
        [Header("Movement")]
        public float gravity = 25f;
        public float maxGroundSpeed = 11f;
        [Tooltip("How sharply the ball steers toward the requested velocity on the ground (1/s). Higher = snappier.")]
        public float groundResponse = 7f;
        [Tooltip("Most acceleration the controls can apply on the ground, including turning and reversing.")]
        public float groundAcceleration = 35f;
        [Tooltip("Most angular acceleration used to keep the ball's spin matching where it's going.")]
        public float rollTorque = 120f;
        public float maxAirSpeed = 8f;
        [Tooltip("How sharply the ball steers toward the requested direction in the air (1/s).")]
        public float airResponse = 4f;
        public float airAcceleration = 14f;
        [Tooltip("Slows the ball when there is no input on the ground.")]
        public float brake = 1.5f;

        [Header("Boost pads")]
        [Tooltip("How far above the upper surface a boost-pad launch peaks.")]
        public float launchClearance = 2.5f;

        [Header("Mines")]
        [Tooltip("Seconds between blowing up on a mine and reappearing at the checkpoint.")]
        public float respawnDelay = 1.1f;

        [Header("References")]
        public Transform cameraTransform;
        public LevelInstance level;

        public float Radius { get; private set; }
        public int CurrentLayer { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsLaunching => launchPad != null;
        /// <summary>Launched from a boost pad.</summary>
        public event System.Action<BoostPad> Launched;
        /// <summary>Fell through a hole: the layer it came from, and where it crossed.</summary>
        public event System.Action<int, Vector3> DroppedThrough;
        /// <summary>True while blown up and waiting to reappear.</summary>
        public bool IsDestroyed => exploding;
        public bool OnPad => pad != null && Time.time - padSeenAt < 0.15f;
        /// <summary>True while the player is pushing back (down), e.g. to back out of a dead end.</summary>
        public bool PushingBack => moveInput.y < -0.5f;
        /// <summary>True while the player is steering at all.</summary>
        public bool Steering => moveInput.sqrMagnitude > 0.01f;

        Rigidbody rb;
        Vector2 moveInput;
        bool touchingGround;
        BoostPad pad;
        float padSeenAt = -10f;
        BoostPad launchPad;
        bool dropping;
        bool exploding;
        Vector3 checkpoint;
        int checkpointLayer = -1;
        int checkpointOrder = -1; // of the furthest checkpoint pad reached in this world
        bool frozen;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.angularDamping = 0.2f;
            Radius = GetComponent<SphereCollider>().radius * transform.lossyScale.x;
            // Rolling speed is naturally limited by spin; launches set linear velocity directly.
            rb.maxAngularVelocity = maxGroundSpeed / Radius * 1.15f;
        }

        void Update()
        {
            if (frozen) { moveInput = Vector2.zero; return; }
            moveInput = GameInput.Move();
            if (GameInput.RespawnPressed() && !exploding) RespawnAtCheckpoint();
        }

        void FixedUpdate()
        {
            if (exploding) return;
            Vector3 pos = rb.position;
            float r = pos.magnitude;
            Vector3 up = r > 1e-4f ? pos / r : Vector3.up;

            IsGrounded = touchingGround && launchPad == null;
            touchingGround = false;
            int previousLayer = CurrentLayer;
            if (level != null) CurrentLayer = level.LayerAt(r);
            // Fell through a hole: drop straight down from here on, so momentum can't carry the ball over a
            // wall into a sealed-off part of the maze below.
            if (CurrentLayer > previousLayer && launchPad == null)
            {
                dropping = true;
                DroppedThrough?.Invoke(previousLayer, pos);
            }
            if (IsGrounded)
            {
                dropping = false;
                if (CurrentLayer != checkpointLayer)
                {
                    checkpoint = pos;
                    checkpointLayer = CurrentLayer;
                    LandedOnNewLayer?.Invoke();
                }
            }

            rb.AddForce(-up * gravity, ForceMode.Acceleration);
            if (frozen)
            {
                // Held where it is (it may still settle onto the ground), not rolling on.
                rb.linearVelocity = up * Mathf.Min(0f, Vector3.Dot(rb.linearVelocity, up));
                rb.angularVelocity = Vector3.zero;
                return;
            }

            if (level != null && r > level.OuterRadius + 60f) { RespawnAtCheckpoint(); return; }
            if (launchPad != null) { GuideLaunch(pos, up, r); return; }
            if (dropping)
            {
                rb.linearVelocity = up * Vector3.Dot(rb.linearVelocity, up);
                rb.angularVelocity = Vector3.zero;
                return;
            }
            // Touching a boost pad launches straight away.
            if (OnPad)
            {
                StartLaunch(pad, up, r);
                return;
            }

            Vector3 move = MoveDirection(up);
            Vector3 tangentVel = Vector3.ProjectOnPlane(rb.linearVelocity, up);

            if (IsGrounded)
            {
                if (move.sqrMagnitude > 0.0001f)
                {
                    // Steer toward the requested velocity: this both speeds the ball up and cancels drift in
                    // other directions, so turns and reversals take effect quickly.
                    Vector3 target = move * maxGroundSpeed;
                    rb.AddForce(Vector3.ClampMagnitude((target - tangentVel) * groundResponse, groundAcceleration), ForceMode.Acceleration);
                    // Spin the ball to roll at that velocity too, or its existing spin keeps carrying it the
                    // old way through friction.
                    Vector3 targetSpin = Vector3.Cross(up, target) / Radius;
                    Vector3 spinError = Vector3.ProjectOnPlane(targetSpin - rb.angularVelocity, up);
                    rb.AddTorque(Vector3.ClampMagnitude(spinError * groundResponse, rollTorque), ForceMode.Acceleration);
                }
                else
                {
                    rb.AddForce(-tangentVel * brake, ForceMode.Acceleration);
                }
            }
            else if (move.sqrMagnitude > 0.0001f)
            {
                // Same steering in the air, gentler, and never slowing a fast flight in the direction you're
                // already pushing.
                Vector3 dir = move.normalized;
                Vector3 change = move * maxAirSpeed - tangentVel;
                float along = Vector3.Dot(change, dir);
                if (along < 0f) change -= dir * along;
                rb.AddForce(Vector3.ClampMagnitude(change * airResponse, airAcceleration), ForceMode.Acceleration);
            }
        }

        Vector3 MoveDirection(Vector3 up)
        {
            if (moveInput.sqrMagnitude < 0.0001f || cameraTransform == null) return Vector3.zero;
            Vector3 fwd = Vector3.ProjectOnPlane(cameraTransform.forward, up);
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(cameraTransform.up, up);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(up, fwd);
            return Vector3.ClampMagnitude(fwd * moveInput.y + right * moveInput.x, 1f);
        }

        void StartLaunch(BoostPad boost, Vector3 up, float r)
        {
            launchPad = boost;
            float rise = Mathf.Max(1f, boost.TargetSurfaceRadius + launchClearance - r);
            rb.linearVelocity = up * Mathf.Sqrt(2f * gravity * rise);
            rb.angularVelocity *= 0.3f;
            boost.OnLaunched();
            Launched?.Invoke(boost);
        }

        /// <summary>
        /// While below the upper shell, keep the ball centered on the hole's axis. Once clear of it, aim the
        /// sideways velocity so the ball comes down on solid ground next to the hole, then hand control back.
        /// </summary>
        void GuideLaunch(Vector3 pos, Vector3 up, float r)
        {
            Vector3 v = rb.linearVelocity;
            float vr = Vector3.Dot(v, up);
            Vector3 axis = launchPad.Axis;
            float target = launchPad.TargetSurfaceRadius;

            if (r < target + 1f)
            {
                if (vr <= 0f) { launchPad = null; return; } // somehow stalled; let physics take over
                Vector3 offAxis = pos - axis * Vector3.Dot(pos, axis);
                rb.linearVelocity = up * vr + Vector3.ProjectOnPlane(-offAxis * 6f, up);
                return;
            }

            float landRadius = target + Radius;
            float h = Mathf.Max(0f, r - landRadius);
            float t = (vr + Mathf.Sqrt(vr * vr + 2f * gravity * h)) / gravity;
            float a = launchPad.LandAngle;
            Vector3 landDir = (axis * Mathf.Cos(a) + launchPad.ExitTangent * Mathf.Sin(a)).normalized;
            Vector3 toLand = Vector3.ProjectOnPlane(landDir * landRadius - pos, up);
            rb.linearVelocity = up * vr + toLand / Mathf.Max(t, 0.2f);
            launchPad = null;
        }

        void OnCollisionStay(Collision c) => CheckGround(c);
        void OnCollisionEnter(Collision c) => CheckGround(c);

        void CheckGround(Collision c)
        {
            Vector3 up = rb.position.normalized;
            for (int i = 0; i < c.contactCount; i++)
            {
                if (Vector3.Dot(c.GetContact(i).normal, up) > 0.55f) { touchingGround = true; return; }
            }
        }

        public void SetPad(BoostPad boost)
        {
            pad = boost;
            padSeenAt = Time.time;
        }

        public void Teleport(Vector3 position)
        {
            launchPad = null;
            dropping = false;
            transform.position = position;
            rb.position = position;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            checkpoint = position;
            checkpointLayer = level != null ? level.LayerAt(position.magnitude) : -1;
            CurrentLayer = Mathf.Max(0, checkpointLayer);
        }

        /// <summary>
        /// Makes this the place the ball comes back to on its current layer (a checkpoint pad), unless it has
        /// already reached a pad further along the world (a higher <paramref name="order"/>).
        /// </summary>
        public void SetCheckpoint(Vector3 position, int order)
        {
            if (order < checkpointOrder) return;
            checkpointOrder = order;
            checkpoint = position;
            checkpointLayer = CurrentLayer;
        }

        /// <summary>Forgets which checkpoint pads were reached, for a new world.</summary>
        public void ForgetCheckpoints() => checkpointOrder = -1;

        /// <summary>Sent when the ball first touches down on a layer after dropping through a hole or climbing.</summary>
        public event System.Action LandedOnNewLayer;

        /// <summary>Sent after the ball reappears at its checkpoint, so the camera can face the way on.</summary>
        public event System.Action Respawned;

        public void RespawnAtCheckpoint()
        {
            int layer = checkpointLayer;
            Teleport(checkpoint + checkpoint.normalized * 0.5f);
            checkpointLayer = layer;
            Respawned?.Invoke();
        }

        /// <summary>Blown up by a mine or lasers: burst into pieces in a fireball, then reappear at the checkpoint.</summary>
        public void HitHazard()
        {
            if (exploding || frozen) return;
            StartCoroutine(Explode());
        }

        IEnumerator Explode()
        {
            exploding = true;
            var look = GetComponent<Renderer>();
            look.enabled = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            var gm = GameManager.Instance;
            if (gm != null && gm.materials.beam != null) Explosion.Spawn(transform.position, gm.materials.beam, new Color(1f, 0.45f, 0.1f));
            Shatter(look.sharedMaterial);
            yield return new WaitForSeconds(respawnDelay);
            rb.isKinematic = false;
            exploding = false;
            RespawnAtCheckpoint();
            look.enabled = true;
        }

        /// <summary>
        /// The ball breaking apart: a dozen chunky pieces of its shell flung out every which way (mostly up and
        /// away from the ground), tumbling as they fall back and shrink away.
        /// </summary>
        void Shatter(Material shell)
        {
            Vector3 center = transform.position, up = center.normalized;
            int pieces = Random.Range(10, 14);
            for (int i = 0; i < pieces; i++)
            {
                var piece = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube);
                Destroy(piece.GetComponent<Collider>());
                piece.name = "Ball Piece";
                var mr = piece.GetComponent<MeshRenderer>();
                mr.sharedMaterial = shell;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector3 dir = Random.onUnitSphere;
                if (Vector3.Dot(dir, up) < 0f) dir = Vector3.Reflect(dir, up);
                piece.transform.SetPositionAndRotation(center + dir * Radius * 0.5f, Random.rotation);
                piece.transform.localScale = new Vector3(Random.Range(0.18f, 0.38f), Random.Range(0.12f, 0.3f), Random.Range(0.18f, 0.38f)) * (Radius * 2f);
                var debris = piece.AddComponent<Debris>();
                debris.velocity = (dir + up * 0.6f).normalized * Random.Range(4f, 8f);
                debris.spin = Random.insideUnitSphere * 720f;
                debris.life = Random.Range(0.8f, 1.2f);
            }
        }

        public void SetFrozen(bool value)
        {
            frozen = value;
            if (value)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
