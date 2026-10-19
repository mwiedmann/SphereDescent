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
                ball = SaveMaterial("Ball", LevelMaterials.MakeGrid(new Color(0.96f, 0.96f, 0.96f), new Color(1f, 0.15f, 0.6f), 0.5f, true)),
                pad = SaveMaterial("BoostPad", LevelMaterials.MakeEmissive(new Color(0.2f, 0.9f, 1f), 2f)),
                ringUp = SaveMaterial("HoleRingTwoWay", LevelMaterials.MakeEmissive(new Color(0.2f, 0.9f, 1f), 1.5f)),
                ringDrop = SaveMaterial("HoleRingDrop", LevelMaterials.MakeEmissive(new Color(1f, 0.45f, 0.05f), 1.8f)),
                exit = SaveMaterial("Exit", LevelMaterials.MakeEmissive(new Color(1f, 0.85f, 0.2f), 3f)),
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

                var routes = new System.Text.StringBuilder();
                foreach (var sec in layout.Sections)
                {
                    routes.Append($" {sec.Shell}{(sec.ExitsUp ? "^" : "v")}{sec.RouteCells}/{sec.CellCount}");
                    allRoutes.Add(sec.RouteCells);
                }
                if (problems.Length > 0) failures++;
                string msg = $"[Validate] World {world}: seed {layout.Seed}, attempts {layout.AttemptsUsed}, {layout.Sections.Count} sections, " +
                             $"{layout.Climbs} climbs, layout {genMs} ms. shell/dir/route/cells:{routes}" +
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
                if (!h.TwoWay) continue;
                BoostPad pad = null;
                foreach (var p in pads)
                    if (Vector3.Dot(p.Axis, h.Direction) > 0.9999f) pad = p;
                Vector3 end = Simulate(level, pad.transform.position + pad.Axis * 0.5f, 5f, pad);
                float fromHole = Vector3.Angle(end, pad.Axis) * Mathf.Deg2Rad * above;
                Report($"section {i + 1} launch to shell {h.Shell} ({fromHole:F1} units from hole center, hole radius " +
                       $"{settings.HoleRadius(layout.Grids[h.Shell], h.Shell):F1})", end, above + 0.5f);
            }

            Physics.simulationMode = prevMode;
            Object.DestroyImmediate(level.gameObject);
        }

        static Vector3 Simulate(LevelInstance level, Vector3 start, float seconds, BoostPad launchFrom)
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

        static void Report(string name, Vector3 end, float expectedRadius)
        {
            float r = end.magnitude;
            bool ok = Mathf.Abs(r - expectedRadius) < 0.3f;
            Debug.Log($"[Physics] {(ok ? "PASS" : "FAIL")} {name}: ended at radius {r:F2}, expected {expectedRadius:F2}");
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
                level.SetVisibleLayers(Mathf.Min(level.LayerAt(ballPos.magnitude), level.LayerAt(camPos.magnitude)));
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

            // The in-game camera: behind and above the ball, looking along forward, pulled in like the real
            // camera when a wall is in the way.
            void GameView(string name, int shell, Vector3 ballDir, Vector3 lookToward, float pitch)
            {
                float r = settings.SurfaceRadius(shell);
                Vector3 up = ballDir.normalized;
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

            // The route through a section, entry to exit.
            System.Collections.Generic.List<int> Route(int index)
            {
                var sec = layout.Sections[index];
                var grid = layout.Grids[sec.Shell];
                var prev = new System.Collections.Generic.Dictionary<int, int> { [sec.EntryCell] = -1 };
                var queue = new System.Collections.Generic.Queue<int>();
                queue.Enqueue(sec.EntryCell);
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    foreach (int nb in grid.Neighbors[cur])
                    {
                        if (prev.ContainsKey(nb) || layout.Region[sec.Shell][nb] != index) continue;
                        if (!layout.Open[sec.Shell][grid.EdgeBetween(cur, nb)]) continue;
                        prev[nb] = cur;
                        queue.Enqueue(nb);
                    }
                }
                var route = new System.Collections.Generic.List<int>();
                for (int c = sec.ExitCell; c != -1; c = prev[c]) route.Insert(0, c);
                return route;
            }

            float r0 = settings.shellRadii[0];
            var g0 = layout.Grids[0];
            Vector3 start = g0.CellCenters[layout.StartCell];

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
                GameView("boost_pad", sec.Shell, near, padDir, 14f);
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
    }
}
