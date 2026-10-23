using System.Collections;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>In a build started with -perfprobe: measures the frame rate at each graphics level, then quits.</summary>
    public class BuildPerf : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-perfprobe") < 0) return;
            new GameObject("Build Perf").AddComponent<BuildPerf>();
        }

        IEnumerator Start()
        {
            var gm = GameManager.Instance;
            yield return new WaitForSeconds(1f);
            while (gm.Arriving) yield return null;
            string report = $"[Perf] screen {Screen.width}x{Screen.height}";
            foreach (var level in new[] { GraphicsLevel.High, GraphicsLevel.Medium, GraphicsLevel.Low })
            {
                GraphicsQuality.Apply(level, gm.sun, FindAnyObjectByType<UnityEngine.Rendering.Volume>());
                yield return new WaitForSeconds(2f);
                int frames = 0; float start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < 5f) { frames++; yield return null; }
                report += $"  {level}: {frames / (Time.realtimeSinceStartup - start):F1} fps";
            }
            GraphicsQuality.Apply(GraphicsLevel.High, gm.sun, FindAnyObjectByType<UnityEngine.Rendering.Volume>());
            Debug.Log(report);
            Application.Quit();
        }
    }
}
