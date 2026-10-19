using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

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
                ball = SaveMaterial("Ball", LevelMaterials.MakeGrid(new Color(0.96f, 0.96f, 0.96f), new Color(0.9f, 0.2f, 0.25f), 0.5f, true)),
                pad = SaveMaterial("BoostPad", LevelMaterials.MakeEmissive(new Color(0.2f, 0.9f, 1f), 2f)),
                ringUp = SaveMaterial("HoleRingTwoWay", LevelMaterials.MakeEmissive(new Color(0.2f, 0.9f, 1f), 1.5f)),
                ringDrop = SaveMaterial("HoleRingDrop", LevelMaterials.MakeEmissive(new Color(1f, 0.55f, 0.1f), 1.5f)),
                exit = SaveMaterial("Exit", LevelMaterials.MakeEmissive(new Color(1f, 0.85f, 0.2f), 3f)),
            };

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
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.55f, 0.72f, 0.92f);
                }
            }
            if (sun != null)
            {
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1.1f;
                RenderSettings.sun = sun;
            }

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.52f);
            QualitySettings.shadowDistance = 60f;

            var gm = new GameObject("Game Manager").AddComponent<GameManager>();
            gm.materials = mats;
            gm.sun = sun;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Sphere Descent: main scene created at " + ScenePath);
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
            for (int world = 1; world <= 8; world++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var layout = MazeLayout.Generate(settings, 1337 + world * 7919);
                long genMs = sw.ElapsedMilliseconds;
                if (settings.requireClimb && layout.ShortestRouteUps == 0) failures++;

                int twoWay = 0;
                var perShell = new int[layout.ShellCount];
                foreach (var h in layout.Holes) { if (h.TwoWay) twoWay++; perShell[h.Shell]++; }
                var shellInfo = new System.Text.StringBuilder();
                for (int sh = 0; sh < layout.ShellCount; sh++)
                    shellInfo.Append($" [r{settings.shellRadii[sh]}: {layout.Grids[sh].CellCount} cells, " +
                                     $"spacing {layout.Grids[sh].CellSpacing * settings.shellRadii[sh]:F1}, {perShell[sh]} holes]");
                string msg = $"[Validate] World {world}: seed {layout.Seed}, attempts {layout.AttemptsUsed}, holes {layout.Holes.Count} ({twoWay} two-way), " +
                             $"route {layout.ShortestRouteMoves} transitions / {layout.ShortestRouteUps} launches, layout {genMs} ms";

                if (world == 1) Debug.Log("[Validate] shells:" + shellInfo);
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
                    int exits = level.GetComponentsInChildren<ExitPortal>().Length;
                    msg += $", build {sw.ElapsedMilliseconds} ms, {verts} verts, {tris} tris, {pads} pads, {exits} exit";
                    Object.DestroyImmediate(level.gameObject);
                }
                Debug.Log(msg);
            }
            Debug.Log(failures == 0 ? "[Validate] OK" : $"[Validate] {failures} worlds have no forced launch");
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

            MazeLayout.Hole drop = default, twoWay = default;
            bool hasDrop = false, hasTwoWay = false;
            foreach (var h in layout.Holes)
            {
                if (h.Shell == 0 && !h.TwoWay && !hasDrop) { drop = h; hasDrop = true; }
                if (h.Shell == 0 && h.TwoWay && !hasTwoWay) { twoWay = h; hasTwoWay = true; }
            }

            float r0 = settings.shellRadii[0], r1 = settings.shellRadii[1];
            Report("solid ground", Simulate(level, grid.CellCenters[layout.StartCell] * (r0 + 3f), 3f, null), r0 + 0.5f);
            if (hasDrop)
                Report("drop hole", Simulate(level, drop.Direction * (r0 + 3f), 4f, null), r1 + 0.5f);
            if (hasTwoWay)
            {
                BoostPad pad = null;
                foreach (var p in level.GetComponentsInChildren<BoostPad>())
                    if (Vector3.Dot(p.Axis, twoWay.Direction) > 0.9999f) pad = p;
                Vector3 end = Simulate(level, pad.transform.position + pad.Axis * 0.5f, 5f, pad);
                float fromHole = Vector3.Angle(end, pad.Axis) * Mathf.Deg2Rad * r0;
                Report($"launch ({fromHole:F1} units from hole center, hole radius {settings.HoleRadius(grid, 0):F1})", end, r0 + 0.5f);
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
                sun.transform.rotation = Quaternion.LookRotation(-(up + Vector3.ProjectOnPlane(new Vector3(0.35f, 0.2f, 1f), up).normalized * 0.55f).normalized);
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

            foreach (var h in layout.Holes)
            {
                if (h.Shell != 0) continue;
                Vector3 d = h.Direction;
                Vector3 t = Tangent(d);
                Vector3 nearHole = (d + t * (5f / r0)).normalized;
                Shot(h.TwoWay ? "3_hole_twoway" : "3_hole_drop", nearHole * (r0 + 0.5f), nearHole * (r0 + 9f) + t * 6f);
                if (h.TwoWay)
                {
                    float r1 = gm.settings.shellRadii[1];
                    Vector3 nearPad = (d + t * (4f / r1)).normalized;
                    Shot("4_inner_pad", nearPad * (r1 + 0.5f), nearPad * (r1 + 3.5f) + t * 8f);
                    break;
                }
            }
            Vector3 e = layout.ExitDirection;
            Vector3 nearExit = (e + Tangent(e) * (5f / gm.settings.coreRadius)).normalized;
            Shot("5_core_exit", nearExit * (gm.settings.coreRadius + 0.5f), nearExit * (gm.settings.coreRadius + 4f) + Tangent(e) * 7f);

            Object.DestroyImmediate(ball);
            Object.DestroyImmediate(level.gameObject);
            Debug.Log("[Preview] done -> " + Path.GetFullPath(outDir));
        }
    }
}
