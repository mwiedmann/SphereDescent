using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SphereDescent.EditorTools
{
    /// <summary>Creates the materials and the main scene. Safe to re-run; it overwrites the generated assets.</summary>
    public static class SphereDescentSetup
    {
        const string MaterialDir = "Assets/Materials";
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Sphere Descent/Rebuild Main Scene")]
        public static void BuildMainScene()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory("Assets/Scenes");

            var mats = new LevelMaterials
            {
                ground = SaveMaterial("Ground", LevelMaterials.MakeGrid(new Color(0.75f, 0.85f, 0.9f), new Color(0.5f, 0.6f, 0.65f), 2f, false)),
                wall = SaveMaterial("Wall", LevelMaterials.MakeGrid(new Color(0.85f, 0.6f, 0.45f), new Color(0.6f, 0.4f, 0.3f), 1f, false)),
                ball = SaveMaterial("Ball", LevelMaterials.MakeBall()),
                pad = SaveMaterial("BoostPad", LevelMaterials.MakeEmissive(new Color(0.2f, 0.9f, 1f), 2f)),
                ringUp = SaveMaterial("HoleRingTwoWay", LevelMaterials.MakeEmissive(new Color(0.2f, 0.9f, 1f), 1.5f)),
                ringDrop = SaveMaterial("HoleRingDrop", LevelMaterials.MakeEmissive(new Color(1f, 0.45f, 0.05f), 1.8f)),
                exit = SaveMaterial("Exit", LevelMaterials.MakeEmissive(new Color(1f, 0.85f, 0.2f), 3f)),
                beam = SaveMaterial("LightBeam", LevelMaterials.MakeBeam()),
            };

            var sky = SaveMaterial("Sky", new Material(Shader.Find("SphereDescent/SynthwaveSky")));
            var postProfile = SavePostProcessing();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Light sun = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out Light l)) sun = l;
                if (root.TryGetComponent(out Camera cam))
                {
                    cam.gameObject.AddComponent<OrbitCamera>();
                    cam.nearClipPlane = 0.1f;
                    cam.farClipPlane = 1000f;
                    cam.clearFlags = CameraClearFlags.Skybox;
                    cam.backgroundColor = new Color(0.08f, 0.02f, 0.16f);
                    cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                }
            }
            if (sun != null)
            {
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1f;
                sun.color = new Color(1f, 0.85f, 0.95f);
                RenderSettings.sun = sun;
            }

            // Synthwave night: purple ambient light, sky from the skybox, neon glow from bloom.
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.24f, 0.42f);
            var volume = new GameObject("Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = postProfile;
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = 60f;
                EditorUtility.SetDirty(urp);
            }

            var gm = new GameObject("Game Manager").AddComponent<GameManager>();
            gm.materials = mats;
            gm.sun = sun;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Sphere Descent: main scene created at " + ScenePath);
        }

        /// <summary>Bloom makes the neon lines and glowing objects glow; a light vignette frames the view.</summary>
        static VolumeProfile SavePostProcessing()
        {
            const string path = "Assets/Settings/PostProcessing.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.2f);
            bloom.scatter.Override(0.65f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.28f);
            vignette.color.Override(new Color(0.1f, 0f, 0.15f));
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static Material SaveMaterial(string name, Material m)
        {
            string path = $"{MaterialDir}/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        /// <summary>Generates several worlds in edit mode and logs layout/mesh stats. Used from batch mode to sanity-check generation.</summary>
        [MenuItem("Sphere Descent/Validate World Generation")]
        public static void ValidateGeneration()
        {
            var settings = new LevelSettings();
            var mats = new LevelMaterials();
            mats.EnsureDefaults();
            int failures = 0;
            var allRoutes = new System.Collections.Generic.List<int>();
            for (int world = 1; world <= 8; world++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var layout = MazeLayout.Generate(settings, 1337 + world * 7919);
                long genMs = sw.ElapsedMilliseconds;
                if (layout == null) { failures++; Debug.Log($"[Validate] World {world}: FAILED to generate"); continue; }

                var problems = new System.Text.StringBuilder();
                if (layout.Sections.Count != settings.ShellCount + 2 * settings.climbsPerWorld)
                    problems.Append($" expected {settings.ShellCount + 2 * settings.climbsPerWorld} sections;");

                // Sections are chained: each starts where the previous one's hole or pad leads.
                for (int i = 0; i < layout.Sections.Count; i++)
                {
                    var sec = layout.Sections[i];
                    var h = layout.Holes[i];
                    if (sec.RouteCells < 0) problems.Append($" section {i + 1} exit unreachable;");
                    bool last = i == layout.Sections.Count - 1;
                    if (last && (h.LandingCell != -1 || sec.Shell != layout.ShellCount - 1)) problems.Append(" last section doesn't drop into the core;");
                    if (!last)
                    {
                        var next = layout.Sections[i + 1];
                        bool linked = sec.ExitsUp
                            ? h.TwoWay && h.Shell == next.Shell && h.Cell == next.EntryCell && h.LandingCell == sec.ExitCell
                            : !h.TwoWay && h.Shell == sec.Shell && h.Cell == sec.ExitCell && h.LandingCell == next.EntryCell;
                        if (!linked || Mathf.Abs(next.Shell - sec.Shell) != 1) problems.Append($" section {i + 1} not linked to {i + 2};");
                    }
                }

                // No dead ends inside sections, other than possibly the entry and exit cells.
                int deadEnds = 0;
                for (int sh = 0; sh < layout.ShellCount; sh++)
                    for (int c = 0; c < layout.Grids[sh].CellCount; c++)
                    {
                        int r = layout.Region[sh][c];
                        if (r < 0) continue;
                        var sec = layout.Sections[r];
                        if (c != sec.EntryCell && c != sec.ExitCell && layout.Openings(sh, c) < 2) deadEnds++;
                    }
                if (deadEnds > 0) problems.Append($" {deadEnds} dead ends;");

                // Mines: clear of walls, holes and pads, far enough apart to roll between, 2-3 groups a section.
                float minGap = float.MaxValue, minWall = float.MaxValue, minHole = float.MaxValue, maxWallMineGap = 0f;
                int wallMines = 0;
                var perSection = new int[layout.Sections.Count];
                for (int m = 0; m < layout.Mines.Count; m++)
                {
                    var mine = layout.Mines[m];
                    float r = settings.shellRadii[mine.Shell];
                    Vector3 p = mine.Direction * r;
                    perSection[mine.Section]++;
                    for (int n = m + 1; n < layout.Mines.Count; n++)
                        if (layout.Mines[n].Shell == mine.Shell)
                            minGap = Mathf.Min(minGap, Vector3.Distance(p, layout.Mines[n].Direction * r) - 2f * settings.mineRadius);
                    var grid = layout.Grids[mine.Shell];
                    float wallGap = float.MaxValue;
                    for (int e = 0; e < grid.Edges.Length; e++)
                    {
                        if (layout.Open[mine.Shell][e]) continue;
                        Vector3 a0 = grid.Edges[e].P0 * r, a1 = grid.Edges[e].P1 * r;
                        Vector3 closest = a0 + Vector3.Project(p - a0, a1 - a0);
                        if (Vector3.Dot(closest - a0, a1 - a0) < 0f) closest = a0;
                        else if ((closest - a0).sqrMagnitude > (a1 - a0).sqrMagnitude) closest = a1;
                        wallGap = Mathf.Min(wallGap, Vector3.Distance(p, closest) - settings.wallThickness * 0.5f - settings.mineRadius);
                    }
                    if (mine.NearWall) { wallMines++; maxWallMineGap = Mathf.Max(maxWallMineGap, wallGap); }
                    else minWall = Mathf.Min(minWall, wallGap);
                    if (wallGap < 0.05f) problems.Append($" a mine touches a wall ({wallGap:F2});");
                    foreach (var h in layout.Holes)
                        if (h.Shell == mine.Shell || h.Shell + 1 == mine.Shell)
                            minHole = Mathf.Min(minHole, Vector3.Angle(h.Direction, mine.Direction) * Mathf.Deg2Rad * r
                                - settings.HoleRadius(layout.Grids[h.Shell], h.Shell) - settings.mineRadius);
                }
                if (minGap < MazeLayout.MinePassage - 0.05f) problems.Append($" mines only {minGap:F2} apart;");
                if (maxWallMineGap >= 1f) problems.Append($" a wall mine leaves {maxWallMineGap:F2} to squeeze past;");
                if (minWall < 0.5f) problems.Append($" a mine {minWall:F2} from a wall;");
                if (minHole < 2f) problems.Append($" a mine {minHole:F2} from a hole or pad;");
                var mineCounts = new System.Text.StringBuilder();
                foreach (int c in perSection) mineCounts.Append(' ').Append(c);

                var routes = new System.Text.StringBuilder();
                foreach (var sec in layout.Sections)
                {
                    routes.Append($" {sec.Shell}{(sec.ExitsUp ? "^" : "v")}{sec.RouteCells}/{sec.CellCount}");
                    allRoutes.Add(sec.RouteCells);
                }
                if (problems.Length > 0) failures++;
                string msg = $"[Validate] World {world}: seed {layout.Seed}, attempts {layout.AttemptsUsed}, {layout.Sections.Count} sections, " +
                             $"{layout.Climbs} climbs, layout {genMs} ms. shell/dir/route/cells:{routes}" +
                             $" mines per section:{mineCounts} ({wallMines} by walls; gap {minGap:F1}, other mines to wall {minWall:F1}, wall mines to wall max {maxWallMineGap:F2}, hole {minHole:F1})" +
                             (problems.Length > 0 ? " PROBLEMS:" + problems : "");

                if (world <= 2)
                {
                    sw.Restart();
                    var level = LevelBuilder.Build(layout, settings, mats, world);
                    int tris = 0, verts = 0;
                    foreach (var mf in level.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mf.sharedMesh == null) continue;
                        tris += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
                        verts += mf.sharedMesh.vertexCount;
                    }
                    int pads = level.GetComponentsInChildren<BoostPad>().Length;
                    msg += $", build {sw.ElapsedMilliseconds} ms, {verts} verts, {tris} tris, {pads} pads";
                    Object.DestroyImmediate(level.gameObject);
                }
                Debug.Log(msg);
            }
            allRoutes.Sort();
            if (allRoutes.Count > 0)
                Debug.Log($"[Validate] shortest route per section (cells): min {allRoutes[0]}, median {allRoutes[allRoutes.Count / 2]}, " +
                          $"max {allRoutes[allRoutes.Count - 1]}; about {settings.cellSpacing:F1} units per cell");
            Debug.Log(failures == 0 ? "[Validate] OK" : $"[Validate] {failures} worlds have problems");
        }

        /// <summary>
        /// Headless physics check of the real BallController: drops onto solid ground, through a hole, and a
        /// boost-pad launch back up. Steps physics manually so it can run in batch mode.
        /// </summary>
        public static void PhysicsSmokeTest()
        {
            var settings = new LevelSettings();
            var mats = new LevelMaterials();
            mats.EnsureDefaults();
            var layout = MazeLayout.Generate(settings, 1337 + 7919);
            var level = LevelBuilder.Build(layout, settings, mats, 1);
            var grid = layout.Grids[0];
            var prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Physics.SyncTransforms();

            float r0 = settings.shellRadii[0];
            Report("solid ground", Simulate(level, grid.CellCenters[layout.StartCell] * (r0 + 3f), 3f, null), r0 + 0.5f);

            // Every link between sections: drop through each hole, launch from each boost pad.
            var pads = level.GetComponentsInChildren<BoostPad>();
            for (int i = 0; i < layout.Holes.Count; i++)
            {
                var h = layout.Holes[i];
                float above = settings.shellRadii[h.Shell], below = settings.SurfaceRadius(h.Shell + 1);
                Report($"section {i + 1} drop (shell {h.Shell}{(h.LandingCell < 0 ? " into core" : "")})",
                    Simulate(level, h.Direction * (above + 3f), 4f, null), below + 0.5f);
                // Rolling into the hole fast must still land right under it, not carry on over a wall.
                Vector3 across = Vector3.Cross(h.Direction, Vector3.up).normalized;
                Vector3 fast = Simulate(level, h.Direction * (above - 0.2f), 4f, null, across * 8f - h.Direction * 5f);
                float off = Vector3.Angle(fast, h.Direction) * Mathf.Deg2Rad * below;
                Report($"section {i + 1} fast drop ({off:F1} units from under the hole, hole radius " +
                       $"{settings.HoleRadius(layout.Grids[h.Shell], h.Shell):F1})", off < settings.HoleRadius(layout.Grids[h.Shell], h.Shell) + 1f ? fast : Vector3.zero, below + 0.5f);
                if (!h.TwoWay) continue;
                BoostPad pad = null;
                foreach (var p in pads)
                    if (Vector3.Dot(p.Axis, h.Direction) > 0.9999f) pad = p;
                Vector3 end = Simulate(level, pad.transform.position + pad.Axis * 0.5f, 5f, pad);
                float fromHole = Vector3.Angle(end, pad.Axis) * Mathf.Deg2Rad * above;
                Report($"section {i + 1} launch to shell {h.Shell} ({fromHole:F1} units from hole center, hole radius " +
                       $"{settings.HoleRadius(layout.Grids[h.Shell], h.Shell):F1})", end, above + 0.5f, settings.HillHeight(h.Shell));
            }

            // Control tests on open ground, after the level is gone so its walls can't get in the way.
            Object.DestroyImmediate(level.gameObject);
            ResponsivenessTest(r0);
            Physics.simulationMode = prevMode;
        }

        static Vector3 Simulate(LevelInstance level, Vector3 start, float seconds, BoostPad launchFrom, Vector3 velocity = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.layer = 2;
            go.AddComponent<Rigidbody>();
            var ball = go.AddComponent<BallController>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(BallController).GetMethod("Awake", flags).Invoke(ball, null);
            var fixedUpdate = typeof(BallController).GetMethod("FixedUpdate", flags);
            ball.level = level;
            ball.Teleport(start);
            go.GetComponent<Rigidbody>().linearVelocity = velocity;
            Physics.SyncTransforms();
            float dt = Time.fixedDeltaTime;
            if (launchFrom != null)
            {
                // Settle onto the pad, then launch.
                for (int i = 0; i < 25; i++) { fixedUpdate.Invoke(ball, null); Physics.Simulate(dt); }
                typeof(BallController).GetMethod("StartLaunch", flags)
                    .Invoke(ball, new object[] { launchFrom, go.transform.position.normalized, go.transform.position.magnitude });
            }
            var rb = go.GetComponent<Rigidbody>();
            float peak = 0f;
            bool touchedDown = false;
            for (float t = 0; t < seconds; t += dt)
            {
                fixedUpdate.Invoke(ball, null);
                Physics.Simulate(dt);
                float r = rb.position.magnitude;
                peak = Mathf.Max(peak, r);
                if (launchFrom != null && !touchedDown && peak > launchFrom.TargetSurfaceRadius + 1.5f && r < launchFrom.TargetSurfaceRadius + 0.6f)
                {
                    touchedDown = true;
                    float d = Vector3.Angle(rb.position, launchFrom.Axis) * Mathf.Deg2Rad * launchFrom.TargetSurfaceRadius;
                    Debug.Log($"[Physics] launch touchdown {d:F2} units from hole center (aimed {launchFrom.LandAngle * launchFrom.TargetSurfaceRadius:F2}), grounded={ball.IsGrounded}");
                }
            }
            Vector3 end = rb.position;
            Object.DestroyImmediate(go);
            return end;
        }

        /// <summary>
        /// Times how quickly the ball answers the controls on open ground (a plain sphere the size of the
        /// surface, no walls): reaching speed from rest, turning 90 degrees, and reversing.
        /// </summary>
        static void ResponsivenessTest(float radius)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ground.transform.localScale = Vector3.one * radius * 2f;
            var camera = new GameObject("Test Camera").transform;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var fixedUpdate = typeof(BallController).GetMethod("FixedUpdate", flags);
            var input = typeof(BallController).GetField("moveInput", flags);
            // Collision callbacks don't run in edit mode, so report the floor contact the ball really has.
            var touching = typeof(BallController).GetField("touchingGround", flags);
            float dt = Time.fixedDeltaTime;

            // Runs a fresh ball: optionally gets it up to full speed heading forward, then holds the given input
            // until done() says so (or a time limit). Returns how long that took.
            float Measure(Vector2 move, bool startAtSpeed, System.Func<Vector3, Vector3, Vector3, bool> done, out float speed)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.layer = 2;
                var rb = go.AddComponent<Rigidbody>();
                var ball = go.AddComponent<BallController>();
                typeof(BallController).GetMethod("Awake", flags).Invoke(ball, null);
                ball.cameraTransform = camera;
                ball.Teleport(Vector3.up * (radius + 0.5f));
                Physics.SyncTransforms();
                Vector3 forward = Vector3.forward;
                void Step(Vector2 m)
                {
                    Vector3 up = rb.position.normalized;
                    forward = Vector3.ProjectOnPlane(forward, up).normalized;
                    camera.rotation = Quaternion.LookRotation(forward, up);
                    input.SetValue(ball, m);
                    touching.SetValue(ball, true);
                    fixedUpdate.Invoke(ball, null);
                    Physics.Simulate(dt);
                }
                if (startAtSpeed) for (int i = 0; i < 200; i++) Step(Vector2.up);
                float t = 0f;
                speed = 0f;
                for (; t < 5f; t += dt)
                {
                    Step(move);
                    Vector3 up = rb.position.normalized;
                    Vector3 v = Vector3.ProjectOnPlane(rb.linearVelocity, up);
                    speed = v.magnitude;
                    if (done(v, forward, Vector3.Cross(up, forward))) break;
                }
                Object.DestroyImmediate(go);
                return t;
            }

            float max = 11f;
            float start = Measure(Vector2.up, false, (v, f, r) => Vector3.Dot(v, f) >= 0.9f * max, out _);
            float turn = Measure(Vector2.right, true, (v, f, r) => v.magnitude > 1f && Vector3.Angle(v, r) < 15f, out float turnSpeed);
            float reverse = Measure(Vector2.down, true, (v, f, r) => Vector3.Dot(v, -f) >= 5f, out _);
            bool ok = start < 1f && turn < 0.6f && reverse < 0.8f;
            Debug.Log($"[Physics] {(ok ? "PASS" : "FAIL")} responsiveness: 0 to 90% speed {start:F2} s, 90-degree turn {turn:F2} s " +
                      $"(speed after {turnSpeed:F1}), reverse to 5 units/s backward {reverse:F2} s");

            BackingUpTest(radius, true);
            BackingUpTest(radius, false);

            Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(ground);
        }

        /// <summary>
        /// Rolls forward with the real follow camera, then holds back, then lets go. With holdWhenBackingUp the
        /// camera should keep its heading and the ball should drive steadily backward.
        /// </summary>
        static void BackingUpTest(float radius, bool hold)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.layer = 2;
            var rb = go.AddComponent<Rigidbody>();
            var ball = go.AddComponent<BallController>();
            typeof(BallController).GetMethod("Awake", flags).Invoke(ball, null);
            var camGo = new GameObject("Test Orbit Camera");
            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.holdWhenBackingUp = hold;
            orbit.target = go.transform;
            ball.cameraTransform = camGo.transform;
            ball.Teleport(Vector3.up * (radius + 0.5f));
            camGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            orbit.Snap();
            Physics.SyncTransforms();
            var fixedUpdate = typeof(BallController).GetMethod("FixedUpdate", flags);
            var input = typeof(BallController).GetField("moveInput", flags);
            var touching = typeof(BallController).GetField("touchingGround", flags);
            var follow = typeof(OrbitCamera).GetMethod("FollowMovement", flags);
            var upField = typeof(OrbitCamera).GetField("up", flags);
            var fwdField = typeof(OrbitCamera).GetField("forward", flags);
            float dt = Time.fixedDeltaTime;

            void Step(Vector2 m)
            {
                // What the camera's LateUpdate does each frame, minus smoothing: carry the heading along the
                // surface, follow the ball's movement, and face that way.
                Vector3 up = rb.position.normalized;
                Vector3 oldUp = (Vector3)upField.GetValue(orbit);
                Vector3 f = Vector3.ProjectOnPlane(Quaternion.FromToRotation(oldUp, up) * orbit.Forward, up).normalized;
                upField.SetValue(orbit, up);
                fwdField.SetValue(orbit, f);
                follow.Invoke(orbit, new object[] { dt });
                camGo.transform.rotation = Quaternion.LookRotation(orbit.Forward, up);
                input.SetValue(ball, m);
                touching.SetValue(ball, true);
                fixedUpdate.Invoke(ball, null);
                Physics.Simulate(dt);
            }

            for (float t = 0; t < 1.5f; t += dt) Step(Vector2.up);
            Vector3 headingBefore = orbit.Forward;
            float minBackSpeed = float.MaxValue, maxSwing = 0f;
            for (float t = 0; t < 2.5f; t += dt)
            {
                Step(Vector2.down);
                Vector3 up = rb.position.normalized;
                // Compare headings after carrying the old one to the ball's current spot on the sphere.
                Vector3 carried = Vector3.ProjectOnPlane(headingBefore, up).normalized;
                maxSwing = Mathf.Max(maxSwing, Vector3.Angle(carried, orbit.Forward));
                if (t > 1f) minBackSpeed = Mathf.Min(minBackSpeed, Vector3.Dot(rb.linearVelocity, -orbit.Forward));
            }
            for (float t = 0; t < 1f; t += dt)
            {
                Step(Vector2.zero);
                Vector3 carried = Vector3.ProjectOnPlane(headingBefore, rb.position.normalized).normalized;
                maxSwing = Mathf.Max(maxSwing, Vector3.Angle(carried, orbit.Forward));
            }
            string label = hold ? "backing up holds the camera" : "(for comparison, without the hold)";
            bool ok = maxSwing < 10f && minBackSpeed > 8f;
            Debug.Log($"[Physics] {(hold ? (ok ? "PASS " : "FAIL ") : "")}{label}: camera swung at most {maxSwing:F0} deg, " +
                      $"backward speed while holding back at least {minBackSpeed:F1}");
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(camGo);
        }

        /// <param name="hills">How far above the expected radius still counts, for a ball that may roll onto a hill.</param>
        static void Report(string name, Vector3 end, float expectedRadius, float hills = 0f)
        {
            float r = end.magnitude;
            bool ok = r > expectedRadius - 0.3f && r < expectedRadius + hills + 0.3f;
            string expected = hills > 0f ? $"{expectedRadius:F2} to {expectedRadius + hills:F2}" : $"{expectedRadius:F2}";
            Debug.Log($"[Physics] {(ok ? "PASS" : "FAIL")} {name}: ended at radius {r:F2}, expected {expected}");
        }
    
        /// <summary>Renders a few preview shots of world 1 to the given folder (batch mode, needs graphics).</summary>
        public static void RenderPreviews()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PREVIEW_DIR") ?? "Previews";
            Directory.CreateDirectory(outDir);
            BuildMainScene();
            var gm = Object.FindAnyObjectByType<GameManager>();
            var layout = MazeLayout.Generate(gm.settings, gm.baseSeed + 1 * 7919);
            var level = LevelBuilder.Build(layout, gm.settings, gm.materials, 1);
            var cam = Camera.main;
            var sun = gm.sun;
            var grid = layout.Grids[0];

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.GetComponent<MeshRenderer>().sharedMaterial = gm.materials.ball;

            void Shot(string name, Vector3 ballPos, Vector3 camPos)
            {
                level.SetShadowLayer(level.LayerAt(ballPos.magnitude));
                level.SetVisibleLayers(Mathf.Max(level.LayerAt(ballPos.magnitude), level.LayerAt(camPos.magnitude)));
                ball.transform.position = ballPos;
                Vector3 up = ballPos.normalized;
                cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(ballPos + up * 0.6f - camPos, up));
                GameManager.UpdateSky(sun, up);
                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }

            Vector3 Tangent(Vector3 up) => Vector3.ProjectOnPlane(new Vector3(0.3f, 0.1f, 1f), up).normalized;

            float r0 = gm.settings.shellRadii[0];
            Vector3 s = grid.CellCenters[layout.StartCell];
            Shot("1_overview", s * (r0 + 0.5f), s * (r0 * 2.6f) + Tangent(s) * 60f);
            Shot("2_surface", s * (r0 + 0.5f), s * (r0 + 4f) - Tangent(s) * 8f);

            // Section 1 from high above, to see its main path and side lanes.
            Shot("2b_section_topdown", s * (r0 + 0.5f), s * (r0 + 70f) + Tangent(s) * 2f);

            bool shotDrop = false, shotTwoWay = false;
            foreach (var h in layout.Holes)
            {
                if (h.LandingCell < 0 || (h.TwoWay ? shotTwoWay : shotDrop)) continue;
                float ra = gm.settings.shellRadii[h.Shell], rb = gm.settings.shellRadii[h.Shell + 1];
                Vector3 d = h.Direction;
                Vector3 t = Tangent(d);
                Vector3 nearHole = (d + t * (5f / ra)).normalized;
                Shot(h.TwoWay ? "3_hole_twoway" : "3_hole_drop", nearHole * (ra + 0.5f), nearHole * (ra + 9f) + t * 6f);
                if (h.TwoWay)
                {
                    Vector3 nearPad = (d + t * (4f / rb)).normalized;
                    Shot("4_inner_pad", nearPad * (rb + 0.5f), nearPad * (rb + 3.5f) + t * 8f);
                    shotTwoWay = true;
                }
                else shotDrop = true;
            }
            Vector3 e = layout.Holes[layout.Holes.Count - 1].Direction;
            Vector3 nearExit = (e + Tangent(e) * (5f / gm.settings.coreRadius)).normalized;
            Shot("5_core_exit", nearExit * (gm.settings.coreRadius + 0.5f), nearExit * (gm.settings.coreRadius + 4f) + Tangent(e) * 7f);

            Object.DestroyImmediate(ball);
            Object.DestroyImmediate(level.gameObject);
            Debug.Log("[Preview] done -> " + Path.GetFullPath(outDir));
        }

        /// <summary>
        /// Renders the README screenshots of world 1 from the main scene (without modifying it) into
        /// Docs/screenshots. Batch mode, needs graphics.
        /// </summary>
        [MenuItem("Sphere Descent/Render README Screenshots")]
        public static void RenderReadmeScreenshots()
        {
            const string outDir = "Docs/screenshots";
            const int width = 1600, height = 900;
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(ScenePath);
            var gm = Object.FindAnyObjectByType<GameManager>();
            gm.materials.EnsureDefaults();
            var settings = gm.settings;
            var layout = MazeLayout.Generate(settings, gm.baseSeed + 1 * 7919);
            var level = LevelBuilder.Build(layout, settings, gm.materials, 1);
            var cam = Camera.main;
            var sun = gm.sun;
            var orbit = cam.GetComponent<OrbitCamera>();

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.GetComponent<MeshRenderer>().sharedMaterial = gm.materials.ball;
            ball.transform.rotation = Quaternion.Euler(30f, 40f, 10f);
            ball.layer = 2; // ignored by the camera's collision probe, as in game
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            Physics.SyncTransforms();

            void Render(string name, Vector3 ballPos, Vector3 camPos, Quaternion camRot, int focusLayer)
            {
                level.SetShadowLayer(level.LayerAt(ballPos.magnitude));
                level.SetVisibleLayers(focusLayer);
                ball.transform.position = ballPos;
                GameManager.UpdateSky(sun, ballPos.normalized);
                cam.transform.SetPositionAndRotation(camPos, camRot);
                var rt = new RenderTexture(width, height, 24) { antiAliasing = 8 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }

            // Height of the ground (hills included) on a shell in a direction: the highest point of the shell's
            // own surface below the shell above.
            float Ground(int shell, Vector3 dir)
            {
                dir = dir.normalized;
                float top = shell == 0 ? settings.shellRadii[0] + 50f : settings.shellRadii[shell - 1] - settings.shellThickness - 0.05f;
                float best = settings.SurfaceRadius(shell);
                foreach (var hit in Physics.RaycastAll(dir * top, -dir, top - settings.SurfaceRadius(shell) + 0.5f))
                    if (hit.collider.name == "Shell") best = Mathf.Max(best, hit.point.magnitude);
                return best;
            }

            // The in-game camera: behind and above the ball, looking along forward, pulled in like the real
            // camera when a wall is in the way.
            void GameView(string name, int shell, Vector3 ballDir, Vector3 lookToward, float pitch)
            {
                Vector3 up = ballDir.normalized;
                float r = Ground(shell, up);
                Vector3 ballPos = up * (r + 0.5f);
                Vector3 fwd = Vector3.ProjectOnPlane(lookToward.normalized * r - ballPos, up).normalized;
                Vector3 lookDir = Quaternion.AngleAxis(pitch, Vector3.Cross(up, fwd)) * fwd;
                Vector3 focus = ballPos + up * orbit.focusHeight;
                float dist = orbit.distance;
                if (Physics.SphereCast(focus, orbit.collisionRadius, -lookDir, out RaycastHit hit, dist,
                        orbit.collisionMask, QueryTriggerInteraction.Ignore))
                    dist = Mathf.Max(orbit.minDistance, hit.distance - 0.1f);
                Render(name, ballPos, focus - lookDir * dist, Quaternion.LookRotation(lookDir, up), shell);
            }

            System.Collections.Generic.List<int> Route(int index) => layout.Route(index);

            // The first render of a batch-mode session can come out wrong (materials not fully set up yet),
            // so render one throwaway frame first.
            var warmUp = new RenderTexture(64, 64, 24);
            cam.targetTexture = warmUp;
            cam.Render();
            cam.targetTexture = null;
            Object.DestroyImmediate(warmUp);

            float r0 = settings.shellRadii[0];
            var g0 = layout.Grids[0];
            Vector3 start = g0.CellCenters[layout.StartCell];
            // Show the light beam over the first section's exit, as at the start of a game.
            level.SetBeamSection(0);

            // Whole planet, from space.
            Vector3 side = Vector3.ProjectOnPlane(new Vector3(0.3f, 0.6f, 1f), start).normalized;
            Vector3 eye = (start * 1.0f + side * 0.45f).normalized * (r0 * 2.7f);
            Render("planet", start * (r0 + 0.5f), eye, Quaternion.LookRotation(-eye, side), 0);

            // Rolling through the first section, at a spot where the corridor runs straight on behind the
            // ball, so the camera sits over open floor rather than looking over a wall.
            var route0 = Route(0);
            int at = 1;
            for (int k = 1; k + 1 < route0.Count; k++)
            {
                Vector3 c = g0.CellCenters[route0[k]];
                Vector3 ahead = Vector3.ProjectOnPlane(g0.CellCenters[route0[k + 1]] - c, c).normalized;
                Vector3 behind = Vector3.ProjectOnPlane(g0.CellCenters[route0[k - 1]] - c, c).normalized;
                if (Vector3.Dot(ahead, -behind) > 0.9f) { at = k; break; }
            }
            GameView("gameplay", 0, g0.CellCenters[route0[at]], g0.CellCenters[route0[at + 1]], orbit.pitch);

            // Approaching the hole at the end of the first section.
            var drop = layout.Holes[0];
            int n = route0.Count;
            Vector3 approach = Vector3.Slerp(g0.CellCenters[route0[n - 2]], drop.Direction, 0.35f);
            GameView("drop_hole", 0, approach, drop.Direction, orbit.pitch + 6f);

            // A boost pad at the end of a section that climbs back up.
            for (int i = 0; i < layout.Sections.Count; i++)
            {
                var sec = layout.Sections[i];
                if (!sec.ExitsUp) continue;
                var grid = layout.Grids[sec.Shell];
                var route = Route(i);
                Vector3 padDir = layout.Holes[i].Direction;
                Vector3 near = Vector3.Slerp(grid.CellCenters[route[route.Count - 2]], padDir, 0.3f);
                level.SetBeamSection(i);
                GameView("boost_pad", sec.Shell, near, padDir, 14f);
                level.SetBeamSection(0);
                break;
            }

            // The synthwave sun on the horizon, from just above the wall tops near the start.
            {
                Vector3 toSun = Vector3.ProjectOnPlane(new Vector3(0.35f, 0.2f, 1f), start).normalized;
                Vector3 sunBall = start * (r0 + 0.5f);
                Vector3 sunEye = sunBall + start * (settings.wallHeight + 3f) - toSun * 6f;
                Vector3 look = Quaternion.AngleAxis(8f, Vector3.Cross(start, toSun)) * toSun;
                Render("horizon", sunBall, sunEye, Quaternion.LookRotation(look, start), 0);
            }

            // Touching down on the core.
            Vector3 coreDir = layout.Holes[layout.Holes.Count - 1].Direction;
            Vector3 coreSide = Vector3.ProjectOnPlane(new Vector3(0.3f, 0.1f, 1f), coreDir).normalized;
            Vector3 corePos = coreDir * (settings.coreRadius + 0.5f);
            Vector3 coreEye = coreDir * (settings.coreRadius + 4f) - coreSide * 7f;
            Render("core", corePos, coreEye, Quaternion.LookRotation(corePos + coreDir * 0.6f - coreEye, coreDir), layout.ShellCount);

            Object.DestroyImmediate(ball);
            Object.DestroyImmediate(level.gameObject);
            Debug.Log("[Readme] screenshots -> " + Path.GetFullPath(outDir));
        }

        /// <summary>
        /// Records about 15 seconds of the ball rolling through world 1. It opens on the synthwave sun from
        /// above the walls, cranes down to the gameplay view as the ball sets off, follows a section's route
        /// across a zone border (so the ball's color changes) to its exit hole, drops through, and carries on
        /// along the next section for a few seconds. Uses the real ball physics and follow camera, stepped by
        /// hand at 60 Hz, saving every other step (30 fps) as PNGs into DEMO_FRAMES_DIR (default
        /// Recordings/frames). Batch mode, needs graphics.
        /// </summary>
        [MenuItem("Sphere Descent/Record Demo Frames")]
        public static void RecordDemoFrames()
        {
            string outDir = System.Environment.GetEnvironmentVariable("DEMO_FRAMES_DIR") ?? "Recordings/frames";
            const int width = 1280, height = 720;
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            EditorSceneManager.OpenScene(ScenePath);
            var gm = Object.FindAnyObjectByType<GameManager>();
            gm.materials.EnsureDefaults();
            var settings = gm.settings;
            var layout = MazeLayout.Generate(settings, gm.baseSeed + 1 * 7919);
            var level = LevelBuilder.Build(layout, settings, gm.materials, 1);
            var cam = Camera.main;
            var orbit = cam.GetComponent<OrbitCamera>();
            var sun = gm.sun;

            // Pick a section ending in a drop hole whose route crosses a zone border, preferring the surface.
            List<int> route = null;
            int sectionIndex = -1, startAt = 0;
            for (int pass = 0; pass < 2 && route == null; pass++)
            {
                for (int i = 0; i < layout.Sections.Count && route == null; i++)
                {
                    var sec = layout.Sections[i];
                    if (sec.ExitsUp || layout.Holes[i].LandingCell < 0 || (pass == 0 && sec.Shell != 0)) continue;
                    var candidate = layout.Route(i);
                    var grid = layout.Grids[sec.Shell];
                    int lastChange = -1;
                    for (int k = 1; k < candidate.Count; k++)
                        if (level.Zones.ZoneAt(grid.CellCenters[candidate[k]]) != level.Zones.ZoneAt(grid.CellCenters[candidate[k - 1]])) lastChange = k;
                    if (lastChange < 0) continue;
                    if (i + 1 >= layout.Sections.Count) continue;
                    route = candidate;
                    sectionIndex = i;
                    // About seven cells before the hole, but always before the zone border.
                    startAt = Mathf.Max(0, Mathf.Min(lastChange - 2, candidate.Count - 8));
                }
            }
            if (route == null) { Debug.LogError("[Demo] no section crosses a zone border"); return; }
            var section = layout.Sections[sectionIndex];
            var hole = layout.Holes[sectionIndex];
            var sectionGrid = layout.Grids[section.Shell];
            float r = settings.shellRadii[section.Shell];
            var waypoints = new List<Vector3>();
            for (int k = startAt + 1; k < route.Count - 1; k++) waypoints.Add(sectionGrid.CellCenters[route[k]]);
            waypoints.Add(hole.Direction);
            // After the drop, carry on along the next section's route.
            var nextSection = layout.Sections[sectionIndex + 1];
            var nextGrid = layout.Grids[nextSection.Shell];
            var afterWaypoints = new List<Vector3>();
            var nextRoute = layout.Route(sectionIndex + 1);
            // Stop short of that section's own exit, so the video doesn't drop or launch again.
            for (int k = 1; k < nextRoute.Count - 1; k++) afterWaypoints.Add(nextGrid.CellCenters[nextRoute[k]]);
            if (afterWaypoints.Count == 0) afterWaypoints.Add(nextGrid.CellCenters[nextRoute[0]]);
            Debug.Log($"[Demo] section {sectionIndex + 1} on {level.LayerName(section.Shell)}, {route.Count - startAt} cells to the hole, " +
                      $"zones {level.Zones.Names[level.Zones.ZoneAt(sectionGrid.CellCenters[route[startAt]])]} -> " +
                      $"{level.Zones.Names[level.Zones.ZoneAt(hole.Direction)]}");

            // The ball, set up as in the game.
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var ball = GameManager.CreateBall(gm.materials.ball);
            typeof(BallController).GetMethod("Awake", flags).Invoke(ball, null);
            var fixedUpdate = typeof(BallController).GetMethod("FixedUpdate", flags);
            var input = typeof(BallController).GetField("moveInput", flags);
            // Collision callbacks don't run in edit mode, so detect the floor under the ball directly.
            var touching = typeof(BallController).GetField("touchingGround", flags);
            var rb = ball.GetComponent<Rigidbody>();
            ball.level = level;
            ball.cameraTransform = cam.transform;
            Vector3 startDir = sectionGrid.CellCenters[route[startAt]];
            ball.Teleport(startDir * (r + ball.Radius + 0.05f));
            // Open facing the sun on the horizon (the sky puts it this way from wherever the ball is), from
            // high enough to see over the walls; the camera cranes down to its usual view once the ball moves.
            Vector3 toSun = Vector3.ProjectOnPlane(new Vector3(0.35f, 0.2f, 1f), startDir).normalized;
            cam.transform.rotation = Quaternion.LookRotation(toSun, startDir);
            float gameFocus = orbit.focusHeight, gamePitch = orbit.pitch;
            const float introHold = 1.5f, craneTime = 2.5f, introFocus = 7f, introPitch = -4f;
            orbit.focusHeight = introFocus;
            orbit.pitch = introPitch;
            orbit.target = ball.transform;
            orbit.Snap();
            Physics.simulationMode = SimulationMode.Script;
            Physics.SyncTransforms();

            var warmUp = new RenderTexture(64, 64, 24);
            cam.targetTexture = warmUp;
            cam.Render();
            cam.targetTexture = null;
            Object.DestroyImmediate(warmUp);
            var rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);

            const float dt = 1f / 60f;
            int next = 0, nextAfter = 0, frame = 0;
            float landedFor = 0f;
            const float afterLanding = 4.5f;
            float rAfter = settings.shellRadii[nextSection.Shell];
            for (int step = 0; step < 60 * 40; step++)
            {
                float t = step * dt;
                Vector3 pos = rb.position;
                Vector3 up = pos.normalized;
                bool grounded = Physics.Raycast(pos, -up, ball.Radius + 0.15f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                bool fell = ball.CurrentLayer > section.Shell;
                bool landed = fell && landedFor > 0f;

                // Crane down from the opening view to the gameplay camera.
                float crane = Mathf.SmoothStep(0f, 1f, (t - introHold) / craneTime);
                orbit.focusHeight = Mathf.Lerp(introFocus, gameFocus, crane);
                orbit.pitch = Mathf.Lerp(introPitch, gamePitch, crane);

                // Autopilot: head for the next waypoint along the route, as camera-relative stick input.
                // Hold still for the opening, coast while falling, then carry on through the next section.
                Vector2 move = Vector2.zero;
                List<Vector3> path = landed ? afterWaypoints : waypoints;
                if (t >= introHold && (!fell || landed))
                {
                    ref int index = ref next;
                    if (landed) index = ref nextAfter;
                    float radius = landed ? rAfter : r;
                    float toWaypoint = Vector3.Angle(up, path[index]) * Mathf.Deg2Rad * radius;
                    if (index < path.Count - 1 && toWaypoint < 2.5f) index++;
                    Vector3 desired = landed && index == path.Count - 1 && toWaypoint < 1.5f
                        ? Vector3.zero
                        : Vector3.ProjectOnPlane(path[index] - up, up).normalized;
                    Vector3 camForward = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
                    Vector3 camRight = Vector3.Cross(up, camForward);
                    move = new Vector2(Vector3.Dot(desired, camRight), Vector3.Dot(desired, camForward)).normalized * 0.75f;
                }
                input.SetValue(ball, move);
                touching.SetValue(ball, grounded);
                fixedUpdate.Invoke(ball, null);
                Physics.Simulate(dt);
                ball.transform.SetPositionAndRotation(rb.position, rb.rotation);

                // What GameManager does each frame.
                orbit.Tick(dt);
                level.SetShadowLayer(ball.CurrentLayer);
                level.SetVisibleLayers(Mathf.Max(ball.CurrentLayer, level.LayerAt(cam.transform.position.magnitude)));
                GameManager.UpdateSky(sun, rb.position.normalized);

                if (step % 2 == 0)
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"frame_{frame++:D5}.png"), tex.EncodeToPNG());
                    cam.targetTexture = null;
                    RenderTexture.active = null;
                }

                if (fell && (grounded || landedFor > 0f)) landedFor += dt;
                if (landedFor > afterLanding) break;
            }

            Physics.simulationMode = SimulationMode.FixedUpdate;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(ball.gameObject);
            Object.DestroyImmediate(level.gameObject);
            Debug.Log($"[Demo] {frame} frames ({frame / 30f:F1} s), {(landedFor > 0f ? "dropped through the hole" : "DID NOT reach the hole")} -> {Path.GetFullPath(outDir)}");
        }
    }
}
