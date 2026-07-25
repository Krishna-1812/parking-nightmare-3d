using System;
using System.Collections.Generic;

namespace PN3D.Core
{
    /// <summary>Traffic light phase for one intersection. Port of <c>updateLights</c>, n3_d.js:1928.</summary>
    public sealed class TrafficLightCtrl
    {
        public double Timer;
        /// <summary>0 green, 1 amber, 2 red — for the player's road.</summary>
        public int State;
        public int InterIdx;

        public const double Cycle = 15.6;   // 7 green, 1.6 amber, 7 red

        public void Tick(double dt)
        {
            Timer += dt;
            double t = Timer % Cycle;
            State = t < 7.0 ? 0 : (t < 8.6 ? 1 : 2);
        }
    }

    public sealed class TrafficCar
    {
        public int Id;            // stable across pooling, keys overtake / near-miss state
        public string Kind;
        public double Len, Wid;

        public double S, T;
        public int Dir, Lane;
        public double V, Cruise;

        public double X, Y, H;
        public double Px, Py, Ph;

        public double HonkCd, BlockT, HitT, PanicT;

        public Obb Box => new Obb(X, Y, H, Len / 2.0, Wid / 2.0);
    }

    public sealed class CrossCar
    {
        public int Id;
        public string Kind;
        public double Len, Wid;
        public double U;          // signed distance along the cross axis
        public int Dir;
        public double V, Off;
        public double X, Y, H;
        public int InterIdx;
    }

    /// <summary>
    /// Ambient traffic plus cross traffic through light-controlled intersections.
    /// Port of <c>class Traffic</c>, src/n3_d.js:1959.
    ///
    /// Car meshes are pooled in the reference, and pooling is simulation-visible: a
    /// pooled car skips the kind draw, so the RNG stream depends on it. The pool is
    /// modelled here for that reason, not as an optimisation.
    /// </summary>
    public sealed class TrafficSystem
    {
        public readonly List<TrafficCar> Cars = new List<TrafficCar>();
        public readonly List<TrafficLightCtrl> Lights = new List<TrafficLightCtrl>();
        public readonly Dictionary<int, List<CrossCar>> Crossers = new Dictionary<int, List<CrossCar>>();

        public double Density;
        public double Window = 170.0;
        public bool Night;

        readonly CompiledRoute _route;
        readonly ParkingSpot _spot;
        readonly int _lanes;
        readonly Rng _rng;
        readonly Stack<PooledRef> _pool = new Stack<PooledRef>();
        int _nextId = 1;

        sealed class PooledRef
        {
            public int Id;
            public string Kind;
            public double Len, Wid;
        }

        /// <summary>
        /// Body dimensions per kind, from <c>CarFactory.traffic</c>, src/n3_c.js:2017.
        /// The factory also draws a body colour there; that draw is deliberately NOT
        /// reproduced, because colour is a render concern with no simulation effect —
        /// the Unity layer derives it from <see cref="TrafficCar.Id"/> instead. The
        /// KIND draw is reproduced, because it sets the length and width the gap and
        /// collision maths use.
        /// </summary>
        static readonly Dictionary<string, (double Len, double Wid)> Dims =
            new Dictionary<string, (double, double)>
            {
                { "sedan", (4.5, 1.8) },
                { "hatch", (3.9, 1.75) },
                { "suv", (4.8, 1.95) },
                { "taxi", (4.5, 1.8) },
                { "police", (4.7, 1.85) },
                { "truck", (6.4, 2.3) },
            };

        static readonly string[] KindPool = { "sedan", "sedan", "hatch", "suv" };

        public TrafficSystem(CompiledRoute route, ParkingSpot spot, int lanes,
                             double density, bool night, Rng rng)
        {
            _route = route;
            _spot = spot;
            _lanes = lanes;
            _rng = rng;
            Density = density;
            Night = night;

            // one controller per intersection that has lights
            for (int i = 0; i < route.Inters.Count; i++)
            {
                if (!route.Inters[i].Lights) continue;
                Lights.Add(new TrafficLightCtrl
                {
                    InterIdx = i,
                    Timer = rng.Rand(0, 6),   // mirrors World.build's rr(0, 6)
                });
                Crossers[i] = new List<CrossCar>();
            }
        }

        public TrafficLightCtrl LightFor(int interIdx)
        {
            for (int i = 0; i < Lights.Count; i++)
                if (Lights[i].InterIdx == interIdx) return Lights[i];
            return null;
        }

        public int TargetCount()
            => (int)Math.Min(19.0, MathX.JsRound(Density * (Window * 2.0) / 100.0 * 2.35));

        PooledRef MakeCar()
        {
            if (_pool.Count > 0) return _pool.Pop();   // pooled: consumes no draws

            // chance(0.12) ? taxi : (chance(0.08) ? suv : pick([...])) — 1, 2 or 3 draws
            string kind;
            if (_rng.Chance(0.12)) kind = "taxi";
            else if (_rng.Chance(0.08)) kind = "suv";
            else kind = _rng.Pick(KindPool);

            var d = Dims.TryGetValue(kind, out var dd) ? dd : Dims["sedan"];
            return new PooledRef { Id = _nextId++, Kind = kind, Len = d.Len, Wid = d.Wid };
        }

