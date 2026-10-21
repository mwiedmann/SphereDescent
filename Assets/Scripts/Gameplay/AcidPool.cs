using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A cell-sized pit of acid crossed by a thin bridge. Dropping off the bridge into the pit blows the ball up,
    /// like touching a mine.
    /// </summary>
    public class AcidPool : MonoBehaviour
    {
        [HideInInspector] public Vector3 center;      // unit direction of the pool cell's center
        [HideInInspector] public Vector3[] neighbors; // unit directions of the cells around it (the pit is nearer the center than any)
        [HideInInspector] public float floorRadius;   // the floor's distance from the world center around the pool
        [HideInInspector] public int layer;
        [Tooltip("How far the ball's center has to sink below the floor to count as in the acid.")]
        public float sinkDepth = 0.15f;

        BallController ball;

        void Update()
        {
            if (ball == null) ball = FindAnyObjectByType<BallController>();
            if (ball == null || ball.IsDestroyed || ball.CurrentLayer != layer) return;
            Vector3 p = ball.transform.position;
            if (p.magnitude > floorRadius - sinkDepth || !Inside(p.normalized)) return;
            ball.HitHazard();
        }

        public bool Inside(Vector3 dir)
        {
            float own = Vector3.Dot(dir, center);
            foreach (var n in neighbors) if (Vector3.Dot(dir, n) > own) return false;
            return true;
        }
    }
}
