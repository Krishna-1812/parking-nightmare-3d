using UnityEngine;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// Builds the whole vertical slice at runtime — world, car, camera, HUD — so the
    /// project runs from any scene with nothing to wire up in the inspector.
    ///
    /// That is a deliberate choice for the greybox stage: it keeps the slice entirely
    /// reproducible from source, with no scene asset whose serialized references can rot.
    /// Milestone step 5 replaces it with a real authored scene.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public const int DefaultMissionId = 1;

        public MissionDriver Driver { get; private set; }

        static bool _spawned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (_spawned) return;
            _spawned = true;
            var go = new GameObject("PN3D_Bootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();
        }

        void Awake()
        {
            // the handling is tuned per-step at 120 Hz; belt and braces alongside
            // ProjectSettings/TimeManager.asset in case a scene or script changed it
            Time.fixedDeltaTime = 1f / 120f;
            Time.maximumDeltaTime = 10f / 120f;

            var run = CreateRun(DefaultMissionId);
            if (run == null) return;

            WorldBuilder.BuildLighting(transform);
            var built = WorldBuilder.Build(run, transform);

            Driver = gameObject.AddComponent<MissionDriver>();
            Driver.Init(run, built.Car, built.CarBody);

            var camGo = new GameObject("PN3D_Camera");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.farClipPlane = 900f;
            cam.backgroundColor = new Color(0.49f, 0.71f, 0.89f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            camGo.AddComponent<AudioListener>();

            var chase = camGo.AddComponent<ChaseCamera>();
            chase.Driver = Driver;
            chase.Target = built.Car;
            chase.SnapBehind();

            var actors = gameObject.AddComponent<ActorViews>();
            actors.Run = run;

            var hud = gameObject.AddComponent<Hud>();
            hud.Driver = Driver;
        }

        /// <summary>
        /// Load the mission and vehicle data and build a run. Shared with the editor
        /// capture tool so both go through exactly one path.
        /// </summary>
        public static MissionRun CreateRun(int missionId)
        {
            string missionsJson = DataPaths.Load("missions.json");
            string vehiclesJson = DataPaths.Load("vehicles.json");
            if (missionsJson == null || vehiclesJson == null) return null;

            var missions = Mission.ParseAll(missionsJson);
            var vehicles = VehicleDef.ParseAll(vehiclesJson);

            var mission = missions.Find(m => m.Id == missionId);
            if (mission == null)
            {
                Debug.LogError($"[PN3D] mission {missionId} not found");
                return null;
            }
            if (!vehicles.TryGetValue(mission.Veh, out var veh))
            {
                Debug.LogError($"[PN3D] vehicle '{mission.Veh}' not found");
                return null;
            }

            return MissionRun.Create(mission, veh);
        }
    }
}
