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
        public int sectionLengthMin = 17;
        public int sectionLengthMax = 29;
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
        public int mineGroupsMin = 3;
        public int mineGroupsMax = 5;
        [Tooltip("Mines in each group, all told. One goes beside each wall of the group's cell first (so walls can't be " +
                 "hugged past it); the rest spread evenly over the cell, each in its most open spot, always leaving " +
                 "room to roll between. More mines = narrower ways through. Mines that don't fit are left out.")]
        public int mineGroupSizeMin = 6;
        public int mineGroupSizeMax = 7;
        [Tooltip("Mine radius. The ball's radius is 0.5.")]
        public float mineRadius = 0.8f;
        [Tooltip("How far a mine sticks up out of the ground.")]
        public float mineHeight = 0.2f;
        [Tooltip("How far the mines beside walls sit from the wall. Keep it under the ball's width (1) so the wall " +
                 "can't be hugged past the group.")]
        public float mineWallGap = 0.35f;
        [Tooltip("Also scatter lone mines over each half of a section (before and after its checkpoint), as many as " +
                 "a group would have, on top of the groups.")]
        public bool singleMines = true;
        [Tooltip("How many of the innermost (smallest) shells get sparser mines: one group fewer per section, smaller " +
                 "groups, and more room between mines.")]
        public int fewerMinesInnermost = 2;
        [Tooltip("On those shells, how many fewer mines each group gets (never fewer than one).")]
        public int innermostGroupSizeCut = 1;
        [Tooltip("On those shells, the clear space left between mines (elsewhere it's 1.6; the ball is 1 across).")]
        public float innermostMineGap = 2.4f;

        [Header("Major hazards")]
        [Tooltip("Major hazards (laser gates, camera runs, acid pools, mortars) in each half of a section, before and " +
                 "after its checkpoint. Never the same kind twice in a half, and each kind is used about equally often.")]
        public int majorHazardsPerHalf = 2;
        [Tooltip("How many of the innermost (smallest) spheres get fewer, since their sections are shorter.")]
        public int fewerHazardsInnermost = 2;
        [Tooltip("Major hazards in each half section on those innermost spheres.")]
        public int majorHazardsPerHalfInnermost = 1;
        public bool laserGates = true;
        public bool securityCameras = true;
        public bool acidPools = true;
        public bool mortars = true;

        [Header("Laser gates")]
        [Tooltip("Lasers in a gate, spread from near the ground to just under the wall tops. Keep the gaps between " +
                 "them under the ball's width (1): with 4-unit walls that's at least 5.")]
        public int laserRows = 7;

        [Header("Security cameras")]
        [Tooltip("Cameras in each run, one per doorway along the route, alternating sides of the passage.")]
        public int camerasPerGroup = 3;
        [Tooltip("Radius of the camera's spotlight on the ground. The ball is 1 across.")]
        public float spotRadius = 2.2f;
        [Tooltip("Seconds for a spotlight to go once through its search pattern.")]
        public float spotPeriod = 3.75f;
        [Tooltip("How far the spotlight swings into the cells on either side of the doorway.")]
        public float spotReach = 3f;
        [Tooltip("How high the camera's pole rises above the wall it stands on.")]
        public float cameraPoleHeight = 3f;

        [Header("Energy cubes")]
        [Tooltip("Cubes to collect on each half of a section (before and after its checkpoint), counting the half's " +
                 "one super cube. 0 = none.")]
        [UnityEngine.Serialization.FormerlySerializedAs("cubesPerHalfLeg")]
        public int cubesPerHalfSection = 7;
        [Tooltip("Share of the ordinary cubes tucked away off the main route, down side lanes and around corners " +
                 "(the super cube is always hidden).")]
        [Range(0f, 1f)] public float hiddenCubeShare = 0.5f;
        [Tooltip("Points for a cube, and for each half's super cube.")]
        public int cubePoints = 10;
        public int superCubePoints = 50;
        [Tooltip("Edge length of a cube's glowing outline (the ball is 1 across). The super cube is larger.")]
        public float cubeSize = 0.5f;
        [Tooltip("Size of the bright point glowing at the heart of each cube.")]
        public float cubeCoreSize = 0.08f;

        [Header("Acid pools")]
        [Tooltip("Width of the bridge over the acid (the ball is 1 across).")]
        public float acidBridgeWidth = 2.275f;
        [Tooltip("The sharpest the bridge may turn in the middle of the pool, in degrees (0 = only dead straight).")]
        public float acidMaxBend = 45f;

        [Header("Mortars")]
        [Tooltip("How near (in units, from the mortar) the ball has to be for it to fire.")]
        public float mortarRange = 18f;
        [Tooltip("Seconds between shots while the ball is in range.")]
        public float mortarInterval = 1.5f;
        [Tooltip("Seconds a bomb is in the air. The mortar aims where the ball will be by then if it keeps rolling the same way.")]
        public float mortarFlightTime = 1f;
        [Tooltip("How near the bomb has to land to the ball to blow it up (from the ball's center). The ball is 1 across.")]
        public float mortarBlastRadius = 2.4f;

        [Header("Checkpoints")]
        [Tooltip("A checkpoint pad halfway along each section: once you've rolled over it, dying sends you back there.")]
        public bool checkpoints = true;
        [Tooltip("On this many of the outer spheres, sections also get checkpoints a quarter and three quarters of the " +
                 "way along. The halfway one still splits the section into halves.")]
        public int extraCheckpointSpheres = 2;
        public float checkpointRadius = 2.8f;
        [Tooltip("Points for reaching a checkpoint.")]
        public int checkpointPoints = 5;

        [Header("The core")]
        [Tooltip("Width of the paths over the lava. A corridor on the first sphere is about Cell Spacing wide.")]
        public float corePathWidth = 8f;
        [Tooltip("How high the paths and plazas stand above the lava.")]
        public float corePathHeight = 1.6f;
        [Tooltip("How far the hills along the paths rise above (and dip below) that.")]
        public float coreHillHeight = 1.1f;
        [Tooltip("Key pads on the core, each breaking one of the walls around the trapped energy ball.")]
        public int coreKeyPads = 6;
        [Tooltip("Lone mines on each of the core's paths (the one from the landing and each key pad's).")]
        public int coreMinesMin = 3;
        public int coreMinesMax = 4;
        [Tooltip("Seconds between blobs of lava raining down on the ball on the core (a random time in this range).")]
        public float lavaRainIntervalMin = 3f;
        public float lavaRainIntervalMax = 4f;
        [Tooltip("Seconds a lava blob takes to fall, aimed where the ball will be by then: how long its shadow shows.")]
        public float lavaBlobFallTime = 1.4f;
        [Tooltip("How near a lava blob has to land to the ball to burn it up (from the ball's edge). The ball is 1 across.")]
        public float lavaBlobSplashRadius = 1.3f;
        [Tooltip("Energy cubes scattered along the core's paths.")]
        public int coreCubes = 10;
        [Tooltip("Points for each key pad, and for freeing the energy ball.")]
        public int keyPadPoints = 10;
        public int rescuePoints = 100;

        [Header("Arrows")]
        [Tooltip("An arrow on the floor on about every Nth cell along each section's route, pointing the way (plus " +
                 "one at each checkpoint). 0 = none.")]
        public int arrowEvery = 4;

        [Header("Orientation")]
        [Tooltip("How high the light beam over the current section's hole or boost pad rises into the sky.")]
        public float beamHeight = 100f;

        [Header("Performance")]
        [Tooltip("Each sphere is drawn in chunks about this many units across; only chunks near the camera are " +
                 "switched on (along with the hazards and pickups in them).")]
        public float chunkSize = 30f;
        [Tooltip("How far from the camera a chunk's middle can be and still be switched on (plus half a chunk). " +
                 "Too small and distant maze pops in.")]
        public float chunkViewDistance = 75f;

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
