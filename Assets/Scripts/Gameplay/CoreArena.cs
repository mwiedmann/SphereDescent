using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// The core's boss battle: knows its walls and the energy ball they trap, cheers each wall going down,
    /// blows the ball up if it rolls off a path into the lava, and makes the walls' plaza a checkpoint once the
    /// ball reaches it, so a fall on the way to a key pad doesn't send it all the way back to the landing.
    /// </summary>
    public class CoreArena : MonoBehaviour
    {
        public CoreBarrier[] barriers;
        public EnergyOrb orb;
        [Tooltip("The lava's surface: the ball's center sinking below this (plus a little) burns it up.")]
        public float lavaRadius = 22f;
        public int layer;
        [Tooltip("Unit direction of the plaza's middle, its radius in units, and the ground's radius.")]
        public Vector3 cage;
        public float cageRadius, groundRadius;
        [Tooltip("Where the ball comes back after reaching the plaza.")]
        public Vector3 plazaCheckpoint;

        BallController ball;
        bool plazaReached;

        public int Remaining
        {
            get
            {
                int n = 0;
                foreach (var b in barriers) if (b != null && !b.Broken) n++;
                return n;
            }
        }

        public bool AllBroken => Remaining == 0;

        /// <summary>The outermost wall no cable has reached yet (the walls are innermost first), or -1.</summary>
        public int OutermostStanding()
        {
            for (int i = barriers.Length - 1; i >= 0; i--)
                if (barriers[i] != null && !barriers[i].Breaking) return i;
            return -1;
        }

        void Start()
        {
            foreach (var b in barriers) if (b != null) b.Broke += OnBroke;
        }

        void OnBroke(CoreBarrier barrier)
        {
            if (GameManager.Instance == null) return;
            int left = Remaining;
            GameManager.Instance.ShowMessage(left > 0 ? $"Wall down! {left} to go" : "The energy ball is free! Roll into it", 2.5f);
        }

        void Update()
        {
            if (ball == null) ball = FindAnyObjectByType<BallController>();
            if (ball == null || ball.IsDestroyed || ball.CurrentLayer != layer) return;
            Vector3 pos = ball.transform.position;
            if (pos.magnitude < lavaRadius + 0.35f) ball.HitHazard();
            else if (!plazaReached && ball.IsGrounded && Vector3.Angle(pos, cage) * Mathf.Deg2Rad * groundRadius < cageRadius)
            {
                plazaReached = true;
                // Past every checkpoint pad in the world.
                ball.SetCheckpoint(plazaCheckpoint, int.MaxValue);
                if (GameManager.Instance != null) GameManager.Instance.ShowMessage("Checkpoint!", 1.6f);
            }
        }
    }
}
