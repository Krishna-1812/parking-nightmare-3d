using UnityEngine;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// Fallback entry point: if a scene does not already contain a
    /// <see cref="MissionHost"/>, spawn one so pressing Play in any scene still gives a
    /// playable mission.
    ///
    /// Milestone step 5 moved the real entry point into the authored
    /// <c>Assets/Scenes/Mission01.unity</c>. This stays because the editor tooling and the
    /// smoke test build their world from script with no scene at all, and because
    /// "open the project, press Play, it works" is worth keeping.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public const int DefaultMissionId = 1;

        static bool _spawned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (_spawned) return;
            if (Object.FindFirstObjectByType<MissionHost>() != null) return;  // scene owns it

            _spawned = true;
            var go = new GameObject("PN3D_Bootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<MissionHost>().MissionId = DefaultMissionId;
        }

        /// <summary>Kept as the tooling's entry point into mission construction.</summary>
        public static MissionRun CreateRun(int missionId) => MissionHost.CreateRun(missionId);
    }
}
