using System.Collections.Generic;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// Rolling hills on one shell: a height above the shell's base radius for any direction, made of
    /// randomly placed smooth bumps. Flat spots (around holes, boost pads and the start) blend back down to
    /// zero, so those keep working exactly as on a flat shell. Height is never negative, so hills never
    /// dip toward the layer below.
    /// </summary>
    public sealed class ShellTerrain
    {
        struct Bump
        {
            public Vector3 Center;  // unit direction
            public float InvSize;   // 1 / (size in world units)
            public float Height;
            public float MinDot;    // beyond this angle the bump is negligible
        }

        struct FlatSpot
        {
            public Vector3 Center;
            public float Flat, Fade; // fully flat within Flat units, back to full height by Flat + Fade
        }

        public static readonly ShellTerrain None = new ShellTerrain(1f, 0f);

        readonly float radius;
        readonly float maxHeight;
        float size;
        readonly List<Bump> bumps = new List<Bump>();
        readonly List<FlatSpot> flats = new List<FlatSpot>();
        // Bumps bucketed by a coarse 3D grid over the unit sphere, so a lookup only checks nearby ones.
        readonly Dictionary<Vector3Int, List<int>> buckets = new Dictionary<Vector3Int, List<int>>();
        float bucketSize = 1f;

        ShellTerrain(float radius, float maxHeight)
        {
            this.radius = radius;
            this.maxHeight = maxHeight;
        }

        /// <param name="size">Typical hill radius (world units).</param>
        /// <param name="coverage">Roughly what fraction of the surface is covered by hills.</param>
        public static ShellTerrain Generate(float radius, float maxHeight, float size, float coverage, int seed)
        {
            var t = new ShellTerrain(radius, maxHeight);
            if (maxHeight <= 0f || size <= 0f || coverage <= 0f) return t;
            t.size = size;
            var rng = new System.Random(seed);
            float area = 4f * Mathf.PI * radius * radius;
            int count = Mathf.CeilToInt(coverage * area / (Mathf.PI * size * size));
            // Bumps reach 3x their size; chord distance never exceeds the angle, so that bounds them on the grid.
            t.bucketSize = Mathf.Min(2f, 3f * 1.4f * size / radius);
            for (int i = 0; i < count; i++)
            {
                // Uniform random direction.
                float z = (float)rng.NextDouble() * 2f - 1f;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float s = Mathf.Sqrt(1f - z * z);
                float bumpSize = size * Mathf.Lerp(0.6f, 1.4f, (float)rng.NextDouble());
                t.bumps.Add(new Bump
                {
                    Center = new Vector3(s * Mathf.Cos(a), z, s * Mathf.Sin(a)),
                    InvSize = 1f / bumpSize,
                    Height = maxHeight * Mathf.Lerp(0.35f, 1f, (float)rng.NextDouble()),
                    MinDot = Mathf.Cos(Mathf.Min(Mathf.PI, 3f * bumpSize / radius)),
                });
                Vector3 c = t.bumps[i].Center;
                float reach = 3f * bumpSize / radius;
                Vector3Int lo = t.Bucket(c - Vector3.one * reach), hi = t.Bucket(c + Vector3.one * reach);
                for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                for (int z2 = lo.z; z2 <= hi.z; z2++)
                {
                    var key = new Vector3Int(x, y, z2);
                    if (!t.buckets.TryGetValue(key, out var list)) t.buckets[key] = list = new List<int>();
                    list.Add(i);
                }
            }
            return t;
        }

        Vector3Int Bucket(Vector3 p) =>
            new Vector3Int(Mathf.FloorToInt(p.x / bucketSize), Mathf.FloorToInt(p.y / bucketSize), Mathf.FloorToInt(p.z / bucketSize));

        /// <summary>
        /// Longest straight piece that follows the ground closely enough (things built on it, like walls, sink
        /// 0.6 units in). Narrow, tall hills need shorter pieces.
        /// </summary>
        public float DetailLength(float maxLength)
        {
            if (maxHeight <= 0f || size <= 0f) return maxLength;
            // Peak curvature of a bump is about 2 * height / size^2, and a chord of length L strays from it
            // by curvature * L^2 / 8; keep that under 0.25 units.
            float l = Mathf.Sqrt(0.25f * 8f * size * size / (2f * maxHeight));
            return Mathf.Clamp(l, 0.5f, maxLength);
        }

        /// <summary>Keeps the ground flat within <paramref name="flat"/> units of a direction, easing back over <paramref name="fade"/>.</summary>
        public void AddFlatSpot(Vector3 direction, float flat, float fade)
        {
            if (maxHeight > 0f) flats.Add(new FlatSpot { Center = direction.normalized, Flat = flat, Fade = fade });
        }

        /// <summary>Height of the ground above the shell's base radius in this direction.</summary>
        public float Height(Vector3 direction)
        {
            if (maxHeight <= 0f) return 0f;
            Vector3 dir = direction.normalized;
            float sum = 0f;
            if (!buckets.TryGetValue(Bucket(dir), out var nearby)) return 0f;
            foreach (int i in nearby)
            {
                var b = bumps[i];
                float dot = Vector3.Dot(dir, b.Center);
                if (dot < b.MinDot) continue;
                // Chord distance is close enough to arc distance at hill scale.
                float d = (dir - b.Center).magnitude * radius * b.InvSize;
                sum += b.Height * Mathf.Exp(-d * d);
            }
            if (sum <= 0f) return 0f;
            // Overlapping hills merge into a ridge instead of stacking past the maximum height.
            float h = maxHeight * (1f - Mathf.Exp(-sum / maxHeight));
            foreach (var f in flats)
            {
                float d = (dir - f.Center).magnitude * radius;
                if (d >= f.Flat + f.Fade) continue;
                h *= Mathf.SmoothStep(0f, 1f, (d - f.Flat) / f.Fade);
            }
            return h;
        }

        /// <summary>
        /// Which way the ground faces in this direction: straight out from the center on flat ground, leaning
        /// on a hillside. Measured from the ground height a short step either side.
        /// </summary>
        public Vector3 Normal(Vector3 direction, float step = 0.3f)
        {
            Vector3 up = direction.normalized;
            if (maxHeight <= 0f) return up;
            Vector3 a = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 b = Vector3.Cross(up, a);
            Vector3 Ground(Vector3 offset)
            {
                Vector3 d = (up * radius + offset).normalized;
                return d * SurfaceRadius(d);
            }
            Vector3 n = Vector3.Cross(Ground(a * step) - Ground(-a * step), Ground(b * step) - Ground(-b * step)).normalized;
            return Vector3.Dot(n, up) < 0f ? -n : n;
        }

        /// <summary>Distance from the center to the ground in this direction.</summary>
        public float SurfaceRadius(Vector3 direction) => radius + Height(direction);
    }
}
