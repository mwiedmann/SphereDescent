using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SphereDescent.EditorTools
{
    public static class CoreShot
    {
        public static void Run()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PREVIEW_DIR");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var gm = Object.FindAnyObjectByType<GameManager>();
            gm.materials.EnsureDefaults();
            int world = int.Parse(System.Environment.GetEnvironmentVariable("WORLD") ?? "1");
            var t0 = Time.realtimeSinceStartup;
            var layout = MazeLayout.Generate(gm.settings, gm.SeedFor(world));
            var style = world % 2 == 1 ? VisualStyle.Synthwave() : VisualStyle.DeepSea();
            var level = LevelBuilder.Build(layout, gm.settings, gm.materials, world, style);
            Debug.Log($"[CoreShot] built in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
            GameManager.ApplyAtmosphere(style, null, gm.sun);
            var core = layout.Core;
            int L = layout.ShellCount;
            level.SetVisibleLayers(L); level.SetShadowLayer(L);
            var cam = Camera.main;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = style.innerBackground;
            cam.farClipPlane = 1000f;
            float G = core.GroundRadius;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.GetComponent<MeshRenderer>().sharedMaterial = gm.materials.ball;
            Vector3 L0 = core.Landing;
            ball.transform.position = L0 * (core.SurfaceRadius(L0) + 0.5f);
            void Shot(string name, Vector3 eye, Vector3 target, Vector3 up)
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, up));
                GameManager.UpdateSky(gm.sun, up.normalized);
                var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 8 };
                cam.targetTexture = rt; cam.Render(); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                cam.targetTexture = null; RenderTexture.active = null;
            }
            foreach (var sw in level.GetComponentsInChildren<LaserSwitch>())
                if (sw.arena != null) sw.cable.SetTriangles(sw.cableTriangles, 0);
            Vector3 fwd = core.FacingFrom(ball.transform.position);
            Shot("1_overview_landing", L0 * 60f, Vector3.zero, fwd);
            Shot("2_overview_cage", core.Cage * 60f, Vector3.zero, Vector3.Cross(core.Cage, Vector3.right));
            Vector3 bp = ball.transform.position;
            Shot("3_player_view", bp + L0 * 5f - fwd * 8f, bp + L0 * 1f + fwd * 4f, L0);
            Vector3 cu = core.Cage, cside = Vector3.ProjectOnPlane(core.Landing - cu, cu).normalized;
            Shot("4_cage", cu * (G + 9f) + cside * 16f, cu * G, cu);
            for (int p = 0; p < 2; p++)
            {
                Vector3 d = core.Pads[p];
                Vector3 side = Vector3.ProjectOnPlane(core.Cage - d, d).normalized;
                Shot($"5_pad{p}", d * (G + 5f) - side * 8f, d * G + side * 3f, d);
            }
            Shot("6_far", -core.Cage * 60f, Vector3.zero, Vector3.Cross(core.Cage, Vector3.right));
            Debug.Log($"[CoreShot] pads {core.Pads.Length} cubes {core.Cubes.Count} paths {core.Paths.Count} done");
        }
    }
}
