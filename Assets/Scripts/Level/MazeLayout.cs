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

        // Clear space kept between any two mines, so the ball (1 unit across) can always roll between them.
        public const float MinePassage = 1.6f;

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
        public List<LaserGate> LaserGates = new List<LaserGate>();
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
            PlaceMines(layout, s, new Random(seed * 31 + 7));
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

            // Doorways along the route, middle first, at least two cells from either end.
            var candidates = new List<int>();
            for (int k = 2; k + 3 < route.Count; k++) candidates.Add(k);
            candidates.Sort((a, b) => Mathf.Abs(2 * a + 1 - route.Count).CompareTo(Mathf.Abs(2 * b + 1 - route.Count)));
            foreach (int k in candidates)
            {
                int near = route[k], far = route[k + 1];
                int edge = grid.EdgeBetween(near, far);
                // Cells reachable from the entry without going through this doorway.
                var reach = layout.Reachable(section, sec.EntryCell, edge);
                if (reach.ContainsKey(sec.ExitCell)) continue; // a side lane goes around it

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

        /// <summary>Steps from <paramref name="from"/> to every cell of a section reachable without crossing <paramref name="blockedEdge"/>.</summary>
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

        /// <summary>Cells a mine group shouldn't use: either side of a laser gate, and its switch.</summary>
        bool ReservedForGate(int shell, int cell)
        {
            var grid = Grids[shell];
            foreach (var g in LaserGates)
                if (g.Shell == shell && (cell == g.SwitchCell || cell == grid.Edges[g.Edge].A || cell == grid.Edges[g.Edge].B))
                    return true;
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
                // The smallest shells are cramped, so they get one group fewer, each with one mine fewer.
                int fewer = shell >= layout.ShellCount - s.fewerMinesInnermost ? 1 : 0;
                int groups = rng.Next(s.mineGroupsMin, s.mineGroupsMax + 1) - fewer;
                groups = Mathf.Min(groups, (last - first) / 2 + 1);
                for (int g = 0; g < groups; g++)
                {
                    // One group per equal slice of the stretch, somewhere in that slice.
                    int lo = first + (last - first + 1) * g / groups;
                    int hi = first + (last - first + 1) * (g + 1) / groups - 1;
                    hi = Mathf.Max(lo, hi);
                    int at = rng.Next(lo, hi + 1);
                    // Keep laser gates and their switches clear; try the rest of the slice.
                    for (int tries = 0; tries <= hi - lo && layout.ReservedForGate(shell, route[at]); tries++)
                        at = lo + (at - lo + 1) % (hi - lo + 1);
                    int cell = route[at];
                    if (layout.ReservedForGate(shell, cell)) continue;
                    PlaceGroup(layout, s, rng, shell, i, cell, radius, fewer);
                    PlaceWallMines(layout, s, shell, i, cell, radius);
                }
            }
        }

        static void PlaceGroup(MazeLayout layout, LevelSettings s, Random rng, int shell, int section, int cell, float radius,
            int fewer)
        {
            Vector3 center = layout.Grids[shell].CellCenters[cell];
            // Tangent frame at the cell center, turned to a random angle.
            Vector3 x = Vector3.Cross(center, Mathf.Abs(center.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 y = Vector3.Cross(center, x);
            float turn = (float)rng.NextDouble() * Mathf.PI * 2f;
            Vector3 u = x * Mathf.Cos(turn) + y * Mathf.Sin(turn), v = Vector3.Cross(center, u);
            // Shift the whole group a little off center so it doesn't always sit in the middle.
            float shift = (float)rng.NextDouble() * 1.5f, shiftAngle = (float)rng.NextDouble() * Mathf.PI * 2f;
            Vector2 offset = new Vector2(Mathf.Cos(shiftAngle), Mathf.Sin(shiftAngle)) * shift;

            // 2 mines: a pair; 3: a triangle; 4: a square. Neighbors are Mine Spacing apart.
            int count = rng.Next(3, 5) - fewer;
            float ring = s.mineSpacing / (2f * Mathf.Sin(Mathf.PI / count));
            Vector3 Spot(int k, Vector2 shiftBy)
            {
                float a = k * Mathf.PI * 2f / count;
                Vector2 p = shiftBy + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                return (center * radius + u * p.x + v * p.y).normalized;
            }
            // Mines must stay clear of the walls and leave room to roll past every other mine. If the shifted
            // group doesn't fit the cell, center it; any mine that still doesn't fit is left out.
            const float wallClearance = 0.5f;
            bool allFit = true;
            for (int k = 0; k < count && allFit; k++) allFit = MineFits(layout, s, shell, cell, Spot(k, offset), radius, wallClearance);
            if (!allFit) offset = Vector2.zero;
            for (int k = 0; k < count; k++)
            {
                Vector3 dir = Spot(k, offset);
                if (MineFits(layout, s, shell, cell, dir, radius, wallClearance))
                    layout.Mines.Add(new Mine { Shell = shell, Section = section, Direction = dir });
            }
        }

        /// <summary>
        /// Whether a mine here leaves room to roll between it and every mine already on the shell, and stays at
        /// least <paramref name="wallClearance"/> from the walls around the cell.
        /// </summary>
        static bool MineFits(MazeLayout layout, LevelSettings s, int shell, int cell, Vector3 dir, float radius, float wallClearance)
        {
            Vector3 p = dir * radius;
            float clear = 2f * s.mineRadius + MinePassage;
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
        /// One mine just off each wall of the group's cell, too close to the wall for the ball to squeeze past,
        /// so you can't skirt the group by hugging a wall. Each starts at the middle of its wall and slides
        /// along it until it leaves room to roll between it and every other mine on the shell.
        /// </summary>
        static void PlaceWallMines(MazeLayout layout, LevelSettings s, int shell, int section, int cell, float radius)
        {
            var grid = layout.Grids[shell];
            Vector3 center = grid.CellCenters[cell] * radius;
            float inset = s.wallThickness * 0.5f + s.mineWallGap + s.mineRadius;
            float[] along = { 0.5f, 0.35f, 0.65f, 0.22f, 0.78f };
            foreach (int nb in grid.Neighbors[cell])
            {
                var edge = grid.Edges[grid.EdgeBetween(cell, nb)];
                if (layout.Open[shell][grid.EdgeBetween(cell, nb)]) continue;
                foreach (float t in along)
                {
                    Vector3 onWall = Vector3.Slerp(edge.P0, edge.P1, t) * radius;
                    Vector3 inward = Vector3.ProjectOnPlane(center - onWall, onWall).normalized;
                    Vector3 dir = (onWall + inward * inset).normalized;
                    // Its own wall is Mine Wall Gap away; this just keeps it off the next wall at a corner.
                    if (!MineFits(layout, s, shell, cell, dir, radius, Mathf.Min(0.3f, s.mineWallGap) - 0.02f)) continue;
                    layout.Mines.Add(new Mine { Shell = shell, Section = section, Direction = dir, NearWall = true });
                    break;
                }
            }
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

            var capacity = new int[L];
            for (int k = 0; k < L; k++)
                capacity[k] = Mathf.Max(1, Mathf.FloorToInt(grids[k].CellCount / (s.sectionLengthMax * 3f)));

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
