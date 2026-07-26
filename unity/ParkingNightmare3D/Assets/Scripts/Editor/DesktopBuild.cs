using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PN3D.EditorTools
{
    /// <summary>
    /// Windows standalone player build, used as a fast diagnostic loop for the mobile port.
    ///
    /// A Windows player takes about two minutes where an Android IL2CPP build takes twelve
    /// to eighteen, and it reproduces the differences that actually bite: shader variant
    /// stripping, engine code stripping, Resources-only data loading, and runtime-created
    /// materials with no material assets for the stripper to learn from. Crucially it also
    /// writes a real Player.log — the test phone runs ColorOS, which drops third-party app
    /// output from logcat entirely, so on-device logging is not available at all.
    ///
    ///   Unity.exe -quit -batchmode -projectPath &lt;proj&gt; \
    ///             -executeMethod PN3D.EditorTools.DesktopBuild.BuildWindows -logFile -
    ///
    /// Not a shipping target. It exists so a device bug can be reproduced in two minutes.
    /// </summary>
    public static class DesktopBuild
    {
        [MenuItem("PN3D/Build Windows player (diagnostic)")]
        public static void BuildWindows()
        {
            DataSync.Sync(false);
            AndroidBuild.AssertShadersIncluded();

            var scenes = EditorBuildSettings.scenes
                                            .Where(s => s.enabled)
                                            .Select(s => s.path)
                                            .ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[PN3D] no enabled scenes in build settings");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            string dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName,
                                      "Build", "Windows");
            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, "ParkingNightmare3D.exe");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[PN3D] could not switch to StandaloneWindows64");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                // Development so Debug.Log reaches Player.log with stack traces.
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            });

            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[PN3D] build {s.result} with {s.totalErrors} error(s)");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[PN3D] BUILD OK  {outPath}  in {s.totalTime.TotalMinutes:F1} min");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
