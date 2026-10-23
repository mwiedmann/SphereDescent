using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// Plays world 1's first section like a person would, for a video: follows the route with a gentle weave,
    /// takes the laser gate's spur to its pad and waits for the cable, crosses the acid bridge slowly, times
    /// the camera run, dodges mortar bombs, hits the checkpoints, grabs some cubes (one or two down side
    /// lanes) and drops through the hole at the end. Optionally writes every frame as a JPEG.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class VideoPilot : MonoBehaviour
    {
        public static bool Done;

        // Recording.
        bool capture;
        string outDir;
        const int Width = 1600, Height = 900;
        RenderTexture rt;
        Texture2D tex;
        int frame;

        LevelInstance level;
        MazeLayout layout;
        LevelSettings s;
        BallController ball;
        Rigidbody rb;
        Camera cam;
        FieldInfo moveField, bombTo, bombAge;
        MethodInfo sweepPoint;
        SphereGrid grid;
        float r;
        const int Shell = 0, Section = 0;
        List<int> route;

        // Navigation graph over the section's floor.
        readonly List<Vector3> nodes = new List<Vector3>();
        readonly List<float> clear = new List<float>();
        readonly List<List<(int to, bool gate)>> links = new List<List<(int, bool)>>();
        readonly HashSet<int> bridgeNodes = new HashSet<int>();
        readonly List<int> nodeCell = new List<int>();
        HashSet<int> routeCells;
        int gateEdge = -1;
        LaserGate gate;
        MazeLayout.AcidPool pool;
        bool hasPool;
        readonly List<SecurityCamera> runCameras = new List<SecurityCamera>();

        enum Kind { Cube, Checkpoint, Pad, GateWait, BridgeIn, BridgeOut, CameraHold, CameraRun, Hole }
        class Goal { public Kind kind; public Vector3 pos; public float order; public string label; }
        readonly List<Goal> goals = new List<Goal>();
        int goalIndex;

        List<Vector3> path;
        List<float> pathClear;
        List<float> pathLen;
        int pathAt;

        enum Mode { Intro, Go, WaitGate, WaitCamera, Dash, Finish }
        Mode mode = Mode.Intro;
        float modeTime, dashStart, wiggle, wiggleRate = 1.25f;
        int deaths, cubesTaken;
        bool wasDestroyed;
        float landedFor;
        float t;

        void Start()
        {
            capture = System.Environment.GetEnvironmentVariable("VIDEO_CAPTURE") == "1";
            outDir = System.Environment.GetEnvironmentVariable("PREVIEW_DIR");
            Time.captureFramerate = 30;
            StartCoroutine(Setup());
        }

        System.Collections.IEnumerator Setup()
        {
            yield return null;
            yield return null;
            level = FindAnyObjectByType<LevelInstance>();
            layout = level.Layout;
            s = level.Settings;
            ball = FindAnyObjectByType<BallController>();
            rb = ball.GetComponent<Rigidbody>();
            cam = Camera.main;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            moveField = typeof(BallController).GetField("moveInput", flags);
            bombTo = typeof(MortarBomb).GetField("to", flags);
            bombAge = typeof(MortarBomb).GetField("age", flags);
            sweepPoint = typeof(SecurityCamera).GetMethod("SweepPoint", flags);
            grid = layout.Grids[Shell];
            r = s.shellRadii[Shell];
            route = layout.Route(Section);
            routeCells = new HashSet<int>(route);
            BuildGraph();
            BuildGoals();
            if (capture)
            {
                Directory.CreateDirectory(outDir);
                rt = new RenderTexture(Width, Height, 24) { antiAliasing = 4 };
                tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            }
            Debug.Log($"[Video] graph {nodes.Count} nodes, {goals.Count} goals: {string.Join(", ", goals.ConvertAll(g => g.label))}");
            ready = true;
        }

        bool ready;

        // ---------------------------------------------------------------- graph

        void BuildGraph()
        {
            var cells = new List<int>();
            for (int c = 0; c < grid.CellCount; c++) if (layout.Region[Shell][c] == Section) cells.Add(c);
            hasPool = layout.AcidPools.Exists(p => p.Section == Section);
            if (hasPool) pool = layout.AcidPools.Find(p => p.Section == Section);
            var g = layout.LaserGates.Find(x => x.Section == Section);
            gateEdge = layout.LaserGates.Exists(x => x.Section == Section) ? g.Edge : -1;
            foreach (var lg in FindObjectsByType<LaserGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var e = grid.Edges[gateEdge];
                if (gateEdge >= 0 && Vector3.Angle(lg.transform.position, e.P0 + e.P1) < 1f) gate = lg;
            }

            var mines = new List<Vector3>();
            foreach (var m in layout.Mines) if (m.Shell == Shell) mines.Add(m.Direction * r);
            var holes = new List<(Vector3 p, float rad)>();
            foreach (var h in layout.Holes)
                if (h.Shell == Shell || h.Shell + 1 == Shell) holes.Add((h.Direction * r, s.HoleRadius(layout.Grids[h.Shell], h.Shell)));

            float Clearance(Vector3 p, int cell)
            {
                float best = 99f;
                var near = new List<int>(grid.Neighbors[cell]) { cell };
                foreach (int a in near)
                    foreach (int nb in grid.Neighbors[a])
                    {
                        int e = grid.EdgeBetween(a, nb);
                        if (layout.Open[Shell][e]) continue;
                        Vector3 a0 = grid.Edges[e].P0 * r, a1 = grid.Edges[e].P1 * r;
                        float tt = Mathf.Clamp01(Vector3.Dot(p - a0, a1 - a0) / (a1 - a0).sqrMagnitude);
                        best = Mathf.Min(best, Vector3.Distance(p, a0 + (a1 - a0) * tt) - s.wallThickness * 0.5f);
                    }
                foreach (var m in mines) best = Mathf.Min(best, Vector3.Distance(p, m) - s.mineRadius * 0.9f - 0.35f);
                foreach (var h in holes) best = Mathf.Min(best, Vector3.Distance(p, h.p) - h.rad - 0.6f);
                return best;
            }

            const float step = 0.85f;
            var hash = new Dictionary<Vector3Int, List<int>>();
            void AddNode(Vector3 p, float c, int cell)
            {
                nodes.Add(p);
                clear.Add(c);
                nodeCell.Add(cell);
                links.Add(new List<(int, bool)>());
                var key = Vector3Int.FloorToInt(p / 1.3f);
                if (!hash.TryGetValue(key, out var list)) hash[key] = list = new List<int>();
                list.Add(nodes.Count - 1);
            }
            foreach (int c in cells)
            {
                if (hasPool && c == pool.Cell) continue;
                Vector3 C = grid.CellCenters[c];
                Vector3 t1 = Vector3.Cross(C, Mathf.Abs(C.y) < 0.9f ? Vector3.up : Vector3.right).normalized, t2 = Vector3.Cross(C, t1);
                for (float x = -11f; x <= 11f; x += step)
                    for (float y = -11f; y <= 11f; y += step)
                    {
                        Vector3 q = (C * r + t1 * x + t2 * y).normalized;
                        if (grid.NearestCell(q) != c) continue;
                        Vector3 p = q * r;
                        float cl = Clearance(p, c);
                        if (cl < 0.85f) continue;
                        AddNode(p, cl, c);
                    }
            }
            int lattice = nodes.Count;

            // The bridge: a chain of nodes along its middle, the only way over the acid.
            if (hasPool)
            {
                Vector3 Doorway(int other)
                {
                    var e = grid.Edges[grid.EdgeBetween(pool.Cell, other)];
                    Vector3 mid = (e.P0 + e.P1).normalized;
                    Vector3 away = Vector3.ProjectOnPlane(grid.CellCenters[other] - mid, mid).normalized;
                    return (mid * r + away * 1.5f).normalized;
                }
                var pts = new List<Vector3> { Doorway(pool.From), grid.CellCenters[pool.Cell], Doorway(pool.To) };
                int first = nodes.Count;
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    int n = Mathf.CeilToInt(Vector3.Angle(pts[k], pts[k + 1]) * Mathf.Deg2Rad * r / 0.5f);
                    for (int i = 0; i < n; i++) { AddNode(Vector3.Slerp(pts[k], pts[k + 1], i / (float)n).normalized * r, 0.3f, pool.Cell); bridgeNodes.Add(nodes.Count - 1); }
                }
                AddNode(pts[2] * r, 0.3f, pool.Cell);
                bridgeNodes.Add(nodes.Count - 1);
                for (int i = first; i + 1 < nodes.Count; i++) { links[i].Add((i + 1, false)); links[i + 1].Add((i, false)); }
                foreach (int end in new[] { first, nodes.Count - 1 })
                    for (int j = 0; j < lattice; j++)
                        if (Vector3.Distance(nodes[j], nodes[end]) < 1.4f) { links[end].Add((j, false)); links[j].Add((end, false)); }
            }

            // Lattice links: within a cell, or across an open doorway (the gate's only once it's off).
            for (int i = 0; i < lattice; i++)
            {
                var key = Vector3Int.FloorToInt(nodes[i] / 1.3f);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!hash.TryGetValue(key + new Vector3Int(dx, dy, dz), out var list)) continue;
                            foreach (int j in list)
                            {
                                if (j <= i || j >= lattice || Vector3.Distance(nodes[i], nodes[j]) > 1.25f) continue;
                                int a = nodeCell[i], b = nodeCell[j];
                                bool isGate = false;
                                if (a != b)
                                {
                                    int e = grid.EdgeBetween(a, b);
                                    if (e < 0 || !layout.Open[Shell][e]) continue;
                                    Vector3 mid = ((nodes[i] + nodes[j]) * 0.5f).normalized * r;
                                    if (Clearance(mid, a) < 0.85f) continue;
                                    isGate = e == gateEdge;
                                }
                                links[i].Add((j, isGate));
                                links[j].Add((i, isGate));
                            }
                        }
            }

            // The camera run's cameras.
            foreach (var c in layout.Cameras)
            {
                if (c.Section != Section) continue;
                var e = grid.Edges[c.Edge];
                foreach (var sc in FindObjectsByType<SecurityCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (sc.layer == Shell && Vector3.Angle(sc.center, e.P0 + e.P1) < 0.01f) runCameras.Add(sc);
            }
        }

        int NearestNode(Vector3 p, bool allowBridge = true)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!allowBridge && bridgeNodes.Contains(i)) continue;
                float d = (nodes[i] - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        List<int> AStar(int from, int to)
        {
            bool gateOpen = gate == null || !gate.Active;
            var gScore = new Dictionary<int, float> { [from] = 0f };
            var prev = new Dictionary<int, int>();
            var open = new SortedSet<(float f, int n)> { (0f, from) };
            var done = new HashSet<int>();
            while (open.Count > 0)
            {
                var cur = open.Min;
                open.Remove(cur);
                int n = cur.n;
                if (n == to) break;
                if (!done.Add(n)) continue;
                foreach (var (m, isGate) in links[n])
                {
                    if (isGate && !gateOpen) continue;
                    float d = Vector3.Distance(nodes[n], nodes[m]);
                    float cost = bridgeNodes.Contains(m) ? d : d * (1f + 1.6f / (clear[m] + 0.2f));
                    // Keep to the main route; side lanes only when a goal is down one.
                    if (!routeCells.Contains(nodeCell[m])) cost *= 2.2f;
                    float g = gScore[n] + cost;
                    if (gScore.TryGetValue(m, out float old) && old <= g) continue;
                    gScore[m] = g;
                    prev[m] = n;
                    open.Add((g + Vector3.Distance(nodes[m], nodes[to]), m));
                }
            }
            var result = new List<int>();
            if (!gScore.ContainsKey(to)) return result;
            for (int c = to; ; c = prev[c]) { result.Insert(0, c); if (c == from) break; }
            return result;
        }

        bool Plan(Vector3 target, bool allowBridgeStart = true)
        {
            int a = NearestNode(Ground(ball.transform.position), allowBridgeStart);
            int b = NearestNode(target);
            var ids = AStar(a, b);
            if (ids.Count == 0) { Debug.LogWarning($"[Video] no path to {goals[goalIndex].label}"); return false; }
            path = ids.ConvertAll(i => nodes[i]);
            pathClear = ids.ConvertAll(i => clear[i]);
            pathLen = new List<float> { 0f };
            for (int i = 1; i < path.Count; i++) pathLen.Add(pathLen[i - 1] + Vector3.Distance(path[i - 1], path[i]));
            pathAt = 0;
            return true;
        }

        Vector3 Ground(Vector3 p) => p.normalized * r;

        // ---------------------------------------------------------------- goals

        void BuildGoals()
        {
            float RouteIndexOf(Vector3 dir)
            {
                int best = 0;
                float bd = float.MaxValue;
                for (int k = 0; k < route.Count; k++)
                {
                    float d = Vector3.Angle(dir, grid.CellCenters[route[k]]);
                    if (d < bd) { bd = d; best = k; }
                }
                return best;
            }
            var spans = layout.MajorHazards.FindAll(h => h.Section == Section);
            bool InSpan(int k) => spans.Exists(h => k >= h.Start - 1 && k <= h.End + 1);
            var mineCells = new HashSet<int>();
            foreach (var (ms, mc) in layout.MineGroups) if (ms == Section) mineCells.Add(mc);

            // Cubes on the route, away from hazards.
            foreach (var c in layout.Cubes)
            {
                if (c.Section != Section || c.Hidden) continue;
                int k = route.IndexOf(c.Cell);
                if (k < 0 || InSpan(k)) continue;
                goals.Add(new Goal { kind = Kind.Cube, pos = c.Direction * r, order = k + 0.2f, label = $"cube@{k}" });
            }
            // A hidden cube down a side lane in each half, the closest to the route.
            var cp = layout.Checkpoints.Find(x => x.Section == Section && x.Main);
            for (int half = 0; half < 1; half++)
            {
                MazeLayout.Cube best = default;
                float bestD = float.MaxValue;
                foreach (var c in layout.Cubes)
                {
                    if (c.Section != Section || !c.Hidden) continue;
                    float k = RouteIndexOf(c.Direction);
                    if ((half == 0) != (k < cp.RouteIndex) || InSpan((int)k) || k < 5) continue;
                    float d = Vector3.Angle(c.Direction, grid.CellCenters[route[(int)k]]);
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (bestD < float.MaxValue)
                    goals.Add(new Goal { kind = Kind.Cube, pos = best.Direction * r, order = RouteIndexOf(best.Direction) + 0.4f, label = $"side-lane cube@{RouteIndexOf(best.Direction)}" });
            }
            foreach (var c in layout.Checkpoints)
                if (c.Section == Section) goals.Add(new Goal { kind = Kind.Checkpoint, pos = c.Direction * r, order = c.RouteIndex, label = $"checkpoint@{c.RouteIndex}" });
            foreach (var g in layout.LaserGates)
            {
                if (g.Section != Section) continue;
                int k = route.IndexOf(g.EntrySideCell);
                Vector3 padPos = grid.CellCenters[g.SwitchCell] * r;
                foreach (var sw in FindObjectsByType<LaserSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (Vector3.Angle(sw.transform.position, grid.CellCenters[g.SwitchCell]) < 2f) padPos = Ground(sw.transform.position);
                goals.Add(new Goal { kind = Kind.Pad, pos = padPos, order = k - 0.6f, label = $"gate pad (spur {g.SwitchInSpur})" });
                var e = grid.Edges[g.Edge];
                Vector3 mid = (e.P0 + e.P1).normalized;
                Vector3 toNear = Vector3.ProjectOnPlane(grid.CellCenters[g.EntrySideCell] - mid, mid).normalized;
                goals.Add(new Goal { kind = Kind.GateWait, pos = (mid * r + toNear * 5f).normalized * r, order = k - 0.3f, label = $"wait at gate@{k}" });
            }
            if (hasPool)
            {
                int k = pool.RouteIndex;
                goals.Add(new Goal { kind = Kind.BridgeIn, pos = nodes[FirstBridgeNode()], order = k - 0.5f, label = $"bridge@{k}" });
                goals.Add(new Goal { kind = Kind.BridgeOut, pos = nodes[LastBridgeNode()], order = k - 0.4f, label = "bridge end" });
            }
            var run = spans.Find(h => h.Type == MazeLayout.MajorHazard.CameraRun);
            if (spans.Exists(h => h.Type == MazeLayout.MajorHazard.CameraRun))
            {
                // One doorway at a time: wait in the middle of a cell (out of every spotlight's reach) for a gap,
                // then dash to the middle of the next.
                for (int k = run.Start; k < run.End; k++)
                {
                    goals.Add(new Goal { kind = Kind.CameraHold, pos = grid.CellCenters[route[k]] * r, order = k + 0.1f, label = $"hold in camera cell {k}" });
                    goals.Add(new Goal { kind = Kind.CameraRun, pos = grid.CellCenters[route[k + 1]] * r, order = k + 0.2f, label = $"dash to {k + 1}" });
                }
            }
            goals.Add(new Goal { kind = Kind.Hole, pos = layout.Holes[Section].Direction * r, order = 1000f, label = "hole" });
            goals.Sort((a, b) => a.order.CompareTo(b.order));
        }

        readonly Dictionary<Goal, CheckpointPad> pads = new Dictionary<Goal, CheckpointPad>();

        CheckpointPad PadFor(Goal g)
        {
            if (pads.TryGetValue(g, out var found)) return found;
            CheckpointPad best = null;
            foreach (var p in FindObjectsByType<CheckpointPad>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (best == null || Vector3.Distance(Ground(p.transform.position), g.pos) < Vector3.Distance(Ground(best.transform.position), g.pos)) best = p;
            pads[g] = best;
            return best;
        }

        int FirstBridgeNode() { int m = int.MaxValue; foreach (int i in bridgeNodes) m = Mathf.Min(m, i); return m; }
        int LastBridgeNode() { int m = -1; foreach (int i in bridgeNodes) m = Mathf.Max(m, i); return m; }

        float ReachRadius(Kind k) => k switch
        {
            Kind.Cube => 0.6f,
            Kind.Checkpoint => 1.0f,
            Kind.Pad => 0.8f,
            Kind.GateWait => 1.2f,
            Kind.BridgeIn => 0.9f,
            Kind.BridgeOut => 0.9f,
            Kind.CameraHold => 1.0f,
            Kind.CameraRun => 1.5f,
            _ => 0.5f,
        };

        // ---------------------------------------------------------------- driving

        void LateUpdate()
        {
            if (!ready || Done) return;
            t += Time.deltaTime;
            modeTime += Time.deltaTime;
            if (Mathf.FloorToInt(t / 3f) != Mathf.FloorToInt((t - Time.deltaTime) / 3f))
                Debug.Log($"[Video]   t={t:F0}s mode {mode}, goal {goals[Mathf.Min(goalIndex, goals.Count - 1)].label}, {Vector3.Distance(Ground(ball.transform.position), goals[Mathf.Min(goalIndex, goals.Count - 1)].pos):F1} away, speed {rb.linearVelocity.magnitude:F1}, layer {ball.CurrentLayer}, bombs {FindObjectsByType<MortarBomb>(FindObjectsSortMode.None).Length}");
            Vector2 input = Drive();
            moveField.SetValue(ball, input);
            KeepCameraForward();
            if (capture) Capture();
            if (frame > 30 * 300 || t > 300f) { Debug.LogWarning("[Video] timed out"); Finish(); }
        }

        Vector2 Drive()
        {
            Vector3 pos = ball.transform.position;
            Vector3 up = pos.normalized;

            if (ball.IsDestroyed)
            {
                if (!wasDestroyed)
                {
                    deaths++;
                    Debug.LogWarning($"[Video] DIED at {t:F1}s going for {goals[Mathf.Min(goalIndex, goals.Count - 1)].label}");
                    // Back to the first camera hold, so the spotlights get timed again.
                    var g = goals[Mathf.Min(goalIndex, goals.Count - 1)].kind;
                    if (g == Kind.CameraHold || g == Kind.CameraRun) goalIndex = goals.FindIndex(x => x.kind == Kind.CameraHold);
                    SetMode(Mode.Go);
                }
                wasDestroyed = true;
                path = null;
                return Vector2.zero;
            }
            wasDestroyed = false;

            // Past the hole: coast, then stop the video a little after landing.
            if (ball.CurrentLayer > Shell)
            {
                if (ball.IsGrounded) landedFor += Time.deltaTime;
                if (landedFor > 2.5f) Finish();
                return Vector2.zero;
            }

            if (mode == Mode.Intro)
            {
                if (modeTime < 1.5f) return Vector2.zero;
                SetMode(Mode.Go);
            }
            if (goalIndex >= goals.Count) return Vector2.zero;
            var goal = goals[goalIndex];
            float toGoal = Vector3.Distance(Ground(pos), goal.pos);

            // Waiting states.
            if (mode == Mode.WaitGate)
            {
                if (gate != null && gate.Active) return Brake();
                if (modeTime < 0.6f) return Brake();
                Advance();
                return Vector2.zero;
            }
            if (mode == Mode.WaitCamera)
            {
                if (Time.time < dashStart) return Brake();
                SetMode(Mode.Dash);
                dashStart = Time.time;
            }

            if (path == null && !Plan(goal.pos, goal.kind != Kind.BridgeIn)) return Vector2.zero;

            // Reached?
            bool directFinal = goal.kind == Kind.Cube || goal.kind == Kind.Pad || goal.kind == Kind.Hole || goal.kind == Kind.Checkpoint;
            bool padLit = goal.kind == Kind.Checkpoint && PadFor(goal) is CheckpointPad pad && pad.Reached;
            if ((toGoal < ReachRadius(goal.kind) || padLit) && goal.kind != Kind.Hole)
            {
                switch (goal.kind)
                {
                    case Kind.GateWait:
                        if (gate != null && gate.Active) { SetMode(Mode.WaitGate); holdPoint = goal.pos; Debug.Log($"[Video] {t:F1}s waiting at the gate for the cable"); return Brake(); }
                        break;
                    case Kind.CameraHold:
                        if (mode != Mode.WaitCamera) { PlanDash(); holdPoint = path != null ? path[0] : goal.pos; return Brake(); }
                        break;
                }
                Advance();
                goal = goals[Mathf.Min(goalIndex, goals.Count - 1)];
                if (path == null && !Plan(goal.pos, goal.kind != Kind.BridgeIn)) return Vector2.zero;
            }

            // Follow the path.
            int bestI = pathAt;
            float bestD = float.MaxValue;
            for (int i = pathAt; i < Mathf.Min(path.Count, pathAt + 25); i++)
            {
                float d = Vector3.Distance(Ground(pos), path[i]);
                if (d < bestD) { bestD = d; bestI = i; }
            }
            pathAt = bestI;
            bool onBridge = hasPool && Vector3.Angle(up, grid.CellCenters[pool.Cell]) * Mathf.Deg2Rad * r < s.cellSpacing * 0.62f && grid.NearestCell(up) == pool.Cell
                            || goal.kind == Kind.BridgeOut;
            float here = pathClear[pathAt];
            float look = onBridge ? 1.1f : Mathf.Clamp(here * 1.1f, 1.2f, 3f);
            float want = pathLen[pathAt] + look;
            int ti = pathAt;
            while (ti < path.Count - 1 && pathLen[ti] < want) ti++;
            Vector3 target = path[ti];
            if (directFinal && toGoal < (goal.kind == Kind.Hole ? 9f : 3.5f)) target = goal.pos;

            // A gentle weave where there's room.
            wiggle += Time.deltaTime * wiggleRate;
            if (Random.value < 0.004f) wiggleRate = Random.Range(0.9f, 1.6f);
            Vector3 along = Vector3.ProjectOnPlane(target - pos, up).normalized;
            Vector3 side = Vector3.Cross(up, along);
            float room = Mathf.Clamp01((here - 1.7f) / 2f);
            if (onBridge || mode == Mode.Dash || toGoal < 3f) room = 0f;
            target += side * (Mathf.Sin(wiggle) * 1.1f + Mathf.Sin(wiggle * 2.3f + 1f) * 0.35f) * room;
            Vector3 desired = Vector3.ProjectOnPlane(target - pos, up).normalized;

            // How fast.
            // Nearly flat out, easing to three quarters near the major hazards.
            float speed = NearMajorHazard(pos) || onBridge ? FullSpeed * 0.75f : FullSpeed;
            if (here < 1.6f) speed = Mathf.Min(speed, 0.7f);
            if (goal.kind == Kind.Cube && toGoal < 3f) speed = Mathf.Min(speed, 0.7f);
            if (goal.kind == Kind.GateWait || goal.kind == Kind.CameraHold) speed = Mathf.Min(speed, 0.2f + toGoal * 0.05f);
            if (mode == Mode.Dash) speed = DashSpeed(pos);

            // Mortar bombs: stop, swerve or run so the one coming down misses.
            Vector3 dodgeDir = desired;
            float dodgeSpeed = speed;
            // Right by the hole, just go: dropping through gets away from any bomb.
            bool committed = goal.kind == Kind.Hole && toGoal < 7f;
            if (!committed && Dodge(pos, desired, speed, ref dodgeDir, ref dodgeSpeed)) { desired = dodgeDir; speed = dodgeSpeed; }

            return ToInput(desired, speed, up);
        }

        Vector3 holdPoint;

        /// <summary>Stays put at the hold point: steers back toward it and against any drift.</summary>
        const float FullSpeed = 0.95f;

        Vector2 Brake()
        {
            Vector3 pos = ball.transform.position, up = pos.normalized;
            Vector3 v = Vector3.ProjectOnPlane(rb.linearVelocity, up);
            Vector3 push = Vector3.ProjectOnPlane(holdPoint - Ground(pos), up) * 0.35f - v * 0.3f;
            float mag = Mathf.Clamp01(push.magnitude);
            if (mag < 0.03f) return Vector2.zero;
            return ToInput(push.normalized, mag, up);
        }

        float nextTurn;

        void KeepCameraForward()
        {
            if (ball.IsDestroyed || ball.CurrentLayer > Shell) return;
            var orbit = cam.GetComponent<OrbitCamera>();
            Vector3 up = ball.transform.position.normalized;
            Vector3 camF = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
            if (mode == Mode.WaitCamera || mode == Mode.WaitGate)
            {
                // Look at what's coming: along the dash, or at the gate.
                Vector3 ahead = mode == Mode.WaitCamera && path != null ? path[Mathf.Min(path.Count - 1, 12)] - path[0] : Vector3.zero;
                if (mode == Mode.WaitGate && gateEdge >= 0) { var e = grid.Edges[gateEdge]; ahead = (e.P0 + e.P1).normalized * r - Ground(ball.transform.position); }
                ahead = Vector3.ProjectOnPlane(ahead, up);
                if (ahead.sqrMagnitude > 0.01f && Vector3.Angle(camF, ahead) > 25f && Time.time > nextTurn) { orbit.TurnTo(ahead, 0.8f); nextTurn = Time.time + 1f; }
                return;
            }
            Vector3 v = Vector3.ProjectOnPlane(rb.linearVelocity, up);
            if (v.magnitude > 2f && Vector3.Angle(camF, v) > 70f && Time.time > nextTurn)
            {
                Debug.Log($"[Video] {t:F1}s (frame {frame}) camera swung round {Vector3.Angle(camF, v):F0} degrees to follow the ball, going for {goals[Mathf.Min(goalIndex, goals.Count - 1)].label}");
                orbit.TurnTo(v, 0.7f);
                nextTurn = Time.time + 0.9f;
            }
        }

        Vector2 ToInput(Vector3 desired, float mag, Vector3 up)
        {
            Vector3 camForward = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
            Vector3 camRight = Vector3.Cross(up, camForward);
            return new Vector2(Vector3.Dot(desired, camRight), Vector3.Dot(desired, camForward)).normalized * mag;
        }

        void SetMode(Mode m) { mode = m; modeTime = 0f; if (ball != null) holdPoint = Ground(ball.transform.position); }

        void Advance()
        {
            Debug.Log($"[Video] {t:F1}s reached {goals[goalIndex].label}");
            goalIndex++;
            path = null;
            if (mode != Mode.Dash) SetMode(Mode.Go);
            else SetMode(Mode.Go);
        }

        bool NearMajorHazard(Vector3 pos)
        {
            Vector3 p = Ground(pos);
            foreach (var m in layout.Mortars) if (m.Section == Section && Vector3.Distance(p, m.Mount * r) < 14f) return true;
            if (gateEdge >= 0) { var e = grid.Edges[gateEdge]; if (Vector3.Distance(p, (e.P0 + e.P1).normalized * r) < 12f) return true; }
            if (hasPool && Vector3.Distance(p, grid.CellCenters[pool.Cell] * r) < 14f) return true;
            foreach (var c in runCameras) if (Vector3.Distance(p, c.center * r) < 10f) return true;
            return false;
        }

        // ---------------------------------------------------------------- camera run

        const float DashAccel = 7f, DashTop = 8.5f;
        List<Vector3> dashPath;
        List<float> dashLen;

        float PlanS(float tau) => tau < DashTop / DashAccel ? 0.5f * DashAccel * tau * tau : 0.5f * DashTop * DashTop / DashAccel + DashTop * (tau - DashTop / DashAccel);

        void PlanDash()
        {
            // Through this hop's doorway on the left, middle or right, whichever has the earliest gap in the
            // spotlights, starting from here.
            var runGoal = goals[goalIndex + 1];
            Vector3 from = Ground(ball.transform.position), to = runGoal.pos;
            Vector3 mid = (from + to).normalized;
            SecurityCamera door = runCameras[0];
            foreach (var c in runCameras) if (Vector3.Angle(c.center, mid) < Vector3.Angle(door.center, mid)) door = c;
            float reach = Mathf.Max(0f, door.sweepAcross - 0.3f);
            float now = Time.time;
            float bestWait = float.MaxValue;
            List<Vector3> bestPath = null;
            List<float> bestLen = null;
            foreach (float f in new[] { 0f, -0.5f, 0.5f, -1f, 1f })
            {
                Vector3 through = (door.center * r + door.across * (reach * f)).normalized;
                var pts = new List<Vector3>();
                var len = new List<float>();
                foreach (var (a0, a1) in new[] { (from.normalized, through), (through, to.normalized) })
                {
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(a0, a1) * Mathf.Deg2Rad * r / 0.5f));
                    for (int i = 0; i < n; i++) pts.Add(Vector3.Slerp(a0, a1, i / (float)n).normalized * r);
                }
                pts.Add(to);
                len.Add(0f);
                for (int i = 1; i < pts.Count; i++) len.Add(len[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));
                float total = len[len.Count - 1];
                for (float wait = 1.6f; wait < Mathf.Min(bestWait, 25f); wait += 0.1f)
                {
                    bool ok = true;
                    for (float tau = 0f; ok; tau += 0.05f)
                    {
                        float sAlong = PlanS(tau);
                        if (sAlong > total) break;
                        Vector3 bp = PointAt(pts, len, sAlong);
                        foreach (var c in runCameras)
                        {
                            Vector3 spot = (Vector3)sweepPoint.Invoke(c, new object[] { now + wait + tau });
                            if (Vector3.Angle(bp, spot) * Mathf.Deg2Rad * r < c.spotRadius + 0.7f) { ok = false; break; }
                        }
                    }
                    if (ok) { bestWait = wait; bestPath = pts; bestLen = len; break; }
                }
            }
            if (bestPath == null)
            {
                Debug.LogWarning("[Video] no gap found in the spotlights");
                if (!Plan(to)) return;
                bestWait = 1f;
            }
            else
            {
                path = bestPath;
                pathLen = bestLen;
                pathClear = bestPath.ConvertAll(_ => 3f);
                pathAt = 0;
            }
            dashStart = now + bestWait;
            Debug.Log($"[Video] {t:F1}s holding {bestWait:F1}s for a gap in the spotlights");
            goalIndex++; // on to the dash itself
            SetMode(Mode.WaitCamera);
        }

        float DashSpeed(Vector3 pos)
        {
            float tau = Time.time - dashStart;
            float sPlan = PlanS(tau);
            float sNow = pathLen[pathAt];
            float vPlan = Mathf.Min(DashTop, DashAccel * tau);
            float want = vPlan + 3f * (sPlan - sNow);
            return Mathf.Clamp(want / s_ballTop, 0f, 1f);
        }

        float s_ballTop => ball.maxGroundSpeed;

        static Vector3 PointAt(List<Vector3> p, List<float> len, float sAlong)
        {
            for (int i = 1; i < p.Count; i++)
                if (len[i] >= sAlong) return Vector3.Lerp(p[i - 1], p[i], Mathf.InverseLerp(len[i - 1], len[i], sAlong));
            return p[p.Count - 1];
        }

        // ---------------------------------------------------------------- mortar

        bool Dodge(Vector3 pos, Vector3 desired, float speed, ref Vector3 dir, ref float mag)
        {
            var bombs = FindObjectsByType<MortarBomb>(FindObjectsSortMode.None);
            if (bombs.Length == 0) return false;
            float blast = s.mortarBlastRadius, flight = s.mortarFlightTime;
            Vector3 up = pos.normalized;
            Vector3 v = Vector3.ProjectOnPlane(rb.linearVelocity, up);
            // Candidate moves: as planned, stop, back off, or veer either way.
            var options = new List<(Vector3 d, float m)>
            {
                (desired, Mathf.Max(speed, 0.9f)), (desired, 0f),
                (Quaternion.AngleAxis(50f, up) * desired, 0.9f), (Quaternion.AngleAxis(-50f, up) * desired, 0.9f),
            };
            float worstNow = float.MaxValue;
            foreach (var b in bombs)
            {
                Vector3 to = (Vector3)bombTo.GetValue(b);
                worstNow = Mathf.Min(worstNow, Vector3.Distance(Ground(pos), Ground(to)));
            }
            // Of the moves that clear every bomb, the one that makes the most headway; failing that, the one
            // that ends up furthest from them.
            int bestOpt = 0;
            float bestScore = float.MinValue;
            const float safe = 1.3f;
            for (int o = 0; o < options.Count; o++)
            {
                float miss = float.MaxValue;
                foreach (var b in bombs)
                {
                    Vector3 to = Ground((Vector3)bombTo.GetValue(b));
                    float left = Mathf.Max(0f, flight - (float)bombAge.GetValue(b));
                    Vector3 p = Simulate(pos, v, options[o].d * options[o].m * ball.maxGroundSpeed, left);
                    miss = Mathf.Min(miss, Vector3.Distance(Ground(p), to));
                }
                float score = miss > blast + safe ? 100f + Vector3.Dot(options[o].d, desired) * options[o].m : miss;
                if (score > bestScore) { bestScore = score; bestOpt = o; }
            }
            // Only step in if the planned move would get caught.
            float planned = float.MaxValue;
            foreach (var b in bombs)
            {
                Vector3 to = Ground((Vector3)bombTo.GetValue(b));
                float left = Mathf.Max(0f, flight - (float)bombAge.GetValue(b));
                planned = Mathf.Min(planned, Vector3.Distance(Ground(Simulate(pos, v, desired * speed * ball.maxGroundSpeed, left)), to));
            }
            if (planned > blast + safe) return false;
            dir = options[bestOpt].d;
            mag = options[bestOpt].m;
            return true;
        }

        Vector3 Simulate(Vector3 pos, Vector3 v, Vector3 targetV, float time)
        {
            float k = ball.groundResponse;
            Vector3 p = pos;
            for (float tt = 0f; tt < time; tt += 0.05f)
            {
                v = Vector3.Lerp(v, targetV, 1f - Mathf.Exp(-k * 0.05f));
                p += v * 0.05f;
            }
            return p;
        }

        // ---------------------------------------------------------------- capture

        void Capture()
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, $"frame_{frame:D5}.jpg"), tex.EncodeToJPG(92));
            cam.targetTexture = null;
            RenderTexture.active = null;
            frame++;
        }

        void Finish()
        {
            if (Done) return;
            int cubesLeft = 0, cubesAll = 0;
            foreach (var c in FindObjectsByType<EnergyCube>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                cubesAll++;
                if (!c.gameObject.activeSelf) cubesTaken++;
            }
            foreach (var m in layout.Mortars)
                if (m.Shell == Shell && Vector3.Distance(m.Mount * r, layout.Holes[Section].Direction * r) < s.mortarRange + 8f)
                    Debug.Log($"[Video] the mortar in section {m.Section + 1} is {Vector3.Distance(m.Mount * r, layout.Holes[Section].Direction * r):F1} from section 1's hole");
            Debug.Log($"[Video] finished at {t:F1}s: {goalIndex}/{goals.Count} goals, {deaths} deaths, {cubesTaken} cubes collected, {frame} frames, landed on layer {ball.CurrentLayer}");
            Done = true;
            PlayProbe.Done = true;
        }
    }
}
