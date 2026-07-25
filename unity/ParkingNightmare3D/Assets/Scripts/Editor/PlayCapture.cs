using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PN3D.Core;
using PN3D.Game;

namespace PN3D.EditorTools
{
    /// <summary>
    /// Enters play mode on the authored Mission 1 scene, autopilots a run, and captures
    /// full-screen frames at each stage.
    ///
    /// The edit-mode capture in <see cref="SliceTools"/> renders a camera to a
    /// RenderTexture, which cannot see the HUD — UI Toolkit draws to a screen overlay
    /// panel that only exists while the game is actually running. Verifying the interface
    /// therefore means really playing the scene, which is also the only end-to-end check
    /// that the committed scene, the panel settings and the UXML all still bind.
    ///
    /// Run it from a WINDOWED editor, not -batchmode:
    ///   Unity.exe -projectPath . -executeMethod PN3D.EditorTools.PlayCapture.Run
    ///             -pn3dOut &lt;dir&gt; -pn3dExit
    /// Two reasons it cannot be batched. <c>WaitForEndOfFrame</c> never resumes under
    /// -batchmode, and a screenshot taken before end of frame has no overlay panel in it;
    /// and there is no -quit here either, because -quit tears the editor down before play
    /// mode has started. The runner exits the editor itself when it is finished.
    /// </summary>
    public static class PlayCapture
    {
        const string ScenePath = "Assets/Scenes/Mission01.unity";

        // Entering play mode reloads the domain, which resets every static field and drops
        // every static event subscription — including a playModeStateChanged handler
        // registered just before the transition. SessionState survives the reload, so the
        // request to capture is left there and picked up on the other side.
        const string ArmedKey = "PN3D.PlayCapture.Armed";
        const string OutKey = "PN3D.PlayCapture.OutDir";
        const string ExitKey = "PN3D.PlayCapture.Exit";

        static string ArgValue(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        [MenuItem("PN3D/Play-mode HUD Capture")]
        public static void Run()
        {
            string outDir = ArgValue("-pn3dOut", Path.Combine(Path.GetTempPath(), "pn3d-hud"));
            Directory.CreateDirectory(outDir);

            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[PN3D] " + ScenePath + " missing — run PN3D/Rebuild Mission 1 Scene");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            SessionState.SetBool(ArmedKey, true);
            SessionState.SetString(OutKey, outDir);
            SessionState.SetBool(ExitKey, HasFlag("-pn3dExit") || Application.isBatchMode);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Deferred, not called inline. -executeMethod runs inside the editor's startup
            // sequence and asking for play mode from there does nothing useful; waiting one
            // editor tick lets startup finish and the transition proceeds normally.
            EditorApplication.update += Kick;
        }

        static void Kick()
        {
            EditorApplication.update -= Kick;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// Picked up on the far side of the play-mode domain reload. Editor assemblies get
        /// runtime initialisation callbacks in the editor, which is what makes this the
        /// simplest place to notice that a capture was requested.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnEnteredPlayMode()
        {
            if (!SessionState.GetBool(ArmedKey, false)) return;
            SessionState.EraseBool(ArmedKey);

            var go = new GameObject("PN3D_PlayCaptureRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<Runner>();
            runner.OutDir = SessionState.GetString(OutKey, Path.GetTempPath());
            runner.ExitWhenDone = SessionState.GetBool(ExitKey, false);
        }

        static bool HasFlag(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (a == name) return true;
            return false;
        }

        /// <summary>
        /// Drives the run from inside play mode. An editor-assembly MonoBehaviour, which
        /// the editor is happy to attach at runtime and which never ships in a build.
        /// </summary>
        sealed class Runner : MonoBehaviour
        {
            public string OutDir;
            public bool ExitWhenDone;

            IEnumerator Start()
            {
                yield return null;

                var host = FindFirstObjectByType<MissionHost>();
                if (host == null || host.Driver == null)
                {
                    Fail("no MissionHost in the scene");
                    yield break;
                }

                var driver = host.Driver;
                var run = host.Run;
                Log($"scene loaded: mission {run.Mission.Id} {run.Mission.Name}, " +
                    $"route {run.Route.Length:0} m, par {run.Mission.Par}s");

                // brief
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("10_brief");

                // countdown
                driver.BeginCountdown();
                yield return new WaitForSecondsRealtime(0.9f);
                yield return Shot("11_countdown");

                // drive, with the same autopilot the headless smoke test uses
                driver.InputOverride = () => SliceTools.Autopilot(run, run.Spot.T);

                bool shotDriving = false, shotAlign = false;
                float deadline = Time.realtimeSinceStartup + 180f;
                while (driver.Stage == RunStage.Countdown || driver.Stage == RunStage.Driving)
                {
                    if (!shotDriving && run.Proj.S > run.Route.Length * 0.35)
                    {
                        shotDriving = true;
                        yield return Shot("12_driving");
                    }
                    if (!shotAlign && run.Park.InZone && run.Car.Speed < 3.0)
                    {
                        shotAlign = true;
                        yield return Shot("13_alignment");
                    }
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Fail($"autopilot timed out at s={run.Proj.S:0} phase={run.Phase}");
                        yield break;
                    }
                    yield return null;
                }

                Log($"finished: stage={driver.Stage} phase={run.Phase} t={run.Timer:0.0}s " +
                    $"shame={run.Shame.Shame:0.0} style={run.Style.Style:0}");

                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot(driver.Stage == RunStage.Results ? "14_results" : "14_failed");

                if (driver.Stage != RunStage.Results)
                {
                    Fail("run did not reach the results screen");
                    yield break;
                }

                Log("PLAY CAPTURE OK -> " + OutDir);
                Done(0);
            }

            IEnumerator Shot(string name)
            {
                // must be after rendering, or the overlay panel has not drawn this frame
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                string path = Path.Combine(OutDir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                DestroyImmediate(tex);
                Log("  wrote " + path);
            }

            void Log(string m) => Debug.Log("[PN3D] " + m);

            void Fail(string m)
            {
                Debug.LogError("[PN3D] FAILED: " + m);
                Done(1);
            }

            void Done(int code)
            {
                EditorApplication.isPlaying = false;
                // -pn3dExit lets a windowed editor run be scripted like a batch one
                if (ExitWhenDone) EditorApplication.Exit(code);
            }
        }
    }
}
