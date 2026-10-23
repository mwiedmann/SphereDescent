using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace SphereDescent
{
    /// <summary>
    /// Pure-data description of the core, where each world ends in a boss battle: a ball of lava with hilly,
    /// wall-less paths running over it. The ball drops in onto a round landing. Well round the core from it,
    /// a round plaza holds a trapped energy ball inside concentric glowing walls. Key pads sit at the far
    /// ends of the paths, each on a wide round patch of ground (room to turn round), one per wall: rolling
    /// over a pad lights a cable that runs back along its path to the walls and breaks the outermost one still
    /// standing, so the walls fall away from the outside in. Energy cubes and a
    /// few lone mines are scattered along the paths.
    ///
    /// The plaza is the hub: one path from it to the landing, and one winding branch out to each pad, spread
    /// evenly around it and reaching well round the core. Once clear of the plaza the branches all curve the
    /// same way, like a pinwheel, so they're longer and reach round the back of the core without coming any
    /// closer together. Each branch keeps to its own slice of the core, so no two paths meet except at the
    /// plaza, and every cable runs back along its own branch, crossing nothing.
    /// The ground is a height field over the whole core: paths and plazas stand above the lava, everything
    /// else sinks below it.
    /// </summary>
    public sealed class CoreLayout
    {
        public float LavaRadius;       // the lava's surface (the core radius)
        public float GroundRadius;     // the plazas' height, which the paths' hills rise and fall around
        public float PathHalfWidth;
        public Vector3 Landing;        // unit direction where the ball drops in
        public float LandingRadius;    // of the flat round landing, in units
        public float PadRadius;        // of the flat round ground at each key pad, room to turn round
        public Vector3 Cage;           // unit direction of the trapped energy ball
        public float CageRadius;       // of the flat round plaza around the walls
        public float[] RingRadii;      // the walls around the energy ball, innermost first
        public Vector3[] Pads;         // unit directions of the key pads
        public List<Vector3> Cubes = new List<Vector3>();
        public List<Vector3> Mines = new List<Vector3>();
        public List<List<Vector3>> Paths = new List<List<Vector3>>();  // center lines from the plaza out, as unit directions about a unit apart: the landing's first, then each pad's
        public List<List<Vector3>> Cables = new List<List<Vector3>>(); // per pad, from the pad in to the innermost wall, as unit directions

        // How far the ground slopes from a path's edge down to below the lava.
        public const float SideWidth = 1.6f;
        // How deep the ground under the lava is.
        public const float LavaDepth = 1.5f;

        Vector3[] hillAxes;
        float[] hillFreq, hillPhase;
        float hillHeight;
        readonly List<(Vector3 center, float reach)> pathBounds = new List<(Vector3, float)>();

        /// <summary>Lays out the core under the hole the last section drops through. Null if nothing fits.</summary>
        public static CoreLayout Generate(LevelSettings s, Vector3 landing, Random rng)
        {
            // Each try a little less ambitious than the last (gentler sways, pads not so far round), in case the
            // paths don't fit.
            const int attempts = 200;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var core = TryGenerate(s, landing.normalized, rng, 1f - 0.5f * attempt / attempts);
                if (core != null) return core;
            }
            return null;
        }

        static CoreLayout TryGenerate(LevelSettings s, Vector3 landing, Random rng, float ambition)
        {
            int rings = Mathf.Max(1, s.coreKeyPads);
            var core = new CoreLayout
            {
                LavaRadius = s.coreRadius,
                GroundRadius = s.coreRadius + s.corePathHeight,
                PathHalfWidth = s.corePathWidth * 0.5f,
                Landing = landing,
                LandingRadius = 5f,
                PadRadius = 6f,
                RingRadii = new float[rings],
                hillHeight = s.coreHillHeight,
            };
            for (int i = 0; i < rings; i++) core.RingRadii[i] = 2.4f + i;
            // Room to roll all the way round the outer wall.
            core.CageRadius = core.RingRadii[rings - 1] + 4.5f;
            float R = core.GroundRadius;
            float Angle(Vector3 a, Vector3 b) => Vector3.Angle(a, b) * Mathf.Deg2Rad * R; // in units along the ground
            float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

            core.hillAxes = new Vector3[3];
            core.hillFreq = new float[3];
            core.hillPhase = new float[3];
            for (int k = 0; k < 3; k++)
            {
                core.hillAxes[k] = RandomDirection(rng);
                core.hillFreq[k] = 4.5f + (float)rng.NextDouble() * 2.5f;
                core.hillPhase[k] = (float)rng.NextDouble() * Mathf.PI * 2f;
            }

            // The cage, well round the core from the landing (whose path then winds round the back of the core).
            Vector3 cage = Vector3.zero;
            for (int k = 0; k < 200 && cage == Vector3.zero; k++)
            {
                var d = RandomDirection(rng);
                float a = Vector3.Angle(d, landing);
                if (a > 115f && a < 135f) cage = d;
            }
            if (cage == Vector3.zero) return null;
            core.Cage = cage;

            // Directions by how far round the core from the cage (theta) and which way (phi, 0 = toward the landing).
            Vector3 t1 = Vector3.ProjectOnPlane(landing, cage).normalized, t2 = Vector3.Cross(cage, t1);
            Vector3 Dir(float theta, float phi) =>
                (cage * Mathf.Cos(theta) + (t1 * Mathf.Cos(phi) + t2 * Mathf.Sin(phi)) * Mathf.Sin(theta)).normalized;

            // The pinwheel: past theta 40 degrees, every branch turns steadily (easing in over 20 degrees) the same
            // way round, about 35 degrees off straight out where it crosses the core's middle: as far as it can
            // while still winding back and forth without crowding the next branch.
            float twist = (rng.Next(2) == 0 ? -1f : 1f) * Rand(0.6f, 0.75f), bendFrom = 40f * Mathf.Deg2Rad, ease = 20f * Mathf.Deg2Rad;
            float Turn(float theta)
            {
                float x = Mathf.Max(0f, theta - bendFrom);
                return twist * (x < ease ? x * x / (2f * ease) : x - ease * 0.5f);
            }

            // One branch each way round the plaza, evenly spread (give or take), the landing's set so it ends at
            // the landing (phi 0).
            int spokes = rings + 1;
            float slice = Mathf.PI * 2f / spokes;
            float thetaLanding = Vector3.Angle(landing, cage) * Mathf.Deg2Rad;
            var centers = new float[spokes];
            centers[0] = -Turn(thetaLanding);
            for (int i = 1; i < spokes; i++) centers[i] = centers[0] + slice * i + Rand(-0.15f, 0.15f) * slice;
            // Each branch keeps inside its own slice: halfway to the branches either side, less room for both paths.
            float HalfSlice(int i)
            {
                float before = i == 0 ? centers[0] - (centers[spokes - 1] - Mathf.PI * 2f) : centers[i] - centers[i - 1];
                float after = i == spokes - 1 ? centers[0] + Mathf.PI * 2f - centers[i] : centers[i + 1] - centers[i];
                return Mathf.Min(before, after) * 0.5f;
            }
            float start = (core.CageRadius - 1f) / R; // just inside the plaza's edge, so the paths join it

            // A branch winding out from the plaza to theta <paramref name="end"/>: straight where it leaves the
            // plaza and where it arrives, swaying up to sway units either side in between, never out of its slice.
            List<Vector3> Branch(int i, float end, float sway)
            {
                float length = (end - start) * R;
                int steps = Mathf.Max(2, Mathf.CeilToInt(length));
                float waves = Rand(2.5f, 3.5f), phase = rng.Next(2) == 0 ? 0f : Mathf.PI;
                float room = core.PathHalfWidth + SideWidth + 2.5f;
                var points = new List<Vector3>();
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps, theta = Mathf.Lerp(start, end, t);
                    float env = Mathf.SmoothStep(0f, 1f, (theta - start) * R / 8f) * Mathf.SmoothStep(0f, 1f, (end - theta) * R / 6f);
                    float across = R * Mathf.Sin(theta);
                    // Measured square to the branch, which runs slantwise once it turns.
                    float slant = Mathf.Sqrt(1f + Mathf.Pow(Mathf.Sin(theta) * (Turn(theta + 0.01f) - Turn(theta)) / 0.01f, 2f));
                    float amp = Mathf.Min(sway / across, Mathf.Max(0f, HalfSlice(i) - room * slant / across));
                    points.Add(Dir(theta, centers[i] + Turn(theta) + amp * env * Mathf.Sin(Mathf.PI * 2f * waves * t + phase)));
                }
                return points;
            }

            core.Paths.Add(Branch(0, thetaLanding, 3f));
            core.Pads = new Vector3[rings];
            for (int p = 0; p < rings; p++)
            {
                var branch = Branch(p + 1, Rand(105f, 105f + 30f * ambition) * Mathf.Deg2Rad, 12f * ambition);
                core.Paths.Add(branch);
                core.Pads[p] = branch[branch.Count - 1];
            }
            // Past the plaza, lava shows between every two paths (the slices only keep them roughly apart).
            float gap = s.corePathWidth + 2f * SideWidth + 1.5f;
            if (core.ClosestPaths() < gap) return null;
            // The same around the pads' wider ground.
            for (int p = 0; p < rings; p++)
                for (int q = 0; q < core.Paths.Count; q++)
                {
                    if (q == p + 1) continue;
                    foreach (var d in core.Paths[q])
                        if (Angle(d, core.Pads[p]) < gap + core.PadRadius - core.PathHalfWidth) return null;
                }
            foreach (var path in core.Paths)
            {
                Vector3 c = Vector3.zero;
                foreach (var p in path) c += p;
                c.Normalize();
                float reach = 0f;
                foreach (var p in path) reach = Mathf.Max(reach, Angle(c, p));
                core.pathBounds.Add((c, reach));
            }

            // Cables: from the pad's edge back along its branch, then straight on across the plaza and under the
            // walls to the face of the innermost one (they only light up as far as the wall they break).
            for (int p = 0; p < rings; p++)
            {
                var cable = new List<Vector3>(core.Paths[p + 1]);
                cable.Reverse();
                while (cable.Count > 2 && Angle(cable[0], core.Pads[p]) < 1.4f) cable.RemoveAt(0);
                float stopAt = (core.RingRadii[0] + 0.35f) / R;
                for (float theta = start - 1f / R; theta > stopAt; theta -= 1f / R) cable.Add(Dir(theta, centers[p + 1]));
                cable.Add(Dir(stopAt, centers[p + 1]));
                core.Cables.Add(cable);
            }

            // A few lone mines on every path, away from its ends and from each other, anywhere across it.
            foreach (var path in core.Paths)
            {
                int want = rng.Next(s.coreMinesMin, s.coreMinesMax + 1), placed = 0;
                for (int tries = 0; tries < 300 && placed < want; tries++)
                {
                    var d = AcrossPath(path, rng.Next(path.Count), Rand(-1f, 1f) * (core.PathHalfWidth - 1.2f));
                    if (Angle(d, landing) < core.LandingRadius + 3f || Angle(d, cage) < core.CageRadius + 4f) continue;
                    bool clear = true;
                    foreach (var pad in core.Pads) if (Angle(d, pad) < core.PadRadius + 1.5f) clear = false;
                    foreach (var m in core.Mines) if (Angle(d, m) < 7f) clear = false;
                    if (!clear) continue;
                    core.Mines.Add(d);
                    placed++;
                }
                if (placed < s.coreMinesMin) return null;
            }

            // Cubes along the paths, apart from each other and from the pads, plazas and mines, some off to the sides.
            for (int tries = 0; tries < 400 && core.Cubes.Count < s.coreCubes; tries++)
            {
                var path = core.Paths[rng.Next(core.Paths.Count)];
                var d = AcrossPath(path, rng.Next(path.Count), Rand(-1f, 1f) * (core.PathHalfWidth - 1.5f));
                if (Angle(d, landing) < core.LandingRadius + 2f || Angle(d, cage) < core.CageRadius + 3f) continue;
                bool clear = true;
                foreach (var pad in core.Pads) if (Angle(d, pad) < 4f) clear = false;
                foreach (var c in core.Cubes) if (Angle(d, c) < 6f) clear = false;
                foreach (var m in core.Mines) if (Angle(d, m) < 3f) clear = false;
                if (clear) core.Cubes.Add(d);
            }
            if (core.Cubes.Count < s.coreCubes) return null;
            return core;

            // The point <paramref name="off"/> units to one side of a path's k-th point.
            Vector3 AcrossPath(List<Vector3> path, int k, float off)
            {
                Vector3 along = path[Mathf.Min(k + 1, path.Count - 1)] - path[Mathf.Max(k - 1, 0)];
                Vector3 side = Vector3.Cross(path[k], along).normalized;
                return (path[k] + side * off / R).normalized;
            }
        }

        /// <summary>How close (center line to center line, in units) any two paths come once clear of the plaza.</summary>
        public float ClosestPaths()
        {
            float R = GroundRadius, hub = CageRadius + 8f, closest = float.MaxValue;
            for (int a = 0; a < Paths.Count; a++)
                for (int b = a + 1; b < Paths.Count; b++)
                    foreach (var pa in Paths[a])
                    {
                        if (Vector3.Angle(pa, Cage) * Mathf.Deg2Rad * R < hub) continue;
                        foreach (var pb in Paths[b])
                            if (Vector3.Angle(pb, Cage) * Mathf.Deg2Rad * R >= hub)
                                closest = Mathf.Min(closest, Vector3.Angle(pa, pb) * Mathf.Deg2Rad * R);
                    }
            return closest;
        }

        static Vector3 RandomDirection(Random rng)
        {
            while (true)
            {
                var v = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                float m = v.sqrMagnitude;
                if (m > 0.01f && m <= 1f) return v / Mathf.Sqrt(m);
            }
        }

        /// <summary>
        /// How far (in units along the ground) a direction is outside the paths and plazas; negative inside.
        /// Only exact up to Side Width: anything farther out may come back as any distance past that.
        /// </summary>
        public float OutsideGround(Vector3 dir)
        {
            float R = GroundRadius;
            float best = Mathf.Min(Vector3.Angle(dir, Landing) * Mathf.Deg2Rad * R - LandingRadius,
                Vector3.Angle(dir, Cage) * Mathf.Deg2Rad * R - CageRadius);
            foreach (var p in Pads) best = Mathf.Min(best, Vector3.Angle(dir, p) * Mathf.Deg2Rad * R - PadRadius);
            for (int i = 0; i < Paths.Count; i++)
            {
                // Skip paths too far off to matter (only distances under Side Width change the ground).
                var (center, reach) = pathBounds[i];
                if (Vector3.Angle(dir, center) * Mathf.Deg2Rad * R > reach + PathHalfWidth + Mathf.Min(best, SideWidth) + 0.5f) continue;
                var path = Paths[i];
                for (int k = 0; k + 1 < path.Count; k++)
                {
                    // Close enough to straight over a unit: distance to the chord.
                    Vector3 a = path[k], ab = path[k + 1] - a;
                    float t = Mathf.Clamp01(Vector3.Dot(dir - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                    float d = (dir - (a + ab * t)).magnitude * R - PathHalfWidth;
                    if (d < best) best = d;
                }
            }
            return best;
        }

        /// <summary>
        /// The height of the top of the paths here: hills rising and falling over the core, flattening out on
        /// the plazas and around the pads.
        /// </summary>
        public float TopRadius(Vector3 dir)
        {
            float R = GroundRadius;
            float h = 0f;
            for (int k = 0; k < 3; k++) h += Mathf.Sin(Vector3.Dot(dir, hillAxes[k]) * hillFreq[k] + hillPhase[k]);
            h = Mathf.Clamp(h / 1.6f, -1f, 1f);
            float flat = Mathf.Min(Vector3.Angle(dir, Landing) * Mathf.Deg2Rad * R - LandingRadius,
                Vector3.Angle(dir, Cage) * Mathf.Deg2Rad * R - CageRadius);
            foreach (var p in Pads) flat = Mathf.Min(flat, Vector3.Angle(dir, p) * Mathf.Deg2Rad * R - PadRadius);
            return GroundRadius + hillHeight * h * Mathf.SmoothStep(0f, 1f, flat / 6f);
        }

        /// <summary>The ground's radius: the top of a path or plaza, sloping down at its edges to below the lava.</summary>
        public float SurfaceRadius(Vector3 dir)
        {
            dir.Normalize();
            float outside = OutsideGround(dir);
            float bottom = LavaRadius - LavaDepth;
            if (outside >= SideWidth) return bottom;
            float top = TopRadius(dir);
            return outside <= 0f ? top : Mathf.Lerp(top, bottom, Mathf.SmoothStep(0f, 1f, outside / SideWidth));
        }

        /// <summary>
        /// The nearest spot to <paramref name="target"/> (a direction) well onto solid ground (2 units in from
        /// the edge, so a lava blob's whole splash lands on it), kept clear of a circle of
        /// <paramref name="keepOut"/> units around the cage: the target itself if it's well onto a path or plaza,
        /// otherwise the nearest point across the path nearest the ball (<paramref name="ball"/>), or the plaza's
        /// edge just outside the keep-out circle.
        /// </summary>
        public Vector3 NearestGround(Vector3 target, Vector3 ball, float keepOut)
        {
            float R = GroundRadius;
            target.Normalize();
            float AngleTo(Vector3 a, Vector3 b) => Vector3.Angle(a, b) * Mathf.Deg2Rad * R;
            // Inside the walls (or onto the energy ball): out to just beyond them, toward the ball.
            if (AngleTo(target, Cage) < keepOut)
            {
                Vector3 away = Vector3.ProjectOnPlane(ball.normalized - Cage, Cage);
                if (away.sqrMagnitude < 1e-8f) away = Vector3.ProjectOnPlane(Landing, Cage);
                return (Cage * Mathf.Cos(keepOut / R) + away.normalized * Mathf.Sin(keepOut / R)).normalized;
            }
            if (OutsideGround(target) <= -2f) return target;
            // The path the ball is on (the nearest to it), and the point on it nearest the target.
            List<Vector3> path = Paths[0];
            float best = float.MaxValue;
            foreach (var p in Paths)
                foreach (var d in p)
                {
                    float a = AngleTo(d, ball);
                    if (a < best) { best = a; path = p; }
                }
            Vector3 center = path[0];
            best = float.MaxValue;
            foreach (var d in path)
            {
                float a = AngleTo(d, target);
                if (a < best) { best = a; center = d; }
            }
            // Then across toward the target, but not out to the edge.
            float across = Mathf.Min(best, PathHalfWidth - 2f);
            Vector3 toward = Vector3.ProjectOnPlane(target - center, center);
            var spot = toward.sqrMagnitude < 1e-10f ? center : (center + toward.normalized * (across / R)).normalized;
            return AngleTo(spot, Cage) < keepOut ? NearestGround(spot, ball, keepOut) : spot;
        }

        /// <summary>Which way is up on the ground here, from how it slopes.</summary>
        public Vector3 Normal(Vector3 dir)
        {
            dir.Normalize();
            Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized, t2 = Vector3.Cross(dir, t1);
            const float step = 0.3f;
            Vector3 Point(Vector3 d) => d.normalized * SurfaceRadius(d.normalized);
            float a = step / GroundRadius;
            Vector3 n = Vector3.Cross(Point(dir + t1 * a) - Point(dir - t1 * a), Point(dir + t2 * a) - Point(dir - t2 * a));
            if (Vector3.Dot(n, dir) < 0f) n = -n;
            return n.sqrMagnitude > 1e-10f ? n.normalized : dir;
        }

        /// <summary>Which way to face on landing (and on coming back after the lava): toward the cage.</summary>
        public Vector3 FacingFrom(Vector3 position)
        {
            Vector3 up = position.normalized;
            Vector3 dir = Vector3.ProjectOnPlane(Cage - up, up);
            return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero;
        }
    }
}
