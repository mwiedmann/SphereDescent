using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SphereDescent.EditorTools
{
    [InitializeOnLoad]
    public static class PlayProbeRunner
    {
        static PlayProbeRunner()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("probe", false))
                    new GameObject("Probe").AddComponent<PlayProbe>();
            };
            EditorApplication.update += () =>
            {
                if (PlayProbe.Done) { PlayProbe.Done = false; EditorApplication.Exit(0); }
            };
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool("probe", true);
            EditorApplication.isPlaying = true;
        }
    }
}
