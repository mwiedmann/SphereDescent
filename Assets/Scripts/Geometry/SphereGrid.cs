using System.Collections.Generic;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// Hex/pentagon cell grid on a unit sphere (the dual of an icosphere). Each icosphere vertex is a
    /// maze cell; each icosphere edge separates two cells and corresponds to one potential wall, which
    /// runs between the centroids of the two triangles sharing that edge.
    /// </summary>
    public sealed class SphereGrid
    {
        public struct Edge
        {
            public int A, B;          // the two cells this wall separates
            public Vector3 P0, P1;    // wall endpoints on the unit sphere
        }

        public readonly Vector3[] CellCenters;
        public readonly int[][] Neighbors;
        public readonly Edge[] Edges;
        /// <summary>Average angular distance (radians) between neighbouring cell centers.</summary>
        public readonly float CellSpacing;

        readonly Dictionary<long, int> edgeLookup = new Dictionary<long, int>();

        static readonly Dictionary<int, SphereGrid> Cache = new Dictionary<int, SphereGrid>();

        public int CellCount => CellCenters.Length;

        /// <summary>Grid whose cells are the vertices of a geodesic sphere of the given frequency.</summary>
        public static SphereGrid Get(int frequency)
        {
            if (!Cache.TryGetValue(frequency, out var grid))
            {
                grid = new SphereGrid(IcoSphere.Geodesic(frequency));
                Cache[frequency] = grid;
            }
            return grid;
        }

        /// <summary>
        /// Grid for a sphere of this radius whose neighbouring cell centers are about
        /// <paramref name="spacing"/> world units apart. Smaller spheres get fewer, not smaller, cells.
        /// </summary>
        public static SphereGrid ForRadius(float radius, float spacing)
        {
            // Neighbour spacing is roughly 1.2/n radians for frequency n; check the nearby frequencies'
            // actual spacing and keep the closest.
            int guess = Mathf.Max(1, Mathf.RoundToInt(radius * 1.2f / spacing));
            SphereGrid best = null;
            float bestError = float.MaxValue;
            for (int n = Mathf.Max(1, guess - 1); n <= guess + 1; n++)
            {
                var grid = Get(n);
                float error = Mathf.Abs(grid.CellSpacing * radius - spacing);
                if (error < bestError) { bestError = error; best = grid; }
            }
            return best;
        }

        public int NearestCell(Vector3 direction)
        {
            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < CellCenters.Length; i++)
            {
                float d = Vector3.Dot(direction, CellCenters[i]);
                if (d > bestDot) { bestDot = d; best = i; }
            }
            return best;
        }

        SphereGrid(IcoSphere ico)
        {
            CellCenters = ico.Vertices;
            var tris = ico.Triangles;
            var neighborSets = new List<int>[CellCenters.Length];
            for (int i = 0; i < neighborSets.Length; i++) neighborSets[i] = new List<int>(6);

            var edgeFaces = new Dictionary<long, List<int>>();
            for (int f = 0; f < tris.Length / 3; f++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = tris[f * 3 + k], b = tris[f * 3 + (k + 1) % 3];
                    long key = Key(a, b);
                    if (!edgeFaces.TryGetValue(key, out var list))
                    {
                        list = new List<int>(2);
                        edgeFaces[key] = list;
                        neighborSets[a].Add(b);
                        neighborSets[b].Add(a);
                    }
                    list.Add(f);
                }
            }

            var edges = new List<Edge>(edgeFaces.Count);
            float spacingSum = 0f;
            foreach (var kv in edgeFaces)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                edgeLookup[kv.Key] = edges.Count;
                edges.Add(new Edge
                {
                    A = a,
                    B = b,
                    P0 = FaceCentroid(ico, kv.Value[0]),
                    P1 = FaceCentroid(ico, kv.Value[1]),
                });
                spacingSum += Vector3.Angle(CellCenters[a], CellCenters[b]) * Mathf.Deg2Rad;
            }

            Edges = edges.ToArray();
            CellSpacing = spacingSum / edges.Count;
            Neighbors = new int[CellCenters.Length][];
            for (int i = 0; i < Neighbors.Length; i++) Neighbors[i] = neighborSets[i].ToArray();
        }

        public int EdgeBetween(int a, int b) => edgeLookup.TryGetValue(Key(a, b), out int e) ? e : -1;

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static Vector3 FaceCentroid(IcoSphere ico, int f)
        {
            var t = ico.Triangles;
            var v = ico.Vertices;
            return (v[t[f * 3]] + v[t[f * 3 + 1]] + v[t[f * 3 + 2]]).normalized;
        }
    }
}
