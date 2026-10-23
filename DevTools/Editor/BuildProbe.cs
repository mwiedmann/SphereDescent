using UnityEditor;
using UnityEngine;
namespace SphereDescent.EditorTools
{
    public static class BuildProbe
    {
        public static void Build()
        {
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64;
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" },
                System.Environment.GetEnvironmentVariable("BUILD_OUT"), BuildTarget.StandaloneOSX, BuildOptions.None);
            Debug.Log($"[BuildProbe] result {report.summary.result}, errors {report.summary.totalErrors}, size {report.summary.totalSize / 1048576} MB, time {report.summary.totalTime}");
        }
    }
}
