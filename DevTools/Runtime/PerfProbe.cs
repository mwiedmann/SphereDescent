using System.Collections;
using System.Linq;
using UnityEngine;

namespace SphereDescent
{
    public class PlayProbe : MonoBehaviour
    {
        public static bool Done;

        IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var cam = Camera.main;
            cam.targetTexture = new RenderTexture(1920, 1080, 24);
            yield return new WaitForSeconds(2f);
            var level = FindAnyObjectByType<LevelInstance>();
            var renderers = level.GetComponentsInChildren<Renderer>(true);
            int drawn = renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy);
            long tris = 0;
            foreach (var r in renderers)
                if (r.enabled && r.gameObject.activeInHierarchy && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                    tris += mf.sharedMesh.GetIndexCount(0) / 3;
            var behaviours = level.GetComponentsInChildren<MonoBehaviour>(false).Where(b => b.isActiveAndEnabled).ToArray();
            var visible = 0;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            foreach (var r in renderers) if (r.enabled && r.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(planes, r.bounds)) visible++;
            float t = 0f; int n = 0; float cpu = 0f;
            var sw = new System.Diagnostics.Stopwatch();
            while (n < 300) { yield return null; t += Time.unscaledDeltaTime; n++; }
            Debug.Log($"[Perf] objects in world: {level.GetComponentsInChildren<Transform>(true).Length}; renderers {renderers.Length}, enabled+active {drawn} ({tris / 1000}k tris), in view {visible}; " +
                      $"scripts running {behaviours.Length} ({behaviours.Count(b => b is Mine)} mines, {behaviours.Count(b => b is EnergyCube)} cubes, {behaviours.Count(b => b is SecurityCamera)} cameras); " +
                      $"frame {t / n * 1000f:F2} ms");
            Done = true;
        }
    }
}
