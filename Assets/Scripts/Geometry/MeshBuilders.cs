using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SphereDescent
{
    public struct HoleCut
    {
        public Vector3 Direction;   // unit direction of the hole center
        public float AngularRadius; // radians (for a round hole)
        // A hole of any shape instead: whether a unit direction is inside it, and how to move a point just
        // outside it onto its edge.
        public System.Func<Vector3, bool> Inside;
        public System.Func<Vector3, Vector3> SnapToEdge;
    }

    public static class MeshBuilders
    {
        /// <summary>
        /// A spherical shell's walkable surface at <paramref name="radius"/> with circular holes cut through it.
        /// Hole edges are snapped to a clean circle and lined with a rim reaching <paramref name="thickness"/>
        /// down. There's no underside: only the layer you're on is drawn, so it's never seen, and nothing can
        /// reach it. With <paramref name="terrain"/>, the surface rises into hills.
        /// </summary>
        public static Mesh BuildShell(IcoSphere ico, float radius, float thickness, IList<HoleCut> holes, ShellTerrain terrain = null)
        {
            var unit = ico.Vertices;
            var tris = ico.Triangles;
            int vCount = unit.Length;

            // Which vertices fall inside a hole.
            var inside = new bool[vCount];
            var cosRadii = new float[holes.Count];
            for (int h = 0; h < holes.Count; h++) cosRadii[h] = Mathf.Cos(holes[h].AngularRadius);
            for (int v = 0; v < vCount; v++)
            {
                for (int h = 0; h < holes.Count; h++)
                {
                    bool inHole = holes[h].Inside != null ? holes[h].Inside(unit[v]) : Vector3.Dot(unit[v], holes[h].Direction) > cosRadii[h];
                    if (inHole) { inside[v] = true; break; }
                }
            }

            // Keep triangles with no vertex inside a hole. A kept triangle's edge is on the boundary when
            // the triangle across it was removed.
            int triCount = tris.Length / 3;
            var keep = new bool[triCount];
            for (int t = 0; t < triCount; t++)
                keep[t] = !inside[tris[t * 3]] && !inside[tris[t * 3 + 1]] && !inside[tris[t * 3 + 2]];

            var across = ico.EdgeNeighbors;
            var kept = new List<int>(tris.Length);
            var boundary = new List<(int a, int b, int third)>();
            var snapped = new HashSet<int>();
            for (int t = 0; t < triCount; t++)
            {
                if (!keep[t]) continue;
                kept.Add(tris[t * 3]); kept.Add(tris[t * 3 + 1]); kept.Add(tris[t * 3 + 2]);
                for (int k = 0; k < 3; k++)
                {
                    if (keep[across[t * 3 + k]]) continue;
                    int a = tris[t * 3 + k], b = tris[t * 3 + (k + 1) % 3], c = tris[t * 3 + (k + 2) % 3];
                    boundary.Add((a, b, c));
                    snapped.Add(a);
                    snapped.Add(b);
                }
            }

            var dirs = (Vector3[])unit.Clone();

            // Snap boundary vertices onto the exact edge of their hole: a circle, or the hole's own shape.
            foreach (int v in snapped)
            {
                int best = -1;
                float bestDot = -2f;
                for (int h = 0; h < holes.Count; h++)
                {
                    float d = Vector3.Dot(unit[v], holes[h].Direction);
                    if (d > bestDot) { bestDot = d; best = h; }
                }
                if (best < 0) continue;
                if (holes[best].SnapToEdge != null) { dirs[v] = holes[best].SnapToEdge(unit[v]).normalized; continue; }
                Vector3 c = holes[best].Direction;
                Vector3 perp = (unit[v] - c * bestDot).normalized;
                float ang = holes[best].AngularRadius;
                dirs[v] = (c * Mathf.Cos(ang) + perp * Mathf.Sin(ang)).normalized;
            }

            float innerRadius = radius - thickness;
            var verts = new List<Vector3>(vCount + boundary.Count * 4);
            var normals = new List<Vector3>(verts.Capacity);
            for (int v = 0; v < vCount; v++)
            {
                verts.Add(dirs[v] * (terrain != null ? terrain.SurfaceRadius(dirs[v]) : radius));
                normals.Add(dirs[v]);
            }
            if (terrain != null)
            {
                // Hillsides lean, so outer normals come from the surrounding triangles (area weighted).
                var sum = new Vector3[vCount];
                for (int i = 0; i < kept.Count; i += 3)
                {
                    int a = kept[i], b = kept[i + 1], c = kept[i + 2];
                    Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                    if (Vector3.Dot(n, verts[a]) < 0f) n = -n;
                    sum[a] += n; sum[b] += n; sum[c] += n;
                }
                for (int v = 0; v < vCount; v++)
                    if (sum[v] != Vector3.zero) normals[v] = sum[v].normalized;
            }

            var indices = new List<int>(kept.Count + boundary.Count * 6);
            indices.AddRange(kept);

            // Rims: quads hanging down from the boundary edges, facing into the hole.
            foreach (var (a, b, third) in boundary)
            {
                Vector3 ao = verts[a], bo = verts[b];
                Vector3 bi = dirs[b] * innerRadius, ai = dirs[a] * innerRadius;
                Vector3 mid = (ao + bo) * 0.5f;
                Vector3 outward = Vector3.ProjectOnPlane(mid - verts[third], bo - ao);
                outward = Vector3.ProjectOnPlane(outward, mid.normalized).normalized;
                AddQuad(verts, normals, indices, ao, bo, bi, ai, outward);
            }

            return MakeMesh("Shell", verts, normals, indices);
        }

        /// <summary>A solid sphere surface (used for the core).</summary>
        public static Mesh BuildSphere(IcoSphere ico, float radius)
        {
            var verts = new List<Vector3>(ico.Vertices.Length);
            var normals = new List<Vector3>(ico.Vertices.Length);
            foreach (var v in ico.Vertices) { verts.Add(v * radius); normals.Add(v); }
            return MakeMesh("Core", verts, normals, new List<int>(ico.Triangles));
        }

        /// <summary>Identifies a wall corner at a joint: the joint point, the two walls, and inside/outside.</summary>
        readonly struct CornerKey : System.IEquatable<CornerKey>
        {
            readonly Vector3 point;
            readonly int wallA, wallB;
            readonly bool outside;

            public CornerKey(Vector3 point, int wall, int otherWall, bool outside)
            {
                this.point = point;
                wallA = Mathf.Min(wall, otherWall);
                wallB = Mathf.Max(wall, otherWall);
                this.outside = outside;
            }

            public bool Equals(CornerKey o) => point.Equals(o.point) && wallA == o.wallA && wallB == o.wallB && outside == o.outside;
            public override bool Equals(object obj) => obj is CornerKey o && Equals(o);
            public override int GetHashCode() => (point.GetHashCode() * 397 ^ wallA * 31 ^ wallB) * 2 + (outside ? 1 : 0);
        }

        /// <summary>
        /// One combined mesh for every maze wall on a sphere. Each wall follows the great-circle arc between
        /// its endpoints and sinks slightly into the ground. Ends are shaped by what meets them, so walls join
        /// without seams or poking corners: each side of a wall that meets another wall is mitered against it,
        /// so at a bend the two walls meet along the line that splits the corner, and where three meet they
        /// leave a small triangle in the middle that gets a cap on top. A free end extends by half the
        /// thickness. Walls meeting at a joint reuse the exact same corner vertices (and the cap uses them too),
        /// so joints are watertight rather than relying on separately computed points lining up. Normals are
        /// per vertex, so the short flat pieces that make up each curved wall shade as one smooth surface.
        /// With <paramref name="terrain"/>, each side of a wall rises and falls with the ground beneath it.
        /// UV x holds the ground radius under each vertex, so the shader can measure height above the floor.
        /// </summary>
        public static Mesh BuildWalls(IList<(Vector3 p0, Vector3 p1)> walls, float radius, float height, float thickness,
            ShellTerrain terrain = null)
        {
            float Ground(Vector3 dir) => terrain != null ? terrain.SurfaceRadius(dir) : radius;
            float pieceLength = terrain != null ? terrain.DetailLength(2f) : 2f;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();
            const float sink = 0.6f;
            float half = thickness * 0.5f;

            // Walls meeting at each endpoint. Shared endpoints are computed identically, so exact keys match.
            var junctions = new Dictionary<Vector3, List<int>>();
            for (int w = 0; w < walls.Count; w++)
            {
                foreach (var p in new[] { walls[w].p0, walls[w].p1 })
                {
                    if (!junctions.TryGetValue(p, out var list)) junctions[p] = list = new List<int>(3);
                    list.Add(w);
                }
            }

            // Unit direction along the surface from endpoint p into wall w.
            Vector3 Into(int w, Vector3 p)
            {
                Vector3 other = walls[w].p0 == p ? walls[w].p1 : walls[w].p0;
                return Vector3.ProjectOnPlane(other - p, p).normalized;
            }

            // How far (radians, along the arc beyond endpoint p) each side of wall w runs, and which shared
            // corner each side ends on. "Left" is the side Cross(up, into the wall) points to.
            void EndExtension(int w, Vector3 p, out float left, out float right, out CornerKey? leftCorner, out CornerKey? rightCorner)
            {
                var meeting = junctions[p];
                left = right = half / radius;
                leftCorner = rightCorner = null;
                if (meeting.Count == 1) return;
                Vector3 into = Into(w, p);
                Vector3 leftDir = Vector3.Cross(p, into);
                // Miter each side against the neighboring wall on that side: the side facing a wall is cut
                // short so the two meet along the line splitting the angle between them. At a bend the outer
                // side, with no wall to face, runs on just as far to meet the other wall's outer corner.
                float leftMiter = float.NaN, rightMiter = float.NaN;
                int leftWall = -1, rightWall = -1;
                foreach (int o in meeting)
                {
                    if (o == w) continue;
                    Vector3 other = Into(o, p);
                    float angle = Vector3.Angle(into, other) * Mathf.Deg2Rad;
                    float miter = Mathf.Clamp(half / Mathf.Tan(angle * 0.5f), 0f, 2f * half) / radius;
                    if (Vector3.Dot(leftDir, other) > 0f)
                    {
                        if (float.IsNaN(leftMiter) || miter < leftMiter) { leftMiter = miter; leftWall = o; }
                    }
                    else if (float.IsNaN(rightMiter) || miter < rightMiter) { rightMiter = miter; rightWall = o; }
                }
                // A side with no wall to face (the outside of a bend) shares its corner with the other wall's
                // outside instead.
                left = float.IsNaN(leftMiter) ? rightMiter : -leftMiter;
                right = float.IsNaN(rightMiter) ? leftMiter : -rightMiter;
                leftCorner = leftWall >= 0 ? new CornerKey(p, w, leftWall, false) : new CornerKey(p, w, rightWall, true);
                rightCorner = rightWall >= 0 ? new CornerKey(p, w, rightWall, false) : new CornerKey(p, w, leftWall, true);
            }

            // Corner vertices (bottom, top) shared by the walls meeting at each joint: the first wall to reach
            // a joint stores its corner and the other reuses it exactly.
            var sharedCorners = new Dictionary<CornerKey, (Vector3 bottom, Vector3 top)>();
            void ShareCorner(CornerKey? key, ref Vector3 bottom, ref Vector3 top)
            {
                if (key == null) return;
                if (sharedCorners.TryGetValue(key.Value, out var shared)) { bottom = shared.bottom; top = shared.top; }
                else sharedCorners[key.Value] = (bottom, top);
            }

            // Top corners of the walls around each three-way junction, which outline the cap's triangle.
            var capCorners = new Dictionary<Vector3, List<Vector3>>();

            for (int w = 0; w < walls.Count; w++)
            {
                var (p0, p1) = walls[w];
                float ang = Vector3.Angle(p0, p1) * Mathf.Deg2Rad;
                if (ang < 1e-5f) continue;
                // Sides as seen walking from p0 to p1: "a" is the side Cross(up, along) points to.
                EndExtension(w, p0, out float startA, out float startB, out var startCornerA, out var startCornerB);
                // From p1, "into the wall" points back toward p0, which swaps left and right.
                EndExtension(w, p1, out float endB, out float endA, out var endCornerB, out var endCornerA);
                int segments = Mathf.Max(1, Mathf.CeilToInt((ang + 2f * half / radius) * radius / pieceLength));

                Vector3 prevA = Vector3.zero, prevB = Vector3.zero, prevSideA = Vector3.zero, prevSideB = Vector3.zero;
                Vector3 pBaseA = Vector3.zero, pTopA = Vector3.zero, pBaseB = Vector3.zero, pTopB = Vector3.zero;
                for (int i = 0; i <= segments; i++)
                {
                    float f = (float)i / segments;
                    // Both sides follow the same arc but can start and end at different points (miters).
                    Vector3 dirA = Vector3.SlerpUnclamped(p0, p1, (-startA + (ang + startA + endA) * f) / ang).normalized;
                    Vector3 dirB = Vector3.SlerpUnclamped(p0, p1, (-startB + (ang + startB + endB) * f) / ang).normalized;
                    Vector3 sideA = Vector3.Cross(dirA, Vector3.ProjectOnPlane(p1 - p0, dirA)).normalized;
                    Vector3 sideB = Vector3.Cross(dirB, Vector3.ProjectOnPlane(p1 - p0, dirB)).normalized;
                    // Each side stands on the ground directly beneath it.
                    Vector3 footA = (dirA * radius + sideA * half).normalized, footB = (dirB * radius - sideB * half).normalized;
                    float groundA = Ground(footA), groundB = Ground(footB);
                    Vector3 baseA = footA * (groundA - sink), topA = footA * (groundA + height);
                    Vector3 baseB = footB * (groundB - sink), topB = footB * (groundB + height);
                    if (i == 0)
                    {
                        ShareCorner(startCornerA, ref baseA, ref topA);
                        ShareCorner(startCornerB, ref baseB, ref topB);
                    }
                    if (i == segments)
                    {
                        ShareCorner(endCornerA, ref baseA, ref topA);
                        ShareCorner(endCornerB, ref baseB, ref topB);
                    }

                    Vector3 endPoint = i == 0 ? p0 : p1;
                    bool hub = (i == 0 || i == segments) && junctions[endPoint].Count >= 3;
                    if (hub)
                    {
                        // The end face is hidden under the junction cap; just record the left top corner
                        // (seen from the junction), which the next wall around shares as its right one.
                        if (!capCorners.TryGetValue(endPoint, out var corners)) capCorners[endPoint] = corners = new List<Vector3>(3);
                        corners.Add(i == 0 ? topA : topB);
                    }
                    else if (i == 0 || i == segments)
                    {
                        // End face, square or mitered.
                        Vector3 facing = Vector3.Cross(topB - baseA, topA - baseB).normalized;
                        Vector3 outward = Vector3.ProjectOnPlane(p1 - p0, dirA) * (i == 0 ? -1f : 1f);
                        if (Vector3.Dot(facing, outward) < 0f) facing = -facing;
                        AddQuad(verts, normals, indices, baseA, topA, topB, baseB, facing);
                    }
                    if (i > 0)
                    {
                        AddQuad(verts, normals, indices, pBaseA, pTopA, topA, baseA, prevSideA, prevSideA, sideA, sideA);
                        AddQuad(verts, normals, indices, pBaseB, pTopB, topB, baseB, -prevSideB, -prevSideB, -sideB, -sideB);
                        AddQuad(verts, normals, indices, pTopA, pTopB, topB, topA, prevA, prevB, dirB, dirA);
                    }
                    prevA = dirA;
                    prevB = dirB;
                    prevSideA = sideA;
                    prevSideB = sideB;
                    pBaseA = baseA;
                    pTopA = topA;
                    pBaseB = baseB;
                    pTopB = topB;
                }
            }

            // Cap the small triangle left in the middle of each three-way junction.
            foreach (var kv in capCorners)
            {
                if (kv.Value.Count != 3) continue;
                Vector3 up = kv.Key.normalized;
                var c = kv.Value;
                int i0 = verts.Count;
                verts.Add(c[0]); verts.Add(c[1]); verts.Add(c[2]);
                normals.Add(up); normals.Add(up); normals.Add(up);
                bool facesUp = Vector3.Dot(Vector3.Cross(c[1] - c[0], c[2] - c[0]), up) >= 0f;
                indices.Add(i0); indices.Add(facesUp ? i0 + 1 : i0 + 2); indices.Add(facesUp ? i0 + 2 : i0 + 1);
            }

            var mesh = MakeMesh("Walls", verts, normals, indices);
            var uvs = new List<Vector2>(verts.Count);
            foreach (var v in verts) uvs.Add(new Vector2(Ground(v), 0f));
            mesh.SetUVs(0, uvs);
            return mesh;
        }

        /// <summary>Flat disc in the local XZ plane facing +Y, centered on the origin. UVs run 0-1 across it.</summary>
        public static Mesh BuildDisc(float radius, int segments = 32)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var normals = new List<Vector3> { Vector3.up };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var indices = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
                // Clockwise seen from above, so the disc faces +Y.
                indices.Add(0); indices.Add(1 + (i + 1) % segments); indices.Add(1 + i);
            }
            var mesh = MakeMesh("Disc", verts, normals, indices);
            mesh.SetUVs(0, uvs);
            return mesh;
        }

        /// <summary>
        /// Open cone with its tip at the origin, widening along +Z to a circle of radius 1 at z = 1. UV y runs
        /// 0 at the wide end to 1 at the tip. Scale it to aim and size it.
        /// </summary>
        public static Mesh BuildCone(int segments = 24)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                Vector3 rim = new Vector3(Mathf.Cos(u * Mathf.PI * 2f), Mathf.Sin(u * Mathf.PI * 2f), 1f);
                Vector3 n = new Vector3(rim.x, rim.y, -1f).normalized;
                verts.Add(rim); normals.Add(n); uvs.Add(new Vector2(u, 0f));
                verts.Add(Vector3.zero); normals.Add(n); uvs.Add(new Vector2(u, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                indices.Add(a); indices.Add(a + 1); indices.Add(a + 2);
            }
            var mesh = MakeMesh("Cone", verts, normals, indices);
            mesh.SetUVs(0, uvs);
            return mesh;
        }

        /// <summary>Open tube along local +Y from 0 to <paramref name="height"/>; UV y runs 0 at the base to 1 at the top.</summary>
        public static Mesh BuildBeam(float radius, float height, int segments = 24)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                Vector3 n = new Vector3(Mathf.Cos(u * Mathf.PI * 2f), 0f, Mathf.Sin(u * Mathf.PI * 2f));
                verts.Add(n * radius); normals.Add(n); uvs.Add(new Vector2(u, 0f));
                verts.Add(n * radius + Vector3.up * height); normals.Add(n); uvs.Add(new Vector2(u, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                indices.Add(a); indices.Add(a + 1); indices.Add(a + 3);
                indices.Add(a); indices.Add(a + 3); indices.Add(a + 2);
            }
            var mesh = MakeMesh("Beam", verts, normals, indices);
            mesh.SetUVs(0, uvs);
            return mesh;
        }

        /// <summary>Torus lying in the local XZ plane, centered on the origin.</summary>
        public static Mesh BuildTorus(float ringRadius, float tubeRadius, int ringSegments = 48, int tubeSegments = 12)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();
            for (int i = 0; i <= ringSegments; i++)
            {
                float u = i / (float)ringSegments * Mathf.PI * 2f;
                Vector3 ringDir = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));
                for (int j = 0; j <= tubeSegments; j++)
                {
                    float v = j / (float)tubeSegments * Mathf.PI * 2f;
                    Vector3 n = ringDir * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v);
                    verts.Add(ringDir * ringRadius + n * tubeRadius);
                    normals.Add(n);
                }
            }
            int stride = tubeSegments + 1;
            for (int i = 0; i < ringSegments; i++)
            {
                for (int j = 0; j < tubeSegments; j++)
                {
                    int a = i * stride + j, b = (i + 1) * stride + j, c = (i + 1) * stride + j + 1, d = i * stride + j + 1;
                    bool facesOut = Vector3.Dot(Vector3.Cross(verts[d] - verts[a], verts[c] - verts[a]), normals[a]) >= 0f;
                    if (facesOut)
                    {
                        indices.Add(a); indices.Add(d); indices.Add(c);
                        indices.Add(a); indices.Add(c); indices.Add(b);
                    }
                    else
                    {
                        indices.Add(a); indices.Add(c); indices.Add(d);
                        indices.Add(a); indices.Add(b); indices.Add(c);
                    }
                }
            }
            return MakeMesh("Torus", verts, normals, indices);
        }

        /// <summary>Adds a flat quad with the given normal; winding is chosen so the quad faces that normal.</summary>
        static void AddQuad(List<Vector3> verts, List<Vector3> normals, List<int> indices,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal)
        {
            int i = verts.Count;
            verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
            normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
            if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), normal) >= 0f)
            {
                indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
                indices.Add(i); indices.Add(i + 2); indices.Add(i + 3);
            }
            else
            {
                indices.Add(i); indices.Add(i + 2); indices.Add(i + 1);
                indices.Add(i); indices.Add(i + 3); indices.Add(i + 2);
            }
        }

        /// <summary>
        /// Adds a quad with a normal per vertex (for smooth shading across neighboring quads); winding is
        /// chosen so the quad faces the average normal.
        /// </summary>
        static void AddQuad(List<Vector3> verts, List<Vector3> normals, List<int> indices,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 n0, Vector3 n1, Vector3 n2, Vector3 n3)
        {
            AddQuad(verts, normals, indices, v0, v1, v2, v3, n0 + n1 + n2 + n3);
            normals[normals.Count - 4] = n0.normalized;
            normals[normals.Count - 3] = n1.normalized;
            normals[normals.Count - 2] = n2.normalized;
            normals[normals.Count - 1] = n3.normalized;
        }

        /// <summary>
        /// Splits a mesh into pieces by which of <paramref name="centers"/> (unit directions) each triangle's
        /// middle lies nearest to, keeping vertex positions, normals and UVs exactly, so the pieces fit back
        /// together seamlessly. Entries are null for centers with no triangles.
        /// </summary>
        public static Mesh[] SplitByDirection(Mesh mesh, Vector3[] centers, string name)
        {
            var verts = mesh.vertices;
            var normals = mesh.normals;
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            var tris = mesh.triangles;
            var pieceTris = new List<int>[centers.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 mid = (verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]).normalized;
                int best = 0;
                float bestDot = -2f;
                for (int c = 0; c < centers.Length; c++)
                {
                    float d = Vector3.Dot(mid, centers[c]);
                    if (d > bestDot) { bestDot = d; best = c; }
                }
                (pieceTris[best] ??= new List<int>()).AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
            }
            var pieces = new Mesh[centers.Length];
            for (int c = 0; c < centers.Length; c++)
            {
                if (pieceTris[c] == null) continue;
                var remap = new Dictionary<int, int>();
                var pv = new List<Vector3>();
                var pn = new List<Vector3>();
                var pu = new List<Vector2>();
                var pt = new List<int>(pieceTris[c].Count);
                foreach (int v in pieceTris[c])
                {
                    if (!remap.TryGetValue(v, out int nv))
                    {
                        remap[v] = nv = pv.Count;
                        pv.Add(verts[v]);
                        pn.Add(normals[v]);
                        if (uvs.Count > 0) pu.Add(uvs[v]);
                    }
                    pt.Add(nv);
                }
                var piece = MakeMesh(name, pv, pn, pt);
                if (pu.Count > 0) piece.SetUVs(0, pu);
                pieces[c] = piece;
            }
            return pieces;
        }

        static Mesh MakeMesh(string name, List<Vector3> verts, List<Vector3> normals, List<int> indices)
        {
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
