using System;

namespace PN3D.Core
{
    public enum ParkType { Parallel, Bay }

    /// <summary>
    /// The target parking spot as an oriented box (DESIGN_SPEC §6).
    /// Geometry ports <c>buildDestination</c>, src/n3_d.js:1711.
    /// </summary>
    public sealed class ParkingSpot
    {
        public ParkType Type;
        public double X, Y, H;
        public double Hl, Hw;
        public double T;      // lateral offset of the centre from the route centreline
        public double CurbT;  // curb reference (= road half-width); parallel only
        public double S;      // arc position along the route

        public Obb Box => new Obb(X, Y, H, Hl, Hw);

        /// <summary>
        /// Where the parking zone begins — crossing this arms the parking check.
        /// </summary>
        public double ZoneS;

        /// <summary>
        /// Build the spot for a mission. <paramref name="route"/> must already be
        /// compiled from enriched segments (see <see cref="RouteCompiler.CompileMission"/>).
        ///
        /// Note how `margin` enters differently per type: for a parallel spot it is
        /// longitudinal slack (the gap between the bracketing cars), for a bay it is
        /// lateral. That is why 1.0 m is brutal on a bay and merely tight on a parallel.
        /// </summary>
        public static ParkingSpot Build(CompiledRoute route, ParkType type, VehicleDef veh,
                                        double margin, int lanes)
        {
            double rw = RoadGeom.HalfWidth(lanes);
            double sSpot = route.Length - 24.0;
            double zoneS = sSpot - 42.0;

            if (type == ParkType.Parallel)
            {
                double gap = veh.Len + margin;
                double t = rw - Math.Max(1.15, veh.Wid / 2.0 + 0.15);
                route.PosAt(sSpot, t, out double px, out double py, out double ph);
                return new ParkingSpot
                {
                    Type = ParkType.Parallel,
                    X = px, Y = py, H = ph,
                    Hl = gap / 2.0,
                    Hw = Math.Max(1.3, veh.Wid / 2.0 + 0.35),
                    T = t, CurbT = rw, S = sSpot, ZoneS = zoneS,
                };
            }
            else
            {
                double spotT = rw + 2.2 + veh.Len / 2.0;
                route.PosAt(sSpot, spotT, out double px, out double py, out double ph);
                return new ParkingSpot
                {
                    Type = ParkType.Bay,
                    X = px, Y = py,
                    H = ph + Math.PI / 2.0,   // nose-in, pointing away from the road
                    Hl = veh.Len / 2.0 + 0.6,
                    Hw = (veh.Wid + margin) / 2.0 + 0.25,
                    T = spotT, CurbT = 0.0, S = sSpot, ZoneS = zoneS,
                };
            }
        }

        public static ParkType ParseType(string s) =>
            s == "bay" ? ParkType.Bay : ParkType.Parallel;
    }
}
