using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace PN3D.EditorTools
{
    /// <summary>
    /// Android player settings and the player build, both scripted rather than clicked.
    ///
    /// Scripted because a store build has a dozen settings that silently produce a
    /// rejected or badly-performing binary if any one of them is wrong, and because the
    /// project arrived carrying the URP template's defaults — ARMv7-only, a
    /// com.UnityTechnologies.* package id, a debug keystore. Everything Play cares about
    /// is asserted here in one place where it can be reviewed in a diff.
    ///
    /// Batch usage:
    ///   Unity.exe -quit -batchmode -nographics -projectPath &lt;proj&gt; \
    ///             -executeMethod PN3D.EditorTools.AndroidBuild.BuildApk -logFile -
    ///
    /// Unlike the play-mode capture in PlayCapture.cs, a player build is perfectly happy
    /// in -batchmode: it never enters play mode, so it never hits the assembly reload
    /// that wedges headless Unity.
    /// </summary>
    public static class AndroidBuild
    {
        // Reverse-DNS on a domain the developer controls. PERMANENT once the first build
        // is uploaded to Play — Google keys the store listing to it and it can never be
        // changed, so it must be settled before the account exists, not after.
        public const string PackageId = "com.krishnaladha.parkingnightmare3d";
        public const string Company   = "Krishna Ladha";
        public const string Product   = "Parking Nightmare 3D";

        // Environment variables, so a real signing password never lands in the repo.
        // Absent, the build falls back to Unity's debug keystore, which installs on a
        // device over adb but is rejected by Play.
        const string KeystoreEnv  = "PN3D_KEYSTORE";
        const string StorePassEnv = "PN3D_KEYSTORE_PASS";
        const string AliasEnv     = "PN3D_KEYALIAS";
        const string AliasPassEnv = "PN3D_KEYALIAS_PASS";

        [MenuItem("PN3D/Android/Apply player settings")]
        public static void ApplyPlayerSettings()
        {
            var android = UnityEditor.Build.NamedBuildTarget.Android;

            PlayerSettings.companyName = Company;
            PlayerSettings.productName = Product;
            PlayerSettings.SetApplicationIdentifier(android, PackageId);

            // 64-bit is mandatory on Play and has been since 2019, and ARM64 forces
            // IL2CPP. Shipping ARMv7 as well would roughly double both IL2CPP build time
            // and binary size to serve a device population that is now vanishingly small.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);

            // API 24 (Android 7.0) — below this Vulkan support is too patchy to rely on
            // and the remaining device share does not justify a GLES-only fallback path.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Landscape only. The chase camera, the HUD layout and tilt steering all
            // assume it, and letting the device rotate to portrait mid-mission would
            // reframe the parking approach the player is judging by eye.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            // Frame pacing asks the driver to hold a steady presentation cadence rather
            // than let frames bunch. On a game judged by how a car feels, a steady 45 is
            // worth more than a 60 that stutters.
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = false;

            ApplyIcons(android);
            ApplyKeystore();
            AssertShadersIncluded();

            AssetDatabase.SaveAssets();
            Debug.Log($"[PN3D] android player settings applied — {PackageId} / " +
                      $"{PlayerSettings.Android.targetArchitectures} / " +
                      $"{PlayerSettings.GetScriptingBackend(android)} / " +
                      $"min {PlayerSettings.Android.minSdkVersion}");
        }

        /// <summary>
        /// Reuse the PWA icons the web build already ships, so the two stores and the
        /// browser install all show the same artwork from one source.
        /// </summary>
        static void ApplyIcons(UnityEditor.Build.NamedBuildTarget android)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icons/AppIcon.png");
            if (icon == null)
            {
                Debug.LogWarning("[PN3D] Assets/Icons/AppIcon.png missing; keeping the default icon");
                return;
            }

            // The legacy icon set, not adaptive/round. AndroidPlatformIconKind lives in
            // the Android editor extension assembly, which this asmdef would have to take
            // a reference on — a build script that stops compiling when someone opens the
            // project without the Android module installed is a bad trade for a rounder
            // icon. Revisit when there is real store artwork with a separate foreground
            // and background layer to place.
            int n = PlayerSettings.GetIconSizes(android, IconKind.Application).Length;
            if (n == 0) return;

            var set = new Texture2D[n];
            for (int i = 0; i < n; i++) set[i] = icon;   // Unity downscales per slot
            PlayerSettings.SetIcons(android, set, IconKind.Application);
        }

        /// <summary>
        /// Every shader this game uses is reached only through Shader.Find, because every
        /// material is created at runtime by MatLib and there is not one material asset in
        /// the project. A player build ships the shaders that assets reference, so without
        /// the Always Included Shaders list it ships none of them.
        ///
        /// This is not hypothetical: the first Android build did exactly that. URP/Lit
        /// resolved to null, `new Material(null)` threw inside BuildGround, and the app ran
        /// at a locked 60fps showing Unity's default skybox and nothing else. The list
        /// lives in ProjectSettings/GraphicsSettings.asset; this fails the build loudly if
        /// it is ever undone, because the symptom appears nowhere near the cause.
        /// </summary>
        internal static void AssertShadersIncluded()
        {
            var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            var have = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue is Shader s)
                    have.Add(s.name);

            foreach (string name in new[]
                     {
                         "PN3D/SkyGradient",
                         "PN3D/Silhouette",
                         // URP's own shaders need listing for the same reason: MatLib
                         // creates every material at runtime, so no material asset
                         // references Lit or Unlit and the build drops them.
                         "Universal Render Pipeline/Lit",
                         "Universal Render Pipeline/Unlit",
                     })
                if (!have.Contains(name))
                    Debug.LogError($"[PN3D] {name} is missing from Always Included Shaders. " +
                                   "It is only ever reached via Shader.Find, so this build " +
                                   "will strip it and the sky or horizon will be absent on device.");
        }

        static void ApplyKeystore()
        {
            string store = Environment.GetEnvironmentVariable(KeystoreEnv);
            string storePass = Environment.GetEnvironmentVariable(StorePassEnv);
            string alias = Environment.GetEnvironmentVariable(AliasEnv);
            string aliasPass = Environment.GetEnvironmentVariable(AliasPassEnv);

            if (string.IsNullOrEmpty(store) || !File.Exists(store))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.Log($"[PN3D] no {KeystoreEnv} set — signing with the debug keystore. " +
                          "Fine for adb install, rejected by Play.");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = store;
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = aliasPass;
            Debug.Log($"[PN3D] signing with {Path.GetFileName(store)} / alias {alias}");
        }

        [MenuItem("PN3D/Android/Build APK")]
        public static void BuildApk() => Build(appBundle: false);

        /// <summary>
        /// Development build: Debug.Log reaches logcat and the profiler can attach.
        /// A release Android build prints nothing under the Unity tag, so this is the
        /// only way to see why something failed on device.
        /// </summary>
        [MenuItem("PN3D/Android/Build APK (development)")]
        public static void BuildApkDev() => Build(appBundle: false, development: true);

        [MenuItem("PN3D/Android/Build AAB for Play")]
        public static void BuildAab() => Build(appBundle: true);

        static void Build(bool appBundle, bool development = false)
        {
            DataSync.Sync(false);
            ApplyPlayerSettings();

            var scenes = EditorBuildSettings.scenes
                                            .Where(s => s.enabled)
                                            .Select(s => s.path)
                                            .ToArray();
            if (scenes.Length == 0)
            {
                Fail("no enabled scenes in build settings");
                return;
            }

            EditorUserBuildSettings.buildAppBundle = appBundle;

            string dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Build", "Android");
            Directory.CreateDirectory(dir);
            string ext = appBundle ? "aab" : "apk";
            string suffix = development ? "-dev" : "";
            string outPath = Path.Combine(dir, $"ParkingNightmare3D-{PlayerSettings.bundleVersion}{suffix}.{ext}");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
            {
                Fail("could not switch the active build target to Android — " +
                     "is the Android module installed?");
                return;
            }

            Debug.Log($"[PN3D] building {scenes.Length} scene(s) -> {outPath}");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.CompressWithLz4HC
                          | (development
                                 ? BuildOptions.Development | BuildOptions.AllowDebugging
                                 : BuildOptions.None),
            });

            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Fail($"build {s.result} with {s.totalErrors} error(s)");
                return;
            }

            Debug.Log($"[PN3D] BUILD OK  {outPath}  " +
                      $"{new FileInfo(outPath).Length / (1024f * 1024f):F1} MB  " +
                      $"in {s.totalTime.TotalMinutes:F1} min");
            Report(report);

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Dump the biggest things in the build. The point of a first build is the size
        /// and shape of it, and this is the only place that information exists.
        /// </summary>
        static void Report(BuildReport report)
        {
            var files = report.GetFiles();
            if (files == null || files.Length == 0) return;

            Debug.Log("[PN3D] largest build outputs:\n" + string.Join("\n",
                files.OrderByDescending(f => f.size)
                     .Take(15)
                     .Select(f => $"  {f.size / (1024f * 1024f),8:F2} MB  {Path.GetFileName(f.path)}")));
        }

        static void Fail(string message)
        {
            Debug.LogError("[PN3D] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
