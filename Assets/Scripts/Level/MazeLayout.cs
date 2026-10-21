using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace SphereDescent
{
    /// <summary>
    /// Pure-data description of a world: a linear chain of maze sections leading from the surface to the core.
    ///
    /// Each section is a winding band of cells on one shell: a main path plus side lanes that branch off and
    /// rejoin it. There are several routes but no dead ends, and every route ends at the same spot: a hole
    /// dropping to the next section on the shell below, or a boost pad launching you up through a hole into
    /// the next section on the shell above. The last section drops into the core, which is the exit.
    /// Cells that belong to no section get a sealed-off filler maze so every shell still looks like a maze.
    ///
    /// Each shell has its own cell grid sized so corridors are equally wide on every layer. Since the grids
    /// don't line up, each hole is placed at a point that sits safely inside a cell both on its own shell
    /// (room for the hole) and on the layer below (room for the landing spot / boost pad).
    /// </summary>
    public sealed class MazeLayout
    {
        public struct Hole
        {
            public int Shell;          // which shell the hole is cut into
            public int Cell;           // cell on that shell's grid containing the hole
            public int LandingCell;    // cell on the next shell's grid where you land (-1 when dropping into the core)
            public Vector3 Direction;  // unit direction of the hole center
            public bool TwoWay;        // a boost pad sits beneath it on the next layer down
        }

        public struct Mine
        {
            public int Shell;
            public int Section;        // the section whose route it sits on
            public Vector3 Direction;  // unit direction of its center
            public bool NearWall;      // set just off a wall, so the wall can't be hugged past the group
            public bool Single;        // a lone mine scattered along the section, not part of a group
        }

        public struct LaserGate
        {
            public int Shell;
            public int Section;
            public int Edge;           // the doorway the lasers fill (an open edge every route must cross)
            public int EntrySideCell;  // the cell on the side you arrive from
            public int SwitchCell;     // where the pad that turns the lasers off sits (on the entry side)
            public bool SwitchInSpur;  // the pad has its own dead-end side path
        }

        public struct SecurityCamera
        {
            public int Shell;
            public int Section;
            public int Edge;           // the doorway its spotlight sweeps (an open edge every route must cross)
            public int NearCell;       // the cell on the side you arrive from
            public Vector3 Mount;      // unit direction of the pole's foot, on top of a wall beside the doorway
            public float Phase;        // where in its sweep it starts (0-1)
            public SweepPattern Pattern;
        }

        // Clear space kept between any two mines, so the ball (1 unit across) can always roll between them.
        // The innermost shells use their own, roomier gap (Innermost Mine Gap).
        public const float MinePassage = 1.6f;

        /// <summary>Whether a shell is one of the innermost few, which get sparser mines.</summary>
        public bool InnermostForMines(LevelSettings s, int shell) => shell >= ShellCount - s.fewerMinesInnermost;

        /// <summary>Clear space kept between mines on a shell.</summary>
        public float MineGap(LevelSettings s, int shell) => InnermostForMines(s, shell) ? Mathf.Max(MinePassage, s.innermostMineGap) : MinePassage;

        public struct Section
        {
            public int Shell;
            public int EntryCell;      // where you arrive (landing spot, or beside the hole you launched through)
            public int ExitCell;       // the hole (going down) or boost pad (going up) that ends the section
            public bool ExitsUp;
            public int RouteCells;     // cells on the shortest route from entry to exit, both included
            public int CellCount;
        }

        public SphereGrid[] Grids; // [shell]
        public int ShellCount;
        public int[][] Region;     // [shell][cell] -> section index, or -1 for sealed-off filler
        public bool[][] Open;      // [shell][edge] -> true when there is no wall
        public List<Hole> Holes = new List<Hole>();
        public List<Section> Sections = new List<Section>();
        public List<Mine> Mines = new List<Mine>();
        public List<(int section, int cell)> MineGroups = new List<(int, int)>();
        public List<LaserGate> LaserGates = new List<LaserGate>();
        public List<SecurityCamera> Cameras = new List<SecurityCamera>();
        public List<Cube> Cubes = new List<Cube>();
        public List<Checkpoint> Checkpoints = new List<Checkpoint>();
        public List<AcidPool> AcidPools = new List<AcidPool>();

        public struct AcidPool
        {
            public int Shell;
            public int Section;
            public int Cell;           // the cell filled with acid
            public int From, To;       // the route cells before and after it: the bridge runs between their doorways
            public int RouteIndex;     // the pool cell's position along the section's route
        }
        public List<Arrow> Arrows = new List<Arrow>();

        public struct Arrow
        {
            public int Shell;
            public Vector3 Direction;  // unit direction of where it lies
            public Vector3 Pointing;   // unit tangent it points along
        }

        public struct Checkpoint
        {
            public int Shell;
            public int Section;
            public int Cell;
            public int RouteIndex;     // position along the section's route
            public Vector3 Direction;  // unit direction of the pad's center
        }

        public struct Cube
        {
            public int Shell;
            public int Section;
            public int Cell;
            public Vector3 Direction;  // unit direction of where it floats
            public bool Hidden;        // off the main route
            public bool Super;
        }
        public int StartCell;
        public int Seed;
        public int AttemptsUsed;

        // Side lanes may only touch cells within this many main-path steps of where they branch off,
        // so no lane can be a shortcut past most of the section.
        const int LaneWindow = 3;
        const int MaxAttempts = 300;

        public int Climbs
        {
            get
            {
                int n = 0;
                foreach (var sec in Sections) if (sec.ExitsUp) n++;
                return n;
            }
        }

        public static MazeLayout Generate(LevelSettings settings, int seed)
        {
            var rng = new Random(seed);
            int attempts = 0;
            // If the shells are too crowded for the requested climbs, settle for fewer.
            for (int climbs = Mathf.Max(0, settings.climbsPerWorld); climbs >= 0; climbs--)
            {
                for (int i = 0; i < MaxAttempts; i++)
                {
                    attempts++;
                    var layout = TryGenerate(settings, climbs, seed, rng);
                    if (layout == null) continue;
                    layout.AttemptsUsed = attempts;
                    if (climbs < settings.climbsPerWorld)
                        Debug.LogWarning($"MazeLayout: only fit {climbs} of {settings.climbsPerWorld} climbs (seed {seed}).");
                    return layout;
                }
            }
            Debug.LogError($"MazeLayout: could not generate a layout for seed {seed}.");
            return null;
        }

        static MazeLayout TryGenerate(LevelSettings s, int climbs, int seed, Random rng)
        {
            int L = s.ShellCount;
            var layout = new MazeLayout
            {
                Grids = new SphereGrid[L],
                ShellCount = L,
                Region = new int[L][],
                Open = new bool[L][],
                Seed = seed,
            };
            for (int shell = 0; shell < L; shell++)
            {
                var grid = layout.Grids[shell] = SphereGrid.ForRadius(s.shellRadii[shell], s.cellSpacing);
                layout.Region[shell] = new int[grid.CellCount];
                for (int c = 0; c < grid.CellCount; c++) layout.Region[shell][c] = -1;
                layout.Open[shell] = new bool[grid.Edges.Length];
            }

            var plan = PlanLayers(s, layout.Grids, climbs, rng);
            if (plan == null) return null;

            layout.StartCell = rng.Next(layout.Grids[0].CellCount);
            int entry = layout.StartCell;
            for (int i = 0; i < plan.Length; i++)
            {
                int nextLayer = i + 1 < plan.Length ? plan[i + 1] : L; // L = the core
                if (!BuildSection(layout, s, i, plan[i], entry, nextLayer, rng, out entry)) return null;
            }

            for (int shell = 0; shell < L; shell++)
                CarveFiller(layout.Grids[shell], layout.Region[shell], layout.Open[shell], rng);
            // Own random streams, so hazard settings never change the maze itself.
            PlaceLaserGates(layout, s, new Random(seed * 17 + 3));
            PlaceCameras(layout, s, new Random(seed * 13 + 5));
            PlaceAcidPools(layout, s);
            PlaceCheckpoints(layout, s);
            PlaceMines(layout, s, new Random(seed * 31 + 7));
            PlaceSingleMines(layout, s, new Random(seed * 19 + 13));
            PlaceCubes(layout, s, new Random(seed * 7 + 11));
            PlaceArrows(layout, s);
            return layout;
        }

        /// <summary>
        /// A laser gate on the odd-numbered sections (1st, 3rd, ...; with Laser Gate Every = 2): lasers fill a doorway that every route through the section
        /// has to cross, and a pad on the near side turns them off. The pad goes down its own short dead-end
        /// side path when there's a sealed-off cell beside the section to carve one into; otherwise in a side
        /// lane, or failing that on the route. A section that has no such doorway passes its turn to the next.
        /// </summary>
        static void PlaceLaserGates(MazeLayout layout, LevelSettings s, Random rng)
        {
            if (s.laserGateEvery <= 0) return;
            int due = 0;
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                if (i < due) continue;
                if (TryPlaceLaserGate(layout, i, rng)) due = i + s.laserGateEvery;
            }
        }

        static bool TryPlaceLaserGate(MazeLayout layout, int section, Random rng)
        {
            var sec = layout.Sections[section];
            int shell = sec.Shell;
            var grid = layout.Grids[shell];
            var region = layout.Region[shell];
            var open = layout.Open[shell];
            var route = layout.Route(section);
            foreach (var (near, edge, reach) in layout.Chokepoints(section))
            {
                var onRoute = new HashSet<int>(route);
                int gateDist = reach[near];
                // 1) A sealed-off filler cell beside the near side, close to the gate: carve it into a spur.
                int best = -1, bestScore = int.MaxValue, bestFrom = -1;
                foreach (var kv in reach)
                {
                    int c = kv.Key;
                    if (c == sec.EntryCell || Mathf.Abs(kv.Value - gateDist) > 3) continue;
                    foreach (int nb in grid.Neighbors[c])
                    {
                        if (region[nb] != -1) continue;
                        int score = Mathf.Abs(kv.Value - gateDist) * 10 + rng.Next(5);
                        if (score < bestScore) { bestScore = score; best = nb; bestFrom = c; }
                    }
                }
                bool spur = best >= 0;
                if (spur)
                {
                    // Seal it off from the filler maze and open it onto the section.
                    foreach (int nb in grid.Neighbors[best]) open[grid.EdgeBetween(best, nb)] = false;
                    open[grid.EdgeBetween(best, bestFrom)] = true;
                    region[best] = section;
                }
                else
                {
                    // 2) A side-lane cell on the near side, nearest the gate; 3) any near-side cell but the entry.
                    foreach (var kv in reach)
                    {
                        if (kv.Key == sec.EntryCell || kv.Key == near) continue;
                        int score = Mathf.Abs(kv.Value - gateDist) + (onRoute.Contains(kv.Key) ? 100 : 0);
                        if (score < bestScore) { bestScore = score; best = kv.Key; }
                    }
                    if (best < 0) continue;
                }
                layout.LaserGates.Add(new LaserGate
                {
                    Shell = shell, Section = section, Edge = edge, EntrySideCell = near, SwitchCell = best, SwitchInSpur = spur,
                });
                return true;
            }
            return false;
        }

        /// <summary>
        /// A run of security cameras on the even-numbered sections (2nd, 4th, ...; with Camera Every = 2), one
        /// per doorway over consecutive doorways along the route, each on a pole on top of a wall beside its
        /// doorway, alternating sides. Each spotlight sweeps its own doorway. The run goes where the most of
        /// its doorways are ones every route has to cross; a section with none passes its turn to the next.
        /// </summary>
        static void PlaceCameras(MazeLayout layout, LevelSettings s, Random rng)
        {
            if (s.cameraEvery <= 0) return;
            int due = 1;
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                if (i < due || layout.LaserGates.Exists(g => g.Section == i)) continue;
                if (TryPlaceCamera(layout, s, i, rng)) due = i + s.cameraEvery;
            }
        }

        static bool TryPlaceCamera(MazeLayout layout, LevelSettings s, int section, Random rng)
        {
            var sec = layout.Sections[section];
            int shell = sec.Shell;
            var grid = layout.Grids[shell];
            var route = layout.Route(section);
            int count = Mathf.Max(1, s.camerasPerGroup);

            // Which doorways along the route every route has to cross.
            var choke = new bool[route.Count];
            foreach (var (near, _, _) in layout.Chokepoints(section)) choke[route.IndexOf(near)] = true;

            // A run of consecutive doorways, two cells clear of either end: the most must-cross doorways
            // first, then nearest the middle of the route.
            int bestStart = -1, bestScore = int.MinValue;
            for (int k = 2; k + count + 2 < route.Count; k++)
            {
                int chokes = 0;
                for (int j = 0; j < count; j++) if (choke[k + j]) chokes++;
                if (chokes == 0) continue;
                int score = chokes * 1000 - Mathf.Abs(2 * k + count - route.Count);
                if (score > bestScore) { bestScore = score; bestStart = k; }
            }
            if (bestStart < 0) return false;

            float phase = (float)rng.NextDouble();
            // Each camera in the run searches its own way, in a shuffled order.
            int firstPattern = rng.Next(3);
            for (int j = 0; j < count; j++)
            {
                int near = route[bestStart + j], far = route[bestStart + j + 1];
                // Alternate sides of the passage: left, right, left, ... as you travel through.
                Vector3 travel = grid.CellCenters[far] - grid.CellCenters[near];
                Vector3 mount = CameraMount(layout, s, shell, near, far, travel, leftSide: j % 2 == 0);
                if (mount == Vector3.zero) continue;
                layout.Cameras.Add(new SecurityCamera
                {
                    Shell = shell, Section = section, Edge = grid.EdgeBetween(near, far), NearCell = near,
                    Mount = mount,
                    // Out of step with each other, so the run has a rhythm to read rather than one gap.
                    Phase = Mathf.Repeat(phase + j / (float)count, 1f),
                    Pattern = (SweepPattern)((firstPattern + j) % 3),
                });
            }
            return true;
        }

        /// <summary>
        /// Where a camera pole goes for a doorway: on a wall of either cell that meets the doorway at one of
        /// its ends, a little way along it from that end. Prefers the end on the requested side of the passage
        /// (seen travelling from <paramref name="near"/> to <paramref name="far"/>), else takes the other.
        /// Zero if neither end has a wall to stand on.
        /// </summary>
        static Vector3 CameraMount(MazeLayout layout, LevelSettings s, int shell, int near, int far, Vector3 travel, bool leftSide)
        {
            var grid = layout.Grids[shell];
            float radius = s.shellRadii[shell];
            var door = grid.Edges[grid.EdgeBetween(near, far)];
            Vector3 up = (door.P0 + door.P1).normalized;
            Vector3 left = Vector3.Cross(travel, up);
            Vector3 preferred = Vector3.zero, fallback = Vector3.zero;
            foreach (int cell in new[] { near, far })
                foreach (int nb in grid.Neighbors[cell])
                {
                    int e = grid.EdgeBetween(cell, nb);
                    if (layout.Open[shell][e]) continue;
                    var wall = grid.Edges[e];
                    Vector3 corner, other;
                    if (wall.P0 == door.P0 || wall.P0 == door.P1) { corner = wall.P0; other = wall.P1; }
                    else if (wall.P1 == door.P0 || wall.P1 == door.P1) { corner = wall.P1; other = wall.P0; }
                    else continue;
                    float length = Vector3.Angle(corner, other) * Mathf.Deg2Rad * radius;
                    Vector3 mount = Vector3.Slerp(corner, other, Mathf.Min(0.4f, 1.5f / length)).normalized;
                    bool onLeft = Vector3.Dot(corner - up, left) > 0f;
                    if (onLeft == leftSide) { if (preferred == Vector3.zero) preferred = mount; }
                    else if (fallback == Vector3.zero) fallback = mount;
                }
            return preferred != Vector3.zero ? preferred : fallback;
        }

        /// <summary>
        /// An acid pool on every Nth section (starting with Acid Pool First Section): a route cell every way
        /// through has to enter, with just two openings (in and out), filled wall to wall with acid and crossed
        /// by a thin bridge. Of the cells that qualify, it takes the one farthest along the route from the
        /// section's laser gate or camera run (or, with neither, farthest from the middle, where the checkpoint
        /// goes), so the big hazards are spread out. A section with no such cell passes its turn to the next.
        /// </summary>
        static void PlaceAcidPools(MazeLayout layout, LevelSettings s)
        {
            if (s.acidPoolEvery <= 0) return;
            int due = Mathf.Max(0, s.acidPoolFirstSection - 1);
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                if (i < due) continue;
                if (TryPlaceAcidPool(layout, i)) due = i + s.acidPoolEvery;
            }
        }

        static bool TryPlaceAcidPool(MazeLayout layout, int section)
        {
            int shell = layout.Sections[section].Shell;
            var grid = layout.Grids[shell];
            var route = layout.Route(section);
            // Where along the route the section's other big hazards are.
            var others = new List<int>();
            foreach (var g in layout.LaserGates) if (g.Section == section) others.Add(route.IndexOf(g.EntrySideCell));
            foreach (var c in layout.Cameras) if (c.Section == section) others.Add(route.IndexOf(c.NearCell));

            int best = -1;
            float bestScore = float.MinValue;
            foreach (var (near, _, _) in layout.Chokepoints(section))
            {
                int k = route.IndexOf(near) + 1; // every way through enters this cell
                if (k < 2 || k > route.Count - 3) continue;
                int cell = route[k];
                if (layout.Openings(shell, cell) != 2) continue;
                if (layout.ReservedForHazard(shell, route[k - 1]) || layout.ReservedForHazard(shell, cell) ||
                    layout.ReservedForHazard(shell, route[k + 1])) continue;
                float score;
                if (others.Count > 0)
                {
                    score = int.MaxValue;
                    foreach (int o in others) score = Mathf.Min(score, Mathf.Abs(k - o));
                    if (score < 3) continue;
                }
                else score = Mathf.Abs(2 * k - route.Count) * 0.5f;
                // Prefer doorways well apart, so the bridge doesn't fold back on itself.
                Vector3 c0 = grid.CellCenters[cell];
                float spread = Vector3.Angle(Vector3.ProjectOnPlane(grid.CellCenters[route[k - 1]] - c0, c0),
                    Vector3.ProjectOnPlane(grid.CellCenters[route[k + 1]] - c0, c0));
                score += spread / 360f;
                if (score > bestScore) { bestScore = score; best = k; }
            }
            if (best < 0) return false;
            layout.AcidPools.Add(new AcidPool
            {
                Shell = shell, Section = section, Cell = route[best], From = route[best - 1], To = route[best + 1], RouteIndex = best,
            });
            return true;
        }

        /// <summary>
        /// A checkpoint on each section: on the route cell nearest its middle that's clear of laser gates and
        /// camera doorways (mines are placed later and keep clear of it). Sections too short to have a middle
        /// apart from their ends get none.
        /// </summary>
        static void PlaceCheckpoints(MazeLayout layout, LevelSettings s)
        {
            if (!s.checkpoints) return;
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                int shell = layout.Sections[i].Shell;
                var route = layout.Route(i);
                int best = -1;
                for (int k = 2; k < route.Count - 2; k++)
                {
                    if (layout.ReservedForHazard(shell, route[k])) continue;
                    if (best < 0 || Mathf.Abs(2 * k - route.Count) < Mathf.Abs(2 * best - route.Count)) best = k;
                }
                if (best < 0) continue;
                layout.Checkpoints.Add(new Checkpoint
                {
                    Shell = shell, Section = i, Cell = route[best], RouteIndex = best,
                    Direction = layout.Grids[shell].CellCenters[route[best]],
                });
            }
        }

        /// <summary>
        /// Energy cubes on each section, placed separately in the halves before and after its checkpoint (the
        /// whole section is one half if it has no checkpoint). In each half some are spread evenly along the
        /// main route (skipping where you arrive and leave, and the checkpoint); the rest are hidden in side-lane
        /// cells branching off that half, the ones farthest off the route first, tucked against the wall that
        /// faces away from the route so they only show once you look around the corner. The most hidden one is
        /// the half's super cube. Side lanes are part of the section, so every cube can be reached; cubes keep
        /// clear of mines, walls, holes and the checkpoint.
        /// </summary>
        static void PlaceCubes(MazeLayout layout, LevelSettings s, Random rng)
        {
            if (s.cubesPerHalfSection <= 0) return;
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                var sec = layout.Sections[i];
                int shell = sec.Shell;
                var grid = layout.Grids[shell];
                float radius = s.shellRadii[shell];
                var route = layout.Route(i);
                int checkpoint = layout.Checkpoints.FindIndex(c => c.Section == i);
                int split = checkpoint >= 0 ? layout.Checkpoints[checkpoint].RouteIndex : -1;

                // Every cell of the section: how many steps off the route, and which route cell it branches from.
                var offRoute = new Dictionary<int, int>();
                var branch = new Dictionary<int, int>();
                var queue = new Queue<int>();
                for (int k = 0; k < route.Count; k++) { offRoute[route[k]] = 0; branch[route[k]] = k; queue.Enqueue(route[k]); }
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    foreach (int nb in grid.Neighbors[cur])
                    {
                        if (offRoute.ContainsKey(nb) || layout.Region[shell][nb] != i || !layout.Open[shell][grid.EdgeBetween(cur, nb)]) continue;
                        offRoute[nb] = offRoute[cur] + 1;
                        branch[nb] = branch[cur];
                        queue.Enqueue(nb);
                    }
                }

                // Cells holding a mine group: no cubes in among the mines.
                var mined = new HashSet<int>();
                foreach (var (mineSection, mineCell) in layout.MineGroups) if (mineSection == i) mined.Add(mineCell);

                var halves = split > 0 ? new[] { (1, split - 1), (split + 1, route.Count - 2) } : new[] { (1, route.Count - 2) };
                foreach (var (from, to) in halves)
                {
                    bool InHalf(int routeIndex) => routeIndex >= from - 1 && routeIndex <= to + 1;
                    int hiddenWanted = Mathf.RoundToInt((s.cubesPerHalfSection - 1) * s.hiddenCubeShare) + 1; // + the super cube
                    var side = new List<int>();
                    foreach (var kv in offRoute)
                        if (kv.Value > 0 && InHalf(branch[kv.Key]) && !layout.ReservedForHazard(shell, kv.Key) && !mined.Contains(kv.Key))
                            side.Add(kv.Key);
                    side.Sort((a, b) => offRoute[b] != offRoute[a] ? offRoute[b].CompareTo(offRoute[a]) : rng.Next(3) - 1);

                    int placedHidden = 0, firstOfHalf = layout.Cubes.Count;
                    foreach (int cell in side)
                    {
                        if (placedHidden >= hiddenWanted) break;
                        // The cell this side cell is entered from (one step closer to the route).
                        int entry = -1;
                        foreach (int nb in grid.Neighbors[cell])
                            if (offRoute.TryGetValue(nb, out int d) && d == offRoute[cell] - 1 && layout.Open[shell][grid.EdgeBetween(cell, nb)]) entry = nb;
                        if (entry < 0) continue;
                        // On the far side of the cell from where you come in, toward its farthest wall, but well off it.
                        Vector3 best = Vector3.zero;
                        float bestFar = -1f;
                        foreach (int nb in grid.Neighbors[cell])
                        {
                            int e = grid.EdgeBetween(cell, nb);
                            if (layout.Open[shell][e]) continue;
                            Vector3 mid = (grid.Edges[e].P0 + grid.Edges[e].P1).normalized;
                            float far = Vector3.Angle(mid, grid.CellCenters[entry]);
                            if (far <= bestFar) continue;
                            Vector3 inward = Vector3.ProjectOnPlane(grid.CellCenters[cell] - mid, mid).normalized;
                            Vector3 spot = (mid * radius + inward * (s.wallThickness * 0.5f + s.cellSpacing * 0.28f)).normalized;
                            if (!CubeFits(layout, s, shell, spot, radius)) continue;
                            bestFar = far; best = spot;
                        }
                        if (best == Vector3.zero)
                        {
                            Vector3 center = grid.CellCenters[cell];
                            if (!CubeFits(layout, s, shell, center, radius)) continue;
                            best = center;
                        }
                        layout.Cubes.Add(new Cube { Shell = shell, Section = i, Cell = cell, Direction = best, Hidden = true, Super = placedHidden == 0 });
                        placedHidden++;
                    }

                    // The rest along this half of the route, evenly spaced, in the middle of the passage. A spot
                    // that's mined or otherwise taken moves to the nearest free route cell in the half.
                    int routeWanted = s.cubesPerHalfSection - placedHidden;
                    for (int n = 0; n < routeWanted && to >= from; n++)
                    {
                        int at = Mathf.Clamp(from + (int)((to - from + 1) * (n + 0.5f) / routeWanted), from, to);
                        for (int step = 0; step <= to - from; step++)
                        {
                            int k = at + ((step & 1) == 0 ? step / 2 : -(step + 1) / 2);
                            if (k < from || k > to || mined.Contains(route[k])) continue;
                            if (TryPlaceInMiddle(layout, s, rng, shell, i, route[k], radius)) break;
                        }
                    }
                    // No side lane to hide one in: the last cube on this half's route becomes its super cube instead.
                    if (placedHidden == 0 && layout.Cubes.Count > firstOfHalf)
                    {
                        var c = layout.Cubes[layout.Cubes.Count - 1];
                        c.Super = true;
                        layout.Cubes[layout.Cubes.Count - 1] = c;
                    }
                }
            }
        }

        /// <summary>
        /// Arrows on the floor hinting which way to go: on about every Nth cell of each section's route (not
        /// the first, skipping cells with mines or hazards), pointing to the next cell along it. Each
        /// checkpoint gets one just past its pad, pointing on.
        /// </summary>
        static void PlaceArrows(MazeLayout layout, LevelSettings s)
        {
            if (s.arrowEvery <= 0) return;
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                int shell = layout.Sections[i].Shell;
                var grid = layout.Grids[shell];
                float radius = s.shellRadii[shell];
                var route = layout.Route(i);
                var mined = new HashSet<int>();
                foreach (var (mineSection, mineCell) in layout.MineGroups) if (mineSection == i) mined.Add(mineCell);
                int checkpoint = layout.Checkpoints.FindIndex(c => c.Section == i);
                int checkpointAt = checkpoint >= 0 ? layout.Checkpoints[checkpoint].RouteIndex : -1;

                bool MineNear(Vector3 p, float range)
                {
                    foreach (var m in layout.Mines)
                        if (m.Shell == shell && Vector3.Distance(m.Direction * radius, p) < range + s.mineRadius) return true;
                    return false;
                }

                void Add(Vector3 at, int toward)
                {
                    at = at.normalized;
                    Vector3 pointing = Vector3.ProjectOnPlane(grid.CellCenters[toward] - at, at).normalized;
                    layout.Arrows.Add(new Arrow { Shell = shell, Direction = at, Pointing = pointing });
                }

                int next = 1;
                for (int k = 1; k < route.Count - 1; k++)
                {
                    if (k == checkpointAt)
                    {
                        // Just past the pad's edge, toward the next cell.
                        Vector3 c = grid.CellCenters[route[k]];
                        Vector3 on = Vector3.ProjectOnPlane(grid.CellCenters[route[k + 1]] - c, c).normalized;
                        Add(c * radius + on * (s.checkpointRadius + 1.6f), route[k + 1]);
                        next = k + s.arrowEvery;
                        continue;
                    }
                    if (k < next || mined.Contains(route[k]) || layout.ReservedForHazard(shell, route[k]) ||
                        Mathf.Abs(k - checkpointAt) < 2 || MineNear(grid.CellCenters[route[k]] * radius, 2.5f)) continue;
                    Add(grid.CellCenters[route[k]], route[k + 1]);
                    next = k + s.arrowEvery;
                }
            }
        }

        /// <summary>
        /// Which way the route goes on from a position: toward the next cell along its section's route after
        /// the route cell nearest to it. Zero if the position isn't in a section.
        /// </summary>
        public Vector3 RouteDirectionAt(int shell, Vector3 position)
        {
            int section = SectionAt(shell, position);
            if (section < 0) return Vector3.zero;
            var grid = Grids[shell];
            var route = Route(section);
            Vector3 up = position.normalized;
            int nearest = 0;
            float best = float.MaxValue;
            for (int k = 0; k < route.Count; k++)
            {
                float a = Vector3.Angle(grid.CellCenters[route[k]], up);
                if (a < best) { best = a; nearest = k; }
            }
            // Toward the cell after the nearest one, or along the route's last step at its end.
            Vector3 target = nearest + 1 < route.Count ? grid.CellCenters[route[nearest + 1]]
                : Holes[section].Direction;
            Vector3 dir = Vector3.ProjectOnPlane(target - up, up);
            return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero;
        }

        /// <summary>A cube near the middle of a cell (its center, or close by if something's in the way).</summary>
        static bool TryPlaceInMiddle(MazeLayout layout, LevelSettings s, Random rng, int shell, int section, int cell, float radius)
        {
            if (layout.AcidPools.Exists(p => p.Shell == shell && p.Cell == cell)) return false; // nothing over the acid
            var grid = layout.Grids[shell];
            Vector3 center = grid.CellCenters[cell];
            Vector3 x = Vector3.Cross(center, Mathf.Abs(center.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 y = Vector3.Cross(center, x);
            for (int tries = 0; tries < 16; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = tries == 0 ? 0f : (float)rng.NextDouble() * 1.5f;
                Vector3 spot = (center * radius + (x * Mathf.Cos(a) + y * Mathf.Sin(a)) * d).normalized;
                if (!CubeFits(layout, s, shell, spot, radius)) continue;
                layout.Cubes.Add(new Cube { Shell = shell, Section = section, Cell = cell, Direction = spot });
                return true;
            }
            return false;
        }

        /// <summary>A cube here can be collected without touching a mine or squeezing against a wall, and isn't on another cube.</summary>
        static bool CubeFits(MazeLayout layout, LevelSettings s, int shell, Vector3 dir, float radius)
        {
            Vector3 p = dir * radius;
            foreach (var m in layout.Mines)
                if (m.Shell == shell && Vector3.Distance(m.Direction * radius, p) < s.mineRadius + 3f) return false;
            foreach (var c in layout.Cubes)
                if (c.Shell == shell && Vector3.Distance(c.Direction * radius, p) < 3f) return false;
            var grid = layout.Grids[shell];
            int cell = grid.NearestCell(dir);
            foreach (int a in new List<int>(grid.Neighbors[cell]) { cell })
                foreach (int nb in grid.Neighbors[a])
                {
                    int e = grid.EdgeBetween(a, nb);
                    if (layout.Open[shell][e]) continue;
                    Vector3 a0 = grid.Edges[e].P0 * radius, a1 = grid.Edges[e].P1 * radius;
                    float t = Mathf.Clamp01(Vector3.Dot(p - a0, a1 - a0) / (a1 - a0).sqrMagnitude);
                    if (Vector3.Distance(p, a0 + (a1 - a0) * t) < s.wallThickness * 0.5f + 0.8f) return false;
                }
            // Clear of checkpoint pads, holes and pads too.
            foreach (var c in layout.Checkpoints)
                if (c.Shell == shell && Vector3.Angle(c.Direction, dir) * Mathf.Deg2Rad * radius < s.checkpointRadius + 1.5f) return false;
            foreach (var h in layout.Holes)
                if ((h.Shell == shell || h.Shell + 1 == shell) && Vector3.Angle(h.Direction, dir) * Mathf.Deg2Rad * radius < s.HoleRadius(layout.Grids[h.Shell], h.Shell) + 1.5f)
                    return false;
            return true;
        }

        /// <summary>
        /// Doorways along a section's route that every route through it has to cross (side lanes don't go
        /// around them), middle of the route first, at least two cells from either end. Each comes with the
        /// cell on the near side and the steps to every cell reachable from the entry without crossing it.
        /// </summary>
        public List<(int near, int edge, Dictionary<int, int> reach)> Chokepoints(int section)
        {
            var sec = Sections[section];
            var grid = Grids[sec.Shell];
            var route = Route(section);
            var order = new List<int>();
            for (int k = 2; k + 3 < route.Count; k++) order.Add(k);
            order.Sort((a, b) => Mathf.Abs(2 * a + 1 - route.Count).CompareTo(Mathf.Abs(2 * b + 1 - route.Count)));
            var result = new List<(int, int, Dictionary<int, int>)>();
            foreach (int k in order)
            {
                int edge = grid.EdgeBetween(route[k], route[k + 1]);
                var reach = Reachable(section, sec.EntryCell, edge);
                if (!reach.ContainsKey(sec.ExitCell)) result.Add((route[k], edge, reach));
            }
            return result;
        }

        /// <summary>Steps from <paramref name="from"/> to every cell of a section reachable without crossing <paramref name="blockedEdge"/>.</summary>
        /// <summary>
        /// The shortest way through a section's cells from one cell to another, both included, never crossing
        /// <paramref name="blockedEdge"/>. Empty if there's none.
        /// </summary>
        public List<int> PathBetween(int section, int from, int to, int blockedEdge = -1)
        {
            int shell = Sections[section].Shell;
            var grid = Grids[shell];
            var prev = new Dictionary<int, int> { [from] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0 && !prev.ContainsKey(to))
            {
                int cur = queue.Dequeue();
                foreach (int nb in grid.Neighbors[cur])
                {
                    int e = grid.EdgeBetween(cur, nb);
                    if (e == blockedEdge || prev.ContainsKey(nb) || Region[shell][nb] != section || !Open[shell][e]) continue;
                    prev[nb] = cur;
                    queue.Enqueue(nb);
                }
            }
            var path = new List<int>();
            if (!prev.ContainsKey(to)) return path;
            for (int c = to; c != -1; c = prev[c]) path.Insert(0, c);
            return path;
        }

        public Dictionary<int, int> Reachable(int section, int from, int blockedEdge = -1)
        {
            int shell = Sections[section].Shell;
            var grid = Grids[shell];
            var dist = new Dictionary<int, int> { [from] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int nb in grid.Neighbors[cur])
                {
                    int e = grid.EdgeBetween(cur, nb);
                    if (e == blockedEdge || dist.ContainsKey(nb) || Region[shell][nb] != section || !Open[shell][e]) continue;
                    dist[nb] = dist[cur] + 1;
                    queue.Enqueue(nb);
                }
            }
            return dist;
        }

        /// <summary>Cells a mine group shouldn't use: either side of a laser gate or camera doorway, gate switches, and checkpoints.</summary>
        bool ReservedForHazard(int shell, int cell)
        {
            var grid = Grids[shell];
            foreach (var g in LaserGates)
                if (g.Shell == shell && (cell == g.SwitchCell || cell == grid.Edges[g.Edge].A || cell == grid.Edges[g.Edge].B))
                    return true;
            foreach (var c in Cameras)
                if (c.Shell == shell && (cell == grid.Edges[c.Edge].A || cell == grid.Edges[c.Edge].B))
                    return true;
            foreach (var c in Checkpoints)
                if (c.Shell == shell && cell == c.Cell) return true;
            foreach (var p in AcidPools)
                if (p.Shell == shell && (cell == p.Cell || cell == p.From || cell == p.To)) return true;
            return false;
        }

        /// <summary>
        /// Groups of mines along each section's shortest route, spread out between its entry and exit (never
        /// in or next to either, so landing spots, holes and pads stay clear). Mines in a group are far enough
        /// apart to roll between, and the group stays near the middle of its cell, so there's room around it.
        /// </summary>
        static void PlaceMines(MazeLayout layout, LevelSettings s, Random rng)
        {
            if (s.mineGroupsMax <= 0) return;
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                int shell = layout.Sections[i].Shell;
                var grid = layout.Grids[shell];
                float radius = s.shellRadii[shell];
                var route = layout.Route(i);
                // Usable stretch of the route: skip two cells at each end.
                int first = 2, last = route.Count - 3;
                if (last < first) continue;
                // The smallest shells are cramped, so they get one group fewer, each with fewer mines.
                bool inner = layout.InnermostForMines(s, shell);
                int groups = rng.Next(s.mineGroupsMin, s.mineGroupsMax + 1) - (inner ? 1 : 0);
                groups = Mathf.Min(groups, (last - first) / 2 + 1);
                var used = new List<int>();
                // A cell can hold a group if it's clear of laser gates, their switches and camera doorways, and
                // doesn't touch another group's cell (so groups never run together).
                bool Usable(int cell)
                {
                    if (layout.ReservedForHazard(shell, cell)) return false;
                    foreach (int u in used)
                        if (u == cell || System.Array.IndexOf(grid.Neighbors[u], cell) >= 0) return false;
                    return true;
                }
                for (int g = 0; g < groups; g++)
                {
                    // One group per equal slice of the stretch, somewhere in that slice: a random spot, or the
                    // next usable one after it. A slice with no usable cell gets no group.
                    int lo = first + (last - first + 1) * g / groups;
                    int hi = first + (last - first + 1) * (g + 1) / groups - 1;
                    hi = Mathf.Max(lo, hi);
                    int at = rng.Next(lo, hi + 1);
                    for (int tries = 0; tries <= hi - lo && !Usable(route[at]); tries++)
                        at = lo + (at - lo + 1) % (hi - lo + 1);
                    int cell = route[at];
                    if (!Usable(cell)) continue;
                    used.Add(cell);
                    layout.MineGroups.Add((i, cell));
                    // Group Size counts every mine: the ones beside the walls first, the rest in the middle.
                    int sizeMin = Mathf.Max(1, s.mineGroupSizeMin);
                    int size = Mathf.Max(1, rng.Next(sizeMin, Mathf.Max(sizeMin, s.mineGroupSizeMax) + 1) -
                        (inner ? s.innermostGroupSizeCut : 0));
                    int byWalls = PlaceWallMines(layout, s, rng, shell, i, cell, radius, size);
                    PlaceGroup(layout, s, rng, shell, i, cell, radius, size - byWalls);
                }
            }
        }

        /// <summary>
        /// The rest of a mine group, spread over its whole cell: each mine goes where the cell is most open
        /// (the farthest point from every mine and wall, out of a batch of random tries), so the group covers
        /// the cell evenly and leaves no wide, easy lane through it. Every mine still leaves room to roll past.
        /// </summary>
        static void PlaceGroup(MazeLayout layout, LevelSettings s, Random rng, int shell, int section, int cell, float radius,
            int count)
        {
            var grid = layout.Grids[shell];
            Vector3 center = grid.CellCenters[cell];
            Vector3 x = Vector3.Cross(center, Mathf.Abs(center.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 y = Vector3.Cross(center, x);
            float reach = s.cellSpacing * 0.62f; // a little past the cell's corners
            const int tries = 40;
            for (int n = 0; n < count; n++)
            {
                Vector3 best = Vector3.zero;
                float bestRoom = 0f;
                for (int t = 0; t < tries; t++)
                {
                    // A random point in a disc around the cell, kept if it's in this cell.
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rng.NextDouble()) * reach;
                    Vector3 dir = (center * radius + (x * Mathf.Cos(a) + y * Mathf.Sin(a)) * d).normalized;
                    if (grid.NearestCell(dir) != cell || !MineFits(layout, s, shell, cell, dir, radius, 0.5f)) continue;
                    float room = Room(layout, s, shell, cell, dir * radius, radius);
                    if (room > bestRoom) { bestRoom = room; best = dir; }
                }
                if (best == Vector3.zero) return; // the cell is full
                layout.Mines.Add(new Mine { Shell = shell, Section = section, Direction = best });
            }
        }

        /// <summary>Open space around a point: how far it is to the edge of the nearest mine or wall.</summary>
        static float Room(MazeLayout layout, LevelSettings s, int shell, int cell, Vector3 p, float radius)
        {
            float room = float.MaxValue;
            foreach (var m in layout.Mines)
                if (m.Shell == shell) room = Mathf.Min(room, Vector3.Distance(m.Direction * radius, p) - s.mineRadius);
            var grid = layout.Grids[shell];
            void Walls(int c)
            {
                foreach (int nb in grid.Neighbors[c])
                {
                    int e = grid.EdgeBetween(c, nb);
                    if (layout.Open[shell][e]) continue;
                    Vector3 a0 = grid.Edges[e].P0 * radius, a1 = grid.Edges[e].P1 * radius;
                    float t = Mathf.Clamp01(Vector3.Dot(p - a0, a1 - a0) / (a1 - a0).sqrMagnitude);
                    room = Mathf.Min(room, Vector3.Distance(p, a0 + (a1 - a0) * t) - s.wallThickness * 0.5f);
                }
            }
            Walls(cell);
            foreach (int nb in grid.Neighbors[cell]) Walls(nb);
            return room;
        }

        /// <summary>
        /// Whether a mine here leaves room to roll between it and every mine already on the shell, and stays at
        /// least <paramref name="wallClearance"/> from the walls around the cell.
        /// </summary>
        /// <summary>
        /// Lone mines scattered over each half of each section (before and after its checkpoint), as many as a
        /// mine group there would have, on top of the groups: at random spots in route and side-lane cells, but
        /// never in a group's cell, near the start or exit, or on a hole, pad, checkpoint, laser gate or camera
        /// doorway. Like every mine they leave room to roll past.
        /// </summary>
        static void PlaceSingleMines(MazeLayout layout, LevelSettings s, Random rng)
        {
            if (!s.singleMines || s.mineGroupsMax <= 0) return;
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                var sec = layout.Sections[i];
                int shell = sec.Shell;
                var grid = layout.Grids[shell];
                float radius = s.shellRadii[shell];
                var route = layout.Route(i);
                bool inner = layout.InnermostForMines(s, shell);
                var groupCells = new HashSet<int>();
                foreach (var (mineSection, mineCell) in layout.MineGroups) if (mineSection == i) groupCells.Add(mineCell);

                // Every cell of the section, with the route cell it branches from (route cells branch from themselves).
                var branch = new Dictionary<int, int>();
                var queue = new Queue<int>();
                for (int k = 0; k < route.Count; k++) { branch[route[k]] = k; queue.Enqueue(route[k]); }
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    foreach (int nb in grid.Neighbors[cur])
                    {
                        if (branch.ContainsKey(nb) || layout.Region[shell][nb] != i || !layout.Open[shell][grid.EdgeBetween(cur, nb)]) continue;
                        branch[nb] = branch[cur];
                        queue.Enqueue(nb);
                    }
                }

                int checkpoint = layout.Checkpoints.FindIndex(c => c.Section == i);
                int split = checkpoint >= 0 ? layout.Checkpoints[checkpoint].RouteIndex : -1;
                var halves = split > 0 ? new[] { (2, split - 1), (split + 1, route.Count - 3) } : new[] { (2, route.Count - 3) };
                foreach (var (from, to) in halves)
                {
                    var cells = new List<int>();
                    foreach (var kv in branch)
                        if (kv.Value >= from && kv.Value <= to && !groupCells.Contains(kv.Key) && !layout.ReservedForHazard(shell, kv.Key))
                            cells.Add(kv.Key);
                    if (cells.Count == 0) continue;
                    int sizeMin = Mathf.Max(1, s.mineGroupSizeMin);
                    int count = Mathf.Max(1, rng.Next(sizeMin, Mathf.Max(sizeMin, s.mineGroupSizeMax) + 1) -
                        (inner ? s.innermostGroupSizeCut : 0));
                    for (int n = 0, tries = 0; n < count && tries < count * 40; tries++)
                    {
                        int cell = cells[rng.Next(cells.Count)];
                        Vector3 center = grid.CellCenters[cell];
                        Vector3 x = Vector3.Cross(center, Mathf.Abs(center.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                        Vector3 y = Vector3.Cross(center, x);
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rng.NextDouble()) * s.cellSpacing * 0.45f;
                        Vector3 dir = (center * radius + (x * Mathf.Cos(a) + y * Mathf.Sin(a)) * d).normalized;
                        if (grid.NearestCell(dir) != cell || !MineFits(layout, s, shell, cell, dir, radius, 0.5f) ||
                            NearHoleOrCheckpoint(layout, s, shell, dir, radius)) continue;
                        layout.Mines.Add(new Mine { Shell = shell, Section = i, Direction = dir, Single = true });
                        n++;
                    }
                }
            }
        }

        /// <summary>Too close to a hole, a boost pad or a checkpoint pad for a mine.</summary>
        static bool NearHoleOrCheckpoint(MazeLayout layout, LevelSettings s, int shell, Vector3 dir, float radius)
        {
            foreach (var h in layout.Holes)
                if ((h.Shell == shell || h.Shell + 1 == shell) &&
                    Vector3.Angle(h.Direction, dir) * Mathf.Deg2Rad * radius < s.HoleRadius(layout.Grids[h.Shell], h.Shell) + s.mineRadius + 3f)
                    return true;
            foreach (var c in layout.Checkpoints)
                if (c.Shell == shell && Vector3.Angle(c.Direction, dir) * Mathf.Deg2Rad * radius < s.checkpointRadius + s.mineRadius + 1.5f)
                    return true;
            return false;
        }

        static bool MineFits(MazeLayout layout, LevelSettings s, int shell, int cell, Vector3 dir, float radius, float wallClearance)
        {
            Vector3 p = dir * radius;
            float clear = 2f * s.mineRadius + layout.MineGap(s, shell);
            foreach (var other in layout.Mines)
                if (other.Shell == shell && Vector3.Distance(other.Direction * radius, p) < clear) return false;
            var grid = layout.Grids[shell];
            float limit = s.wallThickness * 0.5f + s.mineRadius + wallClearance;
            bool TooClose(int c)
            {
                foreach (int nb in grid.Neighbors[c])
                {
                    int e = grid.EdgeBetween(c, nb);
                    if (layout.Open[shell][e]) continue;
                    Vector3 a0 = grid.Edges[e].P0 * radius, a1 = grid.Edges[e].P1 * radius;
                    float t = Mathf.Clamp01(Vector3.Dot(p - a0, a1 - a0) / (a1 - a0).sqrMagnitude);
                    if (Vector3.Distance(p, a0 + (a1 - a0) * t) < limit) return true;
                }
                return false;
            }
            if (TooClose(cell)) return false;
            foreach (int nb in grid.Neighbors[cell]) if (TooClose(nb)) return false;
            return true;
        }

        /// <summary>
        /// A mine just off each wall of the group's cell (up to <paramref name="max"/> of them, walls picked at
        /// random), too close to the wall for the ball to squeeze past, so you can't skirt the group by hugging
        /// that wall. Each starts at the middle of its wall and slides along it until it leaves room to roll
        /// between it and every other mine on the shell. Returns how many it placed.
        /// </summary>
        static int PlaceWallMines(MazeLayout layout, LevelSettings s, Random rng, int shell, int section, int cell, float radius, int max)
        {
            var grid = layout.Grids[shell];
            Vector3 center = grid.CellCenters[cell] * radius;
            float inset = s.wallThickness * 0.5f + s.mineWallGap + s.mineRadius;
            float[] along = { 0.5f, 0.35f, 0.65f, 0.22f, 0.78f };
            var walls = new List<int>();
            foreach (int nb in grid.Neighbors[cell])
                if (!layout.Open[shell][grid.EdgeBetween(cell, nb)]) walls.Add(grid.EdgeBetween(cell, nb));
            Shuffle(walls, rng);
            int placed = 0;
            foreach (int wallEdge in walls)
            {
                if (placed >= max) break;
                var edge = grid.Edges[wallEdge];
                foreach (float t in along)
                {
                    Vector3 onWall = Vector3.Slerp(edge.P0, edge.P1, t) * radius;
                    Vector3 inward = Vector3.ProjectOnPlane(center - onWall, onWall).normalized;
                    Vector3 dir = (onWall + inward * inset).normalized;
                    // Its own wall is Mine Wall Gap away; this just keeps it off the next wall at a corner.
                    if (!MineFits(layout, s, shell, cell, dir, radius, Mathf.Min(0.3f, s.mineWallGap) - 0.02f)) continue;
                    layout.Mines.Add(new Mine { Shell = shell, Section = section, Direction = dir, NearWall = true });
                    placed++;
                    break;
                }
            }
            return placed;
        }

        /// <summary>The shortest route through a section, entry to exit, as cells on its shell.</summary>
        public List<int> Route(int section)
        {
            var sec = Sections[section];
            var grid = Grids[sec.Shell];
            var prev = new Dictionary<int, int> { [sec.EntryCell] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(sec.EntryCell);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int nb in grid.Neighbors[cur])
                {
                    if (prev.ContainsKey(nb) || Region[sec.Shell][nb] != section) continue;
                    if (!Open[sec.Shell][grid.EdgeBetween(cur, nb)]) continue;
                    prev[nb] = cur;
                    queue.Enqueue(nb);
                }
            }
            var route = new List<int>();
            for (int c = sec.ExitCell; c != -1; c = prev[c]) route.Insert(0, c);
            return route;
        }

        /// <summary>
        /// The shell each section is on, in order. Starts on the surface, moves one layer per section, and
        /// finishes on the innermost shell (whose section drops into the core). Small shells only get as
        /// many sections as they have room for.
        /// </summary>
        static int[] PlanLayers(LevelSettings s, SphereGrid[] grids, int climbs, Random rng)
        {
            int L = s.ShellCount;
            var moves = new List<int>();
            for (int i = 0; i < L - 1 + climbs; i++) moves.Add(1);
            for (int i = 0; i < climbs; i++) moves.Add(-1);

            // Sections a shell has room for: each takes up to about twice its main path in cells (the path plus
            // side lanes); a layout that still doesn't fit is retried.
            var capacity = new int[L];
            for (int k = 0; k < L; k++)
                capacity[k] = Mathf.Max(1, Mathf.FloorToInt(grids[k].CellCount / (s.sectionLengthMax * 2f)));

            var plan = new int[moves.Count + 1];
            var visits = new int[L];
            for (int tries = 0; tries < 200; tries++)
            {
                Shuffle(moves, rng);
                System.Array.Clear(visits, 0, L);
                int layer = 0;
                plan[0] = 0;
                visits[0] = 1;
                bool ok = true;
                for (int i = 0; i < moves.Count && ok; i++)
                {
                    layer += moves[i];
                    ok = layer >= 0 && layer < L && ++visits[layer] <= capacity[layer];
                    plan[i + 1] = layer;
                }
                if (ok) return plan;
            }
            return null;
        }

        static bool BuildSection(MazeLayout layout, LevelSettings s, int index, int shell, int entry, int nextLayer,
            Random rng, out int nextEntry)
        {
            nextEntry = -1;
            var grid = layout.Grids[shell];
            var region = layout.Region[shell];
            if (region[entry] != -1) return false;
            int minLength = Mathf.Max(2, s.sectionLengthMin);
            int maxLength = Mathf.Max(minLength, s.sectionLengthMax);

            for (int tries = 0; tries < 12; tries++)
            {
                var spine = GrowSpine(grid, region, entry, rng.Next(minLength, maxLength + 1), rng);
                if (spine.Count < minLength) continue;

                // End the section at the farthest point along the main path that can link to the next layer.
                for (int end = spine.Count - 1; end >= minLength - 1; end--)
                {
                    if (!TryLink(layout, s, shell, spine[end], nextLayer, out var hole, out nextEntry)) continue;
                    spine.RemoveRange(end + 1, spine.Count - end - 1);
                    CarveSection(layout, s, index, shell, spine, rng, out int routeCells, out int cellCount);
                    layout.Holes.Add(hole);
                    layout.Sections.Add(new Section
                    {
                        Shell = shell,
                        EntryCell = entry,
                        ExitCell = spine[spine.Count - 1],
                        ExitsUp = nextLayer < shell,
                        RouteCells = routeCells,
                        CellCount = cellCount,
                    });
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Randomized walk from the entry that never runs alongside itself, so its far-apart parts are only
        /// connected along it. Prefers going roughly straight. Backtracks out of dead ends, and returns the
        /// longest walk found if it can't reach the target length.
        /// </summary>
        static List<int> GrowSpine(SphereGrid grid, int[] region, int entry, int length, Random rng)
        {
            var path = new List<int> { entry };
            var onPath = new HashSet<int> { entry };
            var tried = new List<HashSet<int>> { new HashSet<int>() };
            var best = new List<int>(path);

            for (int steps = 0; steps < 400 && path.Count < length; steps++)
            {
                int cur = path[path.Count - 1];
                Vector3 heading = path.Count > 1 ? (grid.CellCenters[cur] - grid.CellCenters[path[path.Count - 2]]).normalized : Vector3.zero;
                int pick = -1;
                float bestScore = float.MinValue;
                foreach (int nb in grid.Neighbors[cur])
                {
                    if (region[nb] != -1 || onPath.Contains(nb) || tried[tried.Count - 1].Contains(nb)) continue;
                    bool touches = false;
                    foreach (int nn in grid.Neighbors[nb])
                        if (nn != cur && onPath.Contains(nn)) { touches = true; break; }
                    if (touches) continue;
                    Vector3 step = (grid.CellCenters[nb] - grid.CellCenters[cur]).normalized;
                    float score = (float)rng.NextDouble() + 0.6f * Vector3.Dot(step, heading);
                    if (score > bestScore) { bestScore = score; pick = nb; }
                }

                if (pick < 0)
                {
                    if (path.Count == 1) break;
                    onPath.Remove(cur);
                    path.RemoveAt(path.Count - 1);
                    tried.RemoveAt(tried.Count - 1);
                    continue;
                }
                tried[tried.Count - 1].Add(pick);
                path.Add(pick);
                onPath.Add(pick);
                tried.Add(new HashSet<int>());
                if (path.Count > best.Count) best = new List<int>(path);
            }
            return path.Count >= best.Count ? path : best;
        }

        /// <summary>
        /// Finds how the section ending at <paramref name="cell"/> connects to the next layer: a hole here
        /// down to a free cell below, or a boost pad here under a hole in a free cell above.
        /// </summary>
        static bool TryLink(MazeLayout layout, LevelSettings s, int shell, int cell, int nextLayer, out Hole hole, out int nextEntry)
        {
            hole = default;
            nextEntry = -1;
            Vector3 center = layout.Grids[shell].CellCenters[cell];
            if (nextLayer >= layout.ShellCount)
            {
                hole = new Hole { Shell = shell, Cell = cell, LandingCell = -1, Direction = center };
                return true;
            }

            bool down = nextLayer > shell;
            var other = layout.Grids[nextLayer];
            var otherRegion = layout.Region[nextLayer];
            foreach (int candidate in ClosestCells(other, center, 3))
            {
                if (otherRegion[candidate] != -1) continue;
                int free = 0;
                foreach (int nb in other.Neighbors[candidate]) if (otherRegion[nb] == -1) free++;
                if (free < 2) continue; // no room for the next section to start

                int upperShell = down ? shell : nextLayer;
                int upperCell = down ? cell : candidate, lowerCell = down ? candidate : cell;
                if (!HoleDirection(layout, s, upperShell, upperCell, lowerCell, out Vector3 dir)) continue;
                hole = new Hole { Shell = upperShell, Cell = upperCell, LandingCell = lowerCell, Direction = dir, TwoWay = !down };
                nextEntry = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Puts the hole between its cell and the landing cell below so it is within the wall-free slack of
        /// both. Fails when the two cells are too far apart for that.
        /// </summary>
        static bool HoleDirection(MazeLayout layout, LevelSettings s, int shell, int cell, int landingCell, out Vector3 dir)
        {
            var grid = layout.Grids[shell];
            var below = layout.Grids[shell + 1];
            float radius = s.shellRadii[shell], belowRadius = s.shellRadii[shell + 1];
            float holeRadius = s.HoleRadius(grid, shell);
            // How far (radians) the hole may sit from its cell center and still clear the walls, and likewise
            // the landing spot / boost pad below.
            float slackHere = Mathf.Max(0f, grid.CellSpacing * radius * 0.5f - s.wallThickness * 0.5f - holeRadius - 0.5f) / radius;
            float slackBelow = Mathf.Max(0f, below.CellSpacing * belowRadius * 0.5f - s.wallThickness * 0.5f
                                             - LevelSettings.PadRadius(holeRadius) - 0.5f) / belowRadius;

            Vector3 a = below.CellCenters[landingCell], b = grid.CellCenters[cell];
            float gap = Vector3.Angle(a, b) * Mathf.Deg2Rad;
            dir = b;
            if (gap > slackHere + slackBelow) return false;
            if (gap < 1e-6f) return true;
            float fromLanding = Mathf.Clamp(gap * slackBelow / Mathf.Max(1e-6f, slackHere + slackBelow), gap - slackHere, slackBelow);
            dir = Vector3.Slerp(a, b, fromLanding / gap).normalized;
            return true;
        }

        static List<int> ClosestCells(SphereGrid grid, Vector3 direction, int count)
        {
            var result = new List<int>(count + 1);
            var dots = new List<float>(count + 1);
            for (int c = 0; c < grid.CellCount; c++)
            {
                float d = Vector3.Dot(grid.CellCenters[c], direction);
                int at = dots.Count;
                while (at > 0 && dots[at - 1] < d) at--;
                if (at >= count) continue;
                dots.Insert(at, d);
                result.Insert(at, c);
                if (dots.Count > count) { dots.RemoveAt(count); result.RemoveAt(count); }
            }
            return result;
        }

        /// <summary>
        /// Claims the main path plus side lanes beside it, then carves: the main path is opened end to end,
        /// lane cells hang off it and get a second opening so they loop back (no dead ends), and a few main-path
        /// steps are walled off so the route weaves through the lanes.
        /// </summary>
        static void CarveSection(MazeLayout layout, LevelSettings s, int index, int shell, List<int> spine, Random rng,
            out int routeCells, out int cellCount)
        {
            var grid = layout.Grids[shell];
            var region = layout.Region[shell];
            var open = layout.Open[shell];
            int entry = spine[0], exit = spine[spine.Count - 1];

            // Anchor = the main-path step each cell sits beside.
            var anchor = new Dictionary<int, int>();
            for (int i = 0; i < spine.Count; i++) anchor[spine[i]] = i;
            var lanes = new List<int>();
            for (int i = 0; i < spine.Count; i++)
            {
                foreach (int nb in grid.Neighbors[spine[i]])
                {
                    if (region[nb] != -1 || anchor.ContainsKey(nb) || rng.NextDouble() >= s.sideLaneChance) continue;
                    bool local = true;
                    foreach (int nn in grid.Neighbors[nb])
                        if (anchor.TryGetValue(nn, out int a) && Mathf.Abs(a - i) > LaneWindow) { local = false; break; }
                    if (!local) continue;
                    anchor[nb] = i;
                    lanes.Add(nb);
                }
            }

            // A lane cell with only one neighbor in the section could only ever be a dead-end nook.
            int SectionNeighbors(int c)
            {
                int n = 0;
                foreach (int nb in grid.Neighbors[c]) if (anchor.ContainsKey(nb)) n++;
                return n;
            }
            for (bool pruned = true; pruned;)
            {
                pruned = false;
                for (int i = lanes.Count - 1; i >= 0; i--)
                {
                    if (SectionNeighbors(lanes[i]) >= 2) continue;
                    anchor.Remove(lanes[i]);
                    lanes.RemoveAt(i);
                    pruned = true;
                }
            }
            foreach (int c in anchor.Keys) region[c] = index;

            int Edge(int a, int b) => grid.EdgeBetween(a, b);
            int Openings(int c)
            {
                int n = 0;
                foreach (int nb in grid.Neighbors[c]) if (open[Edge(c, nb)]) n++;
                return n;
            }

            for (int i = 1; i < spine.Count; i++) open[Edge(spine[i - 1], spine[i])] = true;

            // Hang each lane cell off a neighbor that's already reachable (every lane cell touches the main path).
            var reached = new HashSet<int>(spine);
            var options = new List<int>(6);
            Shuffle(lanes, rng);
            for (int pending = lanes.Count; pending > 0;)
            {
                int before = pending;
                foreach (int c in lanes)
                {
                    if (reached.Contains(c)) continue;
                    options.Clear();
                    foreach (int nb in grid.Neighbors[c]) if (reached.Contains(nb)) options.Add(nb);
                    if (options.Count == 0) continue;
                    open[Edge(c, options[rng.Next(options.Count)])] = true;
                    reached.Add(c);
                    pending--;
                }
                if (pending == before) break;
            }

            // Give every dead-end lane cell a second way out, preferably into another dead end.
            Shuffle(lanes, rng);
            foreach (int c in lanes)
            {
                if (Openings(c) >= 2) continue;
                options.Clear();
                bool preferDeadEnds = false;
                foreach (int nb in grid.Neighbors[c])
                {
                    if (region[nb] != index || open[Edge(c, nb)]) continue;
                    bool deadEnd = Openings(nb) < 2;
                    if (deadEnd && !preferDeadEnds) { options.Clear(); preferDeadEnds = true; }
                    if (deadEnd == preferDeadEnds) options.Add(nb);
                }
                if (options.Count > 0) open[Edge(c, options[rng.Next(options.Count)])] = true;
            }

            // Wall off some main-path steps, where the section stays connected and nothing becomes a dead end.
            int Needed(int c) => c == entry || c == exit ? 1 : 2;
            var cells = new List<int>(anchor.Keys);
            for (int i = 1; i < spine.Count; i++)
            {
                if (rng.NextDouble() >= s.mainPathCutChance) continue;
                int a = spine[i - 1], b = spine[i];
                if (Openings(a) - 1 < Needed(a) || Openings(b) - 1 < Needed(b)) continue;
                int e = Edge(a, b);
                open[e] = false;
                if (Distances(grid, open, region, index, entry).Count < cells.Count) open[e] = true;
            }

            var dist = Distances(grid, open, region, index, entry);
            routeCells = dist.TryGetValue(exit, out int d) ? d + 1 : -1;
            cellCount = cells.Count;
        }

        /// <summary>Breadth-first step counts from a cell to every cell of its section reachable through open edges.</summary>
        static Dictionary<int, int> Distances(SphereGrid grid, bool[] open, int[] region, int index, int from)
        {
            var dist = new Dictionary<int, int> { [from] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int nb in grid.Neighbors[cur])
                {
                    if (region[nb] != index || dist.ContainsKey(nb) || !open[grid.EdgeBetween(cur, nb)]) continue;
                    dist[nb] = dist[cur] + 1;
                    queue.Enqueue(nb);
                }
            }
            return dist;
        }

        /// <summary>Randomized depth-first maze through the unused cells, so the rest of the shell isn't a bare plain.</summary>
        static void CarveFiller(SphereGrid grid, int[] region, bool[] open, Random rng)
        {
            int n = grid.CellCount;
            var visited = new bool[n];
            var stack = new Stack<int>();
            var options = new List<int>(6);

            for (int startCell = 0; startCell < n; startCell++)
            {
                if (visited[startCell] || region[startCell] != -1) continue;
                visited[startCell] = true;
                stack.Push(startCell);
                while (stack.Count > 0)
                {
                    int cur = stack.Peek();
                    options.Clear();
                    foreach (int nb in grid.Neighbors[cur])
                        if (!visited[nb] && region[nb] == -1) options.Add(nb);
                    if (options.Count == 0) { stack.Pop(); continue; }
                    int next = options[rng.Next(options.Count)];
                    open[grid.EdgeBetween(cur, next)] = true;
                    visited[next] = true;
                    stack.Push(next);
                }
            }
        }

        static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Open sides of a cell.</summary>
        public int Openings(int shell, int cell)
        {
            var grid = Grids[shell];
            int n = 0;
            foreach (int nb in grid.Neighbors[cell]) if (Open[shell][grid.EdgeBetween(cell, nb)]) n++;
            return n;
        }

        /// <summary>Section index at a position on a shell, or -1 for filler.</summary>
        public int SectionAt(int shell, Vector3 position) =>
            shell >= 0 && shell < ShellCount ? Region[shell][Grids[shell].NearestCell(position.normalized)] : -1;
    }
}