        public TrafficCar Spawn(double playerS, bool ahead)
        {
            int dir = _rng.Chance(0.42) ? 1 : -1;

            // short-circuit: with one lane the chance() is never evaluated, so it must
            // not be drawn here either or every later draw shifts
            int lane = (_lanes == 2 && _rng.Chance(0.45)) ? 1 : 0;

            double s = playerS + (ahead
                ? (dir == -1 ? _rng.Rand(Window * 0.55, Window) : _rng.Rand(50, Window))
                : -_rng.Rand(45, Window));

            if (s < 12 || s > _route.Length - 55) return null;
            if (s > _spot.ZoneS - 20) return null;   // keep clear of the destination

            for (int i = 0; i < Cars.Count; i++)
            {
                var c = Cars[i];
                if (c.Dir == dir && c.Lane == lane && Math.Abs(c.S - s) < 18) return null;
            }

            var refs = MakeCar();
            double cruise = _rng.Rand(6.5, 10.5) * (Night ? 0.9 : 1.0);

            var car = new TrafficCar
            {
                Id = refs.Id, Kind = refs.Kind, Len = refs.Len, Wid = refs.Wid,
                S = s,
                T = dir * (RoadGeom.LaneW * 0.5 + lane * RoadGeom.LaneW),
                Dir = dir, Lane = lane,
                V = cruise * 0.8, Cruise = cruise,
                HonkCd = _rng.Rand(1, 3),
            };
            PosFrom(car);
            car.Px = car.X; car.Py = car.Y; car.Ph = car.H;
            Cars.Add(car);
            return car;
        }

        void PosFrom(TrafficCar car)
        {
            var p = _route.SampleAt(car.S);
            double rx = -Math.Sin(p.H), ry = Math.Cos(p.H);
            car.X = p.X + rx * car.T;
            car.Y = p.Y + ry * car.T;
            car.H = car.Dir == 1 ? p.H : p.H + Math.PI;
        }

        void Integrate(TrafficCar car, double dt)
        {
            car.S += car.Dir * car.V * dt;
            PosFrom(car);
        }

        public void Update(double dt, MissionRun run)
        {
            foreach (var l in Lights) l.Tick(dt);

            var pProj = run.Proj;

            // maintain population — chance(0.7) is the spawn argument and is only drawn
            // when the spawn actually happens
            int want = TargetCount();
            if (Cars.Count < want && _rng.Chance(0.18))
                Spawn(pProj.S, _rng.Chance(0.7));

            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                var car = Cars[i];
                car.Px = car.X; car.Py = car.Y; car.Ph = car.H;

                double rel = car.S - pProj.S;
                if (Math.Abs(rel) > Window + 40 || car.S < 8 || car.S > _route.Length - 45 ||
                    (car.S > _spot.ZoneS - 8 && car.Dir == 1))
                {
                    Recycle(car);
                    Cars.RemoveAt(i);
                    continue;
                }

                if (car.HitT > 0)   // pulled over after being hit
                {
                    car.HitT -= dt;
                    car.V = Math.Max(0, car.V - 8 * dt);
                    car.HonkCd -= dt;
                    if (car.HonkCd <= 0 && Math.Abs(rel) < 40)
                        car.HonkCd = _rng.Rand(1.5, 3);
                    Integrate(car, dt);
                    continue;
                }

                if (car.PanicT > 0)   // fleeing the tank or the UFO
                {
                    car.PanicT -= dt;
                    car.V = Math.Min(13, car.V + 8 * dt);
                    Integrate(car, dt);
                    continue;
                }

                double target = car.Cruise;
                double gap = 999.0;

                // leader gap within the same direction and lane
                for (int k = 0; k < Cars.Count; k++)
                {
                    var o = Cars[k];
                    if (ReferenceEquals(o, car) || o.Dir != car.Dir || o.Lane != car.Lane) continue;
                    double ds0 = (o.S - car.S) * car.Dir;
                    if (ds0 > 0 && ds0 < gap) gap = ds0 - o.Len / 2 - car.Len / 2;
                }

                // the player as an obstacle
                if (Math.Abs(pProj.T - car.T) < 1.9)
                {
                    double ds = (pProj.S - car.S) * car.Dir;
                    if (ds > 0 && ds - 2 < gap)
                    {
                        gap = ds - run.Veh.Len / 2 - car.Len / 2;
                        if (gap < 9 && run.Car.SpeedAbs < 1.5 && car.V < 1)
                        {
                            car.BlockT += dt;
                            car.HonkCd -= dt;
                            if (car.BlockT > 2 && car.HonkCd <= 0)
                            {
                                car.HonkCd = _rng.Rand(2, 4.5);
                                run.Shame.TrafficHonk(run.Phase);
                                run.NotifyHonk(car);
                            }
                        }
                        else car.BlockT = 0;
                    }
                }

                // red light for this car's direction
                for (int k = 0; k < _route.Inters.Count; k++)
                {
                    var ctrl = LightFor(k);
                    if (ctrl == null) continue;
                    var inter = _route.Inters[k];
                    double stopS = car.Dir == 1 ? inter.S0 - 3 : inter.S1 + 3;
                    double ds = (stopS - car.S) * car.Dir;
                    if (ds > 0 && ds < 26 && ctrl.State == 2)
                        gap = Math.Min(gap, ds - car.Len / 2);
                }

                // pedestrians who have wandered into the road
                if (run.Peds != null)
                {
                    var list = run.Peds.List;
                    for (int k = 0; k < list.Count; k++)
                    {
                        var ped = list[k];
                        if (!ped.OnRoad) continue;
                        if (MathX.Dist2(car.X, car.Y, ped.X, ped.Y) < 15 * 15)
                        {
                            double ds = ((ped.HasProj ? ped.ProjS : car.S) - car.S) * car.Dir;
                            if (ds > 0 && ds < 14) gap = Math.Min(gap, ds - 1.5);
                        }
                    }
                }

                if (gap < 3) target = 0;
                else if (gap < 8) target = Math.Min(target, (gap - 3) * 0.9);
                else if (gap < 16) target = Math.Min(target, car.Cruise * 0.6 + (gap - 8));

                if (car.V < target) car.V = Math.Min(target, car.V + 3.2 * dt);
                else car.V = Math.Max(target, car.V - 7.5 * dt);

                Integrate(car, dt);
            }

