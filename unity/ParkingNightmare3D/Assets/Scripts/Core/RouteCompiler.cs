using System;
using System.Collections.Generic;

namespace PN3D.Core
{
    /// <summary>A sampled point on the compiled centreline.</summary>
    public struct RoutePoint
    {
        public double X, Y;   // metres, physics plane
        public double H;      // heading, radians; forward = (cos h, sin h)
        public double S;      // arc length from the start
        public string Kind;   // "road" | "inter" | "curve"
        public int Seg;       // index of the authoring segment
    }

    public sealed class Intersection
    {
        public double S0, S1;
        public double Cx, Cy;
        public double H;
        public bool Lights;
        public int Idx;
    }

    public sealed class RouteZone
    {
        public double S0, S1;
        public string Kind;
    }

    public sealed class RouteCurve
    {
        public double S;
        public string Dir;   // "L" | "R"
        public double End;
    }

    public struct RouteSample
    {
        public double X, Y, H, S;
        public string Kind;
    }

    /// <summary>Result of projecting a world point onto the centreline.</summary>
    public struct Projection
    {
        public double S;      // distance along the route
        public double T;      // lateral offset; + is right of travel direction
        public double H;      // centreline heading at that point
        public int Idx;       // nearest point index, reusable as the next hint
        public string Kind;
    }

    /// <summary>
    /// An arc-length-parameterised centreline plus the projection everything
    /// downstream depends on: GPS arrow, distance-to-go, off-road detection,
    /// curb-gap measurement, prop placement and traffic spawning.
    /// </summary>
    public sealed class CompiledRoute
    {
        public RoutePoint[] Pts;
        public double Step;
        public double Length;
        public List<Intersection> Inters;
        public List<RouteZone> Zones;
        public List<RouteCurve> Curves;

        /// <summary>Interpolated centreline pose at arc length <paramref name="qs"/>.</summary>
        public RouteSample SampleAt(double qs)
        {
            qs = MathX.Clamp(qs, 0.0, Length - 0.01);
            // samples are near-uniform, so guess the index then walk
            int i = (int)MathX.Clamp(Math.Floor(qs / Step), 0, Pts.Length - 2);
            while (i > 0 && Pts[i].S > qs) i--;
            while (i < Pts.Length - 2 && Pts[i + 1].S <= qs) i++;

            var a = Pts[i];
            var b = Pts[i + 1];
            double f = (qs - a.S) / Math.Max(0.001, b.S - a.S);
            return new RouteSample
            {
                X = MathX.Lerp(a.X, b.X, f),
                Y = MathX.Lerp(a.Y, b.Y, f),
                H = a.H + MathX.AngNorm(b.H - a.H) * f,
                Kind = a.Kind,
                S = qs,
            };
        }

        /// <summary>World position offset <paramref name="t"/> metres right of the centreline.</summary>
        public void PosAt(double s, double t, out double x, out double y, out double h)
        {
            var p = SampleAt(s);
            x = p.X - Math.Sin(p.H) * t;
            y = p.Y + Math.Cos(p.H) * t;
            h = p.H;
        }

        /// <summary>Global projection — scans the whole centreline.</summary>
        public Projection Project(double px, double py) => ProjectImpl(px, py, -1);

        /// <summary>
        /// Hinted projection — searches +/-30 points around <paramref name="hint"/>
        /// (a previous <see cref="Projection.Idx"/>). Falls back to a global search if
        /// the query point turns out to be more than 40 m from the local window.
        /// </summary>
        public Projection Project(double px, double py, int hint) => ProjectImpl(px, py, hint);

        Projection ProjectImpl(double px, double py, int hint)
        {
            bool hinted = hint >= 0;
            int best = -1;
            double bd = double.PositiveInfinity;

            int lo = hinted ? Math.Max(0, hint - 30) : 0;
            int hi = hinted ? Math.Min(Pts.Length - 1, hint + 30) : Pts.Length - 1;
            int stride = hinted ? 1 : 4;

            for (int i = lo; i <= hi; i += stride)
            {
                double d = MathX.Dist2(px, py, Pts[i].X, Pts[i].Y);
                if (d < bd) { bd = d; best = i; }
            }

            if (hinted && bd > 40.0 * 40.0) return ProjectImpl(px, py, -1); // lost — go global

            if (stride > 1)
            {
                int lo2 = Math.Max(0, best - 4), hi2 = Math.Min(Pts.Length - 1, best + 4);
                for (int i = lo2; i <= hi2; i++)
                {
                    double d = MathX.Dist2(px, py, Pts[i].X, Pts[i].Y);
                    if (d < bd) { bd = d; best = i; }
                }
            }

            var p = Pts[best];
            // project onto the local segment direction for an accurate s and t
            double dx = px - p.X, dy = py - p.Y;
            double fx = Math.Cos(p.H), fy = Math.Sin(p.H);
            double along = dx * fx + dy * fy;
            double t = dx * -fy + dy * fx; // right normal is (-sin h, cos h)

            return new Projection { S = p.S + along, T = t, H = p.H, Idx = best, Kind = p.Kind };
        }
    }

