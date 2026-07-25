using System;
using System.Collections.Generic;

namespace PN3D.Core
{
    /// <summary>
    /// One playthrough of one mission: owns the route, car, spot, and the shame / style /
    /// surface / parking systems, and drives them in the exact order the reference
    /// implementation does (<c>Game.fixedUpdate</c>, src/n3_e.js:760).
    ///
    /// The ordering is load-bearing. Timers advance and shame decays *before* the rules
    /// that add shame, so a step that earns shame never also decays in the same step, and
    /// `CalmT` is zeroed after the decay check rather than before it.
    ///
    /// Engine-free by construction: the Unity layer feeds it input and reads state.
    /// </summary>
    public sealed class MissionRun
    {
        public Mission Mission;
        public VehicleDef Veh;
        public CompiledRoute Route;
        public ParkingSpot Spot;
        public VehicleSim Car;

        public readonly ShameSystem Shame = new ShameSystem();
        public readonly StyleSystem Style = new StyleSystem();
        public readonly SurfaceRules Surface = new SurfaceRules();
        public readonly ParkChecker Park = new ParkChecker();

        public GamePhase Phase = GamePhase.Drive;
        public double Timer;
        public int Collisions;
        public double DistDriven;

        public Projection Proj;
        public ScoreResult Result;

        public List<IcePatch> IcePatches = new List<IcePatch>();
        SurfaceFlags _flags;

        public event Action<ScoreResult> OnSucceeded;
        public event Action<string> OnFailed;

        /// <summary>Metres remaining to the parking spot, for the GPS readout.</summary>
        public double DistanceToGo => Math.Max(0.0, Spot.S - Proj.S);

        public static MissionRun Create(Mission authored, VehicleDef veh)
        {
            // enrichment mutates, so work on a copy and keep the authored data pristine
            var mission = authored.Clone();
            var route = RouteCompiler.CompileMission(mission);
            var spot = ParkingSpot.Build(route, ParkingSpot.ParseType(mission.Park),
                                         veh, mission.Margin, mission.Lanes);

            route.PosAt(0.0, RoadGeom.HalfWidth(mission.Lanes) * 0.5,
                        out double sx, out double sy, out double sh);

            var run = new MissionRun
            {
                Mission = mission,
                Veh = veh,
                Route = route,
                Spot = spot,
                Car = new VehicleSim(veh, sx, sy, sh),
                _flags = new SurfaceFlags { Rain = mission.Rain, Snow = mission.Snow },
            };
            run.Park.IsUfo = veh.Drive == "ufo";
            run.Proj = route.Project(sx, sy);
            run.Shame.OnFail += () => run.Fail("SHAME");
            return run;
        }

        /// <summary>Advance one fixed step. Call at exactly 1/120 s.</summary>
        public void Step(double dt, VehicleInput input)
        {
            if (Phase == GamePhase.Success || Phase == GamePhase.Fail) return;

            // during the settle hold the reference stops feeding input to the car
            var inp = Phase == GamePhase.Settle ? VehicleInput.Idle : input;
            Car.Step(dt, inp);

            double prevS = Proj.S;
            Proj = Route.Project(Car.X, Car.Y, Proj.Idx);
            DistDriven += Math.Abs(Proj.S - prevS);

            // ---- timers, before any rule that can add shame ----
            Timer += dt;
            if (Surface.CurbCd > 0.0) Surface.CurbCd -= dt;
            Style.Tick(dt);
            Shame.Tick(dt, Phase);
            if (Phase == GamePhase.Fail) return;

            // ---- surfaces and zone shame ----
            Surface.Step(dt, Car, Proj, Route, RoadGeom.HalfWidth(Mission.Lanes),
                         Phase, _flags, Shame, Style, IcePatches);
            if (Phase == GamePhase.Fail) return;

            // ---- damage fail ----
            if (Car.Damage >= 100.0 && Phase != GamePhase.Settle) { Fail("DAMAGE"); return; }

            // ---- parking ----
            Park.Phase = Phase;
            Park.Step(dt, Car, Spot, Route, Proj);
            Phase = Park.Phase;

            if (Phase == GamePhase.Success) Succeed();
        }

        void Succeed()
        {
            Car.Vx = Car.Vy = 0.0;
            var q = Park.Measure;
            Result = Scoring.Compute(
                Mission.Par, Timer, Style.Style,
                q.AngDeg, q.CurbGap, q.HasCurbGap,
                Car.Damage, Shame.Shame, Collisions,
                Mission.S2, Mission.S3);
            OnSucceeded?.Invoke(Result);
        }

        public void Fail(string reason)
        {
            if (Phase == GamePhase.Success || Phase == GamePhase.Fail) return;
            Phase = GamePhase.Fail;
            OnFailed?.Invoke(reason);
        }

        /// <summary>Register a collision from the (not yet ported) collision layer.</summary>
        public void RegisterCollision(double severity, CollisionKind kind)
        {
            severity = MathX.Clamp(severity, 0.1, 1.0);
            Collisions++;
            bool isTank = Veh.Drive == "tank";
            double dmg = severity * 13.0 * Veh.Fragility * (isTank ? 0.15 : 1.0);
            Car.Damage = MathX.Clamp(Car.Damage + dmg, 0.0, 100.0);
            Shame.Collision(severity, Phase, kind, isTank);
        }
    }
}