            UpdateCrossers(dt, run);
        }

        void Recycle(TrafficCar car)
            => _pool.Push(new PooledRef { Id = car.Id, Kind = car.Kind, Len = car.Len, Wid = car.Wid });

        void UpdateCrossers(double dt, MissionRun run)
        {
            var pd = run.Proj;

            for (int k = 0; k < _route.Inters.Count; k++)
            {
                var ctrl = LightFor(k);
                if (ctrl == null) continue;
                var inter = _route.Inters[k];
                var list = Crossers[k];

                bool crossGreen = ctrl.State == 2;

                // NOTE the evaluation order: chance() is drawn BEFORE the distance test,
                // so a far-away intersection still consumes a draw
                if (crossGreen && list.Count < 2 && _rng.Chance(0.012) &&
                    Math.Abs((inter.S0 + inter.S1) / 2.0 - pd.S) < 170)
                {
                    var refs = MakeCar();
                    int side = _rng.Chance(0.5) ? 1 : -1;
                    list.Add(new CrossCar
                    {
                        Id = refs.Id, Kind = refs.Kind, Len = refs.Len, Wid = refs.Wid,
                        U = -46 * side, Dir = side, V = _rng.Rand(7, 10), Off = side * 1.9,
                        InterIdx = k,
                    });
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var cr = list[i];
                    double iMid = (inter.S0 + inter.S1) / 2.0;
                    bool playerInBox = Math.Abs(pd.S - iMid) < (inter.S1 - inter.S0) / 2.0 + 2.5 &&
                                       Math.Abs(pd.T) < RoadGeom.HalfWidth(_lanes);
                    double distToCenter = -cr.U * cr.Dir;

                    bool brake = playerInBox && distToCenter > 2 && distToCenter < 18;
                    if (!crossGreen && distToCenter > 8) brake = true;

                    if (brake) cr.V = Math.Max(0, cr.V - 9 * dt);
                    else cr.V = Math.Min(9.5, cr.V + 4 * dt);

                    cr.U += cr.Dir * cr.V * dt;

                    if (Math.Abs(cr.U) > 48)
                    {
                        _pool.Push(new PooledRef { Id = cr.Id, Kind = cr.Kind, Len = cr.Len, Wid = cr.Wid });
                        list.RemoveAt(i);
                        continue;
                    }

                    double crossH = inter.H + Math.PI / 2.0;
                    cr.X = inter.Cx + Math.Cos(crossH) * cr.U + Math.Cos(inter.H) * cr.Off;
                    cr.Y = inter.Cy + Math.Sin(crossH) * cr.U + Math.Sin(inter.H) * cr.Off;
                    cr.H = cr.Dir == 1 ? crossH : crossH + Math.PI;
                }
            }
        }

        /// <summary>Called when the player hits a traffic car: it pulls over and leans on the horn.</summary>
        public void OnHit(TrafficCar car) { car.HitT = 6; car.BlockT = 0; }

        /// <summary>Tank and UFO scatter nearby traffic.</summary>
        public void PanicNear(double x, double y, double r)
        {
            for (int i = 0; i < Cars.Count; i++)
                if (MathX.Dist2(Cars[i].X, Cars[i].Y, x, y) < r * r)
                    Cars[i].PanicT = Math.Max(Cars[i].PanicT, 2.5);
        }
    }
}
