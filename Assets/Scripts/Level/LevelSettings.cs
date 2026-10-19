using System;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>Tunable shape of a world. Shell 0 is the outermost (the surface); the core is below the last shell.</summary>
    [Serializable]
    public class LevelSettings
    {
        [Tooltip("Outer radius of each walkable shell, outermost first.")]
        public float[] shellRadii = { 80f, 66f, 52f, 38f };
        public float coreRadius = 22f;
        public float shellThickness = 1f;

        [Header("Maze")]
        [Tooltip("Distance between neighbouring maze cells (corridor width) in world units, the same on every layer. " +
                 "Smaller inner spheres get fewer cells rather than narrower corridors.")]
        public float cellSpacing = 10.5f;
        [Tooltip("Times per world you have to climb back up a layer on a boost pad. Each climb adds two sections.")]
        public int climbsPerWorld = 3;
        [Tooltip("Length of each section's main path in maze cells (about Cell Spacing units each). " +
                 "Every section ends at a hole down or a boost pad up.")]
        public int sectionLengthMin = 10;
        public int sectionLengthMax = 18;
        [Tooltip("How often a cell beside the main path becomes part of a side lane. Higher = wider sections with more alternative routes.")]
        [Range(0f, 1f)] public float sideLaneChance = 0.5f;
        [Tooltip("How often a step of the main path is walled off, so the route has to detour through a side lane.")]
        [Range(0f, 1f)] public float mainPathCutChance = 0.35f;
        public float wallHeight = 4f;
        public float wallThickness = 0.6f;

        [Header("Holes")]
        [Tooltip("Hole radius as a fraction of the distance between neighbouring cells.")]
        public float holeRadiusFactor = 0.2f;
        public float minHoleRadius = 1f;

        [Header("Orientation")]
        [Tooltip("Glowing pillar near the center of each colored zone on every shell.")]
        public bool landmarks = true;

        [Header("Mesh detail")]
        public int shellMeshSubdivisions = 6;
        public int coreMeshSubdivisions = 5;

        public int ShellCount => shellRadii.Length;

        /// <summary>Boost pad radius for a hole of the given radius.</summary>
        public static float PadRadius(float holeRadius) => Mathf.Max(0.9f, holeRadius * 0.85f);

        public float HoleRadius(SphereGrid grid, int shell) =>
            Mathf.Max(minHoleRadius, holeRadiusFactor * grid.CellSpacing * shellRadii[shell]);

        /// <summary>Radius of the walkable surface for a layer index (shells, then the core).</summary>
        public float SurfaceRadius(int layer) => layer < ShellCount ? shellRadii[layer] : coreRadius;
    }
}
