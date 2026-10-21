using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>How a camera's spotlight searches its doorway. All keep moving at a steady clip; none stop.</summary>
    public enum SweepPattern
    {
        FigureEight, // swings across the doorway, looping into the cells either side
        Orbit,       // circles the doorway in an oval
        Sweep,       // swings from wall to wall across the doorway with a slight wobble
    }

    /// <summary>
    /// A camera on a pole above a wall, sweeping a spotlight across a doorway in one of a few patterns. If the ball is
    /// caught in the light, the spot turns red and locks on, and a moment later the camera fires a laser
    /// that blows the ball up.
    /// </summary>
    public class SecurityCamera : MonoBehaviour
    {
        [Header("Parts")]
        public Transform head;
        public Transform lens;
        public Transform spot;
        public Transform cone;
        public Transform laser;
        public Material spotMaterial, coneMaterial;

        [Header("Sweep")]
        [HideInInspector] public Vector3 center, across, along;
        [HideInInspector] public float groundRadius;
        [HideInInspector] public int layer;
        public SweepPattern pattern;
        public float sweepAcross = 4f;
        public float sweepAlong = 3f;
        public float period = 5f;
        public float phase;
        public float spotRadius = 2.2f;

        [Header("Look")]
        public Color searchColor = new Color(1f, 0.92f, 0.7f);
        public Color alertColor = new Color(1f, 0.05f, 0.02f);
        public float spotIntensity = 1f, coneIntensity = 0.25f;
        [Tooltip("Seconds between being spotted and the laser firing.")]
        public float lockOnTime = 0.25f;
        public float laserTime = 0.25f;

        BallController ball;
        bool firing;
        Vector3 aim;

        void Start()
        {
            SetColor(searchColor);
            if (laser != null) laser.gameObject.SetActive(false);
        }

        void Update()
        {
            if (ball == null) ball = FindAnyObjectByType<BallController>();

            if (!firing)
            {
                aim = SweepPoint(Time.time);

                if (ball != null && !ball.IsDestroyed && ball.CurrentLayer == layer &&
                    Vector3.Angle(ball.transform.position, aim) * Mathf.Deg2Rad * groundRadius < spotRadius)
                    StartCoroutine(Fire());
            }
            else if (ball != null && !ball.IsDestroyed)
            {
                // Locked on: the light follows the ball.
                aim = ball.transform.position.normalized * groundRadius;
            }
            Aim();
        }

        Vector3 SweepPoint(float time)
        {
            Vector2 p = Sweep(Mathf.Repeat(time / period + phase, 1f));
            return (center * groundRadius + across * (sweepAcross * p.x) + along * (sweepAlong * p.y)).normalized * groundRadius;
        }

        /// <summary>Poses the camera as it would be at <paramref name="time"/>, for still renders outside Play mode.</summary>
        public void Preview(float time)
        {
            SetColor(searchColor);
            if (laser != null) laser.gameObject.SetActive(false);
            aim = SweepPoint(time);
            Aim();
        }

        /// <summary>Poses the camera locked on and firing at <paramref name="target"/>, for still renders.</summary>
        public void PreviewCaught(Vector3 target)
        {
            SetColor(alertColor);
            aim = target.normalized * groundRadius;
            Aim();
            Vector3 from = lens.position;
            laser.gameObject.SetActive(true);
            laser.SetPositionAndRotation((from + target) * 0.5f, Quaternion.FromToRotation(Vector3.up, target - from));
            laser.localScale = new Vector3(0.12f, Vector3.Distance(from, target) * 0.5f, 0.12f);
        }

        /// <summary>Where the spot is at time <paramref name="u"/> (0-1 through one period), as fractions of the sweep's reach across and along.</summary>
        Vector2 Sweep(float u)
        {
            float t = u * Mathf.PI * 2f;
            switch (pattern)
            {
                case SweepPattern.Orbit:
                    return new Vector2(Mathf.Cos(t), Mathf.Sin(t));
                case SweepPattern.Sweep:
                    // The wobble is out of step with the swing, so the spot is still moving as it turns at a wall.
                    return new Vector2(Mathf.Sin(t), 0.35f * Mathf.Cos(3f * t));
                default:
                    return new Vector2(Mathf.Sin(t), Mathf.Sin(2f * t));
            }
        }

        void Aim()
        {
            Vector3 up = aim.normalized;
            spot.SetPositionAndRotation(aim + up * 0.04f, Quaternion.FromToRotation(Vector3.up, up));
            head.rotation = Quaternion.LookRotation(aim - head.position, transform.up);
            Vector3 toSpot = aim - lens.position;
            cone.SetPositionAndRotation(lens.position, Quaternion.LookRotation(toSpot, transform.up));
            cone.localScale = new Vector3(spotRadius, spotRadius, toSpot.magnitude);
        }

        IEnumerator Fire()
        {
            firing = true;
            SetColor(alertColor);
            yield return new WaitForSeconds(lockOnTime);
            if (ball != null && !ball.IsDestroyed)
            {
                Vector3 from = lens.position, to = ball.transform.position;
                laser.gameObject.SetActive(true);
                laser.SetPositionAndRotation((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, to - from));
                laser.localScale = new Vector3(0.12f, Vector3.Distance(from, to) * 0.5f, 0.12f);
                ball.HitHazard();
                yield return new WaitForSeconds(laserTime);
                laser.gameObject.SetActive(false);
            }
            // Keep glaring until the ball is back, then go back to searching.
            while (ball != null && ball.IsDestroyed) yield return null;
            SetColor(searchColor);
            firing = false;
        }

        void SetColor(Color c)
        {
            if (spotMaterial != null)
            {
                spotMaterial.SetColor("_Color", c);
                spotMaterial.SetFloat("_Intensity", spotIntensity);
            }
            if (coneMaterial != null)
            {
                coneMaterial.SetColor("_Color", c);
                coneMaterial.SetFloat("_Intensity", coneIntensity);
            }
        }
    }
}
