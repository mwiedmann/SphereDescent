using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// Owns the game loop: spawns the ball and camera, generates each world from a seed, plays out the end of
    /// each world once the core's energy ball is freed and moves on to the next, keeps the sun overhead, and
    /// draws a minimal HUD.
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
        readonly Scoreboard scores = new Scoreboard();
        Tally tally;                // a finished section's score being shown mid-screen
        int summaryPage;            // 0 = none, 1 = the world's sections, 2 = the run's worlds
        float summaryShownAt;
        GUIStyle scoreLabelStyle, scoreValueStyle, tallyTitleStyle, tallyBigStyle, tallyWorldStyle, summaryRowStyle,
            summaryValueStyle, summaryTotalStyle, promptStyle;

        // Points just scored, floating up from where they were picked up.
        readonly System.Collections.Generic.List<Popup> popups = new System.Collections.Generic.List<Popup>();
        GUIStyle popupStyle;

        struct Popup
        {
            public Vector3 Position;
            public int Points;
            public float BornAt;
            public bool Super;
            public Color Color;
        }

        const float PopupLife = 1f;

        class Tally
        {
            public int Section, Earned, Possible, WorldFrom, WorldTo, PossibleFrom, PossibleTo;
            public float StartedAt;
        }

        // How the mid-screen tally plays out, in seconds.
        const float TallyWorldAppears = 1.2f, TallyCountUp = 2f, TallyEnd = 5f, TallyFade = 0.6f;
        [Tooltip("Seconds the end-of-world score screens stay up before any button moves on.")]
        public float summaryMinSeconds = 3f;
        int climbingThrough = -1; // the hole a boost-pad launch is heading up through, until the ball lands
        float thanksShownAt = -1f;  // when the freed energy ball's thanks came up (-1 = not showing)
        GUIStyle thanksTitleStyle, thanksStyle;
        [Tooltip("Seconds the freed energy ball's thanks stay up before any button moves on.")]
        public float thanksMinSeconds = 3f;
        [Tooltip("Seconds the camera watches the freed energy ball fly away.")]
        public float flyAwaySeconds = 4f;
        [Tooltip("Seconds into the energy ball's introduction before a button skips it (so steering doesn't).")]
        public float introSkipDelay = 1f;
        float introStartedAt = -1f; // while the introduction plays
        bool introSkipped;
        string caption;             // what the energy ball is saying
        [Tooltip("How much higher (and farther back) the camera hovers on the core, to see more of the path ahead.")]
        public float coreCameraLift = 2.5f;

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
            // A fresh start from the menu is a new run.
            scores.Reset();
            tally = null;
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
            scores.StartWorld(world, worldStyle.name, layout, settings);

            ball.level = level;
            ball.ForgetCheckpoints();
            ball.Teleport(level.SpawnPosition);
            FaceTheWayOn();
            ShowMessage(styles != null && styles.Length > 1 ? $"World {world}: {worldStyle.name}" : $"World {world}", 3f);
        }

        public Scoreboard Scores => scores;

        public void CollectCube(EnergyCube cube)
        {
            scores.Add(cube.section, cube.points);
            ShowPoints(cube.transform.position, cube.points, cube.isSuper, new Color(1f, 0.85f, 0.25f));
        }

        public void CheckpointReached(CheckpointPad pad)
        {
            scores.Add(pad.section, pad.points);
            ShowPoints(pad.transform.position + pad.transform.up * 0.8f, pad.points, false, new Color(0.4f, 1f, 0.3f));
            ShowMessage("Checkpoint!", 1.6f);
        }

        public void KeyPadPressed(LaserSwitch pad)
        {
            scores.Add(pad.section, pad.points);
            ShowPoints(pad.transform.position + pad.transform.up * 0.8f, pad.points, false, new Color(0.4f, 1f, 0.3f));
        }

        /// <summary>
        /// The ball has rolled into the core's energy ball with every wall down: score the rescue, show its
        /// thanks until a button is pressed, watch it fly away, then bank the core and show the world's scores.
        /// </summary>
        public void EnergyFreed(EnergyOrb orb)
        {
            if (!transitioning) StartCoroutine(FreeEnergy(orb));
        }

        IEnumerator FreeEnergy(EnergyOrb orb)
        {
            transitioning = true;
            ball.SetFrozen(true);
            var w = scores.Current;
            scores.Add(w.CoreIndex, settings.rescuePoints);
            ShowPoints(orb.body.position, settings.rescuePoints, true, orb.color);
            message = null;

            thanksShownAt = Time.time;
            yield return null;
            while (Time.time - thanksShownAt < thanksMinSeconds || !GameInput.ContinuePressed()) yield return null;
            thanksShownAt = -1f;

            // The camera stays put and turns to follow it up and away.
            orbit.enabled = false;
            Vector3 up = ball.transform.position.normalized;
            var flight = StartCoroutine(orb.FlyAway(flyAwaySeconds));
            for (float t = 0f; t < flyAwaySeconds; t += Time.deltaTime)
            {
                Vector3 look = orb.body.position - cam.transform.position;
                if (look.sqrMagnitude > 1e-4f)
                    cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look, up), 1f - Mathf.Exp(-6f * Time.deltaTime));
                yield return null;
            }
            yield return flight;
            orbit.enabled = true;

            scores.Finish(w.CoreIndex);
            yield return AdvanceWorld();
        }

        /// <summary>
        /// The first time the ball reaches the walls on the core, everything stops for the energy ball to
        /// introduce itself: the camera turns to it as it asks to be freed, flies out over the core to a key pad
        /// and lingers there, then flies back to it as it warns of the falling lava, and cuts back to the ball.
        /// Any button skips it, after a moment.
        /// </summary>
        public void PlayCoreIntro(CoreArena arena)
        {
            if (transitioning) { arena.IntroDone = true; return; }
            StartCoroutine(CoreIntro(arena));
        }

        IEnumerator CoreIntro(CoreArena arena)
        {
            transitioning = true;
            ball.SetFrozen(true);
            orbit.enabled = false;
            message = null;
            introStartedAt = Time.time;
            introSkipped = false;
            // The energy ball, from just outside the walls on the ball's side.
            Vector3 orb = arena.orb.body.position, cageUp = arena.cage;
            Vector3 toBall = Vector3.ProjectOnPlane(ball.transform.position.normalized - cageUp, cageUp).normalized;
            Vector3 orbEye = orb + cageUp * 6f + toBall * 14f;
            caption = "Please, free me! Destroy the walls that hold me here.";
            yield return MoveCamera(orbEye, orb, 1.2f, 3f);

            // Out over the core to the key pad nearest the ball, seen from beyond it looking back toward the walls.
            Transform pad = arena.pads.Length > 0 ? arena.pads[0] : null;
            foreach (var p in arena.pads)
                if (Vector3.Distance(p.position, ball.transform.position) < Vector3.Distance(pad.position, ball.transform.position)) pad = p;
            if (pad != null && !introSkipped)
            {
                Vector3 padUp = pad.position.normalized;
                Vector3 outward = Vector3.ProjectOnPlane(padUp - cageUp, padUp).normalized;
                Vector3 padEye = pad.position + padUp * 5f + outward * 6f;
                caption = "Roll over the key pads at the ends of the paths. Each one breaks a wall.";
                yield return MoveCamera(padEye, pad.position, 2.2f, 4.5f);
            }

            // Back to the energy ball for its warning.
            if (!introSkipped)
            {
                caption = "Watch out for the lava falling from above! Good luck!";
                yield return MoveCamera(orbEye, orb, 2.2f, 3f);
            }

            // Then straight back behind the ball, facing the walls.
            orbit.enabled = true;
            FaceTheWayOn();
            caption = null;
            introStartedAt = -1f;
            ball.SetFrozen(false);
            transitioning = false;
            arena.IntroDone = true;
        }

        /// <summary>
        /// Flies the camera to <paramref name="position"/> over <paramref name="move"/> seconds, turning to look at
        /// <paramref name="lookAt"/>, then holds there for <paramref name="hold"/>. It flies in an arc over the
        /// core (round the sphere, rising higher the farther it goes) rather than straight through it. Any button
        /// (after the first moment) skips the rest of the introduction.
        /// </summary>
        IEnumerator MoveCamera(Vector3 position, Vector3 lookAt, float move, float hold)
        {
            Transform view = cam.transform;
            Vector3 fromPos = view.position;
            Quaternion fromRot = view.rotation;
            float fromR = fromPos.magnitude, toR = position.magnitude;
            float lift = 3f + Vector3.Distance(fromPos, position) * 0.15f;
            for (float t = 0f; t < move + hold; t += Time.deltaTime)
            {
                if (IntroSkipPressed()) yield break;
                float k = move > 0f ? Mathf.SmoothStep(0f, 1f, t / move) : 1f;
                Vector3 dir = Vector3.Slerp(fromPos.normalized, position.normalized, k).normalized;
                Vector3 at = dir * (Mathf.Lerp(fromR, toR, k) + lift * Mathf.Sin(Mathf.PI * k));
                Quaternion look = Quaternion.LookRotation(lookAt - at, dir);
                view.SetPositionAndRotation(at, Quaternion.Slerp(fromRot, look, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 3f))));
                yield return null;
            }
        }

        /// <summary>Any button (not steering), once the introduction has been on a moment, skips the rest of it.</summary>
        bool IntroSkipPressed()
        {
            if (!introSkipped && Time.time - introStartedAt >= introSkipDelay && GameInput.ContinuePressed()) introSkipped = true;
            return introSkipped;
        }

        void ShowPoints(Vector3 position, int points, bool super, Color color) =>
            popups.Add(new Popup { Position = position, Points = points, BornAt = Time.time, Super = super, Color = color });

        /// <summary>
        /// Banks a section's points (once), and shows its score mid-screen with the world total counting up to
        /// include it; the corner totals catch up when that's done.
        /// </summary>
        void FinishSection(int index)
        {
            if (scores.Finish(index)) StartCoroutine(ShowTally(index));
        }

        IEnumerator ShowTally(int index)
        {
            while (tally != null) yield return null;
            // The section is already banked, so the world total counts up from what it was before it.
            var w = scores.Current;
            var mine = tally = new Tally
            {
                Section = index, Earned = w.Earned[index], Possible = w.Possible[index],
                WorldFrom = w.Banked - w.Earned[index], WorldTo = w.Banked,
                PossibleFrom = w.PossibleSoFar - w.Possible[index], PossibleTo = w.PossibleSoFar,
                StartedAt = Time.time,
            };
            yield return new WaitForSeconds(TallyEnd);
            // Unless a new run was started from the menu meanwhile.
            if (tally != mine) yield break;
            tally = null;
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
            // Dropping out of a section through its hole finishes it (a climb's hole only leads back down).
            if (best >= 0 && !holes[best].TwoWay) FinishSection(best);
        }

        void OnLaunched(BoostPad pad)
        {
            if (level == null) return;
            var holes = level.Layout.Holes;
            for (int i = 0; i < holes.Count; i++)
                if (holes[i].TwoWay && Vector3.Dot(holes[i].Direction, pad.Axis) > 0.9999f) climbingThrough = i;
            // Launching off a section's pad finishes it.
            if (climbingThrough >= 0) FinishSection(climbingThrough);
        }

        public void OnExitReached()
        {
            if (!transitioning) StartCoroutine(AdvanceWorld());
        }

        IEnumerator AdvanceWorld()
        {
            transitioning = true;
            // Let the last section's tally finish, then the score screens: the world's sections, then (after the
            // first world) the run's worlds. Each stays up a moment before any button moves on.
            ball.SetFrozen(true);
            while (tally != null) yield return null;
            for (int page = 1; page <= (scores.Worlds.Count > 1 ? 2 : 1); page++)
            {
                summaryPage = page;
                summaryShownAt = Time.time;
                yield return null;
                while (Time.time - summaryShownAt < summaryMinSeconds || !GameInput.ContinuePressed()) yield return null;
            }
            summaryPage = 0;
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
                int s = level.Layout.SectionAt(ball.CurrentLayer, ball.transform.position);
                if (s >= 0) section = s;
                // The core scores as one more section after the last.
                bool onCore = ball.CurrentLayer >= level.LayerCount - 1;
                if (onCore) section = level.Layout.Sections.Count;
                orbit.lift = onCore ? coreCameraLift : 0f;
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
            string where = section >= level.Layout.Sections.Count ? "The Core" : $"Section {section + 1}/{level.Layout.Sections.Count}";
            string status = $"World {world}   ·   {where}   ·   " +
                            $"{level.LayerName(layer)} ({layer + 1}/{level.LayerCount})   ·   {zone} zone";
            Shadowed(new Rect(16, 12, 900, 30), status, hudStyle);
            if (summaryPage > 0)
            {
                DrawSummary();
                return;
            }
            DrawScores();

            string help = GameInput.GamepadConnected
                ? "Left stick / D-pad move · Right stick camera · Select respawn"
                : Cursor.lockState == CursorLockMode.Locked
                ? "WASD move · Mouse/Q/E camera · R respawn · Esc free cursor · M menu"
                : "Click to capture mouse · WASD move · Q/E camera · R respawn · N skip world · M menu";
            Shadowed(new Rect(16, Screen.height - 36, 1000, 30), help, hudStyle);

            // Frame time and style, top right.
            string perf = $"{frameMs:F1} ms ({(frameMs > 0f ? 1000f / frameMs : 0f):F0} fps)   ·   {(style != null ? style.name : "")}";
            Shadowed(new Rect(Screen.width - 416, 12, 400, 30), perf, rightStyle ??= new GUIStyle(hudStyle) { alignment = TextAnchor.UpperRight });

            if (menuOpen)
            {
                DrawMenu();
                return;
            }
            DrawPopups();
            if (Time.time < messageUntil && !string.IsNullOrEmpty(message))
                Shadowed(new Rect(0, Screen.height * 0.18f, Screen.width, 60), message, bannerStyle);
            if (tally != null) DrawTally();
            if (thanksShownAt >= 0f) DrawThanks();
            if (introStartedAt >= 0f) DrawCaption();
        }

        /// <summary>What the energy ball is saying during its introduction, low in the middle of the screen.</summary>
        void DrawCaption()
        {
            if (string.IsNullOrEmpty(caption)) return;
            thanksStyle ??= new GUIStyle(bannerStyle) { fontSize = 40, wordWrap = true };
            promptStyle ??= new GUIStyle(hudStyle) { alignment = TextAnchor.MiddleCenter };
            promptStyle.fontSize = 26;
            float width = Mathf.Min(1300f, Screen.width * 0.94f), height = 190f;
            var box = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.68f, width, height);
            Panel(box, 0.75f);
            var speaker = new GUIStyle(promptStyle) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.6f, 0.97f, 1f) } };
            Shadowed(new Rect(box.x, box.y + 10f, width, 34f), "The energy ball", speaker);
            Shadowed(new Rect(box.x + 30f, box.y + 46f, width - 60f, 100f), caption, thanksStyle);
            if (Time.time - introStartedAt >= introSkipDelay)
                Shadowed(new Rect(box.x, box.yMax - 40f, width, 32f), "Press any button to skip", promptStyle);
        }

        /// <summary>The freed energy ball's thanks, mid-screen, until a button is pressed.</summary>
        void DrawThanks()
        {
            thanksTitleStyle ??= new GUIStyle(bannerStyle) { fontSize = 64, normal = { textColor = new Color(0.6f, 0.97f, 1f) } };
            thanksStyle ??= new GUIStyle(bannerStyle) { fontSize = 40, wordWrap = true };
            promptStyle ??= new GUIStyle(hudStyle) { alignment = TextAnchor.MiddleCenter };
            promptStyle.fontSize = 32;
            float t = Time.time - thanksShownAt;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(t / 0.5f));
            float width = Mathf.Min(1200f, Screen.width * 0.94f), height = 330f;
            var box = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.3f, width, height);
            Panel(box, 0.8f);
            Shadowed(new Rect(box.x, box.y + 30f, width, 80f), "Thank you for freeing me!", thanksTitleStyle);
            Shadowed(new Rect(box.x + 40f, box.y + 120f, width - 80f, 110f),
                "More of my kind are still trapped in other worlds.", thanksStyle);
            if (t >= thanksMinSeconds && Mathf.Repeat(Time.time, 1.2f) < 0.85f)
                Shadowed(new Rect(box.x, box.y + 250f, width, 60f), "Press any button to continue", promptStyle);
            GUI.color = old;
        }

        /// <summary>
        /// Points just scored: "+10" flashing as it floats up from where it was picked up and fades over a second.
        /// A super cube's is bigger, swells and shrinks, and flickers through magenta, white and gold.
        /// </summary>
        void DrawPopups()
        {
            popups.RemoveAll(p => Time.time - p.BornAt > PopupLife);
            if (popups.Count == 0 || cam == null) return;
            popupStyle ??= new GUIStyle(bannerStyle) { alignment = TextAnchor.MiddleCenter };
            foreach (var p in popups)
            {
                float age = (Time.time - p.BornAt) / PopupLife;
                Vector3 up = p.Position.normalized;
                Vector3 at = p.Position + up * ((p.Super ? 3.5f : 2.2f) * (1f - (1f - age) * (1f - age)));
                Vector3 screen = cam.WorldToScreenPoint(at);
                if (screen.z <= 0f) continue;
                bool flash = Mathf.Repeat(age * (p.Super ? 14f : 9f), 1f) < 0.5f;
                Color c = p.Color;
                if (p.Super)
                {
                    int phase = Mathf.FloorToInt(age * 14f) % 3;
                    c = phase == 0 ? new Color(1f, 0.2f, 0.85f) : phase == 1 ? Color.white : new Color(1f, 0.85f, 0.25f);
                    popupStyle.fontSize = Mathf.RoundToInt(120f * (1f + 0.18f * Mathf.Sin(age * Mathf.PI * 6f)) * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(age * 5f)));
                }
                else
                {
                    if (flash) c = Color.Lerp(c, Color.white, 0.7f);
                    popupStyle.fontSize = 72;
                }
                c.a = Mathf.Clamp01((1f - age) / 0.35f);
                popupStyle.normal.textColor = c;
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, c.a);
                Shadowed(new Rect(screen.x - 220f, Screen.height - screen.y - 80f, 440f, 160f), $"+{p.Points}", popupStyle);
                GUI.color = old;
            }
        }

        /// <summary>The three score lines in the top-left corner: the game and world as banked, the section live.</summary>
        void DrawScores()
        {
            scoreLabelStyle ??= new GUIStyle(hudStyle) { fontSize = 29, fontStyle = FontStyle.Bold };
            scoreValueStyle ??= new GUIStyle(scoreLabelStyle) { alignment = TextAnchor.UpperRight, normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
            var w = scores.Current;
            if (w == null) return;
            int s = Mathf.Clamp(section, 0, w.Earned.Length - 1);
            // While a finished section's tally plays, the world and game totals hold their old values; they catch
            // up when it's done.
            int worldGot = w.Banked, worldOf = w.PossibleSoFar, gameGot = scores.GameBanked, gameOf = scores.GamePossible;
            if (tally != null)
            {
                gameGot -= worldGot - tally.WorldFrom;
                gameOf -= worldOf - tally.PossibleFrom;
                worldGot = tally.WorldFrom;
                worldOf = tally.PossibleFrom;
            }
            var rows = new[]
            {
                ("Game", Scoreboard.Format(gameGot, gameOf)),
                ("World", Scoreboard.Format(worldGot, worldOf)),
                (s == w.CoreIndex ? "Core" : "Section", Scoreboard.Format(w.Earned[s], w.Possible[s])),
            };
            const float rowH = 42f;
            var box = new Rect(12, 46, 470, 20 + rows.Length * rowH);
            Panel(box, 0.55f);
            for (int i = 0; i < rows.Length; i++)
            {
                var r = new Rect(box.x + 18, box.y + 10 + i * rowH, box.width - 36, rowH);
                Shadowed(r, rows[i].Item1, scoreLabelStyle);
                Shadowed(r, rows[i].Item2, scoreValueStyle);
            }
        }

        /// <summary>A finished section's score mid-screen, then the world total counting up to include it.</summary>
        void DrawTally()
        {
            tallyTitleStyle ??= new GUIStyle(bannerStyle) { fontSize = 52 };
            tallyBigStyle ??= new GUIStyle(bannerStyle) { fontSize = 100, normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
            tallyWorldStyle ??= new GUIStyle(bannerStyle) { fontSize = 64 };
            float t = Time.time - tally.StartedAt;
            float alpha = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((TallyEnd - t) / TallyFade);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float y = Screen.height * 0.26f;
            Shadowed(new Rect(0, y, Screen.width, 72), $"Section {tally.Section + 1} complete", tallyTitleStyle);
            Shadowed(new Rect(0, y + 72, Screen.width, 128), Scoreboard.Format(tally.Earned, tally.Possible), tallyBigStyle);
            if (t >= TallyWorldAppears)
            {
                float u = Mathf.Clamp01((t - TallyWorldAppears - 0.3f) / TallyCountUp);
                u = 1f - (1f - u) * (1f - u);
                // The points and what was possible count up together, so the percentage moves smoothly from the old
                // one to the new; from nothing (the first section), what was possible is simply the section's.
                int shown = Mathf.RoundToInt(Mathf.Lerp(tally.WorldFrom, tally.WorldTo, u));
                int possible = tally.PossibleFrom == 0 ? tally.PossibleTo : Mathf.RoundToInt(Mathf.Lerp(tally.PossibleFrom, tally.PossibleTo, u));
                GUI.color = new Color(1f, 1f, 1f, alpha * Mathf.Clamp01((t - TallyWorldAppears) / 0.3f));
                Shadowed(new Rect(0, y + 216, Screen.width, 88), $"World   {Scoreboard.Format(shown, possible)}", tallyWorldStyle);
            }
            GUI.color = old;
        }

        /// <summary>
        /// The end-of-world screens, as bar graphs: each section's score (or, after the first world, each world's)
        /// as a bar as long as its percentage, with the world (or game) total as a taller bar at the bottom. The bars
        /// grow in one after another as the screen comes up.
        /// </summary>
        void DrawSummary()
        {
            var w = scores.Current;
            string heading, totalLabel;
            int totalGot, totalOf;
            var rows = new System.Collections.Generic.List<(string label, int got, int of, bool done)>();
            if (summaryPage == 1)
            {
                heading = $"World {w.World} complete";
                for (int i = 0; i < w.Earned.Length; i++) rows.Add((w.Label(i), w.Earned[i], w.Possible[i], w.Finished[i]));
                totalLabel = "World";
                totalGot = w.Banked;
                totalOf = w.PossibleSoFar;
            }
            else
            {
                heading = "Your run so far";
                foreach (var ws in scores.Worlds) rows.Add(($"World {ws.World}", ws.Banked, ws.PossibleSoFar, true));
                totalLabel = "Game";
                totalGot = scores.GameBanked;
                totalOf = scores.GamePossible;
            }

            // Laid out at full size, then scaled down to fit smaller screens.
            float rowH = 60f, totalH = 84f, titleH = 110f, promptH = 70f, pad = 40f, width = 1300f;
            float fullH = titleH + rows.Count * rowH + 24f + totalH + promptH + pad;
            float k = Mathf.Min(1f, Screen.height * 0.94f / fullH, Screen.width * 0.94f / width);
            rowH *= k; totalH *= k; titleH *= k; promptH *= k; pad *= k; width *= k;
            float h = fullH * k;
            var box = new Rect((Screen.width - width) * 0.5f, (Screen.height - h) * 0.5f, width, h);
            Panel(box, 0.88f);

            summaryRowStyle ??= new GUIStyle(hudStyle) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            summaryValueStyle ??= new GUIStyle(summaryRowStyle);
            summaryTotalStyle ??= new GUIStyle(bannerStyle);
            promptStyle ??= new GUIStyle(hudStyle) { alignment = TextAnchor.MiddleCenter };
            summaryRowStyle.fontSize = Mathf.RoundToInt(36 * k);
            summaryValueStyle.fontSize = Mathf.RoundToInt(32 * k);
            summaryTotalStyle.fontSize = Mathf.RoundToInt(68 * k);
            promptStyle.fontSize = Mathf.RoundToInt(32 * k);

            float t = Time.time - summaryShownAt;
            float y = box.y + pad * 0.5f;
            Shadowed(new Rect(box.x, y, width, titleH * 0.8f), heading, summaryTotalStyle);
            y += titleH;
            float labelW = 260f * k, left = box.x + pad, trackX = left + labelW, trackW = box.xMax - pad - trackX;

            // How far a bar has grown in: one after another, then the total.
            float Grow(int index) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.25f - index * 0.12f) / 0.7f));
            for (int i = 0; i < rows.Count; i++)
            {
                var (label, got, of, done) = rows[i];
                var rr = new Rect(left, y, box.width, rowH);
                var old = GUI.color;
                if (!done) GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Shadowed(new Rect(left, y, labelW, rowH), label, summaryRowStyle);
                var track = new Rect(trackX, y + rowH * 0.12f, trackW, rowH * 0.76f);
                Bar(track, done ? (of > 0 ? (float)got / of : 0f) * Grow(i) : 0f, false);
                string text = done ? Scoreboard.Format(got, of) : $"not finished   ({of} possible)";
                Shadowed(new Rect(track.x + 14f * k, track.y, track.width, track.height), text, summaryValueStyle);
                GUI.color = old;
                y += rowH;
            }
            y += 24f * k;
            // The total: taller, brighter, last to fill.
            var tr = new Rect(trackX, y, trackW, totalH);
            Shadowed(new Rect(left, y, labelW, totalH), totalLabel, new GUIStyle(summaryRowStyle) { fontSize = Mathf.RoundToInt(44 * k) });
            Bar(tr, (totalOf > 0 ? (float)totalGot / totalOf : 0f) * Grow(rows.Count + 1), true);
            Shadowed(new Rect(tr.x + 16f * k, tr.y, tr.width, tr.height), Scoreboard.Format(totalGot, totalOf),
                new GUIStyle(summaryValueStyle) { fontSize = Mathf.RoundToInt(44 * k) });
            y += totalH + 10f * k;

            bool ready = t >= summaryMinSeconds;
            if (ready && Mathf.Repeat(Time.time, 1.2f) < 0.85f)
            {
                bool more = summaryPage == 1 && scores.Worlds.Count > 1;
                Shadowed(new Rect(box.x, y, width, promptH * 0.7f), more ? "Press any button to continue" : "Press any button to start the next world", promptStyle);
            }
        }

        /// <summary>
        /// A horizontal bar filled to <paramref name="fill"/> (0-1) of its track: red when low, gold in the middle,
        /// green when high, with a lighter strip along its top. The total's bar glows a brighter gold.
        /// </summary>
        static void Bar(Rect track, float fill, bool total)
        {
            Panel(track, 0.55f, new Color(0.25f, 0.22f, 0.35f));
            fill = Mathf.Clamp01(fill);
            if (fill <= 0f) return;
            Color c = fill < 0.5f
                ? Color.Lerp(new Color(0.9f, 0.2f, 0.25f), new Color(1f, 0.75f, 0.15f), fill * 2f)
                : Color.Lerp(new Color(1f, 0.75f, 0.15f), new Color(0.3f, 0.9f, 0.35f), (fill - 0.5f) * 2f);
            if (total) c = Color.Lerp(c, new Color(1f, 0.85f, 0.3f), 0.35f);
            var r = new Rect(track.x, track.y, track.width * fill, track.height);
            Panel(r, total ? 0.95f : 0.85f, c);
            Panel(new Rect(r.x, r.y, r.width, r.height * 0.22f), 0.35f, Color.white);
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

        /// <summary>A plain dark (or <paramref name="tint"/>) panel, see-through by <paramref name="alpha"/>.</summary>
        static void Panel(Rect r, float alpha, Color? tint = null)
        {
            var old = GUI.color;
            var c = tint ?? new Color(0.02f, 0.01f, 0.05f);
            GUI.color = new Color(c.r, c.g, c.b, alpha * old.a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
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
