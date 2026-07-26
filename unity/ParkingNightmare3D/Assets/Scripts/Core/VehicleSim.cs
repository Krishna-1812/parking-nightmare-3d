using System;

namespace PN3D.Core
{
    /// <summary>One frame of driver input. All three input paths converge here (§4).</summary>
    public struct VehicleInput
    {
        public double Steer;      // signed, -1..1
        public double Throttle;   // signed, -1..1; negative is brake-then-reverse
        public bool Handbrake;

        /// <summary>
        /// True for TILT input only, which is already an absolute wheel position. Routes
        /// the sweep through lambda = 20 instead of the keyboard attack of 3.9 — running
        /// tilt through the keyboard sweep was the single largest cause of reported tilt
        /// latency (§4).
        ///
        /// Not the on-screen wheel, despite it also being an absolute position:
        /// src/n3_b.js:694 returns false whenever steerTouch is non-zero, deliberately, so
        /// that the wheel keeps the same feel as the keyboard. Matched here rather than
        /// "corrected", because the par times were tuned against that behaviour.
        /// </summary>
        public bool SteerAnalog;

        public static readonly VehicleInput Idle = new VehicleInput();
    }

    /// <summary>
    /// Kinematic vehicle body. Port of <c>Vehicle3D.update</c>, src/n3_e.js:61.
    ///
    /// Physics is 2D: x, y in metres on a flat plane plus heading h in radians, with
    /// forward = (cos h, sin h). Elevation is cosmetic and never feeds back in. This
    /// deliberately does NOT use WheelCollider or PhysX — the feel is a hand-tuned
    /// kinematic model and substituting real vehicle physics invalidates every par time
    /// and star threshold in the game (§3).
    ///
    /// Call <see cref="Step"/> from FixedUpdate at exactly 1/120 s. Several terms are
    /// per-step exponentials, so the rate is part of the tuning, not an implementation
    /// detail.
    ///
    /// Currently implements the <c>car</c> drive model, which covers 7 of the 9
    /// vehicles. <c>tank</c> (§3.2) and <c>ufo</c> (§3.3) are not ported yet.
    /// </summary>
    public sealed class VehicleSim
    {
        public VehicleDef Def;

        // pose
        public double X, Y, H;
        // previous pose, for render interpolation between fixed steps
        public double Px, Py, Ph;
        // world-frame velocity
        public double Vx, Vy;

        public double Steer;              // current road-wheel angle, radians
        public double SteerCmd;           // swept steering command, -1..1
        public double MaxSteer = MathX.Rad(38);

        public bool Braking, Reversing;

        /// <summary>
        /// Cosmetic damage, 0-100. Costs only 4 points each at scoring and is deliberately
        /// much gentler than shame — damage is pressure, shame is the actual threat (§10).
        /// </summary>
        public double Damage;

        public double SlideAmt;           // |lateral velocity|; drives smoke and skid audio
        public double SurfaceGrip = 1.0;  // < 1 for rain / snow / ice
        public double SlipTimer;          // > 0 while on ice or oil

        public double Bounce, BounceV;    // cosmetic vertical spring (bumps, potholes)
        public double AccF, Pitch, Roll;  // cosmetic body attitude

        /// <summary>Signed forward speed along the current heading.</summary>
        public double Speed => Vx * Math.Cos(H) + Vy * Math.Sin(H);

        public double SpeedAbs => Math.Sqrt(Vx * Vx + Vy * Vy);

        public VehicleSim(VehicleDef def, double x = 0, double y = 0, double h = 0)
        {
            Def = def;
            X = Px = x;
            Y = Py = y;
            H = Ph = h;
        }

        public void StashPrev() { Px = X; Py = Y; Ph = H; }

