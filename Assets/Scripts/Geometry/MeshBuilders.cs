using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SphereDescent
{
    public struct HoleCut
    {
        public Vector3 Direction;   // unit direction of the hole center
        public float AngularRadius; // radians
    }

    public static class MeshBuilders
    {
        /// <summary>
        /// A spherical shell (outer surface at <paramref name="radius"/>, inner surface at radius - thickness)
        /// with circular holes cut through it. Hole edges are snapped to a clean circle and closed with a rim.
        /// </summary>
        public static Mesh BuildShell(IcoSphere ico, float radius, float thickness, IList<HoleCut> holes)
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
                    if (Vector3.Dot(unit[v], holes[h].Direction) > cosRadii[h]) { inside[v] = true; break; }
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

            // Snap boundary vertices onto the exact hole circle for round holes.
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
                Vector3 c = holes[best].Direction;
                Vector3 perp = (unit[v] - c * bestDot).normalized;
                float ang = holes[best].AngularRadius;
                dirs[v] = (c * Mathf.Cos(ang) + perp * Mathf.Sin(ang)).normalized;
            }

            float innerRadius = radius - thickness;
            var verts = new List<Vector3>(vCount * 2 + boundary.Count * 4);
            var normals = new List<Vector3>(verts.Capacity);
            for (int v = 0; v < vCount; v++) { verts.Add(dirs[v] * radius); normals.Add(dirs[v]); }
            for (int v = 0; v < vCount; v++) { verts.Add(dirs[v] * innerRadius); normals.Add(-dirs[v]); }

            var indices = new List<int>(kept.Count * 2 + boundary.Count * 6);
            for (int i = 0; i < kept.Count; i += 3)
            {
                indices.Add(kept[i]); indices.Add(kept[i + 1]); indices.Add(kept[i + 2]);
            }
            for (int i = 0; i < kept.Count; i += 3)
            {
                indices.Add(kept[i] + vCount); indices.Add(kept[i + 2] + vCount); indices.Add(kept[i + 1] + vCount);
            }

            // Rims: quads joining outer and inner boundary edges, facing into the hole.
            foreach (var (a, b, third) in boundary)
            {
                Vector3 ao = dirs[a] * radius, bo = dirs[b] * radius;
                Vector3 bi = dirs[b] * innerRadius, ai = dirs[a] * innerRadius;
                Vector3 mid = (ao + bo) * 0.5f;
                Vector3 outward = Vector3.ProjectOnPlane(mid - dirs[third] * radius, bo - ao);
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

        /// <summary>
        /// One combined mesh for every maze wall on a sphere. Each wall follows the great-circle arc between
        /// its endpoints, sinks slightly into the ground, and is extended by half its thickness so corners close.
        /// </summary>
        public static Mesh BuildWalls(IList<(Vector3 p0, Vector3 p1)> walls, float radius, float height, float thickness)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();
            const float sink = 0.6f;
            float half = thickness * 0.5f;

            foreach (var (p0, p1) in walls)
            {
                float ang = Vector3.Angle(p0, p1) * Mathf.Deg2Rad;
                if (ang < 1e-5f) continue;
                float ext = half / radius;
                int segments = Mathf.Max(1, Mathf.CeilToInt((ang + 2f * ext) * radius / 2f));

                Vector3 prevDir = Vector3.zero, prevSide = Vector3.zero;
                for (int i = 0; i <= segments; i++)
                {
                    float t = (-ext + (ang + 2f * ext) * i / segments) / ang;
                    Vector3 dir = Vector3.SlerpUnclamped(p0, p1, t).normalized;
                    Vector3 along = Vector3.ProjectOnPlane(p1 - p0, dir).normalized;
                    Vector3 side = Vector3.Cross(dir, along).normalized * half;

                    if (i == 0) AddWallCap(verts, normals, indices, dir, side, radius, sink, height, -along);
                    if (i > 0)
                    {
                        Vector3 b0 = prevDir * (radius - sink), t0 = prevDir * (radius + height);
                        Vector3 b1 = dir * (radius - sink), t1 = dir * (radius + height);
                        Vector3 sideN = ((prevSide + side) * 0.5f).normalized;
                        Vector3 upN = ((prevDir + dir) * 0.5f).normalized;
                        AddQuad(verts, normals, indices, b0 + prevSide, t0 + prevSide, t1 + side, b1 + side, sideN);
                        AddQuad(verts, normals, indices, b0 - prevSide, t0 - prevSide, t1 - side, b1 - side, -sideN);
                        AddQuad(verts, normals, indices, t0 + prevSide, t0 - prevSide, t1 - side, t1 + side, upN);
                    }
                    if (i == segments) AddWallCap(verts, normals, indices, dir, side, radius, sink, height, along);
                    prevDir = dir;
                    prevSide = side;
                }
            }

            return MakeMesh("Walls", verts, normals, indices);
        }

        static void AddWallCap(List<Vector3> verts, List<Vector3> normals, List<int> indices,
            Vector3 dir, Vector3 side, float radius, float sink, float height, Vector3 facing)
        {
            Vector3 b = dir * (radius - sink), t = dir * (radius + height);
            AddQuad(verts, normals, indices, b + side, t + side, t - side, b - side, facing);
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
