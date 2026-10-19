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
        public Color skyColor = new Color(0.55f, 0.72f, 0.92f);
        [Tooltip("Each world's seed is derived from this, so the same number always gives the same worlds.")]
        public int baseSeed = 1337;
        public int startWorld = 1;
        public float worldTransitionDelay = 2.5f;
        public Light sun;

        LevelInstance level;
        BallController ball;
        OrbitCamera orbit;
        int world;
        bool transitioning;
        string message;
        float messageUntil;
        GUIStyle hudStyle, bannerStyle;

        public int World => world;

        void Awake()
        {
            Instance = this;
            materials.EnsureDefaults();
            if (sun == null) sun = RenderSettings.sun;
        }

        void Start()
        {
            world = Mathf.Max(1, startWorld);
            CreatePlayer();
            BuildWorld();
        }

        void CreatePlayer()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Player Ball";
            go.layer = 2; // Ignore Raycast, so the camera's collision probe skips the ball
            go.GetComponent<MeshRenderer>().sharedMaterial = materials.ball;
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
            ball = go.AddComponent<BallController>();

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            // Unity's procedural skybox draws its horizon relative to world up, which looks wrong on the
            // sides of a planet, so use a plain sky color instead.
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = skyColor;
            orbit = cam.GetComponent<OrbitCamera>();
            if (orbit == null) orbit = cam.gameObject.AddComponent<OrbitCamera>();
            orbit.target = ball.transform;
            ball.cameraTransform = cam.transform;
        }

        void BuildWorld()
        {
            if (level != null) Destroy(level.gameObject);

            float t0 = Time.realtimeSinceStartup;
            var layout = MazeLayout.Generate(settings, baseSeed + world * 7919);
            level = LevelBuilder.Build(layout, settings, materials, world);
            Debug.Log($"World {world}: seed {layout.Seed}, layout found after {layout.AttemptsUsed} attempts, " +
                      $"shortest route {layout.ShortestRouteMoves} transitions ({layout.ShortestRouteUps} launches), " +
                      $"built in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");

            ball.level = level;
            ball.Teleport(level.SpawnPosition);
            orbit.Snap();
            ShowMessage($"World {world}", 3f);
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
            if (level != null && ball != null)
            {
                level.SetShadowLayer(ball.CurrentLayer);
                // Use the outermost of the ball's and camera's layers, so nothing vanishes while the camera is
                // still following the ball down through a hole.
                int cameraLayer = level.LayerAt(orbit.transform.position.magnitude);
                level.SetVisibleLayers(Mathf.Min(ball.CurrentLayer, cameraLayer));
            }
        }

        void LateUpdate()
        {
            // Keep the sun overhead wherever the ball is, so no part of the sphere is stuck in night.
            if (sun == null || ball == null) return;
            Vector3 up = ball.transform.position.normalized;
            Vector3 tilt = Vector3.ProjectOnPlane(new Vector3(0.35f, 0.2f, 1f), up);
            if (tilt.sqrMagnitude < 0.01f) tilt = Vector3.ProjectOnPlane(Vector3.right, up);
            Vector3 lightDir = -(up + tilt.normalized * 0.55f).normalized;
            sun.transform.rotation = Quaternion.LookRotation(lightDir, tilt);
        }

        void ShowMessage(string text, float seconds)
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
            string status = $"World {world}   ·   {level.LayerName(layer)} ({layer + 1}/{level.LayerCount})   ·   {zone} zone";
            if (ball.OnPad) status += "   ·   Boost pad: press Jump!";
            Shadowed(new Rect(16, 12, 900, 30), status, hudStyle);

            string help = Cursor.lockState == CursorLockMode.Locked
                ? "WASD move · Space jump · Mouse/Q/E camera · R respawn · Esc free cursor"
                : "Click to capture mouse · WASD move · Space jump · Q/E camera · R respawn · N skip world";
            Shadowed(new Rect(16, Screen.height - 36, 1000, 30), help, hudStyle);

            if (Time.time < messageUntil && !string.IsNullOrEmpty(message))
                Shadowed(new Rect(0, Screen.height * 0.25f, Screen.width, 60), message, bannerStyle);
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
