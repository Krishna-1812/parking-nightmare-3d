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

        public TrafficSystem Traffic;
        public PedSystem Peds;
        public Rng Rng;

        /// <summary>The ice cream truck's jingle, which cannot be silenced and draws a crowd.</summary>
        public bool JingleOn;

        public List<IcePatch> IcePatches = new List<IcePatch>();
        SurfaceFlags _flags;

        double _colCd;
        readonly Dictionary<int, double> _overtakeRel = new Dictionary<int, double>();
        readonly Dictionary<int, double> _nearMissCd = new Dictionary<int, double>();

        public event Action<ScoreResult> OnSucceeded;
        public event Action<string> OnFailed;

        /// <summary>Fired when a traffic car leans on its horn at you.</summary>
        public event Action<TrafficCar> OnHonk;

        /// <summary>Metres remaining to the parking spot, for the GPS readout.</summary>
        public double DistanceToGo => Math.Max(0.0, Spot.S - Proj.S);

        public static MissionRun Create(Mission authored, VehicleDef veh, uint? seed = null)
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

            run.JingleOn = mission.Veh == "icecream";
            run.Rng = new Rng(seed ?? (uint)(mission.Id * 7919));
            run.Traffic = new TrafficSystem(route, spot, mission.Lanes, mission.Traffic,
                                            mission.Time == "night", run.Rng);
            run.Peds = new PedSystem(route, RoadGeom.HalfWidth(mission.Lanes),
                                     mission.Peds, run.Rng);
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
            if (_colCd > 0.0) _colCd -= dt;
            Style.Tick(dt);
            Shame.Tick(dt, Phase);
            if (Phase == GamePhase.Fail) return;

            // ---- surfaces and zone shame ----
            Surface.Step(dt, Car, Proj, Route, RoadGeom.HalfWidth(Mission.Lanes),
                         Phase, _flags, Shame, Style, IcePatches);
            OncomingCheck(dt);
            if (Phase == GamePhase.Fail) return;

            // ---- collisions, then the actors, then style scanning ----
            Collide(dt);
            if (Phase == GamePhase.Fail) return;
            Traffic.Update(dt, this);
            Peds.Update(dt, this);
            StyleScan(dt);
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

        /// <summary>
        /// Sustained shame for driving into oncoming traffic — only when a car is
        /// actually bearing down on you, which is why it needs the traffic system
        /// (§10, and src/n3_e.js:865).
        /// </summary>
        void OncomingCheck(double dt)
        {
            if (Phase != GamePhase.Drive) return;
            double rw = RoadGeom.HalfWidth(Mission.Lanes);
            if (!(Math.Abs(Proj.T) < rw && Proj.T < -0.4)) return;
            if (Car.SpeedAbs <= 2) return;

            var cars = Traffic.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (c.Dir == -1 && (c.S - Proj.S) > -4 && (c.S - Proj.S) < 42 &&
                    Math.Abs(c.T - Proj.T) < 2.2)
                {
                    Surface.WarnText = "⚠️ ONCOMING TRAFFIC!";
                    Shame.Add(2.4 * dt, Phase);
                    return;
                }
            }
        }

        /// <summary>Port of <c>Game.collide</c>'s traffic and parked-car paths, src/n3_e.js:910.</summary>
        void Collide(double dt)
        {
            if (Phase == GamePhase.Settle) return;

            var pObb = new Obb(Car.X, Car.Y, Car.H, Veh.Len / 2.0, Veh.Wid / 2.0);
            var cars = Traffic.Cars;

            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (MathX.Dist2(car.X, car.Y, Car.X, Car.Y) > 15 * 15) continue;

                var mtv = ObbCollision.Test(pObb, car.Box);
                if (!mtv.Hit) continue;

                double sev = MathX.Clamp((Math.Abs(Car.Speed) + car.V) / 14.0, 0.1, 1.0);
                Traffic.OnHit(car);
                HitEffects(mtv, CollisionKind.Traffic, sev);
                return;   // one collision resolution per step, as the reference does
            }
        }

        void HitEffects(Mtv mtv, CollisionKind kind, double sev)
        {
            // positional fix, then a damped bounce along the collision normal
            Car.X += mtv.Nx * mtv.Depth;
            Car.Y += mtv.Ny * mtv.Depth;
            double vn = Car.Vx * mtv.Nx + Car.Vy * mtv.Ny;
            if (vn < 0)
            {
                Car.Vx -= (1 + 0.38) * vn * mtv.Nx;
                Car.Vy -= (1 + 0.38) * vn * mtv.Ny;
                Car.Vx *= 0.72; Car.Vy *= 0.72;
            }
            if (_colCd > 0) return;
            _colCd = 0.35;
            RegisterCollision(sev, kind);
        }

        /// <summary>
        /// Overtakes and near misses (§10). Port of <c>Game.styleScan</c>, src/n3_e.js.
        /// Both are keyed by the car's pooled id, which is why pooling identity matters.
        /// </summary>
        void StyleScan(double dt)
        {
            double pSpd = Car.SpeedAbs;
            var cars = Traffic.Cars;

            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                double rel = car.S - Proj.S;

                if (car.Dir == 1)
                {
                    if (!_overtakeRel.TryGetValue(car.Id, out double was))
                    {
                        _overtakeRel[car.Id] = rel;
                    }
                    else
                    {
                        if (was > 2 && rel < -2 && pSpd > car.V + 0.5 &&
                            _colCd <= 0 && Phase == GamePhase.Drive)
                            Style.Overtake(Phase);
                        _overtakeRel[car.Id] = rel;
                    }
                }

                _nearMissCd.TryGetValue(car.Id, out double cd);
                if (cd > 0) { _nearMissCd[car.Id] = cd - dt; continue; }

                double d2 = MathX.Dist2(car.X, car.Y, Car.X, Car.Y);
                double minDim = (car.Wid + Veh.Wid) / 2.0 + 0.85;
                double closing = Math.Abs(pSpd) + car.V;

                if (d2 < (minDim + car.Len / 2) * (minDim + car.Len / 2) && closing > 9 &&
                    _colCd <= 0 && Phase == GamePhase.Drive)
                {
                    var inflated = new Obb(car.X, car.Y, car.H, car.Len / 2 + 0.8, car.Wid / 2 + 0.8);
                    var pObb = new Obb(Car.X, Car.Y, Car.H, Veh.Len / 2.0, Veh.Wid / 2.0);
                    if (ObbCollision.Test(pObb, inflated).Hit)
                    {
                        _nearMissCd[car.Id] = 3;
                        Style.NearMiss(Phase);
                    }
                }
            }
        }

        internal void NotifyHonk(TrafficCar car) => OnHonk?.Invoke(car);

        /// <summary>A pedestrian dived clear of you. 12 shame — "SO CLOSE!!" (§10).</summary>
        public void OnPedDive(Ped ped) => Shame.PedDive(Phase);

        /// <summary>A pedestrian started filming you. 2 shame, once per pedestrian.</summary>
        public void OnPedFilmed(Ped ped) => Shame.Filmed(Phase);

        /// <summary>Register a collision: damage on the car, shame on you.</summary>
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
