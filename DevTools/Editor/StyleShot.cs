using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SphereDescent.EditorTools
{
    public static class StyleShot
    {
        public static void Run()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PREVIEW_DIR");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var gm = Object.FindAnyObjectByType<GameManager>();
            var style = VisualStyle.DeepSea();
            var layout = MazeLayout.Generate(gm.settings, gm.baseSeed + 2 * 7919);
            var level = LevelBuilder.Build(layout, gm.settings, gm.materials, 2, style);
            var sky = new Material(RenderSettings.skybox);
            RenderSettings.skybox = sky;
            GameManager.ApplyAtmosphere(style, sky, gm.sun);
            var cam = Camera.main;
            var grid = layout.Grids[0];
            float r0 = gm.settings.shellRadii[0];
            var route = layout.Route(0);
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.GetComponent<MeshRenderer>().sharedMaterial = gm.materials.ball;
            Physics.SyncTransforms();
            float Ground(Vector3 d) { foreach (var h in Physics.RaycastAll(d.normalized * (r0 + 30f), -d.normalized, 40f)) if (h.collider.name == "Shell") return h.point.magnitude; return r0; }
            level.SetVisibleLayers(0); level.SetShadowLayer(0); level.SetBeamSection(0);
            Vector3 c = grid.CellCenters[route[2]], n = grid.CellCenters[route[3]];
            Vector3 fwd = Vector3.ProjectOnPlane(n - c, c).normalized;
            Vector3 bp = c * (Ground(c) + 0.5f);
            Vector3 eyeDir = (c * r0 - fwd * 7f).normalized;
            Vector3 eye = eyeDir * (Ground(eyeDir) + 4.5f);
            ball.transform.position = bp;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(bp + c + fwd * 6f - eye, c));
            GameManager.UpdateSky(gm.sun, c);
            var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 8 };
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "deep_0.png"), tex.EncodeToPNG());
            Debug.Log("[StyleShot] done");
        }
    }
}
