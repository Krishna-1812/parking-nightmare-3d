using UnityEngine;

namespace PN3D.Game
{
    /// <summary>
    /// Process-wide runtime settings, applied before any scene loads.
    ///
    /// This exists mostly for one line. On mobile Unity does not run as fast as the panel
    /// allows — Application.targetFrameRate defaults to 30 there, and nothing in the
    /// project overrode it. A driving game judged on how the car feels cannot ship at 30
    /// by accident, so the target is asserted here rather than left to the platform
    /// default. If a device cannot hold 60 that is a rendering-cost problem to measure and
    /// fix, not something to hide behind a low cap.
    /// </summary>
    public static class AppConfig
    {
        public const int TargetFps = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            // vSync must be off for targetFrameRate to be honoured at all; with vSync on,
            // Unity ignores the target and paces to the display instead.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFps;

            // A player mid-mission is holding steer, not touching the screen. Without
            // this the phone dims and locks during a long parking approach.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
