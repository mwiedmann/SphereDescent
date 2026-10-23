using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SphereDescent.EditorTools
{
    public static class AcidShot
    {
        public static void Run()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PREVIEW_DIR");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var gm = Object.FindAnyObjectByType<GameManager>();
            var layout = MazeLayout.Generate(gm.settings, gm.baseSeed + 7919);
            var level = LevelBuilder.Build(layout, gm.settings, gm.materials, 1);
            var cam = Camera.main;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.GetComponent<MeshRenderer>().sharedMaterial = gm.materials.ball;
            for (int i = 0; i < layout.AcidPools.Count; i++)
            {
                var p = layout.AcidPools[i];
                level.SetVisibleLayers(p.Shell); level.SetShadowLayer(p.Shell); level.SetBeamSection(p.Section);
                cam.clearFlags = p.Shell > 0 ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox; cam.backgroundColor = Color.black;
                var grid = layout.Grids[p.Shell];
                float r = gm.settings.shellRadii[p.Shell];
                Vector3 c = grid.CellCenters[p.Cell];
                var e = grid.Edges[grid.EdgeBetween(p.Cell, p.From)];
                Vector3 door = (e.P0 + e.P1).normalized;
                Vector3 back = Vector3.ProjectOnPlane(grid.CellCenters[p.From] - c, c).normalized;
                ball.transform.position = (door * r + back * 0.5f).normalized * (r + 0.52f);
                void Shot(string name, Vector3 eye, Vector3 target)
                {
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, c));
                    GameManager.UpdateSky(gm.sun, c);
                    var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 8 };
                    cam.targetTexture = rt; cam.Render(); cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                    cam.targetTexture = null; RenderTexture.active = null;
                }
                Shot($"acid_{i}_topmid", c * (r + 13f) + back * 0.01f, c * r);
            }
            Debug.Log("[AcidShot] done");
        }
    }
}
