using System;
using System.Collections.Generic;

namespace PN3D.Core
{
    public struct IcePatch
    {
        public double X, Y, R;
        public IcePatch(double x, double y, double r) { X = x; Y = y; R = r; }
    }

    /// <summary>Weather / hazard flags for a mission, feeding surface grip.</summary>
    public struct SurfaceFlags
    {
        public bool Rain;
        public bool Snow;
    }

    /// <summary>
    /// Surface classification, grip, and the sustained shame sources (DESIGN_SPEC §8, §10).
    /// Port of <c>Game.surfaceLogic</c>, src/n3_e.js:821.
    ///
    /// This also owns <see cref="VehicleSim.SurfaceGrip"/>: grip is set here every step
    /// from where the car actually is, not from the mission flags alone. §8 describes the
    /// flags but not the values, which are: on road or sidewalk, 0.78 in rain, 0.88 in
    /// snow, otherwise 1; off both, 0.55; and any ice patch multiplies by a further 0.3.
    /// </summary>
    public sealed class SurfaceRules
    {
        public double CurbCd;
        public double WrongWayT;
        public double SmoothMark;
        public bool OnIce;
        public string WarnText = "";

        double _lastT;
        bool _hasLastT;

        /// <summary>True when the car is over the driveable road surface.</summary>
        public bool OnRoad { get; private set; }
        public bool OnSidewalk { get; private set; }

        /// <summary>Fired when the car mounts or leaves the curb, for the thud + bounce.</summary>
        public event Action<bool> OnCurbHop;   // true = mounting (leaving the road)

        public void Step(double dt, VehicleSim car, Projection proj, CompiledRoute route,
                         double rw, GamePhase phase, SurfaceFlags flags,
                         ShameSystem shame, StyleSystem style,
                         IList<IcePatch> icePatches = null)
        {
            double absT = Math.Abs(proj.T);
            bool road = absT < rw;
            bool sidewalk = absT >= rw && absT < rw + 0.35 + RoadGeom.SidewalkW;
            double spd = car.SpeedAbs;
            OnRoad = road;
            OnSidewalk = sidewalk;

            // rain slicks the road, snow more so, off-road is loose
            car.SurfaceGrip = (road || sidewalk)
                ? (flags.Rain ? 0.78 : (flags.Snow ? 0.88 : 1.0))
                : 0.55;

            // black ice: near-zero lateral grip while any wheel is over a patch
            OnIce = false;
            if (icePatches != null)
            {
                for (int i = 0; i < icePatches.Count; i++)
                {
                    var ice = icePatches[i];
                    double rr = ice.R + 0.5;
                    if (MathX.Dist2(ice.X, ice.Y, car.X, car.Y) < rr * rr)
                    {
                        car.SurfaceGrip *= 0.3;
                        OnIce = true;
                        break;
                    }
                }
            }

            // curb hop — 1 s cooldown, and only above 1.5 m/s so crawling over is free
            bool wasRoad = Math.Abs(_hasLastT ? _lastT : proj.T) < rw;
            if (road != wasRoad && spd > 1.5 && CurbCd <= 0.0 && phase != GamePhase.Settle)
            {
                CurbCd = 1.0;
                car.Damage = MathX.Clamp(car.Damage + 1.5 * car.Def.Fragility, 0.0, 100.0);
                car.BounceV = 1.6;
                OnCurbHop?.Invoke(!road);
                if (!road) shame.CurbMount(phase);
                else shame.CurbDismount(phase);
            }
            _lastT = proj.T;
            _hasLastT = true;

            string warn = "";

            if (sidewalk && spd > 1.0 && phase == GamePhase.Drive)
            {
                shame.Add(2.2 * dt, phase);
                warn = "\U0001F6B6 SIDEWALK!";
            }

            if (!road && !sidewalk && spd > 1.0 && phase == GamePhase.Drive)
            {
                shame.Add(1.2 * dt, phase);
                warn = "\U0001F331 LAWN VIOLATION";
            }

            // wrong way — only mid-route, moving forward against the flow
            double hd = Math.Cos(car.H - proj.H);
            if (road && hd < -0.35 && spd > 3.0 && phase == GamePhase.Drive)
            {
                WrongWayT += dt;
                if (WrongWayT > 1.0) warn = "⛔ WRONG WAY!";
                if (WrongWayT > 1.2) shame.Add(1.6 * dt, phase);
            }
            else
            {
                WrongWayT = 0.0;
            }

            // school zone. NOTE: route enrichment strips the `zone` field, so no compiled
            // campaign route actually has one and this never fires — see DESIGN_SPEC §5.1.
            // Kept because it is what the reference does and it costs nothing.
            for (int i = 0; i < route.Zones.Count; i++)
            {
                var z = route.Zones[i];
                if (proj.S > z.S0 && proj.S < z.S1 && z.Kind == "school" && spd > 6.5)
                {
                    warn = "\U0001F6B8 SCHOOL ZONE — SLOW!";
                    shame.Add(2.2 * dt, phase);
                }
            }

            // smooth driving bonus: 180 m covered with at least 4 s of calm behind you
            if (phase == GamePhase.Drive && shame.CalmT > 4.0 && proj.S - SmoothMark > 180.0)
            {
                SmoothMark = proj.S;
                style.Smooth(phase);
            }

            WarnText = warn;
        }

        /// <summary>
        /// Sustained shame from driving into oncoming traffic. Separated out because it
        /// needs the traffic system; call it from there once traffic exists.
        /// </summary>
        public void OncomingDanger(double dt, GamePhase phase, ShameSystem shame)
            => shame.Add(2.4 * dt, phase);
    }
}