    /// <summary>
    /// Compiles the route DSL into an arc-length-parameterised centreline.
    /// Port of <c>compileRoute</c>, src/n3_d.js:16.
    ///
    /// Heading convention: forward = (cos h, sin h), counter-clockwise positive.
    /// To render in Unity: position = (x, elev, y), rotation = Euler(0, 90 - h*Rad2Deg, 0).
    /// </summary>
    public static class RouteCompiler
    {
        public const double Step = 2.0;

        /// <summary>
        /// Compile already-enriched segments. Callers working from
        /// design-spec/data/missions.json must run <see cref="RouteEnricher.Enrich"/>
        /// first — see <see cref="CompileMission"/>.
        /// </summary>
        public static CompiledRoute Compile(IList<RouteSeg> segs)
        {
            var pts = new List<RoutePoint>();
            double x = 0, y = 0, h = 0, s = 0;
            var inters = new List<Intersection>();
            var zones = new List<RouteZone>();
            var curves = new List<RouteCurve>();

            for (int si = 0; si < segs.Count; si++)
            {
                var sg = segs[si];

                if (sg.T == "S" || sg.T == "X")
                {
                    double len = sg.T == "X" ? MathX.Or(sg.W, 26.0) : (sg.Len ?? 0.0);

                    if (sg.T == "X")
                    {
                        inters.Add(new Intersection
                        {
                            S0 = s, S1 = s + len,
                            Cx = x + Math.Cos(h) * len / 2.0,
                            Cy = y + Math.Sin(h) * len / 2.0,
                            H = h,
                            Lights = sg.Lights != false,  // absent means true
                            Idx = inters.Count,
                        });
                    }
                    if (sg.Zone != null)
                        zones.Add(new RouteZone { S0 = s, S1 = s + len, Kind = sg.Zone });

                    int n = Math.Max(1, MathX.JsRoundInt(len / Step));
                    for (int i = 0; i < n; i++)
                    {
                        pts.Add(new RoutePoint
                        {
                            X = x, Y = y, H = h, S = s,
                            Kind = sg.T == "X" ? "inter" : "road",
                            Seg = si,
                        });
                        x += Math.Cos(h) * (len / n);
                        y += Math.Sin(h) * (len / n);
                        s += len / n;
                    }
                }
                else // curve, "L" or "R"
                {
                    double R = MathX.Or(sg.R, 34.0);
                    double ang = MathX.Rad(MathX.Or(sg.A, 90.0));
                    double dir = sg.T == "L" ? -1.0 : 1.0;

                    curves.Add(new RouteCurve { S = s, Dir = sg.T, End = s + R * ang });

                    double arcLen = R * ang;
                    int n = Math.Max(4, MathX.JsRoundInt(arcLen / Step));
                    for (int i = 0; i < n; i++)
                    {
                        pts.Add(new RoutePoint { X = x, Y = y, H = h, S = s, Kind = "curve", Seg = si });
                        double dh = dir * ang / n;
                        // advance along the arc, integrating at the midpoint heading
                        x += Math.Cos(h + dh / 2.0) * (arcLen / n);
                        y += Math.Sin(h + dh / 2.0) * (arcLen / n);
                        h += dh;
                        s += arcLen / n;
                    }
                }
            }

            pts.Add(new RoutePoint { X = x, Y = y, H = h, S = s, Kind = "road", Seg = segs.Count - 1 });

            return new CompiledRoute
            {
                Pts = pts.ToArray(),
                Step = Step,
                Length = s,
                Inters = inters,
                Zones = zones,
                Curves = curves,
            };
        }

        /// <summary>
        /// The path callers should use: enrich then compile, exactly as the web build
        /// does. Mutates <paramref name="mission"/> (enrichment is in-place and
        /// idempotent), so its Par is the real par afterwards.
        /// </summary>
        public static CompiledRoute CompileMission(Mission mission)
        {
            RouteEnricher.Enrich(mission);
            return Compile(mission.Segs);
        }
    }
}
