using UnityEngine;

namespace PN3D.Game.Art
{
    /// <summary>
    /// A reflection probe that rides with the car.
    ///
    /// WHY THIS EXISTS. Car paint is convincing because of what it reflects, not because of
    /// its colour. With only the skybox to draw on, every smooth panel returned the same
    /// smooth gradient, and at grazing angles — where Fresnel drives reflectance to 1 — the
    /// flanks turned into flat white sheets and the car's form dissolved into the
    /// background. That was not a bug in the shading; it was the shading being right about
    /// an environment with nothing in it. Give it houses, kerbs, road and trees to reflect
    /// and the same material reads as painted metal.
    ///
    /// COST. 64 px faces, one face per refresh, refreshed every sixth frame, so a full
    /// cubemap lands about every 0.6 s at 60 fps. That is far too slow for a mirror and
    /// entirely sufficient for blurry paint reflections on a car doing 10 m/s. Measured
    /// budget before this went in was 4.9 ms of a 16.7 ms frame.
    ///
    /// The car is on its own layer and culled out of the probe: without that it reflects
    /// its own interior, which shows up as dark smears crawling across the doors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarReflection : MonoBehaviour
    {
        /// <summary>
        /// Unnamed user layer. Named in ProjectSettings/TagManager.asset as "Vehicle" so
        /// this is not a magic number in the inspector.
        /// </summary>
        public const int VehicleLayer = 8;

        const int Resolution = 64;
        const int RefreshEvery = 6;

        ReflectionProbe _probe;
        int _tick;

        public static CarReflection Attach(Transform car, float height)
        {
            SetLayer(car, VehicleLayer);

            var go = new GameObject("CarReflection");
            go.transform.SetParent(car, false);
            go.transform.localPosition = new Vector3(0, height, 0);

            var c = go.AddComponent<CarReflection>();
            c.Build();
            return c;
        }

        void Build()
        {
            _probe = gameObject.AddComponent<ReflectionProbe>();
            _probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            _probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            _probe.resolution = Resolution;
            _probe.hdr = false;
            _probe.shadowDistance = 30f;
            _probe.farClipPlane = 140f;
            _probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.Skybox;
            _probe.cullingMask = ~(1 << VehicleLayer);

            // Generous box so the probe influences the whole car and the traffic near it.
            // Box projection stays off: it would warp the reflection to a room-shaped
            // volume, and this is an open street.
            _probe.size = new Vector3(80f, 40f, 80f);
            _probe.boxProjection = false;
            _probe.importance = 2;

            _probe.RenderProbe();
        }

        void LateUpdate()
        {
            if (_probe == null) return;
            if (++_tick < RefreshEvery) return;
            _tick = 0;
            _probe.RenderProbe();
        }

        public static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
