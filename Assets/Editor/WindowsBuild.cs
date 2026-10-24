using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SphereDescent.EditorTools
{
    /// <summary>
    /// Builds the game for 64-bit Windows into Builds/Windows/ and zips it (Builds/SphereDescent-Windows.zip) to
    /// copy to a Windows machine: unzip anywhere and run SphereDescent.exe. Needs Unity's "Windows Build Support
    /// (Mono)" module (a Mac can only make Mono builds for Windows). The usual pre-build step (BuildShaders) runs
    /// too. From the Sphere Descent menu, or in batch mode with -executeMethod.
    /// </summary>
    public static class WindowsBuild
    {
        const string Folder = "Builds/Windows";
        const string Zip = "Builds/SphereDescent-Windows.zip";

        [MenuItem("Sphere Descent/Build Windows (zip)")]
        public static void Build()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[WindowsBuild] Windows build support isn't installed: add \"Windows Build Support (Mono)\" " +
                               "to this Unity version in Unity Hub.");
                return;
            }
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = Path.Combine(Folder, "SphereDescent.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[WindowsBuild] build {report.summary.result} with {report.summary.totalErrors} errors");
                return;
            }
            // Unity's debugging symbols aren't needed to play; leave them out of the zip.
            foreach (var extra in Directory.GetDirectories(Folder, "*_BurstDebugInformation_DoNotShip"))
                Directory.Delete(extra, true);
            foreach (var extra in Directory.GetDirectories(Folder, "*_BackUpThisFolder_ButDontShipItWithYourGame"))
                Directory.Delete(extra, true);
            if (File.Exists(Zip)) File.Delete(Zip);
            ZipFile.CreateFromDirectory(Folder, Zip, System.IO.Compression.CompressionLevel.Optimal, false);
            Debug.Log($"[WindowsBuild] built in {report.summary.totalTime:mm\\:ss}, zipped to {Path.GetFullPath(Zip)} " +
                      $"({new FileInfo(Zip).Length / 1048576} MB)");
            EditorUtility.RevealInFinder(Zip);
        }
    }
}