        public void Step(double dt, VehicleInput inp)
        {
            StashPrev();
            var d = Def;
            double maxSp = d.MaxSpeed * (SurfaceGrip < 1.0 ? SurfaceGrip * 1.1 : 1.0);

            // Keyboard steer arrives as a hard +/-1; sweep the command toward it so the
            // wheel turns like a hand is on it — slower to turn in, quicker to return.
            double steerRaw = inp.Steer;
            double steerLam = inp.SteerAnalog
                ? 20.0
                : (Math.Abs(steerRaw) > Math.Abs(SteerCmd) ? 3.9 : 8.0);
            SteerCmd += (steerRaw - SteerCmd) * (1.0 - Math.Exp(-steerLam * dt));
            if (Math.Abs(SteerCmd) < 0.001 && steerRaw == 0.0) SteerCmd = 0.0;

            double steerIn = SteerCmd;
            double thr = inp.Throttle;
            bool hb = inp.Handbrake;
            Braking = false;

            // ---- bicycle model, body frame ----
            double c = Math.Cos(H), s = Math.Sin(H);
            double vF = Vx * c + Vy * s;
            double vL = -Vx * s + Vy * c;
            double vF0 = vF;

            // speed-sensitive steering, so the car calms down at speed
            double steerScale = 1.0 / (1.0 + Math.Abs(vF) * 0.098);
            double target = steerIn * MaxSteer * steerScale;
            double ds = MathX.Clamp(target - Steer, -d.SteerSpeed * dt, d.SteerSpeed * dt);
            Steer += ds;

            // dedicated brake decel (not just "negative engine"): strong, speed-independent
            double brakeDecel = Math.Min(11.0, 4.5 + d.Accel * 0.9);
            if (thr > 0.0)
            {
                if (vF < -0.45)
                {
                    // braking out of reverse — clamped so it cannot overshoot forward
                    vF = Math.Min(0.0, vF + brakeDecel * thr * dt);
                    Braking = true;
                }
                else
                {
                    // power-limited engine: full punch off the line, tapers near top speed
                    double spFrac = MathX.Clamp(vF / maxSp, 0.0, 1.0);
                    vF += d.Accel * thr * (1.15 - 0.75 * spFrac * spFrac) * dt;
                }
            }
            else if (thr < 0.0)
            {
                if (vF > 0.45)
                {
                    vF = Math.Max(0.0, vF + brakeDecel * thr * dt);
                    Braking = true;
                }
                else
                {
                    vF += d.Accel * 0.55 * thr * dt; // reverse
                }
            }

            // resistive forces: quadratic aero calibrated so top speed converges on
            // maxSpeed, constant rolling resistance, light engine braking when coasting
            double kAero = (d.Accel * 0.4) / (d.MaxSpeed * d.MaxSpeed);
            double resist = kAero * vF * Math.Abs(vF) + Math.Sign(vF) * 0.35;
            if (thr == 0.0) resist += vF * 0.18;
            if (Math.Abs(vF) > 0.05)
            {
                double nv = vF - resist * dt;
                vF = (nv * vF < 0.0) ? 0.0 : nv; // resistance never reverses direction
            }
            if (Math.Abs(vF) < 0.09 && thr == 0.0) vF *= (1.0 - 8.0 * dt); // creep kill
            vF = MathX.Clamp(vF, -maxSp * 0.3, maxSp * 1.02);              // reverse capped at 30%

            double grip = d.Grip * SurfaceGrip;
            if (SlipTimer > 0.0) grip *= 0.35;
            if (hb) { grip *= 0.1; vF *= (1.0 - 1.9 * dt); Braking = true; }

            vL *= Math.Max(0.0, 1.0 - grip * 9.5 * dt); // lateral velocity bleed
            SlideAmt = Math.Abs(vL);

            if (Math.Abs(vF) > 0.08)
                H += (vF / d.Wb) * Math.Tan(Steer) * dt;

            // NOTE: velocity is rebuilt from the NEW heading, after the rotation above.
            // Reconstructing it from the pre-rotation heading is the classic way to get
            // a port that looks right and drifts.
            double c2 = Math.Cos(H), s2 = Math.Sin(H);
            Vx = c2 * vF - s2 * vL;
            Vy = s2 * vF + c2 * vL;
            Reversing = vF < -0.3;

            // cosmetic: weight transfer -> nose pitch, centripetal accel -> body roll
            AccF = MathX.Lerp(AccF, (vF - vF0) / dt, MathX.Clamp(7.0 * dt, 0.0, 1.0));
            Pitch = MathX.Clamp(AccF * 0.0055, -0.055, 0.035);
            double latA = vF * vF * Math.Tan(Steer) / d.Wb;
            Roll = MathX.Lerp(Roll, MathX.Clamp(latA * 0.0042, -0.05, 0.05), MathX.Clamp(6.0 * dt, 0.0, 1.0));

            // ---- integrate, common to every drive model ----
            X += Vx * dt;
            Y += Vy * dt;
            if (SlipTimer > 0.0) SlipTimer -= dt;

            // bounce spring (bumps / potholes), cosmetic
            BounceV -= Bounce * 90.0 * dt;
            BounceV *= (1.0 - 8.0 * dt);
            Bounce += BounceV * dt;
        }
    }
}
