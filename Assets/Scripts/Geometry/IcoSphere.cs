using System.Collections.Generic;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// Unit icosphere (radius 1). Triangles are wound so they face outward in Unity
    /// (cross(b - a, c - a) points away from the center). Results are cached per subdivision level.
    /// </summary>
    public sealed class IcoSphere
    {
        public readonly Vector3[] Vertices;
        public readonly int[] Triangles;

        int[] edgeNeighbors;

        /// <summary>
        /// For triangle edge t*3+k (from vertex k to vertex k+1), the triangle on the other side of that edge.
        /// Built once on first use.
        /// </summary>
        public int[] EdgeNeighbors => edgeNeighbors ??= BuildEdgeNeighbors();

        static readonly Dictionary<int, IcoSphere> Cache = new Dictionary<int, IcoSphere>();
        static readonly Dictionary<int, IcoSphere> GeodesicCache = new Dictionary<int, IcoSphere>();

        public static IcoSphere Get(int subdivisions)
        {
            if (!Cache.TryGetValue(subdivisions, out var sphere))
            {
                sphere = new IcoSphere(subdivisions);
                Cache[subdivisions] = sphere;
            }
            return sphere;
        }

        /// <summary>
        /// Geodesic sphere where each icosahedron edge is split into <paramref name="frequency"/> segments
        /// (any whole number, unlike <see cref="Get"/> which only doubles). Frequency 2^n matches subdivision n.
        /// </summary>
        public static IcoSphere Geodesic(int frequency)
        {
            frequency = Mathf.Max(1, frequency);
            if (!GeodesicCache.TryGetValue(frequency, out var sphere))
            {
                sphere = BuildGeodesic(frequency);
                GeodesicCache[frequency] = sphere;
            }
            return sphere;
        }

        static IcoSphere BuildGeodesic(int n)
        {
            var baseIco = Get(0);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            // Shared points along icosahedron edges are deduplicated by quantized position.
            var lookup = new Dictionary<Vector3Int, int>();
            int Point(Vector3 p)
            {
                p.Normalize();
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 100000f), Mathf.RoundToInt(p.y * 100000f), Mathf.RoundToInt(p.z * 100000f));
                if (lookup.TryGetValue(key, out int idx)) return idx;
                idx = verts.Count;
                verts.Add(p);
                lookup[key] = idx;
                return idx;
            }

            var bt = baseIco.Triangles;
            var bv = baseIco.Vertices;
            var row = new int[n + 1][];
            for (int f = 0; f < bt.Length; f += 3)
            {
                Vector3 a = bv[bt[f]], b = bv[bt[f + 1]], c = bv[bt[f + 2]];
                for (int i = 0; i <= n; i++)
                {
                    row[i] = new int[n - i + 1];
                    for (int j = 0; j <= n - i; j++)
                        row[i][j] = Point(a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n));
                }
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n - i; j++)
                    {
                        tris.Add(row[i][j]); tris.Add(row[i + 1][j]); tris.Add(row[i][j + 1]);
                        if (j < n - i - 1)
                        {
                            tris.Add(row[i + 1][j]); tris.Add(row[i + 1][j + 1]); tris.Add(row[i][j + 1]);
                        }
                    }
                }
            }
            return new IcoSphere(verts, tris);
        }

        IcoSphere(List<Vector3> verts, List<int> tris)
        {
            FixWinding(verts, tris);
            Vertices = verts.ToArray();
            Triangles = tris.ToArray();
        }

        IcoSphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;

            var tris = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var midCache = new Dictionary<long, int>();
                var next = new List<int>(tris.Count * 4);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Midpoint(a, b, verts, midCache);
                    int bc = Midpoint(b, c, verts, midCache);
                    int ca = Midpoint(c, a, verts, midCache);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
            }

            FixWinding(verts, tris);
            Vertices = verts.ToArray();
            Triangles = tris.ToArray();
        }

        /// <summary>Enforces outward winding.</summary>
        static void FixWinding(List<Vector3> verts, List<int> tris)
        {
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                {
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                }
            }
        }

        int[] BuildEdgeNeighbors()
        {
            // Sort (edge key, half-edge index) pairs; the two half-edges of each edge end up adjacent.
            int halfEdges = Triangles.Length;
            long v = Vertices.Length;
            var keys = new long[halfEdges];
            for (int i = 0; i < halfEdges; i++)
            {
                int a = Triangles[i];
                int b = Triangles[i % 3 == 2 ? i - 2 : i + 1];
                long edge = a < b ? a * v + b : b * v + a;
                keys[i] = (edge << 22) | (long)i;
            }
            System.Array.Sort(keys);
            var result = new int[halfEdges];
            for (int i = 0; i + 1 < halfEdges; i += 2)
            {
                int h0 = (int)(keys[i] & 0x3fffff), h1 = (int)(keys[i + 1] & 0x3fffff);
                result[h0] = h1 / 3;
                result[h1] = h0 / 3;
            }
            return result;
        }

        static int Midpoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int index)) return index;
            index = verts.Count;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = index;
            return index;
        }
    }
}
