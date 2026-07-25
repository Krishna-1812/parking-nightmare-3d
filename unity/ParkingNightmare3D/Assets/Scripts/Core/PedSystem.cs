using System;
using System.Collections.Generic;

namespace PN3D.Core
{
    public enum PedState { Walk, Film, Cross, Dive, Soaked }

    public sealed class Ped
    {
        public double S, T;
        public int Side, Dir;
        public double X, Y, Px, Py, Face;
        public PedState State = PedState.Walk;
        public double Speed, Phase, StateT;
        public double Dvx, Dvy;

        public bool Filmed, Scandalized, Soaked;
        public bool OnRoad, Attracted;

        /// <summary>Cached route projection; Idx doubles as the next search hint.</summary>
        public bool HasProj;
        public double ProjS, ProjT;
        public int ProjIdx = -1;

        /// <summary>Emote the world layer should show above them, or null.</summary>
        public string Emote;
    }

    /// <summary>
    /// Pedestrians: walking, filming your humiliation, being drawn across the road by an
    /// ice cream jingle, and diving for cover. Port of <c>class Peds</c>, src/n3_d.js:2212.
    ///
    /// They are never hittable — a pedestrian close to a fast car dives clear, and there
    /// is a hard no-overlap teleport behind it. The cost of a near miss is 12 shame
    /// ("SO CLOSE!!"), not damage. That is the joke and the design (§10).
    /// </summary>
    public sealed class PedSystem
    {
        public readonly List<Ped> List = new List<Ped>();

        readonly CompiledRoute _route;
        readonly double _rw;
        readonly Rng _rng;

        static readonly string[] FilmEmotes = { "🎥", "📱", "😳", "🤳" };

        public PedSystem(CompiledRoute route, double rw, int count, Rng rng)
        {
            _route = route;
            _rw = rw;
            _rng = rng;

            for (int i = 0; i < count; i++)
            {
                // draw order matters and mirrors the reference's object literal exactly
                double s = rng.Rand(25, route.Length - 30);
                int side = rng.Chance(0.5) ? 1 : -1;
                double t = side * (rw + 0.35 + rng.Rand(0.8, RoadGeom.SidewalkW - 0.6));
                route.PosAt(s, t, out double px, out double py, out double ph);

                List.Add(new Ped
                {
                    S = s, T = t, Side = side,
                    Dir = rng.Chance(0.5) ? 1 : -1,
                    X = px, Y = py, Px = px, Py = py, Face = ph,
                    State = PedState.Walk,
                    Speed = rng.Rand(0.7, 1.3),
                    Phase = rng.Rand(0, MathX.Tau),
                });
            }
        }

