using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace SphereDescent
{
    /// <summary>
    /// Pure-data description of a world: per-shell regions and maze walls, plus the holes linking layers.
    ///
    /// Every shell is split into several regions that are walled off from each other. Within a region the
    /// maze is fully connected, so the puzzle is at the region level: which hole drops you into which region
    /// below, and which boost pads take you back up. Layouts are searched until the exit is reachable and
    /// no reachable region is a dead end. Optionally (LevelSettings.requireClimb) the core must also be
    /// unreachable by dropping alone, forcing at least one trip back up.
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

        public SphereGrid[] Grids; // [shell]
        public int ShellCount;
        public int[][] Region;     // [shell][cell] -> region id
        public int[] RegionCount;  // [shell]
        public bool[][] Open;      // [shell][edge] -> true when there is no wall
        public List<Hole> Holes = new List<Hole>();
        public int StartCell;
        public Vector3 ExitDirection;
        public int Seed;
        public int AttemptsUsed;
        public int ShortestRouteMoves; // number of hole transitions on the shortest route
        public int ShortestRouteUps;   // launches on the shortest route (0 means the core can be reached by drops alone)

        const int MaxAttempts = 5000;

        public static MazeLayout Generate(LevelSettings settings, int seed)
        {
            for (int retry = 0; retry < 20; retry++)
            {
                var layout = TryGenerate(settings, seed + retry * 104729);
                if (layout != null) return layout;
            }
            Debug.LogError("MazeLayout: could not find a valid layout; using an unvalidated one.");
            return TryGenerate(settings, seed, requireValid: false);
        }

        static MazeLayout TryGenerate(LevelSettings s, int seed, bool requireValid = true)
        {
            var rng = new Random(seed);
            int L = s.ShellCount;
            var layout = new MazeLayout
            {
                Grids = new SphereGrid[L],
                ShellCount = L,
                Region = new int[L][],
                RegionCount = new int[L],
                Open = new bool[L][],
                Seed = seed,
            };

            for (int shell = 0; shell < L; shell++)
            {
                var grid = layout.Grids[shell] = SphereGrid.ForRadius(s.shellRadii[shell], s.cellSpacing);
                int k = s.RegionsFor(shell);
                layout.RegionCount[shell] = k;
                layout.Region[shell] = GrowRegions(grid, k, rng);
                layout.Open[shell] = CarveMaze(grid, layout.Region[shell], s.loopChance, rng);
            }
            layout.StartCell = rng.Next(layout.Grids[0].CellCount);

            // For each cell on the layer below a shell, the nearest cell on that shell.
            var nearestAbove = new int[L][];
            for (int shell = 0; shell < L - 1; shell++)
            {
                var below = layout.Grids[shell + 1];
                nearestAbove[shell] = new int[below.CellCount];
                for (int c = 0; c < below.CellCount; c++)
                    nearestAbove[shell][c] = layout.Grids[shell].NearestCell(below.CellCenters[c]);
            }

            List<Hole> best = null;
            int bestMoves = -1, bestUps = 0;
            int attempt;
            for (attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var holes = PlaceHoles(layout, s, nearestAbove, rng);
                if (!Evaluate(layout, holes, out int moves, out int ups)) continue;
                if (ups > 0 || !s.requireClimb)
                {
                    best = holes; bestMoves = moves; bestUps = ups;
                    break;
                }
                if (best == null) { best = holes; bestMoves = moves; bestUps = ups; }
            }

            if (best == null)
            {
                if (requireValid) return null;
                best = PlaceHoles(layout, s, nearestAbove, rng);
            }

            layout.Holes = best;
            layout.AttemptsUsed = attempt;
            layout.ShortestRouteMoves = bestMoves;
            layout.ShortestRouteUps = bestUps;

            // Exit goes wherever on the core is farthest from every hole dropping into it.
            float bestClearance = -1f;
            var lastGrid = layout.Grids[L - 1];
            layout.ExitDirection = -layout.Grids[0].CellCenters[layout.StartCell];
            for (int c = 0; c < lastGrid.CellCount; c++)
            {
                float clearance = float.MaxValue;
                foreach (var h in best)
                    if (h.Shell == L - 1) clearance = Mathf.Min(clearance, Vector3.Angle(lastGrid.CellCenters[c], h.Direction));
                if (clearance > bestClearance) { bestClearance = clearance; layout.ExitDirection = lastGrid.CellCenters[c]; }
            }
            return layout;
        }

        /// <summary>Splits the sphere into k contiguous regions by randomized multi-source flood fill.</summary>
        static int[] GrowRegions(SphereGrid grid, int k, Random rng)
        {
            int n = grid.CellCount;
            var region = new int[n];
            for (int i = 0; i < n; i++) region[i] = -1;
            var frontier = new List<int>[k];

            // Spread seeds apart: each new seed is the best of several random candidates.
            var seeds = new List<int>();
            for (int r = 0; r < k; r++)
            {
                int bestCell = rng.Next(n);
                float bestScore = -1f;
                for (int c = 0; c < 24 && seeds.Count > 0; c++)
                {
                    int cand = rng.Next(n);
                    if (region[cand] != -1) continue;
                    float minDist = float.MaxValue;
                    foreach (int sd in seeds) minDist = Mathf.Min(minDist, Vector3.Angle(grid.CellCenters[cand], grid.CellCenters[sd]));
                    if (minDist > bestScore) { bestScore = minDist; bestCell = cand; }
                }
                while (region[bestCell] != -1) bestCell = (bestCell + 1) % n;
                seeds.Add(bestCell);
                region[bestCell] = r;
                frontier[r] = new List<int>(grid.Neighbors[bestCell]);
            }

            int remaining = n - k;
            var active = new List<int>();
            while (remaining > 0)
            {
                active.Clear();
                for (int r = 0; r < k; r++) if (frontier[r].Count > 0) active.Add(r);
                if (active.Count == 0) break;
                int reg = active[rng.Next(active.Count)];
                var f = frontier[reg];
                int idx = rng.Next(f.Count);
                int cell = f[idx];
                f[idx] = f[f.Count - 1];
                f.RemoveAt(f.Count - 1);
                if (region[cell] != -1) continue;
                region[cell] = reg;
                remaining--;
                foreach (int nb in grid.Neighbors[cell]) if (region[nb] == -1) f.Add(nb);
            }
            return region;
        }

        /// <summary>Randomized depth-first maze inside each region, plus a few extra openings for loops.</summary>
        static bool[] CarveMaze(SphereGrid grid, int[] region, float loopChance, Random rng)
        {
            int n = grid.CellCount;
            var open = new bool[grid.Edges.Length];
            var visited = new bool[n];
            var stack = new Stack<int>();
            var options = new List<int>(6);

            for (int startCell = 0; startCell < n; startCell++)
            {
                if (visited[startCell]) continue;
                visited[startCell] = true;
                stack.Push(startCell);
                while (stack.Count > 0)
                {
                    int cur = stack.Peek();
                    options.Clear();
                    foreach (int nb in grid.Neighbors[cur])
                        if (!visited[nb] && region[nb] == region[cur]) options.Add(nb);
                    if (options.Count == 0) { stack.Pop(); continue; }
                    int next = options[rng.Next(options.Count)];
                    open[grid.EdgeBetween(cur, next)] = true;
                    visited[next] = true;
                    stack.Push(next);
                }
            }

            for (int e = 0; e < grid.Edges.Length; e++)
            {
                var edge = grid.Edges[e];
                if (!open[e] && region[edge.A] == region[edge.B] && rng.NextDouble() < loopChance) open[e] = true;
            }
            return open;
        }

        static List<Hole> PlaceHoles(MazeLayout layout, LevelSettings s, int[][] nearestAbove, Random rng)
        {
            var holes = new List<Hole>();
            var landingCells = new HashSet<int>(); // landing cells on the current shell from the shell above

            for (int shell = 0; shell < layout.ShellCount; shell++)
            {
                var grid = layout.Grids[shell];
                float radius = s.shellRadii[shell];
                float holeRadius = s.HoleRadius(grid, shell);
                // How far (radians) the hole may sit from its cell center and still clear the walls.
                float slackHere = Mathf.Max(0f, grid.CellSpacing * radius * 0.5f - s.wallThickness * 0.5f - holeRadius - 0.5f) / radius;

                bool intoCore = shell == layout.ShellCount - 1;
                SphereGrid below = intoCore ? null : layout.Grids[shell + 1];
                float slackBelow = 0f;
                if (!intoCore)
                {
                    float belowRadius = s.shellRadii[shell + 1];
                    slackBelow = Mathf.Max(0f, below.CellSpacing * belowRadius * 0.5f - s.wallThickness * 0.5f
                                               - LevelSettings.PadRadius(holeRadius) - 0.5f) / belowRadius;
                }

                // Holes on the same shell keep at least one cell between them, and never open where you land
                // from the shell above (you'd fall straight through, or a pad would sit in the hole).
                var blocked = new HashSet<int>(landingCells);
                if (shell == 0)
                {
                    blocked.Add(layout.StartCell);
                    foreach (int nb in grid.Neighbors[layout.StartCell]) blocked.Add(nb);
                }
                landingCells = new HashSet<int>();

                int count = s.HolesFor(grid.CellCount);
                for (int i = 0; i < count; i++)
                {
                    int cell = -1, landing = -1;
                    Vector3 dir = Vector3.zero;
                    for (int tries = 0; tries < 200; tries++)
                    {
                        if (intoCore)
                        {
                            int c = rng.Next(grid.CellCount);
                            if (blocked.Contains(c)) continue;
                            cell = c;
                            dir = grid.CellCenters[c];
                            break;
                        }

                        // Pick a landing cell below, then put the hole between it and the nearest cell above
                        // so that it is within the wall-free slack of both.
                        int lc = rng.Next(below.CellCount);
                        int hc = nearestAbove[shell][lc];
                        if (blocked.Contains(hc)) continue;
                        Vector3 a = below.CellCenters[lc], b = grid.CellCenters[hc];
                        float gap = Vector3.Angle(a, b) * Mathf.Deg2Rad;
                        if (gap > slackHere + slackBelow) continue;
                        float fromLanding = gap < 1e-6f ? 0f
                            : Mathf.Clamp(gap * slackBelow / Mathf.Max(1e-6f, slackHere + slackBelow), gap - slackHere, slackBelow);
                        dir = gap < 1e-6f ? b : Vector3.Slerp(a, b, fromLanding / gap).normalized;
                        cell = hc;
                        landing = lc;
                        break;
                    }
                    if (cell < 0) break;
                    blocked.Add(cell);
                    foreach (int nb in grid.Neighbors[cell]) blocked.Add(nb);
                    if (landing >= 0) landingCells.Add(landing);
                    holes.Add(new Hole
                    {
                        Shell = shell,
                        Cell = cell,
                        LandingCell = landing,
                        Direction = dir,
                        TwoWay = !intoCore && rng.NextDouble() < s.twoWayChance,
                    });
                }
            }
            return holes;
        }

        /// <summary>
        /// Region-level reachability. Valid when the core is reachable from the start and every reachable
        /// region can still reach the core (no soft-locks). Reports the shortest route and how many launches it uses.
        /// </summary>
        static bool Evaluate(MazeLayout layout, List<Hole> holes, out int moves, out int ups)
        {
            moves = -1; ups = 0;
            int L = layout.ShellCount;
            var offset = new int[L + 1];
            for (int i = 0; i < L; i++) offset[i + 1] = offset[i] + layout.RegionCount[i];
            int core = offset[L];
            int nodeCount = core + 1;

            int Node(int layer, int cell) => layer >= L ? core : offset[layer] + layout.Region[layer][cell];

            var fwd = new List<(int to, bool up)>[nodeCount];
            var rev = new List<int>[nodeCount];
            for (int i = 0; i < nodeCount; i++) { fwd[i] = new List<(int, bool)>(); rev[i] = new List<int>(); }
            foreach (var h in holes)
            {
                int a = Node(h.Shell, h.Cell), b = h.LandingCell < 0 ? core : Node(h.Shell + 1, h.LandingCell);
                fwd[a].Add((b, false)); rev[b].Add(a);
                if (h.TwoWay) { fwd[b].Add((a, true)); rev[a].Add(b); }
            }

            int start = Node(0, layout.StartCell);

            // Shortest route (BFS) from start, tracking launches along the route.
            var dist = new int[nodeCount];
            var upsTo = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++) dist[i] = -1;
            var queue = new Queue<int>();
            dist[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (var (to, up) in fwd[cur])
                {
                    if (dist[to] != -1) continue;
                    dist[to] = dist[cur] + 1;
                    upsTo[to] = upsTo[cur] + (up ? 1 : 0);
                    queue.Enqueue(to);
                }
            }
            if (dist[core] < 0) return false;

            // Every reachable region must still be able to reach the core.
            var canFinish = new bool[nodeCount];
            canFinish[core] = true;
            queue.Enqueue(core);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int from in rev[cur])
                {
                    if (canFinish[from]) continue;
                    canFinish[from] = true;
                    queue.Enqueue(from);
                }
            }
            for (int i = 0; i < nodeCount; i++)
                if (dist[i] >= 0 && !canFinish[i]) return false;

            moves = dist[core];
            ups = upsTo[core];

            // Force at least one launch: the core must be unreachable using drops alone.
            var downSeen = new bool[nodeCount];
            downSeen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (var (to, up) in fwd[cur])
                {
                    if (up || downSeen[to]) continue;
                    downSeen[to] = true;
                    queue.Enqueue(to);
                }
            }
            if (downSeen[core]) ups = 0;
            else ups = Mathf.Max(1, ups);
            return true;
        }

        public int RegionAt(int shell, int cell) => Region[shell][cell];
    }
}
