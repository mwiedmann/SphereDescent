using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SphereDescent.EditorTools
{
    /// <summary>Renders Deep Sea floors in one zone of each pattern, with the geometric and the organic set.</summary>
    public static class PatternShot
    {
        public static void Run()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PREVIEW_DIR");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var gm = Object.FindAnyObjectByType<GameManager>();
            gm.materials.EnsureDefaults();
            var layout = MazeLayout.Generate(gm.settings, gm.SeedFor(2));
            var cam = Camera.main;
            cam.farClipPlane = 1000f;
            foreach (var set in new[] { PatternSet.Geometric, PatternSet.Organic })
            {
                var style = VisualStyle.DeepSea();
                style.patternSet = set;
                var level = LevelBuilder.Build(layout, gm.settings, gm.materials, 2, style);
                GameManager.ApplyAtmosphere(style, null, gm.sun);
                level.SetVisibleLayers(0); level.SetShadowLayer(0);
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = style.innerBackground;
                float r = gm.settings.shellRadii[0];
                var shown = new bool[4];
                var grid = layout.Grids[0];
                // A route cell in a zone of each pattern.
                for (int sec = 0; sec < layout.Sections.Count; sec++)
                {
                    if (layout.Sections[sec].Shell != 0) continue;
                    foreach (int cell in layout.Route(sec))
                    {
                        Vector3 d = grid.CellCenters[cell];
                        int zone = level.Zones.ZoneAt(d * r);
                        if (shown[zone % 4]) continue;
                        // Well inside the zone, not on its border.
                        if (Vector3.Angle(d, level.Zones.Directions[zone]) > 14f) continue;
                        shown[zone % 4] = true;
                        Vector3 side = Vector3.Cross(d, Vector3.up).normalized;
                        Vector3 eye = d * (r + 9f) + side * 9f;
                        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(d * r - eye, d));
                        GameManager.UpdateSky(gm.sun, d);
                        var rt = new RenderTexture(960, 540, 24) { antiAliasing = 8 };
                        cam.targetTexture = rt; cam.Render(); cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
                        File.WriteAllBytes(Path.Combine(outDir, $"{set}_{zone % 4}.png"), tex.EncodeToPNG());
                        cam.targetTexture = null; RenderTexture.active = null;
                    }
                }
                Object.DestroyImmediate(level.gameObject);
            }
            Debug.Log("[PatternShot] done");
        }
    }
}
