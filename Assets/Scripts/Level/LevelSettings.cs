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
        public float cellSpacing = 16f;
        [Tooltip("Times per world you have to climb back up a layer on a boost pad. Each climb adds two sections.")]
        public int climbsPerWorld = 3;
        [Tooltip("Length of each section's main path in maze cells (about Cell Spacing units each). " +
                 "Every section ends at a hole down or a boost pad up.")]
        public int sectionLengthMin = 11;
        public int sectionLengthMax = 19;
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

        [Header("Hills")]
        [Tooltip("Tallest hill in world units on the Hill Reference Shell (0 = flat shells). Hills on other shells are scaled " +
                 "(height and size) by their radius, so they look the same relative to the sphere. Walls follow the ground, " +
                 "so this doesn't change how high they are.")]
        public float hillHeight = 3.5f;
        [Tooltip("Shell (0 = outermost) whose hills are exactly Hill Height and Hill Size. Bigger shells get bigger hills, smaller ones smaller.")]
        public int hillReferenceShell = 2;
        [Tooltip("Typical hill radius in world units on the Hill Reference Shell. Bigger = broader, gentler slopes.")]
        public float hillSize = 5f;
        [Tooltip("Roughly how much of each shell is covered by hills.")]
        [Range(0f, 2f)] public float hillCoverage = 1.2f;
        [Tooltip("Ground stays flat this far beyond the edge of each hole and boost pad.")]
        public float hillFlatMargin = 3f;

        [Header("Mines")]
        [Tooltip("Mine groups along the route of each section (the stretch between two holes or pads). 0 = no mines.")]
        public int mineGroupsMin = 4;
        public int mineGroupsMax = 6;
        [Tooltip("Mines in each group, all told. One goes beside each wall of the group's cell first (so walls can't be " +
                 "hugged past it); the rest are laid out evenly in the middle (2 = a pair, 3 = a triangle, ...). " +
                 "Mines that don't fit their cell are left out.")]
        public int mineGroupSizeMin = 5;
        public int mineGroupSizeMax = 6;
        [Tooltip("Distance between neighboring mines in a group (centers). The ball is 1 unit across.")]
        public float mineSpacing = 5f;
        [Tooltip("Mine radius. The ball's radius is 0.5.")]
        public float mineRadius = 0.8f;
        [Tooltip("How far a mine sticks up out of the ground.")]
        public float mineHeight = 0.2f;
        [Tooltip("How far the mines beside walls sit from the wall. Keep it under the ball's width (1) so the wall " +
                 "can't be hugged past the group.")]
        public float mineWallGap = 0.35f;
        [Tooltip("How many of the innermost (smallest) shells get one mine group fewer per section, and one mine " +
                 "fewer in each group (never fewer than one).")]
        public int fewerMinesInnermost = 2;

        [Header("Laser gates")]
        [Tooltip("A laser gate on every Nth section, starting with the first (2 = sections 1, 3, 5, ...). The pad that " +
                 "turns it off is on the near side. 0 = none.")]
        public int laserGateEvery = 2;
        [Tooltip("Lasers in a gate, spread from near the ground to just under the wall tops. Keep the gaps between " +
                 "them under the ball's width (1): with 4-unit walls that's at least 5.")]
        public int laserRows = 7;

        [Header("Security cameras")]
        [Tooltip("A security camera on every Nth section, starting with the second (2 = sections 2, 4, 6, ...). 0 = none.")]
        public int cameraEvery = 2;
        [Tooltip("Cameras in each run, one per doorway along the route, alternating sides of the passage.")]
        public int camerasPerGroup = 3;
        [Tooltip("Radius of the camera's spotlight on the ground. The ball is 1 across.")]
        public float spotRadius = 2.2f;
        [Tooltip("Seconds for a spotlight to go once through its search pattern.")]
        public float spotPeriod = 5f;
        [Tooltip("How far the spotlight swings into the cells on either side of the doorway.")]
        public float spotReach = 3f;
        [Tooltip("How high the camera's pole rises above the wall it stands on.")]
        public float cameraPoleHeight = 3f;

        [Header("Orientation")]
        [Tooltip("How high the light beam over the current section's hole or boost pad rises into the sky.")]
        public float beamHeight = 100f;

        [Header("Mesh detail")]
        public int shellMeshSubdivisions = 6;
        public int coreMeshSubdivisions = 5;

        public int ShellCount => shellRadii.Length;

        /// <summary>How much bigger this shell's hills are than the reference shell's (its radius relative to it).</summary>
        public float HillScale(int shell) => shellRadii[shell] / shellRadii[Mathf.Clamp(hillReferenceShell, 0, ShellCount - 1)];

        /// <summary>Typical hill radius on a shell.</summary>
        public float HillSize(int shell) => hillSize * HillScale(shell);

        /// <summary>
        /// Tallest hill on a shell: Hill Height scaled like the shell, capped so a wall on top of a hill stays
        /// clear of the shell above.
        /// </summary>
        public float HillHeight(int shell)
        {
            float h = hillHeight * HillScale(shell);
            if (shell > 0) h = Mathf.Min(h, shellRadii[shell - 1] - shellThickness - shellRadii[shell] - wallHeight - 1f);
            return Mathf.Max(0f, h);
        }

        /// <summary>Boost pad radius for a hole of the given radius.</summary>
        public static float PadRadius(float holeRadius) => Mathf.Max(0.9f, holeRadius * 0.85f);

        public float HoleRadius(SphereGrid grid, int shell) =>
            Mathf.Max(minHoleRadius, holeRadiusFactor * grid.CellSpacing * shellRadii[shell]);

        /// <summary>Radius of the walkable surface for a layer index (shells, then the core).</summary>
        public float SurfaceRadius(int layer) => layer < ShellCount ? shellRadii[layer] : coreRadius;
    }
}