        public void Update(double dt, MissionRun run)
        {
            double px = run.Car.X, py = run.Car.Y;

            for (int i = 0; i < List.Count; i++)
            {
                var ped = List[i];
                ped.Px = ped.X; ped.Py = ped.Y;
                // reference reads `(state === 'flee' || state === 'dive' ? 14 : 6)`; there
                // is no 'flee' state in the machine, so Dive is the only 14 case
                ped.Phase += dt * (ped.State == PedState.Dive ? 14 : 6) *
                             ((ped.State == PedState.Walk || ped.State == PedState.Cross) ? 1 : 0.4);
                ped.StateT -= dt;

                double dd = MathX.Dist2(px, py, ped.X, ped.Y);
                bool near = dd < 30 * 30;

                // player bearing down on them fast -> dive
                if (dd < 5.5 * 5.5 && run.Car.SpeedAbs > 6)
                {
                    double toX = ped.X - px, toY = ped.Y - py;
                    double dot = run.Car.Vx * toX + run.Car.Vy * toY;
                    if (dot > 0 && ped.State != PedState.Dive) Dive(ped, run, px, py, false);
                }

                // hard no-overlap guarantee
                if (dd < 1.6 * 1.6) Dive(ped, run, px, py, true);

                // ice cream jingle attraction
                if (run.JingleOn && near && ped.State == PedState.Walk && _rng.Chance(0.006))
                {
                    ped.State = PedState.Cross;
                    ped.Attracted = true;
                    ped.Emote = "🍦";
                }

                switch (ped.State)
                {
                    case PedState.Walk:
                    {
                        ped.S += ped.Dir * ped.Speed * dt;
                        if (ped.S < 15 || ped.S > _route.Length - 18) ped.Dir *= -1;
                        _route.PosAt(ped.S, ped.T, out double wx, out double wy, out double wh);
                        ped.X = wx; ped.Y = wy;
                        ped.Face = wh + (ped.Dir == 1 ? 0 : Math.PI);
                        ped.OnRoad = false;

                        // notice a recently shameful player and start filming
                        if (near && run.Shame.RecentShameT > 0 && _rng.Chance(0.03))
                        {
                            ped.State = PedState.Film;
                            ped.StateT = _rng.Rand(2, 4);
                            ped.Emote = _rng.Pick(FilmEmotes);
                            if (!ped.Filmed) { ped.Filmed = true; run.OnPedFilmed(ped); }
                        }
                        break;
                    }

                    case PedState.Film:
                    {
                        ped.Face = Math.Atan2(py - ped.Y, px - ped.X);
                        if (ped.StateT <= 0) { ped.State = PedState.Walk; ped.Emote = null; }
                        break;
                    }

                    case PedState.Cross:
                    {
                        double ang = Math.Atan2(py - ped.Y, px - ped.X);
                        ped.Face = ang;
                        ped.X += Math.Cos(ang) * 1.9 * dt;
                        ped.Y += Math.Sin(ang) * 1.9 * dt;
                        Reproject(ped);
                        ped.OnRoad = Math.Abs(ped.ProjT) < _rw;
                        if (dd < 4.5 * 4.5 || !run.JingleOn)
                        {
                            ped.State = PedState.Walk;
                            ped.Attracted = false;
                            ped.OnRoad = false;
                            ped.Emote = null;
                            ped.T = ped.Side * (_rw + 0.35 + 1.4);
                            ped.S = MathX.Clamp(ped.ProjS, 16, _route.Length - 20);
                        }
                        break;
                    }

                    case PedState.Dive:
                    {
                        ped.X += ped.Dvx * dt; ped.Y += ped.Dvy * dt;
                        ped.Dvx *= (1 - 3 * dt); ped.Dvy *= (1 - 3 * dt);
                        if (ped.StateT <= 0)
                        {
                            ped.State = PedState.Film;
                            ped.StateT = _rng.Rand(2.5, 4);
                            ped.Emote = "😤";
                            Reproject(ped);
                            ped.S = ped.ProjS;
                            ped.OnRoad = false;
                        }
                        break;
                    }

                    case PedState.Soaked:
                    {
                        ped.Face = Math.Atan2(py - ped.Y, px - ped.X);
                        if (ped.StateT <= 0) { ped.State = PedState.Walk; ped.Emote = null; }
                        break;
                    }
                }
            }
        }

        void Reproject(Ped ped)
        {
            var pr = ped.ProjIdx >= 0
                ? _route.Project(ped.X, ped.Y, ped.ProjIdx)
                : _route.Project(ped.X, ped.Y);
            ped.ProjS = pr.S; ped.ProjT = pr.T; ped.ProjIdx = pr.Idx;
            ped.HasProj = true;
        }

        public void Dive(Ped ped, MissionRun run, double px, double py, bool hard)
        {
            if (ped.State == PedState.Dive)
            {
                if (hard)
                {
                    // still overlapping — teleport clear rather than allow a hit
                    double a = Math.Atan2(ped.Y - py, ped.X - px);
                    ped.X = px + Math.Cos(a) * 3.4;
                    ped.Y = py + Math.Sin(a) * 3.4;
                }
                return;
            }

            double ang = Math.Atan2(ped.Y - py, ped.X - px) + _rng.Rand(-0.4, 0.4);
            ped.State = PedState.Dive;
            ped.StateT = 0.8;
            ped.Dvx = Math.Cos(ang) * 8;
            ped.Dvy = Math.Sin(ang) * 8;
            ped.Emote = "😱";
            run.OnPedDive(ped);
        }

        /// <summary>Splash a puddle over anyone nearby. Returns how many got soaked.</summary>
        public int Soak(double x, double y, double r, MissionRun run)
        {
            int n = 0;
            for (int i = 0; i < List.Count; i++)
            {
                var ped = List[i];
                if (MathX.Dist2(x, y, ped.X, ped.Y) < r * r && ped.State != PedState.Dive)
                {
                    ped.State = PedState.Soaked;
                    ped.StateT = _rng.Rand(2.5, 4);
                    ped.Soaked = true;
                    ped.Emote = "😡";
                    n++;
                }
            }
            return n;
        }
    }
}
