using System.Collections.Generic;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// The core's lava rain: every few seconds while the ball is on the core, a blob of lava drops out of the
    /// dark at where the ball will be when it lands if it keeps rolling the same way, like a mortar's aim. A
    /// dark shadow with a red rim spreads on the ground where it will land, so it can be dodged. A hit burns the
    /// ball up; the blob splats into smaller blobs that dissolve harmlessly. Blobs only ever land on the paths
    /// and plazas: one aimed out over the lava comes down on the nearest spot of the path the ball is on, and
    /// none land inside the walls still standing (or on the energy ball). It starts once the ball has reached
    /// the walls and the energy ball's introduction is over, and stops once the energy ball is freed.
    /// </summary>
    public class LavaRain : MonoBehaviour
    {
        public CoreArena arena;
        [System.NonSerialized] public CoreLayout core; // set by the builder each world, never saved
        [HideInInspector] public int layer;
        public Vector2 interval = new Vector2(3f, 4f);
        [Tooltip("Seconds a blob takes to fall: how long its shadow shows before it lands.")]
        public float fallTime = 1.4f;
        [Tooltip("How near the blob has to land to the ball to burn it up (from the ball's center).")]
        public float splashRadius = 1.6f;
        [Tooltip("Seconds from arriving on the core to the first blob.")]
        public float firstDelay = 2f;
        public Mesh blobMesh;
        public Material blobMaterial, shadowMaterial, rimMaterial, glowMaterial;

        BallController ball;
        Rigidbody ballBody;
        float cooldown = -1f;
        readonly List<LavaBlob> falling = new List<LavaBlob>();

        public bool Stopped => arena != null && arena.orb != null && arena.orb.Freed;

        void Update()
        {
            if (ball == null)
            {
                ball = FindAnyObjectByType<BallController>();
                if (ball != null) ballBody = ball.GetComponent<Rigidbody>();
                if (ball == null) return;
            }
            if (Stopped)
            {
                // The energy ball is free: what's still falling fizzles out.
                foreach (var b in falling) if (b != null) Destroy(b.gameObject);
                falling.Clear();
                enabled = false;
                return;
            }
            falling.RemoveAll(b => b == null);
            if (ball.CurrentLayer != layer || ball.IsDestroyed || (arena != null && !arena.IntroDone))
            {
                cooldown = firstDelay;
                return;
            }
            if (cooldown < 0f) cooldown = firstDelay;
            cooldown -= Time.deltaTime;
            if (cooldown > 0f) return;
            cooldown = Random.Range(interval.x, interval.y);
            Vector3 target = Lead(out Vector3 normal);
            falling.Add(LavaBlob.Drop(this, target, normal));
        }

        /// <summary>Where the ball will be when a blob lands, rolling on over the core at its current velocity, on the ground.</summary>
        Vector3 Lead(out Vector3 normal)
        {
            Vector3 p = ball.transform.position;
            Vector3 v = ballBody != null ? Vector3.ProjectOnPlane(ballBody.linearVelocity, p.normalized) : Vector3.zero;
            if (v.sqrMagnitude > 0.01f)
                p = Quaternion.AngleAxis(v.magnitude * fallTime / p.magnitude * Mathf.Rad2Deg, Vector3.Cross(p, v).normalized) * p;
            // Clear of the walls still standing (and of the energy ball once they're all down).
            int wall = arena != null ? arena.OutermostStanding() : -1;
            float keepOut = (wall >= 0 ? core.RingRadii[wall] : 0f) + splashRadius + 1.5f;
            Vector3 dir = core.NearestGround(p, ball.transform.position, keepOut);
            normal = core.Normal(dir);
            return dir * core.SurfaceRadius(dir);
        }

        /// <summary>
        /// The core's ground (paths and plazas, never walls or the energy ball) straight down from
        /// <paramref name="from"/>, for draping the shadow.
        /// </summary>
        public bool GroundHit(Vector3 from, float length, out Vector3 point, out Vector3 normal)
        {
            Vector3 dir = from.normalized;
            point = dir * core.SurfaceRadius(dir);
            normal = core.Normal(dir);
            return true;
        }

        /// <summary>A blob has landed at <paramref name="point"/>: burn the ball up if it's too near.</summary>
        public void Impact(Vector3 point)
        {
            if (glowMaterial != null) Explosion.Spawn(point, glowMaterial, new Color(1f, 0.4f, 0.05f), splashRadius * 2.2f);
            if (!Stopped && ball != null && !ball.IsDestroyed && ball.CurrentLayer == layer &&
                Vector3.Distance(ball.transform.position, point) < splashRadius + ball.Radius)
                ball.HitHazard();
        }
    }
}
