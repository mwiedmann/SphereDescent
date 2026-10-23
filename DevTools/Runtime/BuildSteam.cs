using System.Collections;
using System.IO;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>In a build started with -steamshot: saves a picture of an acid pool's steam, then quits.</summary>
    public class BuildSteam : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-steamshot") < 0) return;
            new GameObject("Build Steam").AddComponent<BuildSteam>();
        }

        IEnumerator Start()
        {
            var gm = GameManager.Instance;
            yield return new WaitForSeconds(1f);
            while (gm.Arriving) yield return null;
            var pool = FindAnyObjectByType<AcidPool>(FindObjectsInactive.Include);
            var ball = FindAnyObjectByType<BallController>();
            Vector3 c = pool.transform.position;
            ball.Teleport(c.normalized * (c.magnitude + 12f) + Vector3.Cross(c.normalized, Vector3.up).normalized * 6f);
            ball.SetFrozen(true);
            yield return new WaitForSeconds(4f);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            Vector3 up = c.normalized;
            Vector3 eye = c + up * 9f + Vector3.Cross(up, Vector3.up).normalized * 10f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c + up * 1.5f - eye, up));
            yield return null;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            File.WriteAllBytes(System.Environment.GetEnvironmentVariable("STEAM_SHOT") ?? "/tmp/steam.png", tex.EncodeToPNG());
            Debug.Log("[Steam] saved");
            Application.Quit();
        }
    }
}
