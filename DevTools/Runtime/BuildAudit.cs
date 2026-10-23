using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// In a build started with -auditshots (AUDIT_DIR set): saves pictures of the things that only break in
    /// builds (glass cube faces, metal, the core's see-through walls, a foggy style), then quits.
    /// </summary>
    public class BuildAudit : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-auditshots") < 0) return;
            new GameObject("Build Audit").AddComponent<BuildAudit>();
        }

        string dir;

        void Shot(string name, Vector3 eye, Vector3 at, Vector3 up)
        {
            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, up));
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
        }

        IEnumerator Start()
        {
            dir = System.Environment.GetEnvironmentVariable("AUDIT_DIR") ?? "/tmp/audit";
            Directory.CreateDirectory(dir);
            var gm = GameManager.Instance;
            var ball = FindAnyObjectByType<BallController>();
            var orbit = Camera.main.GetComponent<OrbitCamera>();
            yield return new WaitForSeconds(1f);
            while (gm.Arriving) yield return null;
            ball.SetFrozen(true);
            orbit.enabled = false;

            // An energy cube (glass faces) and a security camera (metal), close up.
            // One on the surface (the layer in view), nearest the start.
            EnergyCube cube = null;
            var start = FindAnyObjectByType<LevelInstance>().SpawnPosition;
            foreach (var c in FindObjectsByType<EnergyCube>(FindObjectsInactive.Include))
                if (c.transform.position.magnitude > 75f && (cube == null || Vector3.Distance(c.transform.position, start) < Vector3.Distance(cube.transform.position, start))) cube = c;
            cube.gameObject.SetActive(true);
            Vector3 cu = cube.transform.position.normalized;
            Vector3 side = Vector3.Cross(cu, Vector3.up).normalized;
            ball.Teleport(cube.transform.position + cu * 6f - side * 8f); // out of the way, so it doesn't collect it
            yield return new WaitForSeconds(1.5f);
            Shot("cube", cube.transform.position + cu * 1.2f + side * 2.2f, cube.transform.position + cu * 0.6f, cu);
            var camera = FindAnyObjectByType<SecurityCamera>(FindObjectsInactive.Include);
            Vector3 su = camera.transform.position.normalized;
            ball.Teleport(camera.transform.position + su * 1f);
            yield return new WaitForSeconds(1.5f);
            Shot("security_camera", camera.transform.position + su * 4f + Vector3.Cross(su, Vector3.up).normalized * 5f, camera.transform.position + su * 2.5f, su);

            // The core's walls.
            var level = FindAnyObjectByType<LevelInstance>();
            var core = level.Layout.Core;
            ball.Teleport(core.Landing * (core.SurfaceRadius(core.Landing) + 0.6f));
            yield return new WaitForSeconds(2f);
            Vector3 cg = core.Cage, cs = Vector3.ProjectOnPlane(core.Landing - cg, cg).normalized;
            Shot("core_walls", cg * (core.GroundRadius + 9f) + cs * 16f, cg * core.GroundRadius, cg);

            // A foggy style: a new run in Deep Sea.
            orbit.enabled = true;
            ball.SetFrozen(false);
            typeof(GameManager).GetMethod("StartWorld", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, new object[] { 1, 1 });
            yield return null;
            while (gm.Arriving) yield return null;
            yield return new WaitForSeconds(1f);
            var cam = Camera.main.transform;
            Shot("deep_sea_fog", cam.position, cam.position + cam.forward, ball.transform.position.normalized);
            Debug.Log($"[Audit] saved, fog {RenderSettings.fog} {RenderSettings.fogDensity}");
            Application.Quit();
        }
    }
}
