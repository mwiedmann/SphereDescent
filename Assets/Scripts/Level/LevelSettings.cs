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
        [Tooltip("Number of disconnected regions on each shell. More regions = more travel between layers.")]
        public int[] regionsPerShell = { 2, 3, 3, 2 };
        [Tooltip("Holes per 100 maze cells on each shell. Holes in the last shell drop into the core.")]
        public float holeDensity = 5f;
        [Tooltip("Require that the core can't be reached by dropping alone, forcing at least one boost-pad climb.")]
        public bool requireClimb = false;
        [Range(0f, 1f)] public float twoWayChance = 0.5f;
        [Range(0f, 1f)] public float loopChance = 0.15f;
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

        public int RegionsFor(int shell) =>
            Mathf.Max(1, regionsPerShell.Length > 0 ? regionsPerShell[Mathf.Min(shell, regionsPerShell.Length - 1)] : 1);

        public int HolesFor(int cellCount) => Mathf.Max(1, Mathf.RoundToInt(cellCount * holeDensity / 100f));

        /// <summary>Boost pad radius for a hole of the given radius.</summary>
        public static float PadRadius(float holeRadius) => Mathf.Max(0.9f, holeRadius * 0.85f);

        public float HoleRadius(SphereGrid grid, int shell) =>
            Mathf.Max(minHoleRadius, holeRadiusFactor * grid.CellSpacing * shellRadii[shell]);

        /// <summary>Radius of the walkable surface for a layer index (shells, then the core).</summary>
        public float SurfaceRadius(int layer) => layer < ShellCount ? shellRadii[layer] : coreRadius;
    }
}
