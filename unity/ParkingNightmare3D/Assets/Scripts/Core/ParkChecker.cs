using System;

namespace PN3D.Core
{
    public enum ParkPhase
    {
        /// <summary>Driving the route; the spot is not armed yet.</summary>
        Approach,
        /// <summary>Inside the parking zone, not currently within tolerance.</summary>
        Park,
        /// <summary>Within tolerance and holding still; <see cref="ParkChecker.ParkT"/> is counting.</summary>
        Settle,
        Success,
    }

    /// <summary>
    /// A single measurement of how well the car is sitting in the spot. This is exactly
    /// what the live alignment widget renders, so it is deliberately a value the UI can
    /// read every frame rather than a bare pass/fail.
    /// </summary>
    public struct ParkMeasure
    {
        public bool Inside;      // all four corners in the box, 6 cm slack
        public double DAng;      // signed heading error, radians
        public double CurbGap;   // metres; only meaningful when HasCurbGap
        public bool HasCurbGap;  // false for bay spots — models the reference `null`
        public bool AngOk;
        public bool CurbOk;
        public bool Still;

        public double AngDeg => Math.Abs(DAng) * 180.0 / Math.PI;
    }

    /// <summary>
    /// Parking tolerance check and the 1.5 s settle hold (DESIGN_SPEC §6).
    /// Port of <c>Game.parkingLogic</c>, src/n3_e.js:1177.
    ///
    /// Tolerances: all four corners inside the box with 6 cm slack, heading error
    /// within 8 degrees (UFO exempt, and a parallel spot accepts either facing), curb
    /// gap between -0.02 and 0.40 m for parallel spots, and speed under 0.35 m/s.
    /// </summary>
    public sealed class ParkChecker
    {
        public const double CornerEps = 0.06;
        public const double MaxHeadingErrDeg = 8.0;
        public const double CurbGapMin = -0.02;
        public const double CurbGapMax = 0.40;
        public const double StillSpeed = 0.35;

        /// <summary>
        /// Speed above which an in-progress settle is abandoned. Note this is NOT the
        /// same as <see cref="StillSpeed"/>: entry requires &lt; 0.35 but exit needs
        /// &gt; 0.5, so there is a deliberate hysteresis band. Collapsing the two makes
        /// the hold flicker on and off against sensor and physics jitter.
        /// </summary>
        public const double SettleBreakSpeed = 0.5;

        public const double HoldSeconds = 1.5;

        public ParkPhase Phase = ParkPhase.Approach;
        public bool InZone;
        public double ParkT;
        public ParkMeasure Measure;

        /// <summary>The UFO cannot hold a heading, so it is exempt from the angle check.</summary>
        public bool IsUfo;

        readonly Vec2[] _corners = new Vec2[4];

        /// <summary>
        /// Advance the check one fixed step. <paramref name="proj"/> is the car's current
        /// route projection; its Idx is reused as the search hint when measuring the
        /// curb gap, exactly as the reference does.
        /// </summary>
        public void Step(double dt, VehicleSim car, ParkingSpot spot, CompiledRoute route,
                         Projection proj)
        {
            if (!InZone && proj.S > spot.ZoneS && Phase == ParkPhase.Approach)
            {
                InZone = true;
                Phase = ParkPhase.Park;
            }
            if (!InZone) return;

            var box = spot.Box;
            new Obb(car.X, car.Y, car.H, car.Def.Len / 2.0, car.Def.Wid / 2.0).Corners(_corners);

            bool inside = true;
            for (int i = 0; i < 4; i++)
            {
                if (!box.Contains(_corners[i].X, _corners[i].Y, CornerEps)) { inside = false; break; }
            }

            double dAng = MathX.AngNorm(car.H - spot.H);
            if (spot.Type == ParkType.Parallel)
            {
                // either facing is acceptable
                if (dAng > Math.PI / 2.0) dAng -= Math.PI;
                if (dAng < -Math.PI / 2.0) dAng += Math.PI;
            }

            bool angOk = IsUfo || Math.Abs(dAng * 180.0 / Math.PI) <= MaxHeadingErrDeg;

            double curbGap = 0.0;
            bool hasCurbGap = false;
            if (spot.Type == ParkType.Parallel)
            {
                double maxT = -99.0;
                for (int i = 0; i < 4; i++)
                {
                    var pr = route.Project(_corners[i].X, _corners[i].Y, proj.Idx);
                    if (pr.T > maxT) maxT = pr.T;
                }
                curbGap = spot.CurbT - maxT;
                hasCurbGap = true;
            }

            bool curbOk = spot.Type != ParkType.Parallel ||
                          (hasCurbGap && curbGap >= CurbGapMin && curbGap <= CurbGapMax);

            double speedAbs = car.SpeedAbs;
            bool still = speedAbs < StillSpeed;

            Measure = new ParkMeasure
            {
                Inside = inside, DAng = dAng,
                CurbGap = curbGap, HasCurbGap = hasCurbGap,
                AngOk = angOk, CurbOk = curbOk, Still = still,
            };

            if (Phase == ParkPhase.Park)
            {
                if (inside && angOk && curbOk && still)
                {
                    Phase = ParkPhase.Settle;
                    ParkT = 0.0;
                }
            }
            else if (Phase == ParkPhase.Settle)
            {
                if (!(inside && angOk && curbOk) || speedAbs > SettleBreakSpeed)
                {
                    Phase = ParkPhase.Park;
                    return;
                }
                ParkT += dt;
                if (ParkT >= HoldSeconds) Phase = ParkPhase.Success;
            }
        }

        /// <summary>
        /// A perfect park: heading inside 2 degrees and, for parallel spots, a curb gap
        /// under 15 cm. Drives the alternate confetti, stinger, haptic and the Free Roam
        /// bonus (§6, §9).
        /// </summary>
        public bool IsPerfect()
            => Measure.AngDeg < 2.0 && (!Measure.HasCurbGap || Measure.CurbGap < 0.15);
    }
}
