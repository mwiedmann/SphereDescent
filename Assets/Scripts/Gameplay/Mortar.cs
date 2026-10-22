using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A squat mortar on top of a wall. While the ball is in range it lobs a bomb every couple of seconds,
    /// aimed where the ball will be when the bomb comes down if it keeps rolling the same way at the same
    /// speed. A flashing red marker shows where each bomb will land; roll off it (turn, stop or speed up)
    /// before it hits.
    /// </summary>
    public class Mortar : MonoBehaviour
    {
        [Header("Parts")]
        public Transform turret;   // turns to face the target
        public Transform barrel;   // pitched up inside the turret; kicks back on firing
        public Transform muzzle;
        [Tooltip("Where bombs and markers are kept: the layer, so they aren't switched off with the mortar's chunk.")]
        public Transform bombParent;
        public Mesh bombMesh;
        public Material bombMaterial, bombLightMaterial, markerMaterial, glowMaterial;

        [Header("Firing")]
        [HideInInspector] public int layer;
        public float range = 18f;
        public float interval = 1.5f;
        public float flightTime = 1f;
        public float blastRadius = 2.4f;
        [Tooltip("Seconds from the ball coming into range to the first shot.")]
        public float firstShotDelay = 0.6f;

        BallController ball;
        Rigidbody ballBody;
        float cooldown;
        float kick;
        Vector3 barrelRest;
        Vector3 groundNormal; // of the ground at the latest aim point

        void Start()
        {
            cooldown = firstShotDelay;
            if (barrel != null) barrelRest = barrel.localPosition;
        }

        void Update()
        {
            if (ball == null)
            {
                ball = FindAnyObjectByType<BallController>();
                if (ball != null) ballBody = ball.GetComponent<Rigidbody>();
            }

            bool inRange = ball != null && !ball.IsDestroyed && ball.CurrentLayer == layer &&
                           Vector3.Distance(ball.transform.position, transform.position) < range;
            if (inRange)
            {
                Vector3 target = Lead();
                Face(target);
                cooldown -= Time.deltaTime;
                if (cooldown <= 0f)
                {
                    Fire(target);
                    cooldown = interval;
                }
            }
            else cooldown = Mathf.Max(cooldown, firstShotDelay);

            // The barrel kicks back when it fires and eases out again.
            kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 2.5f);
            if (barrel != null) barrel.localPosition = barrelRest - barrel.localRotation * Vector3.up * (0.35f * kick);
        }

        /// <summary>Where the ball will be after a bomb's flight, rolling on over the sphere at its current velocity.</summary>
        Vector3 Lead()
        {
            Vector3 p = ball.transform.position;
            Vector3 v = ballBody != null ? Vector3.ProjectOnPlane(ballBody.linearVelocity, p.normalized) : Vector3.zero;
            if (v.sqrMagnitude < 0.01f) return Ground(p);
            float angle = v.magnitude * flightTime / p.magnitude * Mathf.Rad2Deg;
            return Ground(Quaternion.AngleAxis(angle, Vector3.Cross(p, v).normalized) * p);
        }

        /// <summary>The ground (floor, or a wall top or bridge) under a point, found by looking straight down.</summary>
        Vector3 Ground(Vector3 point)
        {
            Vector3 up = point.normalized;
            if (GroundHit(point + up * 3f, 8f, out Vector3 ground, out groundNormal)) return ground;
            groundNormal = up;
            return point - up * 0.5f;
        }

        /// <summary>
        /// The first solid surface straight down (toward the world's center) from <paramref name="from"/> within
        /// <paramref name="length"/>, looking past the ball and triggers.
        /// </summary>
        public bool GroundHit(Vector3 from, float length, out Vector3 point, out Vector3 normal)
        {
            var hits = Physics.RaycastAll(from, -from.normalized, length, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            point = normal = Vector3.zero;
            foreach (var h in hits)
            {
                if (h.rigidbody != null && h.rigidbody == ballBody) continue;
                if (h.distance < nearest) { nearest = h.distance; point = h.point; normal = h.normal; }
            }
            return nearest < float.MaxValue;
        }

        void Face(Vector3 target)
        {
            if (turret == null) return;
            Vector3 up = transform.up;
            Vector3 flat = Vector3.ProjectOnPlane(target - turret.position, up);
            if (flat.sqrMagnitude < 0.01f) return;
            turret.rotation = Quaternion.Slerp(turret.rotation, Quaternion.LookRotation(flat, up), 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        void Fire(Vector3 target)
        {
            kick = 1f;
            Vector3 from = muzzle != null ? muzzle.position : transform.position;
            if (glowMaterial != null) Explosion.Spawn(from, glowMaterial, new Color(1f, 0.6f, 0.2f), 1.2f);
            MortarBomb.Launch(this, from, target, groundNormal);
        }

        /// <summary>A bomb has come down at <paramref name="point"/>: blow it up, and the ball with it if it's too near.</summary>
        public void Impact(Vector3 point)
        {
            if (glowMaterial != null) Explosion.Spawn(point, glowMaterial, new Color(1f, 0.45f, 0.1f), blastRadius * 2.5f);
            if (ball != null && !ball.IsDestroyed && ball.CurrentLayer == layer &&
                Vector3.Distance(ball.transform.position, point) < blastRadius)
                ball.HitHazard();
        }
    }
}
