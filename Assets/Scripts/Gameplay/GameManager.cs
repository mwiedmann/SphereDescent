using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// Owns the game loop: spawns the ball and camera, generates each world from a seed, advances to the
    /// next world when the exit is reached, keeps the sun overhead, and draws a minimal HUD.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public LevelSettings settings = new LevelSettings();
        public LevelMaterials materials = new LevelMaterials();
        [Tooltip("Background color when the scene has no skybox material.")]
        public Color skyColor = new Color(0.08f, 0.02f, 0.16f);
        [Tooltip("Each world's seed is derived from this, so the same number always gives the same worlds.")]
        public int baseSeed = 1337;
        [Tooltip("Hand-picked seeds for the first worlds (world 1 first), used instead of one derived from Base Seed.")]
        public int[] worldSeeds = (int[])DefaultWorldSeeds.Clone();

        /// <summary>Picked for how well everything fits: nearly every major hazard slot and every extra checkpoint.</summary>
        public static readonly int[] DefaultWorldSeeds = { 733206 };

        /// <summary>A world's seed: its hand-picked one if it has one, otherwise derived from the base seed.</summary>
        public static int SeedFor(int baseSeed, int[] worldSeeds, int world) =>
            worldSeeds != null && world >= 1 && world <= worldSeeds.Length ? worldSeeds[world - 1] : baseSeed + world * 7919;

        public int SeedFor(int world) => SeedFor(baseSeed, worldSeeds, world);
        public int startWorld = 1;
        public float worldTransitionDelay = 2.5f;
        public Light sun;
        [Tooltip("Looks the worlds take turns through: world 1 uses the first, world 2 the second, and so on, wrapping around.")]
        public VisualStyle[] styles = { VisualStyle.Synthwave(), VisualStyle.DeepSea() };
        [Tooltip("M (or Start on a gamepad) opens a menu to pick the style and the world to play.")]
        public bool styleMenu = true;
        [Tooltip("Highest world the menu lets you pick.")]
        public int maxMenuWorld = 20;

        LevelInstance level;
        BallController ball;
        OrbitCamera orbit;
        Camera cam;
        CameraClearFlags surfaceClearFlags;
        VisualStyle style;
        int styleChoice;      // index into styles, or -1 to take turns world by world
        bool menuOpen;
        int menuStyle, menuWorld; // what the menu has selected, applied on Start
        float frameMs;
        Material skyMaterial;
        ParticleSystem specks;
        int world;
        bool transitioning;
        string message;
        float messageUntil;
        GUIStyle hudStyle, bannerStyle, rightStyle, menuTitleStyle, menuButtonStyle, menuHintStyle;
        int section;
        int score;
        int climbingThrough = -1; // the hole a boost-pad launch is heading up through, until the ball lands

        public int World => world;

        void Awake()
        {
            Instance = this;
            materials.EnsureDefaults();
            if (sun == null) sun = RenderSettings.sun;
            // Styles recolor the sky, so work on a copy rather than the material asset.
            if (RenderSettings.skybox != null) RenderSettings.skybox = skyMaterial = new Material(RenderSettings.skybox);
        }

        VisualStyle StyleFor(int w)
        {
            if (styles == null || styles.Length == 0) return VisualStyle.Synthwave();
            return styleChoice >= 0 && styleChoice < styles.Length ? styles[styleChoice] : styles[(w - 1) % styles.Length];
        }

        /// <summary>Pauses on the menu (the world stays in view behind it), with the current style and world selected.</summary>
        void OpenMenu()
        {
            if (transitioning) return;
            menuOpen = true;
            menuStyle = styleChoice;
            menuWorld = world;
            ball.SetFrozen(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void CloseMenu()
        {
            menuOpen = false;
            ball.SetFrozen(false);
        }

        /// <summary>Starts a world over in a style (-1 = take turns each world), with the score reset.</summary>
        void StartWorld(int styleIndex, int worldNumber)
        {
            styleChoice = styleIndex;
            menuOpen = false;
            world = Mathf.Max(1, worldNumber);
            score = 0;
            BuildWorld();
            ball.SetFrozen(false);
        }

        /// <summary>Sets up the sky, light and air for a world's style (its surfaces are styled as the level is built).</summary>
        void ApplyStyle(VisualStyle s)
        {
            style = s;
            ApplyAtmosphere(s, skyMaterial, sun);
            SetSpecks(s.floatingSpecks, s.speckColor);
        }

        /// <summary>A style's sky, light and fog. Static so editor tools can preview a style too.</summary>
        public static void ApplyAtmosphere(VisualStyle s, Material skyMaterial, Light sun)
        {
            if (skyMaterial != null)
            {
                skyMaterial.SetColor("_TopColor", s.skyTop);
                skyMaterial.SetColor("_MidColor", s.skyMid);
                skyMaterial.SetColor("_HorizonColor", s.skyHorizon);
                skyMaterial.SetColor("_BelowColor", s.skyBelow);
                skyMaterial.SetColor("_SunTopColor", s.sunTop);
                skyMaterial.SetColor("_SunBottomColor", s.sunBottom);
                skyMaterial.SetFloat("_SunSize", s.sunSize);
                skyMaterial.SetFloat("_SunGlow", s.sunGlow);
                skyMaterial.SetFloat("_StarDensity", s.starDensity);
                skyMaterial.SetFloat("_StarDrift", s.starDrift);
                skyMaterial.SetFloat("_RayStrength", s.lightRays);
            }
            if (sun != null)
            {
                sun.color = s.sunLight;
                sun.intensity = s.sunLightIntensity;
            }
            RenderSettings.ambientLight = s.ambientLight;
            RenderSettings.fog = s.fogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = s.fogDensity;
            RenderSettings.fogColor = s.fogColor;
        }

        /// <summary>Tiny specks drifting in the water around the camera (marine snow). Zero turns them off.</summary>
        void SetSpecks(int count, Color color)
        {
            if (count <= 0)
            {
                if (specks != null) specks.gameObject.SetActive(false);
                return;
            }
            if (specks == null)
            {
                var go = new GameObject("Floating Specks");
                go.transform.SetParent(cam.transform, false);
                specks = go.AddComponent<ParticleSystem>();
                specks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var shape = specks.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(36f, 24f, 36f);
                var noise = specks.noise;
                noise.enabled = true;
                noise.strength = 0.25f;
                noise.frequency = 0.3f;
                var r = go.GetComponent<ParticleSystemRenderer>();
                var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                // Additive, so specks glow softly and never darken what's behind them.
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 2f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            specks.gameObject.SetActive(true);
            var main = specks.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = color;
            var emission = specks.emission;
            emission.rateOverTime = count / 8f;
            specks.Play();
        }

        void Start()
        {
            world = Mathf.Max(1, startWorld);
            CreatePlayer();
            BuildWorld();
        }

        /// <summary>The player's ball: a sphere with grippy physics and the ball controller.</summary>
        public static BallController CreateBall(Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Player Ball";
            go.layer = 2; // Ignore Raycast, so the camera's collision probe skips the ball
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            var col = go.GetComponent<SphereCollider>();
            col.material = new PhysicsMaterial("Ball")
            {
                dynamicFriction = 0.9f,
                staticFriction = 0.9f,
                bounciness = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            go.AddComponent<Rigidbody>().mass = 1f;
            return go.AddComponent<BallController>();
        }

        void CreatePlayer()
        {
            ball = CreateBall(materials.ball);
            ball.DroppedThrough += OnDroppedThrough;
            ball.Respawned += FaceTheWayOn;
            ball.LandedOnNewLayer += TurnTheWayOn;
            ball.Launched += OnLaunched;

            cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            // The synthwave skybox follows the player's local up (see UpdateSky); without it, use a plain color.
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = skyColor;
            surfaceClearFlags = cam.clearFlags;
            orbit = cam.GetComponent<OrbitCamera>();
            if (orbit == null) orbit = cam.gameObject.AddComponent<OrbitCamera>();
            orbit.target = ball.transform;
            ball.cameraTransform = cam.transform;
        }

        void BuildWorld()
        {
            if (level != null) Destroy(level.gameObject);

            float t0 = Time.realtimeSinceStartup;
            var layout = MazeLayout.Generate(settings, SeedFor(world));
            var worldStyle = StyleFor(world);
            level = LevelBuilder.Build(layout, settings, materials, world, worldStyle);
            ApplyStyle(worldStyle);
            Debug.Log($"World {world}: seed {layout.Seed}, layout found after {layout.AttemptsUsed} attempts, " +
                      $"{layout.Sections.Count} sections ({layout.Climbs} climbs), " +
                      $"built in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
            section = 0;
            climbingThrough = -1;

            ball.level = level;
            ball.ForgetCheckpoints();
            ball.Teleport(level.SpawnPosition);
            FaceTheWayOn();
            ShowMessage(styles != null && styles.Length > 1 ? $"World {world}: {worldStyle.name}" : $"World {world}", 3f);
        }

        public int Score => score;

        public void CollectCube(EnergyCube cube)
        {
            score += cube.points;
            if (cube.isSuper) ShowMessage($"Super cube! +{cube.points}", 1.5f);
        }

        /// <summary>Points the camera the way the route goes on from where the ball is (at a world's start and on respawn).</summary>
        void FaceTheWayOn()
        {
            if (level == null || ball == null) return;
            Vector3 pos = ball.transform.position;
            orbit.Snap(level.Layout.RouteDirectionAt(level.LayerAt(pos.magnitude), pos));
        }

        /// <summary>After landing on a new layer, swings the camera round to face the way the route goes on.</summary>
        void TurnTheWayOn()
        {
            if (level == null || ball == null) return;
            Vector3 pos = ball.transform.position;
            orbit.TurnTo(level.Layout.RouteDirectionAt(level.LayerAt(pos.magnitude), pos));
        }

        /// <summary>Seals the hole the ball just fell through.</summary>
        void OnDroppedThrough(int fromLayer, Vector3 position)
        {
            if (level == null) return;
            int best = -1;
            float bestAngle = float.MaxValue;
            var holes = level.Layout.Holes;
            for (int i = 0; i < holes.Count; i++)
            {
                if (holes[i].Shell != fromLayer) continue;
                float angle = Vector3.Angle(holes[i].Direction, position);
                if (angle < bestAngle) { bestAngle = angle; best = i; }
            }
            if (best >= 0) level.MarkUsed(false, best);
        }

        void OnLaunched(BoostPad pad)
        {
            if (level == null) return;
            var holes = level.Layout.Holes;
            for (int i = 0; i < holes.Count; i++)
                if (holes[i].TwoWay && Vector3.Dot(holes[i].Direction, pad.Axis) > 0.9999f) climbingThrough = i;
        }

        public void OnExitReached()
        {
            if (!transitioning) StartCoroutine(AdvanceWorld());
        }

        IEnumerator AdvanceWorld()
        {
            transitioning = true;
            ball.SetFrozen(true);
            ShowMessage($"World {world} complete!", worldTransitionDelay);
            yield return new WaitForSeconds(worldTransitionDelay);
            world++;
            BuildWorld();
            ball.SetFrozen(false);
            transitioning = false;
        }

        void Update()
        {
            // Smoothed frame time for the HUD, to compare how heavy each style is.
            frameMs = Mathf.Lerp(frameMs <= 0f ? Time.unscaledDeltaTime * 1000f : frameMs, Time.unscaledDeltaTime * 1000f, 0.05f);

            if (menuOpen)
            {
                int n = GameInput.NumberPressed();
                if (n >= 1 && n <= styles.Length) menuStyle = n - 1;
                else if (n == styles.Length + 1) menuStyle = -1;
                menuWorld = Mathf.Clamp(menuWorld + GameInput.MenuStep(), 1, Mathf.Max(1, maxMenuWorld));
                if (GameInput.ConfirmPressed()) StartWorld(menuStyle, menuWorld);
                else if (GameInput.MenuPressed() || GameInput.UnlockCursorPressed()) CloseMenu();
            }
            else
            {
                if (GameInput.LockCursorPressed() && Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                if (GameInput.UnlockCursorPressed())
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                if (GameInput.SkipWorldPressed()) OnExitReached();
                if (styleMenu && GameInput.MenuPressed()) OpenMenu();
            }
            if (level != null && ball != null)
            {
                // Landing anywhere on the core completes the world.
                if (ball.IsGrounded && ball.CurrentLayer >= level.LayerCount - 1) OnExitReached();
                int s = level.Layout.SectionAt(ball.CurrentLayer, ball.transform.position);
                if (s >= 0) section = s;
                level.SetBeamSection(section);
                // A climb seals its pad and the hole above once the ball has landed up there.
                if (climbingThrough >= 0 && ball.IsGrounded && !ball.IsLaunching &&
                    ball.CurrentLayer == level.Layout.Holes[climbingThrough].Shell)
                {
                    level.MarkUsed(true, climbingThrough);
                    level.MarkUsed(false, climbingThrough);
                    climbingThrough = -1;
                }

                level.SetShadowLayer(ball.CurrentLayer);
                // Only one layer is drawn: the deeper of the ball's and camera's. Falling through a hole shows the
                // new layer as soon as the ball is through; climbing waits for the camera to come up through the
                // hole too, so it never looks up at the underside of the shell above.
                int cameraLayer = level.LayerAt(orbit.transform.position.magnitude);
                int visibleLayer = Mathf.Max(ball.CurrentLayer, cameraLayer);
                level.SetVisibleLayers(visibleLayer);
                level.UpdateChunks(cam.transform.position);
                // Below the surface the layers above are hidden, and the sky is plain black.
                cam.clearFlags = visibleLayer > 0 ? CameraClearFlags.SolidColor : surfaceClearFlags;
                cam.backgroundColor = visibleLayer > 0 ? (style != null ? style.innerBackground : Color.black) : skyColor;
            }
        }

        void LateUpdate()
        {
            if (ball != null) UpdateSky(sun, ball.transform.position.normalized);
        }

        /// <summary>
        /// Keeps the sun light high above wherever the player is, so no part of the sphere is stuck in night,
        /// and points the skybox's horizon and retro sun the same way (the sky sun sits on the horizon in the
        /// light's direction).
        /// </summary>
        public static void UpdateSky(Light sun, Vector3 up)
        {
            Vector3 tilt = Vector3.ProjectOnPlane(new Vector3(0.35f, 0.2f, 1f), up);
            if (tilt.sqrMagnitude < 0.01f) tilt = Vector3.ProjectOnPlane(Vector3.right, up);
            tilt.Normalize();
            if (sun != null) sun.transform.rotation = Quaternion.LookRotation(-(up + tilt * 0.55f).normalized, tilt);
            Shader.SetGlobalVector("_PlayerUp", up);
            Shader.SetGlobalVector("_SkySunDir", (tilt + up * 0.1f).normalized);
        }

        public void ShowMessage(string text, float seconds)
        {
            message = text;
            messageUntil = Time.time + seconds;
        }

        void OnGUI()
        {
            if (level == null || ball == null) return;
            hudStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
            bannerStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 42, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white },
            };

            int layer = Mathf.Clamp(ball.CurrentLayer, 0, level.LayerCount - 1);
            string zone = level.Zones.Names[level.Zones.ZoneAt(ball.transform.position)];
            string status = $"World {world}   ·   Section {section + 1}/{level.Layout.Sections.Count}   ·   " +
                            $"{level.LayerName(layer)} ({layer + 1}/{level.LayerCount})   ·   {zone} zone   ·   Score {score}";
            Shadowed(new Rect(16, 12, 900, 30), status, hudStyle);

            string help = GameInput.GamepadConnected
                ? "Left stick / D-pad move · A jump · Right stick camera · Select respawn"
                : Cursor.lockState == CursorLockMode.Locked
                ? "WASD move · Space jump · Mouse/Q/E camera · R respawn · Esc free cursor · M menu"
                : "Click to capture mouse · WASD move · Space jump · Q/E camera · R respawn · N skip world · M menu";
            Shadowed(new Rect(16, Screen.height - 36, 1000, 30), help, hudStyle);

            // Frame time and style, top right.
            string perf = $"{frameMs:F1} ms ({(frameMs > 0f ? 1000f / frameMs : 0f):F0} fps)   ·   {(style != null ? style.name : "")}";
            Shadowed(new Rect(Screen.width - 416, 12, 400, 30), perf, rightStyle ??= new GUIStyle(hudStyle) { alignment = TextAnchor.UpperRight });

            if (menuOpen)
            {
                DrawMenu();
                return;
            }
            if (Time.time < messageUntil && !string.IsNullOrEmpty(message))
                Shadowed(new Rect(0, Screen.height * 0.25f, Screen.width, 60), message, bannerStyle);
        }

        void DrawMenu()
        {
            const float w = 440f, row = 46f;
            int options = styles.Length + 1;
            float h = 64f + options * row + 40f + row + 16f + row + 16f;
            var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            // Drawn twice for a darker panel behind the buttons.
            GUI.Box(box, "");
            GUI.Box(box, "");
            menuTitleStyle ??= new GUIStyle(bannerStyle) { fontSize = 28 };
            menuButtonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 20 };
            var sectionStyle = menuHintStyle ??= new GUIStyle(hudStyle) { alignment = TextAnchor.UpperCenter, fontSize = 15 };

            float y = box.y + 12;
            Shadowed(new Rect(box.x, y, w, 40), "Style", menuTitleStyle);
            y += 52;
            for (int i = 0; i < options; i++)
            {
                int value = i < styles.Length ? i : -1;
                string label = i < styles.Length ? styles[i].name : "Alternate every world";
                // The selected style is marked, and its button stays pressed.
                bool chosen = GUI.Toggle(new Rect(box.x + 30, y, w - 60, row - 8), menuStyle == value,
                    $"{i + 1}.  {label}{(menuStyle == value ? "   (selected)" : "")}", menuButtonStyle);
                if (chosen) menuStyle = value;
                y += row;
            }

            y += 8;
            Shadowed(new Rect(box.x, y, w, 32), "World", new GUIStyle(menuTitleStyle) { fontSize = 22 });
            y += 34;
            float bw = 60f;
            if (GUI.Button(new Rect(box.x + 30, y, bw, row - 8), "−", menuButtonStyle)) menuWorld = Mathf.Max(1, menuWorld - 1);
            Shadowed(new Rect(box.x + 30 + bw, y + 4, w - 60 - bw * 2, row - 8), $"{menuWorld}",
                new GUIStyle(menuTitleStyle) { fontSize = 24 });
            if (GUI.Button(new Rect(box.xMax - 30 - bw, y, bw, row - 8), "+", menuButtonStyle))
                menuWorld = Mathf.Min(Mathf.Max(1, maxMenuWorld), menuWorld + 1);
            y += row + 16;

            if (GUI.Button(new Rect(box.x + 30, y, w - 60, row - 4), "Start", menuButtonStyle)) StartWorld(menuStyle, menuWorld);

            Shadowed(new Rect(box.x - 60, box.yMax + 6, w + 120, 30),
                "1-3 style   ·   ← → world   ·   Enter start   ·   Esc / M close", sectionStyle);
        }

        static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }
    }
}
