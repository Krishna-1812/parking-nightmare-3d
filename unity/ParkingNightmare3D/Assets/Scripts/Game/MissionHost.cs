using UnityEngine;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// Assembles one playable mission: world, driver, camera and HUD.
    ///
    /// This is the component the authored Mission 1 scene carries. It reuses whatever
    /// camera the scene already provides and only creates one if there is none, so the
    /// same component works from the scene, from a bare GameObject, and from the editor
    /// tooling.
    ///
    /// The world geometry is still generated at load rather than saved into the scene.
    /// That is deliberate: the route, the parking spot and every prop position are
    /// functions of the mission data in <c>design-spec/data</c>, and freezing them into a
    /// scene file would let the art drift out of step with the data the simulation reads.
    /// </summary>
    public sealed class MissionHost : MonoBehaviour
    {
        [Tooltip("Mission id from design-spec/data/missions.json.")]
        public int MissionId = 1;

        public MissionDriver Driver { get; private set; }
        public MissionRun Run { get; private set; }

        void Awake()
        {
            // The handling is tuned per-step at 120 Hz. Belt and braces alongside
            // ProjectSettings/TimeManager.asset, in case a scene or script changed it.
            Time.fixedDeltaTime = 1f / 120f;
            Time.maximumDeltaTime = 10f / 120f;

            Run = CreateRun(MissionId);
            if (Run == null)
            {
                FatalOverlay.Show(gameObject, $"mission {MissionId} could not be created",
                                  new System.IO.FileNotFoundException(
                                      "missions.json / vehicles.json did not load"));
                return;
            }

            WorldBuilder.Built built;
            try
            {
                built = WorldBuilder.Build(Run, transform);
            }
            catch (System.Exception e)
            {
                // Without this the app runs happily at full framerate showing an empty
                // world, which on a release build is indistinguishable from a camera bug.
                FatalOverlay.Show(gameObject, "world construction failed", e);
                return;
            }

            Driver = gameObject.AddComponent<MissionDriver>();
            Driver.Init(Run, built.Car, built.CarBody, built.CarRig);

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("PN3D_Camera") { tag = "MainCamera" };
                camGo.transform.SetParent(transform, false);
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            Art.PostFx.SetupCamera(cam, 62f);

            var chase = cam.GetComponent<ChaseCamera>() ?? cam.gameObject.AddComponent<ChaseCamera>();
            chase.Driver = Driver;
            chase.Target = built.Car;
            chase.SnapBehind();

            var actors = gameObject.AddComponent<ActorViews>();
            actors.Run = Run;

            HudUI.Attach(gameObject, Driver);
        }

        /// <summary>
        /// Load the mission and vehicle data and build a run. Shared with the editor
        /// tooling so both go through exactly one path.
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
